using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        #region 工厂方法

        public static UIMessageItem FromChatMessageContent(ChatMessageContent message)
        {
            var item = new UIMessageItem
            {
                Role = message.Role,
                Content = message.Content ?? string.Empty,
                ModelName = message.ModelId
            };

            if (message.Metadata?.TryGetValue("Thinking", out var thinking) == true)
            {
                item.ThinkingContent = thinking?.ToString() ?? string.Empty;
            }

            if (message.Metadata?.TryGetValue("Usage", out var usage) == true && usage is System.Collections.Generic.IDictionary<string, int> usageDict)
            {
                if (usageDict.TryGetValue("InputTokens", out var inT)) item.InputTokens = inT;
                if (usageDict.TryGetValue("OutputTokens", out var outT)) item.OutputTokens = outT;
                item.TokenCount = item.InputTokens + item.OutputTokens;
            }

            return item;
        }

        public static UIMessageItem CreateUserMessage(string content)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.User,
                Content = content
            };
        }

        public static UIMessageItem CreateAssistantPlaceholder()
        {
            return new UIMessageItem
            {
                Role = AuthorRole.Assistant,
                Content = string.Empty,
                IsStreaming = true
            };
        }

        public static UIMessageItem CreateErrorMessage(string error)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.Assistant,
                Content = error,
                IsError = true
            };
        }

        public static UIMessageItem CreateSystemMessage(string content)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.System,
                Content = content
            };
        }

        #endregion
    }
}
