using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace TM.Services.Framework.AI.Mlm
{
    public sealed class RobertaTinyMlmService : IMicroMlmService, IDisposable
    {
        private const int MaxSequenceLength = 256;
        private const int DefaultVocabSize = 21128;

        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly object _lastUsedLock = new();

        private InferenceSession? _session;
        private BertTokenizer? _tokenizer;
        private int _vocabSize = DefaultVocabSize;

        private string? _inputIdsName;
        private string? _attentionMaskName;
        private string? _tokenTypeIdsName;
        private string? _outputLogitsName;
        private bool _needTokenTypeIds;

        private DateTime _lastUsedUtc = DateTime.MinValue;
        private Timer? _idleTimer;
        private int _idleReleaseMinutes = 10;
        private bool _disposed;

        private int _activeScores;

        public Func<int>? IdleMinutesProvider { get; set; }

        public bool IsModelReady()
        {
            var dir = GetModelDirectory();
            return File.Exists(Path.Combine(dir, "model.onnx"))
                && File.Exists(Path.Combine(dir, "vocab.txt"));
        }

        public void SetIdleReleaseMinutes(int minutes)
        {
            _idleReleaseMinutes = minutes;
        }

        public async Task<float[]> ScoreCandidatesAsync(
            string text,
            int anchorIdx,
            string key,
            IReadOnlyList<string> candidates,
            CancellationToken ct = default)
        {
            if (candidates == null || candidates.Count == 0) return Array.Empty<float>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return new float[candidates.Count];
            if (anchorIdx < 0 || anchorIdx + key.Length > text.Length) return new float[candidates.Count];

            try
            {
                await EnsureLoadedAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TM.App.Log($"[MicroMlm] EnsureLoadedAsync 失败，返回零评分（候选 {candidates.Count} 个）: {ex.Message}");
                return new float[candidates.Count];
            }
            TouchLastUsed();

            Interlocked.Increment(ref _activeScores);
            try
            {
                int n = candidates.Count;
                var scores = new float[n];

                var prefix = text.Substring(0, anchorIdx);
                var suffix = text.Substring(anchorIdx + key.Length);
                var batchTexts = new string[n];
                for (int i = 0; i < n; i++)
                {
                    batchTexts[i] = string.Concat(prefix, candidates[i] ?? string.Empty, suffix);
                }

                var tokenLists = new long[n][];
                int maxLen = 0;
                for (int i = 0; i < n; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var ids = _tokenizer!.EncodeToIds(batchTexts[i], MaxSequenceLength, out _, out _);
                    var arr = new long[ids.Count];
                    for (int j = 0; j < ids.Count; j++) arr[j] = ids[j];
                    tokenLists[i] = arr;
                    if (arr.Length > maxLen) maxLen = arr.Length;
                }
                if (maxLen == 0) return scores;

                var inputIds = new long[n * maxLen];
                var attentionMask = new long[n * maxLen];
                var tokenTypeIds = _needTokenTypeIds ? new long[n * maxLen] : null;
                for (int i = 0; i < n; i++)
                {
                    var toks = tokenLists[i];
                    int offset = i * maxLen;
                    for (int j = 0; j < toks.Length; j++)
                    {
                        inputIds[offset + j] = toks[j];
                        attentionMask[offset + j] = 1L;
                    }
                }

                var onnxInputs = new List<NamedOnnxValue>(3)
                {
                    NamedOnnxValue.CreateFromTensor(_inputIdsName!, new DenseTensor<long>(inputIds, new[] { n, maxLen })),
                    NamedOnnxValue.CreateFromTensor(_attentionMaskName!, new DenseTensor<long>(attentionMask, new[] { n, maxLen })),
                };
                if (_needTokenTypeIds)
                {
                    onnxInputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName!, new DenseTensor<long>(tokenTypeIds!, new[] { n, maxLen })));
                }

                try
                {
                    ScoreBatch(onnxInputs, n, maxLen, anchorIdx, candidates, tokenLists, scores, ct);
                    return scores;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    TM.App.Log($"[MicroMlm] Batch ScoreCandidates 异常（n={n}，全部返回零分）: {ex.Message}");
                    return new float[n];
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeScores);
            }
        }

        private void ScoreBatch(
            List<NamedOnnxValue> onnxInputs,
            int batchSize,
            int seqLen,
            int anchorIdx,
            IReadOnlyList<string> candidates,
            long[][] tokenLists,
            float[] scores,
            CancellationToken ct)
        {
            var session = _session;
            if (session == null)
            {
                TM.App.Log($"[MicroMlm] Session 意外为 null，整个 batch={batchSize} 返回零分（下轮调用会重新 EnsureLoadedAsync）");
                return;
            }

            using var outputs = session.Run(onnxInputs);
            var logits = outputs.First(o => o.Name == _outputLogitsName).AsTensor<float>();

            var dims = logits.Dimensions;
            int actualVocab = dims.Length == 3 ? dims[2] : _vocabSize;
            if (actualVocab != _vocabSize)
            {
                TM.App.Log($"[MicroMlm] 警告：输出 vocab_size={actualVocab} 与期望 {_vocabSize} 不符，按输出为准");
                _vocabSize = actualVocab;
            }

            int candStart = 1 + anchorIdx;

            for (int b = 0; b < batchSize; b++)
            {
                ct.ThrowIfCancellationRequested();

                int candTokens = candidates[b]?.Length ?? 0;
                if (candTokens <= 0) { scores[b] = float.NegativeInfinity; continue; }

                int candEnd = Math.Min(candStart + candTokens, tokenLists[b].Length);
                int actualCandTokens = candEnd - candStart;
                if (actualCandTokens <= 0) { scores[b] = float.NegativeInfinity; continue; }

                float totalLogit = 0f;
                for (int p = 0; p < actualCandTokens; p++)
                {
                    int pos = candStart + p;
                    long targetId = tokenLists[b][pos];
                    totalLogit += logits[b, pos, (int)targetId];
                }

                scores[b] = totalLogit / actualCandTokens;
            }
        }

        public void ReleaseSession()
        {
            _loadLock.Wait();
            try
            {
                ReleaseSessionUnsafe("external request");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _idleTimer?.Dispose(); } catch { }
            try { _loadLock.Wait(); ReleaseSessionUnsafe("dispose"); } catch { } finally { try { _loadLock.Release(); } catch { } }
            _loadLock.Dispose();
        }

        #region 内部

        private async Task EnsureLoadedAsync(CancellationToken ct)
        {
            if (_session != null && _tokenizer != null) return;

            await _loadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_session != null && _tokenizer != null) return;
                if (_disposed) throw new ObjectDisposedException(nameof(RobertaTinyMlmService));

                var dir = GetModelDirectory();
                var modelPath = Path.Combine(dir, "model.onnx");
                var vocabPath = Path.Combine(dir, "vocab.txt");

                if (!File.Exists(modelPath) || !File.Exists(vocabPath))
                {
                    throw new InvalidOperationException(
                        $"MLM 模型文件缺失: {modelPath} 或 {vocabPath}。请先运行 export_onnx.py 生成 chinese-roberta-tiny。");
                }

                TryReadVocabSizeFromConfig();

                var sessionOptions = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = System.Math.Max(1, Environment.ProcessorCount / 2),
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    EnableCpuMemArena = true,
                    EnableMemoryPattern = true,
                };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var session = new InferenceSession(modelPath, sessionOptions);
                DiscoverIoNames(session);

                var tokenizerOptions = new BertOptions
                {
                    LowerCaseBeforeTokenization = false,
                    IndividuallyTokenizeCjk = true,
                    RemoveNonSpacingMarks = false,
                };
                BertTokenizer tokenizer;
                using (var vocabStream = File.OpenRead(vocabPath))
                {
                    tokenizer = BertTokenizer.Create(vocabStream, tokenizerOptions);
                }

                _session = session;
                _tokenizer = tokenizer;
                sw.Stop();

                TM.App.Log($"[MicroMlm] 模型加载完成 vocab={_vocabSize} ioLayout=[{_inputIdsName},{_attentionMaskName},{_tokenTypeIdsName}]→{_outputLogitsName} 耗时 {sw.ElapsedMilliseconds}ms");

                EnsureIdleTimer();
                TouchLastUsed();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private void DiscoverIoNames(InferenceSession session)
        {
            var inputs = session.InputMetadata.Keys.ToList();
            _inputIdsName = MatchName(inputs, "input_ids") ?? inputs.FirstOrDefault(n => n.Contains("input", StringComparison.OrdinalIgnoreCase));
            _attentionMaskName = MatchName(inputs, "attention_mask") ?? inputs.FirstOrDefault(n => n.Contains("mask", StringComparison.OrdinalIgnoreCase));
            _tokenTypeIdsName = MatchName(inputs, "token_type_ids");
            _needTokenTypeIds = _tokenTypeIdsName != null;

            if (_inputIdsName == null || _attentionMaskName == null)
            {
                throw new InvalidOperationException(
                    $"MLM ONNX 输入不完整：{string.Join(",", inputs)}，缺少 input_ids 或 attention_mask");
            }

            var outputs = session.OutputMetadata.Keys.ToList();
            _outputLogitsName = MatchName(outputs, "logits")
                ?? outputs.FirstOrDefault(n => n.Contains("logit", StringComparison.OrdinalIgnoreCase))
                ?? outputs.FirstOrDefault();

            if (_outputLogitsName == null)
            {
                throw new InvalidOperationException("MLM ONNX 模型无任何输出（期望 logits 节点）");
            }
        }

        private static string? MatchName(List<string> names, string exact)
        {
            return names.FirstOrDefault(n => string.Equals(n, exact, StringComparison.OrdinalIgnoreCase));
        }

        private void TryReadVocabSizeFromConfig()
        {
            try
            {
                var asm = typeof(RobertaTinyMlmService).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n =>
                        n.Contains("Mlm.Resources", StringComparison.Ordinal)
                        && n.EndsWith(".config.json", StringComparison.Ordinal));
                if (resourceName == null) return;

                using var stream = asm.GetManifestResourceStream(resourceName)!;
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("vocab_size", out var vs)
                    && vs.TryGetInt32(out var v) && v > 0)
                {
                    _vocabSize = v;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MicroMlm] 读取 MLM config.json 失败（使用默认 {_vocabSize}）: {ex.Message}");
            }
        }

        private static string GetModelDirectory()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "Services", "Framework", "AI", "Mlm",
                "Resources", "chinese-roberta-tiny");
        }

        private void EnsureIdleTimer()
        {
            if (_idleTimer != null) return;
            _idleTimer = new Timer(_ => CheckIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void CheckIdle()
        {
            if (_disposed) return;

            int minutes = IdleMinutesProvider?.Invoke() ?? _idleReleaseMinutes;
            if (minutes <= 0) return;

            if (Interlocked.CompareExchange(ref _activeScores, 0, 0) > 0) return;

            DateTime last;
            lock (_lastUsedLock) { last = _lastUsedUtc; }
            if (last == DateTime.MinValue) return;

            var idle = DateTime.UtcNow - last;
            if (idle.TotalMinutes < minutes) return;

            if (!_loadLock.Wait(0)) return;
            try
            {
                if (Interlocked.CompareExchange(ref _activeScores, 0, 0) > 0) return;
                lock (_lastUsedLock) { last = _lastUsedUtc; }
                if ((DateTime.UtcNow - last).TotalMinutes < minutes) return;
                ReleaseSessionUnsafe($"idle {(int)idle.TotalMinutes}min");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private void TouchLastUsed()
        {
            lock (_lastUsedLock) { _lastUsedUtc = DateTime.UtcNow; }
        }

        private void ReleaseSessionUnsafe(string reason)
        {
            if (_session == null) return;
            try { _session.Dispose(); } catch { }
            _session = null;
            _tokenizer = null;
            _inputIdsName = _attentionMaskName = _tokenTypeIdsName = _outputLogitsName = null;
            _needTokenTypeIds = false;
            TM.App.Log($"[MicroMlm] Session 释放: {reason}");
        }

        #endregion
    }
}
