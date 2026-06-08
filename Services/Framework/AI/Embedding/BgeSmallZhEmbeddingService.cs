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
using TM.Framework.Common.Helpers.Numerics;

namespace TM.Services.Framework.AI.Embedding
{
    public sealed class BgeSmallZhEmbeddingService : IMicroEmbeddingService, IDisposable
    {
        private const string QueryPrefixZh = "为这个句子生成表示以用于检索相关文章：";
        private const int MaxSequenceLength = 512;
        private const int DefaultDimension = 512;

        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly object _lastUsedLock = new();

        private InferenceSession? _session;
        private BertTokenizer? _tokenizer;
        private int _dimension = DefaultDimension;

        private string? _inputIdsName;
        private string? _attentionMaskName;
        private string? _tokenTypeIdsName;
        private string? _outputHiddenStateName;
        private bool _needTokenTypeIds;

        private DateTime _lastUsedUtc = DateTime.MinValue;
        private Timer? _idleTimer;
        private int _idleReleaseMinutes = 10;
        private bool _disposed;

        private int _activeEncodes;

        public Func<int>? IdleMinutesProvider { get; set; }

        public int Dimension => _dimension;

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

        public async Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            var batch = await EncodeBatchAsync(new[] { text ?? string.Empty }, mode, ct).ConfigureAwait(false);
            return batch.Length > 0 ? batch[0] : new float[_dimension];
        }

        public async Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            if (texts == null || texts.Count == 0) return Array.Empty<float[]>();

            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            TouchLastUsed();

            Interlocked.Increment(ref _activeEncodes);
            try
            {
                int batchSize = texts.Count;
                var results = new float[batchSize][];

                var tokenLists = new long[batchSize][];
                int maxLen = 0;
                for (int b = 0; b < batchSize; b++)
                {
                    ct.ThrowIfCancellationRequested();
                    var src = texts[b] ?? string.Empty;
                    var input = mode == EmbeddingMode.Query && src.Length > 0 ? QueryPrefixZh + src : src;
                    var ids = _tokenizer!.EncodeToIds(input, MaxSequenceLength, out _, out _);
                    var arr = new long[ids.Count];
                    for (int i = 0; i < ids.Count; i++) arr[i] = ids[i];
                    tokenLists[b] = arr;
                    if (arr.Length > maxLen) maxLen = arr.Length;
                }
                if (maxLen == 0) maxLen = 1;

                var inputIds = new long[batchSize * maxLen];
                var attentionMask = new long[batchSize * maxLen];
                var tokenTypeIds = _needTokenTypeIds ? new long[batchSize * maxLen] : null;
                for (int b = 0; b < batchSize; b++)
                {
                    var toks = tokenLists[b];
                    int offset = b * maxLen;
                    for (int i = 0; i < toks.Length; i++)
                    {
                        inputIds[offset + i] = toks[i];
                        attentionMask[offset + i] = 1L;
                    }
                }

                var onnxInputs = new List<NamedOnnxValue>(3)
                {
                    NamedOnnxValue.CreateFromTensor(_inputIdsName!, new DenseTensor<long>(inputIds, new[] { batchSize, maxLen })),
                    NamedOnnxValue.CreateFromTensor(_attentionMaskName!, new DenseTensor<long>(attentionMask, new[] { batchSize, maxLen })),
                };
                if (_needTokenTypeIds)
                {
                    onnxInputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName!, new DenseTensor<long>(tokenTypeIds!, new[] { batchSize, maxLen })));
                }

                try
                {
                    ExtractClsBatch(onnxInputs, batchSize, results, ct);
                    return results;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    TM.App.Log($"[MicroEmbedding] Batch Encode 异常（batch={batchSize}，全部返回零向量）: {ex.Message}");
                    for (int b = 0; b < batchSize; b++) results[b] = new float[_dimension];
                    return results;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeEncodes);
            }
        }

        private void ExtractClsBatch(List<NamedOnnxValue> onnxInputs, int batchSize, float[][] results, CancellationToken ct)
        {
            var session = _session;
            if (session == null)
            {
                TM.App.Log($"[MicroEmbedding] Session 意外为 null，整个 batch={batchSize} 返回零向量（下轮调用会重新 EnsureLoadedAsync）");
                for (int b = 0; b < batchSize; b++) results[b] = new float[_dimension];
                return;
            }

            using var outputs = session.Run(onnxInputs);
            var hidden = outputs.First(o => o.Name == _outputHiddenStateName).AsTensor<float>();

            var dims = hidden.Dimensions;
            int hiddenSize = dims.Length == 3 ? dims[2] : _dimension;
            if (hiddenSize != _dimension)
            {
                TM.App.Log($"[MicroEmbedding] 警告：输出 hidden={hiddenSize} 与期望 dimension={_dimension} 不符，按输出为准");
                _dimension = hiddenSize;
            }

            for (int b = 0; b < batchSize; b++)
            {
                ct.ThrowIfCancellationRequested();
                var vec = new float[_dimension];
                for (int d = 0; d < _dimension; d++)
                {
                    vec[d] = hidden[b, 0, d];
                }
                VectorMath.L2NormalizeInPlace(vec);
                results[b] = vec;
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
                if (_disposed) throw new ObjectDisposedException(nameof(BgeSmallZhEmbeddingService));

                var dir = GetModelDirectory();
                var modelPath = Path.Combine(dir, "model.onnx");
                var vocabPath = Path.Combine(dir, "vocab.txt");

                if (!File.Exists(modelPath) || !File.Exists(vocabPath))
                {
                    throw new InvalidOperationException(
                        $"嵌入模型文件缺失: {modelPath} 或 {vocabPath}。请先下载 bge-small-zh-v1.5。");
                }

                TryReadDimensionFromConfig();

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

                TM.App.Log($"[MicroEmbedding] 模型加载完成 dim={_dimension} ioLayout=[{_inputIdsName},{_attentionMaskName},{_tokenTypeIdsName}] 耗时 {sw.ElapsedMilliseconds}ms");

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
                    $"ONNX 输入不完整：{string.Join(",", inputs)}，缺少 input_ids 或 attention_mask");
            }

            var outputs = session.OutputMetadata.Keys.ToList();
            _outputHiddenStateName = MatchName(outputs, "last_hidden_state")
                ?? MatchName(outputs, "sentence_embedding")
                ?? outputs.FirstOrDefault();

            if (_outputHiddenStateName == null)
            {
                throw new InvalidOperationException("ONNX 模型无任何输出");
            }
        }

        private static string? MatchName(List<string> names, string exact)
        {
            return names.FirstOrDefault(n => string.Equals(n, exact, StringComparison.OrdinalIgnoreCase));
        }

        private void TryReadDimensionFromConfig()
        {
            try
            {
                var asm = typeof(BgeSmallZhEmbeddingService).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n =>
                        n.Contains("Embedding.Resources", StringComparison.Ordinal)
                        && n.EndsWith(".config.json", StringComparison.Ordinal));
                if (resourceName == null) return;

                using var stream = asm.GetManifestResourceStream(resourceName)!;
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("hidden_size", out var hidden)
                    && hidden.TryGetInt32(out var dim) && dim > 0)
                {
                    _dimension = dim;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MicroEmbedding] 读取嵌入 config.json 维度失败（使用默认 {_dimension}）: {ex.Message}");
            }
        }

        private static string GetModelDirectory()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "Services", "Framework", "AI", "Embedding",
                "Resources", "bge-small-zh-v1.5");
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

            if (Interlocked.CompareExchange(ref _activeEncodes, 0, 0) > 0) return;

            DateTime last;
            lock (_lastUsedLock) { last = _lastUsedUtc; }
            if (last == DateTime.MinValue) return;

            var idle = DateTime.UtcNow - last;
            if (idle.TotalMinutes < minutes) return;

            if (!_loadLock.Wait(0)) return;
            try
            {
                if (Interlocked.CompareExchange(ref _activeEncodes, 0, 0) > 0) return;
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
            _inputIdsName = _attentionMaskName = _tokenTypeIdsName = _outputHiddenStateName = null;
            _needTokenTypeIds = false;
            TM.App.Log($"[MicroEmbedding] Session 释放: {reason}");
        }

        #endregion
    }
}
