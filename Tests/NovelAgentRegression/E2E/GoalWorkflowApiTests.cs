using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Auth;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

public sealed class GoalWorkflowApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public GoalWorkflowApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GoalWorkflow_ChapterEvidence_ReworkAndAcceptanceAreUserScopedAndIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var userA = await RegisterAsync($"goal-a-{suffix}", $"goal-a-{suffix}@test.com");
        var userB = await RegisterAsync($"goal-b-{suffix}", $"goal-b-{suffix}@test.com");
        await SeedGoalAsync(userA.User.Id);

        using var clientA = CreateAuthenticatedClient(userA.Token);
        using var clientB = CreateAuthenticatedClient(userB.Token);

        var statusResponse = await clientA.GetAsync("/api/goals/goal-e2e/workflow");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadEnvelopeDataAsync<GoalWorkflowStatusResponse>();
        Assert.Equal("goal-e2e", status.Goal.Id);
        Assert.Equal(2, status.Tasks.Count);
        Assert.Contains(status.Tasks, task => task.TaskType == "WriteCandidate");
        Assert.Contains(status.Tasks, task => task.TaskType == "UserAcceptance");
        Assert.Equal(1, status.CandidateChapterCount);

        var chapterResponse = await clientA.GetAsync("/api/goals/goal-e2e/workflow/chapters/1");
        Assert.Equal(HttpStatusCode.OK, chapterResponse.StatusCode);
        var chapter = await chapterResponse.Content.ReadEnvelopeDataAsync<GoalChapterDetailResponse>();
        Assert.Equal("candidate-e2e", chapter.Candidate.Id);
        Assert.Contains("候选正文", chapter.DraftArtifact.ContentJson);

        var forbidden = await clientB.GetAsync("/api/goals/goal-e2e/workflow");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var reworkRequest = new GoalChapterReworkRequest(
            "candidate-e2e",
            1,
            "session-e2e",
            "主角行动动机不清",
            null,
            null,
            "",
            "rework-e2e-1");
        var firstRework = await clientA.PostAsJsonAsync(
            "/api/goals/goal-e2e/workflow/chapters/1/rework",
            reworkRequest);
        var firstReworkBody = await firstRework.Content.ReadAsStringAsync();
        Assert.True(
            firstRework.StatusCode == HttpStatusCode.OK,
            $"Expected rework request to succeed, but received {(int)firstRework.StatusCode}: {firstReworkBody}");
        var first = await firstRework.Content.ReadEnvelopeDataAsync<GoalChapterReworkResponse>();

        var secondRework = await clientA.PostAsJsonAsync(
            "/api/goals/goal-e2e/workflow/chapters/1/rework",
            reworkRequest);
        Assert.Equal(HttpStatusCode.OK, secondRework.StatusCode);
        var second = await secondRework.Content.ReadEnvelopeDataAsync<GoalChapterReworkResponse>();
        Assert.Equal(first.TaskId, second.TaskId);
        Assert.NotNull(first.IntentArtifactId);

        var accept = await clientA.PostAsJsonAsync(
            "/api/goals/goal-e2e/workflow/chapters/1/accept",
            new GoalChapterAcceptRequest("candidate-e2e", 1));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        Assert.Equal(1, await db.KernelTasks.CountAsync(task =>
            task.UserId == userA.User.Id && task.TaskType == "DirectedRework"));
        Assert.True(await db.CandidateAcceptances.AnyAsync(item =>
            item.UserId == userA.User.Id && item.CandidateChapterId == "candidate-e2e"));
        Assert.False(await db.KernelTasks.AnyAsync(task => task.UserId == userB.User.Id));
    }

    private async Task<AuthResponse> RegisterAsync(string username, string email)
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = "SecurePass123!"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadEnvelopeDataAsync<AuthResponse>();
    }

    private async Task SeedGoalAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-e2e",
            UserId = userId,
            ProjectId = "project-e2e",
            GoalType = "chapter_batch",
            HumanReadableObjective = "验证工作流",
            SourceSessionId = "session-e2e",
            TotalCostLimit = 20,
            Status = "running",
            IdempotencyKey = $"goal:{userId}:e2e"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-e2e",
            UserId = userId,
            ProjectId = "project-e2e",
            GoalId = "goal-e2e",
            Version = 1,
            ContentHash = "graph-e2e-hash"
        });
        db.KernelTasks.AddRange(
            new KernelTask
            {
                Id = "graph-e2e:write",
                UserId = userId,
                ProjectId = "project-e2e",
                GoalId = "goal-e2e",
                TaskGraphVersionId = "graph-e2e",
                KernelName = "tianming_writing",
                TaskType = "WriteCandidate",
                Status = "completed",
                IdempotencyKey = $"write:{userId}:e2e"
            },
            new KernelTask
            {
                Id = "graph-e2e:accept",
                UserId = userId,
                ProjectId = "project-e2e",
                GoalId = "goal-e2e",
                TaskGraphVersionId = "graph-e2e",
                BranchId = "branch-e2e",
                KernelName = "human",
                TaskType = "UserAcceptance",
                Status = "awaiting_user",
                IdempotencyKey = $"accept:{userId}:e2e"
            });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-e2e",
            UserId = userId,
            ProjectId = "project-e2e",
            GoalId = "goal-e2e",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        db.KernelArtifacts.AddRange(
            new KernelArtifact
            {
                Id = "artifact-e2e",
                UserId = userId,
                ProjectId = "project-e2e",
                GoalId = "goal-e2e",
                TaskId = "graph-e2e:write",
                BranchId = "branch-e2e",
                ArtifactType = "CandidateChapterDraft",
                ContentJson = "{\"draftContent\":\"候选正文\"}",
                ContentHash = "artifact-e2e-hash"
            },
            new KernelArtifact
            {
                Id = "context-e2e",
                UserId = userId,
                ProjectId = "project-e2e",
                GoalId = "goal-e2e",
                TaskId = "graph-e2e:chapter-1-context",
                BranchId = "branch-e2e",
                ArtifactType = "ChapterContextContract",
                ContentJson = "{}",
                ContentHash = "context-e2e-hash"
            });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-e2e",
            UserId = userId,
            ProjectId = "project-e2e",
            GoalId = "goal-e2e",
            BranchId = "branch-e2e",
            ChapterId = "chapter-e2e",
            ChapterNumber = 1,
            CurrentArtifactId = "artifact-e2e"
        });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
