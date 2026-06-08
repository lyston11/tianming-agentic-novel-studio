using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers
{
    public static class SingleChapterTaskDetector
    {
        private const string ChapterNumChars = @"[\d一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇]+";
        private static readonly Regex ChapterRangePattern = new(
            $@"[第]?{ChapterNumChars}\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*[第]?{ChapterNumChars}\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex TypoChapterPattern = new(
            $@"(?:生成|写|创作|创建|新建|建立|续写|重写|改写|补全|扩写|润色|仿写|完善|修改)\s*第?\s*{ChapterNumChars}\s*张",
            RegexOptions.Compiled);

        private static readonly string[] BatchKeywords =
        {
            "批量", "多章", "几章", "所有章", "全部章", "所有章节", "全部章节", "连续", "章到", "~", "-到", "-至", "到第", "至第"
        };

        private static readonly string[] SingleKeywords =
        {
            "生成第", "写第", "创作第", "创建第", "新建第", "建立第",
            "续写第", "完善第", "扩写第", "修改第",
            "重写第", "改写第", "润色第", "开始写第", "开始生成第", "开始创建第",
            "帮我写第", "帮我生成第", "帮我创建第", "来写第", "来生成第", "来创建第"
        };

        private static readonly string[] NaturalSingleKeywords =
        {
            "接着写", "接着续", "写下一章", "写这一章", "写这章",
            "把这章写", "把这一章写", "把下一章写", "帮我写这章",
            "帮我写下一章", "帮我写这一章", "续这章", "续下一章",
            "接着生成", "继续写这章", "继续写下一章"
        };

        private static readonly Regex ActionChapterPattern = new(
            $@"(?:生成|写|创作|创建|新建|建立|续写|重写|改写|补全|扩写|润色|仿写|完善|修改)\s*{ChapterNumChars}\s*(?:章节|章)",
            RegexOptions.Compiled);

        public static bool IsSingleChapterTask(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return false;

            var normalized = userInput.Replace(" ", "").ToLowerInvariant();

            if (ChapterRangePattern.IsMatch(userInput))
                return false;

            if (ChapterParserHelper.ParseChapterNumberList(userInput) != null)
                return false;

            foreach (var kw in BatchKeywords)
            {
                if (normalized.Contains(kw))
                    return false;
            }

            if (TypoChapterPattern.IsMatch(userInput))
                return true;

            if (ActionChapterPattern.IsMatch(userInput))
                return true;

            foreach (var kw in SingleKeywords)
            {
                if (normalized.Contains(kw) && (normalized.Contains('章') || normalized.Contains("章节")))
                    return true;
            }

            if (normalized.Contains('第') && (normalized.Contains('章') || normalized.Contains("章节")))
            {
                foreach (var kw in SingleKeywords)
                {
                    if (normalized.Contains(kw))
                        return true;
                }
            }

            foreach (var kw in NaturalSingleKeywords)
            {
                if (normalized.Contains(kw))
                    return true;
            }

            return false;
        }
    }
}
