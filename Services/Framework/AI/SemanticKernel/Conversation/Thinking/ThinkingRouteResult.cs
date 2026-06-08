namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking
{
    public readonly struct ThinkingRouteResult
    {
        public string? ThinkingContent { get; init; }

        public string? ThinkingKind { get; init; }

        public string? AnswerContent { get; init; }
    }
}
