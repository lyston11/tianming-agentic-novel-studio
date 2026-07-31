using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.DomainEvents;

public sealed class DomainContractValidator
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DomainContractValidator(NovelAgentDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task ValidateAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        IReadOnlyList<DomainEventProposal> events,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.GetUserId() != claim.UserId)
            throw new InvalidOperationException("当前用户作用域与已领取任务不一致。");
        var taskMatches = await _db.KernelTasks.AsNoTracking().AnyAsync(task =>
            task.Id == claim.TaskId &&
            task.UserId == claim.UserId &&
            task.ProjectId == claim.ProjectId &&
            task.GoalId == claim.GoalId &&
            task.TaskGraphVersionId == claim.TaskGraphVersionId &&
            task.Status == "running" &&
            task.LeaseOwner == claim.LeaseOwner &&
            task.LeaseExpiresAt > DateTime.UtcNow,
            cancellationToken);
        if (!taskMatches)
            throw new InvalidOperationException("任务作用域、状态或 lease 已失效。");

        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.ArtifactType) ||
                artifact.SchemaVersion <= 0 ||
                string.IsNullOrWhiteSpace(artifact.ContentHash))
                throw new InvalidOperationException("Artifact 合同缺少类型、Schema 或内容哈希。");
            EnsureJsonObject(artifact.ContentJson, "Artifact content");
            if (!string.Equals(artifact.Authorship, "agent", StringComparison.Ordinal) &&
                !string.Equals(artifact.Authorship, "human", StringComparison.Ordinal))
                throw new InvalidOperationException("Artifact authorship 不合法。");
        }

        foreach (var domainEvent in events)
        {
            if (string.IsNullOrWhiteSpace(domainEvent.AggregateType) ||
                string.IsNullOrWhiteSpace(domainEvent.AggregateId) ||
                string.IsNullOrWhiteSpace(domainEvent.EventType) ||
                string.IsNullOrWhiteSpace(domainEvent.IdempotencyKey) ||
                domainEvent.AggregateVersion <= 0)
                throw new InvalidOperationException("Domain Event 合同不完整。");
            EnsureJsonObject(domainEvent.PayloadJson, "Domain Event payload");
        }
    }

    private static void EnsureJsonObject(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{name} 必须是 JSON object。");
    }
}
