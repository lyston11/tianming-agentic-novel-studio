using System.Text.RegularExpressions;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {

        private static readonly Regex EllipsisRegex = new(
            @"\.{3,}|\u2026+",
            RegexOptions.Compiled);

        public static string NormalizeEllipsis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return EllipsisRegex.Replace(text, "\u2026\u2026");
        }

        private static readonly Regex DashRegex = new(
            @"-{2,}|[\u2013\u2014\u2015]+",
            RegexOptions.Compiled);

        public static string NormalizeDash(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return DashRegex.Replace(text, "\u2014\u2014");
        }
    }
}
