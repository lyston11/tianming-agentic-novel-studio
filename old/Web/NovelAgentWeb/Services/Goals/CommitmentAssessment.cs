namespace TM.Web.NovelAgentWeb.Services.Goals;

public enum DialogueCommitmentState
{
    Exploring,
    Proposed,
    Committed,
    Revising,
    Cancelled
}

public enum GoalAuthorizationKind
{
    None,
    UnambiguousLanguage,
    ConfirmedContract,
    ExplicitAction
}

public sealed record DialogueMessage(string Role, string Content);

public sealed record CommitmentAssessment(
    DialogueCommitmentState State,
    GoalAuthorizationKind Authorization,
    double Confidence,
    bool RequiresConfirmation,
    string Rationale,
    CreativeGoalContract? ProposedContract);

public sealed record CommitmentAssessmentRequest(
    string ProjectId,
    string CollaborationMode,
    IReadOnlyList<DialogueMessage> Dialogue,
    string AcceptedDecisionsJson,
    string ProjectStateJson,
    bool ExplicitExecutionAction,
    CreativeGoalContract? ProposedContract);

public sealed record DirectorTurnView(
    string ProjectId,
    DialogueCommitmentState State,
    GoalAuthorizationKind Authorization,
    bool RequiresConfirmation,
    string Rationale,
    CreativeGoalContract? ProposedContract);
