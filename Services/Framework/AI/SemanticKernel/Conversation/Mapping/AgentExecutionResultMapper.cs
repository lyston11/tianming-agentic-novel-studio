using System;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public sealed class AgentExecutionResultMapper : IExecutionResultMapper
    {
        public ConversationMessage MapExecutionResult(ExecutionResultContext context)
        {
            var original = context.OriginalMessage;

            var payload = new AgentPayload
            {
                ToolCalls = context.ExecutionTrace ?? Array.Empty<ToolCallRecord>(),
                RawContent = original?.Payload?.RawContent
            };

            var traceText = string.IsNullOrEmpty(context.TraceSummaryText)
                ? "执行完成"
                : context.TraceSummaryText;

            var summary = ConversationSummarizer.ForExecutionCompleted(
                context.ChapterTitle,
                context.ChapterId,
                traceText);

            return new ConversationMessage
            {
                RunId = context.RunId,
                Role = original?.Role ?? Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant,
                Timestamp = original?.Timestamp ?? DateTime.Now,
                Summary = summary,
                AnalysisRaw = context.ThinkingRaw ?? original?.AnalysisRaw ?? string.Empty,
                AnalysisBlocks = original?.AnalysisBlocks ?? Array.Empty<ThinkingBlock>(),
                Payload = payload
            };
        }
    }
}
