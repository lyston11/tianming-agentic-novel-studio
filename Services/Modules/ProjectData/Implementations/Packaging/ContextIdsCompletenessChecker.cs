using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ContextIdsCompletenessChecker
    {
        private readonly IGuideContextService _guideContextService;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public ContextIdsCompletenessChecker(IGuideContextService guideContextService)
        {
            _guideContextService = guideContextService;
        }

        public async Task<CompletenessWarnings?> RunAsync()
        {
            try
            {
                var warnings = await CheckAsync().ConfigureAwait(false);
                await WriteWarningsAsync(warnings).ConfigureAwait(false);
                TM.App.Log($"[CompletenessChecker] 完成: 伏笔警告 {warnings.ForeshadowingWarnings.Count} 条, 冲突警告 {warnings.ConflictWarnings.Count} 条");
                return warnings;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CompletenessChecker] 检查失败（不影响打包）: {ex.Message}");
                return null;
            }
        }

        private async Task<CompletenessWarnings> CheckAsync()
        {
            var warnings = new CompletenessWarnings
            {
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };

            var guidesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            var foreshadowingTask = ReadGuideAsync<ForeshadowingStatusGuide>(
                Path.Combine(guidesDir, "foreshadowing_status_guide.json"));
            var contentGuideTask = _guideContextService.GetContentGuideAsync();

            var conflictShardFiles = Directory.Exists(guidesDir)
                ? Directory.GetFiles(guidesDir, "conflict_progress_guide_vol*.json")
                : Array.Empty<string>();
            var conflictShardsTask = Task.WhenAll(conflictShardFiles.Select(f => ReadGuideAsync<ConflictProgressGuide>(f)));

            await Task.WhenAll(foreshadowingTask, contentGuideTask, conflictShardsTask).ConfigureAwait(false);

            var foreshadowingGuide = foreshadowingTask.Result;
            var conflictGuide = new ConflictProgressGuide();
            foreach (var shard in conflictShardsTask.Result)
                foreach (var (id, entry) in shard.Conflicts)
                    conflictGuide.Conflicts[id] = entry;

            var contentGuide = contentGuideTask.Result;

            var plannedPayoffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plannedConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (contentGuide?.Chapters != null)
            {
                foreach (var chapter in contentGuide.Chapters.Values)
                {
                    if (chapter.ContextIds == null) continue;

                    foreach (var id in chapter.ContextIds.ForeshadowingPayoffs ?? new List<string>())
                        plannedPayoffs.Add(id);

                    foreach (var id in chapter.ContextIds.Conflicts ?? new List<string>())
                        plannedConflicts.Add(id);
                }
            }

            foreach (var (id, entry) in foreshadowingGuide.Foreshadowings)
            {
                if (!entry.IsSetup || entry.IsResolved) continue;
                if (plannedPayoffs.Contains(id)) continue;

                warnings.ForeshadowingWarnings.Add(new ForeshadowingWarning
                {
                    ForeshadowId = id,
                    Name = entry.Name,
                    Tier = entry.Tier,
                    SetupChapter = entry.ActualSetupChapter,
                    Issue = "已埋设，但无任何章节蓝图声明揭示（ForeshadowingPayoffs 中未出现）"
                });
            }

            foreach (var (id, entry) in conflictGuide.Conflicts)
            {
                if (string.Equals(entry.Status, "resolved", StringComparison.OrdinalIgnoreCase)) continue;
                if (plannedConflicts.Contains(id)) continue;

                warnings.ConflictWarnings.Add(new ConflictWarning
                {
                    ConflictId = id,
                    Name = entry.Name,
                    Tier = entry.Tier,
                    Status = entry.Status,
                    Issue = "冲突活跃（未resolved），但无任何章节蓝图声明追踪（Conflicts 中未出现）"
                });
            }

            if (contentGuide?.Chapters != null)
            {
                foreach (var (chapterId, chapter) in contentGuide.Chapters)
                {
                    var ctx = chapter.ContextIds;
                    if (ctx == null) continue;

                    var isEmpty = (ctx.Characters == null || ctx.Characters.Count == 0)
                               && (ctx.Locations == null || ctx.Locations.Count == 0)
                               && (ctx.WorldRuleIds == null || ctx.WorldRuleIds.Count == 0);

                    if (isEmpty)
                    {
                        warnings.EmptyContextWarnings.Add(new EmptyContextWarning
                        {
                            ChapterId = chapterId,
                            Title = chapter.Title,
                            Issue = "Characters/Locations/WorldRuleIds 均为空，建议模块进一步完善蓝图"
                        });
                    }
                }
            }

            warnings.Summary = $"伏笔孤儿: {warnings.ForeshadowingWarnings.Count} 条 / 冲突孤儿: {warnings.ConflictWarnings.Count} 条 / 空ContextIds章节: {warnings.EmptyContextWarnings.Count} 条";
            return warnings;
        }

        private static async Task<T> ReadGuideAsync<T>(string path) where T : new()
        {
            if (!File.Exists(path))
                return new T();

            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions).ConfigureAwait(false) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        private async Task WriteWarningsAsync(CompletenessWarnings warnings)
        {
            var dir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            var path = Path.Combine(dir, "completeness_warnings.json");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmpCic = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpCic))
            {
                await JsonSerializer.SerializeAsync(stream, warnings, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tmpCic, path, overwrite: true);
        }
    }

    public class CompletenessWarnings
    {
        [JsonPropertyName("GeneratedAt")] public string GeneratedAt { get; set; } = string.Empty;
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("ForeshadowingWarnings")] public List<ForeshadowingWarning> ForeshadowingWarnings { get; set; } = new();
        [JsonPropertyName("ConflictWarnings")] public List<ConflictWarning> ConflictWarnings { get; set; } = new();
        [JsonPropertyName("EmptyContextWarnings")] public List<EmptyContextWarning> EmptyContextWarnings { get; set; } = new();
    }

    public class ForeshadowingWarning
    {
        [JsonPropertyName("ForeshadowId")] public string ForeshadowId { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Tier")] public string Tier { get; set; } = string.Empty;
        [JsonPropertyName("SetupChapter")] public string SetupChapter { get; set; } = string.Empty;
        [JsonPropertyName("Issue")] public string Issue { get; set; } = string.Empty;
    }

    public class ConflictWarning
    {
        [JsonPropertyName("ConflictId")] public string ConflictId { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Tier")] public string Tier { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("Issue")] public string Issue { get; set; } = string.Empty;
    }

    public class EmptyContextWarning
    {
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Issue")] public string Issue { get; set; } = string.Empty;
    }
}
