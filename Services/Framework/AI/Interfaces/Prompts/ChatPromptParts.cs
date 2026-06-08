namespace TM.Services.Framework.AI.Interfaces.Prompts
{
    public sealed class ChatPromptParts
    {
        public string SystemPrompt { get; init; } = string.Empty;

        public string UserPrompt { get; init; } = string.Empty;
    }
}
