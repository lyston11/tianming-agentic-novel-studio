using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Context;

public enum AgentContextProfile
{
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

public sealed record ConversationContextRequest(
    string UserId,
    string SessionId,
    string? Query,
    int KnowledgeLimit = 8);

public sealed record ProjectContextSnapshotRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string? GoalId,
    string? Query,
    int KnowledgeLimit = 8,
    string? BindingVersion = null);

public sealed record ConversationCapability(
    string Name,
    bool IsReadOnly);

public sealed record AccessibleProjectContext(
    string ProjectId,
    string Title,
    string Status,
    DateTime UpdatedAt);

public sealed record ConversationBindingPointer(
    string ProjectId,
    string Version);

public abstract record ConversationContext
{
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public long BindingVersion { get; init; }
    public required string SystemInstructions { get; init; }
    public required ChatPromptWindowDto Transcript { get; init; }
    public required IReadOnlyList<ConversationCapability> GeneralCapabilities { get; init; }
}

public sealed record UnboundConversationContext : ConversationContext
{
    public required IReadOnlyList<AccessibleProjectContext> AccessibleProjects { get; init; }
}

public sealed record BoundConversationContext : ConversationContext
{
    public required ConversationBindingPointer Binding { get; init; }
    public required ProjectContextSnapshot ProjectSnapshot { get; init; }
    public IReadOnlyList<string> ProjectTools => ProjectSnapshot.AllowedTools;
}

public sealed record AgentProjectContext(
    string Id,
    string Title,
    string Genre,
    string SubGenre,
    string CoreHook,
    string Status,
    int WordCount,
    int ChapterCount,
    DateTime UpdatedAt);

public sealed record AgentGoalContext(
    string Id,
    string Status,
    string CollaborationMode,
    string HumanReadableObjective,
    long AggregateVersion);

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
    string MetadataJson,
    DateTime UpdatedAt);

public sealed record ProjectContextSnapshot(
    string Version,
    AgentProjectContext Project,
    AgentGoalContext? LatestGoal,
    AgentMemoryBundle Memory,
    AgentKnowledgeContext Knowledge,
    IReadOnlyList<AgentPendingIntentContext> PendingIntents,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<AgentContextSource> Sources);

public sealed record AgentContextEnvelope(
    AgentContextProfile Profile,
    AgentProjectContext Project,
    AgentGoalContext? LatestGoal,
    AgentMemoryBundle Memory,
    AgentKnowledgeContext Knowledge,
    IReadOnlyList<AgentPendingIntentContext> PendingIntents,
    IReadOnlyList<DialogueMessage> Dialogue,
    IReadOnlyList<AgentContextSource> Sources);

public interface IConversationContextAssembler
{
    Task<ConversationContext> BuildAsync(
        ConversationContextRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgentContextAssembler
{
    Task<AgentContextEnvelope> BuildAsync(
        AgentContextRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectContextSnapshot> BuildProjectSnapshotAsync(
        ProjectContextSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<KernelExecutionContext> BuildKernelExecutionAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default);
}
