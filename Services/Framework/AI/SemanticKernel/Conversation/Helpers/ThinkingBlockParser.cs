using System;
using System.Collections.Generic;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers
{
    public static class ThinkingBlockParser
    {
        public static IReadOnlyList<ThinkingBlock> Parse(string? thinkingContent)
        {
            if (string.IsNullOrWhiteSpace(thinkingContent))
            {
                return Array.Empty<ThinkingBlock>();
            }

            var blocks = new List<ThinkingBlock>();
            var lines = thinkingContent.Split('\n');
            var currentLines = new List<string>();
            string? currentTitle = null;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                if (IsHeadingLine(trimmed))
                {
                    if (currentLines.Count > 0 || currentTitle != null)
                    {
                        AddBlock(currentTitle, currentLines, blocks);
                        currentLines.Clear();
                    }

                    currentTitle = ExtractTitle(trimmed);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    currentLines.Add(line);
                }
            }

            if (currentLines.Count > 0 || currentTitle != null)
            {
                AddBlock(currentTitle, currentLines, blocks);
            }

            return blocks;
        }

        private static bool IsHeadingLine(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
                return false;

            if (trimmed.StartsWith('#'))
                return true;

            if (trimmed.Length <= 20 && (trimmed.EndsWith('：') || trimmed.EndsWith(':')))
                return true;

            if (trimmed.Length >= 3 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
                return true;

            return false;
        }

        private static string ExtractTitle(string line)
        {
            var trimmed = line.TrimStart();

            while (trimmed.StartsWith('#'))
            {
                trimmed = trimmed.Substring(1);
            }

            trimmed = trimmed.TrimEnd('：', ':');

            return trimmed.Trim();
        }

        private static void AddBlock(string? title, List<string> lines, List<ThinkingBlock> blocks)
        {
            var body = string.Join("\n", lines).Trim();

            if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(body))
            {
                title = "分析";
            }

            if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(body))
            {
                blocks.Add(new ThinkingBlock
                {
                    Title = title ?? string.Empty,
                    Body = body
                });
            }
        }
    }
}
