using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TM.Framework.Common.Helpers
{
    public enum EntityMatchMode
    {
        Lenient,

        Strict
    }

    public class EntityMatchResult
    {
        public string Input { get; set; } = string.Empty;
        public string? Matched { get; set; }
        public bool IsMatched { get; set; }
        public string MatchType { get; set; } = "None";
    }

    public static class EntityNameNormalizeHelper
    {
        private static readonly Regex BracketAnnotationRegex = new(@"\s*[\(（\[【].*?[\)）\]】]\s*$", RegexOptions.Compiled);
        private static readonly Regex AliasInBracketsRegex = new(@"[\(（\[【](.+?)[\)）\]】]", RegexOptions.Compiled);
        private static readonly Regex LeadingNumberPrefixRegex = new(@"^\s*\d+\s*[\.、\-:\)）]\s*", RegexOptions.Compiled);
        private static readonly Regex ChapterPrefixRegex = new(@"^.{0,30}?[-_—–\s]+(?=第\s*[\d一二三四五六七八九十百千零]+\s*[章卷])", RegexOptions.Compiled);
        private static readonly Regex VolumeChapterPrefixRegex = new(@"^\s*第\s*\d+\s*卷\s*[-_\s]*第\s*\d+\s*章\s*[-_\s]*", RegexOptions.Compiled);
        private static readonly Regex ChineseVolumeChapterPrefixRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*卷\s*[-_\s]*第\s*[一二三四五六七八九十百千零]+\s*章\s*[-_\s]*", RegexOptions.Compiled);
        private static readonly Regex ChapterPrefixShortRegex = new(@"^\s*第\s*\d+\s*[章卷]\s*[：:、\-—–_]*\s*", RegexOptions.Compiled);
        private static readonly Regex ChineseChapterPrefixRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*[章卷]\s*[：:、\-—–_]*\s*", RegexOptions.Compiled);
        private static readonly Regex SceneBlueprintPrefixRegex = new(@"^\s*场景蓝图[-_]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SceneNumberPrefixRegex = new(@"^\s*场景\s*[-_]?\d+(?:-\d+)?\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VolChPrefixRegex = new(@"^\s*vol\s*\d+\s*[_-]?ch\s*\d+\s*[-_]*\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChPrefixRegex = new(@"^\s*ch\s*\d+\s*[-_]*\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SceneRefRegex = new(@"(^|[-_\s])scene\s*[-_]?\d+(?:-\d+)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VolChRefRegex = new(@"(^|[-_\s])vol\d+(_?ch\d+)?(-\d+)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        #region 核心匹配方法（带模式参数）

        public static string NormalizeSingle(string value, IEnumerable<string> candidates, EntityMatchMode mode)
        {
            var result = MatchSingleCore(value, candidates);
            if (result.IsMatched)
                return result.Matched!;

            return mode == EntityMatchMode.Strict ? string.Empty : result.Input;
        }

        public static string NormalizeSingle(string value, IEnumerable<string> candidates)
        {
            return NormalizeSingle(value, candidates, EntityMatchMode.Lenient);
        }

        public static string NormalizeMultiple(string value, IEnumerable<string> candidates, EntityMatchMode mode, string separator = "、")
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var names = SplitNames(value);
            if (names.Count == 0)
                return string.Empty;

            var matched = new List<string>();
            foreach (var name in names)
            {
                var normalized = NormalizeSingle(name, candidates, mode);
                if (!string.IsNullOrWhiteSpace(normalized))
                    matched.Add(normalized);
            }

            return string.Join(separator, matched.Distinct());
        }

        public static string NormalizeMultiple(string value, IEnumerable<string> candidates, string separator = "、")
        {
            return NormalizeMultiple(value, candidates, EntityMatchMode.Lenient, separator);
        }

        #endregion

        #region 严格过滤便捷方法（语义更清晰）

        public static string FilterToCandidate(string value, IEnumerable<string> candidates)
        {
            return NormalizeSingle(value, candidates, EntityMatchMode.Strict);
        }

        public static string FilterToCandidates(string value, IEnumerable<string> candidates, string separator = "、")
        {
            return NormalizeMultiple(value, candidates, EntityMatchMode.Strict, separator);
        }

        #endregion

        #region 诊断方法（用于校验和提示）

        public static EntityMatchResult MatchSingle(string value, IEnumerable<string> candidates)
        {
            return MatchSingleCore(value, candidates);
        }

        public static List<EntityMatchResult> MatchMultiple(string value, IEnumerable<string> candidates)
        {
            var results = new List<EntityMatchResult>();
            if (string.IsNullOrWhiteSpace(value))
                return results;

            var names = SplitNames(value);
            foreach (var name in names)
            {
                results.Add(MatchSingleCore(name, candidates));
            }

            return results;
        }

        private static EntityMatchResult MatchSingleCore(string value, IEnumerable<string> candidates)
        {
            var result = new EntityMatchResult();

            if (string.IsNullOrWhiteSpace(value))
            {
                result.Input = string.Empty;
                result.IsMatched = false;
                result.MatchType = "Empty";
                return result;
            }

            var normalized = value.Trim();
            result.Input = normalized;

            if (IsIgnoredValue(normalized))
            {
                result.IsMatched = true;
                result.Matched = string.Empty;
                result.MatchType = "Ignored";
                return result;
            }

            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0)
            {
                result.IsMatched = false;
                result.MatchType = "NoCandidates";
                return result;
            }

            var exact = list.FirstOrDefault(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exact))
            {
                result.IsMatched = true;
                result.Matched = exact;
                result.MatchType = "Exact";
                return result;
            }

            var normalizedCn = NormalizeChineseOrdinals(normalized);
            if (!string.Equals(normalizedCn, normalized, StringComparison.Ordinal))
            {
                var cnMatch = list.FirstOrDefault(c =>
                    string.Equals(c, normalizedCn, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeChineseOrdinals(c), normalizedCn, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(cnMatch))
                {
                    result.IsMatched = true;
                    result.Matched = cnMatch;
                    result.MatchType = "ChineseOrdinal";
                    return result;
                }
            }

            var contains = list.FirstOrDefault(c => NameExistsInContent(c, normalized) || NameExistsInContent(normalized, c));
            if (!string.IsNullOrEmpty(contains))
            {
                result.IsMatched = true;
                result.Matched = contains;
                result.MatchType = "Contains";
                return result;
            }

            string? bestMatch = null;
            int bestLen = 1;
            foreach (var candidate in list)
            {
                int lcs = LongestCommonSubstringLength(normalized, candidate);
                if (lcs > bestLen)
                {
                    bestLen = lcs;
                    bestMatch = candidate;
                }
            }
            if (bestMatch != null)
            {
                result.IsMatched = true;
                result.Matched = bestMatch;
                result.MatchType = "Substring";
                return result;
            }

            result.IsMatched = false;
            result.MatchType = "None";
            return result;
        }

        public static bool IsIgnoredValue(string value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return string.Equals(normalized, "无", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "暂无", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "空", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无所属", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无角色", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无地点", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无势力", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无物品", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无相关", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无关联", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "不适用", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "未定", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "待定", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "未知", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "不详", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "没有", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "略", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "省略", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "NA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "-", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        public static List<string> SplitNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();

            return value
                .Split(new[] { ',', '，', '、', '\n', '\r', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        public static string StripBracketAnnotation(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var t = name.Trim();
            t = BracketAnnotationRegex.Replace(t, string.Empty);
            return t.Trim();
        }

        public static bool NameExistsInContent(string content, string fullName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(fullName))
                return false;

            if (content.Contains(fullName, StringComparison.OrdinalIgnoreCase))
                return true;

            var primaryName = StripBracketAnnotation(fullName);
            if (!string.IsNullOrWhiteSpace(primaryName) && primaryName != fullName
                && content.Contains(primaryName, StringComparison.OrdinalIgnoreCase))
                return true;

            var aliasMatches = AliasInBracketsRegex.Matches(fullName);
            foreach (Match m in aliasMatches)
            {
                var alias = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(alias) && alias.Length >= 2
                    && content.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string NormalizeBatchEntityName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var t = name.Trim();
            t = LeadingNumberPrefixRegex.Replace(t, string.Empty);
            t = ChapterPrefixRegex.Replace(t, string.Empty);
            t = VolumeChapterPrefixRegex.Replace(t, string.Empty);
            t = ChineseVolumeChapterPrefixRegex.Replace(t, string.Empty);
            t = ChapterPrefixShortRegex.Replace(t, string.Empty);
            t = ChineseChapterPrefixRegex.Replace(t, string.Empty);
            t = SceneBlueprintPrefixRegex.Replace(t, string.Empty);
            t = SceneNumberPrefixRegex.Replace(t, string.Empty);
            t = VolChPrefixRegex.Replace(t, string.Empty);
            t = ChPrefixRegex.Replace(t, string.Empty);
            t = SceneRefRegex.Replace(t, " ");
            t = VolChRefRegex.Replace(t, " ");
            t = t.Replace("__", " ").Replace("--", " ");
            t = t.Trim(' ', '-', '_');
            return t.Trim();
        }

        private static readonly Regex ChineseOrdinalPattern = new(
            @"第([〇零一二两三四五六七八九十百千万]+)([卷章节期集部篇回])",
            RegexOptions.Compiled);

        private static string NormalizeChineseOrdinals(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
            return ChineseOrdinalPattern.Replace(input, m =>
            {
                var num = ParseChineseNumber(m.Groups[1].Value);
                return num > 0 ? $"第{num}{m.Groups[2].Value}" : m.Value;
            });
        }

        private static int ParseChineseNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;

            int result = 0, current = 0;
            foreach (var c in s)
            {
                switch (c)
                {
                    case '〇':
                    case '零': current = 0; break;
                    case '一': current = 1; break;
                    case '二':
                    case '两': current = 2; break;
                    case '三': current = 3; break;
                    case '四': current = 4; break;
                    case '五': current = 5; break;
                    case '六': current = 6; break;
                    case '七': current = 7; break;
                    case '八': current = 8; break;
                    case '九': current = 9; break;
                    case '十':
                        result += (current == 0 ? 1 : current) * 10;
                        current = 0;
                        break;
                    case '百':
                        result += (current == 0 ? 1 : current) * 100;
                        current = 0;
                        break;
                    case '千':
                        result += (current == 0 ? 1 : current) * 1000;
                        current = 0;
                        break;
                    case '万':
                        result += (current == 0 ? 1 : current) * 10000;
                        current = 0;
                        break;
                    default:
                        return -1;
                }
            }
            result += current;
            return result > 0 ? result : -1;
        }

        private static int LongestCommonSubstringLength(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            int maxLen = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    int len = 0;
                    while (i + len < a.Length && j + len < b.Length &&
                           char.ToLowerInvariant(a[i + len]) == char.ToLowerInvariant(b[j + len]))
                    {
                        len++;
                    }
                    if (len > maxLen)
                        maxLen = len;
                }
            }
            return maxLen;
        }

        public static List<string> GetUnmatchedNames(string value, IEnumerable<string> candidates)
        {
            var names = SplitNames(value);
            if (names.Count == 0)
                return new List<string>();

            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0)
                return new List<string>();

            bool IsMatched(string name)
            {
                var r = MatchSingleCore(name, list);
                return r.IsMatched;
            }

            return names.Where(n => !IsMatched(n)).Distinct().ToList();
        }
    }
}
