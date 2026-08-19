using Tianming.NovelAgent.Domain.Common;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Domain.Production;

public enum TaskKind
{
    FreezeContext,
    AnalyzeRequirements,
    PlanBatch,
    CompileChapterContext,
    WriteCandidate,
    ReviewContinuity,
    ReviewLiteraryQuality,
    AwaitHumanAcceptance,
    RequestCanonMerge,
    FinalizeBatch
}

public sealed record TaskNodeDefinition(
    string Id,
    TaskKind Kind,
    IReadOnlyList<string> Dependencies,
    string InputArtifactType,
    string OutputArtifactType,
    bool IsHumanGate = false,
    int MaxAttempts = 1);

public sealed class TaskGraph
{
    public TaskGraph(string id, ProductionMode mode, IEnumerable<TaskNodeDefinition> nodes)
    {
        Id = id;
        Mode = mode;
        Nodes = nodes.ToArray();
        Validate();
    }

    public string Id { get; }
    public ProductionMode Mode { get; }
    public IReadOnlyList<TaskNodeDefinition> Nodes { get; }

    private void Validate()
    {
        if (Nodes.Count == 0)
            throw new DomainRuleException("task_graph.empty", "A task graph must contain at least one node.");
        var byId = Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (byId.Count != Nodes.Count)
            throw new DomainRuleException("task_graph.node.duplicate", "Task node ids must be unique.");

        foreach (var node in Nodes)
        {
            if (node.MaxAttempts <= 0)
                throw new DomainRuleException("task_graph.attempts.invalid", "Task attempts must be positive.");
            foreach (var dependency in node.Dependencies)
            {
                if (!byId.ContainsKey(dependency))
                    throw new DomainRuleException("task_graph.dependency.missing", $"Task {node.Id} depends on missing task {dependency}.");
                if (dependency == node.Id)
                    throw new DomainRuleException("task_graph.dependency.self", $"Task {node.Id} cannot depend on itself.");
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
            Visit(node, byId, visited, visiting);

        var acceptance = Nodes.SingleOrDefault(x => x.Kind == TaskKind.AwaitHumanAcceptance);
        var merge = Nodes.SingleOrDefault(x => x.Kind == TaskKind.RequestCanonMerge);
        if (acceptance is null || !acceptance.IsHumanGate || merge is null || !merge.Dependencies.Contains(acceptance.Id))
            throw new DomainRuleException("task_graph.canon_gate.invalid", "Canon merge must depend on a human acceptance gate.");
    }

    private static void Visit(
        TaskNodeDefinition node,
        IReadOnlyDictionary<string, TaskNodeDefinition> byId,
        ISet<string> visited,
        ISet<string> visiting)
    {
        if (visited.Contains(node.Id))
            return;
        if (!visiting.Add(node.Id))
            throw new DomainRuleException("task_graph.cycle", $"Task graph contains a cycle at {node.Id}.");
        foreach (var dependency in node.Dependencies)
            Visit(byId[dependency], byId, visited, visiting);
        visiting.Remove(node.Id);
        visited.Add(node.Id);
    }
}

public static class FirstBatchTaskGraphCompiler
{
    public static TaskGraph Compile(string graphId, ProductionMode mode, int chapterNumber)
    {
        if (chapterNumber <= 0)
            throw new DomainRuleException("task_graph.chapter.invalid", "The chapter number must be positive.");

        var prefix = $"chapter-{chapterNumber}";
        var nodes = new[]
        {
            Node("freeze", TaskKind.FreezeContext, [], "GoalRevision", "FrozenContext"),
            Node("analyze", TaskKind.AnalyzeRequirements, ["freeze"], "FrozenContext", "RequirementAnalysis", maxAttempts: 2),
            Node("plan", TaskKind.PlanBatch, ["analyze"], "RequirementAnalysis", "BatchPlan", maxAttempts: 2),
            Node($"{prefix}-context", TaskKind.CompileChapterContext, ["plan"], "BatchPlan", "ChapterContext"),
            Node($"{prefix}-write", TaskKind.WriteCandidate, [$"{prefix}-context"], "ChapterContext", "CandidateChapter", maxAttempts: 2),
            Node($"{prefix}-continuity", TaskKind.ReviewContinuity, [$"{prefix}-write"], "CandidateChapter", "ContinuityReview", maxAttempts: 2),
            Node($"{prefix}-literary", TaskKind.ReviewLiteraryQuality, [$"{prefix}-write"], "CandidateChapter", "LiteraryReview", maxAttempts: 2),
            Node("accept", TaskKind.AwaitHumanAcceptance, [$"{prefix}-continuity", $"{prefix}-literary"], "ReviewedCandidate", "Acceptance", true),
            Node("merge", TaskKind.RequestCanonMerge, ["accept"], "Acceptance", "CanonMergeRequested"),
            Node("finalize", TaskKind.FinalizeBatch, ["merge"], "CanonMergeResult", "BatchFinalized")
        };
        return new TaskGraph(graphId, mode, nodes);
    }

    private static TaskNodeDefinition Node(
        string id,
        TaskKind kind,
        IReadOnlyList<string> dependencies,
        string input,
        string output,
        bool human = false,
        int maxAttempts = 1) => new(id, kind, dependencies, input, output, human, maxAttempts);
}
