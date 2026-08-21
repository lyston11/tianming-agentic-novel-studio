using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Production;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Infrastructure;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;
using AgentProductionMode = Tianming.NovelAgent.Domain.Goals.ProductionMode;

namespace TM.Tests.NovelAgentRegression.Reliability;

/// <summary>
/// Slice 6 Application/Database E2E: one PostgreSQL instance, both migration
/// histories, real transactions/outbox/worker claim, a deterministic agent
/// runtime, and exactly one worker driving Conversation → Proposal/Goal →
/// Production → Task → Candidate, followed by two separate user actions
/// (Acceptance, then Merge) that increase the Canon version and leave the
/// merged chapter body queryable.
/// </summary>
public sealed class AgentToCanonE2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _workerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var admin = CreateNovelAdminDbContext();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await admin.Database.MigrateAsync();
        await using (var agentMigration = CreateAgentAdminDbContext())
            await agentMigration.Database.MigrateAsync();
        await admin.Database.ExecuteSqlRawAsync("""
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO novelagent_app;
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO novelagent_app;
            """);

        _workerConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Conversation_to_canon_single_chapter_with_one_worker_and_two_user_actions()
    {
        await using var agentDb = CreateAgentAdminDbContext();
        await using var novelDb = CreateNovelAdminDbContext();

        var clock = new UtcClock();
        var ids = new SequentialIdGenerator();
        var hasher = new Sha256ContractHasher();
        var store = new EfAgentControlStore(agentDb, clock, ids, hasher);
        var userScope = new AgentUserScope();
        var currentUser = new FixedCurrentUser("user-1");

        novelDb.Users.Add(new User
        {
            Id = "user-1",
            Username = "e2e-user",
            Email = "e2e-user@example.invalid",
            PasswordHash = "e2e-not-a-real-password",
            Role = "user"
        });
        novelDb.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "正史合并 E2E 项目"
        });
        novelDb.Chapters.Add(new Chapter
        {
            Id = "chapter-1",
            ProjectId = "project-1",
            Title = "第一章",
            ChapterNumber = 1
        });
        await novelDb.SaveChangesAsync();

        // 1. Bound conversation turn with a deterministic runtime creates an auditable proposal.
        var conversation = new ConversationApplicationService(
            new ProposalRuntime(NewContract()),
            BoundSession(),
            store,
            new CapturingTransientStream(),
            store,
            store,
            ids,
            hasher,
            clock);
        var turn = await conversation.AppendTurnAsync(
            "user-1",
            "session-1",
            new AppendConversationTurnRequest("turn-1", "写第一章"));
        Assert.NotNull(turn.Decision.ProposalId);

        // 2. User confirms the proposal: Goal/Revision/Production/Batch/Graph/Tasks/Branch in one transaction.
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
        var confirmed = await workflow.ConfirmProposalAsync(
            "user-1",
            turn.Decision.ProposalId!,
            "user-1",
            new ConfirmGoalProposalRequest("confirm-1"));

        var productions = new ProductionApplicationService(
            store,
            store,
            new OutboxCanonMergePort(agentDb, ids, clock),
            store,
            store,
            ids,
            clock);
        await productions.StartAsync("user-1", confirmed.ProductionId);
        agentDb.ChangeTracker.Clear();
        Assert.Equal("running", await agentDb.ProductionBatches.Select(x => x.Status).SingleAsync());

        // 3. One worker with deterministic kernel execution drives every dispatchable task.
        var scheduler = CreateScheduler(agentDb, userScope);
        var branchId = await agentDb.CanonBranches.Select(x => x.Id).SingleAsync();
        var failedOnce = false;
        CandidateChapter? candidate = null;
        var canonBranches = new CanonBranchService(novelDb, currentUser, new EfLegacyControlPlaneCommands(
            agentDb, store, clock, ids, userScope));
        while (true)
        {
            var claim = await scheduler.ClaimNextAsync("e2e-worker", TimeSpan.FromMinutes(2));
            if (claim is null)
                break;

            if (!failedOnce)
            {
                failedOnce = true;
                await scheduler.FailAsync(claim, new KernelTaskFailure(
                    KernelTaskFailureCategory.Transient,
                    "simulated transient model timeout"));
                claim = await WaitForRetryAsync(scheduler, claim.TaskId);
            }

            var artifactId = $"artifact-{ids.NewId()}";
            if (claim.TaskType == "WriteCandidate")
            {
                var draft = new
                {
                    artifactId,
                    chapterId = "chapter-1",
                    status = "draft_generated",
                    draftContent = "第一章正文：主角在雨夜推开了旧宅的门。",
                    committedContent = "",
                    changesJson = "",
                    hasChanges = false,
                    repairAttemptCount = 0
                };
                agentDb.KernelArtifacts.Add(new KernelArtifactRecord
                {
                    Id = artifactId,
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    GoalId = claim.GoalId,
                    TaskId = claim.TaskId,
                    BranchId = claim.BranchId,
                    ArtifactType = "CandidateChapterDraft",
                    SchemaVersion = 1,
                    ContentJson = JsonSerializer.Serialize(draft),
                    ContentHash = Sha256(JsonSerializer.Serialize(draft)),
                    Status = "proposed",
                    Authorship = "agent"
                });
                await agentDb.SaveChangesAsync();
                candidate = await canonBranches.AddCandidateAsync(
                    branchId,
                    "chapter-1",
                    1,
                    artifactId,
                    dependsOnCandidateChapterId: null,
                    authorship: "agent",
                    isProtected: false);
                await scheduler.CompleteAsync(claim, [artifactId]);
            }
            else
            {
                var payload = JsonSerializer.Serialize(new { ok = true, taskId = claim.TaskId });
                agentDb.KernelArtifacts.Add(new KernelArtifactRecord
                {
                    Id = artifactId,
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    GoalId = claim.GoalId,
                    TaskId = claim.TaskId,
                    BranchId = claim.BranchId,
                    ArtifactType = "StepOutput",
                    SchemaVersion = 1,
                    ContentJson = payload,
                    ContentHash = Sha256(payload),
                    Status = "adopted",
                    Authorship = "agent"
                });
                await agentDb.SaveChangesAsync();
                await scheduler.CompleteAsync(claim, [artifactId]);
            }
            agentDb.ChangeTracker.Clear();
        }

        // The worker loop must have produced exactly one candidate version.
        Assert.NotNull(candidate);
        Assert.Equal(1, candidate!.Version);
        Assert.Equal(1, failedOnce ? 1 : 0);

        // 4. Outbox delivery of the acceptance-gate bridge advances production to awaiting_acceptance.
        var handler = new NovelAgentOutboxHandler(novelDb, new PrefixMergeService(
            novelDb,
            currentUser,
            new ContentDocumentService(novelDb),
            new ThrowingConflictModel()), productions, userScope);
        var delivered = await PumpOutboxAsync(novelDb, handler);
        Assert.Contains(delivered, x => x.EventType == NovelAgentOutboxHandler.AcceptanceGateReachedEventType);
        Assert.Equal("awaiting_acceptance", await agentDb.BookProductions.Select(x => x.Status).SingleAsync());
        // 5. First user action: accept the candidate (human, auditable).
        var acceptance = await canonBranches.AcceptAsync(candidate.Id, candidate.Version);
        Assert.Equal("accepted", acceptance.Decision);
        Assert.Equal("user-1", acceptance.DecidedByUserId);

        // 6. Second user action: merge the accepted prefix (idempotent request).
        var mergeRequestId = await productions.AcceptPrefixAsync(
            "user-1",
            confirmed.ProductionId,
            branchId,
            1,
            "workflow-worker",
            "accept-1",
            confirmed.CorrelationId);
        var duplicateMergeRequestId = await productions.AcceptPrefixAsync(
            "user-1",
            confirmed.ProductionId,
            branchId,
            1,
            "workflow-worker",
            "accept-1",
            confirmed.CorrelationId);
        Assert.Equal(mergeRequestId, duplicateMergeRequestId);

        // 7. Outbox delivers canon_merge_requested; prefix merge writes Canon truth; production completes.
        delivered = await PumpOutboxAsync(novelDb, handler);
        Assert.Contains(delivered, x => x.EventType == "canon_merge_requested");
        Assert.Equal("completed", await agentDb.BookProductions.Select(x => x.Status).SingleAsync());

        // Redelivery of both facts is idempotent.
        await RedeliverAllAsync(novelDb, handler);

        agentDb.ChangeTracker.Clear();
        novelDb.ChangeTracker.Clear();

        // 8. Canon version increased and merged body queryable.
        var mergeRecord = await novelDb.BranchMergeRecords.AsNoTracking().SingleAsync();
        Assert.Equal("canon-v1", mergeRecord.PreviousCanonVersion);
        Assert.StartsWith("canon:", mergeRecord.NewCanonVersion);
        Assert.NotEqual(mergeRecord.PreviousCanonVersion, mergeRecord.NewCanonVersion);
        Assert.Equal(1, mergeRecord.StartChapterNumber);
        Assert.Equal(1, mergeRecord.EndChapterNumber);

        var chapterVersion = await novelDb.ChapterVersions.AsNoTracking()
            .Where(version => version.ChapterId == "chapter-1")
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();
        Assert.Equal(1, chapterVersion.VersionNumber);
        var document = await novelDb.ContentDocuments.AsNoTracking()
            .Where(item => item.SourceType == "chapter" && item.SourceId == "chapter-1" && item.DocumentRole == "chapter_body")
            .Select(item => item.Id)
            .ToListAsync();
        Assert.NotEmpty(document);
        Assert.True(await novelDb.ContentChunks.AsNoTracking().AnyAsync(chunk =>
            document.Contains(chunk.DocumentId) && chunk.ChunkText.Contains("主角在雨夜推开了旧宅的门。")));

        // 9. Acceptance evidence chain is complete and protected by lease/fence semantics.
        Assert.Single(await novelDb.CandidateAcceptances.AsNoTracking().ToListAsync());
        Assert.Equal(1, await novelDb.KernelArtifacts.AsNoTracking().CountAsync(artifact =>
            artifact.ArtifactType == "AcceptanceDecision" &&
            artifact.Id == $"acceptance-decision:{acceptance.Id}"));
        var productionRow = await agentDb.BookProductions.AsNoTracking().SingleAsync();
        Assert.Equal("completed", productionRow.Status);
        Assert.Null(productionRow.CanonLeaseOwner);
        var lease = await agentDb.CanonWriteLeases.AsNoTracking().SingleOrDefaultAsync();
        Assert.NotNull(lease);
        Assert.Equal("workflow-worker", lease!.Owner);
        Assert.True(lease.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(10));

        // 10. The retried task kept its attempt history and still completed.
        var retriedTask = await agentDb.KernelTasks.AsNoTracking()
            .Where(task => task.Attempt > 1)
            .ToListAsync();
        Assert.NotEmpty(retriedTask);
        Assert.All(retriedTask, task => Assert.Equal("completed", task.Status));

        // 11. Restart recovery: a fresh runtime host rebuilds Run context from durable messages only.
        await using var freshAgentDb = CreateAgentAdminDbContext();
        var freshStore = new EfAgentControlStore(freshAgentDb, clock, ids, hasher);
        var durableMessages = await freshStore.ReadMessagesAsync("user-1", "session-1", CancellationToken.None);
        Assert.Contains(durableMessages, message => message.Role == "user" && message.Content.Contains("写第一章"));
        Assert.Contains(durableMessages, message => message.Role == "assistant");
    }

    private static async Task<List<OutboxEvent>> PumpOutboxAsync(
        PostgresNovelAgentDbContext novelDb,
        NovelAgentOutboxHandler handler)
    {
        var pending = await novelDb.OutboxEvents
            .Where(x => x.Status == "pending")
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .AsNoTracking()
            .ToListAsync();
        var handled = new List<OutboxEvent>();
        foreach (var row in pending)
        {
            if (!handler.CanHandle(row))
                continue;
            await handler.HandleAsync(row, CancellationToken.None);
            var owned = await novelDb.OutboxEvents.SingleAsync(x => x.Id == row.Id);
            owned.Status = "processed";
            owned.CompletedAt = DateTime.UtcNow;
            handled.Add(row);
        }
        await novelDb.SaveChangesAsync();
        return handled;
    }

    private static async Task RedeliverAllAsync(PostgresNovelAgentDbContext novelDb, NovelAgentOutboxHandler handler)
    {
        var processed = await novelDb.OutboxEvents
            .Where(x => x.Status == "processed")
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
        foreach (var row in processed)
        {
            if (handler.CanHandle(row))
                await handler.HandleAsync(row, CancellationToken.None);
        }
        novelDb.ChangeTracker.Clear();
    }

    private static async Task<KernelTaskClaim> WaitForRetryAsync(
        PostgresKernelTaskScheduler scheduler,
        string taskId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var claim = await scheduler.ClaimNextAsync("e2e-worker", TimeSpan.FromMinutes(2));
            if (claim is not null && claim.TaskId == taskId)
                return claim;
        }
        throw new InvalidOperationException($"Task {taskId} did not become claimable after transient failure.");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static GoalContract NewContract() => new(
        "Write the first chapter",
        AgentProductionMode.InteractiveBatch,
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

    private static IConversationSessionBindingReader BoundSession() =>
        new FixedConversationSessionBindingReader(new BoundConversationBinding("project-1"));

    private PostgresKernelTaskScheduler CreateScheduler(AgentControlDbContext db, AgentUserScope userScope)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        return new PostgresKernelTaskScheduler(
            db,
            userScope,
            new BackgroundClaimConnectionFactory(configuration));
    }

    private PostgresNovelAgentDbContext CreateNovelAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private AgentControlDbContext CreateAgentAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new AgentControlDbContext(options);
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;

        public string? TryGetUserId() => userId;

        public string GetUsername() => "e2e-user";

        public string GetEmail() => "e2e-user@example.invalid";

        public string GetRole() => "user";

        public bool IsAdmin() => false;

        public bool IsAuthenticated() => true;
    }

    private sealed class UtcClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _next;

        public string NewId() => $"e2e-{Interlocked.Increment(ref _next):x8}";
    }

    private sealed class CapturingTransientStream : ITransientAgentStream
    {
        public Task PublishTokenDeltaAsync(
            string userId,
            ConversationBinding binding,
            string sessionId,
            string correlationId,
            string delta,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedConversationSessionBindingReader(ConversationBinding binding)
        : IConversationSessionBindingReader
    {
        public Task<ConversationBinding> GetBindingAsync(
            string userId,
            string sessionId,
            CancellationToken cancellationToken) => Task.FromResult(binding);
    }

    private sealed class ThrowingConflictModel : ICanonMergeConflictModelClient
    {
        public Task<CanonMergeConflictReview> ReviewAsync(
            CanonMergeConflictReviewRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deterministic E2E must not need a conflict model.");
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
                "我提议先完成第一章。",
                ConversationDecisionKind.ProposeGoal,
                contract,
                ["我提议先完成第一章。"],
                Reason: null,
                ToolCalls: null,
                Checkpoint: null,
                Messages: null));
        }
    }
}
