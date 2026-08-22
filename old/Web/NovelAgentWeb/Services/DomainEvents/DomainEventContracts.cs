using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.DomainEvents;

public sealed record KernelArtifactProposal(
    string ArtifactType,
    int SchemaVersion,
    string ContentJson,
    string ContentHash,
    string Authorship,
    bool IsProtected);

public sealed record DomainEventProposal(
    string AggregateType,
    string AggregateId,
    long AggregateVersion,
    string EventType,
    IReadOnlyList<string> ArtifactRefs,
    IReadOnlyList<string> EvidenceRefs,
    string PayloadJson,
    string IdempotencyKey);

public sealed record DomainAdoptionResult(
    IReadOnlyList<string> ArtifactIds,
    IReadOnlyList<string> EventIds);

public interface IDomainReducer
{
    Task<DomainAdoptionResult> ApplyAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        IReadOnlyList<DomainEventProposal> events,
        CancellationToken cancellationToken = default);
}

public interface IKernelArtifactStore
{
    Task<IReadOnlyList<string>> AddOrReuseAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> proposals,
        CancellationToken cancellationToken = default);
}
