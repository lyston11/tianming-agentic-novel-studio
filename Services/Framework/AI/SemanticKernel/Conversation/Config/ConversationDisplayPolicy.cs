using System;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Config
{
    public sealed class ConversationDisplayPolicy
    {
        public Func<ConversationMessage, string> SummarySelector { get; init; } = msg => msg.Summary;

        public bool ShowAnalysis { get; init; } = true;

        public bool AnalysisExpandedByDefault { get; init; } = false;

        public string? DefaultPayloadTarget { get; init; }

        public bool HideRawContentInBubble { get; init; } = false;
    }
}
