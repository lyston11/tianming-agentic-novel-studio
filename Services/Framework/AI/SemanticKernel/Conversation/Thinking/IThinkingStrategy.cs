using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking
{
    public interface IThinkingStrategy
    {
        ThinkingRouteResult Extract(StreamingChatMessageContent chunk);

        ThinkingRouteResult Flush();
    }
}
