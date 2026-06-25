using System.Text.RegularExpressions;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public enum TurnIntentType
{
    FreeChat,
    CandidateSelection,
}

public sealed class TurnIntent
{
    public TurnIntentType Type { get; set; } = TurnIntentType.FreeChat;
    public string Label { get; set; } = "free_chat";
    public string RawMessage { get; set; } = string.Empty;
    public string CreativeBrief { get; set; } = string.Empty;
    public string ReferencedProjectId { get; set; } = string.Empty;
    public string ReferencedRunId { get; set; } = string.Empty;
    public string ReferencedChapterId { get; set; } = string.Empty;
    public string SelectedOption { get; set; } = string.Empty;
    public int? SelectedOptionIndex { get; set; }
    public string SelectionKind { get; set; } = string.Empty;
    public List<string> Constraints { get; set; } = new();
    public double Confidence { get; set; } = 0.7;
    public string Source { get; set; } = "conversation_context";
}

public sealed class ConversationKernel
{
    public TurnIntent Classify(AgentSession session, string userMessage)
    {
        var raw = userMessage ?? string.Empty;
        var msg = raw.Trim();
        var intent = new TurnIntent
        {
            RawMessage = raw,
            ReferencedChapterId = AgentChapterReferenceResolver.ResolveChapterId(raw),
            Confidence = 0.72,
            Source = "conversation_kernel",
        };

        if (TryResolveCandidateSelection(session, msg, intent, out var selectionIntent))
            return selectionIntent;

        if (HasProject(session) && LooksLikeProductionStatusQuestion(msg))
        {
            var statusIntent = Fill(intent, TurnIntentType.FreeChat, "status_query", confidence: 0.86);
            statusIntent.SelectionKind = "production";
            return statusIntent;
        }

        return Fill(intent, TurnIntentType.FreeChat, "free_chat", confidence: 0.72);
    }

    public UserTurnEnvelope BuildEnvelope(AgentSession session, string userMessage)
    {
        var intent = Classify(session, userMessage);
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        var activeTask = FindActiveTask(plan);
        var envelope = new UserTurnEnvelope
        {
            Intent = intent,
            DialogueAct = MapDialogueAct(intent.Type),
            CreativeBrief = intent.CreativeBrief,
            ConfirmationDecision = string.Empty,
            ReferencedTask = activeTask?.TaskId ?? string.Empty,
            TargetArtifact = ResolveTargetArtifact(plan, intent, activeTask),
            StatusQueryScope = intent.Label == "status_query" ? FirstNonEmpty(intent.SelectionKind, "production") : string.Empty,
            FeedbackPatch = string.Empty,
            SelectedOption = intent.SelectedOption,
            SelectedOptionIndex = intent.SelectedOptionIndex,
            SelectionKind = intent.SelectionKind,
        };
        if (intent.Type == TurnIntentType.CandidateSelection)
        {
            envelope.TargetArtifact = FirstNonEmpty(intent.ReferencedRunId, envelope.TargetArtifact);
            envelope.ReferencedTask = FirstNonEmpty(envelope.ReferencedTask, activeTask?.TaskId);
        }

        plan.TurnIntent = intent;
        plan.InteractionState = envelope;
        plan.ActiveTurnId = envelope.TurnId;
        plan.ActiveToolTransactionId = plan.ActiveToolTransactionId;
        plan.UpdatedAt = DateTime.UtcNow;
        return envelope;
    }

    private static TurnIntent Fill(
        TurnIntent intent,
        TurnIntentType type,
        string label,
        string creativeBrief = "",
        double confidence = 0.7)
    {
        intent.Type = type;
        intent.Label = label;
        intent.CreativeBrief = string.Empty;
        intent.Confidence = confidence;
        return intent;
    }

    private static DialogueAct MapDialogueAct(TurnIntentType type) => type switch
    {
        TurnIntentType.CandidateSelection => DialogueAct.SelectCandidate,
        _ => DialogueAct.Chat,
    };

    private static AgentScheduledTask? FindActiveTask(AgentMissionPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.SchedulerState.ActiveTaskId))
        {
            var active = plan.SchedulerState.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, plan.SchedulerState.ActiveTaskId, StringComparison.OrdinalIgnoreCase));
            if (active != null) return active;
        }

        return plan.SchedulerState.Tasks.FirstOrDefault(t =>
                   t.Status is "running" or "queued" or "waiting_confirmation") ??
               plan.SchedulerState.Tasks.LastOrDefault();
    }

    private static string ResolveTargetArtifact(AgentMissionPlan plan, TurnIntent intent, AgentScheduledTask? activeTask)
    {
        if (!string.IsNullOrWhiteSpace(intent.ReferencedRunId)) return intent.ReferencedRunId;
        if (!string.IsNullOrWhiteSpace(intent.ReferencedChapterId)) return intent.ReferencedChapterId;
        return FirstNonEmpty(activeTask?.RunId, plan.CurrentRunId);
    }

    private static bool TryResolveCandidateSelection(
        AgentSession session,
        string message,
        TurnIntent intent,
        out TurnIntent selectionIntent)
    {
        selectionIntent = intent;
        if (!TryParseOption(message, out var optionIndex, out var selectedOption))
            return false;

        var plan = session.WorkingMemory.MissionPlan;
        var phase = session.Phase ?? string.Empty;
        var selectionKind = ResolveSelectionKind(session, plan);
        if (string.IsNullOrWhiteSpace(selectionKind))
            return false;

        selectionIntent = Fill(intent, TurnIntentType.CandidateSelection, "candidate_selection", confidence: 0.92);
        selectionIntent.SelectedOptionIndex = optionIndex;
        selectionIntent.SelectedOption = selectedOption;
        selectionIntent.SelectionKind = selectionKind;
        selectionIntent.ReferencedRunId = FirstNonEmpty(
            session.ActiveRunId,
            plan.CurrentRunId,
            plan.ArtifactCursor,
            plan.ActiveArtifactCursor,
            FindActiveTask(plan)?.RunId);
        selectionIntent.ReferencedChapterId = FirstNonEmpty(plan.ActiveChapterId, FindActiveTask(plan)?.ChapterId);
        return true;

        static string ResolveSelectionKind(AgentSession session, AgentMissionPlan plan)
        {
            var phase = session.Phase ?? string.Empty;
            if (phase.Contains("foundation_candidates", StringComparison.OrdinalIgnoreCase) ||
                phase.Contains("foundation", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(FirstNonEmpty(plan.Status, plan.Stage, plan.BookTaskTree.Foundation.Status), "candidate", "候选"))
                return "story_foundation_candidate";

            if (phase.Contains("chapter_candidates", StringComparison.OrdinalIgnoreCase) ||
                ContainsAny(FirstNonEmpty(plan.Status, plan.Stage, plan.LifecycleStatus), "chapter_candidates", "candidate_selected") ||
                plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters).Any(c =>
                    c.CandidateStatus is "candidates_ready" or "candidate_selected" ||
                    c.Status is "candidates_ready" or "candidate_selected"))
                return "chapter_candidate";

            if (plan.ActiveArtifacts.Any(a =>
                    a.ArtifactType.Contains("story_foundation_candidates", StringComparison.OrdinalIgnoreCase)))
                return "story_foundation_candidate";

            if (plan.ActiveArtifacts.Any(a =>
                    a.ArtifactType.Contains("chapter_candidates", StringComparison.OrdinalIgnoreCase)))
                return "chapter_candidate";

            return string.Empty;
        }
    }

    private static bool HasProject(AgentSession session) =>
        !string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
        !session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeProductionStatusQuestion(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var text = message.Trim();
        if (ContainsAny(text, "不要现在执行", "先不要执行", "不要执行"))
            return false;
        if (LooksLikeCompoundMutationRequest(text))
            return false;

        var asksState = ContainsAny(text,
            "执行到哪",
            "执行了吗",
            "还在执行",
            "正在执行",
            "正在写",
            "是不是正在",
            "当前状态",
            "现在状态",
            "什么状态",
            "进度",
            "卡在哪",
            "卡住",
            "跑到哪",
            "做到哪",
            "有没有开始",
            "是否还在",
            "是不是还在");
        if (!asksState)
            return false;

        var productionScope = ContainsAny(text,
            "执行",
            "生产",
            "生成",
            "写",
            "章节",
            "第",
            "工具",
            "任务",
            "后台",
            "门禁",
            "提交",
            "书城",
            "工作流");

        return productionScope || text.Length <= 18;
    }

    private static bool LooksLikeCompoundMutationRequest(string text)
    {
        var hasSequence = ContainsAny(text, "然后", "并", "再", "之后");
        if (!hasSequence)
            return false;

        return ContainsAny(text,
            "覆盖",
            "修改",
            "修订",
            "重写",
            "重新",
            "创建",
            "生成",
            "生产",
            "写",
            "提交",
            "推进",
            "修成",
            "改成");
    }

    private static bool TryParseOption(string message, out int optionIndex, out string selectedOption)
    {
        selectedOption = message.Trim();
        optionIndex = 0;
        if (selectedOption.Length == 0 || selectedOption.Length > 2)
            return false;

        if (int.TryParse(selectedOption, out var n) && n > 0 && n <= 9)
        {
            optionIndex = n;
            return true;
        }

        var c = char.ToUpperInvariant(selectedOption[0]);
        if (selectedOption.Length == 1 && c is >= 'A' and <= 'D')
        {
            optionIndex = c - 'A' + 1;
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

internal static class AgentChapterReferenceResolver
{
    private static readonly Regex CanonicalChapterRegex = new(
        @"\bchapter[-_\s]*0*(\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChineseChapterRegex = new(
        @"第\s*([0-9０-９一二三四五六七八九十百零〇两]+)\s*章",
        RegexOptions.Compiled);

    public static string ResolveChapterId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var canonical = CanonicalChapterRegex.Match(raw);
        if (canonical.Success && int.TryParse(canonical.Groups[1].Value, out var canonicalNumber))
            return FormatChapterId(canonicalNumber);

        var chinese = ChineseChapterRegex.Match(raw);
        if (chinese.Success && TryParseChapterNumber(chinese.Groups[1].Value, out var chineseNumber))
            return FormatChapterId(chineseNumber);

        return string.Empty;
    }

    public static string NormalizeChapterId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var resolved = ResolveChapterId(value);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var trimmed = value.Trim();
        return trimmed.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : string.Empty;
    }

    private static string FormatChapterId(int number) =>
        number > 0 ? $"chapter-{number:000}" : string.Empty;

    private static bool TryParseChapterNumber(string raw, out int number)
    {
        var normalized = NormalizeNumberText(raw);
        if (int.TryParse(normalized, out number))
            return number > 0;

        number = ParseChineseNumber(normalized);
        return number > 0;
    }

    private static string NormalizeNumberText(string raw)
    {
        var chars = raw.Trim().Select(ch =>
        {
            if (ch is >= '０' and <= '９')
                return (char)('0' + ch - '０');
            return ch switch
            {
                '〇' => '零',
                '两' => '二',
                _ => ch,
            };
        });
        return new string(chars.ToArray());
    }

    private static int ParseChineseNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var hundredIndex = text.IndexOf('百');
        if (hundredIndex >= 0)
        {
            var hundreds = hundredIndex == 0 ? 1 : ChineseDigit(text[..hundredIndex]);
            var rest = text[(hundredIndex + 1)..];
            return hundreds <= 0 ? 0 : hundreds * 100 + ParseChineseTens(rest);
        }

        return ParseChineseTens(text);
    }

    private static int ParseChineseTens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var tenIndex = text.IndexOf('十');
        if (tenIndex >= 0)
        {
            var tens = tenIndex == 0 ? 1 : ChineseDigit(text[..tenIndex]);
            var onesText = text[(tenIndex + 1)..];
            var ones = string.IsNullOrWhiteSpace(onesText) ? 0 : ChineseDigit(onesText);
            return tens <= 0 || ones < 0 ? 0 : tens * 10 + ones;
        }

        return ChineseDigit(text);
    }

    private static int ChineseDigit(string text) => text.Trim() switch
    {
        "零" => 0,
        "一" => 1,
        "二" => 2,
        "三" => 3,
        "四" => 4,
        "五" => 5,
        "六" => 6,
        "七" => 7,
        "八" => 8,
        "九" => 9,
        _ => -1,
    };
}

public sealed class ReflectionEngine
{
    private readonly AgentPlanner _planner;
    private readonly AgentQualityReviewSuite _reviewSuite;

    public ReflectionEngine(AgentPlanner planner, AgentQualityReviewSuite reviewSuite)
    {
        _planner = planner;
        _reviewSuite = reviewSuite;
    }

    public async Task<AgentReflection> ReflectAsync(
        AgentObservationContext context,
        AgentRuntimeObservation observation,
        CancellationToken ct)
    {
        var reflection = await _planner.ReflectAsync(context, observation, ct).ConfigureAwait(false);
        NormalizeReflection(reflection, observation);
        reflection.QualityGate = _reviewSuite.Review(context, observation, reflection.QualityGate);
        NormalizeReflection(reflection, observation);
        return reflection;
    }

    private static void NormalizeReflection(AgentReflection reflection, AgentRuntimeObservation observation)
    {
        reflection.QualityGate ??= new AgentQualityGateReport();
        var gate = reflection.QualityGate;
        gate.Status = NormalizeQualityStatus(gate.Status);
        gate.Scores ??= new AgentQualityScores();
        ClampScores(gate.Scores);

        if (gate.Evidence.Count == 0 && gate.Status != "not_applicable")
        {
            gate.Evidence.Add(observation.ToolName);
            gate.Evidence.Add(observation.Phase);
        }

        // Quality reflection is a judge. Tool planning remains in AgentPlanner + ToolPolicyEngine.
        if (gate.Status == "needs_user_input")
            reflection.RequiresUserInput = true;

        reflection.MissionPatch ??= new AgentMissionPatch();
        if (reflection.MissionPatch.ChapterPatches.Count == 0 && !string.IsNullOrWhiteSpace(observation.Artifact?.ArtifactId))
        {
            reflection.MissionPatch.ChapterPatches.Add(new AgentChapterTaskPatch
            {
                ChapterId = observation.Artifact.ArtifactId,
                Status = gate.Status switch
                {
                    "pass" or "warn" when observation.Phase == "validated" => "quality_passed",
                    "fail" or "needs_rewrite" => "quality_failed",
                    "needs_user_input" => "blocked",
                    _ => string.Empty,
                },
                GateStatus = observation.Phase is "validated" or "gate_failed" or "failed" ? observation.Phase : string.Empty,
                QualityIssueSummary = gate.Issues.Count == 0 ? string.Empty : string.Join("；", gate.Issues.Take(3)),
                NextAction = string.Empty,
            });
        }
    }

    private static string NormalizeQualityStatus(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "pass" or "passed" or "quality_passed" => "pass",
            "warn" or "warning" or "quality_warn" => "warn",
            "fail" or "failed" or "quality_failed" => "fail",
            "rewrite" or "needs_rewrite" => "needs_rewrite",
            "needs_user_input" or "blocked" => "needs_user_input",
            _ => "not_applicable",
        };

    private static void ClampScores(AgentQualityScores scores)
    {
        scores.Pacing = Math.Clamp(scores.Pacing, 0, 10);
        scores.CharacterMotivation = Math.Clamp(scores.CharacterMotivation, 0, 10);
        scores.Conflict = Math.Clamp(scores.Conflict, 0, 10);
        scores.Continuity = Math.Clamp(scores.Continuity, 0, 10);
        scores.Prose = Math.Clamp(scores.Prose, 0, 10);
        scores.ReaderPromise = Math.Clamp(scores.ReaderPromise, 0, 10);
    }
}

public sealed class ToolPolicyResult
{
    public bool AllowsExecution { get; set; } = true;
    public bool RequiresConfirmation { get; set; }
    public string Risk { get; set; } = "Low";
    public string Message { get; set; } = string.Empty;
    public AgentAction? ReplacementAction { get; set; }
    public bool IsRepairable { get; set; }
    public string RecommendedToolName { get; set; } = string.Empty;
    public Dictionary<string, string> RecommendedArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string MissingPrerequisite { get; set; } = string.Empty;
    public string UserFacingMessage { get; set; } = string.Empty;

    public static ToolPolicyResult Allow(string risk = "Low", bool requiresConfirmation = false, string message = "") =>
        new() { AllowsExecution = true, Risk = risk, RequiresConfirmation = requiresConfirmation, Message = message };

    public static ToolPolicyResult Block(string message) =>
        new() { AllowsExecution = false, Message = message };

    public static ToolPolicyResult Replace(AgentAction action, string message = "") =>
        new() { AllowsExecution = false, ReplacementAction = action, Message = message };

    public static ToolPolicyResult RepairableBlock(
        string message,
        string recommendedToolName,
        Dictionary<string, string>? recommendedArguments = null,
        string missingPrerequisite = "",
        string userFacingMessage = "") =>
        new()
        {
            AllowsExecution = false,
            Message = message,
            IsRepairable = true,
            RecommendedToolName = recommendedToolName,
            RecommendedArguments = recommendedArguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = missingPrerequisite,
            UserFacingMessage = string.IsNullOrWhiteSpace(userFacingMessage) ? message : userFacingMessage,
            ReplacementAction = string.IsNullOrWhiteSpace(recommendedToolName)
                ? null
                : new AgentAction
                {
                    Type = AgentActionType.ToolCall,
                    Intent = "repair_prerequisite",
                    ToolCall = new AgentToolCall
                    {
                        Name = recommendedToolName,
                        Arguments = recommendedArguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    },
                    Reply = string.IsNullOrWhiteSpace(userFacingMessage) ? message : userFacingMessage,
                    Source = "policy_repair",
                },
        };
}

public sealed class ToolPolicyEngine
{
    private static readonly HashSet<string> CreativeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlanStoryFoundation", "CommitStoryFoundation", "PlanVolumeArc", "CommitVolumeArc",
        "PlanChapter", "SelectChapterCandidate", "ProduceChapter",
        "AuditCommittedChapter", "ReviseCommittedChapter",
        "RefreshProjectIndexes", "AnalyzeDependencyImpact", "ReviewChapter",
    };

    public ToolPolicyResult BeforeCall(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        AgentObservationContext context,
        bool confirmed)
    {
        var name = call.Name.Trim();

        return name switch
        {
            "tool_search" => ToolPolicyResult.Allow(),
            "AuditCommittedChapter" => ToolPolicyResult.Allow("Medium"),
            "ReviseCommittedChapter" => RequireConfirmation(confirmed, "修订已提交章节并覆盖书城正文。"),
            "PlanStoryFoundation" => ToolPolicyResult.Allow(),  // LLM decided, trust it
            "CommitStoryFoundation" => PolicyCommitStoryFoundation(call, session, bible, confirmed),
            "PlanVolumeArc" => PolicyPlanVolumeArc(call, bible),
            "CommitVolumeArc" => AllowAgentAutoProceed("High", "提交卷规划。"),
            "PlanChapter" => PolicyPlanChapter(call, session, bible, context),
            "SelectChapterCandidate" => PolicyRunExists(call, session, bible, "选择章节候选需要已有章节 Run。"),
            "ProduceChapter" => PolicyProduceChapter(call, session, bible),
            "RefreshProjectIndexes" or "AnalyzeDependencyImpact" or "ReviewChapter" => ToolPolicyResult.Allow("Medium"),
            _ => AllowKnownNonCreativeTool(name, context) ?? ToolPolicyResult.Block($"未知工具：{name}。请使用已注册的工具。"),
        };
    }

    private static ToolPolicyResult? AllowKnownNonCreativeTool(string name, AgentObservationContext context)
    {
        var tool = context.AvailableTools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tool == null || CreativeTools.Contains(name))
            return null;

        return ToolPolicyResult.Allow(
            string.IsNullOrWhiteSpace(tool.Risk) ? "Low" : tool.Risk,
            tool.RequiresConfirmation);
    }

    private static ToolPolicyResult PolicyPlanVolumeArc(AgentToolCall call, StoryBibleDocument bible)
    {
        if (bible.Constitution == null)
            return ToolPolicyResult.Block("Story Bible 尚未固化，不能规划卷纲。应先补齐并确认故事地基。");
        if (call.Arguments.ContainsKey("userGoal"))
            return ToolPolicyResult.Block("PlanVolumeArc 已废弃 userGoal 参数。必须使用 creativeBrief/sourceTurnId。");
        if (string.IsNullOrWhiteSpace(Arg(call, "creativeBrief")))
            return ToolPolicyResult.Block("PlanVolumeArc 需要 creativeBrief。Agent 必须先根据上下文自主整理卷级创作简报，Runtime 不会代写。");
        if (string.IsNullOrWhiteSpace(Arg(call, "candidateDirections")))
            return ToolPolicyResult.Block("PlanVolumeArc 需要 candidateDirections。Agent 必须先给出结构化卷级候选方向，Runtime 不会从用户原话或 Story Bible 里推断。");
        return ToolPolicyResult.Allow();
    }

    private static ToolPolicyResult PolicyPlanChapter(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        AgentObservationContext context)
    {
        if (call.Arguments.ContainsKey("userGoal"))
            return ToolPolicyResult.Block("PlanChapter 已废弃 userGoal 参数。必须使用 creativeBrief/sourceTurnId。");
        if (string.IsNullOrWhiteSpace(Arg(call, "creativeBrief")))
            return ToolPolicyResult.Block("PlanChapter 需要 creativeBrief。Agent 必须先根据上下文自主整理章节创作简报，Runtime 不会代写。");
        if (string.IsNullOrWhiteSpace(Arg(call, "candidateDirections")))
            return ToolPolicyResult.Block("PlanChapter 需要 candidateDirections。Agent 必须先给出结构化章节候选方向，Runtime 不会从用户原话里推断。");
        return ToolPolicyResult.Allow("Medium");
    }

    private static ToolPolicyResult PolicyCommitStoryFoundation(AgentToolCall call, AgentSession session, StoryBibleDocument bible, bool confirmed)
    {
        var run = FindRun(call, session, bible);
        if (run == null)
            return ToolPolicyResult.Block("这个待确认动作绑定的 run 不在当前项目 Story Bible 中，我不会执行。请重新生成候选或回到正确项目继续。");

        if (run.StoryConstitution == null)
            return ToolPolicyResult.Block("这个待确认动作的故事地基候选还没有可提交的创意宪法，我不会固化。请重新生成候选或先补齐故事地基。");

        var candidate = ResolveMacroCandidate(call, run, out var selectionError);
        if (!string.IsNullOrWhiteSpace(selectionError))
            return ToolPolicyResult.Block(selectionError);

        if (candidate != null)
        {
            if (!string.IsNullOrWhiteSpace(candidate.CandidateId))
                call.Arguments["selectedMacroCandidateId"] = candidate.CandidateId;
        }

        return ToolPolicyResult.Allow("High");
    }

    private static MacroStoryConceptCandidate? ResolveMacroCandidate(
        AgentToolCall call,
        NovelAgentRun run,
        out string error)
    {
        error = string.Empty;
        if (run.MacroCandidates.Count == 0)
            return null;

        if (call.Arguments.TryGetValue("selectedMacroCandidateId", out var selectedId) &&
            !string.IsNullOrWhiteSpace(selectedId))
        {
            var candidate = run.MacroCandidates.FirstOrDefault(c =>
                string.Equals(c.CandidateId, selectedId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (candidate != null) return candidate;
            error = $"待确认的故事地基候选 ID「{selectedId}」不在当前 run 中，我不会固化。请重新选择候选。";
            return null;
        }

        if (call.Arguments.TryGetValue("selectedMacroCandidateIndex", out var selectedIndexText) &&
            !string.IsNullOrWhiteSpace(selectedIndexText))
        {
            if (!int.TryParse(selectedIndexText.Trim(), out var selectedIndex))
            {
                error = $"待确认的故事地基候选序号「{selectedIndexText}」不是有效数字，请重新选择候选。";
                return null;
            }

            if (selectedIndex == 0)
                return run.MacroCandidates[0];

            if (selectedIndex >= 1 && selectedIndex <= run.MacroCandidates.Count)
                return run.MacroCandidates[selectedIndex - 1];

            error = $"待确认的故事地基候选序号 {selectedIndex} 超出当前 run 的候选范围，请重新选择候选。";
            return null;
        }

        return null;
    }

    private static ToolPolicyResult PolicyProduceChapter(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var run = FindRun(call, session, bible);
        var chapter = FindChapterTask(session.WorkingMemory.MissionPlan, run);
        if (chapter?.Status == "committed")
            return ToolPolicyResult.Block("已提交章节不能通过 ProduceChapter 重新生产；请使用 ReviseCommittedChapter 走已提交章节修订流程。");

        return AllowAgentAutoProceed("High", "执行章节生产闭环。");
    }

    private static ToolPolicyResult PolicyRunExists(AgentToolCall call, AgentSession session, StoryBibleDocument bible, string message) =>
        FindRun(call, session, bible) == null ? ToolPolicyResult.Block(message) : ToolPolicyResult.Allow("Medium");

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static ToolPolicyResult RequireConfirmation(bool confirmed, string message) =>
        confirmed
            ? ToolPolicyResult.Allow("High", requiresConfirmation: false, message)
            : ToolPolicyResult.Allow("High", requiresConfirmation: true, message);

    private static ToolPolicyResult AllowAgentAutoProceed(string risk, string message = "") =>
        ToolPolicyResult.Allow(risk, requiresConfirmation: false, message);

    private static NovelAgentRun? FindRun(AgentToolCall call, AgentSession session, StoryBibleDocument bible)
    {
        var runId = call.Arguments.TryGetValue("runId", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : session.ActiveRunId ?? string.Empty;
        return string.IsNullOrWhiteSpace(runId)
            ? AgentRunSelector.SelectCurrentRun(bible)
            : bible.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private static AgentChapterTask? FindChapterTask(AgentMissionPlan plan, NovelAgentRun? run)
    {
        if (run == null) return null;
        return plan.BookTaskTree.Volumes
            .SelectMany(v => v.Chapters)
            .FirstOrDefault(c =>
                string.Equals(c.RunId, run.RunId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(run.TargetChapterId) &&
                 string.Equals(c.ChapterId, run.TargetChapterId, StringComparison.OrdinalIgnoreCase)));
    }

}
