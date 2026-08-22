using TM.Framework.Common.Helpers;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionChapterMilestoneService : IChapterMilestoneService
{
    private readonly IChapterSummaryService _summaryService;

    public ProductionChapterMilestoneService(IChapterSummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    public async Task<List<VolumeMilestoneEntry>> GetPreviousMilestonesAsync(int currentVolumeNumber)
    {
        if (currentVolumeNumber <= 1)
            return new List<VolumeMilestoneEntry>();

        var cfg = LayeredContextConfig.TakeSnapshot();
        var maxVols = cfg.MilestoneMaxPreviousVolumes;
        var startVol = Math.Max(1, currentVolumeNumber - maxVols);
        var summaries = await _summaryService.GetAllSummariesAsync().ConfigureAwait(false);

        return summaries
            .GroupBy(pair => ChapterParserHelper.ParseChapterId(pair.Key)?.volumeNumber ?? 1)
            .Where(group => group.Key >= startVol && group.Key < currentVolumeNumber)
            .OrderBy(group => group.Key)
            .Select(group => new VolumeMilestoneEntry
            {
                VolumeNumber = group.Key,
                Milestone = BuildMilestoneText(group.Key, group.ToDictionary(pair => pair.Key, pair => pair.Value), cfg)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Milestone))
            .ToList();
    }

    public void InvalidateCache()
    {
        _summaryService.InvalidateCache();
    }

    private static string BuildMilestoneText(
        int volumeNumber,
        Dictionary<string, string> volumeSummaries,
        LayeredContextConfigSnapshot cfg)
    {
        var header = $"=== 第{volumeNumber}卷 历史摘要 ===";
        if (volumeSummaries.Count == 0)
            return header;

        var ordered = volumeSummaries
            .OrderBy(pair => pair.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
            .ToList();

        var interval = Math.Max(1, cfg.MilestoneAnchorInterval);
        var tailRecentCount = Math.Max(0, cfg.VolumeMilestoneTailRecentCount);
        var maxChars = cfg.VolumeMilestoneMaxChars > 0 ? cfg.VolumeMilestoneMaxChars : 12000;
        var perChapterMax = Math.Max(200, Math.Min(800, maxChars / 10));

        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ordered.Count > 0)
            selectedKeys.Add(ordered[0].Key);
        if (ordered.Count > 1)
            selectedKeys.Add(ordered[^1].Key);

        if (tailRecentCount > 0)
        {
            var take = Math.Min(tailRecentCount, ordered.Count);
            var skip = Math.Max(0, ordered.Count - take);
            foreach (var pair in ordered.Skip(skip))
                selectedKeys.Add(pair.Key);
        }

        for (var i = interval; i < ordered.Count - 1; i += interval)
            selectedKeys.Add(ordered[i].Key);

        var lines = new List<string> { header };
        foreach (var pair in ordered.Where(pair => selectedKeys.Contains(pair.Key)))
        {
            var parsed = ChapterParserHelper.ParseChapterId(pair.Key);
            var chapterNumber = parsed?.chapterNumber ?? 0;
            var prefix = chapterNumber > 0 ? $"第{chapterNumber}章" : pair.Key;
            var summary = pair.Value ?? string.Empty;
            if (summary.Length > perChapterMax)
                summary = summary[..perChapterMax] + "...";
            lines.Add($"[{prefix}] {summary}");
        }

        while (GetTotalCharCount(lines) > maxChars && lines.Count > 1)
            lines.RemoveAt(1);

        var merged = string.Join(Environment.NewLine, lines).Trim();
        return merged.Length <= maxChars ? merged : merged[..maxChars];
    }

    private static int GetTotalCharCount(IReadOnlyList<string> lines)
    {
        var total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            total += lines[i].Length;
            if (i < lines.Count - 1)
                total += Environment.NewLine.Length;
        }
        return total;
    }
}
