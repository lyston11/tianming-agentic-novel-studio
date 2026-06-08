using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {

        private static int EstimateTokensFromLength(int charLength)
            => Math.Max(1, (int)(charLength * 0.7));

        public static (bool IsCancelled, string PartialContent) TryExtractCancelledPartial(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return (false, string.Empty);

            const string partialPrefix = "[已取消:部分]";
            if (raw.StartsWith(partialPrefix, StringComparison.Ordinal))
            {
                return (true, raw[partialPrefix.Length..]);
            }

            if (raw.StartsWith("[已取消]", StringComparison.Ordinal))
            {
                return (true, string.Empty);
            }

            return (false, string.Empty);
        }

        private void UpdateThinkingBlocks()
        {
            if (string.IsNullOrWhiteSpace(_thinkingContent))
            {
                ThinkingBlocks = Array.Empty<ThinkingBlock>();
                return;
            }

            const int maxDisplay = 2000;
            var displayContent = _thinkingContent.Length > maxDisplay
                ? _thinkingContent[^maxDisplay..]
                : _thinkingContent;

            var blocks = new List<ThinkingBlock>();
            var currentLines = new List<string>();

            foreach (var line in displayContent.Replace("\r\n", "\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentLines.Count > 0)
                    {
                        AddBlockFromLines(currentLines, blocks);
                        currentLines.Clear();
                    }
                }
                else
                {
                    currentLines.Add(line);
                }
            }

            if (currentLines.Count > 0)
            {
                AddBlockFromLines(currentLines, blocks);
            }

            ThinkingBlocks = blocks;
        }

        private void UpdateChangesBlocks()
        {
            if (string.IsNullOrWhiteSpace(_changesJson))
            {
                ChangesBlocks = Array.Empty<ThinkingBlock>();
                return;
            }

            var pretty = TryFormatJson(_changesJson);
            ChangesBlocks = new[]
            {
                new ThinkingBlock
                {
                    Title = "CHANGES",
                    Body = pretty
                }
            };
        }

        private static string TryFormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryFormatJson), ex);
                return json;
            }
        }

        private static void AddBlockFromLines(List<string> lines, List<ThinkingBlock> blocks)
        {
            if (lines.Count == 0) return;

            var first = lines[0].Trim();
            string title;
            string body;

            if (first.StartsWith('#'))
            {
                title = first.TrimStart('#', ' ').Trim();
                body = string.Join("\n", lines.GetRange(1, lines.Count - 1)).Trim();
            }
            else if (first.EndsWith(':'))
            {
                title = first.TrimEnd(':').Trim();
                body = string.Join("\n", lines.GetRange(1, lines.Count - 1)).Trim();
            }
            else
            {
                title = "Thinking";
                body = string.Join("\n", lines).Trim();
            }

            blocks.Add(new ThinkingBlock
            {
                Title = title,
                Body = body
            });
        }

    }
}
