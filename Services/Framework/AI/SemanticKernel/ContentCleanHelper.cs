using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.SemanticKernel
{
    internal static partial class ContentCleanHelper
    {
        private static readonly char[] LeadingNoiseChars = ['\uFEFF', '\u200B', '\u200C', '\u200D', '\u2060'];

        [GeneratedRegex(@"<\s*(think|thinking|analysis|reasoning|thought|reflection|scratchpad)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase)]
        private static partial Regex TagBlockRegex();

        [GeneratedRegex(@"```[\t ]*(?:think|thinking|analysis|reasoning|thought|reflection|scratchpad)\b[\s\S]*?```", RegexOptions.IgnoreCase)]
        private static partial Regex FencedBlockRegex();

        [GeneratedRegex(@"(?m)^\s*</?\s*(think|thinking|analysis|reasoning|thought|reflection|scratchpad)\b[^>]*>\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex OrphanTagRegex();

        [GeneratedRegex(@"</\s*(think|thinking|analysis|reasoning|thought|reflection|scratchpad)\b[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex InlineOrphanCloseTagRegex();

        [GeneratedRegex(@"(?s)^[\s\S]{0,300}?(?=\s*#\s*第\s*[0-9一二三四五六七八九十百千]+\s*章)")]
        private static partial Regex PrefixNoiseRegex();

        [GeneratedRegex(@"(?m)^\s*#\s*第\s*[0-9一二三四五六七八九十百千]+\s*章.*$")]
        private static partial Regex ChapterHeadingRegex();

        public static string StripModelArtifacts(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = content.TrimStart(LeadingNoiseChars).TrimStart();
            content = TagBlockRegex().Replace(content, string.Empty);
            content = FencedBlockRegex().Replace(content, string.Empty);
            content = OrphanTagRegex().Replace(content, string.Empty);
            content = InlineOrphanCloseTagRegex().Replace(content, string.Empty);
            content = PrefixNoiseRegex().Replace(content, string.Empty);

            var m = ChapterHeadingRegex().Match(content);
            if (m.Success)
                content = content.Substring(m.Index).TrimStart();

            return content.Trim();
        }
    }
}
