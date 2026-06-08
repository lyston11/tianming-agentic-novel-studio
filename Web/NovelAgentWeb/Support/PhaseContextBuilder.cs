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
        if (mission?.MissionPlan != null)
        {
            context["mission_summary"] = new
            {
                stage = mission.MissionPlan.Stage,
                status = mission.MissionPlan.Status,
                project_title = mission.MissionPlan.ProjectTitle,
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
        if (mission?.MissionPlan != null)
        {
            context["mission_plan"] = mission.MissionPlan;
        }

        // Project memory summary
        var projectMemory = await _memoryService.LoadProjectMemoryAsync(
            new NovelProjectInfo { Id = session.ActiveProjectId ?? string.Empty },
            ct).ConfigureAwait(false);

        if (projectMemory != null)
        {
            context["project_memory_summary"] = new
            {
                total_chapters = projectMemory.TotalChapters,
                key_patterns = projectMemory.SuccessPatterns.Take(3).ToList(),
            };
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
}
