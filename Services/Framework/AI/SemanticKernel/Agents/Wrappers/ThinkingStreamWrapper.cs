using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;

namespace TM.Services.Framework.AI.SemanticKernel.Agents.Wrappers
{
    public sealed class ThinkingStreamWrapper
    {
        private readonly string _providerType;

        public ThinkingStreamWrapper(string providerType)
        {
            _providerType = providerType;
        }

        public async IAsyncEnumerable<IStreamChunk> WrapAgentStreamAsync(
            IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> agentStream,
            Guid runId,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var router = new ThinkingRouter(_providerType);
            string? lastFinishReason = null;
            int seq = 0;

            int accPromptTokens = 0;
            int accCompletionTokens = 0;
            var thinkingFullBuilder = new StringBuilder();
            string? thinkingKind = null;
            int thinkingMsFromProvider = 0;
            long? thinkingFirstTicks = null;
            long? thinkingLastTicks = null;

            await foreach (var item in agentStream.WithCancellation(ct).ConfigureAwait(false))
            {
                var chunk = item.Message;

                if (chunk.Metadata?.TryGetValue("FinishReason", out var fr) == true && fr != null)
                {
                    var frStr = fr.ToString();
                    if (!string.IsNullOrEmpty(frStr))
                        lastFinishReason = frStr;
                }

                if (chunk.Metadata?.TryGetValue("Usage", out var usageObj) == true
                    && usageObj is IDictionary<string, int> usageDict)
                {
                    if (usageDict.TryGetValue("InputTokens", out var inT) && inT > accPromptTokens) accPromptTokens = inT;
                    if (usageDict.TryGetValue("OutputTokens", out var outT) && outT > accCompletionTokens) accCompletionTokens = outT;
                }

                if (chunk.Metadata?.TryGetValue("ThinkingFull", out var tf) == true && tf is string tfStr && tfStr.Length > 0)
                {
                    thinkingFullBuilder.Clear();
                    thinkingFullBuilder.Append(tfStr);
                }
                if (chunk.Metadata?.TryGetValue("ThinkingMs", out var tm) == true && tm is int tmInt && tmInt > 0)
                {
                    thinkingMsFromProvider = tmInt;
                }

                if (chunk.InnerContent is OpenAI.Chat.StreamingChatCompletionUpdate openAiUpdate
                    && openAiUpdate.Usage != null)
                {
                    if (openAiUpdate.Usage.InputTokenCount > 0) accPromptTokens = openAiUpdate.Usage.InputTokenCount;
                    if (openAiUpdate.Usage.OutputTokenCount > 0) accCompletionTokens = openAiUpdate.Usage.OutputTokenCount;
                }

                var hasThinkingMeta = chunk.Metadata?.ContainsKey("Thinking") == true;
                if (string.IsNullOrEmpty(chunk.Content) &&
                    !hasThinkingMeta &&
                    chunk.InnerContent is not OpenAI.Chat.StreamingChatCompletionUpdate)
                    continue;

                var routed = router.Route(chunk);

                if (!string.IsNullOrEmpty(routed.ThinkingContent))
                {
                    if (!string.IsNullOrWhiteSpace(routed.ThinkingKind))
                        thinkingKind ??= routed.ThinkingKind;
                    thinkingFirstTicks ??= DateTime.UtcNow.Ticks;
                    thinkingLastTicks = DateTime.UtcNow.Ticks;
                    thinkingFullBuilder.Append(routed.ThinkingContent);
                    yield return new ThinkingDeltaChunk(routed.ThinkingContent, thinkingKind)
                    {
                        RunId = runId,
                        Sequence = seq++,
                    };
                }

                if (!string.IsNullOrEmpty(routed.AnswerContent))
                {
                    yield return new TextDeltaChunk(routed.AnswerContent)
                    {
                        RunId = runId,
                        Sequence = seq++,
                    };
                }
            }

            var flushed = router.Flush();

            if (!string.IsNullOrEmpty(flushed.ThinkingContent))
            {
                if (!string.IsNullOrWhiteSpace(flushed.ThinkingKind))
                    thinkingKind ??= flushed.ThinkingKind;
                thinkingFirstTicks ??= DateTime.UtcNow.Ticks;
                thinkingLastTicks = DateTime.UtcNow.Ticks;
                thinkingFullBuilder.Append(flushed.ThinkingContent);
                yield return new ThinkingDeltaChunk(flushed.ThinkingContent, thinkingKind)
                {
                    RunId = runId,
                    Sequence = seq++,
                };
            }

            if (!string.IsNullOrEmpty(flushed.AnswerContent))
            {
                yield return new TextDeltaChunk(flushed.AnswerContent)
                {
                    RunId = runId,
                    Sequence = seq++,
                };
            }

            if (thinkingFullBuilder.Length > 0)
            {
                int durationMs = thinkingMsFromProvider;
                if (durationMs <= 0 && thinkingFirstTicks.HasValue && thinkingLastTicks.HasValue)
                {
                    durationMs = (int)TimeSpan.FromTicks(thinkingLastTicks.Value - thinkingFirstTicks.Value).TotalMilliseconds;
                }
                yield return new ThinkingCompleteChunk(thinkingFullBuilder.ToString(), durationMs > 0 ? durationMs : 0, thinkingKind)
                {
                    RunId = runId,
                    Sequence = seq++,
                };
            }

            if (accPromptTokens > 0 || accCompletionTokens > 0)
            {
                yield return new UsageChunk(accPromptTokens, accCompletionTokens)
                {
                    RunId = runId,
                    Sequence = seq++,
                };
            }

            yield return new StreamCompleteChunk(lastFinishReason)
            {
                RunId = runId,
                Sequence = seq++,
            };
        }
    }
}
