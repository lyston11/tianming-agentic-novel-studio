using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class RuntimeControllerTests
{
    [Fact]
    public void Controller_ExposesOnlyReadOnlyAuditEndpoints()
    {
        var actions = typeof(RuntimeController).GetMethods()
            .Where(method => method.DeclaringType == typeof(RuntimeController))
            .ToArray();

        Assert.DoesNotContain(actions, method =>
            method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Length > 0 ||
            method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: true).Length > 0 ||
            method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: true).Length > 0);
    }

    [Fact]
    public async Task GetRun_ReturnsOwnedLegacyRun()
    {
        await using var db = CreateDb();
        var run = SeedRun(db, "run-1", "user-1", "session-1", AgentRuntimeRunStatus.Completed);
        await db.SaveChangesAsync();
        var controller = Controller(db, "user-1");

        var result = await controller.GetRun(run.Id, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var payload = Assert.IsType<RuntimeRunDto>(envelope.Data);
        Assert.Equal("run-1", payload.RunId);
        Assert.Equal(AgentRuntimeRunStatus.Completed, payload.Status);
    }

    [Fact]
    public async Task GetRun_RejectsAnotherUsersLegacyRun()
    {
        await using var db = CreateDb();
        SeedRun(db, "run-2", "user-2", "session-2", AgentRuntimeRunStatus.Completed);
        await db.SaveChangesAsync();
        var controller = Controller(db, "user-1");

        var result = await controller.GetRun("run-2", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("RUN_ACCESS_DENIED", envelope.Error!.Code);
    }

    [Fact]
    public async Task GetActiveRun_ReturnsLegacyStateWithoutMutatingIt()
    {
        await using var db = CreateDb();
        var run = SeedRun(db, "run-1", "user-1", "session-1", AgentRuntimeRunStatus.Running);
        run.CurrentPhase = "legacy_phase";
        await db.SaveChangesAsync();
        var controller = Controller(db, "user-1");

        var result = await controller.GetActiveRun("session-1", null, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var payload = Assert.IsType<RuntimeActiveRunDto>(envelope.Data);
        Assert.True(payload.HasActiveRun);
        Assert.Equal("legacy_phase", payload.Run!.CurrentPhase);
        Assert.Equal(AgentRuntimeRunStatus.Running, (await db.AgentRuntimeRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task GetRunEvents_ReturnsOnlyOwnedRunEvents()
    {
        await using var db = CreateDb();
        SeedRun(db, "run-1", "user-1", "session-1", AgentRuntimeRunStatus.Completed);
        db.AgentRuntimeEvents.Add(new AgentRuntimeEvent
        {
            Id = "event-1",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            SessionId = "session-1",
            Type = "legacy_complete",
            Message = "历史执行完成。"
        });
        await db.SaveChangesAsync();
        var controller = Controller(db, "user-1");

        var result = await controller.GetRunEvents("run-1", 20, null, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<RuntimeEventDto>>(envelope.Data);
        Assert.Equal("event-1", Assert.Single(events).EventId);
    }

    private static RuntimeController Controller(NovelAgentDbContext db, string userId) =>
        new(new LegacyRuntimeAuditReader(db), new AgentRuntimeEventService(db), CurrentUser(userId));

    private static AgentRuntimeRun SeedRun(
        NovelAgentDbContext db,
        string runId,
        string userId,
        string sessionId,
        string status)
    {
        var run = new AgentRuntimeRun
        {
            Id = runId,
            UserId = userId,
            SessionId = sessionId,
            Status = status,
            Mode = AgentRuntimeRunMode.Production,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentRuntimeRuns.Add(run);
        return run;
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static ICurrentUserService CurrentUser(string userId)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(service => service.GetUserId()).Returns(userId);
        current.Setup(service => service.TryGetUserId()).Returns(userId);
        current.Setup(service => service.IsAuthenticated()).Returns(true);
        return current.Object;
    }

    private static async Task<ApiEnvelope<object>> ApplyEnvelopeAsync(IActionResult result)
    {
        var filter = new ApiEnvelopeResultFilter();
        var httpContext = new DefaultHttpContext { TraceIdentifier = "test-trace" };
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
        return Assert.IsType<ApiEnvelope<object>>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
    }
}
