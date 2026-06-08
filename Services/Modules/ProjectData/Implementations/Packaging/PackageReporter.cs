using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class PackageReporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public class PackageReport
        {
            [JsonPropertyName("GeneratedAt")] public string GeneratedAt { get; set; } = string.Empty;
            [JsonPropertyName("Version")] public int Version { get; set; }
            [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
            [JsonPropertyName("ChapterStats")] public Dictionary<string, ChapterStat> ChapterStats { get; set; } = new();
            [JsonPropertyName("VolumeStats")] public Dictionary<string, VolumeStat> VolumeStats { get; set; } = new();
            [JsonPropertyName("AnomalyWarnings")] public List<string> AnomalyWarnings { get; set; } = new();
        }

        public class ChapterStat
        {
            [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
            [JsonPropertyName("Characters")] public int Characters { get; set; }
            [JsonPropertyName("Locations")] public int Locations { get; set; }
            [JsonPropertyName("Factions")] public int Factions { get; set; }
            [JsonPropertyName("PlotRules")] public int PlotRules { get; set; }
            [JsonPropertyName("Conflicts")] public int Conflicts { get; set; }
            [JsonPropertyName("ForeshadowingSetups")] public int ForeshadowingSetups { get; set; }
            [JsonPropertyName("ForeshadowingPayoffs")] public int ForeshadowingPayoffs { get; set; }
            [JsonPropertyName("Scenes")] public int Scenes { get; set; }
        }

        public class VolumeStat
        {
            [JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
            [JsonPropertyName("ChapterCount")] public int ChapterCount { get; set; }
            [JsonPropertyName("TotalCharacters")] public int TotalCharacters { get; set; }
            [JsonPropertyName("TotalLocations")] public int TotalLocations { get; set; }
            [JsonPropertyName("TotalFactions")] public int TotalFactions { get; set; }
            [JsonPropertyName("UniqueCharacters")] public int UniqueCharacters { get; set; }
            [JsonPropertyName("UniqueLocations")] public int UniqueLocations { get; set; }
            [JsonPropertyName("UniqueFactions")] public int UniqueFactions { get; set; }
        }

        public async Task<PackageReport> RunAsync(string configBasePath, int version, string? previousReportPath = null)
        {
            var report = new PackageReport
            {
                GeneratedAt = DateTime.UtcNow.ToString("O"),
                Version = version
            };

            try
            {
                await CollectChapterStatsAsync(configBasePath, report).ConfigureAwait(false);

                AggregateVolumeStats(configBasePath, report);

                if (!string.IsNullOrEmpty(previousReportPath) && File.Exists(previousReportPath))
                {
                    await DetectAnomaliesAsync(previousReportPath, report).ConfigureAwait(false);
                }

                report.Summary = $"版本 {version}：{report.ChapterStats.Count} 章节、{report.VolumeStats.Count} 卷；" +
                                 $"{report.AnomalyWarnings.Count} 项异常波动";

                var reportPath = Path.Combine(configBasePath, "package_report.json");
                await SaveReportAsync(reportPath, report).ConfigureAwait(false);

                TM.App.Log($"[PackageReporter] 报告已生成: {report.Summary}");
            }
            catch (Exception ex)
            {
                report.Summary = $"报告生成异常: {ex.Message}";
                TM.App.Log($"[PackageReporter] 异常（不影响打包）: {ex.Message}");
            }

            return report;
        }

        private static async Task CollectChapterStatsAsync(string configBasePath, PackageReport report)
        {
            var guidesDir = Path.Combine(configBasePath, "guides");
            if (!Directory.Exists(guidesDir)) return;

            var shardFiles = Directory.GetFiles(guidesDir, "content_guide_vol*.json");
            foreach (var shardFile in shardFiles)
            {
                try
                {
                    await using var stream = File.OpenRead(shardFile);
                    using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                    if (!doc.RootElement.TryGetProperty("Chapters", out var chaptersProp) ||
                        chaptersProp.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var ch in chaptersProp.EnumerateObject())
                    {
                        var stat = new ChapterStat();
                        if (ch.Value.TryGetProperty("Title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                            stat.Title = titleProp.GetString() ?? string.Empty;

                        if (ch.Value.TryGetProperty("ContextIds", out var ctx))
                        {
                            stat.Characters = CountArray(ctx, "Characters");
                            stat.Locations = CountArray(ctx, "Locations");
                            stat.Factions = CountArray(ctx, "Factions");
                            stat.PlotRules = CountArray(ctx, "PlotRules");
                            stat.Conflicts = CountArray(ctx, "Conflicts");
                            stat.ForeshadowingSetups = CountArray(ctx, "ForeshadowingSetups");
                            stat.ForeshadowingPayoffs = CountArray(ctx, "ForeshadowingPayoffs");
                        }

                        if (ch.Value.TryGetProperty("Scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
                            stat.Scenes = scenes.GetArrayLength();

                        report.ChapterStats[ch.Name] = stat;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PackageReporter] 读取分片失败 {Path.GetFileName(shardFile)}: {ex.Message}");
                }
            }
        }

        private static int CountArray(JsonElement ctx, string field)
        {
            if (!ctx.TryGetProperty(field, out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
            return arr.GetArrayLength();
        }

        private class VolumeBucket
        {
            public HashSet<string> Chars { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Locs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Facs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int ChapterCount;
            public int TotalChars;
            public int TotalLocs;
            public int TotalFacs;
        }

        private static void AggregateVolumeStats(string configBasePath, PackageReport report)
        {
            var volumeBuckets = new Dictionary<int, VolumeBucket>();

            var guidesDir = Path.Combine(configBasePath, "guides");
            if (!Directory.Exists(guidesDir)) return;

            var shardFiles = Directory.GetFiles(guidesDir, "content_guide_vol*.json");
            foreach (var shardFile in shardFiles)
            {
                try
                {
                    using var stream = File.OpenRead(shardFile);
                    using var doc = JsonDocument.Parse(stream);
                    if (!doc.RootElement.TryGetProperty("Chapters", out var chaptersProp) ||
                        chaptersProp.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var ch in chaptersProp.EnumerateObject())
                    {
                        var parsed = ChapterParserHelper.ParseChapterId(ch.Name);
                        if (parsed == null) continue;
                        var volNum = parsed.Value.volumeNumber;

                        if (!volumeBuckets.TryGetValue(volNum, out var bucket))
                        {
                            bucket = new VolumeBucket();
                            volumeBuckets[volNum] = bucket;
                        }

                        bucket.ChapterCount++;

                        if (ch.Value.TryGetProperty("ContextIds", out var ctx))
                        {
                            bucket.TotalChars += CollectIds(ctx, "Characters", bucket.Chars);
                            bucket.TotalLocs += CollectIds(ctx, "Locations", bucket.Locs);
                            bucket.TotalFacs += CollectIds(ctx, "Factions", bucket.Facs);
                        }
                    }
                }
                catch { }
            }

            foreach (var (volNum, bucket) in volumeBuckets)
            {
                report.VolumeStats[$"vol{volNum}"] = new VolumeStat
                {
                    VolumeNumber = volNum,
                    ChapterCount = bucket.ChapterCount,
                    TotalCharacters = bucket.TotalChars,
                    TotalLocations = bucket.TotalLocs,
                    TotalFactions = bucket.TotalFacs,
                    UniqueCharacters = bucket.Chars.Count,
                    UniqueLocations = bucket.Locs.Count,
                    UniqueFactions = bucket.Facs.Count
                };
            }
        }

        private static int CollectIds(JsonElement ctx, string field, HashSet<string> set)
        {
            if (!ctx.TryGetProperty(field, out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
            int added = 0;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var id = item.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                set.Add(id!);
                added++;
            }
            return added;
        }

        private static async Task DetectAnomaliesAsync(string previousReportPath, PackageReport report)
        {
            try
            {
                await using var stream = File.OpenRead(previousReportPath);
                var prev = await JsonSerializer.DeserializeAsync<PackageReport>(stream, JsonOptions).ConfigureAwait(false);
                if (prev == null) return;

                foreach (var (chId, curr) in report.ChapterStats)
                {
                    if (!prev.ChapterStats.TryGetValue(chId, out var prevStat)) continue;

                    AppendAnomaly(report, chId, "角色", prevStat.Characters, curr.Characters);
                    AppendAnomaly(report, chId, "地点", prevStat.Locations, curr.Locations);
                    AppendAnomaly(report, chId, "势力", prevStat.Factions, curr.Factions);
                    AppendAnomaly(report, chId, "剧情规则", prevStat.PlotRules, curr.PlotRules);
                    AppendAnomaly(report, chId, "冲突", prevStat.Conflicts, curr.Conflicts);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageReporter] 上次报告对比失败（非致命）: {ex.Message}");
            }
        }

        private static void AppendAnomaly(PackageReport report, string chId, string label, int prev, int curr)
        {
            if (prev == curr) return;
            var diff = Math.Abs(curr - prev);
            if (diff < 5) return;

            if (prev == 0 && curr >= 5)
            {
                report.AnomalyWarnings.Add($"{chId} 「{label}」从 0 突增到 {curr}，请确认是否预期");
                return;
            }
            if (curr == 0 && prev >= 5)
            {
                report.AnomalyWarnings.Add($"{chId} 「{label}」从 {prev} 锐减到 0，请确认是否预期");
                return;
            }

            var baseValue = Math.Max(prev, 1);
            var ratio = (double)diff / baseValue;
            if (ratio >= 2.0)
            {
                report.AnomalyWarnings.Add($"{chId} 「{label}」从 {prev} 大幅变化为 {curr}，请确认是否预期");
            }
        }

        private static async Task SaveReportAsync(string path, PackageReport report)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tmp, path, overwrite: true);
        }
    }
}
