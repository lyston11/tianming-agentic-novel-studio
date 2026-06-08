using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class MemoryArchitectureTests
{
    [Fact]
    public void UserProfile_HasExpectedProperties()
    {
        var profile = new UserProfile
        {
            UserId = "user123",
            StylePreferences = new() { ["tone"] = "幽默轻松" },
            GenreHabits = new() { ["科幻"] = 5 },
            ConfirmationTolerance = "medium",
            GlobalConstraints = new() { "禁止血腥暴力描写" },
        };

        Assert.Equal("user123", profile.UserId);
        Assert.Equal("幽默轻松", profile.StylePreferences["tone"]);
        Assert.Equal(5, profile.GenreHabits["科幻"]);
        Assert.Single(profile.GlobalConstraints);
    }

    [Fact]
    public void SessionContext_CanHaveNullActiveProjectId()
    {
        var session = new SessionContext
        {
            SessionId = "sess1",
            ActiveProjectId = null,
        };

        Assert.Null(session.ActiveProjectId);
    }

    [Fact]
    public void AgentRuntimeContext_CombinesAllThreeTiers()
    {
        var context = new AgentRuntimeContext
        {
            User = new UserProfile { UserId = "user1" },
            ActiveProject = null,
            Session = new SessionContext { SessionId = "sess1" },
            Mission = new AgentMissionState { CurrentGoal = "test" },
            MissionPlan = new AgentMissionPlan(),
        };

        Assert.Equal("user1", context.User.UserId);
        Assert.Null(context.ActiveProject);
        Assert.Equal("sess1", context.Session.SessionId);
        Assert.Equal("test", context.Mission.CurrentGoal);
        Assert.NotNull(context.MissionPlan);
    }
}
