#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0070

namespace TM.Services.Framework.AI.SemanticKernel
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SKChatService
    {
        static SKChatService()
        {
            TM.Services.Framework.AI.SemanticKernel.Chunk.ExecutionEventChunkAutoBridge.EnsureInitialized();
            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.EnsureInitialized();
        }

        private static readonly Regex ApiVersionPathRegex = new(@"/v\d+(/|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "SKChatService");

        internal static (string Answer, string? Thinking, string Kind) CleanNonStreamContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return (string.Empty, null, "Thinking");

            var strategy = new Conversation.Thinking.Strategies.TagBasedStrategy();
            var chunk = new StreamingChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant, content);
            var pass = strategy.Extract(chunk);
            var tail = strategy.Flush();

            var thinkingSb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(pass.ThinkingContent)) thinkingSb.Append(pass.ThinkingContent);
            if (!string.IsNullOrEmpty(tail.ThinkingContent))
            {
                if (thinkingSb.Length > 0) thinkingSb.Append('\n');
                thinkingSb.Append(tail.ThinkingContent);
            }

            var answerSb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(pass.AnswerContent)) answerSb.Append(pass.AnswerContent);
            if (!string.IsNullOrEmpty(tail.AnswerContent)) answerSb.Append(tail.AnswerContent);

            var thinking = thinkingSb.Length > 0 ? thinkingSb.ToString().Trim() : null;
            var answer = answerSb.ToString().TrimStart('\r', '\n', ' ');
            var kind = pass.ThinkingKind ?? tail.ThinkingKind ?? "Thinking";
            return (answer, string.IsNullOrWhiteSpace(thinking) ? null : thinking, kind);
        }

        internal static string ResolveThinkingKindForDisplay(string? kind, UserConfiguration? config)
        {
            var raw = string.IsNullOrWhiteSpace(kind) ? "Thinking" : kind.Trim();
            if (!string.Equals(raw, "Thinking", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(raw, "Reasoning", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(raw, "Reasoner", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            return config == null ? raw : ModelFamilyClassifier.GetThinkingDisplayKind(config.ModelId, config.ProviderId);
        }

        private static readonly string[] _nonStreamThinkingFieldNames = { "reasoning_content", "thinking_content", "thinking" };
        private static readonly string[] _nonStreamAnswerFieldNames = { "content", "answer", "output_text", "text" };
        private static bool _nonStreamReflectionResolved;
        private static FieldInfo? _completionChoicesField;
        private static FieldInfo? _choiceMessageField;
        private static FieldInfo? _messageRawDataField;

        private static (string Answer, string Thinking, string Source) TryExtractNonStreamExtendedContent(ChatMessageContent? response, string? providerId)
        {
            if (response?.InnerContent is not OpenAI.Chat.ChatCompletion completion)
                return (string.Empty, string.Empty, string.Empty);

            try
            {
                if (!_nonStreamReflectionResolved)
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    _completionChoicesField = completion.GetType().GetField("Choices", flags)
                        ?? FindFieldByNameContains(completion.GetType(), "choices");
                    _nonStreamReflectionResolved = true;
                    LogIfPublicProviderId(providerId, $"[SKChatService] 非流式扩展字段反射解析完成: choices={_completionChoicesField?.Name ?? "null"}");
                }

                if (_completionChoicesField == null)
                    return (string.Empty, string.Empty, string.Empty);

                var choices = _completionChoicesField.GetValue(completion);
                object? firstChoice = null;
                if (choices is System.Collections.IList list && list.Count > 0)
                    firstChoice = list[0];
                else if (choices is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable) { firstChoice = item; break; }
                }
                if (firstChoice == null)
                    return (string.Empty, string.Empty, string.Empty);

                if (_choiceMessageField == null)
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    _choiceMessageField = firstChoice.GetType().GetField("Message", flags)
                        ?? FindFieldByNameContains(firstChoice.GetType(), "message");
                }
                var message = _choiceMessageField?.GetValue(firstChoice);
                if (message == null)
                    return (string.Empty, string.Empty, string.Empty);

                if (_messageRawDataField == null)
                {
                    _messageRawDataField = FindFieldByNameContains(message.GetType(), "serializedAdditionalRawData")
                        ?? FindFieldByNameContains(message.GetType(), "additionalBinaryDataProperties")
                        ?? FindFieldByNameContains(message.GetType(), "additionalRawData")
                        ?? FindFieldByNameContains(message.GetType(), "rawData");
                }
                var rawData = _messageRawDataField?.GetValue(message);
                if (rawData is not IDictionary<string, BinaryData> binaryDict)
                    return (string.Empty, string.Empty, string.Empty);

                foreach (var key in _nonStreamAnswerFieldNames)
                {
                    if (binaryDict.TryGetValue(key, out var binaryValue))
                    {
                        var s = DecodeBinaryDataAsString(binaryValue);
                        if (!string.IsNullOrWhiteSpace(s))
                            return (s, string.Empty, key);
                    }
                }

                foreach (var key in _nonStreamThinkingFieldNames)
                {
                    if (binaryDict.TryGetValue(key, out var binaryValue))
                    {
                        var s = DecodeBinaryDataAsString(binaryValue);
                        if (!string.IsNullOrWhiteSpace(s))
                            return (string.Empty, s, key);
                    }
                }
                return (string.Empty, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                LogIfPublicProviderId(providerId, $"[SKChatService] 非流式扩展字段反射提取异常（非致命）: {ex.Message}");
                return (string.Empty, string.Empty, string.Empty);
            }
        }

        private static string DecodeBinaryDataAsString(BinaryData binaryData)
        {
            var jsonStr = binaryData.ToString();
            if (string.IsNullOrEmpty(jsonStr)) return string.Empty;
            if (jsonStr.Length >= 2 && jsonStr[0] == '"' && jsonStr[^1] == '"')
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr) ?? string.Empty; }
                catch { return jsonStr[1..^1]; }
            }
            return jsonStr;
        }

        private static FieldInfo? FindFieldByNameContains(Type type, string namePart)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (field.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                    return field;
            }
            return null;
        }

        private sealed class KernelBundle : IDisposable
        {
            public Kernel Kernel { get; }
            public IChatCompletionService ChatService { get; }
            public Agents.NovelAgent NovelAgent { get; }
            public HttpClient HttpClient { get; }
            public string ProviderType { get; }
            public string ConfigKey { get; }

            public KernelBundle(
                Kernel kernel,
                IChatCompletionService chatService,
                Agents.NovelAgent novelAgent,
                HttpClient httpClient,
                string providerType,
                string configKey)
            {
                Kernel = kernel;
                ChatService = chatService;
                NovelAgent = novelAgent;
                HttpClient = httpClient;
                ProviderType = providerType;
                ConfigKey = configKey;
            }

            public void Dispose()
            {
                try { HttpClient?.Dispose(); } catch { }
                if (ChatService is IDisposable d) { try { d.Dispose(); } catch { } }
            }
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, KernelBundle> _kernelBundles
            = new(StringComparer.Ordinal);

        private void InvalidateAllBundles()
        {
            List<KernelBundle> toDispose;
            lock (_kernelLock)
            {
                toDispose = _kernelBundles.Values.ToList();
                _kernelBundles.Clear();
                _kernel = null;
                _chatService = null;
                _novelAgent = null;
                _kernelHttpClient = null;
                _lastKernelConfigKey = null;
            }

            if (toDispose.Count > 0)
            {
                _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30))
                    .ContinueWith(_ =>
                    {
                        foreach (var b in toDispose)
                        {
                            try { b.Dispose(); } catch { }
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
            }
        }

        internal const char KernelKeySeparator = '\u001F';

        private void EvictStaleSiblingBundles(string newKey)
        {
            if (string.IsNullOrEmpty(newKey)) return;

            var newSegs = newKey.Split(KernelKeySeparator);
            if (newSegs.Length != 5) return;
            var newProvider = newSegs[0];
            var newModel = newSegs[1];
            var newEndpoint = newSegs[2];
            var newApiKey = newSegs[3];
            var newTimeout = newSegs[4];

            List<KernelBundle>? toDispose = null;
            foreach (var kv in _kernelBundles)
            {
                if (string.Equals(kv.Key, newKey, StringComparison.Ordinal)) continue;
                var segs = kv.Key.Split(KernelKeySeparator);
                if (segs.Length != 5) continue;
                if (!string.Equals(segs[0], newProvider, StringComparison.Ordinal)) continue;
                if (!string.Equals(segs[1], newModel, StringComparison.Ordinal)) continue;
                if (!string.Equals(segs[2], newEndpoint, StringComparison.Ordinal)) continue;
                if (!string.Equals(segs[4], newTimeout, StringComparison.Ordinal)) continue;
                if (string.Equals(segs[3], newApiKey, StringComparison.Ordinal)) continue;

                if (_kernelBundles.TryRemove(kv.Key, out var removed))
                {
                    toDispose ??= new List<KernelBundle>();
                    toDispose.Add(removed);
                }
            }

            if (toDispose != null && toDispose.Count > 0)
            {
                LogIfPublicProviderId(newProvider, $"[SKChatService] 淘汰 {toDispose.Count} 个同前缀旧 bundle (ApiKey 已轮换)");
                var snapshot = toDispose;
                _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30))
                    .ContinueWith(_ =>
                    {
                        foreach (var b in snapshot)
                        {
                            try { b.Dispose(); } catch { }
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
            }
        }

        private Kernel? _kernel;
        private IChatCompletionService? _chatService;
        private ChatHistory _chatHistory = new();
        private CancellationTokenSource? _chatCts;
        private CancellationTokenSource? _streamCts;
        private CancellationTokenSource? _businessCts;
        private TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode _currentMode = TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Channel;
        private RunType _currentRunType = RunType.Chat;
        private string[]? _forcedFunctionNames;
        private bool _isSessionCompressed;

        private readonly ChatHistoryCompressionService _compression;
        private readonly IAIUsageStatisticsService _statistics;
        private int _turnIndex;
        private readonly object _kernelLock = new();
        private string? _lastKernelConfigKey;
        private HttpClient? _kernelHttpClient;
        private bool _useDirectKernel = true;
        private DateTime? _directKernelDisabledAt;
        private static readonly TimeSpan DirectKernelRetryAfter = TimeSpan.FromMinutes(30);
        private bool _skipThinkingInjection;
        private Agents.NovelAgent? _novelAgent;

        private string _currentProviderType = "TagBased";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _streamingUnsupportedEndpoints = new();
        private static readonly TimeSpan StreamingMarkExpiry = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan StreamNetworkFallbackWindow = TimeSpan.FromMinutes(2);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Compatible, DateTime Time)> _streamToolsCache = new();
        private static readonly TimeSpan StreamToolsCacheExpiry = TimeSpan.FromHours(24);
        private static readonly TimeSpan FirstChunkTimeout = TimeSpan.FromSeconds(15);

        private string GetStreamToolsKey(UserConfiguration? cfg = null)
        {
            cfg ??= AI.GetActiveConfiguration();
            return cfg == null ? "" : $"{cfg.ProviderId}|{cfg.CustomEndpoint}|tools";
        }

        private static bool? GetStreamToolsCompatibility(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_streamToolsCache.TryGetValue(key, out var entry)) return null;
            if (!entry.Compatible && DateTime.UtcNow - entry.Time > StreamToolsCacheExpiry)
            {
                _streamToolsCache.TryRemove(key, out _);
                return null;
            }
            return entry.Compatible;
        }

        private static void CacheStreamToolsResult(string key, bool compatible)
        {
            if (string.IsNullOrEmpty(key)) return;
            _streamToolsCache[key] = (compatible, DateTime.UtcNow);
            LogIfPublicCacheKey(key, $"[SKChatService] stream+tools 缓存写入: {MaskCacheKeyForLog(key)} = {(compatible ? "兼容" : "不兼容")}");
        }

        private static bool IsToolsUnsupportedError(Exception ex)
        {
            var msg = (ex.Message ?? string.Empty).ToLowerInvariant();
            var inner = (ex.InnerException?.Message ?? string.Empty).ToLowerInvariant();
            var combined = msg + " " + inner;

            if (combined.Contains("tool") || combined.Contains("function"))
            {
                if (combined.Contains("not supported") || combined.Contains("unsupported")
                    || combined.Contains("not available") || combined.Contains("unknown")
                    || combined.Contains("invalid") || combined.Contains("400")
                    || combined.Contains("422") || combined.Contains("not allowed"))
                    return true;
            }
            if (combined.Contains("stream") && (combined.Contains("not supported") || combined.Contains("unsupported")))
                return true;

            return false;
        }

        private const int RateLimitBackoffBaseSeconds = 30;
        private const int RateLimitBackoffMaxSeconds = 60;
        private const int ServerErrorBackoffBaseSeconds = 2;
        private const int ServerErrorBackoffMaxSeconds = 10;

        private static TimeSpan GetExponentialBackoff(int attempt, int baseSeconds, int maxSeconds)
        {
            if (attempt < 0) attempt = 0;
            if (baseSeconds <= 0) baseSeconds = 1;
            if (maxSeconds <= 0) maxSeconds = baseSeconds;

            var factor = 1 << Math.Min(attempt, 10);
            var seconds = baseSeconds * factor;
            if (seconds > maxSeconds) seconds = maxSeconds;
            return TimeSpan.FromSeconds(seconds);
        }

        private string GetConfigSummary(UserConfiguration? cfg = null)
        {
            try
            {
                cfg ??= AI.GetActiveConfiguration();
                if (cfg == null) return "Provider=?, Model=?, BaseUrl=?";
                if (IsTianmingPrivateProvider(cfg.ProviderId)) return $"Provider={TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel}, Model={TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel}, BaseUrl={TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel}";
                return $"Provider={cfg.ProviderId}, Model={cfg.ModelId}, BaseUrl={cfg.CustomEndpoint}";
            }
            catch
            {
                return "Provider=?, Model=?, BaseUrl=?";
            }
        }

        private string GetEndpointKey(UserConfiguration? cfg = null)
        {
            cfg ??= AI.GetActiveConfiguration();
            return cfg == null ? "" : $"{cfg.ProviderId}|{cfg.CustomEndpoint}";
        }

        private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(90);

        private static readonly TimeSpan ToolExecutionIdleTimeout = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan StreamMaxDuration = TimeSpan.FromMinutes(30);

        public readonly record struct AdaptiveResult(
            string Content,
            int InputTokens,
            int OutputTokens,
            int FirstTokenMs = 0,
            double TokensPerSecond = 0)
        {
            public static AdaptiveResult Error(string content) => new AdaptiveResult(content, 0, 0);
        }

        private async Task<AdaptiveResult> AdaptiveGenerateAsync(
            ChatHistory history,
            PromptExecutionSettings settings,
            IProgress<string>? progress,
            CancellationToken ct,
            UserConfiguration? config = null,
            KernelBundle? preBuiltBundle = null)
        {
            var bundle = preBuiltBundle ?? EnsureKernelInitialized(config);
            if (bundle == null)
                return AdaptiveResult.Error("[错误] AI 服务未配置");

            var endpointKey = GetEndpointKey(config);
            var useStreaming = true;
            var standardModeReported = false;
            if (_streamingUnsupportedEndpoints.TryGetValue(endpointKey, out var markedTime))
            {
                var elapsed = DateTime.UtcNow - markedTime;
                if (elapsed > StreamingMarkExpiry)
                {
                    _streamingUnsupportedEndpoints.TryRemove(endpointKey, out _);
                    LogIfPublic(config, $"[SKChatService] 流式标记已过期({elapsed.TotalMinutes:F1}分钟)，恢复流式尝试: {MaskCacheKeyForLog(endpointKey)}");
                }
                else
                {
                    var remaining = StreamingMarkExpiry - elapsed;
                    LogIfPublic(config, $"[SKChatService] 跳过流式（标记剩余{remaining.TotalSeconds:F0}秒），直接使用标准模式: {MaskCacheKeyForLog(endpointKey)}");
                    GenerationProgressHub.Report($"检测到流式异常标记（剩余{remaining.TotalSeconds:F0}秒），直接使用标准模式...");
                    useStreaming = false;
                }
            }

            if (useStreaming)
            {
                int totalChunksReceived = 0;
                int chunks = 0;
                using var absoluteCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                absoluteCts.CancelAfter(StreamMaxDuration);
                var adaptiveSw = System.Diagnostics.Stopwatch.StartNew();
                int adaptiveFirstTokenMs = 0;
                try
                {
                    var sb = new System.Text.StringBuilder();
                    bool thinkingReported = false;
                    bool receivingReported = false;
                    var router = new ThinkingRouter(bundle.ProviderType);
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(absoluteCts.Token);
                    idleCts.CancelAfter(StreamIdleTimeout);
                    string? adaptiveStreamFinishReason = null;
                    int streamInTokens = 0;
                    int streamOutTokens = 0;
                    progress?.Report("已发送流式请求，等待模型响应...");

                    await foreach (var chunk in bundle.ChatService.GetStreamingChatMessageContentsAsync(
                        history, settings, bundle.Kernel, idleCts.Token).ConfigureAwait(false))
                    {
                        idleCts.CancelAfter(StreamIdleTimeout);
                        totalChunksReceived++;

                        if (chunk.Metadata?.TryGetValue("FinishReason", out var chunkFr) == true && chunkFr != null)
                        {
                            var frStr = chunkFr.ToString();
                            if (!string.IsNullOrEmpty(frStr))
                                adaptiveStreamFinishReason = frStr;
                        }

                        if (chunk.Metadata?.TryGetValue("Usage", out var usageObj) == true
                            && usageObj is System.Collections.Generic.IDictionary<string, int> usageDict)
                        {
                            if (usageDict.TryGetValue("InputTokens", out var inT) && inT > streamInTokens) streamInTokens = inT;
                            if (usageDict.TryGetValue("OutputTokens", out var outT) && outT > streamOutTokens) streamOutTokens = outT;
                        }

                        if (chunk.InnerContent is OpenAI.Chat.StreamingChatCompletionUpdate openAiUpdate
                            && openAiUpdate.Usage != null)
                        {
                            if (openAiUpdate.Usage.InputTokenCount > 0) streamInTokens = openAiUpdate.Usage.InputTokenCount;
                            if (openAiUpdate.Usage.OutputTokenCount > 0) streamOutTokens = openAiUpdate.Usage.OutputTokenCount;
                        }

                        var routed = router.Route(chunk);
                        if (!string.IsNullOrEmpty(routed.AnswerContent))
                        {
                            sb.Append(routed.AnswerContent);
                            chunks++;
                            if (adaptiveFirstTokenMs == 0)
                                adaptiveFirstTokenMs = (int)adaptiveSw.ElapsedMilliseconds;
                            if (!receivingReported)
                            {
                                receivingReported = true;
                                progress?.Report("开始接收正文内容...");
                                GenerationProgressHub.ReportPhase(ProgressPhase.Drafting, "正文生成中...");
                            }
                        }
                        else if (!string.IsNullOrEmpty(routed.ThinkingContent))
                        {
                            if (!thinkingReported)
                            {
                                thinkingReported = true;
                                progress?.Report("模型思考中...");
                            }
                        }
                    }
                    var flushed = router.Flush();
                    if (!string.IsNullOrEmpty(flushed.AnswerContent))
                    {
                        sb.Append(flushed.AnswerContent);
                        chunks++;
                        if (adaptiveFirstTokenMs == 0)
                            adaptiveFirstTokenMs = (int)adaptiveSw.ElapsedMilliseconds;
                    }
                    ChatModeSettings.SyncLastFinishReason(adaptiveStreamFinishReason, config ?? AI.GetActiveConfiguration());
                    progress?.Report($"接收完成，共 {sb.Length} 字符");
                    if (InfoLogDedup.ShouldLog("SK:StreamDone")) LogIfPublic(config, $"[SKChatService] 流式生成完成: {sb.Length} 字符, {chunks} 块, in/out={streamInTokens}/{streamOutTokens}");
                    try
                    {
                        var cfg = config ?? AI.GetActiveConfiguration();
                        if (cfg != null)
                            ChatModeSettings.RecordSuccessObservation(cfg, history, settings, sb.ToString());
                    }
                    catch (Exception ex) { DebugLogOnce("RecordSuccessObs-Stream", ex); }
                    double adaptiveTps = 0;
                    adaptiveSw.Stop();
                    if (streamOutTokens > 0 && adaptiveFirstTokenMs > 0 && adaptiveSw.ElapsedMilliseconds > adaptiveFirstTokenMs)
                    {
                        var generateMs = adaptiveSw.ElapsedMilliseconds - adaptiveFirstTokenMs;
                        if (generateMs > 0)
                            adaptiveTps = streamOutTokens / (generateMs / 1000.0);
                    }

                    if (sb.Length == 0 && chunks == 0 && streamOutTokens == 0)
                    {
                        LogIfPublic(config, "[SKChatService] 流式响应为空（0 字符/0 块/0 tokens），疑似服务端异常响应，降级非流式重试（不标记端点）");
                        GenerationProgressHub.Report("流式响应为空，切换标准模式重试...");
                    }
                    else
                    {
                        return new AdaptiveResult(sb.ToString(), streamInTokens, streamOutTokens, adaptiveFirstTokenMs, adaptiveTps);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (totalChunksReceived == 0)
                    {
                        LogIfPublic(config, "[SKChatService] 流式空闲超时且无任何chunk，疑似假流，降级非流式");
                        if (!string.IsNullOrEmpty(endpointKey))
                            _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow;
                        GenerationProgressHub.Report("流式无响应，切换标准模式重试...");
                    }
                    else if (absoluteCts.IsCancellationRequested)
                    {
                        if (chunks == 0)
                        {
                            LogIfPublic(config, $"[SKChatService] 流式超过最大时长({(int)StreamMaxDuration.TotalMinutes}分钟)，仅thinking无正文，降级非流式重试（chunks={totalChunksReceived}）");
                            if (!string.IsNullOrEmpty(endpointKey))
                                _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow - (StreamingMarkExpiry - StreamNetworkFallbackWindow);
                            GenerationProgressHub.Report("流式思考超时，切换标准模式重试...");
                            GlobalToast.Warning("流式超时", $"流式超过{(int)StreamMaxDuration.TotalMinutes}分钟，降级非流式重试");
                        }
                        else
                        {
                            LogIfPublic(config, $"[SKChatService] 流式超过最大时长({(int)StreamMaxDuration.TotalMinutes}分钟)，已收到 {totalChunksReceived} chunks（含thinking），强制终止");
                            GenerationProgressHub.Report($"响应超时（超过{(int)StreamMaxDuration.TotalMinutes}分钟）");
                            GlobalToast.Error("响应超时", $"模型思考超过{(int)StreamMaxDuration.TotalMinutes}分钟仍未完成，已强制终止");
                            return AdaptiveResult.Error($"[错误] 响应超时：模型思考超过{(int)StreamMaxDuration.TotalMinutes}分钟仍未完成");
                        }
                    }
                    else
                    {
                        LogIfPublic(config, $"[SKChatService] 流式空闲超时（90s无数据），已接收 {totalChunksReceived} chunks");
                        GenerationProgressHub.Report("响应超时（90秒无数据）");
                        GlobalToast.Warning("流式空闲超时", "服务器超过90秒未返回新数据，请稍后重试或更换模型");
                        return AdaptiveResult.Error("[错误] 响应超时：服务器超过90秒未返回数据");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (IsStreamNetworkError(ex))
                {
                    LogIfPublic(config, $"[SKChatService] 流式网络断连（非端点问题）: {ex.Message}，已收到 {totalChunksReceived} 块");
                    if (totalChunksReceived == 0)
                    {
                        if (!string.IsNullOrEmpty(endpointKey))
                            _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow - (StreamingMarkExpiry - StreamNetworkFallbackWindow);
                        GenerationProgressHub.Report("流式连接断连，切换标准模式重试...");
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    var (useResult, _) = ClassifyException(ex);

                    bool isKeyLevelOrRequestLevel =
                        useResult == TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.RateLimited
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.ModelNotFound
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.ContentFiltered
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.InvalidRequest;

                    if (isKeyLevelOrRequestLevel)
                    {
                        LogIfPublic(config, $"[SKChatService] 流式异常属 Key/请求级({useResult})，抛出由上层处理，不标记端点: {ex.Message}");
                        throw;
                    }

                    bool isGenuineStreamUnsupported =
                        useResult == TM.Services.Framework.AI.Core.KeyUseResult.StreamNotSupported
                        || ex is NotSupportedException;

                    if (isGenuineStreamUnsupported)
                    {
                        LogIfPublic(config, $"[SKChatService] 端点不支持流式，降级非流式（30分钟标记）: {ex.Message}");
                        if (!string.IsNullOrEmpty(endpointKey))
                            _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow;
                        GenerationProgressHub.Report("端点不支持流式，使用标准模式...");
                    }
                    else
                    {
                        LogIfPublic(config, $"[SKChatService] 流式瞬态异常({useResult})，短暂降级非流式（2分钟）: [{ex.GetType().Name}] {ex.Message}");
                        if (!string.IsNullOrEmpty(endpointKey))
                            _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow - (StreamingMarkExpiry - StreamNetworkFallbackWindow);
                        GenerationProgressHub.Report("流式异常，切换标准模式重试...");
                    }
                }
            }
            else
            {
                standardModeReported = true;
                GenerationProgressHub.Report("使用标准模式，等待模型响应...");
            }

            using var nonStreamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var effectiveCfg = config ?? AI.GetActiveConfiguration();
            var configuredTimeout = effectiveCfg?.TimeoutSeconds ?? 0;
            int budgetSec;
            if (configuredTimeout <= 0)
            {
                budgetSec = 120;
            }
            else if (effectiveCfg?.SupportsThinking == true)
            {
                budgetSec = Math.Max(configuredTimeout * 4, 120);
            }
            else
            {
                budgetSec = configuredTimeout;
            }
            var nonStreamBudget = TimeSpan.FromSeconds(budgetSec);
            nonStreamCts.CancelAfter(nonStreamBudget);
            var nonStreamToken = nonStreamCts.Token;
            if (!standardModeReported)
                GenerationProgressHub.Report("已进入标准模式，等待模型响应...");
            ChatMessageContent response;
            var nonStreamSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                response = await bundle.ChatService.GetChatMessageContentAsync(history, settings, bundle.Kernel, nonStreamToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                nonStreamSw.Stop();
                if (!string.IsNullOrEmpty(endpointKey)
                    && _streamingUnsupportedEndpoints.TryGetValue(endpointKey, out var nonStreamMarkedTime))
                {
                    var elapsed = DateTime.UtcNow - nonStreamMarkedTime;
                    var remaining = StreamingMarkExpiry - elapsed;
                    if (remaining <= StreamNetworkFallbackWindow + TimeSpan.FromSeconds(5))
                        _streamingUnsupportedEndpoints.TryRemove(endpointKey, out _);
                }
                var elapsedSec = Math.Max(1, (int)Math.Round(nonStreamSw.Elapsed.TotalSeconds));
                LogIfPublic(config, $"[SKChatService] 非流式降级超时（{elapsedSec}秒），已检查临时流式标记并返回错误: {MaskCacheKeyForLog(endpointKey)}");
                GenerationProgressHub.Report("标准模式超时");
                GlobalToast.Error("请求超时", $"标准模式响应超时（{elapsedSec}秒），请检查网络、延长超时配置或更换模型");
                return AdaptiveResult.Error($"[错误] 标准模式响应超时（{elapsedSec}秒），请检查网络、延长 HTTP 超时配置或更换模型");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (!string.IsNullOrEmpty(endpointKey)
                    && _streamingUnsupportedEndpoints.TryGetValue(endpointKey, out var nonStreamMarkedTime))
                {
                    var elapsed = DateTime.UtcNow - nonStreamMarkedTime;
                    var remaining = StreamingMarkExpiry - elapsed;
                    if (remaining <= StreamNetworkFallbackWindow + TimeSpan.FromSeconds(5))
                        _streamingUnsupportedEndpoints.TryRemove(endpointKey, out _);
                }
                LogIfPublic(config, $"[SKChatService] 非流式降级也失败，已检查临时流式标记: [{ex.GetType().Name}] {ex.Message}");
                GenerationProgressHub.Report("标准模式请求失败，准备切换密钥或重试...");
                throw;
            }
            var content = response.Content ?? string.Empty;
            var adaptiveFinishReason = response.Metadata?.TryGetValue("FinishReason", out var afr) == true ? afr?.ToString() : null;
            ChatModeSettings.SyncLastFinishReason(adaptiveFinishReason, config ?? AI.GetActiveConfiguration());

            int nsInTokens = 0;
            int nsOutTokens = 0;
            try
            {
                if (response.Metadata != null
                    && response.Metadata.TryGetValue("Usage", out var uObj)
                    && uObj is System.Collections.Generic.IDictionary<string, int> uDict)
                {
                    uDict.TryGetValue("InputTokens", out nsInTokens);
                    uDict.TryGetValue("OutputTokens", out nsOutTokens);
                }
                if (nsInTokens == 0 && nsOutTokens == 0
                    && response.InnerContent is OpenAI.Chat.ChatCompletion oaiComp && oaiComp.Usage != null)
                {
                    nsInTokens = oaiComp.Usage.InputTokenCount;
                    nsOutTokens = oaiComp.Usage.OutputTokenCount;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(content) && nsOutTokens > 0)
            {
                var activeProviderId = config?.ProviderId ?? AI.GetActiveConfiguration()?.ProviderId;
                var (extractedAnswer, extractedThinking, extractedSource) = TryExtractNonStreamExtendedContent(response, activeProviderId);
                if (!string.IsNullOrWhiteSpace(extractedAnswer))
                {
                    LogIfPublicProviderId(activeProviderId, $"[SKChatService] 非流式 content 为空（out_tokens={nsOutTokens}），从扩展字段 '{extractedSource}' 提取 {extractedAnswer.Length} 字符");
                    content = extractedAnswer;
                }
                else if (!string.IsNullOrWhiteSpace(extractedThinking))
                {
                    LogIfPublicProviderId(activeProviderId, $"[SKChatService] 非流式 content 为空（out_tokens={nsOutTokens}），仅命中思考字段 '{extractedSource}' {extractedThinking.Length} 字符，业务正文不采用");
                }
                else
                {
                    var modelHint = config?.ModelId ?? AI.GetActiveConfiguration()?.ModelId ?? "unknown";
                    if (!IsTianmingPrivateProvider(activeProviderId))
                    {
                        var endpointHint = config?.CustomEndpoint ?? AI.GetActiveConfiguration()?.CustomEndpoint ?? string.Empty;
                        TM.App.Log($"[SKChatService] 非流式 content 为空但 out_tokens={nsOutTokens}，扩展字段均未命中（model={modelHint}, endpoint={endpointHint}），疑似中转站吞掉正文");
                    }
                }
            }

            var (cleanedAdaptive, _, _) = CleanNonStreamContent(content);
            if (!string.IsNullOrWhiteSpace(cleanedAdaptive))
                content = cleanedAdaptive;

            content = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(content);
            progress?.Report($"生成完成，共 {content.Length} 字符");
            try
            {
                var cfg = config ?? AI.GetActiveConfiguration();
                if (cfg != null)
                    ChatModeSettings.RecordSuccessObservation(cfg, history, settings, content);
            }
            catch (Exception ex) { DebugLogOnce("RecordSuccessObs-NonStream", ex); }
            return new AdaptiveResult(content, nsInTokens, nsOutTokens);
        }

        private sealed class DisposableAction : IDisposable
        {
            private Action? _dispose;

            public DisposableAction(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }

        private readonly object _lastRunIdLock = new();
        private Guid _lastRunId;

        public Guid LastRunId
        {
            get { lock (_lastRunIdLock) return _lastRunId; }
            private set { lock (_lastRunIdLock) _lastRunId = value; }
        }

        public void SetLastRunId(Guid runId) => LastRunId = runId;

        public IReadOnlyList<SearchResult>? LastToolReferences { get; private set; }

        private readonly object _toolRefsLock = new();

        public void ClearToolReferences()
        {
            lock (_toolRefsLock) { LastToolReferences = null; }
        }

        public void AppendToolReferences(IEnumerable<SearchResult>? items)
        {
            if (items == null) return;
            lock (_toolRefsLock)
            {
                var list = LastToolReferences != null
                    ? new List<SearchResult>(LastToolReferences)
                    : new List<SearchResult>();
                var existingIds = new HashSet<string>(list.Select(r => r.ChapterId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    if (item == null) continue;
                    var id = item.ChapterId ?? string.Empty;
                    if (existingIds.Add(id))
                        list.Add(item);
                }
                LastToolReferences = list;
            }
        }

        public IReadOnlyList<SearchResult>? GetLastToolReferences()
        {
            var refs = LastToolReferences;
            return (refs == null || refs.Count == 0) ? null : refs;
        }

        public string LastThinkingContent { get; private set; } = string.Empty;
        public string LastThinkingKind { get; private set; } = "Thinking";

        private readonly AIService _ai;
        private readonly ProxyService _proxy;
        private readonly SessionManager _sessions;

        private static readonly ResiliencePipeline _streamingPipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.8,
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                    ex is not OperationCanceledException and not TimeoutRejectedException
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden }
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.PaymentRequired }
                    and not System.Net.Http.HttpRequestException { StatusCode: (System.Net.HttpStatusCode)429 })
            })
            .Build();

        private AIService AI => _ai;
        private ProxyService Proxy => _proxy;

        public SessionManager Sessions => _sessions;

        public bool IsSessionCompressed => _isSessionCompressed;

        public bool IsMainConversationGenerating =>
            (_chatCts != null && !_chatCts.IsCancellationRequested)
            || (_streamCts != null && !_streamCts.IsCancellationRequested);

        private Action? _cancelWorkspaceBatchAction;

        public bool IsWorkspaceBatchGenerating => _cancelWorkspaceBatchAction != null;

        public void RegisterWorkspaceBatch(Action cancelAction) => _cancelWorkspaceBatchAction = cancelAction;

        public void UnregisterWorkspaceBatch() => _cancelWorkspaceBatchAction = null;

        public void CancelWorkspaceBatch()
        {
            _cancelWorkspaceBatchAction?.Invoke();
            _cancelWorkspaceBatchAction = null;
        }

        private readonly TaskCompletionSource<bool> _initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializedAsync => _initializedTcs.Task;

        public SKChatService(AIService ai, ProxyService proxy, SessionManager sessions)
        {
            _ai = ai;
            _proxy = proxy;
            _sessions = sessions;
            _compression = new ChatHistoryCompressionService(
                (systemPrompt, userPrompt, ct) => GenerateOneShotAsync(systemPrompt, userPrompt, ct),
                GetModelContextWindow);
            _statistics = ServiceLocator.Get<IAIUsageStatisticsService>();

            TM.App.Log("[SKChatService] 初始化");

            _proxy.ConfigChanged += (_, _) =>
            {
                try
                {
                    InvalidateAllBundles();
                    TM.App.Log("[SKChatService] 代理配置变更，已失效所有 Kernel Bundle");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 处理代理变更失败: {ex.Message}");
                }
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    var allSessions = Sessions.GetAllSessions();
                    if (allSessions.Count == 0) return;
                    var initialSessionId = allSessions[0].Id;
                    var records = await Sessions.LoadMessagesAsync(initialSessionId).ConfigureAwait(false);
                    _chatHistory = Sessions.SwitchSessionWithRecords(initialSessionId, records);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 初始化会话失败: {ex.Message}");
                }
                finally
                {
                    _initializedTcs.TrySetResult(true);
                }
            });

            _ = Conversation.Mapping.PlanModeMapper.PrewarmContentGuideCacheAsync()
                .ContinueWith(t => { if (t.IsFaulted) TM.App.Log($"[SKChatService] Plan预热失败: {t.Exception?.GetBaseException().Message}"); }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

            TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectChanged += (_, _) =>
            {
                try
                {
                    BeginDraftSession();
                    TM.App.Log("[SKChatService] 项目切换，已清空对话上下文");
                    _ = Conversation.Mapping.PlanModeMapper.PrewarmContentGuideCacheAsync()
                        .ContinueWith(t => { if (t.IsFaulted) TM.App.Log($"[SKChatService] Plan预热失败: {t.Exception?.GetBaseException().Message}"); }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 项目切换清空对话上下文失败: {ex.Message}");
                }
            };
        }

        public RunType CurrentRunType
        {
            get => _currentRunType;
            set => _currentRunType = value;
        }

        public IDisposable UseTransientMode(TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode mode, RunType runType = RunType.Chat, string[]? forcedFunctions = null)
        {
            var oldMode = _currentMode;
            var oldFilter = PlanModeFilter.IsEnabled;
            var oldRunType = _currentRunType;
            var oldForced = _forcedFunctionNames;

            _currentMode = mode;
            _currentRunType = runType;
            _forcedFunctionNames = forcedFunctions;
            PlanModeFilter.IsEnabled = ChatModeSettings.RequiresFunctionConfirmation(mode);

            return new DisposableAction(() =>
            {
                _currentMode = oldMode;
                _currentRunType = oldRunType;
                _forcedFunctionNames = oldForced;
                PlanModeFilter.IsEnabled = oldFilter;
            });
        }

        private static bool IsTianmingPrivateProvider(string? providerId)
            => TM.Services.Framework.AI.Core.TianmingProviderIdentity.IsTianmingPrivate(providerId);

        private static bool IsTianmingPrivateCacheKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var idx = key.IndexOf('|');
            var providerId = idx >= 0 ? key[..idx] : key;
            return IsTianmingPrivateProvider(providerId);
        }

        private static string MaskCacheKeyForLog(string? key)
            => IsTianmingPrivateCacheKey(key) ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : key ?? string.Empty;

        private static void LogIfPublicProviderId(string? providerId, string message)
        {
            if (IsTianmingPrivateProvider(providerId)) return;
            TM.App.Log(message);
        }

        private static void LogIfPublicCacheKey(string? key, string message)
        {
            if (IsTianmingPrivateCacheKey(key)) return;
            TM.App.Log(message);
        }

        private void LogIfPublic(UserConfiguration? cfg, string message)
        {
            var providerId = cfg?.ProviderId ?? AI.GetActiveConfiguration()?.ProviderId;
            LogIfPublicProviderId(providerId, message);
        }
    }
}
