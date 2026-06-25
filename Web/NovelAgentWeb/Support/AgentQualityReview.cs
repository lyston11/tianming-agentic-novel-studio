namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentQualityReviewSuite
{
    public AgentQualityGateReport Review(
        AgentObservationContext context,
        AgentRuntimeObservation observation,
        AgentQualityGateReport baseGate)
    {
        baseGate ??= new AgentQualityGateReport();
        if (!IsWritingObservation(observation))
            return baseGate;

        var text = BuildReviewText(context, observation, baseGate);
        var reports = new List<AgentReviewerReport>
        {
            ReviewDimension("pacing", text, new[] { "拖沓", "水", "重复", "铺垫过长", "跳跃", "断裂" }, "节奏需要更紧，场景目标和转折要更明确。"),
            ReviewDimension("motivation", text, new[] { "动机不足", "动机不成立", "强行", "工具人", "行为不可信" }, "补足角色选择压力、欲望和代价。"),
            ReviewDimension("conflict", text, new[] { "冲突未推进", "没有冲突", "阻力不足", "无推进", "平" }, "让场景产生明确阻力、升级或不可逆结果。"),
            ReviewDimension("continuity", text, new[] { "连续性冲突", "事实冲突", "设定冲突", "伏笔遗漏", "不一致", "CHANGES", "ShortId" }, "回到事实快照、伏笔账本和 CHANGES 修正连续性。"),
            ReviewDimension("style", text, new[] { "风格偏离", "文风不符", "口吻不对", "读者承诺偏离", "像大纲" }, "按作者偏好和读者承诺重写表达层。"),
        };

        var arbiter = Arbitrate(baseGate, reports, observation);
        baseGate.ReviewReports = reports;
        baseGate.ArbiterDecision = arbiter;
        baseGate.Status = arbiter.Status;
        baseGate.RequiresUserInput = arbiter.RequiresUserInput || baseGate.RequiresUserInput;
        baseGate.Issues = baseGate.Issues.Concat(reports.SelectMany(r => r.Issues)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(12).ToList();
        baseGate.Evidence = baseGate.Evidence.Concat(reports.SelectMany(r => r.Evidence)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(12).ToList();
        baseGate.RewriteDecision = FirstNonEmpty(baseGate.RewriteDecision, arbiter.Reason);
        baseGate.Scores = BuildScores(reports, baseGate.Scores);
        return baseGate;
    }

    private static bool IsWritingObservation(AgentRuntimeObservation observation) =>
        string.Equals(observation.ToolName, "ProduceChapter", StringComparison.OrdinalIgnoreCase) ||
        observation.Phase.Contains("draft", StringComparison.OrdinalIgnoreCase) ||
        observation.Phase.Contains("validated", StringComparison.OrdinalIgnoreCase) ||
        observation.Phase.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static string BuildReviewText(AgentObservationContext context, AgentRuntimeObservation observation, AgentQualityGateReport baseGate)
    {
        var parts = new[]
        {
            context.UserMessage,
            context.ProjectSummary,
            context.MissionPlan.CurrentObjective,
            context.MissionPlan.CurrentNovelGoal,
            observation.Message,
            observation.Artifact?.Summary ?? string.Empty,
            string.Join("；", baseGate.Issues),
            string.Join("；", context.AuthorMemory.StyleLikes),
            string.Join("；", context.AuthorMemory.StyleDislikes),
            string.Join("；", context.Rag.KnowledgeNotes.Take(6)),
        };
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static AgentReviewerReport ReviewDimension(string reviewer, string text, IReadOnlyList<string> failSignals, string advice)
    {
        var hits = failSignals.Where(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)).ToList();
        var status = hits.Count >= 2 ? "fail" : hits.Count == 1 ? "warn" : "pass";
        return new AgentReviewerReport
        {
            Reviewer = reviewer,
            Status = status,
            Score = status == "pass" ? 8 : status == "warn" ? 6 : 3,
            Issues = hits.Select(hit => $"{reviewer}: 命中风险「{hit}」").ToList(),
            Evidence = hits.Count == 0 ? new List<string> { $"{reviewer}: 未发现硬阻塞信号" } : hits,
            RewriteAdvice = status == "pass" ? string.Empty : advice,
            Blocking = status == "fail",
        };
    }

    private static QualityArbiterDecision Arbitrate(
        AgentQualityGateReport baseGate,
        IReadOnlyList<AgentReviewerReport> reports,
        AgentRuntimeObservation observation)
    {
        var blocking = reports.Where(r => r.Blocking).Select(r => r.Reviewer).ToList();
        var warnCount = reports.Count(r => r.Status == "warn");

        if (baseGate.Status is "needs_user_input")
        {
            return new QualityArbiterDecision
            {
                Status = "needs_user_input",
                Reason = "结构化门禁或模型反思要求用户补充输入。",
                RequiresUserInput = true,
                BlockingReviewers = blocking,
            };
        }

        if (baseGate.Status is "fail" or "needs_rewrite" || blocking.Count > 0 || warnCount >= 3)
        {
            var degraded = blocking.Count > 0
                ? blocking
                : reports.Where(r => r.Status == "warn").Select(r => r.Reviewer).ToList();
            return new QualityArbiterDecision
            {
                Status = "needs_rewrite",
                Reason = blocking.Count > 0
                    ? $"质量评审阻塞：{string.Join("、", blocking)}。"
                    : "多个质量维度警告，提交前需要重写或修复。",
                BlockingReviewers = degraded,
            };
        }

        if (observation.Phase.Contains("validated", StringComparison.OrdinalIgnoreCase) && baseGate.Status is "pass" or "warn")
        {
            return new QualityArbiterDecision
            {
                Status = warnCount > 0 ? "warn" : "pass",
                Reason = warnCount > 0 ? "结构门禁通过，质量评审有轻微警告。" : "结构门禁和质量评审均通过。",
            };
        }

        return new QualityArbiterDecision
        {
            Status = baseGate.Status == "not_applicable" ? "not_applicable" : FirstNonEmpty(baseGate.Status, "warn"),
            Reason = "写作中间步骤已完成，等待下一步结构门禁或提交前确认。",
        };
    }

    private static AgentQualityScores BuildScores(IReadOnlyList<AgentReviewerReport> reports, AgentQualityScores existing)
    {
        var byName = reports.ToDictionary(r => r.Reviewer, StringComparer.OrdinalIgnoreCase);
        return new AgentQualityScores
        {
            Pacing = Score("pacing", existing.Pacing),
            CharacterMotivation = Score("motivation", existing.CharacterMotivation),
            Conflict = Score("conflict", existing.Conflict),
            Continuity = Score("continuity", existing.Continuity),
            Prose = Score("style", existing.Prose),
            ReaderPromise = Math.Min(Score("style", existing.ReaderPromise), Score("conflict", existing.ReaderPromise)),
        };

        int Score(string key, int fallback) => byName.TryGetValue(key, out var report)
            ? Math.Clamp(report.Score, 0, 10)
            : Math.Clamp(fallback, 0, 10);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
