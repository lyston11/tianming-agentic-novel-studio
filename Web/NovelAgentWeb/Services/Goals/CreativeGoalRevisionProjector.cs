using System.Text.Json;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public static class CreativeGoalRevisionProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CreativeGoal Project(CreativeGoal committedGoal, IEnumerable<GoalRevision> revisions)
    {
        var effective = JsonSerializer.Deserialize<CreativeGoal>(
            JsonSerializer.Serialize(committedGoal, JsonOptions),
            JsonOptions) ?? throw new InvalidOperationException("CreativeGoal 无法构造有效修订视图。");
        foreach (var revision in revisions.OrderBy(item => item.RevisionNumber))
            ApplyChanges(effective, revision.ConstraintChangesJson);
        return effective;
    }

    private static void ApplyChanges(CreativeGoal goal, string changesJson)
    {
        var changes = JsonSerializer.Deserialize<GoalConstraintChanges>(changesJson, JsonOptions)
            ?? throw new InvalidOperationException("Goal Revision 约束变化为空。");
        if (changes.HumanReadableObjective != null)
            goal.HumanReadableObjective = RequireText(changes.HumanReadableObjective, "humanReadableObjective");
        if (changes.TargetChapterRangeJson != null)
            goal.TargetChapterRangeJson = RequireText(changes.TargetChapterRangeJson, "targetChapterRangeJson");
        if (changes.SuccessCriteria != null)
            goal.SuccessCriteriaJson = JsonSerializer.Serialize(changes.SuccessCriteria, JsonOptions);
        if (changes.MustPreserve != null)
            goal.MustPreserveJson = JsonSerializer.Serialize(changes.MustPreserve, JsonOptions);
        if (changes.MustHappen != null)
            goal.MustHappenJson = JsonSerializer.Serialize(changes.MustHappen, JsonOptions);
        if (changes.MustNotChange != null)
            goal.MustNotChangeJson = JsonSerializer.Serialize(changes.MustNotChange, JsonOptions);
        if (changes.AcceptancePolicyJson != null)
            goal.AcceptancePolicyJson = RequireText(changes.AcceptancePolicyJson, "acceptancePolicyJson");
        if (changes.ReworkPolicyJson != null)
            goal.ReworkPolicyJson = RequireText(changes.ReworkPolicyJson, "reworkPolicyJson");
        if (changes.TotalCostLimit.HasValue)
            goal.TotalCostLimit = changes.TotalCostLimit.Value > 0
                ? changes.TotalCostLimit.Value
                : throw new InvalidOperationException("Goal Revision 总金额上限必须大于零。");
    }

    private static string RequireText(string value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Goal Revision 字段 {fieldName} 不能为空。")
            : value.Trim();
}
