using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Config
{
    public sealed class ConversationModeProfile
    {
        public ChatMode Mode { get; init; }

        public IConversationMessageMapper Mapper { get; init; } = default!;

        public IExecutionResultMapper? ExecutionResultMapper { get; init; }

        public ConversationDisplayPolicy DisplayPolicy { get; init; } = default!;

        public bool RequiresExecutionEngine { get; init; }

        public string Description { get; init; } = string.Empty;
    }
}
