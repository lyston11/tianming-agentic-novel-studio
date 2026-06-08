using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class PhaseContextBuilder
{
    private readonly AgentMemoryService _memoryService;
    private readonly NovelAgentWorkspace _workspace;

    public PhaseContextBuilder(AgentMemoryService memoryService, NovelAgentWorkspace workspace)
    {
        _memoryService = memoryService;
        _workspace = workspace;
    }

    public async Task<int> EstimateTokensForPhaseAsync(
        ConversationPhase phase,
        SessionContext session,
        CancellationToken ct)
    {
        return phase switch
        {
            ConversationPhase.Conversation => 500,   // Checkpoint only
            ConversationPhase.Planning => 2500,      // Checkpoint + summary + RAG(5)
            ConversationPhase.Creation => 12000,     // Full context package
            ConversationPhase.Review => 7000,        // Checkpoint + draft + reports
            _ => 500,
        };
    }

    public async Task<Dictionary<string, object>> PrepareConversationContextAsync(
        SessionContext session,
        AgentMissionState mission,
        CancellationToken ct)
    {
        var context = new Dictionary<string, object>();

        // Minimal context: only recent observations
        if (session.RecentObservations != null && session.RecentObservations.Count > 0)
        {
            context["recent_observations"] = session.RecentObservations.TakeLast(1).ToList();
        }

        // Mission summary (very brief)
        var missionPlan = mission?.MissionPlan;
        if (missionPlan != null)
        {
            context["mission_summary"] = new
            {
                status = missionPlan.Status,
                current_objective = missionPlan.CurrentObjective,
            };
        }

        return context;
    }

    public async Task<Dictionary<string, object>> PreparePlanningContextAsync(
        SessionContext session,
        AgentMissionState mission,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        var context = new Dictionary<string, object>();

        // Recent observations (3 items)
        if (session.RecentObservations != null && session.RecentObservations.Count > 0)
        {
            context["recent_observations"] = session.RecentObservations.TakeLast(3).ToList();
        }

        // Mission plan summary
        var missionPlan = mission?.MissionPlan;
        if (missionPlan != null)
        {
            context["mission_plan"] = missionPlan;
        }

        // Story Bible basics
        if (bible?.Constitution != null)
        {
            context["story_foundation"] = new
            {
                genre = bible.Constitution.Genre,
                core_hook = bible.Constitution.CoreHook,
                volume_count = bible.VolumeArcs?.Count ?? 0,
            };
        }

        return context;
    }

    public async Task<Dictionary<string, object>> PrepareCreationContextAsync(
        SessionContext session,
        AgentMissionState mission,
        StoryBibleDocument bible,
        string? runId,
        CancellationToken ct)
    {
        var context = new Dictionary<string, object>();

        // Find current run and context package
        NovelAgentRun? run = null;
        if (!string.IsNullOrWhiteSpace(runId))
        {
            run = bible.AgentRuns?.FirstOrDefault(r =>
                string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        // Full context package if available
        if (run?.ContextPackage != null)
        {
            context["context_package"] = run.ContextPackage;
        }

        // Story Bible (full)
        context["story_bible"] = bible;

        // Mission plan
        var missionPlan = mission?.MissionPlan;
        if (missionPlan != null)
        {
            context["mission_plan"] = missionPlan;
        }

        // Recent observations (5 items for creation phase)
        if (session.RecentObservations != null && session.RecentObservations.Count > 0)
        {
            context["recent_observations"] = session.RecentObservations.TakeLast(5).ToList();
        }

        return context;
    }

    public async Task<Dictionary<string, object>> PrepareReviewContextAsync(
        SessionContext session,
        AgentMissionState mission,
        StoryBibleDocument bible,
        string? runId,
        CancellationToken ct)
    {
        var context = new Dictionary<string, object>();

        // Find current run
        NovelAgentRun? run = null;
        if (!string.IsNullOrWhiteSpace(runId))
        {
            run = bible.AgentRuns?.FirstOrDefault(r =>
                string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        // Draft artifact
        if (run?.DraftArtifact != null)
        {
            context["draft_artifact"] = run.DraftArtifact;
        }

        // Gate report
        if (run?.GateReport != null)
        {
            context["gate_report"] = run.GateReport;
        }

        // Mission plan
        var missionPlan = mission?.MissionPlan;
        if (missionPlan != null)
        {
            context["mission_plan"] = missionPlan;
        }

        // Recent observations (3 items)
        if (session.RecentObservations != null && session.RecentObservations.Count > 0)
        {
            context["recent_observations"] = session.RecentObservations.TakeLast(3).ToList();
        }

        return context;
    }
}
