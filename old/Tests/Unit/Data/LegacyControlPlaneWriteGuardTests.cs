using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Data.Interceptors;
using Xunit;

namespace Tests.Unit.Data;

public sealed class LegacyControlPlaneWriteGuardTests
{
    [Fact]
    public async Task Enabled_guard_rejects_legacy_control_plane_writes()
    {
        await using var db = CreateContext(enabled: true);
        db.CreativeGoals.Add(NewGoal());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("use NovelAgent Application commands", error.Message);
    }

    [Fact]
    public async Task Disabled_guard_preserves_the_migration_window()
    {
        await using var db = CreateContext(enabled: false);
        db.CreativeGoals.Add(NewGoal());

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.CreativeGoals.CountAsync());
    }

    private static NovelAgentDbContext CreateContext(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TargetArchitecture:EnforceLegacyControlPlaneReadOnly"] = enabled.ToString()
            })
            .Build();
        var guard = new LegacyControlPlaneWriteGuard(configuration);
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(guard)
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static CreativeGoal NewGoal() => new()
    {
        Id = "goal-1",
        UserId = "user-1",
        ProjectId = "project-1",
        SourceSessionId = "session-1",
        GoalType = "novel_production",
        HumanReadableObjective = "Write chapter one",
        IdempotencyKey = "goal-1"
    };
}
