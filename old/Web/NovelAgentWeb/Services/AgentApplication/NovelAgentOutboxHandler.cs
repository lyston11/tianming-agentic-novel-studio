using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Production;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Canon;

namespace TM.Web.NovelAgentWeb.Services.AgentApplication;

public interface INovelAgentOutboxHandler
{
    bool CanHandle(OutboxEvent outboxEvent);
    Task HandleAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}

public sealed class NovelAgentOutboxHandler(
    NovelAgentDbContext db,
    IPrefixMergeService prefixMerge,
    ProductionApplicationService productions,
    IUserScope userScope) : INovelAgentOutboxHandler
{
    public const string AcceptanceGateReachedEventType = "novel_agent_acceptance_gate_reached";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool CanHandle(OutboxEvent outboxEvent) =>
        outboxEvent.EventType is "agent_stream_event" or "canon_merge_requested" or AcceptanceGateReachedEventType;

    public async Task HandleAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        if (outboxEvent.EventType == "agent_stream_event")
            return;

        if (outboxEvent.EventType == AcceptanceGateReachedEventType)
        {
            await HandleAcceptanceGateAsync(outboxEvent, cancellationToken);
            return;
        }

        var request = JsonSerializer.Deserialize<CanonMergeRequest>(outboxEvent.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Canon merge outbox payload is invalid.");
        if (request.UserId != outboxEvent.UserId || request.ProjectId != outboxEvent.ProjectId)
            throw new InvalidOperationException("Canon merge outbox scope does not match its payload.");

        using var _ = userScope.Enter(request.UserId);
        var branchStatus = await db.CanonBranches.AsNoTracking()
            .Where(item =>
                item.Id == request.BranchId &&
                item.UserId == request.UserId &&
                item.ProjectId == request.ProjectId &&
                item.GoalId == request.GoalId)
            .Select(item => item.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (branchStatus == "needs_decision")
        {
            await productions.HandleCanonMergeRejectedAsync(
                request.UserId,
                request.ProductionId,
                request.RequestId,
                "Canon baseline conflict requires a workflow decision.",
                request.CorrelationId,
                cancellationToken);
            return;
        }
        try
        {
            await prefixMerge.MergeAcceptedPrefixAsync(request.BranchId, cancellationToken);
            await productions.HandleCanonMergedAsync(
                request.UserId,
                request.ProductionId,
                request.RequestId,
                hasMoreBatches: false,
                request.CorrelationId,
                cancellationToken);
        }
        catch (CanonMergeConflictException conflict)
        {
            await productions.HandleCanonMergeRejectedAsync(
                request.UserId,
                request.ProductionId,
                request.RequestId,
                $"Canon baseline conflict: {conflict.GoalBaselineVersion} -> {conflict.CurrentCanonVersion}",
                request.CorrelationId,
                cancellationToken);
        }
    }

    private async Task HandleAcceptanceGateAsync(
        OutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AcceptanceGateReachedPayload>(outboxEvent.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Acceptance gate outbox payload is invalid.");
        if (payload.UserId != outboxEvent.UserId ||
            payload.ProjectId != outboxEvent.ProjectId ||
            payload.ProductionId != outboxEvent.AggregateId)
        {
            throw new InvalidOperationException("Acceptance gate outbox scope does not match its payload.");
        }

        using var _ = userScope.Enter(payload.UserId);
        var task = await db.KernelTasks.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == payload.TaskId &&
            item.UserId == payload.UserId &&
            item.ProjectId == payload.ProjectId &&
            item.GoalId == payload.GoalId &&
            item.TaskGraphVersionId == payload.TaskGraphVersionId &&
            item.BranchId == payload.BranchId &&
            item.TaskType == "AcceptanceGate" &&
            item.Status == "awaiting_user",
            cancellationToken) ?? throw new InvalidOperationException(
            "Acceptance gate fact is no longer valid; production state was not advanced.");
        var dependencies = JsonSerializer.Deserialize<string[]>(task.DependencyTaskIdsJson) ?? [];
        var graphTasks = await db.KernelTasks.AsNoTracking()
            .Where(item =>
                item.UserId == payload.UserId &&
                item.TaskGraphVersionId == payload.TaskGraphVersionId)
            .ToListAsync(cancellationToken);
        var byNodeId = graphTasks.ToDictionary(TaskNodeId, StringComparer.Ordinal);
        if (!dependencies.All(dependency =>
                byNodeId.TryGetValue(dependency, out var dependencyTask) &&
                dependencyTask.Status is "completed" or "reused"))
        {
            throw new InvalidOperationException("Acceptance gate dependencies are not complete.");
        }

        var productionMatches = await db.ProductionBatches.AsNoTracking().AnyAsync(batch =>
            batch.UserId == payload.UserId &&
            batch.ProjectId == payload.ProjectId &&
            batch.GoalId == payload.GoalId &&
            batch.BookProductionId == payload.ProductionId &&
            batch.TaskGraphVersionId == payload.TaskGraphVersionId &&
            batch.CanonBranchId == payload.BranchId,
            cancellationToken);
        if (!productionMatches)
            throw new InvalidOperationException("Acceptance gate does not belong to the production batch.");

        await productions.ReachAcceptanceGateAsync(payload.UserId, payload.ProductionId, cancellationToken);
    }

    private static string TaskNodeId(KernelTask task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }
}

public sealed record AcceptanceGateReachedPayload(
    string UserId,
    string ProjectId,
    string GoalId,
    string ProductionId,
    string TaskGraphVersionId,
    string TaskId,
    string? BranchId);
