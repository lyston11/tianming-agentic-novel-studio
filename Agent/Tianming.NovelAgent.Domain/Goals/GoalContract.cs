using Tianming.NovelAgent.Domain.Common;

namespace Tianming.NovelAgent.Domain.Goals;

public enum ProductionMode
{
    SingleChapter,
    InteractiveBatch,
    AutonomousBook
}

public sealed record ChapterRange(int Start, int End)
{
    public int Count => End - Start + 1;

    public void Validate()
    {
        if (Start <= 0 || End < Start)
            throw new DomainRuleException("goal.chapter_range.invalid", "The chapter range must be positive and ordered.");
    }
}

public sealed record GoalContract(
    string Objective,
    ProductionMode Mode,
    ChapterRange ChapterRange,
    IReadOnlyList<string> SuccessCriteria,
    IReadOnlyList<string> MustPreserve,
    IReadOnlyList<string> MustHappen,
    IReadOnlyList<string> MustNotChange,
    string AcceptancePolicy,
    string ReworkPolicy,
    decimal TotalCostLimit,
    string CanonBaselineVersion,
    string KnowledgeSnapshotVersion,
    string QualityContractVersion,
    string StyleProfileVersion,
    IReadOnlyDictionary<string, string> ModelVersions,
    IReadOnlyDictionary<string, string> ProtocolVersions)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Objective))
            throw new DomainRuleException("goal.objective.required", "A goal objective is required.");
        ChapterRange.Validate();
        if (SuccessCriteria.Count == 0)
            throw new DomainRuleException("goal.success_criteria.required", "At least one success criterion is required.");
        if (TotalCostLimit < 0)
            throw new DomainRuleException("goal.cost_limit.invalid", "The cost limit cannot be negative.");
        if (string.IsNullOrWhiteSpace(CanonBaselineVersion))
            throw new DomainRuleException("goal.canon_baseline.required", "A canon baseline is required.");
    }
}

public sealed record FrozenContextReference(
    string ContextId,
    string ContentHash,
    string CanonBaselineVersion,
    string KnowledgeSnapshotVersion,
    string StyleProfileVersion,
    IReadOnlyDictionary<string, string> ModelVersions,
    IReadOnlyDictionary<string, string> PromptVersions,
    IReadOnlyDictionary<string, string> SchemaVersions,
    IReadOnlyDictionary<string, string> ProtocolVersions);
