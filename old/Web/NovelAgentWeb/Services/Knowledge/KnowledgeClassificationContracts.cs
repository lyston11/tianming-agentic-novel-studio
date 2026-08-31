namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeClassificationRequest(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    string? SessionId = null,
    string? RunId = null);

public sealed record KnowledgeClassificationPrompt(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    string ProjectTitle,
    string KnowledgeTitle,
    string KnowledgeEntryType,
    string KnowledgeContent,
    int KnowledgeWeight);

public sealed class KnowledgeClassificationDecision
{
    public string Model { get; set; } = "";
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = "";
    public string PackagePolicy { get; set; } = "";
    public List<string> TargetEntities { get; set; } = new();
    public string Rule { get; set; } = "";
    public bool ShouldEnterGate { get; set; }
    public bool ShouldEnterBlueprint { get; set; }
    public bool ShouldEnterFactSnapshot { get; set; }
    public double Confidence { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class KnowledgeClassificationResult
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string KnowledgeId { get; set; } = "";
    public string Model { get; set; } = "";
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = "";
    public string PackagePolicy { get; set; } = "";
    public IReadOnlyList<string> TargetEntities { get; set; } = Array.Empty<string>();
    public string Rule { get; set; } = "";
    public bool ShouldEnterGate { get; set; }
    public bool ShouldEnterBlueprint { get; set; }
    public bool ShouldEnterFactSnapshot { get; set; }
    public double Confidence { get; set; }
}

public interface IKnowledgeClassificationModelClient
{
    Task<KnowledgeClassificationDecision> ClassifyAsync(
        KnowledgeClassificationPrompt prompt,
        CancellationToken ct = default);
}

public interface IKnowledgeClassificationService
{
    Task<KnowledgeClassificationResult> ClassifyAndApplyAsync(
        KnowledgeClassificationRequest request,
        CancellationToken ct = default);
}

public sealed record KnowledgeStoryBiblePromotionRequest(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    string KnowledgeTitle,
    string KnowledgeEntryType,
    string ClassificationId,
    string Model,
    string Role,
    string Scope,
    string ConstraintLevel,
    string PackagePolicy,
    string Rule,
    IReadOnlyList<string> TargetEntities,
    bool ShouldEnterGate,
    bool ShouldEnterBlueprint,
    bool ShouldEnterFactSnapshot,
    double Confidence,
    string? SessionId,
    string? RunId);

public interface IKnowledgeStoryBiblePromotionService
{
    Task PromoteAsync(KnowledgeStoryBiblePromotionRequest request, CancellationToken ct = default);
}
