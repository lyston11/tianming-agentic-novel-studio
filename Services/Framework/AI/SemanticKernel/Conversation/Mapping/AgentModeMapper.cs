using System;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public class AgentModeMapper : IConversationMessageMapper
    {
        public System.Threading.Tasks.Task<ConversationMessage> MapFromStreamingResultAsync(
            string userInput,
            string rawContent,
            string? thinking)
            => System.Threading.Tasks.Task.FromResult(new ConversationMessage
            {
                Role = Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant,
                Timestamp = DateTime.Now,
                Summary = rawContent,
                AnalysisRaw = thinking ?? string.Empty,
                AnalysisBlocks = ThinkingBlockParser.Parse(thinking),
                Payload = new AgentPayload()
            });

        public string GenerateSummary(ConversationMessage message)
        {
            return message.Summary;
        }
    }
}
