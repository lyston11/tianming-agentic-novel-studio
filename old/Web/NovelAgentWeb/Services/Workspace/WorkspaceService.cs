using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public class WorkspaceService : IWorkspaceService
{
    private readonly NovelAgentDbContext _db;

    public WorkspaceService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default)
    {
        var projects = await _db.NovelProjects
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (projects.Count == 0)
        {
            return new WorkspaceResponse(new List<NovelBookView>(), 0);
        }

        var projectIds = projects.Select(p => p.Id).ToList();

        var canonicalVolumeCounts = await _db.Volumes
            .Where(v => projectIds.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var plannedVolumeCounts = await _db.VolumeArcs
            .Where(v => projectIds.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var chapterStats = await _db.Chapters
            .Where(c => projectIds.Contains(c.ProjectId))
            .GroupBy(c => c.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                GeneratedCount = g.Count(c => c.Status == "committed"),
                PlannedCount = g.Count(c => c.Status != "committed"),
                NeedsRewriteCount = 0
            })
            .ToDictionaryAsync(x => x.ProjectId, ct);

        var constitutions = await _db.StoryConstitutions
            .Where(sc => projectIds.Contains(sc.ProjectId))
            .ToDictionaryAsync(sc => sc.ProjectId, ct);

        var bookViews = projects.Select(p =>
        {
            var stats = chapterStats.GetValueOrDefault(p.Id);
            var constitution = constitutions.GetValueOrDefault(p.Id);

            return new NovelBookView(
                p.Id,
                p.Title,
                p.Genre ?? string.Empty,
                p.SubGenre ?? string.Empty,
                constitution?.CoreHook ?? string.Empty,
                constitution?.ReaderPromise ?? string.Empty,
                p.Status,
                p.Status != "archived",
                canonicalVolumeCounts.GetValueOrDefault(p.Id) > 0
                    ? canonicalVolumeCounts[p.Id]
                    : plannedVolumeCounts.GetValueOrDefault(p.Id, 0),
                stats?.GeneratedCount ?? 0,
                stats?.PlannedCount ?? 0,
                stats?.NeedsRewriteCount ?? 0,
                p.UpdatedAt.ToString("o"),
                null);
        }).ToList();

        return new WorkspaceResponse(bookViews, bookViews.Count);
    }
}
