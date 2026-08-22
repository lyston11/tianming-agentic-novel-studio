namespace TM.Web.NovelAgentWeb.Services.Rework;

public sealed record CompileReworkIntentRequest(
    string CandidateChapterId,
    int CandidateVersion,
    string SessionId,
    string UserDescription,
    int? SelectionStart = null,
    int? SelectionEnd = null,
    string SelectedText = "");

public sealed record ReworkIntentCompilationContext(
    string UserId,
    string ProjectId,
    string GoalId,
    string BranchId,
    string CandidateChapterId,
    int CandidateVersion,
    string CandidateContent,
    string UserDescription,
    int? SelectionStart,
    int? SelectionEnd,
    string SelectedText);

public sealed record ReworkIntentDraft(
    string TargetScope,
    string Problem,
    string DesiredEffect,
    IReadOnlyList<string> Preserve,
    IReadOnlyList<string> MayChange,
    IReadOnlyList<string> MustNotChange,
    IReadOnlyList<string> AcceptanceCriteria,
    string ImpactLevel,
    string ImpactAssessment);

public interface IReworkIntentModelClient
{
    Task<ReworkIntentDraft> CompileAsync(
        ReworkIntentCompilationContext context,
        CancellationToken cancellationToken = default);
}
