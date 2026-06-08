using System;
using TM.Services.Modules.ProjectData.Implementations;
using System.Reflection;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class WriterPlugin
    {
        private static string CleanGeneratedContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = content.Trim();

            content = ContentCleanHelper.StripModelArtifacts(content);

            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var endIndex = content.IndexOf('\n');
                if (endIndex > 0)
                    content = content.Substring(endIndex + 1);
            }
            if (content.EndsWith("```", StringComparison.Ordinal))
                content = content.Substring(0, content.Length - 3);

            if (content.StartsWith("---", StringComparison.Ordinal))
            {
                var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
                if (endIndex > 0)
                {
                    var nextLineIndex = content.IndexOf('\n', endIndex + 3);
                    if (nextLineIndex > 0)
                        content = content.Substring(nextLineIndex + 1);
                    else
                        content = content.Substring(endIndex + 3);
                }
            }

            var lines = content.Split('\n');
            var cleanedLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                var cleanLine = line;

                if (!cleanLine.TrimStart().StartsWith('#'))
                {
                    cleanLine = BoldStarRegex.Replace(cleanLine, "$1");
                    cleanLine = BoldUnderRegex.Replace(cleanLine, "$1");
                    cleanLine = ItalicStarRegex.Replace(cleanLine, "$1");
                    cleanLine = InlineCodeRegex.Replace(cleanLine, "$1");
                }

                cleanedLines.Add(cleanLine);
            }

            content = string.Join("\n", cleanedLines);

            content = UselessHeadingRegex.Replace(content, string.Empty);

            content = MultipleNewlineRegex.Replace(content, "\n\n");

            return content.Trim();
        }

        private static string CleanContentKeepChanges(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
                return string.Empty;

            var (idx, _) = GenerationGate.FindSeparatorIndex(rawContent);
            if (idx < 0)
                return CleanGeneratedContent(rawContent);

            var contentPart = rawContent.Substring(0, idx).Trim();
            var changesPart = rawContent.Substring(idx).Trim();

            var cleanedContent = CleanGeneratedContent(contentPart);

            if (string.IsNullOrEmpty(changesPart))
                return cleanedContent;

            return $"{cleanedContent}\n\n{changesPart}";
        }

        private static string StripPromptEchoKeepChanges(string rawContentWithChanges, string? expectedTitle)
        {
            if (string.IsNullOrWhiteSpace(rawContentWithChanges))
                return string.Empty;

            var (idx, _) = GenerationGate.FindSeparatorIndex(rawContentWithChanges);
            if (idx < 0)
                return StripPromptEchoFromBody(rawContentWithChanges, expectedTitle);

            var body = rawContentWithChanges.Substring(0, idx).Trim();
            var changesPart = rawContentWithChanges.Substring(idx).Trim();

            var cleanedBody = StripPromptEchoFromBody(body, expectedTitle);
            if (string.IsNullOrWhiteSpace(changesPart))
                return cleanedBody;

            return $"{cleanedBody}\n\n{changesPart}";
        }

        private static string StripPromptEchoFromBody(string body, string? expectedTitle)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            var text = body.TrimStart();

            var m = ChapterHeadingRegex.Match(text);
            if (m.Success)
            {
                return text.Substring(m.Index).TrimStart();
            }

            if (!string.IsNullOrWhiteSpace(expectedTitle))
            {
                var titleLine = "# " + expectedTitle.Trim();
                var idx = text.IndexOf(titleLine, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    return text.Substring(idx).TrimStart();
                }
            }

            var m2 = GenericHeadingRegex.Match(text);
            if (m2.Success)
            {
                return text.Substring(m2.Index).TrimStart();
            }

            return text;
        }

        private static string StripLeadingTitle(string contentWithChanges)
        {
            if (string.IsNullOrWhiteSpace(contentWithChanges))
                return contentWithChanges;

            var (sepIdx, _) = GenerationGate.FindSeparatorIndex(contentWithChanges);

            string body, changesSuffix;
            if (sepIdx >= 0)
            {
                body = contentWithChanges.Substring(0, sepIdx).TrimEnd();
                changesSuffix = "\n\n" + contentWithChanges.Substring(sepIdx).TrimStart();
            }
            else
            {
                body = contentWithChanges;
                changesSuffix = string.Empty;
            }

            var trimmedBody = body.TrimStart();
            if (trimmedBody.Length > 0)
            {
                var firstLineEnd = trimmedBody.IndexOf('\n');
                var firstLine = (firstLineEnd >= 0 ? trimmedBody.Substring(0, firstLineEnd) : trimmedBody).Trim();

                if (firstLine.StartsWith('#'))
                {
                    trimmedBody = firstLineEnd >= 0
                        ? trimmedBody.Substring(firstLineEnd + 1).TrimStart()
                        : string.Empty;
                }
            }

            return $"{trimmedBody}{changesSuffix}";
        }

        private static string BuildCanonicalTabTitle(string chapterId, string? title)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            var chapterNum = parsed?.chapterNumber ?? 0;

            if (chapterNum > 0 && !string.IsNullOrWhiteSpace(title))
                return $"第{chapterNum}章 {title}";
            if (chapterNum > 0)
                return $"第{chapterNum}章";
            if (!string.IsNullOrWhiteSpace(title))
                return title;
            return chapterId;
        }

        private static int CountWords(string text) => WordCountHelper.CountRaw(text);

    }
}
