using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TM.Framework.Common.Helpers
{
    public static class ChapterParserHelper
    {
        #region 正则表达式（预编译提升性能）

        private static readonly Regex VolChRegex = new(@"vol(\d+)_ch(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VCRegex = new(@"v(\d+)_c(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumNumRegex = new(@"(\d+)_(\d+)", RegexOptions.Compiled);
        private static readonly Regex VolOnlyRegex = new(@"vol(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChOnlyRegex = new(@"ch(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChSuffixRegex = new(@"_ch(\d+)$", RegexOptions.Compiled);

        private static readonly Regex VolChapterNLRegex = new(
            @"(?:生成|写|创作|写作|续写|重写|改写|补全|扩写|润色|仿写|改|写到|写至|写完|写完第|完善|修改|开始写|开始生成|帮我写|帮我生成|来写|来生成)?\s*第?\s*([一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇\d]+)\s*卷\s*第?\s*([一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇\d]+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ChapterOnlyNLRegex = new(
            @"(?:生成|写|创作|写作|续写|重写|改写|补全|扩写|润色|仿写|改|写到|写至|写完|写完第|完善|修改|开始写|开始生成|帮我写|帮我生成|来写|来生成)?\s*第?\s*([一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇\d]+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex TypoChapterNLRegex = new(
            @"(?:生成|写|创作|写作|续写|重写|改写|补全|扩写|润色|仿写|完善|修改|开始写|开始生成|帮我写|帮我生成|来写|来生成)\s*第?\s*([一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇\d]+)\s*张",
            RegexOptions.Compiled);

        private static readonly string ChineseNumPattern = @"[一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇]";
        private static readonly string MixedNumPattern = $@"(?:{ChineseNumPattern}+|\d+)";

        private static readonly Regex ChapterTitleRegex = new(
            $@"^第({ChineseNumPattern}+|\d+)(?:章节|章)([：:.]?)\s*(.*)",
            RegexOptions.Compiled);

        private static readonly Regex ChapterTitleDetectRegex = new(
            $@"第{MixedNumPattern}(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ChapterTokenRegex = new(
            $@"第?\s*({MixedNumPattern})\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex CompactChapterListRegex = new(
            $@"第?\s*((?:{MixedNumPattern}\s*(?:[,，、/]|(?:和|与|及))\s*)+{MixedNumPattern})\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex MultiRangeTokenRegex = new(
            $@"(?:从)?第?\s*({MixedNumPattern})\s*(?:章节|章)?\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*第?\s*({MixedNumPattern})\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ArabicRangeRegex = new(
            @"(?:从)?[第]?(\d+)\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*[第]?(\d+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ArabicRangeWithChapterRegex = new(
            @"(?:从)?第(\d+)(?:章节|章)?\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*第?(\d+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ArabicRangeLooseRegex = new(
            @"(\d+)\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*(\d+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ChineseRangeRegex = new(
            $@"(?:从)?第({ChineseNumPattern}+)\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*第?({ChineseNumPattern}+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ChineseRangeWithChapterRegex = new(
            $@"(?:从)?第({ChineseNumPattern}+)(?:章节|章)?\s*[-~～〜—–－−‐‑‒―﹣﹘到至]\s*第?({ChineseNumPattern}+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex SpecialChapterRegex = new(
            @"^(序章|楔子|番外|后记|尾声|引子|终章|大结局)([：:.]?\s*.*)?$",
            RegexOptions.Compiled);

        private static readonly Regex EnglishChapterRegex = new(
            @"^Chapter\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LooseArabicRangeRegex = new(
            @"[第]?(\d+)\s*[^\d]{0,6}[-~～〜—–－−‐‑‒―﹣﹘到至][^\d]{0,6}\s*[第]?(\d+)\s*(?:章节|章)",
            RegexOptions.Compiled);

        private static readonly Regex ListSeparatorRegex = new(
            @"\s*(?:[,，、/]|和|与|及)\s*",
            RegexOptions.Compiled);

        #endregion

        #region 章节ID解析

        public static (int volumeNumber, int chapterNumber)? ParseChapterId(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId))
                return null;

            var match = VolChRegex.Match(chapterId);
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }

            match = VCRegex.Match(chapterId);
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }

            match = NumNumRegex.Match(chapterId);
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }

            return null;
        }

        public static IReadOnlyList<(int start, int end)>? ParseChapterRanges(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            content = NormalizeDashes(content);

            var matches = MultiRangeTokenRegex.Matches(content);
            if (matches.Count < 2)
            {
                return null;
            }

            var ranges = new List<(int start, int end)>();
            foreach (Match m in matches)
            {
                if (!m.Success)
                {
                    continue;
                }

                var rawStart = m.Groups[1].Value;
                var rawEnd = m.Groups[2].Value;

                var start = int.TryParse(rawStart, out var parsedStart) ? parsedStart : ChineseToArabic(rawStart);
                var end = int.TryParse(rawEnd, out var parsedEnd) ? parsedEnd : ChineseToArabic(rawEnd);

                if (start <= 0 || end <= 0)
                {
                    continue;
                }

                if (end < start)
                {
                    (start, end) = (end, start);
                }

                ranges.Add((start, end));
            }

            return ranges.Count > 0 ? ranges : null;
        }

        public static IReadOnlyList<int>? ParseChapterNumberList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (ParseChapterRange(text) != null)
            {
                return null;
            }

            var numbers = new HashSet<int>();

            foreach (Match m in ChapterTokenRegex.Matches(text))
            {
                if (!m.Success)
                {
                    continue;
                }

                var raw = m.Groups[1].Value;
                var n = int.TryParse(raw, out var parsed) ? parsed : ChineseToArabic(raw);
                if (n > 0)
                {
                    numbers.Add(n);
                }
            }

            if (numbers.Count >= 2)
            {
                var list = new List<int>(numbers);
                list.Sort();
                return list;
            }

            var match = CompactChapterListRegex.Match(text);
            if (match.Success)
            {
                var rawList = match.Groups[1].Value;
                var parts = ListSeparatorRegex.Split(rawList);
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part))
                    {
                        continue;
                    }

                    var token = part.Trim();
                    var n = int.TryParse(token, out var parsed) ? parsed : ChineseToArabic(token);
                    if (n > 0)
                    {
                        numbers.Add(n);
                    }
                }
            }

            if (numbers.Count >= 2)
            {
                var list = new List<int>(numbers);
                list.Sort();
                return list;
            }

            return null;
        }

        public static (int volumeNumber, int chapterNumber) ParseChapterIdOrDefault(string chapterId)
        {
            return ParseChapterId(chapterId) ?? (0, 0);
        }

        public static int ExtractVolumeNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var match = VolOnlyRegex.Match(text);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static int ExtractChapterNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var match = ChOnlyRegex.Match(text);
            if (match.Success)
                return int.Parse(match.Groups[1].Value);

            match = ChapterOnlyNLRegex.Match(text);
            if (match.Success)
            {
                var numStr = match.Groups[1].Value;
                return int.TryParse(numStr, out var num) ? num : ChineseToArabic(numStr);
            }

            match = ChapterTitleRegex.Match(text);
            if (match.Success)
            {
                var numStr = match.Groups[1].Value;
                if (int.TryParse(numStr, out var num))
                    return num;
                return ChineseToArabic(numStr);
            }

            return 0;
        }

        public static int ExtractChapterNumberFromSuffix(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId))
                return 0;

            var match = ChSuffixRegex.Match(chapterId);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static string ReplaceChapterNumber(string templateChapterId, int newChapterNumber)
        {
            if (string.IsNullOrEmpty(templateChapterId))
                return templateChapterId;

            return ChSuffixRegex.Replace(templateChapterId, $"_ch{newChapterNumber}");
        }

        #endregion

        #region 自然语言解析

        public static (int? volume, int? chapter) ParseFromNaturalLanguage(string text)
        {
            if (string.IsNullOrEmpty(text))
                return (null, null);

            if (ParseChapterRange(text) != null)
            {
                return (null, null);
            }

            var match = VolChapterNLRegex.Match(text);
            if (match.Success)
            {
                var volStr = match.Groups[1].Value;
                var chStr = match.Groups[2].Value;
                var vol = int.TryParse(volStr, out var v) ? v : ChineseToArabic(volStr);
                var ch = int.TryParse(chStr, out var c) ? c : ChineseToArabic(chStr);
                return (vol > 0 ? vol : null, ch > 0 ? ch : null);
            }

            match = VolChRegex.Match(text);
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }

            match = ChapterOnlyNLRegex.Match(text);
            if (match.Success)
            {
                var chStr = match.Groups[1].Value;
                var ch = int.TryParse(chStr, out var c) ? c : ChineseToArabic(chStr);
                return (null, ch > 0 ? ch : null);
            }

            match = TypoChapterNLRegex.Match(text);
            if (match.Success)
            {
                var chStr = match.Groups[1].Value;
                var ch = int.TryParse(chStr, out var c) ? c : ChineseToArabic(chStr);
                return (null, ch > 0 ? ch : null);
            }

            return (null, null);
        }

        public static string? ParseToChapterId(string text, int defaultVolume = 1)
        {
            var (vol, ch) = ParseFromNaturalLanguage(text);

            if (ch.HasValue)
            {
                var volume = vol ?? defaultVolume;
                return $"vol{volume}_ch{ch.Value}";
            }

            return null;
        }

        public static (int start, int end)? ParseChapterRange(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            content = NormalizeDashes(content);

            var match = ArabicRangeRegex.Match(content);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var start) &&
                int.TryParse(match.Groups[2].Value, out var end))
            {
                if (end < start)
                {
                    (start, end) = (end, start);
                }
                return (start, end);
            }

            match = ArabicRangeWithChapterRegex.Match(content);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out start) &&
                int.TryParse(match.Groups[2].Value, out end))
            {
                if (end < start)
                {
                    (start, end) = (end, start);
                }
                return (start, end);
            }

            match = ArabicRangeLooseRegex.Match(content);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out start) &&
                int.TryParse(match.Groups[2].Value, out end))
            {
                if (end < start)
                {
                    (start, end) = (end, start);
                }
                return (start, end);
            }

            match = ChineseRangeRegex.Match(content);
            if (match.Success)
            {
                var startCh = ChineseToArabic(match.Groups[1].Value);
                var endCh = ChineseToArabic(match.Groups[2].Value);
                if (startCh > 0 && endCh > 0)
                {
                    if (endCh < startCh)
                    {
                        (startCh, endCh) = (endCh, startCh);
                    }
                    return (startCh, endCh);
                }
            }

            match = ChineseRangeWithChapterRegex.Match(content);
            if (match.Success)
            {
                var startCh = ChineseToArabic(match.Groups[1].Value);
                var endCh = ChineseToArabic(match.Groups[2].Value);
                if (startCh > 0 && endCh > 0)
                {
                    if (endCh < startCh)
                    {
                        (startCh, endCh) = (endCh, startCh);
                    }
                    return (startCh, endCh);
                }
            }

            match = LooseArabicRangeRegex.Match(content);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out start) &&
                int.TryParse(match.Groups[2].Value, out end))
            {
                if (end < start)
                {
                    (start, end) = (end, start);
                }
                return (start, end);
            }

            return null;
        }

        #endregion

        #region 章节标题处理

        public static bool IsChapterTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();

            if (ChapterTitleDetectRegex.IsMatch(trimmed))
                return true;

            if (SpecialChapterRegex.IsMatch(trimmed))
                return true;

            if (EnglishChapterRegex.IsMatch(trimmed))
                return true;

            return false;
        }

        public static (int? number, string? name) ExtractChapterParts(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (null, null);

            var trimmed = title.Trim();

            var match = ChapterTitleRegex.Match(trimmed);
            if (match.Success)
            {
                var numStr = match.Groups[1].Value;
                var chapterNum = char.IsDigit(numStr[0])
                    ? int.Parse(numStr)
                    : ChineseToArabic(numStr);
                var chapterName = match.Groups[3].Value.Trim();
                return (chapterNum > 0 ? chapterNum : null, string.IsNullOrEmpty(chapterName) ? null : chapterName);
            }

            var specialMatch = SpecialChapterRegex.Match(trimmed);
            if (specialMatch.Success)
            {
                var specialType = specialMatch.Groups[1].Value;
                var specialName = specialMatch.Groups[2].Value.Trim();
                if (specialName.StartsWith('：') || specialName.StartsWith(':') || specialName.StartsWith('.'))
                    specialName = specialName.Substring(1).Trim();
                return (null, string.IsNullOrEmpty(specialName) ? specialType : $"{specialType} {specialName}");
            }

            var engMatch = EnglishChapterRegex.Match(trimmed);
            if (engMatch.Success)
            {
                var num = int.Parse(engMatch.Groups[1].Value);
                var remaining = trimmed.Substring(engMatch.Length).Trim();
                if (remaining.StartsWith(':') || remaining.StartsWith('-') || remaining.StartsWith('.'))
                    remaining = remaining.Substring(1).Trim();
                return (num, string.IsNullOrEmpty(remaining) ? null : remaining);
            }

            return (null, null);
        }

        public static string NormalizeChapterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title;

            var match = ChapterTitleRegex.Match(title);
            if (match.Success)
            {
                var numStr = match.Groups[1].Value;
                var chapterNum = char.IsDigit(numStr[0])
                    ? int.Parse(numStr)
                    : ChineseToArabic(numStr);

                if (chapterNum > 0)
                {
                    var chapterName = match.Groups[3].Value.Trim();
                    return string.IsNullOrEmpty(chapterName)
                        ? $"第{chapterNum}章"
                        : $"第{chapterNum}章：{chapterName}";
                }
            }

            return title;
        }

        #endregion

        #region 中文数字转换

        private static readonly Dictionary<char, int> ChineseDigits = new()
        {
            ['零'] = 0,
            ['〇'] = 0,
            ['一'] = 1,
            ['壹'] = 1,
            ['二'] = 2,
            ['贰'] = 2,
            ['两'] = 2,
            ['三'] = 3,
            ['叁'] = 3,
            ['四'] = 4,
            ['肆'] = 4,
            ['五'] = 5,
            ['伍'] = 5,
            ['六'] = 6,
            ['陆'] = 6,
            ['七'] = 7,
            ['柒'] = 7,
            ['八'] = 8,
            ['捌'] = 8,
            ['九'] = 9,
            ['玖'] = 9,
            ['十'] = 10,
            ['拾'] = 10,
            ['百'] = 100,
            ['佰'] = 100,
            ['千'] = 1000,
            ['仟'] = 1000,
            ['万'] = 10000,
            ['萬'] = 10000
        };

        public static int ChineseToArabic(string chinese)
        {
            if (string.IsNullOrEmpty(chinese))
                return 0;

            if (int.TryParse(chinese, out var num))
                return num;

            int result = 0;
            int temp = 0;
            int lastUnit = 1;

            for (int i = 0; i < chinese.Length; i++)
            {
                var c = chinese[i];
                if (!ChineseDigits.TryGetValue(c, out var value))
                    continue;

                if (value >= 10)
                {
                    if (temp == 0)
                        temp = 1;

                    if (value >= 10000)
                    {
                        result = (result + temp) * value;
                        temp = 0;
                    }
                    else if (value >= lastUnit)
                    {
                        result += temp * value;
                        temp = 0;
                    }
                    else
                    {
                        temp *= value;
                    }
                    lastUnit = value;
                }
                else
                {
                    temp = value;
                }
            }

            return result + temp;
        }

        #endregion

        #region 辅助方法

        private static string NormalizeDashes(string text) => text
            .Replace('－', '-')
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace('−', '-')
            .Replace('‐', '-')
            .Replace('‑', '-')
            .Replace('‒', '-')
            .Replace('―', '-')
            .Replace('﹣', '-')
            .Replace('﹘', '-')
            .Replace('～', '-')
            .Replace('〜', '-');

        public static int CompareChapterId(string a, string b)
        {
            var partsA = ParseChapterIdOrDefault(a);
            var partsB = ParseChapterIdOrDefault(b);

            if (partsA.volumeNumber != partsB.volumeNumber)
                return partsA.volumeNumber.CompareTo(partsB.volumeNumber);

            return partsA.chapterNumber.CompareTo(partsB.chapterNumber);
        }

        public static string BuildChapterId(int volumeNumber, int chapterNumber)
        {
            return $"vol{volumeNumber}_ch{chapterNumber}";
        }

        public static bool ContainsChapterReference(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return ChapterTitleDetectRegex.IsMatch(text) ||
                   VolChRegex.IsMatch(text) ||
                   SpecialChapterRegex.IsMatch(text);
        }

        #endregion
    }
}
