using System;
using System.Collections.Generic;
using TM.Framework.UI.Workspace.RightPanel.Modes;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Models
{
    public sealed class ExecutionResultContext
    {
        public Guid RunId { get; init; }
        public ChatMode Mode { get; init; }
        public TimeSpan Duration { get; init; }
        public string TraceSummaryText { get; init; } = string.Empty;
        public IReadOnlyList<ToolCallRecord> ExecutionTrace { get; init; } = Array.Empty<ToolCallRecord>();
        public string? ChapterId { get; init; }
        public string? ChapterTitle { get; init; }
        public ConversationMessage? OriginalMessage { get; init; }
        public string? ThinkingRaw { get; init; }
        public bool IsCancelled { get; init; }
        public bool IsError { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
