#pragma warning disable SKEXP0130

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.Middleware;
using TM.Services.Framework.AI.Middleware.Builtins;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Discovery;
using System.Diagnostics;
using TM.Services.Framework.AI.Monitoring;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        #region Chat API 实现

        public async Task<string> SendMessageAsync(string displayText, string promptForModel)
        {
            return await SendMessageAsync(displayText, promptForModel, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<string> SendMessageAsync(string displayText, string promptForModel, CancellationToken cancellationToken)
        {
            await InitializedAsync.ConfigureAwait(false);

            var runId = ShortIdGenerator.NewGuid();
            LastRunId = runId;
            var mode = _currentMode;

            CancellationTokenSource? localCts = null;
            var sw = Stopwatch.StartNew();

            AIRequestContext? pipelineCtx = null;
            var pipeline = CapabilityServices.DefaultPipeline;
            int inTokens = 0, outTokens = 0;

            try
            {
                var bundle = EnsureKernelInitialized();
                if (bundle == null)
                {
                    GlobalToast.Error("AI 服务未配置", "请先前往“智能助手 > 模型管理”完成 API Key 配置。");
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var oldChatCts = System.Threading.Interlocked.Exchange(ref _chatCts, localCts);
                try { oldChatCts?.Cancel(); } catch { }

                EnsureSystemPrompt(null);

                await EnsureCompressionIfNeededAsync(promptForModel, localCts.Token).ConfigureAwait(false);

                _chatHistory.AddUserMessage(promptForModel);

                LogIfPublic(null, $"[SKChatService] 发送消息: {displayText.Substring(0, Math.Min(50, displayText.Length))}...");

                var sendCfg = AI.GetActiveConfiguration();
                if (sendCfg != null)
                {
                    var sendResolved = CapabilityServices.DefaultResolver.Resolve(
                        providerId: sendCfg.ProviderId,
                        modelId: sendCfg.ModelId,
                        endpoint: sendCfg.CustomEndpoint,
                        userHint: new UserCapabilityHint
                        {
                            CapabilitiesDetected = sendCfg.CapabilitiesDetected,
                            IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(sendCfg.ProviderId, sendCfg.ModelId),
                        });
                    pipelineCtx = new AIRequestContext
                    {
                        RunId = runId,
                        Config = sendCfg,
                        ChatHistory = _chatHistory,
                        Resolved = sendResolved,
                    };
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = GetCurrentProviderType();
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(sendCfg);
                    await pipeline.RunStageAsync(pipelineCtx, MiddlewareStage.BeforeRequest, localCts.Token).ConfigureAwait(false);
                }

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunStarted,
                    Title = displayText.Length > 30 ? displayText[..30] : displayText,
                    Detail = displayText
                });

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.UserMessage,
                    Title = "User",
                    Detail = promptForModel
                });

                const int maxTokensRetries = 5;
                int tokenRetryCount = 0;
                int? fallbackMaxTokens = null;

                while (true)
                {
                    PromptExecutionSettings? lastSettings = null;

                    try
                    {
                        var response = await InvokeApiWithRotationAsync(
                            async (bundle, innerCt) =>
                            {
                                var settings = GetCurrentModeSettings(fallbackMaxTokens, bundle.Kernel);
                                if (pipelineCtx != null)
                                {
                                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(sendCfg);
                                    await pipeline.RunStageAsync(pipelineCtx with { Settings = settings }, MiddlewareStage.TransformSettings, innerCt).ConfigureAwait(false);
                                }
                                lastSettings = settings;
                                return await bundle.ChatService.GetChatMessageContentAsync(
                                    _chatHistory, settings, bundle.Kernel, innerCt).ConfigureAwait(false);
                            },
                            localCts.Token).ConfigureAwait(false);

                        sw.Stop();
                        var content = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(response.Content ?? string.Empty);

                        try
                        {
                            var activeCfg = AI.GetActiveConfiguration();
                            if (activeCfg != null && lastSettings != null)
                                ChatModeSettings.RecordSuccessObservation(activeCfg, _chatHistory, lastSettings, content);
                        }
                        catch (Exception ex) { DebugLogOnce("RecordSuccessObs-Adaptive", ex); }

                        var (cleanedContent, _, _) = CleanNonStreamContent(content);
                        if (!string.IsNullOrWhiteSpace(cleanedContent))
                            content = cleanedContent;

                        _chatHistory.AddAssistantMessage(content);

                        LogIfPublic(null, $"[SKChatService] 收到响应: {content.Substring(0, Math.Min(50, content.Length))}...");

                        var cfg = AI.GetActiveConfiguration();
                        (inTokens, outTokens) = TryExtractTokenUsage(response);
                        _statistics.RecordCall(new ApiCallRecord
                        {
                            Timestamp = DateTime.Now,
                            ModelName = cfg?.ModelId ?? "unknown",
                            Provider = cfg?.ProviderId ?? "Chat",
                            Success = true,
                            ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                            InputTokens = inTokens,
                            OutputTokens = outTokens
                        });

                        if (inTokens > 0 || outTokens > 0)
                            AIChunkBus.Publish(new UsageChunk(inTokens, outTokens) { RunId = runId });

                        ExecutionEventHub.Publish(new ExecutionEvent
                        {
                            RunId = runId,
                            Mode = mode,
                            RunType = _currentRunType,
                            EventType = ExecutionEventType.AssistantMessage,
                            Title = "Assistant",
                            Detail = content,
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

                        PlanModeFilter.ResetRun(runId);

                        if (pipelineCtx != null)
                        {
                            await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = content }, MiddlewareStage.AfterResponse, localCts?.Token ?? cancellationToken).ConfigureAwait(false);
                        }

                        return content;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                        && tokenRetryCount < maxTokensRetries
                        && (ChatModeSettings.IsUnsupportedParameterError(ex) || ChatModeSettings.IsMaxTokensError(ex)
                            || ChatModeSettings.IsContextWindowError(ex)
                            || ChatModeSettings.IsLongContextRejectedError(ex, AI.GetActiveConfiguration())))
                    {
                        var longCtxCfg = AI.GetActiveConfiguration();
                        if (ChatModeSettings.IsLongContextRejectedError(ex, longCtxCfg)
                            && longCtxCfg != null && !string.IsNullOrEmpty(longCtxCfg.ModelId))
                        {
                            ChatModeSettings.MarkUnsupportedParam(
                                longCtxCfg.ProviderId, longCtxCfg.CustomEndpoint, longCtxCfg.ModelId, "long_context");
                            longCtxCfg.EnableLongContext = null;
                            try { AI.UpdateConfiguration(longCtxCfg); } catch { }
                            tokenRetryCount++;
                            LogIfPublic(longCtxCfg, $"[SKChatService] 1M 上下文请求被拒，已回退到基线窗口并重试: {longCtxCfg.ModelId}");
                            GlobalToast.Warning("1M 上下文已回退", "端点未接受 1M 参数，已自动回退并重试");
                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            continue;
                        }

                        if (ChatModeSettings.TryParseUnsupportedParamName(ex, out var unsupParamName))
                        {
                            var unsupCfg = AI.GetActiveConfiguration();
                            if (unsupCfg != null && !string.IsNullOrEmpty(unsupCfg.ModelId))
                                ChatModeSettings.MarkUnsupportedParam(unsupCfg.ProviderId, unsupCfg.CustomEndpoint, unsupCfg.ModelId, unsupParamName);
                            if (unsupParamName.Contains("max_tokens") || unsupParamName.Contains("max_output") || unsupParamName.Contains("max_completion"))
                                fallbackMaxTokens = null;
                            tokenRetryCount++;
                            LogIfPublic(unsupCfg, $"[SKChatService] SendMessage 端点不支持参数 '{unsupParamName}'，标记并重试");
                        }
                        else if (ChatModeSettings.IsContextWindowError(ex))
                        {
                            var ctxCfg = AI.GetActiveConfiguration();
                            if (ChatModeSettings.TryParseContextWindowLimit(ex, out var parsedCw)
                                && ctxCfg != null && !string.IsNullOrEmpty(ctxCfg.ModelId))
                                ChatModeSettings.RecordDiscoveredContextWindow(ctxCfg.ModelId, parsedCw, ctxCfg.CustomEndpoint, ctxCfg.ProviderId, DiscoverySource.ErrorParsed);

                            var cwForCompress = ChatModeSettings.GetDiscoveredContextWindow(ctxCfg?.ModelId ?? string.Empty, ctxCfg?.CustomEndpoint, ctxCfg?.ProviderId);
                            if (cwForCompress <= 0)
                            {
                                var knownCw = GetModelContextWindow(ctxCfg?.ModelId ?? string.Empty);
                                cwForCompress = knownCw > 0
                                    ? Math.Max(4096, knownCw * 4 / 5)
                                    : Math.Max(4096, (int)(TM.Framework.Common.Helpers.TokenEstimator.CountTokens(_chatHistory) * 0.8));
                            }
                            try
                            {
                                if (ctxCfg != null && !string.IsNullOrEmpty(ctxCfg.ModelId))
                                {
                                    _chatHistory = await _compression.CompressChatHistoryAsync(
                                        _chatHistory, ctxCfg.ModelId, cwForCompress, cancellationToken).ConfigureAwait(false);
                                    _isSessionCompressed = true;
                                }
                            }
                            catch (Exception compEx) { DebugLogOnce("CwCompress-SendMsg", compEx); }
                            fallbackMaxTokens = null;
                            tokenRetryCount++;
                            LogIfPublic(ctxCfg, $"[SKChatService] SendMessage context_window retry #{tokenRetryCount}, cw={cwForCompress}");
                        }
                        else
                        {
                            var currentMax = ChatModeSettings.LastUsedMaxTokens;
                            var isParsedSm = ChatModeSettings.TryParseMaxTokensLimit(ex, out var parsedLimit);
                            if (isParsedSm)
                                fallbackMaxTokens = parsedLimit;
                            else
                                fallbackMaxTokens = currentMax > 0
                                    ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                    : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);

                            if (currentMax > 0 && fallbackMaxTokens.HasValue && fallbackMaxTokens.Value >= currentMax)
                                throw;

                            if (fallbackMaxTokens.HasValue && fallbackMaxTokens.Value > 0)
                            {
                                var c = AI.GetActiveConfiguration();
                                if (c != null && !string.IsNullOrEmpty(c.ModelId))
                                    ChatModeSettings.RecordDiscoveredMaxOutput(c.ModelId, fallbackMaxTokens.Value, c.CustomEndpoint, c.ProviderId,
                                        isParsedSm ? DiscoverySource.ErrorParsed : DiscoverySource.ProbedBoundary);
                            }
                            tokenRetryCount++;
                            LogIfPublic(null, $"[SKChatService] SendMessage max_tokens retry #{tokenRetryCount}: {currentMax} -> {fallbackMaxTokens}");
                        }
                    }
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-SendMessage", pipelineEx); }
                }
                if (localCts?.IsCancellationRequested == true)
                {
                    LogIfPublic(null, "[SKChatService] 请求已被 CancelCurrentRequest 取消");
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
                    PlanModeFilter.ResetRun(runId);
                    return "[已取消]";
                }
                var cfg = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg?.ModelId ?? "unknown", Provider = cfg?.ProviderId ?? "Chat", Success = false, ResponseTimeMs = (int)sw.ElapsedMilliseconds, InputTokens = inTokens, OutputTokens = outTokens, ErrorMessage = "请求超时" });
                if (inTokens > 0 || outTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(inTokens, outTokens) { RunId = runId });
                LogIfPublic(cfg, $"[SKChatService] 请求超时或被底层取消: {ex.Message}");
                GlobalToast.Warning("请求超时", "请检查网络或代理连接后重试");
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
                PlanModeFilter.ResetRun(runId);
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException ex)
            {
                LogIfPublic(null, "[SKChatService] 请求已取消");
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-SendMessage2", pipelineEx); }
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
                PlanModeFilter.ResetRun(runId);
                return "[已取消]";
            }
            catch (Exception ex)
            {
                sw.Stop();
                var cfg = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg?.ModelId ?? "unknown", Provider = cfg?.ProviderId ?? "Chat", Success = false, ResponseTimeMs = (int)sw.ElapsedMilliseconds, InputTokens = inTokens, OutputTokens = outTokens, ErrorMessage = ex.Message });
                if (inTokens > 0 || outTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(inTokens, outTokens) { RunId = runId });
                LogIfPublic(cfg, $"[SKChatService] 错误: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 请求失败", ex.Message, cfg?.ProviderId);

                if (pipelineCtx != null)
                {
                    try
                    {
                        await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception pipelineEx)
                    {
                        DebugLogOnce("PipelineOnError-SendMessage", pipelineEx);
                    }
                }

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "错误",
                    Detail = IsTianmingPrivateProvider(cfg?.ProviderId) ? "请求失败" : ex.Message,
                    Succeeded = false
                });
                PlanModeFilter.ResetRun(runId);
                return IsTianmingPrivateProvider(cfg?.ProviderId) ? "[错误] 请求失败" : $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    Interlocked.CompareExchange(ref _chatCts, null, localCts);
                    localCts.Dispose();
                }
            }
        }

        public async Task<string> GenerateWithChatHistoryAsync(ChatHistory history, string userPrompt, CancellationToken cancellationToken = default)
            => await GenerateWithChatHistoryAsync(history, userPrompt, null, cancellationToken, null).ConfigureAwait(false);

        public async Task<string> GenerateWithChatHistoryAsync(ChatHistory history, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken = default)
            => await GenerateWithChatHistoryAsync(history, userPrompt, progress, cancellationToken, null).ConfigureAwait(false);

        public async Task<string> GenerateWithChatHistoryAsync(ChatHistory history, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken, UserConfiguration? overrideConfig)
        {
            ArgumentNullException.ThrowIfNull(history);
            using var _progressRunScope = GenerationProgressHub.BeginRun(Guid.Empty);

            await InitializedAsync.ConfigureAwait(false);

            CancellationTokenSource? localCts = null;
            var swBizOuter = Stopwatch.StartNew();

            var bizRunId = TM.Framework.Common.Helpers.Id.ShortIdGenerator.NewGuid();
            AIRequestContext? pipelineCtx = null;
            var pipeline = CapabilityServices.DefaultPipeline;
            int bizInTokens = 0, bizOutTokens = 0;
            UserConfiguration? config = null;

            try
            {
                var initialBundle = EnsureKernelInitialized(overrideConfig);
                if (initialBundle == null)
                {
                    GlobalToast.Error("AI 服务未配置", "请先前往“智能助手 > 模型管理”完成 API Key 配置。");
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                System.Threading.Interlocked.Exchange(ref _businessCts, localCts);

                config = overrideConfig ?? AI.GetActiveConfiguration();
                if (config == null || string.IsNullOrEmpty(config.ModelId))
                {
                    GlobalToast.Error("AI 服务未配置", "当前配置缺少有效的模型信息，请检查模型管理中的默认模型设置。");
                    return "[错误] 当前没有激活的AI模型";
                }

                var bizResolved = CapabilityServices.DefaultResolver.Resolve(
                    providerId: config.ProviderId,
                    modelId: config.ModelId,
                    endpoint: config.CustomEndpoint,
                    userHint: new UserCapabilityHint
                    {
                        CapabilitiesDetected = config.CapabilitiesDetected,
                        IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(config.ProviderId, config.ModelId),
                    });
                pipelineCtx = new AIRequestContext
                {
                    RunId = bizRunId,
                    Config = config,
                    ChatHistory = history,
                    Resolved = bizResolved,
                };
                pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = initialBundle.ProviderType ?? GetCurrentProviderType();
                pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(config);
                await pipeline.RunStageAsync(pipelineCtx, MiddlewareStage.BeforeRequest, localCts.Token).ConfigureAwait(false);

                var (compressedHistory, compressed) = await _compression.EnsureCompressionIfNeededAsync(
                    history,
                    config.ModelId,
                    userPrompt,
                    localCts.Token).ConfigureAwait(false);

                if (compressed)
                {
                    LogIfPublic(config, $"[SKChatService] 业务会话触发压缩: model={config.ModelId}, historyCount={history.Count}");

                    history.Clear();
                    foreach (var message in compressedHistory)
                    {
                        history.Add(message);
                    }

                }

                history.AddUserMessage(userPrompt);

                {
                    var preFlightCw = GetModelContextWindow(config.ModelId ?? string.Empty);
                    if (preFlightCw > 0)
                    {
                        var (preFlightInput, _, _) = _compression.GetContextUsage(history, config.ModelId ?? string.Empty, null, preFlightCw);
                        if (preFlightInput > (int)(preFlightCw * 0.97))
                        {
                            LogIfPublic(config, $"[SKChatService] 发送前预检：input({preFlightInput}) > contextWindow({preFlightCw})×0.97，强制压缩");
                            var preFlightResult = await _compression.CompressChatHistoryAsync(
                                history, config.ModelId ?? string.Empty, preFlightCw, localCts.Token).ConfigureAwait(false);
                            history.Clear();
                            foreach (var m in preFlightResult) history.Add(m);
                        }
                    }
                }

                const int maxTokensRetriesBiz = 5;
                int tokenRetryCountBiz = 0;
                int? fallbackMaxTokensBiz = null;

                while (true)
                {
                    var settings = ChatModeSettings.GetExecutionSettings(
                        TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Business, history,
                        overrideMaxTokens: fallbackMaxTokensBiz, overrideConfig: overrideConfig);

                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(config);
                    await pipeline.RunStageAsync(pipelineCtx with { Settings = settings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);

                    if (InfoLogDedup.ShouldLog($"GenerateWithChatHistory:start:{config.ModelId}"))
                    {
                        LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 调用开始: model={config.ModelId}, historyCount={history.Count}, promptLen={userPrompt.Length}, maxTokens={fallbackMaxTokensBiz?.ToString() ?? "AUTO"}");
                    }
                    try
                    {
                        var swBizStartTime = DateTime.Now;
                        var swBiz = Stopwatch.StartNew();
                        var bizResult = await InvokeApiWithRotationAsync(
                            async (bundle, innerCt) => await AdaptiveGenerateAsync(history, settings, progress, innerCt, overrideConfig, bundle).ConfigureAwait(false),
                            localCts.Token,
                            config: config,
                            progress: progress).ConfigureAwait(false);
                        swBiz.Stop();
                        var content = bizResult.Content;
                        bizInTokens = bizResult.InputTokens;
                        bizOutTokens = bizResult.OutputTokens;

                        var (isBizCancelled, _) = UIMessageItem.TryExtractCancelledPartial(content);
                        if (!string.IsNullOrWhiteSpace(content)
                            && (content.StartsWith("[错误]", StringComparison.Ordinal) || isBizCancelled))
                        {
                            if (history.Count > 0 && history[^1].Role == AuthorRole.User)
                                history.RemoveAt(history.Count - 1);
                            _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = config.ModelId ?? "unknown", Provider = config.ProviderId ?? "Business", Success = false, ResponseTimeMs = (int)swBiz.ElapsedMilliseconds, InputTokens = bizInTokens, OutputTokens = bizOutTokens, ErrorMessage = content });
                            return content;
                        }

                        const int maxBizLengthCont = 2;
                        for (int lc = 0;
                             lc < maxBizLengthCont
                             && ChatModeSettings.IsFinishReasonTruncated(ChatModeSettings.LastFinishReason)
                             && !cancellationToken.IsCancellationRequested; lc++)
                        {
                            var upgradedMax = ChatModeSettings.GetUpgradeMaxTokens(ChatModeSettings.LastUsedMaxTokens, config.ModelId, config.CustomEndpoint, config.ProviderId);
                            if (!upgradedMax.HasValue)
                            {
                                LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory finish_reason=length 已在梯队顶端，放弃续写");
                                break;
                            }
                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory finish_reason=length，续写#{lc + 1}: {ChatModeSettings.LastUsedMaxTokens} -> {upgradedMax.Value}");
                            history.AddAssistantMessage(content);
                            history.AddUserMessage("请继续");
                            var contSettingsBiz = ChatModeSettings.GetExecutionSettings(
                                TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Business, history,
                                overrideMaxTokens: upgradedMax.Value, overrideConfig: overrideConfig);
                            if (contSettingsBiz is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings oaiCsBiz)
                                oaiCsBiz.FunctionChoiceBehavior = null;
                            var contBundleBiz = EnsureKernelInitialized(config);
                            pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = contBundleBiz?.ProviderType ?? GetCurrentProviderType();
                            pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(config);
                            await pipeline.RunStageAsync(pipelineCtx with { Settings = contSettingsBiz }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                            string contContentBiz;
                            try
                            {
                                var contResultBiz = await InvokeApiWithRotationAsync(
                                    async (bundle, innerCt) => await AdaptiveGenerateAsync(history, contSettingsBiz, progress, innerCt, overrideConfig, bundle).ConfigureAwait(false),
                                    localCts.Token, config: config, progress: progress).ConfigureAwait(false);
                                contContentBiz = contResultBiz.Content;
                                bizInTokens += contResultBiz.InputTokens;
                                bizOutTokens += contResultBiz.OutputTokens;
                            }
                            catch (Exception contEx)
                            {
                                LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 续写异常: {contEx.Message}");
                                if (history.Count >= 2
                                    && history[^1].Role == AuthorRole.User
                                    && history[^2].Role == AuthorRole.Assistant)
                                {
                                    history.RemoveAt(history.Count - 1);
                                    history.RemoveAt(history.Count - 1);
                                }
                                break;
                            }
                            if (history.Count >= 2
                                && history[^1].Role == AuthorRole.User
                                && history[^2].Role == AuthorRole.Assistant)
                            {
                                history.RemoveAt(history.Count - 1);
                                history.RemoveAt(history.Count - 1);
                            }
                            contContentBiz = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(contContentBiz);
                            var (isBizContCancelled, _) = UIMessageItem.TryExtractCancelledPartial(contContentBiz);
                            if (!string.IsNullOrWhiteSpace(contContentBiz)
                                && (contContentBiz.StartsWith("[错误]", StringComparison.Ordinal) || isBizContCancelled))
                            {
                                LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 续写返回错误/取消，停止续写: {contContentBiz}");
                                break;
                            }
                            if (!string.IsNullOrEmpty(contContentBiz))
                                content = content + "\n\n" + contContentBiz;
                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 续写完成，追加 {contContentBiz.Length} 字符，finish_reason={ChatModeSettings.LastFinishReason ?? "(null)"}");
                        }

                        history.AddAssistantMessage(content);
                        _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = config.ModelId ?? "unknown", Provider = config.ProviderId ?? "Business", Success = true, ResponseTimeMs = (int)swBiz.ElapsedMilliseconds, InputTokens = bizInTokens, OutputTokens = bizOutTokens, FirstTokenMs = bizResult.FirstTokenMs, TokensPerSecond = bizResult.TokensPerSecond });

                        if (bizInTokens > 0 || bizOutTokens > 0)
                            AIChunkBus.Publish(new UsageChunk(bizInTokens, bizOutTokens) { RunId = bizRunId });

                        if (bizResult.FirstTokenMs > 0)
                            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.ReportFirstToken(
                                bizRunId, swBizStartTime.AddMilliseconds(bizResult.FirstTokenMs));

                        await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = content }, MiddlewareStage.AfterResponse, localCts.Token).ConfigureAwait(false);

                        return content;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                        && tokenRetryCountBiz < maxTokensRetriesBiz
                        && (ChatModeSettings.IsUnsupportedParameterError(ex) || ChatModeSettings.IsMaxTokensError(ex)
                            || ChatModeSettings.IsContextWindowError(ex)
                            || ChatModeSettings.IsLongContextRejectedError(ex, config)))
                    {
                        if (ChatModeSettings.IsLongContextRejectedError(ex, config)
                            && !string.IsNullOrEmpty(config.ModelId))
                        {
                            ChatModeSettings.MarkUnsupportedParam(config.ProviderId, config.CustomEndpoint, config.ModelId, "long_context");
                            config.EnableLongContext = null;
                            try { AI.UpdateConfiguration(config); } catch { }
                            tokenRetryCountBiz++;
                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 1M 上下文请求被拒，已回退到基线窗口并重试: {config.ModelId}");
                            GlobalToast.Warning("1M 上下文已回退", "端点未接受 1M 参数，已自动回退并重试");
                            continue;
                        }

                        if (ChatModeSettings.TryParseUnsupportedParamName(ex, out var unsupParamNameBiz))
                        {
                            if (!string.IsNullOrEmpty(config.ModelId))
                                ChatModeSettings.MarkUnsupportedParam(config.ProviderId, config.CustomEndpoint, config.ModelId, unsupParamNameBiz);
                            if (unsupParamNameBiz.Contains("max_tokens") || unsupParamNameBiz.Contains("max_output") || unsupParamNameBiz.Contains("max_completion"))
                                fallbackMaxTokensBiz = null;
                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory 端点不支持参数 '{unsupParamNameBiz}'，标记并重试");
                            GenerationProgressHub.Report($"端点不支持参数 {unsupParamNameBiz}，调整后重试...");
                        }
                        else if (ChatModeSettings.IsContextWindowError(ex))
                        {
                            if (ChatModeSettings.TryParseContextWindowLimit(ex, out var parsedCwBiz)
                                && !string.IsNullOrEmpty(config.ModelId))
                                ChatModeSettings.RecordDiscoveredContextWindow(config.ModelId, parsedCwBiz, config.CustomEndpoint, config.ProviderId, DiscoverySource.ErrorParsed);

                            var cwBiz = ChatModeSettings.GetDiscoveredContextWindow(config.ModelId ?? string.Empty, config.CustomEndpoint, config.ProviderId);
                            if (cwBiz <= 0)
                            {
                                var knownCwBiz = GetModelContextWindow(config.ModelId ?? string.Empty);
                                if (knownCwBiz > 0)
                                    cwBiz = Math.Max(4096, knownCwBiz * 4 / 5);
                                else
                                {
                                    var inputEst = TM.Framework.Common.Helpers.TokenEstimator.CountTokens(history);
                                    cwBiz = Math.Max(4096, (int)(inputEst * 0.8));
                                }
                            }
                            if (cwBiz > 0)
                            {
                                try
                                {
                                    var bizModelId = config.ModelId;
                                    if (!string.IsNullOrEmpty(bizModelId))
                                    {
                                        var compressedBiz = await _compression.CompressChatHistoryAsync(
                                            history, bizModelId, cwBiz, cancellationToken).ConfigureAwait(false);

                                        history.Clear();
                                        foreach (var message in compressedBiz)
                                        {
                                            history.Add(message);
                                        }
                                    }
                                }
                                catch (Exception compEx) { DebugLogOnce("CwCompressBiz", compEx); }
                            }

                            fallbackMaxTokensBiz = null;
                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory context_window retry #{tokenRetryCountBiz + 1}, cw={cwBiz}");
                            GenerationProgressHub.Report($"上下文超限，已压缩上下文到 {cwBiz} token 后重试...");
                        }
                        else
                        {
                            var currentMax = ChatModeSettings.LastUsedMaxTokens;
                            var maxOutputSourceBiz = DiscoverySource.ProbedBoundary;
                            var isParsedBiz = ChatModeSettings.TryParseMaxTokensLimit(ex, out var parsedLimitBiz);
                            if (isParsedBiz)
                            {
                                fallbackMaxTokensBiz = parsedLimitBiz;
                                maxOutputSourceBiz = DiscoverySource.ErrorParsed;
                            }
                            else if (!string.IsNullOrEmpty(config.ModelId))
                            {
                                var probeBundle = EnsureKernelInitialized(config);
                                if (probeBundle != null)
                                {
                                    var probedBiz = await ChatModeSettings.ProbeMaxTokensConcurrentAsync(
                                        async (maxT, probeCt) =>
                                        {
                                            var ph = new ChatHistory("Reply OK");
                                            ph.AddUserMessage("Hi");
                                            var ps = new OpenAIPromptExecutionSettings { MaxTokens = maxT };
                                            var curBundle = EnsureKernelInitialized(config)
                                                ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                            var probeResolved = CapabilityServices.DefaultResolver.Resolve(
                                                providerId: config.ProviderId,
                                                modelId: config.ModelId,
                                                endpoint: config.CustomEndpoint,
                                                userHint: new UserCapabilityHint
                                                {
                                                    CapabilitiesDetected = config.CapabilitiesDetected,
                                                    IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(config.ProviderId, config.ModelId),
                                                });
                                            var probeCtx = new AIRequestContext
                                            {
                                                RunId = bizRunId,
                                                Config = config,
                                                Settings = ps,
                                                ChatHistory = ph,
                                                Resolved = probeResolved,
                                            };
                                            probeCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = curBundle.ProviderType ?? GetCurrentProviderType();
                                            probeCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(config);
                                            await pipeline.RunStageAsync(probeCtx, MiddlewareStage.TransformSettings, probeCt).ConfigureAwait(false);
                                            await curBundle.ChatService.GetChatMessageContentAsync(ph, ps, curBundle.Kernel, probeCt).ConfigureAwait(false);
                                        },
                                        config.ModelId, config.CustomEndpoint, config.ProviderId,
                                        cancellationToken).ConfigureAwait(false);

                                    if (probedBiz.HasValue)
                                        maxOutputSourceBiz = DiscoverySource.ProbedExact;
                                    fallbackMaxTokensBiz = probedBiz
                                        ?? (currentMax > 0
                                            ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                            : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop));
                                }
                            }
                            else
                            {
                                fallbackMaxTokensBiz = currentMax > 0
                                    ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                    : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);
                            }

                            if (fallbackMaxTokensBiz.HasValue && fallbackMaxTokensBiz.Value > 0
                                && !string.IsNullOrEmpty(config.ModelId))
                                ChatModeSettings.RecordDiscoveredMaxOutput(config.ModelId, fallbackMaxTokensBiz.Value, config.CustomEndpoint, config.ProviderId,
                                    maxOutputSourceBiz);

                            LogIfPublic(config, $"[SKChatService] GenerateWithChatHistory max_tokens retry #{tokenRetryCountBiz + 1}: {currentMax} -> {fallbackMaxTokensBiz}");
                            GenerationProgressHub.Report($"输出长度超限，调整为 {fallbackMaxTokensBiz?.ToString() ?? "自动"} 后重试...");
                        }
                        tokenRetryCountBiz++;
                    }
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-GenerateWithHistory", pipelineEx); }
                }
                if (localCts?.IsCancellationRequested == true)
                {
                    LogIfPublic(config, "[SKChatService] GenerateWithChatHistoryAsync 请求已被 CancelCurrentRequest 取消");
                    return "[已取消]";
                }
                LogIfPublic(config, $"[SKChatService] GenerateWithChatHistoryAsync 请求超时: {ex.Message}");
                var cfg2 = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg2?.ModelId ?? "unknown", Provider = cfg2?.ProviderId ?? "Business", Success = false, ResponseTimeMs = (int)swBizOuter.ElapsedMilliseconds, InputTokens = bizInTokens, OutputTokens = bizOutTokens, ErrorMessage = "请求超时" });
                if (bizInTokens > 0 || bizOutTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(bizInTokens, bizOutTokens) { RunId = bizRunId });
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException ex)
            {
                LogIfPublic(config, "[SKChatService] GenerateWithChatHistoryAsync 请求已取消");
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-GenerateWithHistory2", pipelineEx); }
                }
                return "[已取消]";
            }
            catch (Exception ex)
            {
                LogIfPublic(config, $"[SKChatService] GenerateWithChatHistoryAsync 错误: {ex.GetType().Name}: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 业务生成失败", ex.Message, config?.ProviderId);
                var cfg2 = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg2?.ModelId ?? "unknown", Provider = cfg2?.ProviderId ?? "Business", Success = false, ResponseTimeMs = (int)swBizOuter.ElapsedMilliseconds, InputTokens = bizInTokens, OutputTokens = bizOutTokens, ErrorMessage = ex.Message });
                if (bizInTokens > 0 || bizOutTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(bizInTokens, bizOutTokens) { RunId = bizRunId });

                if (pipelineCtx != null)
                {
                    try
                    {
                        await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception pipelineEx)
                    {
                        DebugLogOnce("PipelineOnError-GenerateWithHistory", pipelineEx);
                    }
                }

                return IsTianmingPrivateProvider(config?.ProviderId) ? "[错误] 请求失败" : $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    Interlocked.CompareExchange(ref _businessCts, null, localCts);
                    localCts.Dispose();
                }
            }
        }

        public async Task<string> SendSilentMessageAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
        {
            using var _progressRunScope = GenerationProgressHub.BeginRun(Guid.Empty);

            await InitializedAsync.ConfigureAwait(false);

            var swSilent = Stopwatch.StartNew();

            var silentRunId = TM.Framework.Common.Helpers.Id.ShortIdGenerator.NewGuid();
            AIRequestContext? pipelineCtx = null;
            var pipeline = CapabilityServices.DefaultPipeline;
            int silentInTokens = 0, silentOutTokens = 0;

            try
            {
                var initialBundle = EnsureKernelInitialized();
                if (initialBundle == null)
                {
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                var tempHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    tempHistory.AddSystemMessage(systemPrompt);
                }
                var wrappedMessage = $"<user_request>\n{userMessage}\n</user_request>";
                tempHistory.AddUserMessage(wrappedMessage);

                var silentCfg = AI.GetActiveConfiguration();
                if (silentCfg != null)
                {
                    var silentResolved = CapabilityServices.DefaultResolver.Resolve(
                        providerId: silentCfg.ProviderId,
                        modelId: silentCfg.ModelId,
                        endpoint: silentCfg.CustomEndpoint,
                        userHint: new UserCapabilityHint
                        {
                            CapabilitiesDetected = silentCfg.CapabilitiesDetected,
                            IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(silentCfg.ProviderId, silentCfg.ModelId),
                        });
                    pipelineCtx = new AIRequestContext
                    {
                        RunId = silentRunId,
                        Config = silentCfg,
                        ChatHistory = tempHistory,
                        Resolved = silentResolved,
                    };
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = initialBundle.ProviderType ?? GetCurrentProviderType();
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(silentCfg);
                    await pipeline.RunStageAsync(pipelineCtx, MiddlewareStage.BeforeRequest, cancellationToken).ConfigureAwait(false);
                }

                const int maxTokensRetries = 5;
                int tokenRetryCount = 0;
                int? fallbackMaxTokens = null;

                while (true)
                {
                    var settings = ChatModeSettings.GetExecutionSettings(
                        TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Business, tempHistory,
                        overrideMaxTokens: fallbackMaxTokens);

                    if (pipelineCtx != null)
                    {
                        pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(silentCfg);
                        await pipeline.RunStageAsync(pipelineCtx with { Settings = settings }, MiddlewareStage.TransformSettings, cancellationToken).ConfigureAwait(false);
                    }

                    try
                    {
                        var response = await InvokeApiWithRotationAsync(
                            async (bundle, innerCt) =>
                            {
                                return await bundle.ChatService.GetChatMessageContentAsync(
                                    tempHistory, settings, bundle.Kernel, innerCt).ConfigureAwait(false);
                            },
                            cancellationToken).ConfigureAwait(false);

                        var rawContent = response.Content ?? string.Empty;

                        try
                        {
                            var cfg = AI.GetActiveConfiguration();
                            if (cfg != null)
                                ChatModeSettings.RecordSuccessObservation(cfg, tempHistory, settings, rawContent);
                        }
                        catch (Exception ex) { DebugLogOnce("RecordSuccessObs-Biz", ex); }

                        (silentInTokens, silentOutTokens) = TryExtractTokenUsage(response);
                        var silentCfgStat = AI.GetActiveConfiguration();
                        swSilent.Stop();
                        _statistics.RecordCall(new ApiCallRecord
                        {
                            Timestamp = DateTime.Now,
                            ModelName = silentCfgStat?.ModelId ?? "unknown",
                            Provider = silentCfgStat?.ProviderId ?? "Silent",
                            Success = true,
                            ResponseTimeMs = (int)swSilent.ElapsedMilliseconds,
                            InputTokens = silentInTokens,
                            OutputTokens = silentOutTokens
                        });

                        if (silentInTokens > 0 || silentOutTokens > 0)
                            AIChunkBus.Publish(new UsageChunk(silentInTokens, silentOutTokens) { RunId = silentRunId });

                        if (TM.Services.Modules.ProjectData.Implementations.GenerationGate.HasChangesRegion(rawContent))
                        {
                            if (pipelineCtx != null)
                            {
                                await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = rawContent }, MiddlewareStage.AfterResponse, cancellationToken).ConfigureAwait(false);
                            }
                            return rawContent;
                        }

                        var (cleanedSilent, _, _) = CleanNonStreamContent(rawContent);
                        var silentFinal = string.IsNullOrWhiteSpace(cleanedSilent) ? rawContent : cleanedSilent;

                        if (pipelineCtx != null)
                        {
                            await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = silentFinal }, MiddlewareStage.AfterResponse, cancellationToken).ConfigureAwait(false);
                        }
                        return silentFinal;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                        && tokenRetryCount < maxTokensRetries
                        && (ChatModeSettings.IsUnsupportedParameterError(ex) || ChatModeSettings.IsMaxTokensError(ex)
                            || ChatModeSettings.IsLongContextRejectedError(ex, AI.GetActiveConfiguration())))
                    {
                        var longCtxCfgSilent = AI.GetActiveConfiguration();
                        if (ChatModeSettings.IsLongContextRejectedError(ex, longCtxCfgSilent)
                            && longCtxCfgSilent != null && !string.IsNullOrEmpty(longCtxCfgSilent.ModelId))
                        {
                            ChatModeSettings.MarkUnsupportedParam(longCtxCfgSilent.ProviderId, longCtxCfgSilent.CustomEndpoint, longCtxCfgSilent.ModelId, "long_context");
                            longCtxCfgSilent.EnableLongContext = null;
                            try { AI.UpdateConfiguration(longCtxCfgSilent); } catch { }
                            tokenRetryCount++;
                            LogIfPublic(longCtxCfgSilent, $"[SKChatService] SendSilent 1M 上下文请求被拒，已回退到基线窗口并重试: {longCtxCfgSilent.ModelId}");
                            GlobalToast.Warning("1M 上下文已回退", "端点未接受 1M 参数，已自动回退并重试");
                            continue;
                        }

                        if (ChatModeSettings.TryParseUnsupportedParamName(ex, out var unsupParamNameSilent))
                        {
                            var unsupCfg = AI.GetActiveConfiguration();
                            if (unsupCfg != null && !string.IsNullOrEmpty(unsupCfg.ModelId))
                                ChatModeSettings.MarkUnsupportedParam(unsupCfg.ProviderId, unsupCfg.CustomEndpoint, unsupCfg.ModelId, unsupParamNameSilent);
                            if (unsupParamNameSilent.Contains("max_tokens") || unsupParamNameSilent.Contains("max_output") || unsupParamNameSilent.Contains("max_completion"))
                                fallbackMaxTokens = null;
                            tokenRetryCount++;
                            LogIfPublic(unsupCfg, $"[SKChatService] SendSilent 端点不支持参数 '{unsupParamNameSilent}'，标记并重试");
                            continue;
                        }
                        var currentMax = ChatModeSettings.LastUsedMaxTokens;
                        var isParsedSl = ChatModeSettings.TryParseMaxTokensLimit(ex, out var parsedLimit);
                        if (isParsedSl)
                            fallbackMaxTokens = parsedLimit;
                        else
                            fallbackMaxTokens = currentMax > 0
                                ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);

                        if (currentMax > 0 && fallbackMaxTokens.HasValue && fallbackMaxTokens.Value >= currentMax)
                            throw;

                        if (fallbackMaxTokens.HasValue && fallbackMaxTokens.Value > 0)
                        {
                            var c = AI.GetActiveConfiguration();
                            if (c != null && !string.IsNullOrEmpty(c.ModelId))
                                ChatModeSettings.RecordDiscoveredMaxOutput(c.ModelId, fallbackMaxTokens.Value, c.CustomEndpoint, c.ProviderId,
                                    isParsedSl ? DiscoverySource.ErrorParsed : DiscoverySource.ProbedBoundary);
                        }
                        tokenRetryCount++;
                        LogIfPublic(null, $"[SKChatService] SendSilent max_tokens retry #{tokenRetryCount}: {currentMax} -> {fallbackMaxTokens}");
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-Silent", pipelineEx); }
                }
                try
                {
                    swSilent.Stop();
                    var cfg = AI.GetActiveConfiguration();
                    _statistics.RecordCall(new ApiCallRecord
                    {
                        Timestamp = DateTime.Now,
                        ModelName = cfg?.ModelId ?? "unknown",
                        Provider = cfg?.ProviderId ?? "Silent",
                        Success = false,
                        ResponseTimeMs = (int)swSilent.ElapsedMilliseconds,
                        InputTokens = silentInTokens,
                        OutputTokens = silentOutTokens,
                        ErrorMessage = "用户取消"
                    });
                    if (silentInTokens > 0 || silentOutTokens > 0)
                        AIChunkBus.Publish(new UsageChunk(silentInTokens, silentOutTokens) { RunId = silentRunId });
                }
                catch { }
                return "[已取消]";
            }
            catch (Exception ex)
            {
                var silentErrCfg = AI.GetActiveConfiguration();
                LogIfPublic(silentErrCfg, $"[SKChatService] SendSilentMessageAsync 错误: {ex.Message}");
                try
                {
                    swSilent.Stop();
                    var cfg = AI.GetActiveConfiguration();
                    _statistics.RecordCall(new ApiCallRecord
                    {
                        Timestamp = DateTime.Now,
                        ModelName = cfg?.ModelId ?? "unknown",
                        Provider = cfg?.ProviderId ?? "Silent",
                        Success = false,
                        ResponseTimeMs = (int)swSilent.ElapsedMilliseconds,
                        InputTokens = silentInTokens,
                        OutputTokens = silentOutTokens,
                        ErrorMessage = ex.Message
                    });
                    if (silentInTokens > 0 || silentOutTokens > 0)
                        AIChunkBus.Publish(new UsageChunk(silentInTokens, silentOutTokens) { RunId = silentRunId });
                }
                catch { }

                if (pipelineCtx != null)
                {
                    try
                    {
                        await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception pipelineEx)
                    {
                        DebugLogOnce("PipelineOnError-SendSilent", pipelineEx);
                    }
                }

                return IsTianmingPrivateProvider(silentErrCfg?.ProviderId) ? "[错误] 请求失败" : $"[错误] {ex.Message}";
            }
        }

        public async Task<string> GenerateOneShotAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => await GenerateOneShotAsync(null, systemPrompt, userPrompt, null, cancellationToken).ConfigureAwait(false);

        public async Task<string> GenerateOneShotAsync(UserConfiguration config, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => await GenerateOneShotAsync(config, systemPrompt, userPrompt, null, cancellationToken).ConfigureAwait(false);

        public async Task<string> GenerateOneShotAsync(string systemPrompt, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken = default)
            => await GenerateOneShotAsync(null, systemPrompt, userPrompt, progress, cancellationToken).ConfigureAwait(false);

        public async Task<string> GenerateOneShotAsync(UserConfiguration? config, string systemPrompt, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken = default)
        {
            using var _progressRunScope = GenerationProgressHub.BeginRun(Guid.Empty);

            await InitializedAsync.ConfigureAwait(false);

            CancellationTokenSource? localCts = null;
            var swOsOuter = Stopwatch.StartNew();

            var osRunId = TM.Framework.Common.Helpers.Id.ShortIdGenerator.NewGuid();
            AIRequestContext? pipelineCtx = null;
            var pipeline = CapabilityServices.DefaultPipeline;
            int osInTokens = 0, osOutTokens = 0;

            try
            {
                var initialBundle = EnsureKernelInitialized(config);
                if (initialBundle == null)
                {
                    GlobalToast.Error("AI 服务未配置", "请先前往“智能助手 > 模型管理”完成 API Key 配置。");
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                var history = string.IsNullOrWhiteSpace(systemPrompt)
                    ? new ChatHistory()
                    : new ChatHistory(systemPrompt);

                if (!string.IsNullOrWhiteSpace(userPrompt))
                {
                    history.AddUserMessage(userPrompt);
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                System.Threading.Interlocked.Exchange(ref _businessCts, localCts);

                var osCfgInit = config ?? AI.GetActiveConfiguration();
                if (osCfgInit != null)
                {
                    var osResolved = CapabilityServices.DefaultResolver.Resolve(
                        providerId: osCfgInit.ProviderId,
                        modelId: osCfgInit.ModelId,
                        endpoint: osCfgInit.CustomEndpoint,
                        userHint: new UserCapabilityHint
                        {
                            CapabilitiesDetected = osCfgInit.CapabilitiesDetected,
                            IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(osCfgInit.ProviderId, osCfgInit.ModelId),
                        });
                    pipelineCtx = new AIRequestContext
                    {
                        RunId = osRunId,
                        Config = osCfgInit,
                        ChatHistory = history,
                        Resolved = osResolved,
                    };
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = initialBundle.ProviderType ?? GetCurrentProviderType();
                    pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(osCfgInit);
                    await pipeline.RunStageAsync(pipelineCtx, MiddlewareStage.BeforeRequest, localCts.Token).ConfigureAwait(false);
                }

                const int maxTokensRetries = 5;
                int tokenRetryCount = 0;
                int? fallbackMaxTokens = null;

                while (true)
                {
                    var settings = ChatModeSettings.GetExecutionSettings(
                        TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Business, history,
                        overrideMaxTokens: fallbackMaxTokens, overrideConfig: config);

                    var osCfgLoop = config ?? AI.GetActiveConfiguration();
                    if (pipelineCtx != null)
                    {
                        pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(osCfgLoop);
                        await pipeline.RunStageAsync(pipelineCtx with { Settings = settings }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                    }

                    LogIfPublic(osCfgLoop, $"[SKChatService] OneShot 生成开始 maxTokens={fallbackMaxTokens?.ToString() ?? "AUTO"}");

                    try
                    {
                        var swOsStartTime = DateTime.Now;
                        var swOs = Stopwatch.StartNew();
                        var osResult = await InvokeApiWithRotationAsync(
                            async (bundle, innerCt) => await AdaptiveGenerateAsync(history, settings, progress, innerCt, config, bundle).ConfigureAwait(false),
                            localCts.Token,
                            config: config,
                            progress: progress).ConfigureAwait(false);
                        var content = osResult.Content;
                        osInTokens = osResult.InputTokens;
                        osOutTokens = osResult.OutputTokens;
                        swOs.Stop();

                        var (isOsCancelled, _) = UIMessageItem.TryExtractCancelledPartial(content);
                        if (!string.IsNullOrWhiteSpace(content)
                            && (content.StartsWith("[错误]", StringComparison.Ordinal) || isOsCancelled))
                        {
                            var cfgOsEarly = config ?? AI.GetActiveConfiguration();
                            _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOsEarly?.ModelId ?? "unknown", Provider = cfgOsEarly?.ProviderId ?? "OneShot", Success = false, ResponseTimeMs = (int)swOs.ElapsedMilliseconds, InputTokens = osInTokens, OutputTokens = osOutTokens, ErrorMessage = content });
                            return content;
                        }

                        const int maxOsLengthCont = 2;
                        for (int lc = 0;
                             lc < maxOsLengthCont
                             && ChatModeSettings.IsFinishReasonTruncated(ChatModeSettings.LastFinishReason)
                             && !cancellationToken.IsCancellationRequested; lc++)
                        {
                            var upgradedMax = ChatModeSettings.GetUpgradeMaxTokens(ChatModeSettings.LastUsedMaxTokens, (config ?? AI.GetActiveConfiguration())?.ModelId, (config ?? AI.GetActiveConfiguration())?.CustomEndpoint, (config ?? AI.GetActiveConfiguration())?.ProviderId);
                            if (!upgradedMax.HasValue)
                            {
                                LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot finish_reason=length 已在梯队顶端，放弃续写");
                                break;
                            }
                            LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot finish_reason=length，续写#{lc + 1}: {ChatModeSettings.LastUsedMaxTokens} -> {upgradedMax.Value}");
                            history.AddAssistantMessage(content);
                            history.AddUserMessage("请继续");
                            var contSettingsOs = ChatModeSettings.GetExecutionSettings(
                                TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Business, history,
                                overrideMaxTokens: upgradedMax.Value, overrideConfig: config);
                            if (contSettingsOs is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings oaiCsOs)
                                oaiCsOs.FunctionChoiceBehavior = null;
                            var osCfgCont = config ?? AI.GetActiveConfiguration();
                            if (osCfgCont != null && pipelineCtx != null)
                            {
                                var contBundleOs = EnsureKernelInitialized(osCfgCont);
                                pipelineCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = contBundleOs?.ProviderType ?? GetCurrentProviderType();
                                pipelineCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(osCfgCont);
                                await pipeline.RunStageAsync(pipelineCtx with { Settings = contSettingsOs }, MiddlewareStage.TransformSettings, localCts.Token).ConfigureAwait(false);
                            }
                            string contContentOs;
                            try
                            {
                                var contResultOs = await InvokeApiWithRotationAsync(
                                    async (bundle, innerCt) => await AdaptiveGenerateAsync(history, contSettingsOs, progress, innerCt, config, bundle).ConfigureAwait(false),
                                    localCts.Token, config: config, progress: progress).ConfigureAwait(false);
                                contContentOs = contResultOs.Content;
                                osInTokens += contResultOs.InputTokens;
                                osOutTokens += contResultOs.OutputTokens;
                            }
                            catch (Exception contEx)
                            {
                                LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot 续写异常: {contEx.Message}");
                                if (history.Count >= 2
                                    && history[^1].Role == AuthorRole.User
                                    && history[^2].Role == AuthorRole.Assistant)
                                {
                                    history.RemoveAt(history.Count - 1);
                                    history.RemoveAt(history.Count - 1);
                                }
                                break;
                            }
                            if (history.Count >= 2
                                && history[^1].Role == AuthorRole.User
                                && history[^2].Role == AuthorRole.Assistant)
                            {
                                history.RemoveAt(history.Count - 1);
                                history.RemoveAt(history.Count - 1);
                            }
                            contContentOs = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(contContentOs);
                            var (isOsContCancelled, _) = UIMessageItem.TryExtractCancelledPartial(contContentOs);
                            if (!string.IsNullOrWhiteSpace(contContentOs)
                                && (contContentOs.StartsWith("[错误]", StringComparison.Ordinal) || isOsContCancelled))
                            {
                                LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot 续写返回错误/取消，停止续写: {contContentOs}");
                                break;
                            }
                            if (!string.IsNullOrEmpty(contContentOs))
                                content = content + "\n\n" + contContentOs;
                            LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot 续写完成，追加 {contContentOs.Length} 字符，finish_reason={ChatModeSettings.LastFinishReason ?? "(null)"}");
                        }

                        if (InfoLogDedup.ShouldLog("SK:OneShotDone")) LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot 生成完成，长度: {content.Length}");
                        var cfgOs = config ?? AI.GetActiveConfiguration();
                        _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = true, ResponseTimeMs = (int)swOs.ElapsedMilliseconds, InputTokens = osInTokens, OutputTokens = osOutTokens, FirstTokenMs = osResult.FirstTokenMs, TokensPerSecond = osResult.TokensPerSecond });

                        if (osInTokens > 0 || osOutTokens > 0)
                            AIChunkBus.Publish(new UsageChunk(osInTokens, osOutTokens) { RunId = osRunId });

                        if (osResult.FirstTokenMs > 0)
                            TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.ReportFirstToken(
                                osRunId, swOsStartTime.AddMilliseconds(osResult.FirstTokenMs));

                        if (pipelineCtx != null)
                        {
                            await pipeline.RunStageAsync(pipelineCtx with { FinalAnswer = content }, MiddlewareStage.AfterResponse, localCts.Token).ConfigureAwait(false);
                        }

                        return content;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                        && tokenRetryCount < maxTokensRetries
                        && (ChatModeSettings.IsUnsupportedParameterError(ex) || ChatModeSettings.IsMaxTokensError(ex)
                            || ChatModeSettings.IsContextWindowError(ex)
                            || ChatModeSettings.IsLongContextRejectedError(ex, config ?? AI.GetActiveConfiguration())))
                    {
                        var longCtxCfgOs = config ?? AI.GetActiveConfiguration();
                        if (ChatModeSettings.IsLongContextRejectedError(ex, longCtxCfgOs)
                            && longCtxCfgOs != null && !string.IsNullOrEmpty(longCtxCfgOs.ModelId))
                        {
                            ChatModeSettings.MarkUnsupportedParam(longCtxCfgOs.ProviderId, longCtxCfgOs.CustomEndpoint, longCtxCfgOs.ModelId, "long_context");
                            longCtxCfgOs.EnableLongContext = null;
                            try { AI.UpdateConfiguration(longCtxCfgOs); } catch { }
                            tokenRetryCount++;
                            LogIfPublic(longCtxCfgOs, $"[SKChatService] OneShot 1M 上下文请求被拒，已回退到基线窗口并重试: {longCtxCfgOs.ModelId}");
                            GlobalToast.Warning("1M 上下文已回退", "端点未接受 1M 参数，已自动回退并重试");
                            continue;
                        }

                        if (ChatModeSettings.TryParseUnsupportedParamName(ex, out var unsupParamNameOs))
                        {
                            var c = config ?? AI.GetActiveConfiguration();
                            if (c != null && !string.IsNullOrEmpty(c.ModelId))
                                ChatModeSettings.MarkUnsupportedParam(c.ProviderId, c.CustomEndpoint, c.ModelId, unsupParamNameOs);
                            if (unsupParamNameOs.Contains("max_tokens") || unsupParamNameOs.Contains("max_output") || unsupParamNameOs.Contains("max_completion"))
                                fallbackMaxTokens = null;
                            tokenRetryCount++;
                            LogIfPublic(c, $"[SKChatService] OneShot 端点不支持参数 '{unsupParamNameOs}'，标记并重试");
                        }
                        else if (ChatModeSettings.IsContextWindowError(ex))
                        {
                            var c = config ?? AI.GetActiveConfiguration();
                            bool cwParsedOs = false;
                            if (c != null && !string.IsNullOrEmpty(c.ModelId))
                            {
                                if (ChatModeSettings.TryParseContextWindowLimit(ex, out var parsedCwOs) && parsedCwOs > 0)
                                {
                                    ChatModeSettings.RecordDiscoveredContextWindow(c.ModelId, parsedCwOs, c.CustomEndpoint, c.ProviderId, DiscoverySource.ErrorParsed);
                                    cwParsedOs = true;
                                }
                            }

                            if (cwParsedOs)
                            {
                                fallbackMaxTokens = null;
                            }
                            else
                            {
                                var currentMax = ChatModeSettings.LastUsedMaxTokens;
                                var nextMax = currentMax > 0 ? ChatModeSettings.GetFallbackMaxTokens(currentMax) : 4096;
                                if (currentMax > 0 && nextMax >= currentMax)
                                    throw;
                                fallbackMaxTokens = nextMax;
                            }
                            tokenRetryCount++;
                            var osCfgLog = config ?? AI.GetActiveConfiguration();
                            LogIfPublic(osCfgLog, $"[SKChatService] OneShot context_window retry #{tokenRetryCount}: maxTokens->{(fallbackMaxTokens?.ToString() ?? "AUTO")}, discovered={ChatModeSettings.GetDiscoveredContextWindow(osCfgLog?.ModelId ?? string.Empty, osCfgLog?.CustomEndpoint, osCfgLog?.ProviderId)}");
                        }
                        else
                        {
                            var currentMax = ChatModeSettings.LastUsedMaxTokens;
                            var maxOutputSourceOs = DiscoverySource.ProbedBoundary;
                            var isParsedOs = ChatModeSettings.TryParseMaxTokensLimit(ex, out var parsedLimitOs);
                            if (isParsedOs)
                            {
                                fallbackMaxTokens = parsedLimitOs;
                                maxOutputSourceOs = DiscoverySource.ErrorParsed;
                            }
                            else
                            {
                                var osCfg = config ?? AI.GetActiveConfiguration();
                                var probeBundleOs = osCfg != null ? EnsureKernelInitialized(osCfg) : null;
                                if (probeBundleOs != null && osCfg != null && !string.IsNullOrEmpty(osCfg.ModelId))
                                {
                                    var probedOs = await ChatModeSettings.ProbeMaxTokensConcurrentAsync(
                                        async (maxT, probeCt) =>
                                        {
                                            var ph = new ChatHistory("Reply OK");
                                            ph.AddUserMessage("Hi");
                                            var ps = new OpenAIPromptExecutionSettings { MaxTokens = maxT };
                                            var curBundle = EnsureKernelInitialized(osCfg)
                                                ?? throw new InvalidOperationException("[SKChatService] Kernel 不可用");
                                            var probeResolved = CapabilityServices.DefaultResolver.Resolve(
                                                providerId: osCfg.ProviderId,
                                                modelId: osCfg.ModelId,
                                                endpoint: osCfg.CustomEndpoint,
                                                userHint: new UserCapabilityHint
                                                {
                                                    CapabilitiesDetected = osCfg.CapabilitiesDetected,
                                                    IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(osCfg.ProviderId, osCfg.ModelId),
                                                });
                                            var probeCtx = new AIRequestContext
                                            {
                                                RunId = osRunId,
                                                Config = osCfg,
                                                Settings = ps,
                                                ChatHistory = ph,
                                                Resolved = probeResolved,
                                            };
                                            probeCtx.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = curBundle.ProviderType ?? GetCurrentProviderType();
                                            probeCtx.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(osCfg);
                                            await pipeline.RunStageAsync(probeCtx, MiddlewareStage.TransformSettings, probeCt).ConfigureAwait(false);
                                            await curBundle.ChatService.GetChatMessageContentAsync(ph, ps, curBundle.Kernel, probeCt).ConfigureAwait(false);
                                        },
                                        osCfg.ModelId, osCfg.CustomEndpoint, osCfg.ProviderId,
                                        cancellationToken).ConfigureAwait(false);

                                    if (probedOs.HasValue)
                                        maxOutputSourceOs = DiscoverySource.ProbedExact;
                                    fallbackMaxTokens = probedOs
                                        ?? (currentMax > 0
                                            ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                            : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop));
                                }
                                else
                                {
                                    fallbackMaxTokens = currentMax > 0
                                        ? ChatModeSettings.GetFallbackMaxTokens(currentMax)
                                        : ChatModeSettings.GetFallbackMaxTokens(ChatModeSettings.MaxTokensLadderTop);
                                }
                            }

                            if (fallbackMaxTokens.HasValue && fallbackMaxTokens.Value > 0)
                            {
                                var c = config ?? AI.GetActiveConfiguration();
                                if (c != null && !string.IsNullOrEmpty(c.ModelId))
                                    ChatModeSettings.RecordDiscoveredMaxOutput(c.ModelId, fallbackMaxTokens.Value, c.CustomEndpoint, c.ProviderId,
                                        maxOutputSourceOs);
                            }
                            tokenRetryCount++;
                            LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot max_tokens retry #{tokenRetryCount}: {currentMax} -> {fallbackMaxTokens}");
                        }
                    }
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-OneShot", pipelineEx); }
                }
                if (localCts?.IsCancellationRequested == true)
                {
                    LogIfPublic(config ?? AI.GetActiveConfiguration(), "[SKChatService] OneShot 请求已被 CancelCurrentRequest 取消");
                    return "[已取消]";
                }
                LogIfPublic(config ?? AI.GetActiveConfiguration(), $"[SKChatService] OneShot 请求超时或被底层取消: {ex.Message}");
                var cfgOs = config ?? AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = false, ResponseTimeMs = (int)swOsOuter.ElapsedMilliseconds, InputTokens = osInTokens, OutputTokens = osOutTokens, ErrorMessage = "请求超时" });
                if (osInTokens > 0 || osOutTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(osInTokens, osOutTokens) { RunId = osRunId });
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException ex)
            {
                LogIfPublic(config ?? AI.GetActiveConfiguration(), "[SKChatService] OneShot 请求已取消");
                if (pipelineCtx != null)
                {
                    try { await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception pipelineEx) { DebugLogOnce("PipelineOnError-Cancel-OneShot2", pipelineEx); }
                }
                return "[已取消]";
            }
            catch (Exception ex)
            {
                var cfgOs = config ?? AI.GetActiveConfiguration();
                LogIfPublic(cfgOs, $"[SKChatService] OneShot 错误: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 生成失败", ex.Message, cfgOs?.ProviderId);
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = false, ResponseTimeMs = (int)swOsOuter.ElapsedMilliseconds, InputTokens = osInTokens, OutputTokens = osOutTokens, ErrorMessage = ex.Message });
                if (osInTokens > 0 || osOutTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(osInTokens, osOutTokens) { RunId = osRunId });

                if (pipelineCtx != null)
                {
                    try
                    {
                        await pipeline.RunStageAsync(pipelineCtx with { Error = ex }, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception pipelineEx)
                    {
                        DebugLogOnce("PipelineOnError-OneShot", pipelineEx);
                    }
                }

                return IsTianmingPrivateProvider(cfgOs?.ProviderId) ? "[错误] 请求失败" : $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    Interlocked.CompareExchange(ref _businessCts, null, localCts);
                    localCts.Dispose();
                }
            }
        }

        private static (int InputTokens, int OutputTokens) TryExtractTokenUsage(Microsoft.SemanticKernel.ChatMessageContent? response)
        {
            if (response == null) return (0, 0);

            try
            {
                if (response.Metadata != null
                    && response.Metadata.TryGetValue("Usage", out var usageObj)
                    && usageObj is System.Collections.Generic.IDictionary<string, int> usageDict)
                {
                    int inT = 0, outT = 0;
                    usageDict.TryGetValue("InputTokens", out inT);
                    usageDict.TryGetValue("OutputTokens", out outT);
                    if (inT > 0 || outT > 0) return (inT, outT);
                }

                if (response.InnerContent is OpenAI.Chat.ChatCompletion oaiComp && oaiComp.Usage != null)
                {
                    return (oaiComp.Usage.InputTokenCount, oaiComp.Usage.OutputTokenCount);
                }
            }
            catch { }
            return (0, 0);
        }

        #endregion

        public void CancelCurrentRequest()
        {
            try
            {
                _chatCts?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogOnce("CancelCurrentRequest_Chat", ex);
            }

            try
            {
                _streamCts?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogOnce("CancelCurrentRequest_Stream", ex);
            }

            try
            {
                _businessCts?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogOnce("CancelCurrentRequest_Business", ex);
            }

            TM.App.Log("[SKChatService] 取消当前请求（chat + stream + business）");
        }

        private void EnsureSystemPrompt(string? explicitSystemPrompt)
        {
            string systemPrompt = explicitSystemPrompt ?? string.Empty;

            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                var config = AI.GetActiveConfiguration();
                systemPrompt = AIService.GetEffectiveDeveloperMessage(config);
            }

            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                return;
            }

            if (_currentMode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Plan ||
                _currentMode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Agent)
            {
                const string enforceBlock = "\n\n<output_format_enforcement mandatory=\"true\">\nYou MUST output in exactly this structure:\n<analysis>(thinking/reasoning ONLY here)</analysis><answer>(final output ONLY here)</answer>\n\nRules:\n1) NEVER include reasoning in <answer>.\n2) NEVER output meta-info like 'Thought for 50.1 s'. Record thoughts in <analysis> only.\n3) NEVER omit <analysis>/<answer> tags.\n</output_format_enforcement>\n";

                if (!systemPrompt.Contains("<output_format_enforcement", StringComparison.Ordinal))
                {
                    systemPrompt += enforceBlock;
                }
            }

            if (_chatHistory != null)
            {
                foreach (var msg in _chatHistory)
                {
                    if (msg.Role == AuthorRole.System && !string.IsNullOrWhiteSpace(msg.Content))
                    {
                        if (string.Equals(msg.Content, systemPrompt, StringComparison.Ordinal))
                        {
                            return;
                        }
                        break;
                    }
                }
            }

            var oldHistory = _chatHistory ?? new ChatHistory();
            var newHistory = new ChatHistory(systemPrompt);
            bool skippedFirstSystem = false;

            foreach (var msg in oldHistory)
            {
                if (msg.Role == AuthorRole.System)
                {
                    if (!skippedFirstSystem)
                    {
                        skippedFirstSystem = true;
                        continue;
                    }

                    var systemText = msg.Content;
                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        newHistory.AddSystemMessage(systemText);
                    }

                    continue;
                }

                var text = msg.Content;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (msg.Role == AuthorRole.User)
                {
                    newHistory.AddUserMessage(text);
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    newHistory.AddAssistantMessage(text);
                }
            }

            _chatHistory = newHistory;
            _isSessionCompressed = false;
        }
    }
}

