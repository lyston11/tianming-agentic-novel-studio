namespace Tianming.NovelAgent.Application.Conversation;

public sealed record AccessibleProjectCatalogItem(
    string ProjectId,
    string Title,
    string Status,
    DateTimeOffset UpdatedAt,
    string? Description = null);

public sealed record ActivateProjectContextCommand(
    string UserId,
    string SessionId,
    string ProjectId,
    string IdempotencyKey,
    long ExpectedBindingVersion,
    string? SourceUserMessageId = null,
    string? ConfirmationActionId = null);

public sealed record ProjectContextActivationResult(
    string Code,
    bool Succeeded,
    bool Recoverable,
    string Message,
    string? ProjectId,
    long BindingVersion,
    DateTimeOffset? ConfirmedAt = null)
{
    public static ProjectContextActivationResult Failure(
        string code,
        string message,
        long bindingVersion = 0) =>
        new(code, false, true, message, null, bindingVersion);
}

public interface IProjectContextStore
{
    Task<IReadOnlyList<AccessibleProjectCatalogItem>> ListAccessibleProjectsAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<ProjectContextActivationResult> ActivateAsync(
        ActivateProjectContextCommand command,
        CancellationToken cancellationToken);
}

public sealed class ProjectContextApplicationService(IProjectContextStore store)
{
    public Task<IReadOnlyList<AccessibleProjectCatalogItem>> ListAccessibleProjectsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return store.ListAccessibleProjectsAsync(userId.Trim(), cancellationToken);
    }

    public Task<ProjectContextActivationResult> ActivateAsync(
        ActivateProjectContextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.SourceUserMessageId)
            && string.IsNullOrWhiteSpace(command.ConfirmationActionId))
        {
            return Task.FromResult(ProjectContextActivationResult.Failure(
                "confirmation_required",
                "项目上下文激活需要可审计的用户明确确认。",
                command.ExpectedBindingVersion));
        }

        if (string.IsNullOrWhiteSpace(command.UserId)
            || string.IsNullOrWhiteSpace(command.SessionId)
            || string.IsNullOrWhiteSpace(command.ProjectId)
            || string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.ExpectedBindingVersion < 0)
        {
            return Task.FromResult(ProjectContextActivationResult.Failure(
                "invalid_request",
                "项目、会话、幂等键和预期 binding version 均为必填项。",
                command.ExpectedBindingVersion));
        }

        return store.ActivateAsync(
            command with
            {
                UserId = command.UserId.Trim(),
                SessionId = command.SessionId.Trim(),
                ProjectId = command.ProjectId.Trim(),
                IdempotencyKey = command.IdempotencyKey.Trim(),
                SourceUserMessageId = EmptyToNull(command.SourceUserMessageId),
                ConfirmationActionId = EmptyToNull(command.ConfirmationActionId)
            },
            cancellationToken);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
