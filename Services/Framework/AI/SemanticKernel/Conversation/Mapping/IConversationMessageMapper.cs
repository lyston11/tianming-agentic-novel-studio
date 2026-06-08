using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public interface IConversationMessageMapper
    {
        System.Threading.Tasks.Task<ConversationMessage> MapFromStreamingResultAsync(
            string userInput,
            string rawContent,
            string? thinking);

        string GenerateSummary(ConversationMessage message);
    }
}
