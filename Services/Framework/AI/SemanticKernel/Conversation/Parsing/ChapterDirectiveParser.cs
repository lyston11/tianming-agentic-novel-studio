using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing
{
    public static class ChapterDirectiveParser
    {
        private static readonly Regex ContinuePattern = new(
            @"@(?:续写|continue)[:：\s]*(\S+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RewritePattern = new(
            @"@(?:重写|rewrite)[:：\s]*(\S+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string? ParseSourceChapterId(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return null;

            var match = ContinuePattern.Match(detail);
            if (match.Success && match.Groups.Count > 1)
            {
                var chapterId = match.Groups[1].Value.Trim();
                return string.IsNullOrEmpty(chapterId) ? null : chapterId;
            }

            return null;
        }

        public static string? ParseTargetChapterId(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return null;

            var match = RewritePattern.Match(detail);
            if (match.Success && match.Groups.Count > 1)
            {
                var chapterId = match.Groups[1].Value.Trim();
                return string.IsNullOrEmpty(chapterId) ? null : chapterId;
            }

            return null;
        }

        public static bool HasContinueDirective(string? detail)
        {
            return !string.IsNullOrWhiteSpace(detail) && ContinuePattern.IsMatch(detail);
        }

        public static bool HasRewriteDirective(string? detail)
        {
            return !string.IsNullOrWhiteSpace(detail) && RewritePattern.IsMatch(detail);
        }
    }
}
