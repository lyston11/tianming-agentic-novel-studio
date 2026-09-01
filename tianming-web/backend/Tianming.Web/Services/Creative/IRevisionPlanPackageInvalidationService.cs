namespace TM.Web.NovelAgentWeb.Services.Creative;

public interface IRevisionPlanPackageInvalidationService
{
    Task<RevisionPlanPackageInvalidationResult> InvalidateAsync(
        InvalidateRevisionPlanPackagesRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InvalidateRevisionPlanPackagesRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string RuntimeRunId,
    string RevisionPlanId);

public sealed class RevisionPlanPackageInvalidationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RevisionPlanId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> AffectedChapterIds { get; set; } = new();
    public List<string> InvalidatedPackageIds { get; set; } = new();
    public List<RevisionPlanInvalidatedPackageItem> Packages { get; set; } = new();
}

public sealed class RevisionPlanInvalidatedPackageItem
{
    public string PackageId { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
}
