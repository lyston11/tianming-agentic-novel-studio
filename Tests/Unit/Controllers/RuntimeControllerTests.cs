using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Reflection;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;

namespace Tests.Unit.Controllers;

public class RuntimeControllerTests
{
    [Fact]
    public void GetRunEvents_QueryEndpoint_AllowsSessionReplayWithoutRunId()
    {
        var method = typeof(RuntimeController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m =>
                m.Name == nameof(RuntimeController.GetRunEvents) &&
                m.GetCustomAttributes<HttpGetAttribute>()
                    .Any(attr => attr.Template == "events"));
        var runId = method.GetParameters().Single(p => p.Name == "runId");

        var nullability = new NullabilityInfoContext().Create(runId);

        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
    }

    [Fact]
    public async Task GetRun_ReturnsUnifiedEnvelopeWithRuntimeContractFields()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续写第二章",
            Mode: AgentRuntimeRunMode.Production,
            IdempotencyKey: "msg-1",
            SourceMessageId: "msg-1",
            Budget: new AgentRuntimeBudget(MaxToolCalls: 5)));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"));

        var result = await controller.GetRun(run.Id, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("v1", envelope.ApiVersion);
        Assert.Equal("agent-tools-v1", envelope.ToolSchemaVersion);
        Assert.Equal("agent-loop-v1", envelope.AgentLoopVersion);
        Assert.Equal("agentic-tianming-v1", envelope.KernelVersion);
        Assert.NotNull(envelope.Data);
        var data = Assert.IsType<RuntimeRunDto>(envelope.Data);
        Assert.Equal(run.Id, data.RunId);
        Assert.Equal(AgentRuntimeRunMode.Production, data.Mode);
        Assert.Equal("msg-1", data.IdempotencyKey);
        Assert.Contains("\"maxToolCalls\":5", data.BudgetJson);
    }

    [Fact]
    public async Task GetRunEvents_ReturnsUnifiedEnvelopeWithDisplayAndArtifactFields()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续写"));
        var events = new AgentRuntimeEventService(db);
        await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_event",
            Message: "生产包已构建",
            Stage: "BuildChapterPackage",
            Status: "completed",
            ArtifactType: "TianmingPackage",
            ArtifactId: "package-1",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline));
        var controller = new RuntimeController(runs, events, CurrentUser("user-1"));

        var result = await controller.GetRunEvents(run.Id, "user-1", "session-1", 20, null, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var eventsPayload = Assert.IsAssignableFrom<IReadOnlyList<RuntimeEventDto>>(envelope.Data);
        var evt = Assert.Single(eventsPayload);
        Assert.Equal("BuildChapterPackage", evt.Stage);
        Assert.Equal("TianmingPackage", evt.ArtifactType);
        Assert.Equal("package-1", evt.ArtifactId);
        Assert.Equal(AgentRuntimeEventSurface.Workflow, evt.DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, evt.DisplayPolicy);
    }

    [Fact]
    public async Task GetRunEvents_WhenAfterEventIdProvidedReturnsSessionEventsAfterCursor()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续写"));
        var events = new AgentRuntimeEventService(db);
        var first = await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "step_start",
            Message: "开始构建生产包"));
        var second = await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "step_complete",
            Message: "生产包已构建"));
        var third = await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "正在生成正文"));
        first.CreatedAt = new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);
        second.CreatedAt = new DateTime(2026, 6, 23, 10, 0, 1, DateTimeKind.Utc);
        third.CreatedAt = new DateTime(2026, 6, 23, 10, 0, 2, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        var controller = new RuntimeController(runs, events, CurrentUser("user-1"));

        var result = await controller.GetRunEvents(
            runId: "",
            userId: "user-1",
            sessionId: "session-1",
            limit: 20,
            afterEventId: first.Id,
            ct: CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var eventsPayload = Assert.IsAssignableFrom<IReadOnlyList<RuntimeEventDto>>(envelope.Data);
        Assert.Collection(
            eventsPayload,
            evt => Assert.Equal(second.Id, evt.EventId),
            evt => Assert.Equal(third.Id, evt.EventId));
    }

    [Fact]
    public async Task GetRunEvents_WhenUserIdOmittedUsesCurrentUserForSessionReplay()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续写"));
        var events = new AgentRuntimeEventService(db);
        var saved = await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "正在生成正文"));
        var controller = new RuntimeController(runs, events, CurrentUser("user-1"));

        var result = await controller.GetRunEvents(
            runId: "",
            userId: null,
            sessionId: "session-1",
            limit: 20,
            afterEventId: null,
            ct: CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var eventsPayload = Assert.IsAssignableFrom<IReadOnlyList<RuntimeEventDto>>(envelope.Data);
        var evt = Assert.Single(eventsPayload);
        Assert.Equal(saved.Id, evt.EventId);
    }

    [Fact]
    public async Task GetActiveRun_ReturnsIdleWhenSessionHasNoActiveRuntimeRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"));

        var result = await controller.GetActiveRun("session-1", null, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<RuntimeActiveRunDto>(envelope.Data);
        Assert.False(data.HasActiveRun);
        Assert.Equal("idle", data.Status);
        Assert.Null(data.Run);
    }

    [Fact]
    public async Task GetActiveRun_ReturnsCurrentUserActiveRuntimeState()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "写第一章",
            Mode: AgentRuntimeRunMode.Production));
        await runs.MarkRunningAsync(run.Id);
        await runs.UpdateProgressAsync(run.Id, "draft_generation", "正在生成正文", "ProduceChapter", 2);
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"));

        var result = await controller.GetActiveRun("session-1", null, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<RuntimeActiveRunDto>(envelope.Data);
        Assert.True(data.HasActiveRun);
        Assert.Equal(AgentRuntimeRunStatus.Running, data.Status);
        Assert.NotNull(data.Run);
        Assert.Equal(run.Id, data.Run!.RunId);
        Assert.Equal("draft_generation", data.Run.CurrentPhase);
        Assert.Equal("ProduceChapter", data.Run.ActiveTool);
        Assert.Equal(2, data.Run.CurrentStep);
        Assert.NotNull(data.HeartbeatAt);
    }

    [Fact]
    public async Task GetActiveRun_RejectsOtherUsersSessionUnlessAdmin()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-2",
            SessionId: "session-2",
            ProjectId: "project-2",
            UserMessage: "后台任务"));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"));

        var result = await controller.GetActiveRun("session-2", "user-2", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("RUN_ACCESS_DENIED", envelope.Error!.Code);
    }

    [Fact]
    public async Task GetActiveRun_AllowsAdminReadOnlyLookupByUserId()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-2",
            SessionId: "session-2",
            ProjectId: "project-2",
            UserMessage: "后台任务"));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("admin", isAdmin: true));

        var result = await controller.GetActiveRun("session-2", "user-2", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<RuntimeActiveRunDto>(envelope.Data);
        Assert.True(data.HasActiveRun);
        Assert.Equal(run.Id, data.Run!.RunId);
        Assert.Equal("user-2", data.Run.UserId);
    }

    [Fact]
    public async Task GetRun_RejectsOtherUsersRunUnlessAdmin()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-2",
            SessionId: "session-2",
            ProjectId: "project-2",
            UserMessage: "后台任务"));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"));

        var result = await controller.GetRun(run.Id, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("RUN_ACCESS_DENIED", envelope.Error!.Code);
    }

    [Fact]
    public async Task GetRun_AllowsAdminReadOnlyAccess()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-2",
            SessionId: "session-2",
            ProjectId: "project-2",
            UserMessage: "后台任务"));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("admin", isAdmin: true));

        var result = await controller.GetRun(run.Id, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<RuntimeRunDto>(envelope.Data);
        Assert.Equal("user-2", data.UserId);
    }

    [Fact]
    public async Task CancelRun_PersistsCancelInterruptForAgentObservation()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "后台任务"));
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"), interrupts);

        var result = await controller.CancelRun(run.Id, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var reloaded = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == run.Id);
        Assert.True(reloaded.CancelRequested);
        var interrupt = Assert.Single(await db.AgentInterrupts.ToListAsync());
        Assert.Equal(run.Id, interrupt.RuntimeRunId);
        Assert.Equal("cancel", interrupt.Kind);
        Assert.Equal(AgentInterruptStatus.Pending, interrupt.Status);
    }

    [Fact]
    public async Task InterruptRun_WithDirectionChangeQueuesInterruptWithoutCancellingRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db, runtimeRuns: runs);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "后台任务"));
        await runs.MarkRunningAsync(run.Id);
        var controller = new RuntimeController(runs, new AgentRuntimeEventService(db), CurrentUser("user-1"), interrupts);

        var result = await controller.InterruptRun(
            run.Id,
            new RuntimeInterruptRequest
            {
                Kind = "direction_change",
                Message = "不要感情线，改成打怪升级",
                Priority = 90
            },
            CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<RuntimeInterruptDto>(envelope.Data);
        Assert.Equal(run.Id, data.RunId);
        Assert.Equal("direction_change", data.Kind);
        Assert.Equal(AgentInterruptStatus.Pending, data.Status);

        var reloaded = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == run.Id);
        Assert.False(reloaded.CancelRequested);
        var interrupt = await db.AgentInterrupts.SingleAsync();
        Assert.Equal("direction_change", interrupt.Kind);
        Assert.Equal("不要感情线，改成打怪升级", interrupt.Message);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static ICurrentUserService CurrentUser(string userId, bool isAdmin = false)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(x => x.GetUserId()).Returns(userId);
        current.Setup(x => x.TryGetUserId()).Returns(userId);
        current.Setup(x => x.IsAdmin()).Returns(isAdmin);
        current.Setup(x => x.IsAuthenticated()).Returns(true);
        return current.Object;
    }

    private static async Task<ApiEnvelope<object>> ApplyEnvelopeAsync(IActionResult result)
    {
        var filter = new ApiEnvelopeResultFilter();
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "test-trace";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            controller: new object());

        await filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                result,
                controller: new object())));

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        return Assert.IsType<ApiEnvelope<object>>(objectResult.Value);
    }
}
