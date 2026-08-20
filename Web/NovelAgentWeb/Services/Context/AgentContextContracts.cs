using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Context;

public enum AgentContextProfile
{
    Conversation,
    GoalCommit,
    ChapterExecution
}

public sealed record AgentContextRequest(
    AgentContextProfile Profile,
    string UserId,
    string ProjectId,
    string SessionId,
    string Query,
    int KnowledgeLimit = 8);

public sealed record AgentProjectContext(
    string Id,
    string Title,
    string Genre,
    string SubGenre,
    string CoreHook,
    string Status,
    int WordCount,
    int ChapterCount);

public sealed record AgentGoalContext(
    string Id,
    string Status,
    string CollaborationMode,
    string HumanReadableObjective);

public sealed record AgentContextSource(
    string SourceType,
    string SourceId,
    string Version);

public sealed record AgentPendingIntentContext(
    string Id,
    string Source,
    string RawContent,
    string NormalizedIntent,
    string TargetScope,
    string Status,
    string MetadataJson);

public sealed record AgentContextEnvelope(
    AgentContextProfile Profile,
    AgentProjectContext Project,
    AgentGoalContext? LatestGoal,
    AgentMemoryBundle Memory,
    AgentKnowledgeContext Knowledge,
    IReadOnlyList<AgentPendingIntentContext> PendingIntents,
    IReadOnlyList<DialogueMessage> Dialogue,
    IReadOnlyList<AgentContextSource> Sources);

public interface IAgentContextAssembler
{
    Task<AgentContextEnvelope> BuildAsync(
        AgentContextRequest request,
        CancellationToken cancellationToken = default);

    Task<KernelExecutionContext> BuildKernelExecutionAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default);
}
