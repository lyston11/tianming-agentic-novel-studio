using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {

        private const double AiPhraseReplaceRate = 0.85;

        private const double WordReplaceRate = 0.50;

        private const double NovelOralReplaceRate = 0.50;

        private static string RemoveParagraphsMatching(string text, Regex[] patterns)
        {
            var normalized = text.Replace("\r\n", "\n");
            var paragraphs = normalized.Split(new[] { "\n\n" }, StringSplitOptions.None);

            var kept = new List<string>(paragraphs.Length);
            foreach (var p in paragraphs)
            {
                bool hit = false;
                foreach (var rx in patterns)
                {
                    if (rx.IsMatch(p)) { hit = true; break; }
                }
                if (!hit) kept.Add(p);
            }
            return string.Join("\n\n", kept);
        }

        private static IReadOnlyList<string> BuildAvailable(string[] safe, HashSet<string> used)
        {
            if (used.Count == 0) return safe;

            var avail = new List<string>(safe.Length);
            foreach (var c in safe)
            {
                if (!used.Contains(c)) avail.Add(c);
            }
            if (avail.Count > 0) return avail;

            used.Clear();
            return safe;
        }

        private static string[] FilterSafeCandidates(string[] candidates)
        {
            return candidates
                .Where(static c => !AiBlacklist.Contains(c) && !NovelBlacklist.Contains(c))
                .ToArray();
        }

        private static string ApplyDigitNormalization(string text)
        {
            text = DigitRule_Year4DigitStrict.Replace(text,
                static m => DigitConcat(m.Groups[1].Value));

            text = DigitRule_YearDecade.Replace(text,
                static m => ChineseDecadeMap.TryGetValue(m.Groups[1].Value, out var d)
                    ? d + "年代"
                    : m.Value);

            text = DigitRule_MonthStrict.Replace(text,
                static m => SmallNumberToChinese(m.Groups[1].Value));

            text = DigitRule_DayStrict.Replace(text,
                static m => SmallNumberToChinese(m.Groups[1].Value));

            return text;
        }

        private static string DigitConcat(string digits)
        {
            var sb = new StringBuilder(digits.Length);
            foreach (var c in digits)
                sb.Append(DigitToChineseMap.TryGetValue(c, out var ch) ? ch : c);
            return sb.ToString();
        }

        private static string SmallNumberToChinese(string s)
        {
            if (!int.TryParse(s, out var n) || n <= 0) return s;

            if (n < 10)
                return DigitToChineseMap.TryGetValue(s[0], out var c1)
                    ? c1.ToString()
                    : s;

            if (n == 10) return "十";

            if (n < 20)
            {
                var ones = (char)('0' + (n - 10));
                return DigitToChineseMap.TryGetValue(ones, out var c2)
                    ? "十" + c2
                    : s;
            }

            if (n < 100)
            {
                var tens = (char)('0' + n / 10);
                var ones = n % 10;
                var prefix = (DigitToChineseMap.TryGetValue(tens, out var ct) ? ct.ToString() : tens.ToString())
                             + "十";
                if (ones == 0) return prefix;
                var oneChar = (char)('0' + ones);
                return prefix + (DigitToChineseMap.TryGetValue(oneChar, out var co) ? co.ToString() : oneChar.ToString());
            }

            return s;
        }

        private static string CollapseBlankLines(string text)
        {
            return BlankLineCollapser.Replace(text, "\n\n").Trim();
        }

        private static string NormalizePunctuationArtifacts(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = SentenceCommaArtifactRegex.Replace(text, "$1");
            text = CommaSentenceArtifactRegex.Replace(text, "$1");
            text = DuplicateCommaRegex.Replace(text, "，");
            text = LeadingCommaRegex.Replace(text, "$1");
            text = TrailingCommaRegex.Replace(text, "$1");
            return DuplicateSentencePunctuationRegex.Replace(text, "$1");
        }

        private static readonly Regex BlankLineCollapser =
            new(@"\n{3,}", RegexOptions.Compiled);

        private static readonly Regex SentenceCommaArtifactRegex =
            new(@"([。！？])[,，]+", RegexOptions.Compiled);

        private static readonly Regex CommaSentenceArtifactRegex =
            new(@"[,，]+([。！？])", RegexOptions.Compiled);

        private static readonly Regex DuplicateCommaRegex =
            new(@"[,，]{2,}", RegexOptions.Compiled);

        private static readonly Regex LeadingCommaRegex =
            new(@"(^|\n)[,，]+", RegexOptions.Compiled);

        private static readonly Regex TrailingCommaRegex =
            new(@"[,，]+(\n|$)", RegexOptions.Compiled);

        private static readonly Regex DuplicateSentencePunctuationRegex =
            new(@"([。！？])\1+", RegexOptions.Compiled);

        public static async Task<string> ApplyPreLLMAsync(string text, PickerScorers? scorers, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = NormalizeEllipsis(text);
            text = NormalizeDash(text);

            text = RemoveParagraphsMatching(text, NovelArtifactRegexes);

            foreach (var rx in NovelMarkdownRegexes)
                text = rx.Replace(text, string.Empty);

            text = RemoveParagraphsMatching(text, AiTemplateRegexes);

            foreach (var phrase in AiFillerPhrases)
                text = text.Replace(phrase, string.Empty);

            text = ApplyDigitNormalization(text);

            text = await ApplyDictionaryAsync(text, NovelOralPhrases, NovelOralReplaceRate, scorers, bypassPicker: false, ct).ConfigureAwait(false);
            text = await ApplyDictionaryAsync(text, PhraseReplacements, AiPhraseReplaceRate, scorers, bypassPicker: false, ct).ConfigureAwait(false);

            text = await ApplyDictionaryAsync(text, NovelOralWords, NovelOralReplaceRate, scorers, bypassPicker: false, ct).ConfigureAwait(false);
            text = await ApplyDictionaryAsync(text, WordReplacements, WordReplaceRate, scorers, bypassPicker: false, ct).ConfigureAwait(false);

            text = await ApplyDictionaryAsync(text, ForcedPhraseReplacements, 1.0, scorers, bypassPicker: true, ct).ConfigureAwait(false);

            return CollapseBlankLines(NormalizePunctuationArtifacts(text));
        }

        public static async Task<string> ApplyPostLLMAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = ApplyDigitNormalization(text);

            text = await ApplyDictionaryAsync(text, ForcedPhraseReplacements, 1.0, scorers: null, bypassPicker: true, ct).ConfigureAwait(false);

            foreach (var phrase in AiFillerPhrases)
                text = text.Replace(phrase, string.Empty);

            text = NormalizeEllipsis(text);
            text = NormalizeDash(text);

            text = RemoveParagraphsMatching(text, AiTemplateRegexes);

            foreach (var rx in NovelMarkdownRegexes)
                text = rx.Replace(text, string.Empty);

            return CollapseBlankLines(NormalizePunctuationArtifacts(text));
        }

        private static async Task<string> ApplyDictionaryAsync(
            string text, Dictionary<string, string[]> dict, double rate,
            PickerScorers? scorers,
            bool bypassPicker,
            CancellationToken ct = default,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(dict))] string dictName = "")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var kv in dict.OrderByDescending(static kv => kv.Key.Length))
            {
                ct.ThrowIfCancellationRequested();

                var key = kv.Key;
                if (key.Length == 0) continue;
                if (text.IndexOf(key, StringComparison.Ordinal) < 0) continue;

                var safe = FilterSafeCandidates(kv.Value);
                if (safe.Length == 0) continue;

                var used = new HashSet<string>();

                var sb = new StringBuilder(text.Length);
                int pos = 0;
                while (pos < text.Length)
                {
                    int idx = text.IndexOf(key, pos, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        sb.Append(text, pos, text.Length - pos);
                        break;
                    }
                    sb.Append(text, pos, idx - pos);

                    if (Random.Shared.NextDouble() < rate)
                    {
                        var avail = BuildAvailable(safe, used);

                        string chosen;
                        if (bypassPicker)
                        {
                            chosen = avail.Count > 0 ? avail[0] : safe[0];
                        }
                        else
                        {
                            chosen = await CandidatePicker.PickBestAsync(text, idx, key, avail, scorers, ct).ConfigureAwait(false);
                        }

                        sb.Append(chosen);
                        used.Add(chosen);
                    }
                    else
                    {
                        sb.Append(key);
                    }

                    pos = idx + key.Length;
                }
                text = sb.ToString();
            }

            sw.Stop();
            TM.App.Log($"[HumanizeRules.ApplyDictionary] {dictName} 耗时 {sw.ElapsedMilliseconds} ms (bypassPicker={bypassPicker})");
            return text;
        }
    }
}
