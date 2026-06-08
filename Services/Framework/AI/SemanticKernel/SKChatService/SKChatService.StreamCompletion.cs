#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.Middleware;
using TM.Services.Framework.AI.Middleware.Builtins;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Discovery;
using Polly.CircuitBreaker;
using TM.Services.Framework.AI.Monitoring;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        public async Task<string> SendStreamMessageAsync(string displayText, string promptForModel, Action<string> onChunk, CancellationToken cancellationToken = default)
        {
            var parts = new ChatPromptParts
            {
                SystemPrompt = string.Empty,
                UserPrompt = promptForModel
            };

            return await SendStreamMessageAsync(displayText, parts, onChunk, null, cancellationToken).ConfigureAwait(false);
        }

        public Task<string> SendStreamMessageAsync(string displayText, ChatPromptParts promptParts, Action<string> onChunk, CancellationToken cancellationToken = default)
        {
            return SendStreamMessageAsync(displayText, promptParts, onChunk, null, cancellationToken, null);
        }

        public Task<string> SendStreamMessageAsync(string displayText, ChatPromptParts promptParts, Action<string> onChunk, Action<string>? onThinkingChunk, CancellationToken cancellationToken)
        {
            return SendStreamMessageAsync(displayText, promptParts, onChunk, onThinkingChunk, cancellationToken, null);
        }

        public async Task<string> SendStreamMessageAsync(string displayText, ChatPromptParts promptParts, Action<string> onChunk, Action<string>? onThinkingChunk, CancellationToken cancellationToken, Action<string?>? onStatusChanged)
        {
            ArgumentNullException.ThrowIfNull(promptParts);

            await InitializedAsync.ConfigureAwait(false);

            var outerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var oldOuterCts = System.Threading.Interlocked.Exchange(ref _streamCts, outerCts);
            try { oldOuterCts?.Cancel(); } catch { }
            var outerToken = outerCts.Token;

            try
            {

                var runId = ShortIdGenerator.NewGuid();
                LastRunId = runId;
                ClearToolReferences();
                var mode = _currentMode;

                using var _progressRunScope = GenerationProgressHub.BeginRun(runId);

                const int maxTokensRetries = 5;
                int tokenRetryCount = 0;
                int? fallbackMaxTokens = null;
                int? fallbackContextWindow = null;

                var streamRotation = ServiceLocator.Get<TM.Services.Framework.AI.Core.ApiKeyRotationService>();
                var streamConfig = AI.GetActiveConfiguration();
                if (streamConfig == null)
                {
                    GlobalToast.Error("AI 服务未配置", "当前没有激活的 AI 模型，请先前往“智能助手 > 模型管理”完成配置。");
                    return "[错误] 当前没有激活的AI模型";
                }

                var fallbackResult = await TryRunPromptToolFallbackAsync(
                    streamConfig, displayText, promptParts, onChunk, onStatusChanged, runId, outerToken).ConfigureAwait(false);
                if (fallbackResult != null)
                {
                    return fallbackResult;
                }

                int maxRetries = streamConfig.RetryCount > 0 ? streamConfig.RetryCount : 2;
                var streamExcludeKeyIds = new HashSet<string>();
                var streamRateLimitExcludeKeyIds = new HashSet<string>();
                var streamFailedKeyDetails = new List<string>();
                TM.Services.Framework.AI.Core.KeySelection? streamCurrentKey = null;
                var streamRateLimitedCount = 0;
                var streamServerErrorCount = 0;
                const int streamMaxRateLimitRounds = 3;
                var streamRateLimitRound = 0;

                var poolSize = streamRotation.GetPoolStatus(streamConfig.ProviderId)?.ActiveKeys ?? maxRetries + 1;
                var effectiveMaxRetries = Math.Max(poolSize - 1, maxRetries);

                var pipeline = CapabilityServices.DefaultPipeline;
                var pipelineResolved = CapabilityServices.DefaultResolver.Resolve(
                    providerId: streamConfig.ProviderId,
                    modelId: streamConfig.ModelId,
                    endpoint: streamConfig.CustomEndpoint,
                    userHint: new UserCapabilityHint
                    {
                        CapabilitiesDetected = streamConfig.CapabilitiesDetected,
                        IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(streamConfig.ProviderId, streamConfig.ModelId),
                    });
                var pipelineCtx = new AIRequestContext
                {
                    RunId = runId,
                    Config = streamConfig,
                    ChatHistory = _chatHistory,
                    Resolved = pipelineResolved,
                };
                pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = GetCurrentProviderType();
                pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                await pipeline.RunStageAsync(pipelineCtx, MiddlewareStage.BeforeRequest, outerToken).ConfigureAwait(false);

                try
                {
                    for (int attempt = 0; attempt <= effectiveMaxRetries; attempt++)
                    {
                        CancellationTokenSource? localCts = null;
                        var answerBuilder = new System.Text.StringBuilder();
                        var thinkingBuilder = new System.Text.StringBuilder();
                        LastThinkingKind = ResolveThinkingKindForDisplay(null, streamConfig);
                        var hasTools = false;
                        var streamToolsKey = string.Empty;
                        var isStreamToolsProbing = false;
                        var firstChunkReceived = false;

                        var streamSw = System.Diagnostics.Stopwatch.StartNew();
                        int streamInputTokens = 0;
                        int streamOutputTokens = 0;
                        int streamFirstTokenMs = 0;
                        int streamThinkingMs = 0;
                        int streamToolCallCount = 0;

                        bool recorded = false;
                        void RecordStreamCall(bool success, string? errMsg)
                        {
                            if (recorded) return;
                            recorded = true;
                            if (success && streamCurrentKey != null && streamConfig != null)
                            {
                                try
                                {
                                    streamRotation.ReportKeyResult(
                                        streamConfig.ProviderId,
                                        streamCurrentKey.KeyId,
                                        TM.Services.Framework.AI.Core.KeyUseResult.Success);
                                }
                                catch (Exception rkrEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] RecordStreamCall Success 上报失败（非致命）: {rkrEx.Message}");
                                }
                            }
                            try
                            {
                                streamSw.Stop();
                                var cfg = streamConfig;
                                double tps = 0;
                                if (streamOutputTokens > 0 && streamSw.ElapsedMilliseconds > streamFirstTokenMs && streamFirstTokenMs > 0)
                                {
                                    var generateMs = streamSw.ElapsedMilliseconds - streamFirstTokenMs;
                                    if (generateMs > 0)
                                        tps = streamOutputTokens / (generateMs / 1000.0);
                                }
                                var pluginToolCount = PlanModeFilter.GetToolCallCount(runId);
                                var finalToolCount = Math.Max(streamToolCallCount, pluginToolCount);
                                _statistics.RecordCall(new ApiCallRecord
                                {
                                    Timestamp = DateTime.Now,
                                    ModelName = cfg?.ModelId ?? "unknown",
                                    Provider = cfg?.ProviderId ?? "Chat-Stream",
                                    Success = success,
                                    ResponseTimeMs = (int)streamSw.ElapsedMilliseconds,
                                    InputTokens = streamInputTokens,
                                    OutputTokens = streamOutputTokens,
                                    ErrorMessage = errMsg,
                                    FirstTokenMs = streamFirstTokenMs,
                                    TokensPerSecond = tps,
                                    ThinkingMs = streamThinkingMs,
                                    ToolCallCount = finalToolCount
                                });
                            }
                            catch (Exception recordEx) { DebugLogOnce("RecordStreamCall", recordEx); }
                            try { PlanModeFilter.ResetRun(runId); } catch { }
                        }

                        TM.Services.Framework.AI.RateLimiting.ApiRateLimiter.ReleaseHandle streamReleaseHandle;
                        try
                        {
                            var streamLimiter = ServiceLocator.Get<TM.Services.Framework.AI.RateLimiting.ApiRateLimiter>();
                            streamReleaseHandle = streamLimiter.Acquire(
                                streamConfig.ProviderId,
                                rpmLimit: streamConfig.RateLimitRPM,
                                tpmLimit: streamConfig.RateLimitTPM,
                                maxConcurrency: streamConfig.MaxConcurrency,
                                estimatedTokens: 0);
                        }
                        catch (TM.Services.Framework.AI.RateLimiting.LocalRateLimitException rlex)
                        {
                            LogIfPublic(streamConfig, $"[SKChatService] 流式本地速率限制触发: {rlex.Message}");
                            onStatusChanged?.Invoke($"本地限流：等待重试...");
                            try { await Task.Delay(Math.Min(rlex.WaitMs, 5000), outerToken).ConfigureAwait(false); }
                            catch (OperationCanceledException) { throw; }
                            RetryFallbackMiddleware.MarkRetry(pipelineCtx, "local_rate_limit");
                            attempt--;
                            continue;
                        }

                        Action<string> effectiveThinkingSink;
                        if (onThinkingChunk != null)
                        {
                            effectiveThinkingSink = t =>
                            {
                                onThinkingChunk(t);
                                thinkingBuilder.Append(t);
                            };
                        }
                        else if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Plan ||
                                 mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Agent)
                        {
                            effectiveThinkingSink = t =>
                            {
                                if (!string.IsNullOrWhiteSpace(t)) thinkingBuilder.AppendLine(t);
                            };
                        }
                        else
                        {
                            effectiveThinkingSink = t => thinkingBuilder.Append(t);
                        }

                        var draftingPhaseReported = false;
                        void ReportDraftingOnce()
                        {
                            if (draftingPhaseReported) return;
                            draftingPhaseReported = true;
                            GenerationProgressHub.ReportPhase(ProgressPhase.Drafting, "正文生成中...");
                        }

                        async Task PublishSimulatedChunks(string? answer, string? thinking, string? kind, int startSequence = 0, bool complete = true, CancellationToken simCt = default)
                        {
                            var displayKind = ResolveThinkingKindForDisplay(kind, streamConfig);
                            var chunks = SimulatedStreamChunker.Slice(answer, thinking, displayKind, runId, startSequence: startSequence).ToList();
                            foreach (var chunk in chunks)
                            {
                                if (chunk is TextDeltaChunk)
                                    ReportDraftingOnce();
                                await pipeline.RunStageAsync(pipelineCtx with { Chunk = chunk }, MiddlewareStage.OnChunk, simCt).ConfigureAwait(false);
                                AIChunkBus.Publish(chunk);
                            }

                            if (complete)
                            {
                                var completeChunk = new StreamCompleteChunk("stop")
                                {
                                    RunId = runId,
                                    Sequence = startSequence + chunks.Count,
                                };
                                await pipeline.RunStageAsync(pipelineCtx with { Chunk = completeChunk }, MiddlewareStage.OnChunk, simCt).ConfigureAwait(false);
                                AIChunkBus.Publish(completeChunk);
                            }
                        }

                        try
                        {
                            var poolStatus = streamRotation.GetPoolStatus(streamConfig.ProviderId);
                            if (poolStatus?.TotalKeys > 0)
                            {
                                poolSize = poolStatus.ActiveKeys > 0 ? poolStatus.ActiveKeys : maxRetries + 1;
                                effectiveMaxRetries = Math.Max(poolSize - 1, maxRetries);

                                var streamCombinedExclude = new HashSet<string>(streamExcludeKeyIds);
                                streamCombinedExclude.UnionWith(streamRateLimitExcludeKeyIds);
                                streamCurrentKey = streamRotation.GetNextKey(streamConfig.ProviderId, streamCombinedExclude);
                                if (streamCurrentKey == null)
                                {
                                    if (streamRateLimitExcludeKeyIds.Count > 0 && streamRateLimitRound < streamMaxRateLimitRounds)
                                    {
                                        streamRateLimitRound++;
                                        streamRateLimitedCount++;

                                        var minRemaining = streamRotation.GetMinRemainingCooldownSeconds(streamConfig.ProviderId, streamRateLimitExcludeKeyIds);
                                        TimeSpan rlDelay;
                                        string rlSource;
                                        if (minRemaining.HasValue && minRemaining.Value > 0)
                                        {
                                            rlDelay = TimeSpan.FromSeconds(Math.Min(minRemaining.Value + 1, RateLimitBackoffMaxSeconds * 4));
                                            rlSource = $"池中最早恢复={minRemaining.Value}s";
                                        }
                                        else
                                        {
                                            rlDelay = GetExponentialBackoff(streamRateLimitedCount - 1, RateLimitBackoffBaseSeconds, RateLimitBackoffMaxSeconds);
                                            rlSource = $"指数退避 {rlDelay.TotalSeconds:F0}s";
                                        }

                                        var statusMsg = $"所有密钥均被限流，{rlDelay.TotalSeconds:F0}s 后自动恢复（第 {streamRateLimitRound}/{streamMaxRateLimitRounds} 轮）";
                                        onStatusChanged?.Invoke(statusMsg);
                                        GlobalToast.Info("端点限速", statusMsg);
                                        LogIfPublic(streamConfig, $"[SKChatService] 429 端点级等待（Streaming）: 第{streamRateLimitRound}轮, {rlSource} | {GetConfigSummary(streamConfig)}");
                                        try { await Task.Delay(rlDelay, outerToken).ConfigureAwait(false); }
                                        catch (OperationCanceledException) { throw; }
                                        onStatusChanged?.Invoke(null);
                                        streamRateLimitExcludeKeyIds.Clear();
                                        RetryFallbackMiddleware.MarkRetry(pipelineCtx, "endpoint_rate_limit");
                                        attempt--;
                                        continue;
                                    }

                                    try
                                    {
                                        var router = ServiceLocator.Get<WritingApiRouter>();
                                        var beforeId = streamConfig.Id;
                                        router.TryActivateBackupForFailedConfig(beforeId);
                                        var after = AI.GetActiveConfiguration();
                                        if (router.IsUsingBackup && after != null && !string.Equals(after.Id, beforeId, StringComparison.Ordinal))
                                        {
                                            onStatusChanged?.Invoke("主接口不可用，已切换备用接口继续重试...");
                                            streamConfig = after;
                                            streamExcludeKeyIds.Clear();
                                            streamRateLimitExcludeKeyIds.Clear();
                                            streamCurrentKey = null;
                                            attempt = -1;
                                            RetryFallbackMiddleware.MarkRetry(pipelineCtx, "backup_endpoint_switch");

                                            var afterResolved = CapabilityServices.DefaultResolver.Resolve(
                                                providerId: streamConfig.ProviderId,
                                                modelId: streamConfig.ModelId,
                                                endpoint: streamConfig.CustomEndpoint,
                                                userHint: new UserCapabilityHint
                                                {
                                                    CapabilitiesDetected = streamConfig.CapabilitiesDetected,
                                                    IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(streamConfig.ProviderId, streamConfig.ModelId),
                                                });
                                            pipelineCtx = pipelineCtx with { Config = streamConfig, Resolved = afterResolved };
                                            pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = GetCurrentProviderType();
                                            pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);

                                            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.Track(
                                                runId,
                                                providerId: streamConfig.ProviderId,
                                                modelId: streamConfig.ModelId,
                                                endpoint: streamConfig.CustomEndpoint);

                                            continue;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogIfPublic(streamConfig, $"[SKChatService] 流式备用切换失败: {ex.Message}");
                                    }

                                    if (streamRateLimitRound >= streamMaxRateLimitRounds && streamExcludeKeyIds.Count == 0)
                                    {
                                        GlobalToast.Warning("端点整体限流", $"所有密钥均被端点限流（已重试 {streamRateLimitRound} 轮），请稍后再试或切换端点。");
                                        LogIfPublic(streamConfig, $"[SKChatService] 流式端点整体限流，{streamRateLimitRound} 轮重试后仍未恢复，并非密钥永久失效 | {GetConfigSummary(streamConfig)}");
                                    }
                                    else if (streamServerErrorCount > 0 && streamExcludeKeyIds.Count == streamServerErrorCount)
                                    {
                                        NotifyRealError("连接失败", "无法连接到 API 端点，请检查网络或代理设置。", streamConfig.ProviderId);
                                        LogIfPublic(streamConfig, $"[SKChatService] 流式连接失败（非密钥问题），ServerError/Unknown 导致 | {GetConfigSummary(streamConfig)}");
                                    }
                                    else
                                    {
                                        NotifyAllKeysExhausted(streamFailedKeyDetails, streamConfig.ProviderId);
                                    }
                                    RecordStreamCall(success: false, errMsg: "所有密钥不可用");
                                    try
                                    {
                                        var keyExhaustedEx = new InvalidOperationException("所有密钥不可用");
                                        await pipeline.RunStageAsync(pipelineCtx with { Error = keyExhaustedEx }, MiddlewareStage.OnError, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    }
                                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-StreamKeyExhausted", pipelineEx); }
                                    return "[错误] 所有密钥不可用";
                                }

                            }

                            var streamRoundApiKey = streamCurrentKey?.ApiKey ?? streamConfig.ApiKey ?? string.Empty;
                            var streamBundle = EnsureKernelInitialized(streamConfig, streamRoundApiKey);
                            if (streamBundle == null)
                            {
                                var error = "[错误] AI 服务未配置";
                                onChunk(error);
                                RecordStreamCall(success: false, errMsg: "AI 服务未配置");
                                return error;
                            }

                            localCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);

                            LogIfPublic(streamConfig, $"[SKChatService] SystemPrompt长度: {promptParts.SystemPrompt?.Length ?? 0}");
                            EnsureSystemPrompt(promptParts.SystemPrompt);

                            var userPromptForModel = string.IsNullOrWhiteSpace(promptParts.UserPrompt) ? displayText : promptParts.UserPrompt;
                            await EnsureCompressionIfNeededAsync(userPromptForModel, localCts.Token, fallbackContextWindow).ConfigureAwait(false);

                            _chatHistory.AddUserMessage(userPromptForModel);

                            LogIfPublic(streamConfig, $"[SKChatService] 流式发送: {displayText.Substring(0, Math.Min(50, displayText.Length))}...");

                            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.Track(
                                runId,
                                providerId: streamConfig.ProviderId,
                                modelId: streamConfig.ModelId,
                                endpoint: streamConfig.CustomEndpoint);

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunStarted,
                                Title = displayText.Length > 30 ? displayText[..30] : displayText,
                                Detail = displayText
                            });

                            var settings = GetCurrentModeSettings(fallbackMaxTokens, streamBundle.Kernel);
                            pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                            await pipeline.RunStageAsync(pipelineCtx with { Settings = settings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                            string? streamFinishReason = null;

                            var streamEpKey = GetEndpointKey(streamConfig);
                            streamToolsKey = GetStreamToolsKey(streamConfig);
                            hasTools = settings.FunctionChoiceBehavior != null;
                            var streamToolsCompat = hasTools ? GetStreamToolsCompatibility(streamToolsKey) : null;

                            if (hasTools && streamToolsCompat == false)
                            {
                                LogIfPublic(streamConfig, $"[SKChatService] stream+tools 缓存命中(不兼容)，直接非流式+tools: {MaskCacheKeyForLog(streamToolsKey)}");
                                RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_tools_incompatible_cached");
                                var fbSettings = GetCurrentModeSettings(fallbackMaxTokens, streamBundle.Kernel);
                                pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                await pipeline.RunStageAsync(pipelineCtx with { Settings = fbSettings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                                try
                                {
                                    var fbResp = await streamBundle.ChatService.GetChatMessageContentAsync(_chatHistory, fbSettings, streamBundle.Kernel, localCts.Token).ConfigureAwait(false);
                                    var fbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(fbResp.Content ?? string.Empty);
                                    var (fbContent, fbThinking, fbKind) = CleanNonStreamContent(fbSanitized);
                                    if (string.IsNullOrWhiteSpace(fbContent))
                                        fbContent = "（模型未输出正式回答，请重试。）";
                                    await PublishSimulatedChunks(fbContent, fbThinking, fbKind, simCt: localCts.Token).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(fbThinking))
                                    {
                                        LastThinkingKind = ResolveThinkingKindForDisplay(fbKind, streamConfig);
                                        effectiveThinkingSink(fbThinking);
                                    }
                                    answerBuilder.Append(fbContent);
                                    onChunk(fbContent);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception fbEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] stream+tools 非流式也失败: {fbEx.Message}");
                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    throw;
                                }
                                goto skipStreamingPipeline;
                            }

                            if (_streamingUnsupportedEndpoints.TryGetValue(streamEpKey, out var streamEpMark))
                            {
                                var streamEpElapsed = DateTime.UtcNow - streamEpMark;
                                if (streamEpElapsed > StreamingMarkExpiry)
                                {
                                    _streamingUnsupportedEndpoints.TryRemove(streamEpKey, out _);
                                    LogIfPublic(streamConfig, $"[SKChatService] 流式标记已过期({streamEpElapsed.TotalMinutes:F1}min)，恢复流式: {MaskCacheKeyForLog(streamEpKey)}");
                                }
                                else
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] 端点在降级标记内，直接非流式(剩余{(StreamingMarkExpiry - streamEpElapsed).TotalSeconds:F0}s): {MaskCacheKeyForLog(streamEpKey)}");
                                    RetryFallbackMiddleware.MarkFallback(pipelineCtx, "streaming_unsupported_endpoint_marked");
                                    var fbSettings = GetCurrentModeSettings(fallbackMaxTokens, streamBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = fbSettings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                                    try
                                    {
                                        var fbResp = await streamBundle.ChatService.GetChatMessageContentAsync(_chatHistory, fbSettings, streamBundle.Kernel, localCts.Token).ConfigureAwait(false);
                                        var fbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(fbResp.Content ?? string.Empty);
                                        var (fbContent, fbThinking, fbKind) = CleanNonStreamContent(fbSanitized);
                                        if (string.IsNullOrWhiteSpace(fbContent))
                                            fbContent = "（模型未输出正式回答，请重试。）";
                                        await PublishSimulatedChunks(fbContent, fbThinking, fbKind, simCt: localCts.Token).ConfigureAwait(false);
                                        if (!string.IsNullOrEmpty(fbThinking))
                                        {
                                            LastThinkingKind = ResolveThinkingKindForDisplay(fbKind, streamConfig);
                                            effectiveThinkingSink(fbThinking);
                                        }
                                        answerBuilder.Append(fbContent);
                                        onChunk(fbContent);
                                    }
                                    catch (OperationCanceledException) { throw; }
                                    catch (Exception fbEx)
                                    {
                                        LogIfPublic(streamConfig, $"[SKChatService] 标记窗口内非流式也失败: {fbEx.Message}");
                                        if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                            _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                        throw;
                                    }
                                    goto skipStreamingPipeline;
                                }
                            }

                            isStreamToolsProbing = hasTools && streamToolsCompat == null;
                            firstChunkReceived = false;

                            onStatusChanged?.Invoke("等待端点响应...");

                            await _streamingPipeline.ExecuteAsync(async innerCt =>
                            {
                                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                                idleCts.CancelAfter(isStreamToolsProbing ? FirstChunkTimeout : StreamIdleTimeout);

                                using var idleController = new IdleTimeoutController(
                                    idleCts, runId, StreamIdleTimeout, ToolExecutionIdleTimeout);

                                await foreach (var streamChunk in streamBundle.NovelAgent.InvokeStreamingAsync(_chatHistory, settings, runId, idleCts.Token).ConfigureAwait(false))
                                {
                                    await pipeline.RunStageAsync(pipelineCtx with { Chunk = streamChunk }, MiddlewareStage.OnChunk, idleCts.Token).ConfigureAwait(false);

                                    AIChunkBus.Publish(streamChunk);

                                    var isContentChunk = streamChunk is TextDeltaChunk or ThinkingDeltaChunk or ToolCallChunk;

                                    if (!firstChunkReceived && isContentChunk)
                                    {
                                        firstChunkReceived = true;
                                        if (streamFirstTokenMs == 0)
                                            streamFirstTokenMs = (int)streamSw.ElapsedMilliseconds;
                                        if (isStreamToolsProbing)
                                            CacheStreamToolsResult(streamToolsKey, compatible: true);
                                        idleController.ResetIdle();
                                        onStatusChanged?.Invoke(null);
                                    }
                                    else if (firstChunkReceived)
                                    {
                                        idleController.ResetIdle();
                                    }

                                    switch (streamChunk)
                                    {
                                        case TextDeltaChunk textChunk:
                                            var sanitizedChunk = TM.Services.Framework.AI.Core.ModelNameSanitizer.SanitizeChunk(textChunk.Content);
                                            ReportDraftingOnce();
                                            answerBuilder.Append(sanitizedChunk);
                                            onChunk(sanitizedChunk);
                                            break;
                                        case ThinkingDeltaChunk thinkingChunk:
                                            if (!string.IsNullOrWhiteSpace(thinkingChunk.Kind))
                                                LastThinkingKind = ResolveThinkingKindForDisplay(thinkingChunk.Kind, streamConfig);
                                            effectiveThinkingSink(thinkingChunk.Content);
                                            break;
                                        case ThinkingCompleteChunk thinkingComplete:
                                            if (!string.IsNullOrWhiteSpace(thinkingComplete.Kind))
                                                LastThinkingKind = ResolveThinkingKindForDisplay(thinkingComplete.Kind, streamConfig);
                                            if (thinkingComplete.DurationMs > 0) streamThinkingMs = thinkingComplete.DurationMs;
                                            break;
                                        case ToolCallChunk toolCall:
                                            streamToolCallCount++;
                                            break;
                                        case UsageChunk usage:
                                            if (usage.PromptTokens > 0) streamInputTokens = usage.PromptTokens;
                                            if (usage.CompletionTokens > 0) streamOutputTokens = usage.CompletionTokens;
                                            break;
                                        case StreamCompleteChunk completeChunk:
                                            streamFinishReason = completeChunk.FinishReason;
                                            break;
                                    }
                                }
                            }, localCts.Token).ConfigureAwait(false);

                            if (isStreamToolsProbing && !firstChunkReceived && answerBuilder.Length == 0
                                && !cancellationToken.IsCancellationRequested)
                            {
                                CacheStreamToolsResult(streamToolsKey, compatible: false);
                                LogIfPublic(streamConfig, $"[SKChatService] stream+tools 正常结束但 0 chunk，标记不兼容并非流式重试: {MaskCacheKeyForLog(streamToolsKey)}");
                                RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_zero_chunk_fallback");
                                try
                                {
                                    var emptyFbSettings = GetCurrentModeSettings(fallbackMaxTokens, streamBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = emptyFbSettings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                                    var emptyFbResp = await streamBundle.ChatService.GetChatMessageContentAsync(_chatHistory, emptyFbSettings, streamBundle.Kernel, localCts.Token).ConfigureAwait(false);
                                    var emptyFbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(emptyFbResp.Content ?? string.Empty);
                                    var (emptyFbContent, emptyFbThinking, emptyFbKind) = CleanNonStreamContent(emptyFbSanitized);
                                    if (string.IsNullOrWhiteSpace(emptyFbContent))
                                        emptyFbContent = "（模型未输出正式回答，请重试。）";
                                    await PublishSimulatedChunks(emptyFbContent, emptyFbThinking, emptyFbKind, simCt: localCts.Token).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(emptyFbThinking))
                                    {
                                        LastThinkingKind = ResolveThinkingKindForDisplay(emptyFbKind, streamConfig);
                                        effectiveThinkingSink(emptyFbThinking);
                                    }
                                    answerBuilder.Append(emptyFbContent);
                                    onChunk(emptyFbContent);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception emptyFbEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] 0 chunk 非流式兼底失败: {emptyFbEx.Message}");
                                }
                            }
                        skipStreamingPipeline:
                            ChatModeSettings.SyncLastFinishReason(streamFinishReason, AI.GetActiveConfiguration());

                            var result = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(answerBuilder.ToString());

                            const int maxStreamLengthCont = 2;
                            for (int lc = 0;
                                 lc < maxStreamLengthCont
                                 && ChatModeSettings.IsFinishReasonTruncated(streamFinishReason)
                                 && !cancellationToken.IsCancellationRequested; lc++)
                            {
                                var streamCfg = AI.GetActiveConfiguration();
                                var upgradedMax = ChatModeSettings.GetUpgradeMaxTokens(ChatModeSettings.LastUsedMaxTokens, streamCfg?.ModelId, streamCfg?.CustomEndpoint, streamCfg?.ProviderId);
                                if (!upgradedMax.HasValue)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] Stream finish_reason=length 已在梯队顶端，放弃续写");
                                    break;
                                }
                                LogIfPublic(streamConfig, $"[SKChatService] Stream finish_reason=length，自动续写#{lc + 1}: {ChatModeSettings.LastUsedMaxTokens} -> {upgradedMax.Value}");
                                _chatHistory.AddAssistantMessage(result);
                                _chatHistory.AddUserMessage("请继续");
                                var contSettings = GetCurrentModeSettings(upgradedMax.Value, streamBundle.Kernel);
                                pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                await pipeline.RunStageAsync(pipelineCtx with { Settings = contSettings }, MiddlewareStage.TransformSettings, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                if (contSettings is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings oaiCs)
                                    oaiCs.FunctionChoiceBehavior = null;
                                string contContent;
                                try
                                {
                                    var contStreamResult = await AdaptiveGenerateAsync(_chatHistory, contSettings, null, localCts?.Token ?? cancellationToken, null).ConfigureAwait(false);
                                    contContent = contStreamResult.Content;
                                    streamInputTokens += contStreamResult.InputTokens;
                                    streamOutputTokens += contStreamResult.OutputTokens;
                                }
                                catch (Exception contEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] Stream 续写异常: {contEx.Message}");
                                    if (_chatHistory.Count >= 2
                                        && _chatHistory[^1].Role == AuthorRole.User
                                        && _chatHistory[^2].Role == AuthorRole.Assistant)
                                    {
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    }
                                    break;
                                }
                                streamFinishReason = ChatModeSettings.LastFinishReason;
                                if (_chatHistory.Count >= 2
                                    && _chatHistory[^1].Role == AuthorRole.User
                                    && _chatHistory[^2].Role == AuthorRole.Assistant)
                                {
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                }
                                contContent = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(contContent);
                                var (isStreamContCancelled, _) = UIMessageItem.TryExtractCancelledPartial(contContent);
                                if (!string.IsNullOrWhiteSpace(contContent)
                                    && (contContent.StartsWith("[错误]", StringComparison.Ordinal) || isStreamContCancelled))
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] Stream 续写返回错误/取消，停止续写: {contContent}");
                                    break;
                                }
                                if (!string.IsNullOrEmpty(contContent))
                                {
                                    var appendedContent = "\n\n" + contContent;
                                    await PublishSimulatedChunks(appendedContent, null, null, complete: false, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    onChunk(appendedContent);
                                    result += appendedContent;
                                }
                                LogIfPublic(streamConfig, $"[SKChatService] Stream 续写完成，追加 {contContent.Length} 字符，finish_reason={streamFinishReason ?? "(null)"}");
                            }

                            LastThinkingContent = thinkingBuilder.ToString();

                            if (string.IsNullOrWhiteSpace(result) && attempt < maxRetries)
                            {
                                LogIfPublic(streamConfig, $"[SKChatService] 模型返回空回复，自动重试（第 {attempt + 1} 次）");
                                RetryFallbackMiddleware.MarkRetry(pipelineCtx, "empty_reply");
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                {
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                }

                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(result))
                            {
                                result = "（模型未输出正式回答，请重试。）";
                                await PublishSimulatedChunks(result, null, null, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                onChunk(result);
                            }

                            if (_chatHistory.Count == 0 ||
                                _chatHistory[^1].Role != AuthorRole.Assistant ||
                                _chatHistory[^1].Content != result)
                            {
                                _chatHistory.AddAssistantMessage(result);
                            }

                            _turnIndex++;

                            LogIfPublic(streamConfig, $"[SKChatService] 流式完成，总长度: {result.Length}，finish_reason={streamFinishReason ?? "(null)"}");

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.AssistantMessage,
                                Title = "Assistant",
                                Detail = result,
                                Succeeded = true
                            });

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunCompleted,
                                Title = "Run completed",
                                Succeeded = true
                            });
                            RecordStreamCall(success: true, errMsg: null);
                            await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = result }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                            return result;
                        }
                        catch (BrokenCircuitException brokenEx)
                        {
                            LogIfPublic(streamConfig, "[SKChatService] 熔断器开路，端点暂时不可用");
                            GlobalToast.Error("端点暂时不可用", "熔断器已开路，请稍等 30 秒后重试，或切换到其他模型");
                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            try
                            {
                                await pipeline.RunStageAsync(pipelineCtx with { Error = brokenEx }, MiddlewareStage.OnError, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-CircuitBreaker", pipelineEx); }
                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunFailed,
                                Title = "熔断器开路",
                                Detail = "[错误] 端点暂时不可用",
                                Succeeded = false
                            });
                            RecordStreamCall(success: false, errMsg: "熔断器开路");
                            return "[错误] 端点暂时不可用，请稍等 30 秒后重试";
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            if (answerBuilder.Length == 0)
                            {
                                try
                                {
                                    var idleEpKey = GetEndpointKey(streamConfig);
                                    if (isStreamToolsProbing && !firstChunkReceived)
                                    {
                                        CacheStreamToolsResult(streamToolsKey, compatible: false);
                                        LogIfPublic(streamConfig, $"[SKChatService] stream+tools 探测超时（{FirstChunkTimeout.TotalSeconds}s），标记不兼容并非流式重试");
                                    }
                                    else if (!string.IsNullOrEmpty(idleEpKey))
                                    {
                                        _streamingUnsupportedEndpoints[idleEpKey] = DateTime.UtcNow - (StreamingMarkExpiry - StreamNetworkFallbackWindow);
                                    }
                                    GlobalToast.Warning("流式无响应", "已自动切换为标准模式重试");
                                    LogIfPublic(streamConfig, "[SKChatService] 流式空闲超时（0 chunk），降级非流式重试");
                                    RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_idle_timeout_fallback");
                                    onStatusChanged?.Invoke("端点无响应，降级非流式重试...");

                                    var idleFbBundle = EnsureKernelInitialized(streamConfig, streamCurrentKey?.ApiKey ?? streamConfig.ApiKey ?? string.Empty)
                                        ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                    var idleFbSettings = GetCurrentModeSettings(fallbackMaxTokens, idleFbBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = idleFbSettings }, MiddlewareStage.TransformSettings, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var idleFbResp = await idleFbBundle.ChatService.GetChatMessageContentAsync(
                                        _chatHistory, idleFbSettings, idleFbBundle.Kernel, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var idleFbContent = idleFbResp.Content ?? string.Empty;
                                    try
                                    {
                                        var cfg = AI.GetActiveConfiguration();
                                        if (cfg != null)
                                            ChatModeSettings.RecordSuccessObservation(cfg, _chatHistory, idleFbSettings, idleFbContent);
                                    }
                                    catch (Exception obsEx) { DebugLogOnce("RecordSuccessObs-IdleTimeout", obsEx); }
                                    var idleFbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(idleFbContent);
                                    var (idleSanitized, idleThinking, idleKind) = CleanNonStreamContent(idleFbSanitized);
                                    if (string.IsNullOrWhiteSpace(idleSanitized))
                                    {
                                        idleSanitized = "（模型未输出正式回答，请重试。）";
                                    }
                                    await PublishSimulatedChunks(idleSanitized, idleThinking, idleKind, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(idleThinking))
                                    {
                                        LastThinkingKind = ResolveThinkingKindForDisplay(idleKind, streamConfig);
                                        effectiveThinkingSink(idleThinking);
                                    }
                                    onChunk(idleSanitized);
                                    answerBuilder.Append(idleSanitized);

                                    if (_chatHistory.Count == 0 ||
                                        _chatHistory[^1].Role != AuthorRole.Assistant ||
                                        _chatHistory[^1].Content != idleSanitized)
                                    {
                                        _chatHistory.AddAssistantMessage(idleSanitized);
                                    }

                                    ExecutionEventHub.Publish(new ExecutionEvent
                                    {
                                        RunId = runId,
                                        Mode = mode,
                                        RunType = _currentRunType,
                                        EventType = ExecutionEventType.AssistantMessage,
                                        Title = "Assistant",
                                        Detail = idleSanitized,
                                        Succeeded = true
                                    });
                                    ExecutionEventHub.Publish(new ExecutionEvent
                                    {
                                        RunId = runId,
                                        Mode = mode,
                                        RunType = _currentRunType,
                                        EventType = ExecutionEventType.RunCompleted,
                                        Title = "Run completed",
                                        Succeeded = true
                                    });
                                    RecordStreamCall(success: true, errMsg: null);
                                    await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = idleSanitized }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    return idleSanitized;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception idleFbEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] 空闲超时后非流式重试也失败: {idleFbEx.Message}");
                                }
                            }

                            LogIfPublic(streamConfig, "[SKChatService] 流式空闲超时（90s无数据），强制中止流");
                            GlobalToast.Warning("响应超时", "AI 流式超过90秒无响应，请检查网络或端点状态");
                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            try
                            {
                                var idleEx = new TimeoutException("流式请求空闲超时（90s 无数据）");
                                await pipeline.RunStageAsync(pipelineCtx with { Error = idleEx }, MiddlewareStage.OnError, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-IdleTimeout", pipelineEx); }
                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunFailed,
                                Title = "空闲超时",
                                Detail = "[错误] 请求超时",
                                Succeeded = false
                            });
                            RecordStreamCall(success: false, errMsg: "请求空闲超时");
                            return "[错误] 请求空闲超时";
                        }
                        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            if (localCts?.IsCancellationRequested == true)
                            {
                                LogIfPublic(streamConfig, "[SKChatService] 流式请求已被 CancelCurrentRequest 取消");
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                ExecutionEventHub.Publish(new ExecutionEvent
                                {
                                    RunId = runId,
                                    Mode = mode,
                                    RunType = _currentRunType,
                                    EventType = ExecutionEventType.RunFailed,
                                    Title = "已取消",
                                    Detail = "[已取消]",
                                    Succeeded = false
                                });
                                RecordStreamCall(success: false, errMsg: "用户取消");
                                return "[已取消]";
                            }
                            LogIfPublic(streamConfig, $"[SKChatService] 流式请求超时或被底层取消: {ex.Message}");
                            GlobalToast.Warning("请求超时", "请检查网络或代理连接后重试");

                            var partialOnTimeout = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(answerBuilder.ToString());
                            LastThinkingContent = thinkingBuilder.ToString();
                            if (!string.IsNullOrWhiteSpace(partialOnTimeout))
                            {
                                if (_chatHistory.Count == 0 ||
                                    _chatHistory[^1].Role != AuthorRole.Assistant ||
                                    _chatHistory[^1].Content != partialOnTimeout)
                                {
                                    _chatHistory.AddAssistantMessage(partialOnTimeout);
                                }
                                LogIfPublic(streamConfig, $"[SKChatService] 超时但保留部分回答: {partialOnTimeout.Length} 字符");
                            }
                            else
                            {
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                {
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                }
                            }

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunFailed,
                                Title = "请求超时",
                                Detail = "[错误] 请求超时",
                                Succeeded = false
                            });
                            RecordStreamCall(success: false, errMsg: "请求超时");
                            return string.IsNullOrWhiteSpace(partialOnTimeout) ? "[错误] 请求超时" : $"[已取消:部分]{partialOnTimeout}";
                        }
                        catch (OperationCanceledException)
                        {
                            var partialAnswer = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(answerBuilder.ToString());
                            LastThinkingContent = thinkingBuilder.ToString();

                            if (!string.IsNullOrWhiteSpace(partialAnswer))
                            {
                                if (_chatHistory.Count == 0 ||
                                    _chatHistory[^1].Role != AuthorRole.Assistant ||
                                    _chatHistory[^1].Content != partialAnswer)
                                {
                                    _chatHistory.AddAssistantMessage(partialAnswer);
                                }
                                LogIfPublic(streamConfig, $"[SKChatService] 流式取消，保留部分回答: {partialAnswer.Length} 字符");
                            }
                            else
                            {
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                {
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                }
                                LogIfPublic(streamConfig, "[SKChatService] 流式取消，无部分内容");
                            }

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunFailed,
                                Title = "已取消",
                                Detail = "[已取消]",
                                Succeeded = false
                            });
                            RecordStreamCall(success: false, errMsg: "用户取消");
                            return string.IsNullOrWhiteSpace(partialAnswer) ? "[已取消]" : $"[已取消:部分]{partialAnswer}";
                        }
                        catch (Exception ex)
                        {
                            if (answerBuilder.Length == 0
                                && !cancellationToken.IsCancellationRequested
                                && hasTools && IsToolsUnsupportedError(ex))
                            {
                                CacheStreamToolsResult(streamToolsKey, compatible: false);
                                LogIfPublic(streamConfig, $"[SKChatService] HTTP 即时检测 stream+tools 不兼容，非流式重试: {ex.Message}");
                                RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_tools_http_unsupported");
                                try
                                {
                                    var toolsFbBundle = EnsureKernelInitialized(streamConfig, streamCurrentKey?.ApiKey ?? streamConfig.ApiKey ?? string.Empty)
                                        ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                    var toolsFbSettings = GetCurrentModeSettings(fallbackMaxTokens, toolsFbBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = toolsFbSettings }, MiddlewareStage.TransformSettings, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var toolsFbResp = await toolsFbBundle.ChatService.GetChatMessageContentAsync(_chatHistory, toolsFbSettings, toolsFbBundle.Kernel, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var toolsFbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(toolsFbResp.Content ?? string.Empty);
                                    var (toolsFbContent, toolsFbThinking, toolsFbKind) = CleanNonStreamContent(toolsFbSanitized);
                                    if (string.IsNullOrWhiteSpace(toolsFbContent))
                                        toolsFbContent = "（模型未输出正式回答，请重试。）";
                                    await PublishSimulatedChunks(toolsFbContent, toolsFbThinking, toolsFbKind, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(toolsFbThinking))
                                    {
                                        LastThinkingKind = ResolveThinkingKindForDisplay(toolsFbKind, streamConfig);
                                        effectiveThinkingSink(toolsFbThinking);
                                    }
                                    onChunk(toolsFbContent);
                                    answerBuilder.Append(toolsFbContent);
                                    if (_chatHistory.Count == 0 || _chatHistory[^1].Role != AuthorRole.Assistant || _chatHistory[^1].Content != toolsFbContent)
                                        _chatHistory.AddAssistantMessage(toolsFbContent);
                                    ExecutionEventHub.Publish(new ExecutionEvent { RunId = runId, Mode = mode, RunType = _currentRunType, EventType = ExecutionEventType.AssistantMessage, Title = "Assistant", Detail = toolsFbContent, Succeeded = true });
                                    ExecutionEventHub.Publish(new ExecutionEvent { RunId = runId, Mode = mode, RunType = _currentRunType, EventType = ExecutionEventType.RunCompleted, Title = "Run completed", Succeeded = true });
                                    RecordStreamCall(success: true, errMsg: null);
                                    await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = toolsFbContent }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    return toolsFbContent;
                                }
                                catch (Exception toolsFbEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] stream+tools 非流式重试也失败: {toolsFbEx.Message}");
                                }
                            }

                            if (answerBuilder.Length == 0
                                && !cancellationToken.IsCancellationRequested
                                && IsStreamNetworkError(ex))
                            {
                                try
                                {
                                    var endpointKey = GetEndpointKey(streamConfig);
                                    if (!string.IsNullOrEmpty(endpointKey))
                                    {
                                        _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow - (StreamingMarkExpiry - StreamNetworkFallbackWindow);
                                    }

                                    GlobalToast.Warning("流式连接中断", "检测到流式连接被中断，已自动切换为标准模式重试。可稍后重试或更换端点。");
                                    LogIfPublic(streamConfig, $"[SKChatService] 流式网络断连（0 chunk），已切换非流式重试: {ex.Message}");
                                    RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_network_error_fallback");

                                    var netFbBundle = EnsureKernelInitialized(streamConfig, streamCurrentKey?.ApiKey ?? streamConfig.ApiKey ?? string.Empty)
                                        ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                    var settingsForFallback = GetCurrentModeSettings(fallbackMaxTokens, netFbBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = settingsForFallback }, MiddlewareStage.TransformSettings, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var response = await netFbBundle.ChatService.GetChatMessageContentAsync(_chatHistory, settingsForFallback, netFbBundle.Kernel, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var fallbackContent = response.Content ?? string.Empty;
                                    try
                                    {
                                        var cfg = AI.GetActiveConfiguration();
                                        if (cfg != null)
                                            ChatModeSettings.RecordSuccessObservation(cfg, _chatHistory, settingsForFallback, fallbackContent);
                                    }
                                    catch (Exception obsEx) { DebugLogOnce("RecordSuccessObs-Adaptive2", obsEx); }
                                    var netFbSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(fallbackContent);
                                    var (sanitized, netFbThinking, netFbKind) = CleanNonStreamContent(netFbSanitized);
                                    if (string.IsNullOrWhiteSpace(sanitized))
                                    {
                                        sanitized = "（模型未输出正式回答，请重试。）";
                                    }
                                    await PublishSimulatedChunks(sanitized, netFbThinking, netFbKind, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(netFbThinking))
                                    {
                                        LastThinkingKind = ResolveThinkingKindForDisplay(netFbKind, streamConfig);
                                        effectiveThinkingSink(netFbThinking);
                                    }
                                    onChunk(sanitized);
                                    answerBuilder.Append(sanitized);

                                    if (_chatHistory.Count == 0 ||
                                        _chatHistory[^1].Role != AuthorRole.Assistant ||
                                        _chatHistory[^1].Content != sanitized)
                                    {
                                        _chatHistory.AddAssistantMessage(sanitized);
                                    }

                                    ExecutionEventHub.Publish(new ExecutionEvent
                                    {
                                        RunId = runId,
                                        Mode = mode,
                                        RunType = _currentRunType,
                                        EventType = ExecutionEventType.AssistantMessage,
                                        Title = "Assistant",
                                        Detail = sanitized,
                                        Succeeded = true
                                    });

                                    ExecutionEventHub.Publish(new ExecutionEvent
                                    {
                                        RunId = runId,
                                        Mode = mode,
                                        RunType = _currentRunType,
                                        EventType = ExecutionEventType.RunCompleted,
                                        Title = "Run completed",
                                        Succeeded = true
                                    });
                                    RecordStreamCall(success: true, errMsg: null);
                                    await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = sanitized }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    return sanitized;
                                }
                                catch (Exception fex)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] 流式网络断连后非流式重试失败: {fex.Message}");
                                }
                            }

                            if (answerBuilder.Length == 0 && !cancellationToken.IsCancellationRequested
                                && IsConnectionError(ex) && _useDirectKernel)
                            {
                                _useDirectKernel = false;
                                _directKernelDisabledAt = DateTime.UtcNow;
                                InvalidateAllBundles();
                                LogIfPublic(streamConfig, $"[SKChatService] 直连失败，切换代理重试（{DirectKernelRetryAfter.TotalMinutes:F0} 分钟后尝试恢复直连）: {ex.Message} | {GetConfigSummary(streamConfig)}");
                                RetryFallbackMiddleware.MarkRetry(pipelineCtx, "direct_to_proxy");
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                continue;
                            }

                            if (answerBuilder.Length == 0 && !cancellationToken.IsCancellationRequested
                                && IsThinkingNotSupportedError(ex) && !_skipThinkingInjection)
                            {
                                var thinkingCfg = streamConfig ?? AI.GetActiveConfiguration();
                                if (ChatModeSettings.TryRecordReasoningCapForFailure(thinkingCfg, out var thFamily, out var thFrom, out var thTo))
                                {
                                    onStatusChanged?.Invoke($"推理参数 {thFrom} 不兼容，已自动降级到 {thTo} 后重试...");
                                    LogIfPublic(streamConfig, $"[SKChatService] 流式推理参数自动降级: family={thFamily}, {thFrom} -> {thTo}, error={ex.Message}");
                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, "reasoning_param_unsupported");
                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    attempt--;
                                    continue;
                                }
                                _skipThinkingInjection = true;
                                ResetActiveThinkingParams(streamConfig ?? AI.GetActiveConfiguration());
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                var errMsg = IsTianmingPrivateProvider(streamConfig?.ProviderId)
                                    ? "当前模型不支持思考/推理参数，已记录兼容性降级，请重新发送消息。"
                                    : $"当前模型不支持思考/推理参数，已记录兼容性降级，请重新发送消息。\n({ex.Message})";
                                GlobalToast.Error("推理参数不支持", "已记录兼容性降级，请重新发送");
                                LogIfPublic(streamConfig, $"[SKChatService] 思考参数不支持，已写入兼容性 cap 并返回错误: {ex.Message}");
                                RecordStreamCall(success: false, errMsg: "推理参数不支持");
                                return $"[错误] {errMsg}";
                            }

                            var (keyUseResult, keyRawMsg) = ClassifyException(ex);

                            if (answerBuilder.Length == 0 && !cancellationToken.IsCancellationRequested
                                && ChatModeSettings.ShouldAttemptReasoningFallback(ex, keyUseResult))
                            {
                                var rtCfg = streamConfig ?? AI.GetActiveConfiguration();
                                if (ChatModeSettings.TryRecordReasoningCapForFailure(rtCfg, out var rtFamily, out var rtFrom, out var rtTo))
                                {
                                    onStatusChanged?.Invoke($"推理参数 {rtFrom} 不兼容，已自动降级到 {rtTo} 后重试...");
                                    LogIfPublic(streamConfig, $"[SKChatService] 流式推理参数自动降级: family={rtFamily}, {rtFrom} -> {rtTo}, error={keyRawMsg}");
                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, "reasoning_family_fallback");
                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    attempt--;
                                    continue;
                                }
                            }

                            if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.ModelNotFound)
                            {
                                NotifyRealError("模型不存在", keyRawMsg, streamConfig?.ProviderId);
                                throw new AlreadyNotifiedApiException(keyRawMsg, ex);
                            }

                            if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.ContentFiltered)
                            {
                                NotifyRealError("内容审核拒绝", keyRawMsg, streamConfig?.ProviderId);
                                throw new AlreadyNotifiedApiException(keyRawMsg, ex);
                            }

                            if (answerBuilder.Length == 0 && !cancellationToken.IsCancellationRequested
                                && streamCurrentKey != null && streamConfig != null)
                            {
                                if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.NetworkError)
                                {
                                    NotifyRealError("网络连接失败", "网络/端点连接异常，导致请求中断。可稍后重试或更换端点。", streamConfig.ProviderId);
                                    throw new AlreadyNotifiedApiException(keyRawMsg, ex);
                                }

                                var shouldRotate =
                                    !ChatModeSettings.IsMaxTokensError(ex) && !ChatModeSettings.IsContextWindowError(ex)
                                    && !ChatModeSettings.IsUnsupportedParameterError(ex)
                                    && keyUseResult is not TM.Services.Framework.AI.Core.KeyUseResult.InvalidRequest
                                    && keyUseResult is not TM.Services.Framework.AI.Core.KeyUseResult.StreamNotSupported
                                    && (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                                            or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                                            or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                                            or TM.Services.Framework.AI.Core.KeyUseResult.RateLimited
                                        || (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                                                or TM.Services.Framework.AI.Core.KeyUseResult.Unknown
                                            && attempt < maxRetries));

                                if (shouldRotate)
                                {
                                    var kLabel = IsTianmingPrivateProvider(streamConfig.ProviderId)
                                        ? "内置密钥"
                                        : !string.IsNullOrWhiteSpace(streamCurrentKey.Remark)
                                        ? streamCurrentKey.Remark
                                        : streamCurrentKey.ApiKey.Length > 10 ? streamCurrentKey.ApiKey[..10] + "..." : streamCurrentKey.ApiKey;
                                    streamRotation.ReportKeyResult(streamConfig.ProviderId, streamCurrentKey.KeyId, keyUseResult, keyRawMsg);

                                    if (keyUseResult != TM.Services.Framework.AI.Core.KeyUseResult.RateLimited
                                        && !IsTianmingPrivateProvider(streamConfig.ProviderId))
                                        streamFailedKeyDetails.Add($"[{kLabel}] → {keyRawMsg}");

                                    if (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                                        or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                                        or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted)
                                    {
                                        NotifyKeyError(keyUseResult, kLabel, keyRawMsg, streamConfig.ProviderId);
                                        onStatusChanged?.Invoke("当前密钥不可用，切换下一个密钥重试...");
                                    }

                                    if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                                    {
                                        var retryAfterSec = TryExtractRetryAfterSeconds(ex);
                                        var cooledSec = streamRotation.CooldownRateLimitedKey(streamConfig.ProviderId, streamCurrentKey.KeyId, retryAfterSec);
                                        streamRateLimitExcludeKeyIds.Add(streamCurrentKey.KeyId);

                                        var retryAfterTag = retryAfterSec.HasValue ? $"Retry-After={retryAfterSec}s" : "无 Retry-After";
                                        LogIfPublic(streamConfig, $"[SKChatService] 429 限流（Streaming），{retryAfterTag}，冷却 {cooledSec}s | {GetConfigSummary(streamConfig)}");
                                        onStatusChanged?.Invoke(IsTianmingPrivateProvider(streamConfig.ProviderId)
                                            ? $"当前端点已限速，{cooledSec}s 后自动恢复，切换其他密钥重试..."
                                            : $"[{kLabel}] 已冷却 {cooledSec}s 自动恢复，切换其他密钥重试...");
                                    }
                                    else if (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                                                  or TM.Services.Framework.AI.Core.KeyUseResult.Unknown)
                                    {
                                        streamServerErrorCount++;
                                        var delay = GetExponentialBackoff(streamServerErrorCount - 1, ServerErrorBackoffBaseSeconds, ServerErrorBackoffMaxSeconds);
                                        LogIfPublic(streamConfig, $"[SKChatService] 服务端错误退避（Streaming）: {keyUseResult}, {delay.TotalSeconds}s | {GetConfigSummary(streamConfig)}");
                                        onStatusChanged?.Invoke($"AI服务暂时异常，{delay.TotalSeconds:F0}s 后重试...");
                                        try { await Task.Delay(delay, outerToken).ConfigureAwait(false); }
                                        catch (OperationCanceledException) { throw; }
                                        onStatusChanged?.Invoke(null);
                                    }
                                    else
                                    {
                                        streamExcludeKeyIds.Add(streamCurrentKey.KeyId);
                                    }
                                    streamCurrentKey = null;

                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);

                                    if (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                                        or TM.Services.Framework.AI.Core.KeyUseResult.Unknown)
                                        LogIfPublic(streamConfig, $"[SKChatService] 流式服务端错误，退避后重试同一 key | {keyUseResult}, attempt={attempt}/{maxRetries}");
                                    else
                                        LogIfPublic(streamConfig, $"[SKChatService] 流式密钥轮换: {keyUseResult}，已排除，等待下次 attempt 换 key");

                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, $"key_rotation:{keyUseResult}");
                                    continue;
                                }
                            }

                            if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.StreamNotSupported
                                && answerBuilder.Length == 0)
                            {
                                try
                                {
                                    GlobalToast.Info("流式不支持", "该端点不支持流式传输，已自动降级为标准模式。");
                                    LogIfPublic(streamConfig, "[SKChatService] 端点不支持流式，自动降级为非流式模式");
                                    RetryFallbackMiddleware.MarkFallback(pipelineCtx, "stream_not_supported_fallback");

                                    if (streamConfig == null)
                                        throw new InvalidOperationException("[SKChatService] streamConfig 未就绪");
                                    var sfBundle = EnsureKernelInitialized(streamConfig, streamCurrentKey?.ApiKey ?? streamConfig.ApiKey ?? string.Empty)
                                        ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                    var sfSettings = GetCurrentModeSettings(fallbackMaxTokens, sfBundle.Kernel);
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = sfSettings }, MiddlewareStage.TransformSettings, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var sfResponse = await sfBundle.ChatService.GetChatMessageContentAsync(_chatHistory, sfSettings, sfBundle.Kernel, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    var sfContent = sfResponse?.Content ?? string.Empty;
                                    var sfSanitized = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(sfContent);
                                    var (sfCleaned, sfThinking, sfKind) = CleanNonStreamContent(sfSanitized);
                                    sfSanitized = string.IsNullOrWhiteSpace(sfCleaned) ? sfSanitized : sfCleaned;
                                    if (string.IsNullOrWhiteSpace(sfSanitized))
                                    {
                                        sfSanitized = "（模型未输出正式回答，请重试。）";
                                    }
                                    await PublishSimulatedChunks(sfSanitized, sfThinking, sfKind, simCt: localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    onChunk(sfSanitized);
                                    answerBuilder.Append(sfSanitized);

                                    if (_chatHistory.Count == 0 ||
                                        _chatHistory[^1].Role != AuthorRole.Assistant ||
                                        _chatHistory[^1].Content != sfSanitized)
                                    {
                                        _chatHistory.AddAssistantMessage(sfSanitized);
                                    }

                                    ExecutionEventHub.Publish(new ExecutionEvent { RunId = runId, Mode = mode, RunType = _currentRunType, EventType = ExecutionEventType.AssistantMessage, Title = "Assistant", Detail = sfSanitized, Succeeded = true });
                                    ExecutionEventHub.Publish(new ExecutionEvent { RunId = runId, Mode = mode, RunType = _currentRunType, EventType = ExecutionEventType.RunCompleted, Title = "Run completed", Succeeded = true });
                                    RecordStreamCall(success: true, errMsg: null);
                                    await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = sfSanitized }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                                    return sfSanitized;
                                }
                                catch (Exception sfEx)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] 非流式降级也失败: {sfEx.Message}");
                                    NotifyRealError("AI 请求失败", sfEx.Message, streamConfig?.ProviderId);
                                    throw new AlreadyNotifiedApiException(sfEx.Message, sfEx);
                                }
                            }

                            if (tokenRetryCount < maxTokensRetries)
                            {
                                var longCtxConfig = AI.GetActiveConfiguration();
                                if (ChatModeSettings.IsLongContextRejectedError(ex, longCtxConfig)
                                    && longCtxConfig != null && !string.IsNullOrEmpty(longCtxConfig.ModelId))
                                {
                                    ChatModeSettings.MarkUnsupportedParam(
                                        longCtxConfig.ProviderId, longCtxConfig.CustomEndpoint, longCtxConfig.ModelId, "long_context");
                                    longCtxConfig.EnableLongContext = null;
                                    try { AI.UpdateConfiguration(longCtxConfig); } catch { }
                                    tokenRetryCount++;
                                    LogIfPublic(longCtxConfig, $"[SKChatService] 1M 上下文请求被拒，已回退到基线窗口并重试: {longCtxConfig.ModelId}");
                                    GlobalToast.Warning("1M 上下文已回退", "端点未接受 1M 参数，已自动回退并重试");
                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, "long_context_rejected");
                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    continue;
                                }
                            }

                            if (tokenRetryCount < maxTokensRetries
                                && ChatModeSettings.TryParseUnsupportedParamName(ex, out var unsupParamName))
                            {
                                var unsupConfig = AI.GetActiveConfiguration();
                                if (unsupConfig != null && !string.IsNullOrEmpty(unsupConfig.ModelId))
                                {
                                    ChatModeSettings.MarkUnsupportedParam(unsupConfig.ProviderId, unsupConfig.CustomEndpoint, unsupConfig.ModelId, unsupParamName);
                                    tokenRetryCount++;
                                    if (unsupParamName.Contains("max_tokens") || unsupParamName.Contains("max_output") || unsupParamName.Contains("max_completion"))
                                        fallbackMaxTokens = null;
                                    LogIfPublic(unsupConfig, $"[SKChatService] 端点不支持参数 '{unsupParamName}'，标记并重试");
                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, $"unsupported_param:{unsupParamName}");
                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    continue;
                                }
                            }

                            if (tokenRetryCount < maxTokensRetries && ChatModeSettings.IsMaxTokensError(ex))
                            {
                                var currentMax = ChatModeSettings.LastUsedMaxTokens;
                                var maxOutputSourceSt = DiscoverySource.ProbedBoundary;
                                var isParsedSt = ChatModeSettings.TryParseMaxTokensLimit(ex, out var parsedLimit);
                                if (isParsedSt)
                                {
                                    fallbackMaxTokens = parsedLimit;
                                    maxOutputSourceSt = DiscoverySource.ErrorParsed;
                                }
                                else
                                {
                                    var probeConfig = AI.GetActiveConfiguration();
                                    var probeBundleStream = probeConfig != null ? EnsureKernelInitialized(probeConfig) : null;
                                    if (probeConfig != null && probeBundleStream != null && !string.IsNullOrEmpty(probeConfig.ModelId))
                                    {
                                        var probed = await ChatModeSettings.ProbeMaxTokensConcurrentAsync(
                                            async (maxT, probeCt) =>
                                            {
                                                var probeHistory = new ChatHistory("Reply OK");
                                                probeHistory.AddUserMessage("Hi");
                                                var probeSettings = new OpenAIPromptExecutionSettings { MaxTokens = maxT };
                                                var curBundle = EnsureKernelInitialized(probeConfig)
                                                    ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                                var probeResolved = CapabilityServices.DefaultResolver.Resolve(
                                                    providerId: probeConfig.ProviderId,
                                                    modelId: probeConfig.ModelId,
                                                    endpoint: probeConfig.CustomEndpoint,
                                                    userHint: new UserCapabilityHint
                                                    {
                                                        CapabilitiesDetected = probeConfig.CapabilitiesDetected,
                                                        IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(probeConfig.ProviderId, probeConfig.ModelId),
                                                    });
                                                var probeCtx = new AIRequestContext
                                                {
                                                    RunId = runId,
                                                    Config = probeConfig,
                                                    Settings = probeSettings,
                                                    ChatHistory = probeHistory,
                                                    Resolved = probeResolved,
                                                };
                                                probeCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = curBundle.ProviderType ?? GetCurrentProviderType();
                                                probeCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(probeConfig);
                                                await pipeline.RunStageAsync(probeCtx, MiddlewareStage.TransformSettings, probeCt).ConfigureAwait(false);
                                                await curBundle.ChatService.GetChatMessageContentAsync(probeHistory, probeSettings, curBundle.Kernel, probeCt).ConfigureAwait(false);
                                            },
                                            probeConfig.ModelId, probeConfig.CustomEndpoint, probeConfig.ProviderId,
                                            cancellationToken).ConfigureAwait(false);

                                        if (probed.HasValue)
                                        {
                                            fallbackMaxTokens = probed.Value;
                                            maxOutputSourceSt = DiscoverySource.ProbedExact;
                                        }
                                        else
                                            fallbackMaxTokens = currentMax > 0
                                                ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                                : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);
                                    }
                                    else
                                    {
                                        fallbackMaxTokens = currentMax > 0
                                            ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                            : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);
                                    }
                                }

                                if (currentMax > 0 && fallbackMaxTokens.HasValue && fallbackMaxTokens.Value >= currentMax)
                                {
                                    LogIfPublic(streamConfig, $"[SKChatService] max_tokens min reached: {currentMax}");
                                }
                                else
                                {
                                    tokenRetryCount++;
                                    LogIfPublic(streamConfig, $"[SKChatService] max_tokens retry #{tokenRetryCount}: {currentMax} -> {(fallbackMaxTokens?.ToString() ?? "null(AUTO)")}");

                                    if (fallbackMaxTokens.HasValue && fallbackMaxTokens.Value > 0)
                                    {
                                        var retryConfig = AI.GetActiveConfiguration();
                                        if (retryConfig != null && !string.IsNullOrEmpty(retryConfig.ModelId))
                                            ChatModeSettings.RecordDiscoveredMaxOutput(retryConfig.ModelId, fallbackMaxTokens.Value, retryConfig.CustomEndpoint, retryConfig.ProviderId, maxOutputSourceSt);
                                    }

                                    if (fallbackContextWindow.HasValue && fallbackContextWindow.Value > 0)
                                    {
                                        async Task<bool> TryCompressAsync(int contextWindow)
                                        {
                                            try
                                            {
                                                var config = AI.GetActiveConfiguration();
                                                if (config == null || string.IsNullOrEmpty(config.ModelId))
                                                {
                                                    return false;
                                                }

                                                _chatHistory = await _compression.CompressChatHistoryAsync(
                                                    _chatHistory,
                                                    config.ModelId,
                                                    contextWindow,
                                                    cancellationToken).ConfigureAwait(false);
                                                _isSessionCompressed = true;
                                                return true;
                                            }
                                            catch (Exception ex)
                                            {
                                                DebugLogOnce("TryCompressAsync", ex);
                                                return false;
                                            }
                                        }

                                        await TryCompressAsync(fallbackContextWindow.Value).ConfigureAwait(false);
                                    }

                                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                    {
                                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                    }

                                    RetryFallbackMiddleware.MarkRetry(pipelineCtx, "max_tokens_retry");
                                    continue;
                                }
                            }

                            if (tokenRetryCount < maxTokensRetries && ChatModeSettings.IsContextWindowError(ex))
                            {
                                var ctxConfig = AI.GetActiveConfiguration();
                                int discoveredCw = 0;
                                if (ChatModeSettings.TryParseContextWindowLimit(ex, out var parsedCw))
                                    discoveredCw = parsedCw;

                                if (discoveredCw > 0 && ctxConfig != null && !string.IsNullOrEmpty(ctxConfig.ModelId))
                                {
                                    ChatModeSettings.RecordDiscoveredContextWindow(ctxConfig.ModelId, discoveredCw, ctxConfig.CustomEndpoint, ctxConfig.ProviderId, DiscoverySource.ErrorParsed);
                                    fallbackContextWindow = discoveredCw;
                                }
                                else if (fallbackContextWindow.HasValue)
                                {
                                    fallbackContextWindow = Math.Max(4096, fallbackContextWindow.Value / 2);
                                }
                                else
                                {
                                    var knownCw = GetModelContextWindow(ctxConfig?.ModelId ?? string.Empty);
                                    if (knownCw > 0)
                                        fallbackContextWindow = Math.Max(4096, knownCw * 4 / 5);
                                    else
                                    {
                                        var inputEst = TM.Framework.Common.Helpers.TokenEstimator.CountTokens(_chatHistory);
                                        fallbackContextWindow = Math.Max(4096, (int)(inputEst * 0.8));
                                    }
                                }

                                if (fallbackContextWindow.HasValue && fallbackContextWindow.Value > 0)
                                {
                                    try
                                    {
                                        var cfgCw = ctxConfig ?? AI.GetActiveConfiguration();
                                        if (cfgCw != null)
                                        {
                                            _chatHistory = await _compression.CompressChatHistoryAsync(
                                                _chatHistory, cfgCw.ModelId, fallbackContextWindow.Value, cancellationToken).ConfigureAwait(false);
                                            _isSessionCompressed = true;
                                            LogIfPublic(cfgCw, $"[SKChatService] context_window压缩完成，窗口={fallbackContextWindow.Value}");
                                        }
                                    }
                                    catch (Exception compEx) { DebugLogOnce("CwCompress", compEx); }
                                }

                                fallbackMaxTokens = null;

                                tokenRetryCount++;
                                if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                    _chatHistory.RemoveAt(_chatHistory.Count - 1);
                                RetryFallbackMiddleware.MarkRetry(pipelineCtx, "context_window_retry");
                                continue;
                            }

                            LogIfPublic(streamConfig, $"[SKChatService] 流式错误: {ex}");
                            if (ex is NotSupportedException && ex.Message.Contains("reasoning effort"))
                                NotifyRealError("推理参数格式错误", "当前端点不支持通过此方式注入推理强度参数，请在模型配置中取消勾选「支持推理参数」或切换到 OpenRouter 端点。", streamConfig?.ProviderId);
                            else if (IsStreamNetworkError(ex))
                                NotifyRealError("流式连接中断", "流式传输过程中网络/端点连接被中断，已自动尝试切换为标准模式重试。可稍后重试或更换端点。", streamConfig?.ProviderId);
                            else
                                NotifyRealError("AI 流式请求失败", ex.Message, streamConfig?.ProviderId);

                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                            {
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            }

                            try
                            {
                                await pipeline.RunStageAsync(
                                    pipelineCtx with { Error = ex },
                                    MiddlewareStage.OnError,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception pipelineEx)
                            {
                                DebugLogOnce("PipelineOnError", pipelineEx);
                            }

                            ExecutionEventHub.Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                RunType = _currentRunType,
                                EventType = ExecutionEventType.RunFailed,
                                Title = "错误",
                                Detail = ex.ToString(),
                                Succeeded = false
                            });

                            RecordStreamCall(success: false, errMsg: ex.Message);
                            return $"[错误] {ex.Message}";
                        }
                        finally
                        {
                            try { streamReleaseHandle.Dispose(); } catch { }

                            if (localCts != null)
                            {
                                Interlocked.CompareExchange(ref _streamCts, null, localCts);
                                localCts.Dispose();
                            }
                        }
                    }
                }
                catch (AlreadyNotifiedApiException ane)
                {
                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.RunFailed,
                        Title = "错误",
                        Detail = ane.Message,
                        Succeeded = false
                    });
                    PlanModeFilter.ResetRun(runId);
                    return $"[错误] {ane.Message}";
                }

                try
                {
                    var unknownEx = new InvalidOperationException("流式发送未知错误（未命中 catch 兑底）");
                    await pipeline.RunStageAsync(pipelineCtx with { Error = unknownEx }, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-StreamUnknown", pipelineEx); }
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "未知错误",
                    Detail = "[错误] 未知错误",
                    Succeeded = false
                });
                return "[错误] 未知错误";
            }
            finally
            {
                System.Threading.Interlocked.CompareExchange(ref _streamCts, null, outerCts);
                outerCts.Dispose();
            }
        }
    }
}

