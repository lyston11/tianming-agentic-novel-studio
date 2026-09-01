namespace TM.Web.NovelAgentWeb.Services.Goals;

public enum TaskExecutionKind
{
    Kernel,
    System,
    HumanGate
}

public enum AuthorityMutation
{
    None,
    CommitCanon,
    MergeAcceptedPrefix
}

public sealed record TaskGraphNode(
    string Id,
    string TaskType,
    string KernelName,
    TaskExecutionKind ExecutionKind,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> RequiresArtifacts,
    IReadOnlyList<string> ProducesArtifacts,
    AuthorityMutation AuthorityMutation,
    int? ChapterNumber);

public sealed record TaskGraphDefinition(
    string GoalId,
    int Version,
    IReadOnlyList<TaskGraphNode> Nodes);

public sealed record TaskGraphValidationError(string Code, string Message, string? NodeId = null);

public interface IGoalCompiler
{
    Task<TaskGraphDefinition> CompileAsync(
        string goalId,
        CancellationToken cancellationToken = default);

    Task<TaskGraphDefinition> RecompileForRevisionAsync(
        string goalId,
        string revisionId,
        IReadOnlyCollection<string> affectedNodeIds,
        CancellationToken cancellationToken = default);
}
