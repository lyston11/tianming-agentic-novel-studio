using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class PhaseContextBuilderTests
{
    private PhaseContextBuilder CreateBuilder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test-phase-context-" + Guid.NewGuid().ToString("N"));
        var workspace = new NovelAgentWorkspace
        {
            StorageRoot = tempDir,
            Orchestrator = null!
        };
        var memoryService = new AgentMemoryService(workspace);
        return new PhaseContextBuilder(memoryService, workspace);
    }

    [Fact]
    public async Task PrepareCreationContextAsync_WithoutRun_ReturnsBasicContext()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Generate chapter" };
        var missionPlan = new AgentMissionPlan();
        var bible = new StoryBibleDocument();

        var context = await builder.PrepareCreationContextAsync(session, mission, missionPlan, bible, null, CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context.ContainsKey("story_bible"));
        Assert.True(context.ContainsKey("mission_plan"));
        Assert.False(context.ContainsKey("context_package")); // No run provided
    }

    [Fact]
    public async Task PrepareCreationContextAsync_WithRun_IncludesContextPackage()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Generate chapter" };
        var missionPlan = new AgentMissionPlan();

        var contextPackage = new ChapterContextPackageSummary
        {
            ChapterId = "ch1",
            Status = "ready"
        };

        var bible = new StoryBibleDocument
        {
            AgentRuns = new List<NovelAgentRun>
            {
                new NovelAgentRun
                {
                    RunId = "run123",
                    ContextPackage = contextPackage
                }
            }
        };

        var context = await builder.PrepareCreationContextAsync(session, mission, missionPlan, bible, "run123", CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context.ContainsKey("context_package"));
        var retrievedPackage = context["context_package"] as ChapterContextPackageSummary;
        Assert.NotNull(retrievedPackage);
        Assert.Equal("ch1", retrievedPackage.ChapterId);
    }

    [Fact]
    public async Task PrepareCreationContextAsync_WithRecentObservations_IncludesLast5()
    {
        var builder = CreateBuilder();
        var session = new SessionContext
        {
            SessionId = "s1",
            ActiveProjectId = "p1",
            RecentObservations = new List<object> { "obs1", "obs2", "obs3", "obs4", "obs5", "obs6", "obs7" }
        };
        var mission = new AgentMissionState { CurrentGoal = "Generate" };
        var missionPlan = new AgentMissionPlan();
        var bible = new StoryBibleDocument();

        var context = await builder.PrepareCreationContextAsync(session, mission, missionPlan, bible, null, CancellationToken.None);

        Assert.True(context.ContainsKey("recent_observations"));
        var observations = context["recent_observations"] as List<object>;
        Assert.NotNull(observations);
        Assert.Equal(5, observations.Count);
        Assert.Equal("obs3", observations[0]); // Last 5: obs3, obs4, obs5, obs6, obs7
        Assert.Equal("obs7", observations[4]);
    }

    [Fact]
    public async Task PrepareReviewContextAsync_WithoutRun_ReturnsBasicContext()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Review chapter" };
        var missionPlan = new AgentMissionPlan();
        var bible = new StoryBibleDocument();

        var context = await builder.PrepareReviewContextAsync(session, mission, missionPlan, bible, null, CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context.ContainsKey("mission_plan"));
        Assert.False(context.ContainsKey("draft_artifact")); // No run provided
        Assert.False(context.ContainsKey("gate_report"));
    }

    [Fact]
    public async Task PrepareReviewContextAsync_WithRun_IncludesDraftAndGateReport()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Review" };
        var missionPlan = new AgentMissionPlan();

        var draftArtifact = new ChapterDraftArtifact
        {
            ChapterId = "ch1"
        };

        var gateReport = new GenerationGateReport
        {
            Status = "pass",
            ProtocolPassed = true
        };

        var bible = new StoryBibleDocument
        {
            AgentRuns = new List<NovelAgentRun>
            {
                new NovelAgentRun
                {
                    RunId = "run456",
                    DraftArtifact = draftArtifact,
                    GateReport = gateReport
                }
            }
        };

        var context = await builder.PrepareReviewContextAsync(session, mission, missionPlan, bible, "run456", CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context.ContainsKey("draft_artifact"));
        Assert.True(context.ContainsKey("gate_report"));

        var retrievedDraft = context["draft_artifact"] as ChapterDraftArtifact;
        Assert.NotNull(retrievedDraft);
        Assert.Equal("ch1", retrievedDraft.ChapterId);

        var retrievedGate = context["gate_report"] as GenerationGateReport;
        Assert.NotNull(retrievedGate);
        Assert.Equal("pass", retrievedGate.Status);
    }

    [Fact]
    public async Task PrepareReviewContextAsync_WithRecentObservations_IncludesLast3()
    {
        var builder = CreateBuilder();
        var session = new SessionContext
        {
            SessionId = "s1",
            ActiveProjectId = "p1",
            RecentObservations = new List<object> { "obs1", "obs2", "obs3", "obs4", "obs5" }
        };
        var mission = new AgentMissionState { CurrentGoal = "Review" };
        var missionPlan = new AgentMissionPlan();
        var bible = new StoryBibleDocument();

        var context = await builder.PrepareReviewContextAsync(session, mission, missionPlan, bible, null, CancellationToken.None);

        Assert.True(context.ContainsKey("recent_observations"));
        var observations = context["recent_observations"] as List<object>;
        Assert.NotNull(observations);
        Assert.Equal(3, observations.Count);
        Assert.Equal("obs3", observations[0]); // Last 3: obs3, obs4, obs5
        Assert.Equal("obs5", observations[2]);
    }

    [Fact]
    public async Task PrepareCreationContextAsync_CaseInsensitiveRunId_FindsRun()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Generate" };
        var missionPlan = new AgentMissionPlan();

        var bible = new StoryBibleDocument
        {
            AgentRuns = new List<NovelAgentRun>
            {
                new NovelAgentRun
                {
                    RunId = "ABC123xyz",
                    ContextPackage = new ChapterContextPackageSummary { ChapterId = "ch1" }
                }
            }
        };

        var context = await builder.PrepareCreationContextAsync(session, mission, missionPlan, bible, "abc123XYZ", CancellationToken.None);

        Assert.True(context.ContainsKey("context_package"));
    }

    [Fact]
    public async Task PrepareReviewContextAsync_CaseInsensitiveRunId_FindsRun()
    {
        var builder = CreateBuilder();
        var session = new SessionContext { SessionId = "s1", ActiveProjectId = "p1" };
        var mission = new AgentMissionState { CurrentGoal = "Review" };
        var missionPlan = new AgentMissionPlan();

        var bible = new StoryBibleDocument
        {
            AgentRuns = new List<NovelAgentRun>
            {
                new NovelAgentRun
                {
                    RunId = "DEF456uvw",
                    DraftArtifact = new ChapterDraftArtifact { ChapterId = "ch1" }
                }
            }
        };

        var context = await builder.PrepareReviewContextAsync(session, mission, missionPlan, bible, "def456UVW", CancellationToken.None);

        Assert.True(context.ContainsKey("draft_artifact"));
    }
}
