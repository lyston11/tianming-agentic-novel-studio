#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        #region 会话管理

        public async System.Threading.Tasks.Task SwitchSessionAsync(string sessionId)
        {
            var records = await Sessions.LoadMessagesAsync(sessionId).ConfigureAwait(false);

            _chatHistory = Sessions.SwitchSessionWithRecords(sessionId, records);
            _turnIndex = 0;
            _isSessionCompressed = false;

            TM.App.Log($"[SKChatService] 已切换到会话: {sessionId}");
        }

        public ChatHistory GetChatHistory() => _chatHistory;

        #region R1

        public void SaveMessages(IEnumerable<UIMessageItem> messages)
        {
            var records = messages.Select(m => m.ToSerializedRecord()).ToList();
            Sessions.SaveCurrentMessages(records);

            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (!string.IsNullOrEmpty(sessionId))
            {
                Sessions.UpdateSessionMode(sessionId, ((int)_currentMode).ToString());
            }

            _chatHistory = Sessions.RebuildChatHistory(records);
        }

        public async System.Threading.Tasks.Task<List<SerializedMessageRecord>> LoadMessagesAsync()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
                return new List<SerializedMessageRecord>();
            return await Sessions.LoadMessagesAsync(sessionId).ConfigureAwait(false);
        }

        public void RebuildHistoryFromMessages(IEnumerable<UIMessageItem> messages)
        {
            var list = messages.ToList();
            var records = list.Select(m => m.ToSerializedRecord()).ToList();
            _chatHistory = Sessions.RebuildChatHistory(records);

            int turnCount = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].IsUser && list[i + 1].IsAssistant)
                {
                    turnCount++;
                    i++;
                }
            }
            _turnIndex = turnCount;

            TM.App.Log($"[SKChatService] 重建 ChatHistory，消息数: {_chatHistory.Count}");
        }

        #endregion

        private static bool IsLocalEndpoint(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "localhost"
                || host == "127.0.0.1"
                || host == "::1"
                || host.StartsWith("192.168.", StringComparison.Ordinal)
                || host.StartsWith("10.", StringComparison.Ordinal)
                || (host.StartsWith("172.", StringComparison.Ordinal) && System.Net.IPAddress.TryParse(host, out var ip)
                    && ip.GetAddressBytes() is { } b && b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }

        private static string EnsureApiVersion(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            var normalized = url.TrimEnd('/');
            if (ApiVersionPathRegex.IsMatch(normalized))
                return normalized;
            return normalized + "/v1";
        }

        private static string ResolveProtocol(string? baseUrl, string providerName)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (baseUrl.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase))
                    return "anthropic";

                if (baseUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                    baseUrl.Contains("generativelanguage.google", StringComparison.OrdinalIgnoreCase) ||
                    baseUrl.Contains("aiplatform.google", StringComparison.OrdinalIgnoreCase))
                    return "gemini";

                if (baseUrl.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
                    baseUrl.Contains(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase))
                    return "azure-openai";

                return "openai-compat";
            }

            if (providerName.Contains("anthropic", StringComparison.OrdinalIgnoreCase) || providerName.Contains("claude", StringComparison.OrdinalIgnoreCase))
                return "anthropic";

            if (providerName.Contains("gemini", StringComparison.OrdinalIgnoreCase) || providerName.Contains("google", StringComparison.OrdinalIgnoreCase))
                return "gemini";

            if (providerName.Contains("azure", StringComparison.OrdinalIgnoreCase))
                return "azure-openai";

            return "openai-compat";
        }

        private static string StripModelNamePrefix(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return modelName;

            while (modelName.StartsWith('['))
            {
                var closeBracket = modelName.IndexOf(']');
                if (closeBracket < 0 || closeBracket >= modelName.Length - 1)
                    break;
                modelName = modelName[(closeBracket + 1)..];
            }

            if (modelName.EndsWith("-free", StringComparison.OrdinalIgnoreCase))
            {
                modelName = modelName[..^5];
            }

            return modelName;
        }

        private static string StripLongContextSuffix(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return modelName;
            if (modelName.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
                return modelName.Substring(0, modelName.Length - 4);
            if (modelName.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
                return modelName.Substring(0, modelName.Length - 9);
            return modelName;
        }

        #endregion

    }
}
