using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public sealed record UnifiedMemoryRecord(
    string Id,
    string Scope,
    string Kind,
    string ContentJson,
    string Status,
    string Source,
    int Version,
    string? EffectiveGoalId);

public sealed record AgentMemoryBundle(
    AgentMemoryContextDto Structured,
    IReadOnlyList<UnifiedMemoryRecord> Records);

public interface IMemoryStore
{
    Task<AgentMemoryBundle> ReadAsync(
        string userId,
        string projectId,
        string sessionId,
        string? goalId,
        CancellationToken cancellationToken = default);
}

public sealed class UnifiedMemoryStore : IMemoryStore
{
    private readonly IAgentMemoryContextService _contexts;
    private readonly NovelAgentDbContext _db;

    public UnifiedMemoryStore(
        IAgentMemoryContextService contexts,
        NovelAgentDbContext db)
    {
        _contexts = contexts;
        _db = db;
    }

    public async Task<AgentMemoryBundle> ReadAsync(
        string userId,
        string projectId,
        string sessionId,
        string? goalId,
        CancellationToken cancellationToken = default)
    {
        var structured = await _contexts.BuildAsync(
                userId,
                projectId,
                sessionId,
                cancellationToken)
            .ConfigureAwait(false);

        var author = await _db.AuthorMemories.AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "active")
            .OrderBy(item => item.CreatedAt)
            .Select(item => new UnifiedMemoryRecord(
                item.Id,
                "user",
                item.MemoryKind,
                item.ContentJson,
                item.Status,
                item.Source,
                item.Version,
                null))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var project = await _db.ProjectCollaborationDecisions.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == projectId &&
                item.Status == "active" &&
                (item.Scope == "project" ||
                 (item.Scope == "goal" && item.EffectiveGoalId == goalId)))
            .OrderBy(item => item.CreatedAt)
            .Select(item => new UnifiedMemoryRecord(
                item.Id,
                item.Scope,
                item.MemoryKind,
                item.ContentJson,
                item.Status,
                item.Source,
                1,
                item.EffectiveGoalId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var session = await _db.SessionDialogueStates.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ProjectId == projectId &&
                item.SessionId == sessionId &&
                (item.Status == "pending" || item.Status == "accepted"))
            .OrderBy(item => item.CreatedAt)
            .Select(item => new UnifiedMemoryRecord(
                item.Id,
                "session",
                item.MemoryKind,
                item.ContentJson,
                item.Status,
                "dialogue",
                1,
                goalId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AgentMemoryBundle(
            structured,
            author.Concat(project).Concat(session).ToArray());
    }
}
