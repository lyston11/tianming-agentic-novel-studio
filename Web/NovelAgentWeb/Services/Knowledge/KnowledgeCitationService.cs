using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record RecordKnowledgeCitationRequest(
    string ProjectId,
    string KnowledgeEntryId,
    string? GoalId,
    string? ChapterId,
    string? ChapterVersionId,
    string Purpose,
    string SourceArtifactId,
    string IdempotencyKey);

public interface IKnowledgeCitationService
{
    Task<KnowledgeCitation> RecordAsync(
        RecordKnowledgeCitationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeCitationService : IKnowledgeCitationService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IProjectKnowledgeUsageService _usage;

    public KnowledgeCitationService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IProjectKnowledgeUsageService usage)
    {
        _db = db;
        _currentUser = currentUser;
        _usage = usage;
    }

    public async Task<KnowledgeCitation> RecordAsync(
        RecordKnowledgeCitationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("知识引用必须提供 idempotency key。", nameof(request));
        var userId = _currentUser.GetUserId();
        var existing = await _db.KnowledgeCitations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing != null)
            return existing;
        var projectExists = await _db.NovelProjects.AsNoTracking().AnyAsync(project =>
            project.Id == request.ProjectId && project.UserId == userId,
            cancellationToken);
        if (!projectExists)
            throw new KeyNotFoundException("引用目标项目不存在或不属于当前用户。");
        var entry = await _db.KnowledgeEntries.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.KnowledgeEntryId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("知识条目版本不存在或不属于当前用户。");
        if (entry.Status != "active")
            throw new InvalidOperationException("未确认的 proposed 知识条目不能用于创作。");

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var citation = new KnowledgeCitation
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ProjectId = request.ProjectId,
                KnowledgeEntryId = entry.Id,
                KnowledgeVersion = entry.KnowledgeVersion,
                GoalId = request.GoalId,
                ChapterId = request.ChapterId,
                ChapterVersionId = request.ChapterVersionId,
                Purpose = request.Purpose,
                SourceArtifactId = request.SourceArtifactId,
                IdempotencyKey = request.IdempotencyKey,
                CreatedAt = DateTime.UtcNow
            };
            _db.KnowledgeCitations.Add(citation);
            await _usage.MarkReferencedAsync(
                userId,
                request.ProjectId,
                entry.LogicalKnowledgeId,
                sessionId: null,
                runId: request.GoalId,
                idempotencyKey: request.IdempotencyKey,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return citation;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }
}
