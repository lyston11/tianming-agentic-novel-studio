namespace TM.Web.NovelAgentWeb.Services.Goals;

public interface ICommitmentAssessmentService
{
    Task<CommitmentAssessment> AssessAsync(
        CommitmentAssessmentRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICommitmentAssessmentModelClient
{
    Task<CommitmentAssessment> AssessAsync(
        string userId,
        CommitmentAssessmentRequest request,
        CancellationToken cancellationToken = default);
}
