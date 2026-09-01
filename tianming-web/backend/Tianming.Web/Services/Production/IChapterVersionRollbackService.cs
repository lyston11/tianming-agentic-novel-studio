namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterVersionRollbackService
{
    Task<RollbackChapterVersionResult> RollbackAsync(
        RollbackChapterVersionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RollbackChapterVersionRequest(
    string UserId,
    string ProjectId,
    string ChapterId,
    string TargetVersionId,
    string RuntimeRunId,
    string? Reason,
    string? IdempotencyKey = null);

public sealed class RollbackChapterVersionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = string.Empty;
    public int CurrentVersionNumber { get; set; }
    public string CurrentDocumentId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public List<string> InvalidatedPackageIds { get; set; } = new();
}
