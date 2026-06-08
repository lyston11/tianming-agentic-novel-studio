using System;
using System.Collections.Generic;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public static class PromptToolParser
    {
        public static IReadOnlyList<PromptToolCall> Parse(string? modelOutput)
        {
            var result = new List<PromptToolCall>();
            if (string.IsNullOrEmpty(modelOutput)) return result;

            int pos = 0;
            while (pos < modelOutput.Length)
            {
                var openIdx = modelOutput.IndexOf(PromptToolProtocol.ToolUseOpen, pos, StringComparison.Ordinal);
                if (openIdx < 0) break;

                var contentStart = openIdx + PromptToolProtocol.ToolUseOpen.Length;
                var closeIdx = modelOutput.IndexOf(PromptToolProtocol.ToolUseClose, contentStart, StringComparison.Ordinal);
                if (closeIdx < 0)
                {
                    var unclosed = modelOutput.Substring(contentStart);
                    var partial = ExtractCall(unclosed);
                    if (partial != null) result.Add(partial);
                    break;
                }

                var inner = modelOutput.Substring(contentStart, closeIdx - contentStart);
                var call = ExtractCall(inner);
                if (call != null) result.Add(call);

                pos = closeIdx + PromptToolProtocol.ToolUseClose.Length;
            }

            return result;
        }

        public static bool ContainsToolUse(string? modelOutput)
        {
            return !string.IsNullOrEmpty(modelOutput)
                && modelOutput!.Contains(PromptToolProtocol.ToolUseOpen, StringComparison.Ordinal);
        }

        private static PromptToolCall? ExtractCall(string innerXml)
        {
            if (string.IsNullOrWhiteSpace(innerXml)) return null;

            var name = ExtractBetween(innerXml, PromptToolProtocol.ToolNameOpen, PromptToolProtocol.ToolNameClose);
            var args = ExtractBetween(innerXml, PromptToolProtocol.ArgumentsOpen, PromptToolProtocol.ArgumentsClose);

            if (string.IsNullOrWhiteSpace(name)) return null;

            return new PromptToolCall(
                ToolName: XmlUnescape(name!.Trim()),
                ArgumentsJson: XmlUnescape((args ?? string.Empty).Trim()));
        }

        private static string? ExtractBetween(string source, string openTag, string closeTag)
        {
            var s = source.IndexOf(openTag, StringComparison.Ordinal);
            if (s < 0) return null;
            var contentStart = s + openTag.Length;
            var e = source.IndexOf(closeTag, contentStart, StringComparison.Ordinal);
            if (e < 0) return null;
            return source.Substring(contentStart, e - contentStart);
        }

        public static string XmlUnescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&");
        }
    }

    public sealed record PromptToolCall(string ToolName, string ArgumentsJson);
}
