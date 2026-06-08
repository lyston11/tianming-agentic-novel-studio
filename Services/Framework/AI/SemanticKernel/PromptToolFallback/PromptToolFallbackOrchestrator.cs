using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public sealed class PromptToolFallbackOrchestrator
    {
        public int MaxToolRounds { get; init; } = 8;

        public AuthorRole ResultRole { get; init; } = AuthorRole.User;

        private readonly PromptToolInvoker _invoker = new();

        public async Task<(string Answer, int InputTokens, int OutputTokens)> RunAsync(
            Kernel kernel,
            IChatCompletionService chatService,
            PromptExecutionSettings? settings,
            ChatHistory chatHistory,
            IEnumerable<KernelFunction> allowedFunctions,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(kernel);
            ArgumentNullException.ThrowIfNull(chatService);
            ArgumentNullException.ThrowIfNull(chatHistory);

            var workingHistory = new ChatHistory();
            foreach (var msg in chatHistory)
            {
                workingHistory.Add(msg);
            }

            var instructions = PromptToolPromptBuilder.BuildToolInstructions(allowedFunctions);
            if (!string.IsNullOrEmpty(instructions))
            {
                workingHistory.Insert(0, new ChatMessageContent(AuthorRole.System, instructions));
            }

            string lastAnswer = string.Empty;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            for (int round = 0; round < MaxToolRounds && !ct.IsCancellationRequested; round++)
            {
                var response = await chatService
                    .GetChatMessageContentAsync(workingHistory, settings, kernel, ct)
                    .ConfigureAwait(false);

                var (roundIn, roundOut) = ExtractTokenUsage(response);
                if (roundIn > totalInputTokens) totalInputTokens = roundIn;
                totalOutputTokens += roundOut;

                var rawText = response.Content ?? string.Empty;

                if (!PromptToolParser.ContainsToolUse(rawText))
                {
                    lastAnswer = rawText;
                    break;
                }

                var calls = PromptToolParser.Parse(rawText);
                if (calls.Count == 0)
                {
                    lastAnswer = rawText;
                    break;
                }

                workingHistory.AddAssistantMessage(rawText);

                var results = new List<PromptToolInvocationResult>();
                foreach (var call in calls)
                {
                    if (ct.IsCancellationRequested) break;
                    var result = await _invoker.InvokeAsync(kernel, call, ct).ConfigureAwait(false);
                    results.Add(result);
                }

                var resultText = PromptToolResultFormatter.FormatBatch(results);
                if (!string.IsNullOrEmpty(resultText))
                {
                    workingHistory.AddMessage(ResultRole, resultText);
                }

                lastAnswer = rawText;
            }

            return (StripToolUseBlocks(lastAnswer), totalInputTokens, totalOutputTokens);
        }

        private static (int InputTokens, int OutputTokens) ExtractTokenUsage(ChatMessageContent? response)
        {
            if (response == null) return (0, 0);
            try
            {
                if (response.Metadata != null
                    && response.Metadata.TryGetValue("Usage", out var usageObj)
                    && usageObj is System.Collections.Generic.IDictionary<string, int> usageDict)
                {
                    int inT = 0, outT = 0;
                    usageDict.TryGetValue("InputTokens", out inT);
                    usageDict.TryGetValue("OutputTokens", out outT);
                    if (inT > 0 || outT > 0) return (inT, outT);
                }
                if (response.InnerContent is OpenAI.Chat.ChatCompletion oaiComp && oaiComp.Usage != null)
                {
                    return (oaiComp.Usage.InputTokenCount, oaiComp.Usage.OutputTokenCount);
                }
            }
            catch { }
            return (0, 0);
        }

        public static string StripToolUseBlocks(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new System.Text.StringBuilder();
            int pos = 0;
            while (pos < text!.Length)
            {
                var openIdx = text.IndexOf(PromptToolProtocol.ToolUseOpen, pos, StringComparison.Ordinal);
                if (openIdx < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    break;
                }

                if (openIdx > pos) sb.Append(text, pos, openIdx - pos);

                var closeIdx = text.IndexOf(PromptToolProtocol.ToolUseClose, openIdx, StringComparison.Ordinal);
                if (closeIdx < 0)
                {
                    break;
                }
                pos = closeIdx + PromptToolProtocol.ToolUseClose.Length;
            }
            return sb.ToString().Trim();
        }
    }
}
