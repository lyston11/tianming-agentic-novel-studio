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
}
