using System.Text.RegularExpressions;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {
        private static readonly Regex[] NovelArtifactRegexes =
        {
            new(@"我将按照您的", RegexOptions.Compiled),
            new(@"按照您的要求", RegexOptions.Compiled),
            new(@"根据您(供给|提供)的", RegexOptions.Compiled),
            new(@"以下是我(根据|为您|按照)", RegexOptions.Compiled),
            new(@"故事梗概", RegexOptions.Compiled),
            new(@"本次写作(重点|将|主要)", RegexOptions.Compiled),
            new(@"接下来故事(可能|将|会)", RegexOptions.Compiled),
            new(@"这是一个关于.{0,20}的故事", RegexOptions.Compiled),
        };

        private static readonly Regex MarkdownHeaderRegex =
            new(@"^\s*#{1,4}\s+", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex MarkdownBulletOpenerRegex =
            new(@"^\s*[-\*]\s+\*\*[^\*]+\*\*[:：]", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex MarkdownHorizontalRuleRegex =
            new(@"^\s*(?:---+|\*\*\*+|===+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex[] NovelMarkdownRegexes =
        {
            MarkdownHeaderRegex,
            MarkdownBulletOpenerRegex,
            MarkdownHorizontalRuleRegex,
        };
    }
}
