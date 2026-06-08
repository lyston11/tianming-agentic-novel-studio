using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies
{
    public class MetadataThinkingStrategy : IThinkingStrategy
    {
        public ThinkingRouteResult Extract(StreamingChatMessageContent chunk)
        {
            string? thinking = null;
            string? answer = null;

            if (chunk.Metadata?.TryGetValue("Thinking", out var thinkingObj) == true)
            {
                var text = thinkingObj?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    thinking = text;
                }
            }

            var content = chunk.Content;
            if (!string.IsNullOrEmpty(content))
            {
                answer = content;
            }

            return new ThinkingRouteResult { ThinkingContent = thinking, ThinkingKind = thinking != null ? "Thinking" : null, AnswerContent = answer };
        }

        public ThinkingRouteResult Flush() => default;
    }
}
