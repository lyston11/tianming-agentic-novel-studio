using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public sealed class LegacyProjectSnapshotReaderAdapter(
    NovelAgentDbContext db,
    IGoalBaselineProvider baselines) : ILegacyProjectSnapshotReader
{
    public async Task<LegacyProjectSnapshot> ReadRecoverableSnapshotAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var formalVersions = await (
            from version in db.ChapterVersions.AsNoTracking()
            join chapter in db.Chapters.AsNoTracking()
                on new { version.ProjectId, version.ChapterId }
                equals new { chapter.ProjectId, ChapterId = chapter.Id }
            where version.UserId == userId
                && version.ProjectId == projectId
                && version.Status == "committed"
                && version.ContentDocumentId == chapter.CurrentDocumentId
            orderby chapter.ChapterNumber
            select version.Id)
            .ToListAsync(cancellationToken);
        var goalDecisions = await db.GoalRevisions.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId)
            .OrderBy(x => x.RevisionNumber)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var collaborationDecisions = await db.ProjectCollaborationDecisions.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId && x.Status == "active")
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var legacyKnowledge = await db.KnowledgeBases.AsNoTracking()
            .Where(x => x.UserId == userId && x.SourceProjectId == projectId && !x.IsArchived)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var versionedKnowledge = await db.KnowledgeEntries.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId && x.Status == "active")
            .OrderBy(x => x.KnowledgeVersion)
            .ThenBy(x => x.SourceEntryIndex)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var baseline = await baselines.CaptureAsync(userId, projectId, cancellationToken);
        return new LegacyProjectSnapshot(
            projectId,
            formalVersions,
            goalDecisions.Concat(collaborationDecisions).Distinct(StringComparer.Ordinal).ToArray(),
            legacyKnowledge.Concat(versionedKnowledge).Distinct(StringComparer.Ordinal).ToArray(),
            baseline.CanonVersion,
            baseline.KnowledgeVersion,
            baseline.QualityContractVersion,
            baseline.StyleProfileVersion,
            System.Text.Json.JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(baseline.ModelConfigVersionsJson)
                ?? new Dictionary<string, string>(),
            System.Text.Json.JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(baseline.ProtocolVersionsJson)
                ?? new Dictionary<string, string>());
    }
}
