namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class TaskGraphValidator
{
    public IReadOnlyList<TaskGraphValidationError> Validate(TaskGraphDefinition graph)
    {
        var errors = new List<TaskGraphValidationError>();
        var nodes = new Dictionary<string, TaskGraphNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (!nodes.TryAdd(node.Id, node))
                errors.Add(new("TASK_GRAPH_DUPLICATE_NODE", $"任务 ID 重复：{node.Id}", node.Id));
            if (node.ExecutionKind == TaskExecutionKind.Kernel && node.AuthorityMutation != AuthorityMutation.None)
                errors.Add(new("KERNEL_AUTHORITY_WRITE_FORBIDDEN", "专业内核不能直接修改权威状态。", node.Id));
        }

        foreach (var node in nodes.Values)
        {
            foreach (var dependency in node.DependsOn)
            {
                if (!nodes.ContainsKey(dependency))
                    errors.Add(new("TASK_GRAPH_MISSING_DEPENDENCY", $"依赖任务不存在：{dependency}", node.Id));
            }
        }

        var order = TopologicalOrder(nodes, errors);
        if (order.Count == nodes.Count)
        {
            ValidateArtifacts(nodes, order, errors);
            ValidateChapterReviews(nodes, errors);
        }

        if (!nodes.Values.Any(node => node.TaskType == BookProductionWorkflow.AcceptanceGate))
            errors.Add(new("TASK_GRAPH_ACCEPTANCE_REQUIRED", "任务图缺少验收门。"));
        if (!nodes.Values.Any(node => node.TaskType == "PrefixMerge"))
            errors.Add(new("TASK_GRAPH_PREFIX_MERGE_REQUIRED", "任务图缺少连续前缀合并节点。"));

        return errors;
    }

    public void ValidateOrThrow(TaskGraphDefinition graph)
    {
        var errors = Validate(graph);
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            errors.Select(error => $"{error.Code}: {error.Message}")));
    }

    private static List<string> TopologicalOrder(
        IReadOnlyDictionary<string, TaskGraphNode> nodes,
        ICollection<TaskGraphValidationError> errors)
    {
        var indegree = nodes.ToDictionary(pair => pair.Key, _ => 0, StringComparer.Ordinal);
        var dependents = nodes.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            foreach (var dependency in node.DependsOn.Where(nodes.ContainsKey))
            {
                indegree[node.Id]++;
                dependents[dependency].Add(node.Id);
            }
        }

        var ready = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var order = new List<string>(nodes.Count);
        while (ready.TryDequeue(out var current))
        {
            order.Add(current);
            foreach (var dependent in dependents[current])
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                    ready.Enqueue(dependent);
            }
        }

        if (order.Count != nodes.Count)
            errors.Add(new("TASK_GRAPH_CYCLE", "任务图包含循环依赖。"));
        return order;
    }

    private static void ValidateArtifacts(
        IReadOnlyDictionary<string, TaskGraphNode> nodes,
        IReadOnlyList<string> order,
        ICollection<TaskGraphValidationError> errors)
    {
        var availableByNode = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var nodeId in order)
        {
            var node = nodes[nodeId];
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in node.DependsOn)
            {
                if (availableByNode.TryGetValue(dependency, out var dependencyArtifacts))
                    available.UnionWith(dependencyArtifacts);
                if (nodes.TryGetValue(dependency, out var dependencyNode))
                    available.UnionWith(dependencyNode.ProducesArtifacts);
            }

            foreach (var required in node.RequiresArtifacts.Where(required => !available.Contains(required)))
                errors.Add(new("TASK_GRAPH_MISSING_ARTIFACT", $"缺少所需产物：{required}", node.Id));
            available.UnionWith(node.ProducesArtifacts);
            availableByNode[node.Id] = available;
        }
    }

    private static void ValidateChapterReviews(
        IReadOnlyDictionary<string, TaskGraphNode> nodes,
        ICollection<TaskGraphValidationError> errors)
    {
        foreach (var write in nodes.Values.Where(node => node.TaskType == "WriteCandidate"))
        {
            var chapter = write.ChapterNumber;
            var hasContinuity = nodes.Values.Any(node =>
                node.ChapterNumber == chapter && node.TaskType == "ReviewContinuity");
            var hasLiterary = nodes.Values.Any(node =>
                node.ChapterNumber == chapter && node.TaskType == "ReviewLiteraryQuality");
            if (!hasContinuity || !hasLiterary)
                errors.Add(new("CHAPTER_REVIEW_REQUIRED", "候选章节缺少连续性或审美审稿。", write.Id));
        }
    }
}
