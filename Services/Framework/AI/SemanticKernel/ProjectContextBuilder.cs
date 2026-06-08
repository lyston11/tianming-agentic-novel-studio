using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static class ProjectContextBuilder
    {
        private static string? _cachedContext;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly SemaphoreSlim _buildLock = new(1, 1);
        private const int CacheTtlSeconds = 60;

        public static void Invalidate() => _cacheExpiry = DateTime.MinValue;

        public static async Task<string?> BuildAsync()
        {
            if (DateTime.Now < _cacheExpiry)
                return _cachedContext;

            await _buildLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (DateTime.Now < _cacheExpiry)
                    return _cachedContext;

                try
                {
                    var volumeService = ServiceLocator.Get<VolumeDesignService>();
                    var outlineService = ServiceLocator.Get<OutlineService>();
                    var chapterService = ServiceLocator.Get<ChapterService>();
                    var blueprintService = ServiceLocator.Get<BlueprintService>();

                    await Task.WhenAll(
                        volumeService.InitializeAsync(),
                        outlineService.InitializeAsync(),
                        chapterService.InitializeAsync(),
                        blueprintService.InitializeAsync()).ConfigureAwait(false);

                    var volumes = volumeService.GetAllVolumeDesigns()
                        .Where(v => v.IsEnabled && v.VolumeNumber > 0)
                        .OrderBy(v => v.VolumeNumber)
                        .ToList();

                    var outlines = outlineService.GetAllOutlines()
                        .Where(o => o.IsEnabled)
                        .ToList();

                    var allChapters = chapterService.GetAllChapters()
                        .Where(c => c.IsEnabled)
                        .ToList();

                    var allBlueprints = blueprintService.GetAllBlueprints()
                        .Where(b => b.IsEnabled)
                        .ToList();

                    if (volumes.Count == 0 && outlines.Count == 0)
                    {
                        _cachedContext = null;
                        _cacheExpiry = DateTime.Now.AddSeconds(CacheTtlSeconds);
                        return null;
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine("<project_context>");

                    var outline = outlines.FirstOrDefault(o => o.TotalChapterCount > 0) ?? outlines.FirstOrDefault();
                    if (outline != null)
                    {
                        sb.Append("  <outline");
                        if (outline.TotalChapterCount > 0)
                            sb.Append($" totalChapters=\"{outline.TotalChapterCount}\"");
                        sb.AppendLine(">");
                        if (!string.IsNullOrWhiteSpace(outline.OneLineOutline))
                            sb.AppendLine($"    一句话大纲：{outline.OneLineOutline}");
                        if (!string.IsNullOrWhiteSpace(outline.CoreConflict))
                            sb.AppendLine($"    核心冲突：{outline.CoreConflict}");
                        if (!string.IsNullOrWhiteSpace(outline.Theme))
                            sb.AppendLine($"    主题：{outline.Theme}");
                        if (!string.IsNullOrWhiteSpace(outline.VolumeDivision))
                            sb.AppendLine($"    卷/幕划分：{outline.VolumeDivision}");
                        sb.AppendLine("  </outline>");
                    }

                    if (volumes.Count > 0)
                    {
                        var totalChapters = outlines.FirstOrDefault(o => o.TotalChapterCount > 0)?.TotalChapterCount ?? 0;
                        System.Collections.Generic.List<VolumeChapterRange>? ranges = null;
                        if (totalChapters > 0)
                        {
                            try
                            {
                                var volumeDivision = outlines.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.VolumeDivision))?.VolumeDivision;
                                if (!ChapterAllocationHelper.TryParseVolumeDivision(volumeDivision, volumes.Count, totalChapters, out var parsedRanges))
                                    parsedRanges = ChapterAllocationHelper.Allocate(volumes.Count, totalChapters);
                                ranges = parsedRanges;
                            }
                            catch { }
                        }

                        sb.AppendLine($"  <volumes count=\"{volumes.Count}\">");
                        foreach (var vol in volumes)
                        {
                            var range = ranges?.FirstOrDefault(r => r.VolumeNumber == vol.VolumeNumber);
                            var volName = vol.VolumeNumber > 0
                                ? $"第{vol.VolumeNumber}卷 {vol.VolumeTitle}".Trim()
                                : vol.Name;

                            var designedCount = allChapters.Count(c =>
                                string.Equals(c.CategoryId, vol.Id, StringComparison.Ordinal)
                                || string.Equals(c.Category, volName, StringComparison.Ordinal)
                                || string.Equals(c.Category, vol.Name, StringComparison.Ordinal)
                                || (!string.IsNullOrWhiteSpace(c.Category) && c.Category.StartsWith($"第{vol.VolumeNumber}卷", StringComparison.Ordinal)));

                            var bpCount = allBlueprints.Count(b =>
                                string.Equals(b.CategoryId, vol.Id, StringComparison.Ordinal)
                                || string.Equals(b.Category, volName, StringComparison.Ordinal)
                                || string.Equals(b.Category, vol.Name, StringComparison.Ordinal)
                                || (!string.IsNullOrWhiteSpace(b.Category) && b.Category.StartsWith($"第{vol.VolumeNumber}卷", StringComparison.Ordinal)));

                            sb.Append($"    <vol num=\"{vol.VolumeNumber}\"");
                            if (!string.IsNullOrWhiteSpace(vol.VolumeTitle))
                                sb.Append($" title=\"{vol.VolumeTitle}\"");
                            if (range != null)
                                sb.Append($" chapterRange=\"第{range.StartChapter}~{range.EndChapter}章({range.TargetChapterCount}章)\"");
                            else if (vol.TargetChapterCount > 0)
                                sb.Append($" targetChapters=\"{vol.TargetChapterCount}章\"");
                            sb.Append($" designed=\"{designedCount}章\" blueprinted=\"{bpCount}章\"");
                            sb.AppendLine(">");

                            if (!string.IsNullOrWhiteSpace(vol.VolumeTheme))
                                sb.AppendLine($"      主题：{vol.VolumeTheme}");
                            if (!string.IsNullOrWhiteSpace(vol.StageGoal))
                                sb.AppendLine($"      阶段目标：{vol.StageGoal}");
                            if (!string.IsNullOrWhiteSpace(vol.MainConflict))
                                sb.AppendLine($"      主冲突：{vol.MainConflict}");

                            sb.AppendLine("    </vol>");
                        }
                        sb.AppendLine("  </volumes>");
                    }
                    else
                    {
                        sb.AppendLine("  <volumes count=\"0\">尚未配置分卷设计。</volumes>");
                    }

                    sb.Append("</project_context>");

                    var result = sb.ToString();
                    _cachedContext = result;
                    _cacheExpiry = DateTime.Now.AddSeconds(CacheTtlSeconds);
                    return result;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectContextBuilder] 构建项目知识库上下文失败: {ex.Message}");
                    return null;
                }
            }
            finally
            {
                _buildLock.Release();
            }
        }
    }
}
