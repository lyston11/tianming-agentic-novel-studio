using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeStateServiceTests
{
    [Fact]
    public async Task CreateQueuedAsync_PersistsQueuedRunAndFindsActiveRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);

        var created = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: null,
            UserMessage: "开始写一本末世小说"));

        Assert.Equal("queued", created.Status);
        Assert.Equal("session-1", created.SessionId);
        Assert.Equal("开始写一本末世小说", created.UserMessage);

        var active = await runs.TryGetActiveAsync("user-1", "session-1");

        Assert.NotNull(active);
        Assert.Equal(created.Id, active!.Id);
    }

    [Fact]
    public async Task AddAsync_PersistsPendingInterruptForActiveRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));

        var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "freeform",
            Message: "把女主改得更聪明一点",
            Priority: 0));

        Assert.Equal("pending", interrupt.Status);
        Assert.Equal(run.Id, interrupt.RuntimeRunId);

        var pending = await interrupts.GetPendingAsync(run.Id);

        Assert.Single(pending);
        Assert.Equal("把女主改得更聪明一点", pending[0].Message);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
