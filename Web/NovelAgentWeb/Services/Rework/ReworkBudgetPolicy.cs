using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Rework;

public sealed class ReworkBudgetPolicy
{
    public bool CanStartAutomaticAttempt(ReworkIntent intent) =>
        intent.Status == "proposed" && intent.AttemptCount < MaxAttempts(intent.TargetScope);

    public string EvaluateOutcome(
        ReworkIntent intent,
        bool problemImproved,
        bool impactExpanded)
    {
        if (!problemImproved || impactExpanded)
            return "needs_decision";
        return intent.AttemptCount >= MaxAttempts(intent.TargetScope)
            ? "needs_decision"
            : "proposed";
    }

    public string GetPropagation(string impactLevel) => impactLevel switch
    {
        "copy" => "none",
        "local_fact" => "revalidate_following",
        "key_plot" => "revision_plan_required",
        "hard_conflict" => "block_merge",
        _ => throw new ArgumentOutOfRangeException(nameof(impactLevel), "未知的返工影响级别。")
    };

    private static int MaxAttempts(string targetScope) => targetScope switch
    {
        "selection" => 2,
        "chapter" => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(targetScope), "未知的返工目标范围。")
    };
}
