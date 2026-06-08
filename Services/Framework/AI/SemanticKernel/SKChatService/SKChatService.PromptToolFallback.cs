#pragma warning disable SKEXP0010

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Framework.AI.Middleware;
using TM.Services.Framework.AI.Middleware.Builtins;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.PromptToolFallback;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        private async Task<string?> TryRunPromptToolFallbackAsync(
            UserConfiguration streamConfig,
            string displayText,
            ChatPromptParts promptParts,
            Action<string> onChunk,
            Action<string?>? onStatusChanged,
            Guid runId,
            CancellationToken cancellationToken)
        {
            if (streamConfig == null) return null;

            var resolved = CapabilityServices.DefaultResolver.Resolve(
                providerId: streamConfig.ProviderId,
                modelId: streamConfig.ModelId,
                endpoint: streamConfig.CustomEndpoint,
                userHint: new UserCapabilityHint
                {
                    CapabilitiesDetected = streamConfig.CapabilitiesDetected,
                    SupportsNativeToolUse = null,
                    IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(
                        streamConfig.ProviderId, streamConfig.ModelId),
                });

            if (!PromptToolFallbackEnabler.ShouldUseFallback(resolved, _currentMode))
            {
                return null;
            }

            LogIfPublic(streamConfig, $"[SKChatService] P5.2 触发 Prompt Tool fallback: {streamConfig.ProviderId}/{streamConfig.ModelId}, mode={_currentMode}");
            onStatusChanged?.Invoke("使用 Prompt Tool fallback...");

            var pipelineContext = new AIRequestContext
            {
                RunId = runId,
                Config = streamConfig,
                ChatHistory = _chatHistory,
                Resolved = resolved,
            };
            pipelineContext.Metadata[ThinkingRequestMiddleware.ProviderTypeKey] = GetCurrentProviderType();
            pipelineContext.Metadata[ThinkingRequestMiddleware.SuppressedKey] = IsThinkingInjectionSuppressed(streamConfig);
            var pipeline = CapabilityServices.DefaultPipeline;

            try
            {
                await pipeline.RunStageAsync(pipelineContext, MiddlewareStage.BeforeRequest, cancellationToken).ConfigureAwait(false);

                EnsureSystemPrompt(promptParts.SystemPrompt);
                var userPromptForModel = string.IsNullOrWhiteSpace(promptParts.UserPrompt) ? displayText : promptParts.UserPrompt;
                await EnsureCompressionIfNeededAsync(userPromptForModel, cancellationToken).ConfigureAwait(false);
                _chatHistory.AddUserMessage(userPromptForModel);

                TM.Services.Framework.AI.Monitoring.RequestLifecycleCollector.Track(
                    runId,
                    providerId: streamConfig.ProviderId,
                    modelId: streamConfig.ModelId,
                    endpoint: streamConfig.CustomEndpoint);
                RetryFallbackMiddleware.MarkFallback(pipelineContext, "prompt_tool_fallback");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = _currentMode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunStarted,
                    Title = "Prompt Tool fallback",
                    Detail = IsTianmingPrivateProvider(streamConfig.ProviderId) ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : $"{streamConfig.ProviderId}/{streamConfig.ModelId}",
                });

                var orchestrator = new PromptToolFallbackOrchestrator();
                var orchestratorResult = await InvokeApiWithRotationAsync<(string Answer, int InputTokens, int OutputTokens)>(
                    async (bundle, ct) =>
                    {
                        var settings = GetCurrentModeSettings(null, bundle.Kernel);
                        if (settings != null) settings.FunctionChoiceBehavior = null;
                        pipelineContext = pipelineContext with { Settings = settings, ChatHistory = _chatHistory };

                        await pipeline.RunStageAsync(pipelineContext, MiddlewareStage.TransformSettings, ct).ConfigureAwait(false);

                        var allowedFunctions = bundle.Kernel.Plugins
                            .SelectMany(p => p.AsEnumerable())
                            .Where(f => PlanModeFilter.IsToolAllowedForMode(_currentMode, f.PluginName, f.Name))
                            .ToList();

                        return await orchestrator.RunAsync(
                            bundle.Kernel,
                            bundle.ChatService,
                            settings,
                            _chatHistory,
                            allowedFunctions,
                            ct).ConfigureAwait(false);
                    },
                    cancellationToken,
                    config: streamConfig,
                    allowFailover: true).ConfigureAwait(false);

                var answer = orchestratorResult.Answer;
                var fallbackInTokens = orchestratorResult.InputTokens;
                var fallbackOutTokens = orchestratorResult.OutputTokens;

                var cleaned = CleanNonStreamContent(answer);
                string displayAnswer;
                if (!string.IsNullOrWhiteSpace(cleaned.Answer))
                {
                    displayAnswer = cleaned.Answer;
                }
                else if (!string.IsNullOrWhiteSpace(cleaned.Thinking) || string.IsNullOrWhiteSpace(answer))
                {
                    displayAnswer = "（模型未输出正式回答，请重试。）";
                }
                else
                {
                    displayAnswer = answer;
                }
                var simulatedChunks = SimulatedStreamChunker.Slice(
                    displayAnswer,
                    cleaned.Thinking,
                    ResolveThinkingKindForDisplay(cleaned.Kind, streamConfig),
                    runId).ToList();

                foreach (var chunk in simulatedChunks)
                {
                    await pipeline.RunStageAsync(pipelineContext with { Chunk = chunk }, MiddlewareStage.OnChunk, cancellationToken).ConfigureAwait(false);
                    AIChunkBus.Publish(chunk);
                }

                if (fallbackInTokens > 0 || fallbackOutTokens > 0)
                    AIChunkBus.Publish(new UsageChunk(fallbackInTokens, fallbackOutTokens) { RunId = runId });

                var completeChunk = new StreamCompleteChunk("stop")
                {
                    RunId = runId,
                    Sequence = simulatedChunks.Count,
                };
                await pipeline.RunStageAsync(pipelineContext with { Chunk = completeChunk }, MiddlewareStage.OnChunk, cancellationToken).ConfigureAwait(false);
                AIChunkBus.Publish(completeChunk);

                if (!string.IsNullOrEmpty(displayAnswer))
                {
                    onChunk(displayAnswer);
                }

                if (!string.IsNullOrEmpty(displayAnswer))
                {
                    _chatHistory.AddAssistantMessage(displayAnswer);
                }

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = _currentMode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.AssistantMessage,
                    Title = "Assistant",
                    Detail = displayAnswer,
                    Succeeded = true,
                });
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = _currentMode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunCompleted,
                    Title = "Run completed (fallback)",
                    Succeeded = true,
                });

                var afterCtx = pipelineContext with { FinalAnswer = displayAnswer };
                await pipeline.RunStageAsync(afterCtx, MiddlewareStage.AfterResponse, cancellationToken).ConfigureAwait(false);

                LastRunId = runId;
                return displayAnswer;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogIfPublic(streamConfig, "[SKChatService] Prompt Tool fallback 用户取消");
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = _currentMode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "已取消",
                    Detail = "[已取消]",
                    Succeeded = false,
                });
                onChunk("[已取消]");
                return "[已取消]";
            }
            catch (Exception ex)
            {
                LogIfPublic(streamConfig, $"[SKChatService] Prompt Tool fallback 失败: {ex.Message}");
                try
                {
                    var errorCtx = pipelineContext with { Error = ex };
                    await pipeline.RunStageAsync(errorCtx, MiddlewareStage.OnError, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception pipelineEx)
                {
                    DebugLogOnce("PipelineOnError", pipelineEx);
                }

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = _currentMode,
                    RunType = _currentRunType,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "Prompt Tool fallback 失败",
                    Detail = IsTianmingPrivateProvider(streamConfig.ProviderId) ? "请求失败" : ex.ToString(),
                    Succeeded = false,
                });

                var errMsg = IsTianmingPrivateProvider(streamConfig.ProviderId)
                    ? "[错误] Prompt Tool fallback 失败"
                    : $"[错误] Prompt Tool fallback 失败: {ex.Message}";
                onChunk(errMsg);
                return errMsg;
            }
        }
    }
}
