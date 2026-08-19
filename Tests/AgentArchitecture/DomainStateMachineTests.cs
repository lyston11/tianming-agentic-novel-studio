using Tianming.NovelAgent.Domain.Common;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;

namespace Tests.AgentArchitecture;

public sealed class DomainStateMachineTests
{
    [Fact]
    public void Goal_requires_a_confirmed_proposal_and_keeps_revision_snapshot()
    {
        var proposal = NewProposal();
        proposal.Propose();
        var revision = NewRevision(proposal);

        var goal = CreativeGoal.Confirm("goal-1", proposal, revision);

        Assert.Equal(GoalProposalStatus.Confirmed, proposal.Status);
        Assert.Equal(CreativeGoalStatus.Confirmed, goal.Status);
        Assert.Same(revision, goal.CurrentRevision);
        goal.Activate();
        goal.Complete();
        Assert.Equal(CreativeGoalStatus.Completed, goal.Status);
        Assert.Throws<DomainRuleException>(goal.Activate);
    }

    [Theory]
    [InlineData(ProductionMode.SingleChapter)]
    [InlineData(ProductionMode.InteractiveBatch)]
    [InlineData(ProductionMode.AutonomousBook)]
    public void Every_mode_uses_the_same_typed_first_batch_protocol(ProductionMode mode)
    {
        var graph = FirstBatchTaskGraphCompiler.Compile("graph", mode, 1);

        Assert.Equal(
            Enum.GetValues<TaskKind>(),
            graph.Nodes.Select(x => x.Kind));
        Assert.True(graph.Nodes.Single(x => x.Kind == TaskKind.AwaitHumanAcceptance).IsHumanGate);
        Assert.Contains(
            graph.Nodes.Single(x => x.Kind == TaskKind.AwaitHumanAcceptance).Id,
            graph.Nodes.Single(x => x.Kind == TaskKind.RequestCanonMerge).Dependencies);
    }

    [Fact]
    public void Task_graph_rejects_cycles_and_missing_dependencies()
    {
        var nodesWithCycle = new[]
        {
            new TaskNodeDefinition("a", TaskKind.FreezeContext, ["b"], "in", "out"),
            new TaskNodeDefinition("b", TaskKind.AwaitHumanAcceptance, ["a"], "in", "accepted", true),
            new TaskNodeDefinition("merge", TaskKind.RequestCanonMerge, ["b"], "accepted", "merged")
        };
        var nodesWithMissingDependency = new[]
        {
            new TaskNodeDefinition("a", TaskKind.FreezeContext, ["missing"], "in", "out"),
            new TaskNodeDefinition("accept", TaskKind.AwaitHumanAcceptance, ["a"], "in", "accepted", true),
            new TaskNodeDefinition("merge", TaskKind.RequestCanonMerge, ["accept"], "accepted", "merged")
        };

        Assert.Equal("task_graph.cycle", Assert.Throws<DomainRuleException>(() => new TaskGraph("g", ProductionMode.SingleChapter, nodesWithCycle)).Code);
        Assert.Equal("task_graph.dependency.missing", Assert.Throws<DomainRuleException>(() => new TaskGraph("g", ProductionMode.SingleChapter, nodesWithMissingDependency)).Code);
    }

    [Fact]
    public void Production_requires_a_valid_canon_lease_before_merge()
    {
        var now = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        var production = NewProduction();
        production.Start();
        production.ReachAcceptanceGate();

        var expired = new CanonWriteLease("worker", now, 1);
        Assert.Equal(
            "production.canon_lease.invalid",
            Assert.Throws<DomainRuleException>(() => production.AcceptPrefix(expired, "worker", now)).Code);

        production.AcceptPrefix(new CanonWriteLease("worker", now.AddMinutes(1), 2), "worker", now);
        Assert.Equal(ProductionStatus.MergingCanon, production.Status);
        production.CanonMerged(false);
        Assert.Equal(ProductionStatus.Completed, production.Status);
    }

    [Fact]
    public void Kernel_task_retries_within_budget_and_rejects_lost_lease()
    {
        var now = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        var task = new KernelTask(
            "task",
            new TaskNodeDefinition("task", TaskKind.WriteCandidate, [], "context", "candidate", MaxAttempts: 2));

        task.MarkReady();
        task.Claim("worker", now.AddMinutes(1), now);
        Assert.Throws<DomainRuleException>(() => task.Start("other", now));
        task.Start("worker", now);
        task.Fail("worker", now, retryable: true);
        Assert.Equal(KernelTaskStatus.Ready, task.Status);

        task.Claim("worker", now.AddMinutes(1), now);
        task.Start("worker", now);
        task.Fail("worker", now, retryable: true);
        Assert.Equal(KernelTaskStatus.Failed, task.Status);
    }

    private static GoalProposal NewProposal() =>
        new("proposal-1", "user-1", "project-1", "session-1", NewContract());

    private static GoalContract NewContract() =>
        new(
            "Write the first chapter",
            ProductionMode.InteractiveBatch,
            new ChapterRange(1, 1),
            ["The chapter has a complete dramatic turn"],
            ["Existing protagonist identity"],
            ["Introduce the inciting incident"],
            ["Do not alter protected canon"],
            "human",
            "directed",
            10m,
            "canon-v1",
            "knowledge-v1",
            "quality-v1",
            "style-v1",
            new Dictionary<string, string> { ["writing"] = "model-v1" },
            new Dictionary<string, string> { ["agent"] = "protocol-v1" });

    private static GoalRevision NewRevision(GoalProposal proposal) =>
        new(
            "revision-1",
            1,
            proposal.Contract,
            new FrozenContextReference(
                "context-1",
                "hash",
                "canon-v1",
                "knowledge-v1",
                "style-v1",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>()),
            new GoalConfirmation("user-1", DateTimeOffset.UtcNow, "confirm-1", proposal.SourceSessionId, proposal.Id),
            "contract-hash",
            "1",
            "initial",
            null);

    private static Production NewProduction() =>
        new(
            "production-1",
            "user-1",
            "project-1",
            "goal-1",
            "revision-1",
            ProductionMode.InteractiveBatch,
            FirstBatchTaskGraphCompiler.Compile("graph-1", ProductionMode.InteractiveBatch, 1));
}
