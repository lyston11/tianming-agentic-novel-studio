namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionDependencyGuard
{
    Task<IReadOnlyList<ProductionDependencyBlock>> FindBlocksAsync(
        ProductionDependencyGuardRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ProductionDependencyGuardRequest(
    string UserId,
    string ProjectId,
    int TargetChapterNumber);

public sealed class ProductionDependencyBlock
{
    public string Code { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string TargetChapterId { get; init; } = string.Empty;
    public int TargetChapterNumber { get; init; }
    public string PreviousChapterId { get; init; } = string.Empty;
    public int PreviousChapterNumber { get; init; }
    public IReadOnlyList<string> OutboxEventIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutboxEventTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
    public bool Recoverable { get; init; } = true;
    public string RecommendedToolName { get; init; } = "QueryNovelProductionState";
    public Dictionary<string, string> RecommendedArguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
