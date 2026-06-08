using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class ChatPromptBridge
    {

        public ChatPromptParts BuildPromptParts(ChatMode mode, string userInput)
        {
            userInput ??= string.Empty;

            return Prompts.PromptLibrary.BuildSimplePromptParts(mode, userInput);
        }

        public string BuildPrompt(ChatMode mode, string userInput)
        {
            var parts = BuildPromptParts(mode, userInput);

            if (string.IsNullOrWhiteSpace(parts.SystemPrompt))
                return parts.UserPrompt;

            if (string.IsNullOrWhiteSpace(parts.UserPrompt))
                return parts.SystemPrompt;

            return parts.SystemPrompt + "\n\n" + parts.UserPrompt;
        }

        #region 静态便捷访问

        private static ChatPromptBridge? _staticInstance;
        private static ChatPromptBridge StaticInstance => _staticInstance ??= ServiceLocator.Get<ChatPromptBridge>();

        public static ChatPromptParts BuildParts(ChatMode mode, string userInput)
            => StaticInstance.BuildPromptParts(mode, userInput);

        public static string Build(ChatMode mode, string userInput)
            => StaticInstance.BuildPrompt(mode, userInput);

        #endregion

    }
}
