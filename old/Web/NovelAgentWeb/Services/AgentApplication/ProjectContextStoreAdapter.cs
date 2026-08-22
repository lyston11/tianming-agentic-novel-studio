using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public sealed class ProjectContextStoreAdapter(NovelAgentDbContext db) : IProjectContextStore
{
    public async Task<IReadOnlyList<AccessibleProjectCatalogItem>> ListAccessibleProjectsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        return await db.NovelProjects
            .AsNoTracking()
            .Where(project => project.UserId == userId)
            .OrderByDescending(project => project.UpdatedAt)
            .Select(project => new AccessibleProjectCatalogItem(
                project.Id,
                project.Title,
                project.Status,
                AsUtcOffset(project.UpdatedAt),
                null))
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProjectContextActivationResult> ActivateAsync(
        ActivateProjectContextCommand command,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
            transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (transaction)
        {
            try
            {
                var session = await db.AgentSessions
                    .FirstOrDefaultAsync(
                        item => item.Id == command.SessionId && item.UserId == command.UserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (session is null || session.IsArchived)
                {
                    return ProjectContextActivationResult.Failure(
                        "conversation_unavailable",
                        "Conversation 不存在、已归档或不属于当前用户。",
                        command.ExpectedBindingVersion);
                }

                var replay = await FindActivationAsync(command.SessionId, command.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                if (replay is not null)
                    return Replay(command, replay);

                if (session.BindingVersion != command.ExpectedBindingVersion)
                {
                    return ProjectContextActivationResult.Failure(
                        "version_conflict",
                        "项目绑定已变化；请重新读取 Conversation 上下文后再确认。",
                        session.BindingVersion);
                }

                if (!string.IsNullOrWhiteSpace(session.ProjectId))
                {
                    if (string.Equals(session.ProjectId, command.ProjectId, StringComparison.Ordinal))
                    {
                        var latest = await db.ProjectContextActivations
                            .AsNoTracking()
                            .Where(item => item.SessionId == command.SessionId)
                            .OrderByDescending(item => item.BindingVersion)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                        return Activated(session.ProjectId, session.BindingVersion, latest?.ConfirmedAt, "already_activated");
                    }

                    return ProjectContextActivationResult.Failure(
                        "switch_not_supported",
                        "当前阶段不支持在一个 Conversation 中切换项目。",
                        session.BindingVersion);
                }

                var projectExists = await db.NovelProjects
                    .AsNoTracking()
                    .AnyAsync(
                        project => project.Id == command.ProjectId && project.UserId == command.UserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!projectExists)
                {
                    return ProjectContextActivationResult.Failure(
                        "project_unavailable",
                        "项目不存在或当前用户无权访问；请重新发现可访问项目。",
                        session.BindingVersion);
                }

                var confirmedAt = DateTime.UtcNow;
                var nextVersion = checked(session.BindingVersion + 1);
                session.ProjectId = command.ProjectId;
                session.BindingVersion = nextVersion;
                session.UpdatedAt = confirmedAt;
                db.ProjectContextActivations.Add(new ProjectContextActivation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = command.UserId,
                    SessionId = command.SessionId,
                    ProjectId = command.ProjectId,
                    SourceUserMessageId = command.SourceUserMessageId,
                    ConfirmationActionId = command.ConfirmationActionId,
                    IdempotencyKey = command.IdempotencyKey,
                    PreviousBindingVersion = command.ExpectedBindingVersion,
                    BindingVersion = nextVersion,
                    ConfirmedAt = confirmedAt
                });

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return Activated(command.ProjectId, nextVersion, confirmedAt, "activated");
            }
            catch (DbUpdateException)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                var resolved = await ResolveConcurrentWriteAsync(command, cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                    return resolved;
                throw;
            }
        }
    }

    private Task<ProjectContextActivation?> FindActivationAsync(
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        db.ProjectContextActivations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.SessionId == sessionId && item.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private async Task<ProjectContextActivationResult?> ResolveConcurrentWriteAsync(
        ActivateProjectContextCommand command,
        CancellationToken cancellationToken)
    {
        var replay = await FindActivationAsync(command.SessionId, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
            return Replay(command, replay);

        var currentVersion = await db.AgentSessions
            .AsNoTracking()
            .Where(item => item.Id == command.SessionId && item.UserId == command.UserId)
            .Select(item => (long?)item.BindingVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (currentVersion is null)
        {
            return ProjectContextActivationResult.Failure(
                "conversation_unavailable",
                "Conversation 不存在、已归档或不属于当前用户。",
                command.ExpectedBindingVersion);
        }

        if (currentVersion == command.ExpectedBindingVersion)
            return null;

        return ProjectContextActivationResult.Failure(
            "version_conflict",
            "项目绑定已被并发更新；请重新读取 Conversation 上下文。",
            currentVersion.Value);
    }

    private static ProjectContextActivationResult Replay(
        ActivateProjectContextCommand command,
        ProjectContextActivation activation)
    {
        if (!string.Equals(activation.UserId, command.UserId, StringComparison.Ordinal)
            || !string.Equals(activation.ProjectId, command.ProjectId, StringComparison.Ordinal)
            || activation.PreviousBindingVersion != command.ExpectedBindingVersion
            || !string.Equals(activation.SourceUserMessageId, command.SourceUserMessageId, StringComparison.Ordinal)
            || !string.Equals(activation.ConfirmationActionId, command.ConfirmationActionId, StringComparison.Ordinal))
        {
            return ProjectContextActivationResult.Failure(
                "idempotency_conflict",
                "该幂等键已用于不同的项目确认请求。",
                activation.BindingVersion);
        }

        return Activated(
            activation.ProjectId,
            activation.BindingVersion,
            activation.ConfirmedAt,
            "activated");
    }

    private static ProjectContextActivationResult Activated(
        string projectId,
        long bindingVersion,
        DateTime? confirmedAt,
        string code) =>
        new(
            code,
            true,
            false,
            code == "activated" ? "项目上下文已激活。" : "Conversation 已绑定该项目。",
            projectId,
            bindingVersion,
            confirmedAt is null ? null : AsUtcOffset(confirmedAt.Value));

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
