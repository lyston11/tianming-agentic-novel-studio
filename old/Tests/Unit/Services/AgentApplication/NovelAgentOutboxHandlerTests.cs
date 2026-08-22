using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Production;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Canon;
using Xunit;
using KernelTaskEntity = TM.Web.NovelAgentWeb.Data.Entities.KernelTask;
using ProductionAggregate = Tianming.NovelAgent.Domain.Production.Production;

namespace Tests.Unit.Services.AgentApplication;

public sealed class NovelAgentOutboxHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Acceptance_gate_delivery_advances_production_once()
    {
        await using var db = CreateDb();
        db.KernelTasks.AddRange(
            KernelTask("graph-1:review", "ReviewContinuity", "completed", "[]"),
            KernelTask("graph-1:accept", "AcceptanceGate", "awaiting_user", "[\"review\"]"));
        db.ProductionBatches.Add(new ProductionBatch
        {
            Id = "batch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BookProductionId = "production-1",
            BatchNumber = 1,
            StartChapterNumber = 1,
            EndChapterNumber = 1,
            Status = "running",
            TaskGraphVersionId = "graph-1",
            CanonBranchId = "branch-1"
        });
        await db.SaveChangesAsync();
        var production = NewProduction();
        production.Start();
        var fixture = CreateHandler(db, production, canonRequest: null);
        var payload = new AcceptanceGateReachedPayload(
            "user-1",
            "project-1",
            "goal-1",
            "production-1",
            "graph-1",
            "graph-1:accept",
            "branch-1");
        var outbox = Outbox(
            NovelAgentOutboxHandler.AcceptanceGateReachedEventType,
            "production-1",
            payload);

        await fixture.Handler.HandleAsync(outbox, CancellationToken.None);
        await fixture.Handler.HandleAsync(outbox, CancellationToken.None);

        Assert.Equal(ProductionStatus.AwaitingAcceptance, production.Status);
        Assert.Single(fixture.Events.Items);
        Assert.Equal("ProductionAwaitingAcceptance", fixture.Events.Items[0].EventType);
    }

    [Fact]
    public async Task Canon_conflict_redelivery_uses_persisted_needs_decision_state()
    {
        await using var db = CreateDb();
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Status = "needs_decision",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        await db.SaveChangesAsync();
        var production = NewProduction();
        production.Start();
        production.ReachAcceptanceGate();
        var lease = new CanonWriteLease("workflow:user-1", Now.AddMinutes(5), 1);
        production.AcceptPrefix(lease, lease.Owner, Now);
        var request = new CanonMergeRequest(
            "merge-1",
            "user-1",
            "project-1",
            "goal-1",
            "production-1",
            "branch-1",
            1,
            lease,
            "correlation-1",
            "accept-1");
        var prefix = new Mock<IPrefixMergeService>(MockBehavior.Strict);
        var fixture = CreateHandler(db, production, request, prefix.Object);
        var outbox = Outbox("canon_merge_requested", "production-1", request);

        await fixture.Handler.HandleAsync(outbox, CancellationToken.None);
        await fixture.Handler.HandleAsync(outbox, CancellationToken.None);

        Assert.Equal(ProductionStatus.Blocked, production.Status);
        Assert.Single(fixture.Events.Items);
        Assert.Equal("CanonMergeRejected", fixture.Events.Items[0].EventType);
        prefix.VerifyNoOtherCalls();
    }

    private static HandlerFixture CreateHandler(
        NovelAgentDbContext db,
        ProductionAggregate production,
        CanonMergeRequest? canonRequest,
        IPrefixMergeService? prefixMerge = null)
    {
        var repository = new FakeProductionRepository(production);
        var events = new RecordingEventWriter();
        var service = new ProductionApplicationService(
            repository,
            new UnusedLeaseManager(),
            new FakeCanonPort(canonRequest),
            events,
            new ImmediateUnitOfWork(),
            new SequentialIds(),
            new FixedClock());
        var handler = new NovelAgentOutboxHandler(
            db,
            prefixMerge ?? Mock.Of<IPrefixMergeService>(),
            service,
            new AgentUserScope());
        return new HandlerFixture(handler, events);
    }

    private static ProductionAggregate NewProduction() => new(
        "production-1",
        "user-1",
        "project-1",
        "goal-1",
        "revision-1",
        ProductionMode.InteractiveBatch,
        FirstBatchTaskGraphCompiler.Compile("graph-1", ProductionMode.InteractiveBatch, 1));

    private static KernelTaskEntity KernelTask(string id, string type, string status, string dependencies) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        GoalId = "goal-1",
        TaskGraphVersionId = "graph-1",
        BranchId = "branch-1",
        KernelName = "test",
        TaskType = type,
        Status = status,
        DependencyTaskIdsJson = dependencies,
        IdempotencyKey = id
    };

    private static OutboxEvent Outbox(string eventType, string aggregateId, object payload) => new()
    {
        Id = $"outbox-{eventType}",
        UserId = "user-1",
        ProjectId = "project-1",
        EventType = eventType,
        AggregateType = "book_production",
        AggregateId = aggregateId,
        IdempotencyKey = $"key-{eventType}",
        PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private sealed record HandlerFixture(
        NovelAgentOutboxHandler Handler,
        RecordingEventWriter Events);

    private sealed class FakeProductionRepository(ProductionAggregate production) : IProductionRepository
    {
        public Task<ProductionAggregate?> GetAsync(string userId, string productionId, CancellationToken cancellationToken) =>
            Task.FromResult(production.UserId == userId && production.Id == productionId ? production : null);

        public Task<ProductionAggregate?> FindByGoalIdAsync(string userId, string goalId, CancellationToken cancellationToken) =>
            Task.FromResult(production.UserId == userId && production.GoalId == goalId ? production : null);

        public Task AddAsync(
            ProductionAggregate value,
            GoalContract contract,
            FrozenContextReference context,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(ProductionAggregate value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCanonPort(CanonMergeRequest? request) : ICanonMergePort
    {
        public Task<CanonMergeRequest?> FindByIdempotencyKeyAsync(
            string userId,
            string productionId,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult<CanonMergeRequest?>(null);

        public Task<CanonMergeRequest?> GetAsync(
            string userId,
            string requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(request?.UserId == userId && request.RequestId == requestId ? request : null);

        public Task RequestMergeAsync(CanonMergeRequest value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedLeaseManager : ICanonLeaseManager
    {
        public Task<CanonWriteLease> AcquireAsync(
            string userId,
            string projectId,
            string productionId,
            string owner,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingEventWriter : IAgentEventWriter
    {
        public List<AgentDomainEvent> Items { get; } = [];

        public Task AppendAsync(
            AgentDomainEvent domainEvent,
            AgentStreamKind streamKind,
            string streamId,
            object publicData,
            CancellationToken cancellationToken)
        {
            Items.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateUnitOfWork : IAgentUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;
        public string NewId() => $"event-{Interlocked.Increment(ref _next)}";
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
