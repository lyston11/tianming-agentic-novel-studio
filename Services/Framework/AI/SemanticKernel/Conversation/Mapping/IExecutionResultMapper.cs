using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public interface IExecutionResultMapper
    {
        ConversationMessage MapExecutionResult(ExecutionResultContext context);
    }
}
