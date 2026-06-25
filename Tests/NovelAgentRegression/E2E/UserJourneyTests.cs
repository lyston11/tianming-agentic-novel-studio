using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.Auth;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Services.Framework.AI.NovelAgent.Models;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// End-to-end tests that verify complete user journeys through the system.
/// Tests the full stack from authentication through project creation, chapter management, and data isolation.
/// </summary>
public class UserJourneyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserJourneyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AgentChat_ProjectlessMemoryQuestion_AnswersForegroundAndReturnsMemoryAudit()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "memoryQuestionUser",
            Email = "memory-question@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.AgentMemories.Add(new AgentMemory
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = auth.User.Id,
                ProjectId = null,
                SessionId = null,
                MemoryType = "author.display_name",
                MemoryKey = "display_name",
                Content = JsonSerializer.Serialize("lyston"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync("/api/agent/session", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);
        Assert.NotEmpty(session.SessionId);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "你知道我的名字吗？",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var reply = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(reply);
        Assert.Equal(session.SessionId, reply.SessionId);
        Assert.Contains("lyston", reply.Reply);
        Assert.Null(reply.RunId);
        Assert.Equal("idle", reply.Phase);
        Assert.Contains(reply.MemoryAudit?.Reads ?? Array.Empty<AgentMemoryReadAuditSummary>(),
            read => read.MemoryScope == "author" &&
                    read.MemoryKeys.Contains("author.display_name") &&
                    read.SessionId == session.SessionId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.False(await db.AgentRuntimeRuns.AnyAsync(r => r.SessionId == session.SessionId));
        }
    }

    [Fact]
    public async Task AgentChat_WhenLlmChoosesProduceChapter_QueuesBackgroundProductionRun()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentProductionQueueUser",
            Email = "agent-production-queue@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-production-queue-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 后台生产入队测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);
        Assert.Equal(projectId, session.ActiveProjectId);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PRODUCE_CHAPTER：请由模型自主选择章节生产工具来写第一章。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var reply = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(reply);
        Assert.Equal(session.SessionId, reply.SessionId);
        Assert.Equal("queued", reply.Phase);
        Assert.False(string.IsNullOrWhiteSpace(reply.RunId));
        Assert.Contains("后台执行", reply.Reply);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var run = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == reply.RunId);

            Assert.Equal(auth.User.Id, run.UserId);
            Assert.Equal(session.SessionId, run.SessionId);
            Assert.Equal(projectId, run.ProjectId);
            Assert.Equal("queued", run.Status);
            Assert.Equal("E2E_PRODUCE_CHAPTER：请由模型自主选择章节生产工具来写第一章。", run.UserMessage);
        }
    }

    [Fact]
    public async Task AgentChat_WhenLlmChoosesQueryProjectContent_RepliesWithChapterBodyWithoutBackgroundRun()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentContentQueryUser",
            Email = "agent-content-query@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            await db.SaveChangesAsync();
        }

        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Title = "Agent 章节回读测试",
            Genre = "废土",
            CoreHook = "旧邮路在废土中复苏。"
        });
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);

        var project = await projectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(project);

        var chapterResponse = await _client.PostAsJsonAsync("/api/chapters", new CreateChapterRequest
        {
            ProjectId = project.Id,
            Title = "第一章 旧邮路的蓝光",
            ChapterNumber = 1,
            Status = "published",
            Content = """
            沈砚在废城邮局的塌墙下醒来。

            银蓝邮徽在掌心发烫，指向被灰尘封死的投递窗口。

            章末，废弃分拣台传出第二次敲击声，像有人等待投递。
            """
        });
        Assert.Equal(HttpStatusCode.Created, chapterResponse.StatusCode);

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={project.Id}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_QUERY_CHAPTER_CONTENT：读取第一章标题、正文开头和结尾。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var reply = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(reply);
        Assert.Equal(session.SessionId, reply.SessionId);
        Assert.Equal(project.Id, reply.ActiveProjectId);
        Assert.Null(reply.RunId);
        Assert.NotEqual("queued", reply.Phase);
        Assert.Contains("第一章 旧邮路的蓝光", reply.Reply);
        Assert.Contains("沈砚在废城邮局", reply.Reply);
        Assert.Contains("分拣台传出第二次敲击声", reply.Reply);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.False(await db.AgentRuntimeRuns.AnyAsync(r => r.SessionId == session.SessionId));
            Assert.Contains(await db.AgentToolExecutions
                    .Where(execution => execution.SessionId == session.SessionId)
                    .ToListAsync(),
                execution => execution.ToolName == "QueryProjectContent" && execution.Status == "succeeded");
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenQueuedRunExecutes_PersistsRuntimeCompletionAndEvents()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentWorkerExecutionUser",
            Email = "agent-worker-execution@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-worker-execution-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 后台 Worker 执行测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PRODUCE_CHAPTER：请由模型自主选择章节生产工具来写第一章。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var run = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == queued.RunId);
            var events = await db.AgentRuntimeEvents
                .Where(evt => evt.RuntimeRunId == queued.RunId)
                .OrderBy(evt => evt.CreatedAt)
                .ToListAsync();
            var toolExecution = await db.AgentToolExecutions
                .SingleAsync(execution =>
                    execution.SessionId == session.SessionId &&
                    execution.ToolName == "ProduceChapter");

            Assert.Equal("completed", run.Status);
            Assert.Equal("completed", run.CurrentPhase);
            Assert.NotEqual("{}", run.ResultJson);
            Assert.Contains("ProduceChapter", run.ResultJson);
            Assert.Equal(projectId, toolExecution.ProjectId);
            Assert.Equal("failed", toolExecution.Status);
            Assert.Contains("已规划章节 Run", toolExecution.ResultMessage);
            Assert.Contains("chapter_plan_run", toolExecution.MissingPrerequisite);
            Assert.Contains(events, evt => evt.Type == AgentSseEventType.RunUpdate && evt.Message.Contains("开始后台执行"));
            Assert.Contains(events, evt => evt.Type == AgentSseEventType.AgentReply);
            Assert.Contains(events, evt => evt.Type == AgentSseEventType.RunUpdate && evt.Message.Contains("后台执行已完成"));
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenLlmPlansThenProduces_UsesPlannedChapterRun()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentPlanThenProduceUser",
            Email = "agent-plan-then-produce@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-plan-then-produce-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 规划后生产测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_THEN_PRODUCE：请先规划第一章，再基于规划进入章节生产。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var toolExecutions = await db.AgentToolExecutions
                .Where(execution => execution.SessionId == session.SessionId)
                .OrderBy(execution => execution.StartedAt)
                .ToListAsync();

            Assert.Contains(toolExecutions, execution => execution.ToolName == "PlanChapter" && execution.Status == "succeeded");
            var selection = Assert.Single(toolExecutions.Where(execution => execution.ToolName == "SelectChapterCandidate"));
            Assert.Equal("succeeded", selection.Status);

            var produce = Assert.Single(toolExecutions.Where(execution => execution.ToolName == "ProduceChapter"));
            using var args = JsonDocument.Parse(produce.ArgumentsJson);
            Assert.True(args.RootElement.TryGetProperty("runId", out var runIdArgument));
            Assert.Equal(selection.RunId, runIdArgument.GetString());
            Assert.Equal("succeeded", produce.Status);
            Assert.Contains("章节草稿已生成并通过硬门禁", produce.ResultMessage);
            Assert.NotEqual("chapter_plan_run", produce.MissingPrerequisite);
            Assert.DoesNotContain("已规划章节 Run", produce.ResultMessage);
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenLlmPlansProducesAndCommits_PersistsLibraryAndProductionTruth()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentPlanProduceCommitUser",
            Email = "agent-plan-produce-commit@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-plan-produce-commit-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 完整提交闭环测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_THEN_COMMIT：请先规划第一章，再生产并提交到书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var run = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == queued.RunId);
            var toolExecutions = await db.AgentToolExecutions
                .Where(execution => execution.SessionId == session.SessionId)
                .OrderBy(execution => execution.StartedAt)
                .ToListAsync();

            Assert.Equal("completed", run.Status);
            Assert.Contains(toolExecutions, execution => execution.ToolName == "PlanChapter" && execution.Status == "succeeded");
            Assert.Contains(toolExecutions, execution => execution.ToolName == "SelectChapterCandidate" && execution.Status == "succeeded");

            var produce = Assert.Single(toolExecutions.Where(execution => execution.ToolName == "ProduceChapter"));
            Assert.Equal("succeeded", produce.Status);
            Assert.False(string.IsNullOrWhiteSpace(produce.RunId));
            Assert.Contains("章节生产闭环已完成", produce.ResultMessage);
            Assert.Contains("提交书城", produce.ResultMessage);

            var chapter = await db.Chapters.SingleAsync(chapter => chapter.ProjectId == projectId);
            Assert.Equal("committed", chapter.Status);
            Assert.False(string.IsNullOrWhiteSpace(chapter.CurrentDocumentId));
            Assert.True(chapter.WordCount > 0);

            var version = await db.ChapterVersions.SingleAsync(version => version.ProjectId == projectId && version.ChapterId == chapter.Id);
            Assert.Equal(produce.RunId, version.RuntimeRunId);
            Assert.Equal("committed", version.Status);
            Assert.False(string.IsNullOrWhiteSpace(version.GateReportJson));
            Assert.False(string.IsNullOrWhiteSpace(version.AgentReviewJson));

            var snapshots = await db.ProjectFactSnapshots
                .Where(snapshot => snapshot.ProjectId == projectId && snapshot.ChapterId == chapter.Id)
                .OrderBy(snapshot => snapshot.VersionNumber)
                .ToListAsync();
            Assert.Contains(snapshots, snapshot => snapshot.ChapterVersionId == version.Id && snapshot.Source == "chapter_commit");
            Assert.Contains(snapshots, snapshot =>
                snapshot.ChapterVersionId == version.Id &&
                snapshot.Source == "chapter_fact_extraction" &&
                snapshot.SnapshotJson.Contains("沈砚") &&
                snapshot.SnapshotJson.Contains("银蓝邮徽"));

            var review = await db.AgentReviews.SingleAsync(review => review.ProjectId == projectId && review.ChapterId == chapter.Id);
            Assert.Equal(produce.RunId, review.RuntimeRunId);
            Assert.NotEqual("Fail", review.OverallResult);
            Assert.False(review.RequiresRewrite);

            var gate = await db.GenerationGateReports.SingleAsync(gate => gate.ProjectId == projectId && gate.ChapterId == chapter.Id);
            Assert.True(gate.ProtocolPassed);
            Assert.True(gate.ChangesDetected);

            Assert.Contains(await db.ProductionEvents.Where(evt => evt.ProjectId == projectId).ToListAsync(),
                evt => evt.EventType == "chapter_committed" && evt.ArtifactId == version.Id);
            Assert.Contains(await db.OutboxEvents.Where(evt => evt.ProjectId == projectId).ToListAsync(),
                evt => evt.EventType == "finalize_chapter_commit_metadata" && evt.Status == "completed");
            Assert.Contains(await db.OutboxEvents.Where(evt => evt.ProjectId == projectId).ToListAsync(),
                evt => evt.EventType == "extract_chapter_continuity_facts" && evt.Status == "completed");
        }

        var workflowResponse = await _client.GetAsync($"/api/workflow/project/{projectId}");
        Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);

        var workflow = await workflowResponse.Content.ReadEnvelopeDataAsync<ProjectWorkflowDocument>();
        Assert.Equal(1, workflow.Library.GeneratedChapterCount);
        Assert.Contains(workflow.ProductionStages.SelectMany(stage => stage.ProductionEvents),
            evt => evt.EventType == "chapter_committed" && evt.Evidence?.AgentReview?.OverallResult != "Fail");
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenWritingSecondChapter_UsesFirstChapterFactSnapshotContinuity()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentSecondChapterContinuityUser",
            Email = "agent-second-chapter-continuity@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-second-chapter-continuity-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 多章连续性测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var firstChatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_THEN_COMMIT：请先规划第一章，再生产并提交到书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, firstChatResponse.StatusCode);

        var firstQueued = await firstChatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(firstQueued);
        Assert.Equal("queued", firstQueued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(firstQueued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(firstQueued.RunId!, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        var secondChatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_SECOND_THEN_COMMIT：请规划第二章，必须承接第一章结尾和事实快照，再提交到书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, secondChatResponse.StatusCode);

        var secondQueued = await secondChatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(secondQueued);
        Assert.Equal("queued", secondQueued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(secondQueued.RunId));
        Assert.NotEqual(firstQueued.RunId, secondQueued.RunId);

        await worker.ExecuteRunWithLeaseAsync(secondQueued.RunId!, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapters = await db.Chapters
                .Where(chapter => chapter.ProjectId == projectId)
                .OrderBy(chapter => chapter.ChapterNumber)
                .ToListAsync();

            Assert.Equal(2, chapters.Count);
            Assert.All(chapters, chapter => Assert.Equal("committed", chapter.Status));

            var secondChapter = chapters.Single(chapter => chapter.ChapterNumber == 2);
            var secondBodyChunks = await db.ContentChunks
                .Where(chunk => chunk.DocumentId == secondChapter.CurrentDocumentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync();
            var secondBody = string.Join(string.Empty, secondBodyChunks);

            Assert.Contains("沈砚", secondBody);
            Assert.Contains("分拣台第二次敲击声", secondBody);
            Assert.Contains("银蓝邮徽", secondBody);
            Assert.Contains("不能主动攻击", secondBody);

            var secondPackage = await db.TianmingPackages.SingleAsync(package =>
                package.ProjectId == projectId &&
                package.PackageKind == "chapter_context_package" &&
                package.ChapterId == "chapter-002");
            var secondPackageFacts = string.Join("\n", ExtractJsonStringValues(secondPackage.KnowledgeSnapshotJson ?? "{}"));
            Assert.Contains("上一章主角", secondPackageFacts);
            Assert.Contains("沈砚", secondPackageFacts);
            Assert.Contains("下一章必须承接", secondPackageFacts);
            Assert.Contains("分拣台第二次敲击声", secondPackageFacts);
            Assert.Contains("银蓝邮徽不能主动攻击", secondPackageFacts);

            var firstSnapshot = await db.ProjectFactSnapshots
                .Where(snapshot => snapshot.ProjectId == projectId &&
                                   snapshot.Source == "chapter_fact_extraction")
                .OrderBy(snapshot => snapshot.CreatedAt)
                .FirstAsync();
            var firstSnapshotFacts = string.Join("\n", ExtractJsonStringValues(firstSnapshot.SnapshotJson ?? "{}"));
            Assert.Contains("沈砚", firstSnapshotFacts);
            Assert.Contains("分拣台传出第二次敲击声", firstSnapshotFacts);
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenAgentReviewFails_RewritesAndCommitsAfterPassingReview()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentReviewRewriteUser",
            Email = "agent-review-rewrite@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        E2EWritingModelScenarios.MarkAgentReviewRewriteUser(auth.User.Id);

        const string projectId = "agent-review-rewrite-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "AgentReview 自动改写测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_REVIEW_REWRITE_THEN_COMMIT：请规划第一章，首次总编验收失败时自动按反馈重写并提交书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeRun = await db.AgentRuntimeRuns.SingleAsync(run => run.Id == queued.RunId);
            Assert.Equal("completed", runtimeRun.Status);

            var produceExecutions = await db.AgentToolExecutions
                .Where(execution =>
                    execution.SessionId == session.SessionId &&
                    execution.ToolName == "ProduceChapter")
                .OrderBy(execution => execution.StartedAt)
                .ToListAsync();
            var produce = produceExecutions.LastOrDefault();
            Assert.NotNull(produce);
            Assert.True(
                produce.Status == "succeeded",
                $"""
                ProduceChapter count: {produceExecutions.Count}
                ProduceChapter executions: {JsonSerializer.Serialize(produceExecutions.Select(execution => new
                {
                    execution.Status,
                    execution.Phase,
                    execution.ResultPhase,
                    execution.ArgumentsJson,
                    execution.ErrorMessage,
                    execution.FailureJson,
                    execution.ResultMessage
                }))}
                ProduceChapter status: {produce.Status}
                ProduceChapter arguments: {produce.ArgumentsJson}
                ProduceChapter error: {produce.ErrorMessage}
                ProduceChapter failure: {produce.FailureJson}
                ProduceChapter result: {produce.ResultMessage}
                """);
            Assert.False(string.IsNullOrWhiteSpace(produce.RunId));
            Assert.Contains("章节生产闭环已完成", produce.ResultMessage);

            var chapter = await db.Chapters.SingleAsync(chapter => chapter.ProjectId == projectId);
            Assert.Equal("committed", chapter.Status);
            Assert.False(string.IsNullOrWhiteSpace(chapter.CurrentDocumentId));

            var bodyChunks = await db.ContentChunks
                .Where(chunk => chunk.DocumentId == chapter.CurrentDocumentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync();
            var body = string.Join(string.Empty, bodyChunks);
            Assert.Contains("按评审补足战斗反馈", body);
            Assert.Contains("沈砚", body);

            var reviews = await db.AgentReviews
                .Where(review => review.ProjectId == projectId && review.ChapterId == chapter.Id)
                .OrderBy(review => review.CreatedAt)
                .ToListAsync();
            Assert.True(reviews.Count >= 2);
            var reviewDebug = string.Join(" || ", reviews.Select(review =>
            {
                return $"{review.OverallResult}/{review.RequiresRewrite}/{review.Summary}/editorial={ExtractReviewCheckDebug(review.ReviewJson, "agent_editorial_semantic_alignment")}";
            }));
            Assert.True(
                reviews.Any(review => review.RequiresRewrite || review.OverallResult == "Fail"),
                reviewDebug + " || " + E2EWritingModelScenarios.DumpRecentCalls());
            Assert.False(reviews.Last().RequiresRewrite);
            Assert.NotEqual("Fail", reviews.Last().OverallResult);
            Assert.StartsWith("1:LLM 总编验收通过", ExtractReviewCheckDebug(
                reviews.Last().ReviewJson,
                "agent_editorial_semantic_alignment"));

            var feedbackEvent = await db.ProductionEvents.SingleAsync(evt =>
                evt.ProjectId == projectId &&
                evt.RuntimeRunId == produce.RunId &&
                evt.EventType == "chapter_agent_review_feedback_applied");
            Assert.Equal("completed", feedbackEvent.Status);

            var package = await db.TianmingPackages.SingleAsync(package =>
                package.ProjectId == projectId &&
                package.RuntimeRunId == produce.RunId &&
                package.PackageKind == "chapter_context_package");
            var packageFacts = string.Join(
                "\n",
                ExtractJsonStringValues(package.InputJson)
                    .Concat(ExtractJsonStringValues(package.KnowledgeSnapshotJson ?? "{}")));
            Assert.Contains("AgentReview修订要求", packageFacts);

            var version = await db.ChapterVersions.SingleAsync(version =>
                version.ProjectId == projectId &&
                version.ChapterId == chapter.Id);
            Assert.Equal("committed", version.Status);
            Assert.Contains("\"requiresRewrite\":false", version.AgentReviewJson);
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenRevisionPlanReadyForRebuild_RebuildsAndCommitsWithRevisionEvidence()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentRevisionRebuildUser",
            Email = "agent-revision-rebuild@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-revision-rebuild-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 修订计划重建测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var chapterResponse = await _client.PostAsJsonAsync("/api/chapters", new CreateChapterRequest
        {
            ProjectId = projectId,
            Title = "第一章 旧版逃生",
            ChapterNumber = 1,
            Status = "published",
            Content = """
            沈砚在废城邮局醒来，只沿着银蓝邮徽照出的窄巷逃走。

            这一版没有怪物围攻，也没有明确的生存反击。
            """
        });
        Assert.Equal(HttpStatusCode.Created, chapterResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapter = await db.Chapters.SingleAsync(chapter =>
                chapter.ProjectId == projectId &&
                chapter.ChapterNumber == 1);

            db.TianmingPackages.Add(new TianmingPackage
            {
                Id = "pkg-e2e-revision-old",
                UserId = auth.User.Id,
                ProjectId = projectId,
                ChapterId = "chapter-001",
                RuntimeRunId = "run-e2e-revision-old",
                PackageKind = "chapter_context_package",
                Status = "stale",
                InputJson = "{}",
                KnowledgeSnapshotJson = "{}",
                FactSnapshotJson = "{}",
                PromptVersion = "chapter-context-v1",
                KernelVersion = "agentic-tianming-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-9)
            });
            db.RevisionPlans.Add(new RevisionPlan
            {
                Id = "revision-plan-e2e-rebuild",
                UserId = auth.User.Id,
                ProjectId = projectId,
                SessionId = "session-e2e-revision",
                RuntimeRunId = "run-e2e-revision-authoring",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-001",
                Status = "ready_for_rebuild",
                RequirementsJson = """["RevisionPlanRebuildE2E：把第一章改成怪物围攻后的明确生存反击","保留沈砚和银蓝邮徽","邮徽不能主动攻击"]""",
                ContinuityRequirementsJson = """["章末仍要留下分拣台第二次敲击声"]""",
                ImpactAnalysisJson = """{"reason":"用户认为旧版太平，需要重写第一章并让后续按新版承接"}""",
                AffectedChapterIdsJson = """["chapter-001"]""",
                InvalidatedPackageIdsJson = """["pkg-e2e-revision-old"]""",
                RiskLevel = "high",
                Recommendation = "重建第一章生产包并生成 v2 正文。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-8),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-7)
            });
            await db.SaveChangesAsync();

            Assert.Equal("published", chapter.Status);
        }

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_REVISION_REBUILD_THEN_COMMIT：请按已准备好的修订计划重建第一章，并提交书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeRun = await db.AgentRuntimeRuns.SingleAsync(run => run.Id == queued.RunId);
            Assert.Equal("completed", runtimeRun.Status);

            var produceExecutions = await db.AgentToolExecutions
                .Where(execution =>
                    execution.SessionId == session.SessionId &&
                    execution.ToolName == "ProduceChapter")
                .OrderBy(execution => execution.StartedAt)
                .ToListAsync();
            var produce = produceExecutions.LastOrDefault();
            Assert.NotNull(produce);
            Assert.True(
                produce.Status == "succeeded",
                $"""
                ProduceChapter count: {produceExecutions.Count}
                ProduceChapter executions: {JsonSerializer.Serialize(produceExecutions.Select(execution => new
                {
                    execution.Status,
                    execution.Phase,
                    execution.ResultPhase,
                    execution.ArgumentsJson,
                    execution.ErrorMessage,
                    execution.FailureJson,
                    execution.ResultMessage
                }))}
                ProduceChapter status: {produce.Status}
                ProduceChapter arguments: {produce.ArgumentsJson}
                ProduceChapter error: {produce.ErrorMessage}
                ProduceChapter failure: {produce.FailureJson}
                ProduceChapter result: {produce.ResultMessage}
                """);
            using (var args = JsonDocument.Parse(produce.ArgumentsJson))
            {
                Assert.Equal("revision-plan-e2e-rebuild", args.RootElement.GetProperty("revisionPlanId").GetString());
            }

            var chapter = await db.Chapters.SingleAsync(chapter =>
                chapter.ProjectId == projectId &&
                chapter.ChapterNumber == 1);
            var bodyChunks = await db.ContentChunks
                .Where(chunk => chunk.DocumentId == chapter.CurrentDocumentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync();
            var body = string.Join(string.Empty, bodyChunks);
            Assert.Contains("RevisionPlanRebuildE2E 按修订计划改写", body);
            Assert.Contains("怪物", body);
            Assert.Contains("沈砚", body);
            Assert.Contains("银蓝邮徽", body);

            var plan = await db.RevisionPlans.SingleAsync(plan => plan.Id == "revision-plan-e2e-rebuild");
            Assert.Equal("executed", plan.Status);

            var newPackage = await db.TianmingPackages.SingleAsync(package =>
                package.ProjectId == projectId &&
                package.RuntimeRunId == produce.RunId &&
                package.PackageKind == "chapter_context_package");
            Assert.Contains("revision-plan-e2e-rebuild", newPackage.InputJson);
            Assert.Contains("revision-plan-e2e-rebuild", newPackage.KnowledgeSnapshotJson);
            Assert.Contains("pkg-e2e-revision-old", newPackage.KnowledgeSnapshotJson);

            var version = await db.ChapterVersions.SingleAsync(version =>
                version.ProjectId == projectId &&
                version.RuntimeRunId == produce.RunId);
            Assert.Equal(newPackage.Id, version.PackageId);
            Assert.Equal("committed", version.Status);

            Assert.Contains(await db.ProductionEvents.Where(evt => evt.ProjectId == projectId).ToListAsync(),
                evt => evt.EventType == "revision_plan_executed" &&
                       evt.ArtifactId == "revision-plan-e2e-rebuild" &&
                       evt.Status == "executed");

            using var contentScope = _factory.Services.CreateScope();
            var contentQuery = await contentScope.ServiceProvider
                .GetRequiredService<IProjectContentQueryService>()
                .QueryAsync(new ProjectContentQueryRequest(
                    UserId: auth.User.Id,
                    ProjectId: projectId,
                    StoryBible: new StoryBibleDocument(),
                    ChapterId: "",
                    ChapterNumber: 1,
                    VolumeNumber: 0,
                    IncludeBody: true,
                    IncludeFacts: true),
                    CancellationToken.None);
            Assert.NotNull(contentQuery);
            var item = Assert.Single(contentQuery!.Items);
            Assert.Contains(item.SourceRevisionPlans, source => source.RevisionPlanId == "revision-plan-e2e-rebuild");
            Assert.Contains("pkg-e2e-revision-old", item.RebuiltFromPackageIds);
        }
    }

    [Fact]
    public async Task AgentRuntimeWorker_WhenProjectKnowledgeIsBound_UsesKnowledgeInCommittedChapterAndWorkflowEvidence()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "agentKnowledgeBoundUser",
            Email = "agent-knowledge-bound@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "agent-knowledge-bound-project";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ILlmApiKeyProtector>();

            var settings = await db.UserSettings.SingleAsync(s => s.UserId == auth.User.Id);
            settings.LlmProvider = "openai";
            settings.LlmBaseUrl = "http://fake.local/v1";
            settings.LlmModel = "fake-model";
            settings.LlmApiKeyEncrypted = protector.Protect("fake-key");

            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "Agent 知识库绑定生产测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var knowledgeResponse = await _client.PostAsJsonAsync("/api/knowledge", new CreateKnowledgeRequest
        {
            ProjectId = projectId,
            EntryType = "HardFact",
            Title = "盐鸦驿站",
            Content = "盐鸦驿站是旧邮路第一处安全点；逆风邮旗只会在蓝光邮路被正确承接时升起。",
            Tags = new List<string> { "旧邮路", "安全点", "硬事实" },
            Weight = 90
        });
        Assert.Equal(HttpStatusCode.OK, knowledgeResponse.StatusCode);

        var knowledge = await knowledgeResponse.Content.ReadEnvelopeDataAsync<KnowledgeResponse>();
        Assert.NotNull(knowledge);

        var bindResponse = await _client.PostAsJsonAsync($"/api/knowledge/{knowledge.Id}/usage", new IncrementKnowledgeUsageRequest
        {
            ProjectId = projectId
        });
        Assert.Equal(HttpStatusCode.NoContent, bindResponse.StatusCode);

        var sessionResponse = await _client.PostAsync($"/api/agent/session?projectId={projectId}", content: null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadEnvelopeDataAsync<AgentSessionResponse>();
        Assert.NotNull(session);

        var chatResponse = await _client.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest(
            "E2E_PLAN_THEN_COMMIT：请结合项目知识库写第一章，并提交到书城。",
            session.SessionId));
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var queued = await chatResponse.Content.ReadEnvelopeDataAsync<AgentChatResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Phase);
        Assert.False(string.IsNullOrWhiteSpace(queued.RunId));

        var worker = new AgentRuntimeWorker(
            _factory.Services.GetRequiredService<IAgentRuntimeQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeWorker>.Instance,
            _factory.Services.GetRequiredService<IAgentRuntimeRunLeaseService>());

        await worker.ExecuteRunWithLeaseAsync(queued.RunId!, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapters = await db.Chapters
                .Where(chapter => chapter.ProjectId == projectId)
                .ToListAsync();
            if (chapters.Count == 0)
            {
                var runtimeRun = await db.AgentRuntimeRuns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(run => run.Id == queued.RunId);
                var toolExecutions = await db.AgentToolExecutions
                    .AsNoTracking()
                    .Where(execution => execution.ProjectId == projectId)
                    .OrderBy(execution => execution.StartedAt)
                    .Select(execution => new
                    {
                        execution.ToolName,
                        execution.Status,
                        execution.ArgumentsJson,
                        execution.ErrorMessage,
                        execution.ResultMessage,
                        execution.FailureJson
                    })
                    .ToListAsync();
                var productionEvents = await db.ProductionEvents
                    .AsNoTracking()
                    .Where(evt => evt.ProjectId == projectId)
                    .OrderBy(evt => evt.CreatedAt)
                    .Select(evt => new
                    {
                        evt.EventType,
                        evt.Stage,
                        evt.Status,
                        evt.ArtifactType,
                        evt.ArtifactId,
                        evt.ChapterId,
                        evt.Message
                    })
                    .ToListAsync();
                var packages = await db.TianmingPackages
                    .AsNoTracking()
                    .Where(package => package.ProjectId == projectId)
                    .OrderBy(package => package.CreatedAt)
                    .Select(package => new
                    {
                        package.Id,
                        package.PackageKind,
                        package.ChapterId,
                        package.RuntimeRunId,
                        package.Status
                    })
                    .ToListAsync();
                var outbox = await db.OutboxEvents
                    .AsNoTracking()
                    .Where(evt => evt.ProjectId == projectId)
                    .OrderBy(evt => evt.CreatedAt)
                    .Select(evt => new
                    {
                        evt.EventType,
                        evt.AggregateType,
                        evt.AggregateId,
                        evt.Status,
                        evt.LastError
                    })
                    .ToListAsync();

                var resultJson = runtimeRun?.ResultJson ?? string.Empty;
                var exceptionIndex = resultJson.IndexOf("DbUpdateException", StringComparison.OrdinalIgnoreCase);
                var resultExcerpt = exceptionIndex < 0
                    ? resultJson[..Math.Min(resultJson.Length, 2000)]
                    : resultJson[Math.Max(0, exceptionIndex - 1000)..Math.Min(resultJson.Length, exceptionIndex + 3000)];

                Assert.Fail($"""
                Expected knowledge-bound production to commit one chapter, but no chapters were persisted.
                RuntimeRun: {JsonSerializer.Serialize(new
                {
                    runtimeRun?.Id,
                    runtimeRun?.Status,
                    runtimeRun?.CurrentPhase,
                    runtimeRun?.CurrentStep,
                    runtimeRun?.ActiveTool,
                    runtimeRun?.LastMessage,
                    runtimeRun?.ErrorMessage,
                    runtimeRun?.FailureJson,
                    ResultJsonLength = runtimeRun?.ResultJson?.Length ?? 0
                })}
                ResultExcerpt: {resultExcerpt}
                ToolExecutions: {JsonSerializer.Serialize(toolExecutions)}
                ProductionEvents: {JsonSerializer.Serialize(productionEvents)}
                TianmingPackages: {JsonSerializer.Serialize(packages)}
                Outbox: {JsonSerializer.Serialize(outbox)}
                """);
            }

            var chapter = Assert.Single(chapters);
            var content = await db.ContentChunks
                .Where(chunk => chunk.DocumentId == chapter.CurrentDocumentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync();
            var chapterText = string.Join(string.Empty, content);

            Assert.Contains("盐鸦驿站", chapterText);
            Assert.Contains("逆风邮旗", chapterText);

            var package = await db.TianmingPackages.SingleAsync(package =>
                package.ProjectId == projectId &&
                package.PackageKind == "chapter_context_package");
            Assert.False(string.IsNullOrWhiteSpace(package.KnowledgeSnapshotJson));
            using var knowledgeSnapshot = JsonDocument.Parse(package.KnowledgeSnapshotJson);
            var knowledgeBindings = knowledgeSnapshot.RootElement.GetProperty("knowledgeBindings").EnumerateArray().ToList();
            var binding = Assert.Single(knowledgeBindings, item =>
                item.GetProperty("knowledgeId").GetString() == knowledge.Id);
            Assert.Equal("盐鸦驿站", binding.GetProperty("title").GetString());
            Assert.Equal("DefaultEveryChapter", binding.GetProperty("packagePolicy").GetString());
            Assert.Equal("HardConstraint", binding.GetProperty("constraintLevel").GetString());

            var bindingSummary = knowledgeSnapshot.RootElement.GetProperty("knowledgeBindingSummary");
            Assert.Equal(1, bindingSummary.GetProperty("bindingCount").GetInt32());
            Assert.Equal(1, bindingSummary.GetProperty("hardConstraintCount").GetInt32());

            var usage = await db.ProjectKnowledgeUsages.SingleAsync(usage =>
                usage.UserId == auth.User.Id &&
                usage.ProjectId == projectId &&
                usage.KnowledgeId == knowledge.Id);
            Assert.Equal("referenced", usage.Status);
            Assert.Contains(chapter.Id, usage.UsedByChaptersJson ?? string.Empty);
        }

        var workflowResponse = await _client.GetAsync($"/api/workflow/project/{projectId}");
        Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);

        var workflow = await workflowResponse.Content.ReadEnvelopeDataAsync<ProjectWorkflowDocument>();
        var knowledgeEvidence = workflow.ProductionStages
            .SelectMany(stage => stage.ProductionEvents)
            .SelectMany(evt => evt.Evidence?.KnowledgeBindings ?? Array.Empty<WorkflowKnowledgeBindingEvidence>())
            .ToList();
        Assert.Contains(knowledgeEvidence, evidence =>
            evidence.KnowledgeId == knowledge.Id &&
            evidence.Title == "盐鸦驿站" &&
            evidence.ProjectUsageStatus == "used");
    }

    [Fact]
    public async Task Workspace_ProjectWithCanonicalVolumeAndCommittedChapters_ReturnsLibraryCounts()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "workspaceLibraryUser",
            Email = "workspace-library@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.NovelProjects.Add(new NovelProject
            {
                Id = "workspace-project-1",
                UserId = auth.User.Id,
                Title = "书城 API 正式卷测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new Volume
            {
                Id = "workspace-volume-1",
                ProjectId = "workspace-project-1",
                Title = "第一卷：黑雨旧邮路",
                VolumeNumber = 1
            });
            db.Chapters.AddRange(
                new Chapter
                {
                    Id = "workspace-chapter-1",
                    ProjectId = "workspace-project-1",
                    VolumeId = "workspace-volume-1",
                    Title = "第一章：邮徽醒来",
                    ChapterNumber = 1,
                    Status = "committed",
                    WordCount = 3200
                },
                new Chapter
                {
                    Id = "workspace-chapter-2",
                    ProjectId = "workspace-project-1",
                    VolumeId = "workspace-volume-1",
                    Title = "第二章：旧站台追击",
                    ChapterNumber = 2,
                    Status = "committed",
                    WordCount = 3500
                });
            await db.SaveChangesAsync();
        }

        var workspaceResponse = await _client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, workspaceResponse.StatusCode);

        var workspace = await workspaceResponse.Content.ReadEnvelopeDataAsync<WorkspaceResponse>();
        var book = Assert.Single(workspace.Projects, project => project.ProjectId == "workspace-project-1");
        Assert.Equal(1, book.VolumeCount);
        Assert.Equal(2, book.GeneratedChapterCount);
        Assert.Equal(0, book.PlannedChapterCount);
    }

    [Fact]
    public async Task Workflow_ProjectWithCanonicalVolumeAndCommittedChapters_ReturnsCanonicalVolume()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "workflowLibraryUser",
            Email = "workflow-library@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "workflow-project-1";
        const string volumeId = "workflow-volume-1";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "工作流 API 正式卷测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new Volume
            {
                Id = volumeId,
                ProjectId = projectId,
                Title = "第一卷：黑雨旧邮路",
                VolumeNumber = 1
            });
            db.Chapters.AddRange(
                new Chapter
                {
                    Id = "workflow-chapter-1",
                    ProjectId = projectId,
                    VolumeId = volumeId,
                    Title = "第一章：邮徽醒来",
                    ChapterNumber = 1,
                    Status = "committed",
                    WordCount = 3200
                },
                new Chapter
                {
                    Id = "workflow-chapter-2",
                    ProjectId = projectId,
                    VolumeId = volumeId,
                    Title = "第二章：旧站台追击",
                    ChapterNumber = 2,
                    Status = "committed",
                    WordCount = 3500
                });
            await db.SaveChangesAsync();
        }

        var workflowResponse = await _client.GetAsync($"/api/workflow/project/{projectId}");
        Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);

        var workflow = await workflowResponse.Content.ReadEnvelopeDataAsync<ProjectWorkflowDocument>();
        Assert.Equal(1, workflow.Project?.VolumeCount);
        Assert.Equal(2, workflow.Library.GeneratedChapterCount);
        var volume = Assert.Single(workflow.Library.Volumes);
        Assert.Equal(volumeId, volume.VolumeId);
        Assert.Equal("第一卷：黑雨旧邮路", volume.Title);
        Assert.Equal(2, volume.Chapters.Count);
        Assert.All(volume.Chapters, chapter => Assert.Equal(volumeId, chapter.VolumeId));
        Assert.DoesNotContain(workflow.Library.Volumes, item => item.Title == "数据库章节");
    }

    [Fact]
    public async Task Workflow_ProjectWithProductionTruthEvidence_ReturnsGateReviewFactAndMemoryEvidence()
    {
        var registerRequest = new RegisterRequest
        {
            Username = "workflowEvidenceUser",
            Email = "workflow-evidence@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        const string projectId = "workflow-evidence-project";
        const string volumeId = "workflow-evidence-volume";
        const string chapterId = "workflow-evidence-chapter-001";
        const string runId = "workflow-evidence-run-001";
        const string packageId = "workflow-evidence-package-001";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.NovelProjects.Add(new NovelProject
            {
                Id = projectId,
                UserId = auth.User.Id,
                Title = "工作流生产证据测试",
                Genre = "废土",
                Status = "writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new Volume
            {
                Id = volumeId,
                ProjectId = projectId,
                Title = "第一卷：黑雨旧邮路",
                VolumeNumber = 1
            });
            db.Chapters.Add(new Chapter
            {
                Id = chapterId,
                ProjectId = projectId,
                VolumeId = volumeId,
                Title = "第一章：邮徽醒来",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 3300
            });
            db.TianmingPackages.Add(new TianmingPackage
            {
                Id = packageId,
                UserId = auth.User.Id,
                ProjectId = projectId,
                ChapterId = chapterId,
                RuntimeRunId = runId,
                PackageKind = "chapter_generation",
                Status = "completed",
                InputJson = "{}",
                KnowledgeSnapshotJson = """
                {
                  "hardContinuityFacts": ["沈砚已经获得银蓝邮徽。"],
                  "ragQueries": ["旧邮路规则"],
                  "acceptedCreativeIntents": ["第一章必须是打怪升级开局。"]
                }
                """,
                PromptVersion = "chapter-prompt-v1",
                KernelVersion = "agentic-tianming-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "workflow-evidence-fact-001",
                UserId = auth.User.Id,
                ProjectId = projectId,
                ChapterId = chapterId,
                VersionNumber = 1,
                Source = "chapter_commit",
                SnapshotJson = """
                {
                  "protagonistName": "沈砚",
                  "protagonistIdentity": "旧邮路临时投递员",
                  "protagonistStatus": "刚从黑雨怪潮中脱身",
                  "currentLocation": "废弃邮站",
                  "systemState": "银蓝邮徽已绑定但不能主动攻击",
                  "equipmentState": "银蓝邮徽、半截撬棍",
                  "keyEvents": ["沈砚用邮徽识别旧邮路逃生线"],
                  "endingState": "邮站门后响起第二次投递铃",
                  "nextChapterMustCarry": ["第二章必须承接投递铃响起"]
                }
                """,
                CreatedAt = DateTime.UtcNow.AddMinutes(-3)
            });
            db.GenerationGateReports.Add(new GenerationGateReportRecord
            {
                Id = "workflow-evidence-gate-001",
                UserId = auth.User.Id,
                ProjectId = projectId,
                RuntimeRunId = runId,
                ChapterId = chapterId,
                PackageId = packageId,
                ArtifactId = "gate-artifact-001",
                Status = "validated",
                ReportJson = """
                {
                  "status": "validated",
                  "protocolPassed": true,
                  "changesDetected": true,
                  "factSnapshotPassed": true,
                  "blueprintPassed": true,
                  "ragPassed": true,
                  "issues": [],
                  "repairHints": []
                }
                """,
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true,
                IssueCount = 0,
                RepairHintCount = 0,
                ValidatedAt = DateTime.UtcNow.AddMinutes(-2),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            db.AgentReviews.Add(new AgentReviewRecord
            {
                Id = "workflow-evidence-review-001",
                UserId = auth.User.Id,
                ProjectId = projectId,
                RuntimeRunId = runId,
                ChapterId = chapterId,
                PackageId = packageId,
                ReviewId = "model-review-001",
                OverallResult = "Pass",
                ValidationOverallResult = "Pass",
                RequiresRewrite = false,
                QualityScore = 88,
                ContentLength = 3300,
                CheckCount = 1,
                Summary = "打怪升级目标明确，邮徽规则承接稳定。",
                ReviewJson = """
                {
                  "decision": "commit",
                  "overallResult": "Pass",
                  "problems": [],
                  "suggestions": ["下一章强化投递铃的代价。"],
                  "checks": [
                    {
                      "key": "user_intent",
                      "name": "用户创意",
                      "status": "Pass",
                      "message": "符合打怪升级开局。",
                      "evidence": ["第一章主冲突由怪潮追击驱动。"]
                    }
                  ]
                }
                """,
                ReviewedAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            db.AgentMemoryReads.Add(new AgentMemoryRead
            {
                Id = "workflow-evidence-memory-read-001",
                UserId = auth.User.Id,
                ProjectId = projectId,
                SessionId = "workflow-evidence-session",
                RunId = runId,
                MemoryScope = "project",
                MemoryKeysJson = """["project.tone","project.protagonist"]""",
                SourceType = "memory_repository",
                Consumer = "chapter_package_builder",
                CreatedAt = DateTime.UtcNow.AddSeconds(-50)
            });
            db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
            {
                Id = "workflow-evidence-memory-promotion-001",
                UserId = auth.User.Id,
                ProjectId = projectId,
                SessionId = "workflow-evidence-session",
                RunId = runId,
                SourceScope = "session",
                TargetScope = "project",
                SourceMemoryKey = "session.requirement.opening_battle",
                TargetMemoryKey = "project.requirement.opening_battle",
                PromotionReason = "用户确认第一章采用打怪升级开局。",
                CreatedAt = DateTime.UtcNow.AddSeconds(-40)
            });
            db.ProductionEvents.Add(new ProductionEvent
            {
                Id = "workflow-evidence-event-001",
                RuntimeRunId = runId,
                UserId = auth.User.Id,
                ProjectId = projectId,
                ChapterId = chapterId,
                PackageId = packageId,
                EventType = "chapter_quality_reviewed",
                Stage = NovelAgentProductionStages.QualityReview,
                Status = "completed",
                Message = "章节质量评审完成。",
                ArtifactType = "agent_review",
                ArtifactId = "model-review-001",
                DataJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var workflowResponse = await _client.GetAsync($"/api/workflow/project/{projectId}");
        Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);

        var workflow = await workflowResponse.Content.ReadEnvelopeDataAsync<ProjectWorkflowDocument>();
        var productionEvent = workflow.ProductionStages
            .SelectMany(stage => stage.ProductionEvents)
            .Single(evt => evt.Id == "workflow-evidence-event-001");

        Assert.NotNull(productionEvent.Evidence);
        Assert.Equal("validated", productionEvent.Evidence!.Gate?.Status);
        Assert.True(productionEvent.Evidence.Gate?.FactSnapshotPassed);
        Assert.Equal("沈砚", productionEvent.Evidence.FactSnapshot?.ProtagonistName);
        Assert.Equal("commit", productionEvent.Evidence.AgentReview?.Decision);
        Assert.Equal("Pass", productionEvent.Evidence.AgentReview?.OverallResult);
        Assert.Contains(productionEvent.Evidence.AgentReviewChecks, check => check.Key == "user_intent");
        Assert.Contains(productionEvent.Evidence.MemoryReads, read => read.MemoryKeys.Contains("project.protagonist"));
        Assert.Contains(productionEvent.Evidence.MemoryPromotions, promotion => promotion.TargetMemoryKey == "project.requirement.opening_battle");
        Assert.Equal(1, productionEvent.Evidence.KnowledgeFactCount);
        Assert.Equal(1, productionEvent.Evidence.RagQueryCount);
        Assert.Equal(1, productionEvent.Evidence.AcceptedCreativeIntentCount);
    }

    /// <summary>
    /// Journey 1: Complete user flow from registration to semantic search.
    /// Tests: Register → Login → Create Project → Create Chapter → Verify Database State.
    /// </summary>
    [Fact]
    public async Task Journey1_RegisterToChapterCreation_FullWorkflow()
    {
        // Step 1: Register a new user
        var registerRequest = new RegisterRequest
        {
            Username = "journey1user",
            Email = "journey1@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var authResponse = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.Equal("journey1user", authResponse.User.Username);
        Assert.NotEmpty(authResponse.Token);
        Assert.NotEmpty(authResponse.User.Id);

        // Verify user and user settings were created in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var user = await db.Users.FindAsync(authResponse.User.Id);
            Assert.NotNull(user);
            Assert.Equal("journey1user", user.Username);
            Assert.Equal("journey1@test.com", user.Email);
            Assert.Equal("author", user.Role);

            var userSettings = await db.UserSettings.FindAsync(authResponse.User.Id);
            Assert.NotNull(userSettings);
            Assert.Equal(0.7f, userSettings.LlmTemperature);
        }

        // Step 2: Login with the new user
        var loginRequest = new LoginRequest
        {
            EmailOrUsername = "journey1user",
            Password = "SecurePass123!"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginAuthResponse = await loginResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(loginAuthResponse);
        Assert.NotEmpty(loginAuthResponse.Token);
        Assert.Equal(authResponse.User.Id, loginAuthResponse.User.Id);

        var token = loginAuthResponse.Token;

        // Step 3: Create a project with JWT authentication
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createProjectRequest = new CreateProjectRequest
        {
            Title = "Journey 1 Novel",
            Genre = "Fantasy",
            SubGenre = "Epic Fantasy",
            CoreHook = "A hero's journey to save the world"
        };

        var createProjectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest);
        Assert.Equal(HttpStatusCode.Created, createProjectResponse.StatusCode);

        var projectResponse = await createProjectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(projectResponse);
        Assert.Equal("Journey 1 Novel", projectResponse.Title);
        Assert.Equal("Fantasy", projectResponse.Genre);
        Assert.NotEmpty(projectResponse.Id);

        // Verify project was created with correct userId
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var project = await db.NovelProjects.FindAsync(projectResponse.Id);
            Assert.NotNull(project);
            Assert.Equal(authResponse.User.Id, project.UserId);
            Assert.Equal("Journey 1 Novel", project.Title);
        }

        // Step 4: Create a chapter in the project
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = projectResponse.Id,
            Title = "The Beginning",
            ChapterNumber = 1,
            Content = "In the beginning, there was a world full of magic and mystery. The hero awakens to find his destiny."
        };

        var createChapterResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        Assert.Equal(HttpStatusCode.Created, createChapterResponse.StatusCode);

        var chapterResponse = await createChapterResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(chapterResponse);
        Assert.Equal("The Beginning", chapterResponse.Title);
        Assert.Equal(1, chapterResponse.ChapterNumber);
        Assert.NotEmpty(chapterResponse.Id);

        // Verify chapter was created in database with correct metadata
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapter = await db.Chapters.FindAsync(chapterResponse.Id);
            Assert.NotNull(chapter);
            Assert.Equal(projectResponse.Id, chapter.ProjectId);
            Assert.Equal("The Beginning", chapter.Title);
            Assert.Equal(1, chapter.ChapterNumber);
            Assert.True(chapter.WordCount > 0);
            Assert.True(await db.ContentDocuments.AnyAsync(d =>
                d.UserId == authResponse.User.Id &&
                d.ProjectId == projectResponse.Id &&
                d.SourceType == "chapter" &&
                d.SourceId == chapter.Id &&
                d.DocumentRole == "chapter_body"));
        }

        // Step 5: Retrieve the chapter and verify content
        var getChapterResponse = await _client.GetAsync($"/api/chapters/{chapterResponse.Id}");
        Assert.Equal(HttpStatusCode.OK, getChapterResponse.StatusCode);

        var retrievedChapter = await getChapterResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(retrievedChapter);
        Assert.Equal("The Beginning", retrievedChapter.Title);
        Assert.Contains("magic and mystery", retrievedChapter.Content);

        // Step 6: Get all chapters for the project
        var getChaptersResponse = await _client.GetAsync($"/api/chapters/project/{projectResponse.Id}");
        Assert.Equal(HttpStatusCode.OK, getChaptersResponse.StatusCode);

        var chapters = await getChaptersResponse.Content.ReadEnvelopeDataAsync<List<ChapterResponse>>();
        Assert.NotNull(chapters);
        Assert.Single(chapters);
        Assert.Equal("The Beginning", chapters[0].Title);
    }

    /// <summary>
    /// Journey 2: Chapter deletion cascades to foreshadow references.
    /// Tests: Create Chapter with Foreshadow → Delete Chapter → Verify Foreshadow FK is NULL.
    /// </summary>
    [Fact]
    public async Task Journey2_DeleteChapter_ClearsForeshadowReferences()
    {
        // Setup: Register user and login
        var registerRequest = new RegisterRequest
        {
            Username = "journey2user",
            Email = "journey2@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        var authResponse = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(authResponse);

        var token = authResponse.Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create project
        var createProjectRequest = new CreateProjectRequest
        {
            Title = "Journey 2 Novel",
            Genre = "Mystery"
        };

        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest);
        var project = await projectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(project);

        // Create chapter
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = project.Id,
            Title = "Chapter with Foreshadow",
            ChapterNumber = 1,
            Content = "A mysterious clue is revealed."
        };

        var chapterResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        var chapter = await chapterResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(chapter);

        // Manually create a foreshadow that references this chapter
        string foreshadowId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = new Foreshadow
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = project.Id,
                Name = "The Great Mystery",
                Type = "plot",
                Status = "setup",
                SetupChapterId = chapter.Id,
                Importance = 5,
                Description = "A mystery that will be resolved later"
            };
            db.Foreshadows.Add(foreshadow);
            await db.SaveChangesAsync();
            foreshadowId = foreshadow.Id;
        }

        // Verify foreshadow references the chapter
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = await db.Foreshadows.FindAsync(foreshadowId);
            Assert.NotNull(foreshadow);
            Assert.Equal(chapter.Id, foreshadow.SetupChapterId);
        }

        // Delete the chapter
        var deleteResponse = await _client.DeleteAsync($"/api/chapters/{chapter.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify foreshadow still exists but SetupChapterId is NULL
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = await db.Foreshadows.FindAsync(foreshadowId);
            Assert.NotNull(foreshadow);
            Assert.Null(foreshadow.SetupChapterId);
            Assert.Equal("The Great Mystery", foreshadow.Name);
        }

        // Verify chapter is deleted from database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var deletedChapter = await db.Chapters.FindAsync(chapter.Id);
            Assert.Null(deletedChapter);
        }
    }

    /// <summary>
    /// Journey 3: Admin can access other users' projects, regular users cannot.
    /// Tests: Create User A with project → Login as Admin → Admin accesses User A's project.
    /// Also tests: Regular User B cannot access User A's project.
    /// </summary>
    [Fact]
    public async Task Journey3_AdminAccess_CanViewOtherUsersProjects()
    {
        // Step 1: Create User A and their project
        var userARegister = new RegisterRequest
        {
            Username = "userA",
            Email = "userA@test.com",
            Password = "SecurePass123!"
        };

        var userARegisterResponse = await _client.PostAsJsonAsync("/api/auth/register", userARegister);
        var userAAuth = await userARegisterResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(userAAuth);

        var userAClient = _factory.CreateClient();
        userAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAAuth.Token);

        var userAProjectRequest = new CreateProjectRequest
        {
            Title = "User A's Secret Novel",
            Genre = "Thriller"
        };

        var userAProjectResponse = await userAClient.PostAsJsonAsync("/api/projects", userAProjectRequest);
        var userAProject = await userAProjectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(userAProject);

        // Step 2: Create User B (regular user)
        var userBRegister = new RegisterRequest
        {
            Username = "userB",
            Email = "userB@test.com",
            Password = "SecurePass123!"
        };

        var userBRegisterResponse = await _client.PostAsJsonAsync("/api/auth/register", userBRegister);
        var userBAuth = await userBRegisterResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(userBAuth);

        var userBClient = _factory.CreateClient();
        userBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBAuth.Token);

        // Step 3: User B tries to access User A's project - should be FORBIDDEN
        var userBAccessResponse = await userBClient.GetAsync($"/api/projects/{userAProject.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, userBAccessResponse.StatusCode);

        // Step 4: Create an Admin user directly in the database
        string adminUserId;
        string adminToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

            // Create admin user with hashed password
            var adminUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "admin",
                Email = "admin@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPass123!"),
                Role = "Admin",
                IsActive = true
            };
            db.Users.Add(adminUser);

            var adminSettings = new UserSettings
            {
                UserId = adminUser.Id
            };
            db.UserSettings.Add(adminSettings);

            await db.SaveChangesAsync();
            adminUserId = adminUser.Id;
        }

        // Login as admin
        var adminLoginRequest = new LoginRequest
        {
            EmailOrUsername = "admin",
            Password = "AdminPass123!"
        };

        var adminLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", adminLoginRequest);
        Assert.Equal(HttpStatusCode.OK, adminLoginResponse.StatusCode);

        var adminAuth = await adminLoginResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(adminAuth);
        Assert.Equal("Admin", adminAuth.User.Role);
        adminToken = adminAuth.Token;

        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Step 5: Admin accesses User A's project - should SUCCEED
        var adminAccessResponse = await adminClient.GetAsync($"/api/projects/{userAProject.Id}");
        Assert.Equal(HttpStatusCode.OK, adminAccessResponse.StatusCode);

        var adminAccessedProject = await adminAccessResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(adminAccessedProject);
        Assert.Equal("User A's Secret Novel", adminAccessedProject.Title);
        Assert.Equal(userAProject.Id, adminAccessedProject.Id);

        // Step 6: Admin lists all projects - should see User A's project
        var adminListResponse = await adminClient.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

        var adminProjects = await adminListResponse.Content.ReadEnvelopeDataAsync<JsonElement>();
        var items = adminProjects.GetProperty("items").EnumerateArray().ToList();

        // Admin should see User A's project in the list
        var userAProjectInList = items.FirstOrDefault(p =>
            p.GetProperty("id").GetString() == userAProject.Id);
        Assert.NotEqual(default(JsonElement), userAProjectInList);
    }

    /// <summary>
    /// Journey 4: User isolation - users can only see their own projects.
    /// Tests: User A creates project → User B cannot see it in their project list.
    /// </summary>
    [Fact]
    public async Task Journey4_UserIsolation_OnlySeesOwnProjects()
    {
        // Create User A
        var userARegister = new RegisterRequest
        {
            Username = "isolationUserA",
            Email = "isolationA@test.com",
            Password = "SecurePass123!"
        };

        var userAResponse = await _client.PostAsJsonAsync("/api/auth/register", userARegister);
        var userAAuth = await userAResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(userAAuth);

        var userAClient = _factory.CreateClient();
        userAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAAuth.Token);

        // User A creates 2 projects
        for (int i = 1; i <= 2; i++)
        {
            var projectRequest = new CreateProjectRequest
            {
                Title = $"User A Project {i}",
                Genre = "Fantasy"
            };
            var projectResponse = await userAClient.PostAsJsonAsync("/api/projects", projectRequest);
            Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        }

        // Create User B
        var userBRegister = new RegisterRequest
        {
            Username = "isolationUserB",
            Email = "isolationB@test.com",
            Password = "SecurePass123!"
        };

        var userBResponse = await _client.PostAsJsonAsync("/api/auth/register", userBRegister);
        var userBAuth = await userBResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(userBAuth);

        var userBClient = _factory.CreateClient();
        userBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBAuth.Token);

        // User B creates 1 project
        var userBProjectRequest = new CreateProjectRequest
        {
            Title = "User B Project 1",
            Genre = "Sci-Fi"
        };
        var userBProjectResponse = await userBClient.PostAsJsonAsync("/api/projects", userBProjectRequest);
        Assert.Equal(HttpStatusCode.Created, userBProjectResponse.StatusCode);

        // User A lists their projects - should see only their 2 projects
        var userAListResponse = await userAClient.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, userAListResponse.StatusCode);

        var userAProjects = await userAListResponse.Content.ReadEnvelopeDataAsync<JsonElement>();
        var userAItems = userAProjects.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, userAItems.Count);
        Assert.All(userAItems, p => Assert.StartsWith("User A Project", p.GetProperty("title").GetString()!));

        // User B lists their projects - should see only their 1 project
        var userBListResponse = await userBClient.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, userBListResponse.StatusCode);

        var userBProjects = await userBListResponse.Content.ReadEnvelopeDataAsync<JsonElement>();
        var userBItems = userBProjects.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(userBItems);
        Assert.Equal("User B Project 1", userBItems[0].GetProperty("title").GetString());
    }

    /// <summary>
    /// Journey 5: Update chapter and verify content synchronization.
    /// Tests: Create Chapter → Update Content → Verify File System and Database Updated.
    /// </summary>
    [Fact]
    public async Task Journey5_UpdateChapter_ContentSynchronization()
    {
        // Setup user and project
        var register = new RegisterRequest
        {
            Username = "journey5user",
            Email = "journey5@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", register);
        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var projectRequest = new CreateProjectRequest
        {
            Title = "Journey 5 Novel",
            Genre = "Drama"
        };

        var projectResponse = await _client.PostAsJsonAsync("/api/projects", projectRequest);
        var project = await projectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(project);

        // Create chapter
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = project.Id,
            Title = "Original Title",
            ChapterNumber = 1,
            Content = "Original content goes here."
        };

        var createResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        var chapter = await createResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(chapter);

        // Update chapter with new content
        var updateRequest = new UpdateChapterRequest
        {
            Title = "Updated Title",
            Content = "This is the completely rewritten content with much more detail and information."
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/chapters/{chapter.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedChapter = await updateResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(updatedChapter);
        Assert.Equal("Updated Title", updatedChapter.Title);
        Assert.Contains("completely rewritten", updatedChapter.Content);

        // Verify database was updated
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var dbChapter = await db.Chapters.FindAsync(chapter.Id);
            Assert.NotNull(dbChapter);
            Assert.Equal("Updated Title", dbChapter.Title);
            Assert.True(dbChapter.WordCount > 0);
        }

        // Get chapter again to verify persistence
        var getResponse = await _client.GetAsync($"/api/chapters/{chapter.Id}");
        var retrievedChapter = await getResponse.Content.ReadEnvelopeDataAsync<ChapterResponse>();
        Assert.NotNull(retrievedChapter);
        Assert.Equal("Updated Title", retrievedChapter.Title);
        Assert.Contains("completely rewritten", retrievedChapter.Content);
    }

    /// <summary>
    /// Journey 6: Unauthorized access without JWT token.
    /// Tests: Access protected endpoints without authentication → Should return 401.
    /// </summary>
    [Fact]
    public async Task Journey6_UnauthorizedAccess_Returns401()
    {
        // Create a client without authentication header
        var unauthClient = _factory.CreateClient();

        // Try to list projects - should return 401
        var listResponse = await unauthClient.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        // Try to create project - should return 401
        var createRequest = new CreateProjectRequest
        {
            Title = "Unauthorized Project",
            Genre = "Fantasy"
        };

        var createResponse = await unauthClient.PostAsJsonAsync("/api/projects", createRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

        // Try to access a specific project - should return 401
        var getResponse = await unauthClient.GetAsync("/api/projects/some-id");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        // Try to create chapter - should return 401
        var chapterRequest = new CreateChapterRequest
        {
            ProjectId = "some-id",
            Title = "Chapter",
            ChapterNumber = 1,
            Content = "Content"
        };

        var chapterResponse = await unauthClient.PostAsJsonAsync("/api/chapters", chapterRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, chapterResponse.StatusCode);
    }

    private static IReadOnlyList<string> ExtractJsonStringValues(string json)
    {
        using var document = JsonDocument.Parse(json);
        var values = new List<string>();
        AddJsonStringValues(document.RootElement, values);
        return values;
    }

    private static string ExtractReviewCheckDebug(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("checks", out var checks) ||
            checks.ValueKind != JsonValueKind.Array)
        {
            return "checks_missing";
        }

        foreach (var check in checks.EnumerateArray())
        {
            if (!check.TryGetProperty("key", out var keyElement) ||
                !string.Equals(keyElement.GetString(), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var status = check.TryGetProperty("status", out var statusElement)
                ? statusElement.ToString()
                : "status_missing";
            var message = check.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "message_missing";
            var suggestions = check.TryGetProperty("suggestions", out var suggestionsElement) &&
                              suggestionsElement.ValueKind == JsonValueKind.Array
                ? string.Join("/", suggestionsElement.EnumerateArray().Select(item => item.GetString()))
                : "suggestions_missing";
            return $"{status}:{message}:{suggestions}";
        }

        return "editorial_missing";
    }

    private static void AddJsonStringValues(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    values.Add(property.Name);
                    AddJsonStringValues(property.Value, values);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AddJsonStringValues(item, values);
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
                break;
        }
    }
}
