using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Rework;
using TM.Web.NovelAgentWeb.Services.Settings;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// AC-15 stage 2: drive the Agent-to-Canon flow through the real HTTP API and
/// observe progress through the SSE stream endpoints. The application runs
/// against a real PostgreSQL container with both migration histories applied
/// by the host; the outbox host service delivers bridge and merge facts; user
/// actions go through authenticated API calls only. SSE payloads are treated
/// purely as change notifications — every state assertion re-reads the API
/// Read Model or the database, never the SSE payload.
/// </summary>
public sealed class AgentToCanonApiSseE2ETests(ApiSseFactory factory) : IClassFixture<ApiSseFactory>
{
    private readonly ApiSseFactory _factory = factory;

    [Fact]
    public async Task Api_driven_agent_to_canon_flow_notifies_via_sse_and_keeps_read_model_authoritative()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var auth = await RegisterAsync($"api-sse-{suffix}");
        var userId = auth.UserId;
        var client = CreateAuthenticatedClient(auth.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PostgresNovelAgentDbContext>();
            db.NovelProjects.Add(new NovelProject
            {
                Id = $"proj-{suffix}",
                UserId = userId,
                Title = "API/SSE E2E 项目"
            });
            db.Chapters.Add(new Chapter
            {
                Id = $"chap-{suffix}",
                ProjectId = $"proj-{suffix}",
                Title = "第一章",
                ChapterNumber = 1
            });
            await db.SaveChangesAsync();
        }

        // 1. Unbound session creation, then subscribe to the conversation stream.
        var sessionId = await CreateUnboundSessionAsync(client, suffix);
        var conversationEvents = new SseCollector();
        using var conversationStream = await OpenSseAsync(
            client, $"/api/novel-agent/streams/conversations/{sessionId}", conversationEvents);

        // 2. Unbound turn: discussion only, no proposal.
        var discussTurn = await PostTurnAsync(client, sessionId, $"turn-d-{suffix}", "帮我聊聊第一章可以写什么");
        Assert.Equal("DiscussOnly", discussTurn.DecisionKind);

        // 3. Explicit project activation with the persisted user message as provenance.
        using var activateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/novel-agent/conversations/{sessionId}/project-context/activate")
        {
            Content = JsonContent.Create(new
            {
                projectId = $"proj-{suffix}",
                expectedBindingVersion = 0,
                sourceUserMessageId = discussTurn.MessageId
            })
        };
        activateRequest.Headers.Add("Idempotency-Key", $"activate-{suffix}");
        var activate = await client.SendAsync(activateRequest);
        if (!activate.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Activation failed: {(int)activate.StatusCode} {await activate.Content.ReadAsStringAsync()}");

        // 4. Bound turn: deterministic runtime proposes the goal.
        var proposeTurn = await PostTurnAsync(client, sessionId, $"turn-p-{suffix}", "就按这个方案写第一章，开始吧");
        Assert.Equal("ProposeGoal", proposeTurn.DecisionKind);
        Assert.NotNull(proposeTurn.DecisionKind);

        // 5. Confirm the proposal, then subscribe to the workflow stream.
        var confirm = await client.PostAsJsonAsync(
            $"/api/novel-agent/proposals/{proposeTurn.ProposalId}/confirm",
            new { idempotencyKey = $"confirm-{suffix}" });
        confirm.EnsureSuccessStatusCode();
        var confirmPayload = await confirm.Content.ReadAsStringAsync();
        using (var confirmDoc = JsonDocument.Parse(confirmPayload))
        {
            var confirmData = confirmDoc.RootElement.GetProperty("data");
            var goalId = confirmData.GetProperty("goalId").GetString()!;
            var productionId = confirmData.GetProperty("productionId").GetString()!;
            var correlationId = confirmData.GetProperty("correlationId").GetString()!;

            var workflowEvents = new SseCollector();
            using var workflowStream = await OpenSseAsync(
                client, $"/api/novel-agent/streams/workflows/proj-{suffix}", workflowEvents);

            // 6. Start production via API; the in-test worker completes kernels
            //    deterministically while the hosted outbox service delivers facts.
            var start = await client.PostAsync($"/api/novel-agent/productions/{productionId}/commands/start", null);
            start.EnsureSuccessStatusCode();

            var (branchId, candidateId, candidateVersion) = await DriveWorkerAsync(userId, branchProbe: null, suffix);
            Assert.NotEqual(Guid.Empty.ToString("N"), candidateId);

            // 7. The hosted outbox service delivers the acceptance-gate bridge.
            await WaitForAsync(async () =>
                await QueryProductionStatusAsync(productionId) == "awaiting_acceptance");

            // 8. User acceptance through the chapter evidence API, then merge
            //    through the application command API. Duplicate merge keeps one
            //    request id.
            var accept = await client.PostAsJsonAsync(
                $"/api/goals/{goalId}/workflow/chapters/1/accept",
                new { candidateChapterId = candidateId, candidateVersion });
            accept.EnsureSuccessStatusCode();

            var merge = await client.PostAsJsonAsync(
                $"/api/novel-agent/productions/{productionId}/commands/accept-prefix",
                new { branchId, acceptedThroughChapter = 1, idempotencyKey = $"merge-{suffix}", correlationId });
            merge.EnsureSuccessStatusCode();
            using var mergeDoc = JsonDocument.Parse(await merge.Content.ReadAsStringAsync());
            var mergeRequestId = mergeDoc.RootElement.GetProperty("data").GetProperty("requestId").GetString()!;

            var mergeDuplicate = await client.PostAsJsonAsync(
                $"/api/novel-agent/productions/{productionId}/commands/accept-prefix",
                new { branchId, acceptedThroughChapter = 1, idempotencyKey = $"merge-{suffix}", correlationId });
            mergeDuplicate.EnsureSuccessStatusCode();
            using (var mergeDupDoc = JsonDocument.Parse(await mergeDuplicate.Content.ReadAsStringAsync()))
                Assert.Equal(mergeRequestId, mergeDupDoc.RootElement.GetProperty("data").GetProperty("requestId").GetString());

            // 9. The hosted outbox service delivers the canon merge request; the
            //    prefix merge writes Canon truth and the production completes.
            await WaitForAsync(async () => await QueryProductionStatusAsync(productionId) == "completed");

            // 10. Read Model authority: the workflow API reflects the final state.
            var readModel = await client.GetAsync($"/api/novel-agent/workflows/projects/proj-{suffix}");
            readModel.EnsureSuccessStatusCode();
            var readModelJson = await readModel.Content.ReadAsStringAsync();
            Assert.Contains("completed", readModelJson);

            // Canon truth: version increased and merged body queryable.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PostgresNovelAgentDbContext>();
                var record = await db.BranchMergeRecords.AsNoTracking().SingleAsync();
                Assert.Equal("canon-v1", record.PreviousCanonVersion);
                Assert.StartsWith("canon:", record.NewCanonVersion);
                Assert.NotEqual(record.PreviousCanonVersion, record.NewCanonVersion);
                var documentIds = await db.ContentDocuments.AsNoTracking()
                    .Where(item => item.SourceType == "chapter" && item.DocumentRole == "chapter_body")
                    .Select(item => item.Id)
                    .ToListAsync();
                Assert.NotEmpty(documentIds);
            }

            // 11. SSE was notification only: the streams carried the lifecycle
            //     facts while all state assertions above used API/DB reads.
            await workflowEvents.WaitForAsync(
                events => events.Any(x => x.Contains("GoalConfirmed", StringComparison.Ordinal))
                          && events.Any(x => x.Contains("ProductionStarted", StringComparison.Ordinal))
                          && events.Any(x => x.Contains("ProductionAwaitingAcceptance", StringComparison.Ordinal))
                          && events.Any(x => x.Contains("CanonPrefixMerged", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(30));
            await conversationEvents.WaitForAsync(
                events => events.Any(x => x.Contains("ConversationTurnCompleted", StringComparison.Ordinal))
                          && events.Any(x => x.Contains("GoalProposalCreated", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(10));
        }
    }

    private async Task<(string BranchId, string CandidateId, int CandidateVersion)> DriveWorkerAsync(
        string userId, string? branchProbe, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        using var userContext = backgroundUser.Push(userId);
        var scheduler = scope.ServiceProvider.GetRequiredService<IKernelTaskScheduler>();
        var branches = scope.ServiceProvider.GetRequiredService<ICanonBranchService>();
        var agentDb = scope.ServiceProvider.GetRequiredService<AgentControlDbContext>();
        var branchId = await agentDb.CanonBranches.AsNoTracking().Select(x => x.Id).SingleAsync();

        var failedOnce = false;
        string candidateId = Guid.Empty.ToString("N");
        var candidateVersion = 0;
        while (true)
        {
            var claim = await scheduler.ClaimNextAsync("api-sse-worker", TimeSpan.FromMinutes(2));
            if (claim is null)
                break;

            if (!failedOnce)
            {
                failedOnce = true;
                await scheduler.FailAsync(claim, new KernelTaskFailure(
                    KernelTaskFailureCategory.Transient, "api-sse simulated transient failure"));
                claim = await WaitForRetryAsync(scheduler, claim.TaskId);
            }

            var artifactId = $"art-{Guid.NewGuid():N}";
            if (claim.TaskType == "WriteCandidate")
            {
                var draft = new
                {
                    artifactId,
                    chapterId = $"chap-{suffix}",
                    status = "draft_generated",
                    draftContent = "第一章正文：海雾在黎明前散开，灯塔第一次亮起。",
                    committedContent = "",
                    changesJson = "",
                    hasChanges = false,
                    repairAttemptCount = 0
                };
                var contentJson = JsonSerializer.Serialize(draft);
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
                    ContentJson = contentJson,
                    ContentHash = Sha256(contentJson),
                    Status = "proposed",
                    Authorship = "agent"
                });
                await agentDb.SaveChangesAsync();
                var candidate = await branches.AddCandidateAsync(
                    branchId, $"chap-{suffix}", 1, artifactId, null, "agent", false);
                candidateId = candidate.Id;
                candidateVersion = candidate.Version;
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

        var retried = await agentDb.KernelTasks.AsNoTracking().AnyAsync(x => x.Attempt > 1);
        Assert.True(retried, "expected at least one retried kernel task");
        return (branchId, candidateId, candidateVersion);
    }

    private static async Task<KernelTaskClaim> WaitForRetryAsync(
        IKernelTaskScheduler scheduler, string taskId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var claim = await scheduler.ClaimNextAsync("api-sse-worker", TimeSpan.FromMinutes(2));
            if (claim is not null && claim.TaskId == taskId)
                return claim;
        }
        throw new InvalidOperationException($"Task {taskId} did not become claimable after transient failure.");
    }

    private async Task<string> QueryProductionStatusAsync(string productionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentControlDbContext>();
        return await db.BookProductions.AsNoTracking()
            .Where(x => x.Id == productionId)
            .Select(x => x.Status)
            .SingleAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutSeconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        throw new InvalidOperationException("Condition was not met before the timeout.");
    }

    private static async Task<string> CreateUnboundSessionAsync(HttpClient client, string suffix)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/session");
        request.Headers.Add("Idempotency-Key", $"sess-{suffix}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(string.Empty, data.GetProperty("activeProjectId").GetString());
        return data.GetProperty("sessionId").GetString()!;
    }

    private static async Task<TurnPayload> PostTurnAsync(
        HttpClient client, string sessionId, string idempotencyKey, string content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/novel-agent/conversations/{sessionId}/turns",
            new { idempotencyKey, content });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement.GetProperty("data");
        return new TurnPayload(
            root.GetProperty("messageId").GetString()!,
            root.GetProperty("decision").GetProperty("kind").GetString()!,
            root.GetProperty("decision").TryGetProperty("proposalId", out var proposalId)
                ? proposalId.GetString()
                : null);
    }

    private static async Task<HttpResponseMessage> OpenSseAsync(
        HttpClient client, string path, SseCollector collector)
    {
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path),
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        _ = Task.Run(async () =>
        {
            try
            {
                var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                var current = new StringBuilder();
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        current.Append(line["data:".Length..].Trim());
                        continue;
                    }
                    if (line.Length == 0 && current.Length > 0)
                    {
                        collector.Add(current.ToString());
                        current.Clear();
                    }
                }
            }
            catch
            {
                // Stream ended; the collector keeps what it observed.
            }
        });
        return response;
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AuthPayload> RegisterAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.com",
            Password = "SecurePass123!"
        });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        return new AuthPayload(
            data.GetProperty("token").GetString()!,
            data.GetProperty("user").GetProperty("id").GetString()!);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record TurnPayload(string MessageId, string DecisionKind, string? ProposalId);

    private sealed record AuthPayload(string Token, string UserId);

    private sealed class SseCollector
    {
        private readonly List<string> _events = [];
        private readonly object _lock = new();

        public void Add(string payload)
        {
            lock (_lock)
                _events.Add(payload);
        }

        public async Task WaitForAsync(Func<IReadOnlyList<string>, bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                List<string> snapshot;
                lock (_lock)
                    snapshot = [.. _events];
                if (predicate(snapshot))
                    return;
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            }
            List<string> final;
            lock (_lock)
                final = [.. _events];
            throw new InvalidOperationException(
                $"SSE events did not satisfy the predicate within the timeout. Observed: {string.Join(" | ", final)}");
        }
    }

}

/// <summary>
/// Standalone application factory for the API/SSE stage: real PostgreSQL,
/// both migration histories applied by the host, the outbox host service
/// enabled, every LLM-facing port replaced by deterministic stubs.
/// </summary>
public sealed class ApiSseFactory : WebApplicationFactory<TM.Web.NovelAgentWeb.Program>, IAsyncLifetime
{
    private const string TestJwtSecretKey = "test-secret-key-with-at-least-32-characters-for-security";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _applicationConnectionString = string.Empty;
    private string _workerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var bootstrap = new NpgsqlConnection(_postgres.GetConnectionString());
        await bootstrap.OpenAsync();
        await using (var roles = bootstrap.CreateCommand())
        {
            roles.CommandText = """
                CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
                CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
                GRANT CONNECT ON DATABASE postgres TO novelagent_app;
                GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
                GRANT USAGE ON SCHEMA public TO novelagent_app;
                GRANT USAGE ON SCHEMA public TO novelagent_worker;
                """;
            await roles.ExecuteNonQueryAsync();
        }
        _applicationConnectionString = _postgres.GetConnectionString();
        _workerConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
    }

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("ApiSseE2E");
        builder.UseSetting("ConnectionStrings:NovelAgentDb", _applicationConnectionString);
        builder.UseSetting("ConnectionStrings:NovelAgentMigrationDb", _applicationConnectionString);
        builder.UseSetting("ConnectionStrings:NovelAgentWorkerDb", _workerConnectionString);
        builder.UseSetting("JwtSettings:SecretKey", TestJwtSecretKey);
        builder.UseSetting("JwtSettings:Issuer", "NovelAgentWeb");
        builder.UseSetting("JwtSettings:Audience", "NovelAgentWeb");
        builder.UseSetting("JwtSettings:ExpiryDays", "7");

        builder.ConfigureServices(services =>
        {
            // Keep only the outbox host service: it delivers acceptance-gate
            // bridges and canon-merge requests. Every other background
            // service is either legacy, environment-bound, or would make
            // the flow nondeterministic.
            services.RemoveAll<IHostedService>();
            services.AddHostedService<ProductionOutboxHostedService>();

            services.RemoveAll<IVectorStore>();
            services.AddSingleton<IVectorStore, NoopVectorStore>();
            services.RemoveAll<IDistributedCacheService>();
            services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
            services.RemoveAll<IDistributedLockService>();
            services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
            services.RemoveAll<IAgentRuntimeEventFanout>();
            services.AddSingleton<IAgentRuntimeEventFanout, NoopAgentRuntimeEventFanout>();
            services.RemoveAll<IAgentRuntimeEventStreamConsumer>();
            services.AddSingleton<IAgentRuntimeEventStreamConsumer, NoopAgentRuntimeEventStreamConsumer>();
            services.RemoveAll<ILlmConnectionHealthService>();
            services.AddSingleton<ILlmConnectionHealthService, E2ELlmConnectionHealthService>();
            services.RemoveAll<IWritingModelCompletionService>();
            services.AddScoped<IWritingModelCompletionService, E2EWritingModelCompletionService>();
            services.RemoveAll<IReworkIntentModelClient>();
            services.AddScoped<IReworkIntentModelClient, E2EReworkIntentModelClient>();

            // Deterministic conversation runtime decisions: unbound turns
            // discuss, the first bound turn proposes the fixed contract.
            services.RemoveAll<IConversationTextCompletionPort>();
            services.AddSingleton<IConversationTextCompletionPort, ScriptedConversationCompletion>();
        });
    }
}

internal sealed class ScriptedConversationCompletion : IConversationTextCompletionPort
{
    private int _boundCalls;

    public Task<string> CompleteAsync(
        string userId, string system, string user, CancellationToken ct = default)
    {
        if (system.Contains("not bound to a project", StringComparison.Ordinal))
        {
            return Task.FromResult(
                """{"kind":"discussOnly","message":"我们可以先聚焦第一章的核心冲突，你想从哪个场景切入？"}""");
        }

        var call = Interlocked.Increment(ref _boundCalls);
        if (call > 1)
        {
            return Task.FromResult(
                """{"kind":"discussOnly","message":"提案已经生成，等待你在提案卡上确认。"}""");
        }

        return Task.FromResult("""
            {
              "kind": "proposeGoal",
              "message": "我提议生产第一章：海雾散开后的灯塔。",
              "contract": {
                "objective": "Write the first chapter",
                "mode": "InteractiveBatch",
                "chapterRange": { "start": 1, "end": 1 },
                "successCriteria": ["Deliver a complete dramatic turn"],
                "mustPreserve": ["Protagonist identity"],
                "mustHappen": ["Inciting incident"],
                "mustNotChange": ["Protected canon"],
                "acceptancePolicy": "human",
                "reworkPolicy": "directed",
                "totalCostLimit": 10,
                "canonBaselineVersion": "canon-v1",
                "knowledgeSnapshotVersion": "knowledge-v1",
                "qualityContractVersion": "quality-v1",
                "styleProfileVersion": "style-v1",
                "modelVersions": { "writing": "model-v1" },
                "protocolVersions": { "agent": "protocol-v1" }
              }
            }
            """);
    }
}
