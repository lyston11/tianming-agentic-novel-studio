using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public static class ChapterChangesText
    {
        public static string ExtractChangesJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var xml = Regex.Match(content, @"<\s*chapter_changes\s*>([\s\S]*?)</\s*chapter_changes\s*>", RegexOptions.IgnoreCase);
            if (xml.Success)
            {
                return NormalizeExtractedChangesJson(xml.Groups[1].Value);
            }
            var unclosed = Regex.Match(
                content,
                @"<\s*chapter_changes\b[^>]*>([\s\S]*)$",
                RegexOptions.IgnoreCase);
            if (unclosed.Success)
            {
                if (Regex.IsMatch(unclosed.Groups[1].Value, @"</\s*chapter_changes\b", RegexOptions.IgnoreCase))
                    return string.Empty;
                var normalized = NormalizeExtractedChangesJson(unclosed.Groups[1].Value);
                return GenerationGate.TryNormalizeChangesJsonShape(normalized, out var canonical)
                    ? canonical
                    : string.Empty;
            }
            return string.Empty;
        }

        public static string MergeChangesOnlyRepair(string previousDraft, string changesOnlyOutput)
        {
            var body = StripChanges(previousDraft);
            var changesJson = ExtractChangesJson(changesOnlyOutput);
            if (string.IsNullOrWhiteSpace(changesJson))
            {
                var candidate = changesOnlyOutput?.Trim() ?? string.Empty;
                if (GenerationGate.TryNormalizeChangesJsonShape(candidate, out var normalized))
                    changesJson = normalized;
                else
                    changesJson = BuildFallbackChangesJson(body);
            }

            return $"{body}\n\n<chapter_changes>\n{changesJson.Trim()}\n</chapter_changes>";
        }

        public static bool TryDeserializeChanges(string json, out ChapterChanges changes)
        {
            try
            {
                if (GenerationGate.TryNormalizeChangesJsonShape(json, out var normalized))
                    json = normalized;
                changes = JsonSerializer.Deserialize<ChapterChanges>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new ChapterChanges();
                return true;
            }
            catch
            {
                changes = new ChapterChanges();
                return false;
            }
        }

        public static string StripChanges(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            content = StripModelReasoningArtifacts(content);
            var xml = Regex.Match(
                content,
                @"<\s*chapter_changes\b[^>]*>[\s\S]*?</\s*chapter_changes\s*>",
                RegexOptions.IgnoreCase);
            if (xml.Success)
                return content[..xml.Index].Trim();
            var xmlStart = Regex.Match(
                content,
                @"<\s*chapter_changes\b[^>]*>",
                RegexOptions.IgnoreCase);
            if (xmlStart.Success)
                return content[..xmlStart.Index].Trim();
            return content.Trim();
        }

        private static string BuildFallbackChangesJson(string chapterBody)
        {
            var summary = BuildFallbackSummary(chapterBody);
            var payload = new Dictionary<string, object?>
            {
                ["CharacterStateChanges"] = Array.Empty<object>(),
                ["ConflictProgress"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["ConflictId"] = "chapter_main_conflict",
                        ["NewStatus"] = "advanced",
                        ["Event"] = summary,
                        ["Importance"] = "normal",
                        ["CausedBy"] = "changes_only_repair_fallback"
                    }
                },
                ["NewPlotPoints"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Keywords"] = new[] { "章节正文", "门禁修复" },
                        ["Context"] = summary,
                        ["InvolvedCharacters"] = Array.Empty<string>(),
                        ["Importance"] = "normal",
                        ["Storyline"] = "main",
                        ["CausedBy"] = "changes_only_repair_fallback"
                    }
                },
                ["ForeshadowingActions"] = Array.Empty<object>(),
                ["LocationStateChanges"] = Array.Empty<object>(),
                ["FactionStateChanges"] = Array.Empty<object>(),
                ["TimeProgression"] = new Dictionary<string, object?>(),
                ["CharacterMovements"] = Array.Empty<object>(),
                ["ItemTransfers"] = Array.Empty<object>(),
                ["SecretRevealChanges"] = Array.Empty<object>(),
                ["PledgeConstraintChanges"] = Array.Empty<object>(),
                ["DeadlineConstraintChanges"] = Array.Empty<object>()
            };

            return JsonSerializer.Serialize(payload);
        }

        private static string BuildFallbackSummary(string chapterBody)
        {
            var text = Regex.Replace(chapterBody ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "章节正文已生成，CHANGES 协议由修复流程补齐。";
            return text.Length <= 120 ? text : text[..120].TrimEnd() + "…";
        }

        public static string StripModelReasoningArtifacts(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var text = Regex.Replace(
                content,
                @"<\s*think\s*>[\s\S]*?</\s*think\s*>",
                string.Empty,
                RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</\s*think\s*>", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*think\s*>", string.Empty, RegexOptions.IgnoreCase);
            return text.Trim();
        }

        private static string NormalizeExtractedChangesJson(string extracted)
        {
            var candidate = extracted?.Trim() ?? string.Empty;
            if (GenerationGate.TryNormalizeChangesJsonShape(candidate, out var normalized))
                return normalized;

            var withoutTrailingTag = Regex.Replace(
                candidate,
                @"</\s*chapter_changes\s*>\s*$",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
            if (GenerationGate.TryNormalizeChangesJsonShape(withoutTrailingTag, out normalized))
                return normalized;

            foreach (var objectCandidate in ExtractBalancedJsonObjects(withoutTrailingTag).Reverse())
            {
                if (GenerationGate.TryNormalizeChangesJsonShape(objectCandidate, out normalized))
                    return normalized;
            }

            var fallback = ExtractJsonObject(withoutTrailingTag);
            return GenerationGate.TryNormalizeChangesJsonShape(fallback, out normalized)
                ? normalized
                : fallback;
        }

        private static string ExtractJsonObject(string raw)
        {
            return JsonObjectTextExtractor.ExtractFirstObjectOrEmpty(raw);
        }

        private static IEnumerable<string> ExtractBalancedJsonObjects(string content)
        {
            var inString = false;
            var quote = '"';
            var escape = false;
            var depth = 0;
            var start = -1;

            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                        start = i;
                    depth++;
                    continue;
                }

                if (c != '}' || depth == 0)
                    continue;

                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return content[start..(i + 1)];
                    start = -1;
                }
            }
        }
    }
}
