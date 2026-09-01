using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Infrastructure;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tests.AgentArchitecture;

public sealed class LegacyRecoveryApplicationServiceTests
{
    [Fact]
    public async Task Recovery_creates_an_idempotent_proposal_from_safe_snapshot_data()
    {
        var snapshots = new StubSnapshotReader();
        var store = new CapturingConversationStore();
        var service = CreateService(snapshots, store);
        var request = NewRequest();

        var first = await service.CreateProposalAsync("user-1", "project-1", request);
        var duplicate = await service.CreateProposalAsync("user-1", "project-1", request);

        Assert.Equal(first.ProposalId, duplicate.ProposalId);
        Assert.Equal(first.ContractHash, duplicate.ContractHash);
        Assert.Equal(first.CorrelationId, duplicate.CorrelationId);
        Assert.Equal(first.Evidence.FormalChapterVersionIds, duplicate.Evidence.FormalChapterVersionIds);
        Assert.Equal(2, snapshots.CallCount);
        Assert.Single(store.SavedProposals);
        var proposal = store.SavedProposals[0];
        Assert.Equal(GoalProposalStatus.Proposed, proposal.Status);
        Assert.Equal("session-recovery", proposal.SourceSessionId);
        Assert.Equal("canon-v7", proposal.Contract.CanonBaselineVersion);
        Assert.Equal("knowledge-v4", proposal.Contract.KnowledgeSnapshotVersion);
        Assert.Equal(["chapter-version-1"], first.Evidence.FormalChapterVersionIds);
    }

    [Fact]
    public async Task Recovery_rejects_legacy_execution_state_before_reading_the_snapshot()
    {
        var snapshots = new StubSnapshotReader();
        var service = CreateService(snapshots, new CapturingConversationStore());
        var request = NewRequest() with { RuntimeRunId = "legacy-run" };

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateProposalAsync("user-1", "project-1", request));

        Assert.Contains("cannot be restored", error.Message);
        Assert.Equal(0, snapshots.CallCount);
    }

    private static LegacyRecoveryApplicationService CreateService(
        StubSnapshotReader snapshots,
        CapturingConversationStore store) => new(
            snapshots,
            store,
            new CapturingEventWriter(),
            new InlineUnitOfWork(),
            new SequentialIds(),
            new Sha256ContractHasher(),
            new FixedClock());

    private static CreateLegacyRecoveryProposalRequest NewRequest() => new(
        "session-recovery",
        "recover-1",
        "Continue the retained novel from chapter two",
        NovelAgentProductionMode.InteractiveBatch,
        2,
        2,
        ["Preserve continuity and finish a dramatic turn"],
        ["Retained chapter one"]);

    private sealed class StubSnapshotReader : ILegacyProjectSnapshotReader
    {
        public int CallCount { get; private set; }

        public Task<LegacyProjectSnapshot> ReadRecoverableSnapshotAsync(
            string userId,
            string projectId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new LegacyProjectSnapshot(
                projectId,
                ["chapter-version-1"],
                ["decision-1"],
                ["knowledge-1"],
                "canon-v7",
                "knowledge-v4",
                "quality-v1",
                "style-v2",
                new Dictionary<string, string> { ["writing"] = "model-v3" },
                new Dictionary<string, string> { ["agent"] = "protocol-v1" }));
        }
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        private readonly Dictionary<string, ConversationTurnResult> _results = [];
        public List<GoalProposal> SavedProposals { get; } = [];

        public Task<ConversationTurnResult?> FindTurnResultAsync(
            string userId,
            string sessionId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(_results.GetValueOrDefault($"{userId}:{sessionId}:{idempotencyKey}"));

        public Task SaveTurnAsync(
            string userId,
            string? projectId,
            string sessionId,
            string idempotencyKey,
            string userMessageId,
            string userMessage,
            string assistantMessageId,
            ConversationRuntimeResult runtimeResult,
            GoalProposal? proposal,
            ConversationTurnResult result,
            CancellationToken cancellationToken)
        {
            _results.Add($"{userId}:{sessionId}:{idempotencyKey}", result);
            if (proposal is not null)
                SavedProposals.Add(proposal);
            return Task.CompletedTask;
        }

        public Task<GoalProposal?> GetProposalAsync(
            string userId,
            string proposalId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateProposalAsync(
            GoalProposal proposal,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingEventWriter : IAgentEventWriter
    {
        public Task AppendAsync(
            AgentDomainEvent domainEvent,
            AgentStreamKind streamKind,
            string streamId,
            object publicData,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineUnitOfWork : IAgentUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;
        public string NewId() => $"id-{Interlocked.Increment(ref _next):D4}";
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    }
}
