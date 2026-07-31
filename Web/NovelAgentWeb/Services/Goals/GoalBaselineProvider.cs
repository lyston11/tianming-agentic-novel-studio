using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class GoalBaselineProvider : IGoalBaselineProvider
{
    private readonly NovelAgentDbContext _db;

    public GoalBaselineProvider(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<GoalBaselines> CaptureAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var latestCanonVersion = await _db.BranchMergeRecords
            .AsNoTracking()
            .Where(record => record.UserId == userId && record.ProjectId == projectId)
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .Select(record => record.NewCanonVersion)
            .FirstOrDefaultAsync(cancellationToken);
        var latestChapter = await _db.ChapterVersions
            .AsNoTracking()
            .Where(version => version.UserId == userId && version.ProjectId == projectId)
            .OrderByDescending(version => version.CreatedAt)
            .Select(version => new { version.Id, version.VersionNumber })
            .FirstOrDefaultAsync(cancellationToken);
        var knowledgeVersionNumber = await _db.KnowledgeDocumentBlobs
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "processed")
            .Select(item => (long?)item.KnowledgeVersion)
            .MaxAsync(cancellationToken);
        var style = await _db.StyleProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "active")
            .OrderByDescending(item => item.KnowledgeVersion)
            .ThenByDescending(item => item.Version)
            .Select(item => new { item.Id, item.Version, item.KnowledgeVersion })
            .FirstOrDefaultAsync(cancellationToken);
        var modelVersions = await _db.ModelKernelConfigurations
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId && item.Status == "active")
            .OrderBy(item => item.KernelName)
            .ToDictionaryAsync(item => item.KernelName, item => item.Version, cancellationToken);

        var canonVersion = !string.IsNullOrWhiteSpace(latestCanonVersion)
            ? latestCanonVersion
            : latestChapter == null
                ? "canon:empty"
                : $"chapter:{latestChapter.Id}:v{latestChapter.VersionNumber}";
        var knowledgeVersion = !knowledgeVersionNumber.HasValue
            ? "knowledge:empty"
            : $"knowledge:{userId}:v{knowledgeVersionNumber.Value}";
        var styleVersion = style == null
            ? "style:none"
            : $"style:{style.Id}:v{style.Version}:knowledge-v{style.KnowledgeVersion}";
        var modelVersionsJson = JsonSerializer.Serialize(modelVersions);
        var protocolVersionsJson = "{\"goal\":1,\"artifact\":1,\"event\":1}";

        return new GoalBaselines(
            canonVersion,
            knowledgeVersion,
            "quality:default-v1",
            styleVersion,
            modelVersionsJson,
            protocolVersionsJson,
            JsonSerializer.Serialize(new
            {
                canon = canonVersion,
                knowledge = knowledgeVersion,
                models = modelVersionsJson
            }));
    }
}
