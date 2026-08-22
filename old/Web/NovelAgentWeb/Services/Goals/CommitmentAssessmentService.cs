using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class CommitmentAssessmentService : ICommitmentAssessmentService
{
    private readonly ICommitmentAssessmentModelClient _model;
    private readonly ICurrentUserService _currentUser;

    public CommitmentAssessmentService(
        ICommitmentAssessmentModelClient model,
        ICurrentUserService currentUser)
    {
        _model = model;
        _currentUser = currentUser;
    }

    public async Task<CommitmentAssessment> AssessAsync(
        CommitmentAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExplicitExecutionAction)
        {
            if (request.ProposedContract == null)
                throw new InvalidOperationException("明确执行动作必须关联可展示的 CreativeGoal 合同。");

            return new CommitmentAssessment(
                DialogueCommitmentState.Committed,
                GoalAuthorizationKind.ExplicitAction,
                1,
                false,
                "用户通过执行控件明确授权。",
                request.ProposedContract);
        }

        var assessment = await _model.AssessAsync(
            _currentUser.GetUserId(),
            request,
            cancellationToken);

        return assessment with { Confidence = Math.Clamp(assessment.Confidence, 0, 1) };
    }
}
