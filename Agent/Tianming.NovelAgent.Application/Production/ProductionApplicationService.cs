using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using ProductionAggregate = Tianming.NovelAgent.Domain.Production.Production;

namespace Tianming.NovelAgent.Application.Production;

public sealed class ProductionApplicationService(
    IProductionRepository productions,
    ICanonLeaseManager leases,
    ICanonMergePort canon,
    IAgentEventWriter events,
    IAgentUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock)
{
    public async Task StartAsync(string userId, string productionId, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(userId, productionId, "ProductionStarted", production => production.Start(), cancellationToken);
    }

    public async Task ReachAcceptanceGateAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(userId, productionId, cancellationToken);
        if (current.Status == Tianming.NovelAgent.Domain.Production.ProductionStatus.AwaitingAcceptance)
            return;
        // Duplicate acceptance-gate deliveries must stay no-ops even after the
        // production reached a terminal state through a later user merge.
        if (current.Status is Tianming.NovelAgent.Domain.Production.ProductionStatus.Completed
            or Tianming.NovelAgent.Domain.Production.ProductionStatus.Failed
            or Tianming.NovelAgent.Domain.Production.ProductionStatus.Cancelled)
            return;
        await ChangeAsync(
            userId,
            productionId,
            "ProductionAwaitingAcceptance",
            production => production.ReachAcceptanceGate(),
            cancellationToken);
    }

    public Task PauseAsync(
        string userId,
        string productionId,
        bool hasRunningTask,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            userId,
            productionId,
            "ProductionPauseRequested",
            production => production.Pause(hasRunningTask),
            cancellationToken);

    public Task ReachSafePointAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            userId,
            productionId,
            "ProductionPaused",
            production => production.ReachSafePoint(),
            cancellationToken);

    public Task ResumeAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            userId,
            productionId,
            "ProductionResumed",
            production => production.Resume(),
            cancellationToken);

    public Task CancelAsync(
        string userId,
        string productionId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            userId,
            productionId,
            "ProductionCancelled",
            production => production.Cancel(reason),
            cancellationToken);

    public async Task<string> AcceptPrefixAsync(
        string userId,
        string productionId,
        string branchId,
        int acceptedThroughChapter,
        string owner,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await canon.FindByIdempotencyKeyAsync(
            userId,
            productionId,
            idempotencyKey,
            cancellationToken);
        if (existing is not null)
            return existing.RequestId;

        var production = await GetAsync(userId, productionId, cancellationToken);
        var requestId = ids.NewId();

        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var lease = await leases.AcquireAsync(
                userId,
                production.ProjectId,
                production.Id,
                owner,
                ct);
            production.AcceptPrefix(lease, owner, clock.UtcNow);
            await productions.UpdateAsync(production, ct);
            var request = new CanonMergeRequest(
                requestId,
                userId,
                production.ProjectId,
                production.GoalId,
                production.Id,
                branchId,
                acceptedThroughChapter,
                lease,
                correlationId,
                idempotencyKey);
            await canon.RequestMergeAsync(request, ct);
            await WriteEventAsync(production, "CanonMergeRequested", correlationId, request, ct);
            return requestId;
        }, cancellationToken);
    }

    public async Task HandleCanonMergedAsync(
        string userId,
        string productionId,
        string mergeRequestId,
        bool hasMoreBatches,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = await canon.GetAsync(userId, mergeRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"Canon merge request {mergeRequestId} was not found.");
        if (request.ProductionId != productionId)
            throw new InvalidOperationException("The canon merge request belongs to another production.");
        var current = await GetAsync(userId, productionId, cancellationToken);
        if (current.Status is Tianming.NovelAgent.Domain.Production.ProductionStatus.Completed
            or Tianming.NovelAgent.Domain.Production.ProductionStatus.Running)
            return;

        await ChangeAsync(
            userId,
            productionId,
            "CanonPrefixMerged",
            production => production.CanonMerged(hasMoreBatches),
            cancellationToken,
            correlationId,
            mergeRequestId);
    }

    public async Task HandleCanonMergeRejectedAsync(
        string userId,
        string productionId,
        string mergeRequestId,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = await canon.GetAsync(userId, mergeRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"Canon merge request {mergeRequestId} was not found.");
        if (request.ProductionId != productionId)
            throw new InvalidOperationException("The canon merge request belongs to another production.");
        var current = await GetAsync(userId, productionId, cancellationToken);
        if (current.Status == Tianming.NovelAgent.Domain.Production.ProductionStatus.Blocked)
            return;
        await ChangeAsync(
            userId,
            productionId,
            "CanonMergeRejected",
            production => production.Block(reason),
            cancellationToken,
            correlationId,
            mergeRequestId);
    }

    private async Task ChangeAsync(
        string userId,
        string productionId,
        string eventType,
        Action<ProductionAggregate> transition,
        CancellationToken cancellationToken,
        string? correlationId = null,
        string? causationId = null)
    {
        var production = await GetAsync(userId, productionId, cancellationToken);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            transition(production);
            await productions.UpdateAsync(production, ct);
            await WriteEventAsync(production, eventType, correlationId ?? ids.NewId(), new { production.Status }, ct, causationId);
            return true;
        }, cancellationToken);
    }

    private async Task<ProductionAggregate> GetAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken) =>
        await productions.GetAsync(userId, productionId, cancellationToken)
        ?? throw new KeyNotFoundException($"Production {productionId} was not found.");

    private Task WriteEventAsync(
        ProductionAggregate production,
        string eventType,
        string correlationId,
        object payload,
        CancellationToken cancellationToken,
        string? causationId = null) =>
        events.AppendAsync(
            new AgentDomainEvent(
                ids.NewId(),
                eventType,
                "Production",
                production.Id,
                production.Version,
                production.UserId,
                production.ProjectId,
                production.GoalId,
                correlationId,
                causationId,
                clock.UtcNow,
                JsonSerializer.Serialize(payload)),
            AgentStreamKind.Workflow,
            production.ProjectId,
            payload,
            cancellationToken);
}
