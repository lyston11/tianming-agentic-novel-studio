using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Models;
using Tianming.NovelAgent.Application.Production;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Contracts.Models;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;
using Tianming.NovelAgent.Infrastructure;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tests.AgentArchitecture;

public sealed class PostgresVerticalSliceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("novel_agent_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Proposal_to_canon_merge_uses_one_persisted_control_plane()
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new AgentControlDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        var ids = new SequentialIdGenerator();
        var hasher = new Sha256ContractHasher();
        var store = new EfAgentControlStore(db, clock, ids, hasher);
        var runtime = new ProposalRuntime(NewContract());
        var transient = new CapturingTransientStream();
        var conversation = new ConversationApplicationService(
            runtime,
            store,
            transient,
            store,
            store,
            ids,
            hasher,
            clock);
        var workflow = new WorkflowApplicationService(
            store,
            store,
            store,
            store,
            store,
            store,
            ids,
            hasher,
            clock);

        var turn = await conversation.AppendTurnAsync(
            "user-1",
            "project-1",
            "session-1",
            new AppendConversationTurnRequest("turn-1", "Write the first chapter"));
        var duplicateTurn = await conversation.AppendTurnAsync(
            "user-1",
            "project-1",
            "session-1",
            new AppendConversationTurnRequest("turn-1", "This duplicate must not run"));

        Assert.Equal(turn, duplicateTurn);
        Assert.Equal(1, runtime.CallCount);
        Assert.Equal(["draft", " ready"], transient.Deltas);
        Assert.NotNull(turn.Decision.ProposalId);

        var confirmed = await workflow.ConfirmProposalAsync(
            "user-1",
            turn.Decision.ProposalId!,
            "user-1",
            new ConfirmGoalProposalRequest("confirm-1"));
        var duplicateConfirmation = await workflow.ConfirmProposalAsync(
            "user-1",
            turn.Decision.ProposalId!,
            "user-1",
            new ConfirmGoalProposalRequest("confirm-1"));

        Assert.Equal(confirmed.GoalId, duplicateConfirmation.GoalId);
        Assert.Equal(confirmed.ProductionId, duplicateConfirmation.ProductionId);
        Assert.Equal(confirmed.CorrelationId, duplicateConfirmation.CorrelationId);
        var persistedTasks = await db.KernelTasks.AsNoTracking().OrderBy(x => x.Priority).ToListAsync();
        Assert.Equal(10, persistedTasks.Count);
        Assert.Equal("planned", persistedTasks[0].Status);
        Assert.All(persistedTasks.Skip(1), task => Assert.Equal("blocked", task.Status));
        var persistedBatch = await db.ProductionBatches.AsNoTracking().SingleAsync();
        var persistedBranch = await db.CanonBranches.AsNoTracking().SingleAsync();
        var persistedContext = await db.GoalContextSnapshots.AsNoTracking().SingleAsync();
        Assert.Equal("planned", persistedBatch.Status);
        Assert.Equal(persistedBranch.Id, persistedBatch.CanonBranchId);
        Assert.Equal(confirmed.GoalId, persistedContext.GoalId);
        Assert.Equal("canon-v1", persistedContext.CanonVersion);
        Assert.All(persistedTasks, task => Assert.Equal(persistedBranch.Id, task.BranchId));

        var mergeOutbox = new OutboxCanonMergePort(db, ids, clock);
        var production = new ProductionApplicationService(store, store, mergeOutbox, store, store, ids, clock);
        await production.StartAsync("user-1", confirmed.ProductionId);
        db.ChangeTracker.Clear();
        Assert.Equal("ready", await db.KernelTasks.OrderBy(x => x.Priority).Select(x => x.Status).FirstAsync());
        Assert.Equal("running", await db.ProductionBatches.Select(x => x.Status).SingleAsync());
        await production.ReachAcceptanceGateAsync("user-1", confirmed.ProductionId);
        var mergeRequestId = await production.AcceptPrefixAsync(
            "user-1",
            confirmed.ProductionId,
            persistedBranch.Id,
            1,
            "workflow-worker",
            "accept-1",
            confirmed.CorrelationId);
        var duplicateMergeRequestId = await production.AcceptPrefixAsync(
            "user-1",
            confirmed.ProductionId,
            persistedBranch.Id,
            1,
            "workflow-worker",
            "accept-1",
            confirmed.CorrelationId);
        Assert.Equal(mergeRequestId, duplicateMergeRequestId);
        await production.HandleCanonMergedAsync(
            "user-1",
            confirmed.ProductionId,
            mergeRequestId,
            false,
            confirmed.CorrelationId);
        await production.HandleCanonMergedAsync(
            "user-1",
            confirmed.ProductionId,
            mergeRequestId,
            false,
            confirmed.CorrelationId);

        db.ChangeTracker.Clear();
        var persistedProduction = await db.BookProductions.AsNoTracking().SingleAsync();
        Assert.Equal("completed", persistedProduction.Status);
        Assert.Equal(5, persistedProduction.AggregateVersion);
        Assert.Null(persistedProduction.CanonLeaseOwner);

        var conversationEvents = await store.ReadAsync(
            "user-1",
            AgentStreamKind.Conversation,
            "session-1",
            0,
            100,
            CancellationToken.None);
        var workflowEvents = await store.ReadAsync(
            "user-1",
            AgentStreamKind.Workflow,
            "project-1",
            0,
            100,
            CancellationToken.None);

        Assert.Single(conversationEvents);
        Assert.Equal(Enumerable.Range(1, 5).Select(x => (long)x), workflowEvents.Select(x => x.Sequence));
        Assert.Contains(await db.OutboxEvents.AsNoTracking().ToListAsync(), x =>
            x.EventType == "canon_merge_requested" && x.IdempotencyKey ==
                $"canon-merge:user-1:{confirmed.ProductionId}:accept-1");
        Assert.Equal(6, await db.DomainEvents.CountAsync());
        Assert.Equal(7, await db.OutboxEvents.CountAsync());

        var modelStore = new EfModelExecutionStore(db, ids, clock);
        var provider = new StubModelProvider("openai");
        var gateway = new AuditedModelGateway([provider], modelStore);
        var modelRequest = NewModelRequest("model-call-1", "openai", confirmed.GoalId);
        var modelResult = await gateway.CompleteAsync(modelRequest, CancellationToken.None);
        var duplicateModelResult = await gateway.CompleteAsync(modelRequest, CancellationToken.None);
        Assert.Equivalent(modelResult, duplicateModelResult, strict: true);
        Assert.Equal(1, provider.CallCount);

        var failingGateway = new AuditedModelGateway([new FailingModelProvider()], modelStore);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingGateway.CompleteAsync(
            NewModelRequest("model-call-unknown", "failing", confirmed.GoalId),
            CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal("outcome_unknown", await db.ModelExecutions
            .Where(x => x.IdempotencyKey == "model-call-unknown")
            .Select(x => x.Status)
            .SingleAsync());
        var budget = await db.CreativeGoals.AsNoTracking()
            .Where(x => x.Id == confirmed.GoalId)
            .Select(x => new { x.ReservedCost, x.ActualCost })
            .SingleAsync();
        Assert.Equal(0.5m, budget.ReservedCost);
        Assert.Equal(0.1m, budget.ActualCost);
    }

    [Fact]
    public async Task Conversation_tool_confirmation_is_persisted_and_idempotent()
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new AgentControlDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        var ids = new SequentialIdGenerator();
        var hasher = new Sha256ContractHasher();
        var store = new EfAgentControlStore(db, clock, ids, hasher);
        var workflow = new WorkflowApplicationService(
            store,
            store,
            store,
            store,
            store,
            store,
            ids,
            hasher,
            clock);
        var tools = new AgentToolRegistry([new ConfirmCreativeGoalTool(workflow)]);
        var conversation = new ConversationApplicationService(
            new AutoConfirmRuntime(NewContract()),
            store,
            new CapturingTransientStream(),
            store,
            store,
            ids,
            hasher,
            clock,
            tools);

        var turn = await conversation.AppendTurnAsync(
            "user-1",
            "project-1",
            "session-1",
            new AppendConversationTurnRequest("auto-turn-1", "Start the production now"));
        var duplicate = await conversation.AppendTurnAsync(
            "user-1",
            "project-1",
            "session-1",
            new AppendConversationTurnRequest("auto-turn-1", "This must be idempotent"));

        Assert.NotNull(turn.Confirmation);
        Assert.Equal(turn.Confirmation, duplicate.Confirmation);
        Assert.Equal(turn.Decision.ProposalId, await db.GoalProposals.Select(x => x.Id).SingleAsync());
        Assert.Equal("confirmed", await db.GoalProposals.Select(x => x.Status).SingleAsync());
        Assert.Equal(turn.Confirmation!.GoalRevisionId, await db.GoalRevisions.Select(x => x.Id).SingleAsync());
        Assert.Equal(turn.Decision.ProposalId, await db.GoalRevisions.Select(x => x.SourceProposalId).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_confirmation_with_same_idempotency_key_returns_one_durable_result()
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        var ids = new SequentialIdGenerator();
        var hasher = new Sha256ContractHasher();

        string proposalId;
        await using (var setupDb = new AgentControlDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var setupStore = new EfAgentControlStore(setupDb, clock, ids, hasher);
            var conversation = new ConversationApplicationService(
                new ProposalRuntime(NewContract()),
                setupStore,
                new CapturingTransientStream(),
                setupStore,
                setupStore,
                ids,
                hasher,
                clock);
            var turn = await conversation.AppendTurnAsync(
                "user-1",
                "project-1",
                "session-1",
                new AppendConversationTurnRequest("concurrent-turn-1", "Write the first chapter"));
            proposalId = Assert.IsType<string>(turn.Decision.ProposalId);
        }

        await using var firstDb = new AgentControlDbContext(options);
        await using var secondDb = new AgentControlDbContext(options);
        var firstStore = new EfAgentControlStore(firstDb, clock, ids, hasher);
        var secondStore = new EfAgentControlStore(secondDb, clock, ids, hasher);
        var readBarrier = new ConfirmationReadBarrier();
        var firstWorkflow = NewWorkflow(firstStore, new CoordinatingGoalRepository(firstStore, readBarrier), ids, hasher, clock);
        var secondWorkflow = NewWorkflow(secondStore, new CoordinatingGoalRepository(secondStore, readBarrier), ids, hasher, clock);
        var request = new ConfirmGoalProposalRequest("concurrent-confirm-1");

        var results = await Task.WhenAll(
            firstWorkflow.ConfirmProposalAsync("user-1", proposalId, "user-1", request),
            secondWorkflow.ConfirmProposalAsync("user-1", proposalId, "user-1", request));

        Assert.Equal(results[0], results[1]);
        await using var verificationDb = new AgentControlDbContext(options);
        Assert.Equal(1, await verificationDb.GoalRevisions.CountAsync());
        Assert.Equal(1, await verificationDb.CreativeGoals.CountAsync());
        Assert.Equal(1, await verificationDb.BookProductions.CountAsync());
        Assert.Equal(1, await verificationDb.DomainEvents.CountAsync(x => x.EventType == "GoalConfirmed"));
    }

    private static ModelRequest NewModelRequest(string idempotencyKey, string provider, string goalId) => new(
        "write_candidate",
        [new ModelMessage("user", "Write")],
        null,
        provider,
        "model-v1",
        "prompt-v1",
        "schema-v1",
        "context-hash",
        0.5m,
        $"correlation-{idempotencyKey}",
        idempotencyKey,
        UserId: "user-1",
        ProjectId: "project-1",
        GoalId: goalId,
        TaskId: "task-1");

    private static GoalContract NewContract() => new(
        "Write the first chapter",
        ProductionMode.InteractiveBatch,
        new ChapterRange(1, 1),
        ["Deliver a complete dramatic turn"],
        ["Protagonist identity"],
        ["Inciting incident"],
        ["Protected canon"],
        "human",
        "directed",
        10m,
        "canon-v1",
        "knowledge-v1",
        "quality-v1",
        "style-v1",
        new Dictionary<string, string> { ["writing"] = "model-v1" },
        new Dictionary<string, string> { ["agent"] = "protocol-v1" });

    private static WorkflowApplicationService NewWorkflow(
        EfAgentControlStore store,
        IGoalRepository goals,
        IIdGenerator ids,
        IContractHasher hasher,
        IClock clock) => new(
            store,
            goals,
            store,
            store,
            store,
            store,
            ids,
            hasher,
            clock);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _next;
        public string NewId() => $"id-{Interlocked.Increment(ref _next):D4}";
    }

    private sealed class ProposalRuntime(GoalContract contract) : IConversationAgentRuntime
    {
        public int CallCount { get; private set; }

        public Task<ConversationRuntimeResult> RunTurnAsync(
            ConversationTurnContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ConversationRuntimeResult(
                "The goal contract is ready for confirmation.",
                ConversationDecisionKind.ProposeGoal,
                contract,
                ["draft", " ready"]));
        }
    }

    private sealed class AutoConfirmRuntime(GoalContract contract) : IConversationAgentRuntime
    {
        public Task<ConversationRuntimeResult> RunTurnAsync(
            ConversationTurnContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationRuntimeResult(
                "The goal is being confirmed.",
                ConversationDecisionKind.ProposeGoal,
                contract,
                [],
                "The user explicitly committed.",
                [new AgentToolCall("confirm_creative_goal")]));
    }

    private sealed class StubModelProvider(string provider) : IModelProviderAdapter
    {
        public int CallCount { get; private set; }
        public string Provider => provider;
        public ModelProviderCapabilities Capabilities { get; } = new(true, false, false, true, false);

        public Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ModelResult(
                "draft",
                null,
                Provider,
                request.Model,
                "provider-request-1",
                "completed",
                new ModelUsage(10, 20, 0.1m, "USD"),
                new Dictionary<string, string>()));
        }
    }

    private sealed class FailingModelProvider : IModelProviderAdapter
    {
        public string Provider => "failing";
        public ModelProviderCapabilities Capabilities { get; } = new(true, false, false, false, false);

        public Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provider outcome is unknown");
    }

    private sealed class CapturingTransientStream : ITransientAgentStream
    {
        public List<string> Deltas { get; } = [];

        public Task PublishTokenDeltaAsync(
            string userId,
            string projectId,
            string sessionId,
            string correlationId,
            string delta,
            CancellationToken cancellationToken)
        {
            Deltas.Add(delta);
            return Task.CompletedTask;
        }
    }

    private sealed class ConfirmationReadBarrier
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivalCount) == 2)
                _bothRead.TrySetResult();
            await _bothRead.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CoordinatingGoalRepository(
        IGoalRepository inner,
        ConfirmationReadBarrier readBarrier) : IGoalRepository
    {
        public async Task<ConfirmGoalProposalResult?> FindConfirmationResultAsync(
            string userId,
            string projectId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var result = await inner.FindConfirmationResultAsync(
                userId,
                projectId,
                idempotencyKey,
                cancellationToken);
            if (result is null)
                await readBarrier.ArriveAsync(cancellationToken);
            return result;
        }

        public Task<CreativeGoal?> GetAsync(
            string userId,
            string goalId,
            CancellationToken cancellationToken) => inner.GetAsync(userId, goalId, cancellationToken);

        public Task AddAsync(CreativeGoal goal, CancellationToken cancellationToken) =>
            inner.AddAsync(goal, cancellationToken);

        public Task UpdateAsync(CreativeGoal goal, CancellationToken cancellationToken) =>
            inner.UpdateAsync(goal, cancellationToken);
    }
}
