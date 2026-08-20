using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Rag;

namespace TM.Web.NovelAgentWeb.DataMigration;

public sealed class TargetArchitectureCutoverService
{
    private static readonly string[] ActiveLegacyStatuses =
        ["queued", "running", "Planning", "Repairing", "awaiting_confirmation"];
    private readonly NovelAgentDbContext _db;
    private readonly MigrationVerifier _verifier;
    private readonly IVectorIndexRebuilder _vectors;
    private readonly IBackgroundUserContext _backgroundUsers;
    private readonly ILegacyExecutionArchiveService _legacyArchives;

    public TargetArchitectureCutoverService(
        NovelAgentDbContext db,
        MigrationVerifier verifier,
        IVectorIndexRebuilder vectors,
        IBackgroundUserContext backgroundUsers,
        ILegacyExecutionArchiveService legacyArchives)
    {
        _db = db;
        _verifier = verifier;
        _vectors = vectors;
        _backgroundUsers = backgroundUsers;
        _legacyArchives = legacyArchives;
    }

    public async Task<TargetArchitectureCutoverReport> PreflightAndRebuildAsync(
        string sqlitePath,
        string userId,
        bool rebuildVectors,
        CancellationToken cancellationToken = default)
    {
        using var userScope = _backgroundUsers.Push(userId);
        var verification = await _verifier.VerifyUserAsync(sqlitePath, userId, cancellationToken);
        await _legacyArchives.PrepareUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var activeLegacyRuns = await _db.AgentRuntimeRuns.CountAsync(item =>
            item.UserId == userId && ActiveLegacyStatuses.Contains(item.Status), cancellationToken);
        var pendingOutbox = await _db.OutboxEvents.CountAsync(item =>
            item.UserId == userId && item.Status != "completed", cancellationToken);
        var blockers = new List<string>();
        if (!verification.IsValid) blockers.Add("迁移核验未通过。");
        if (activeLegacyRuns > 0) blockers.Add("仍有未完成的旧 ReAct runtime run。");
        if (pendingOutbox > 0) blockers.Add("仍有未完成 Outbox 事件。");
        var rebuilt = 0;
        if (blockers.Count == 0 && rebuildVectors)
            rebuilt = await _vectors.RebuildUserAsync(userId, cancellationToken);
        return new TargetArchitectureCutoverReport(
            userId,
            blockers.Count == 0,
            verification,
            activeLegacyRuns,
            pendingOutbox,
            rebuilt,
            blockers);
    }
}
