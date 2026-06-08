using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public enum TurnIntentType
{
    FreeChat,
    StatusQuery,
    Confirmation,
    Cancel,
    NewProjectSeed,
    CreativeBrief,
    ContinueMission,
    RevisionRequest,
    UserFeedback,
    ProjectSwitch,
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
    public string Source { get; set; } = "rule";
}

public sealed class ConversationKernel
{
    public TurnIntent Classify(AgentSession session, string userMessage)
    {
        var raw = userMessage ?? string.Empty;
        var msg = raw.Trim();
        var normalized = msg.ToLowerInvariant();
        var intent = new TurnIntent
        {
            RawMessage = raw,
            Confidence = 0.72,
            Source = "conversation_kernel",
        };

        if (IsCancel(normalized))
            return Fill(intent, TurnIntentType.Cancel, "cancel", confidence: 0.95);

        if (IsConfirmation(normalized))
            return Fill(intent, TurnIntentType.Confirmation, "confirmation", confidence: 0.95);

        if (IsStatusQuery(normalized))
            return Fill(intent, TurnIntentType.StatusQuery, "status_query", confidence: 0.9);

        if (IsContinue(normalized))
            return Fill(intent, TurnIntentType.ContinueMission, "continue_mission", confidence: 0.86);

        if (TryResolveCandidateSelection(session, msg, intent, out var selectionIntent))
            return selectionIntent;

        if (IsProjectSwitch(normalized))
            return Fill(intent, TurnIntentType.ProjectSwitch, "project_switch", confidence: 0.78);

        if (IsRevisionRequest(normalized))
            return Fill(intent, TurnIntentType.RevisionRequest, "revision_request", msg, confidence: 0.82);

        if (IsUserFeedback(normalized))
            return Fill(intent, TurnIntentType.UserFeedback, "user_feedback", msg, confidence: 0.78);

        if (IsNewProjectSeed(normalized, session))
            return Fill(intent, TurnIntentType.NewProjectSeed, "new_project_seed", msg, confidence: 0.8);

        if (LooksLikeCreativeBrief(normalized))
            return Fill(intent, TurnIntentType.CreativeBrief, "creative_brief", msg, confidence: 0.78);

        return Fill(intent, TurnIntentType.FreeChat, "free_chat", confidence: 0.65);
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
            ConfirmationDecision = intent.Type switch
            {
                TurnIntentType.Confirmation => "confirm",
                TurnIntentType.Cancel => "cancel",
                _ => string.Empty,
            },
            ReferencedTask = activeTask?.TaskId ?? string.Empty,
            TargetArtifact = ResolveTargetArtifact(plan, intent, activeTask),
            StatusQueryScope = ResolveStatusScope(intent.RawMessage),
            FeedbackPatch = intent.Type is TurnIntentType.RevisionRequest or TurnIntentType.UserFeedback
                ? intent.RawMessage.Trim()
                : string.Empty,
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
        intent.CreativeBrief = type is TurnIntentType.CreativeBrief or TurnIntentType.NewProjectSeed
            ? creativeBrief
            : string.Empty;
        intent.Confidence = confidence;
        return intent;
    }

    private static DialogueAct MapDialogueAct(TurnIntentType type) => type switch
    {
        TurnIntentType.StatusQuery => DialogueAct.AskStatus,
        TurnIntentType.Confirmation => DialogueAct.Confirm,
        TurnIntentType.Cancel => DialogueAct.Cancel,
        TurnIntentType.NewProjectSeed => DialogueAct.StartProject,
        TurnIntentType.CreativeBrief => DialogueAct.ProvideBrief,
        TurnIntentType.ContinueMission => DialogueAct.ContinueTask,
        TurnIntentType.RevisionRequest => DialogueAct.ReviseArtifact,
        TurnIntentType.UserFeedback => DialogueAct.GiveFeedback,
        TurnIntentType.ProjectSwitch => DialogueAct.SwitchProject,
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
        if (intent.Type is TurnIntentType.StatusQuery or TurnIntentType.RevisionRequest or TurnIntentType.UserFeedback)
            return FirstNonEmpty(plan.ArtifactCursor, plan.ActiveArtifactCursor, activeTask?.RunId, plan.CurrentRunId);
        return FirstNonEmpty(activeTask?.RunId, plan.CurrentRunId);
    }

    private static string ResolveStatusScope(string raw)
    {
        var msg = raw.Trim().ToLowerInvariant();
        if (ContainsAny(msg, "章节", "章", "草稿", "正文", "那章", "刚才那章")) return "chapter";
        if (ContainsAny(msg, "项目", "新书", "小说", "书")) return "project";
        if (ContainsAny(msg, "任务", "进度", "状态")) return "mission";
        return "general";
    }

    private static bool IsConfirmation(string msg) =>
        msg is "确认" or "确定" or "同意" or "好" or "好的" or "可以" or "ok" or "yes" or "行" or "继续执行" ||
        ContainsAny(msg, "确认执行", "确认提交", "提交吧", "就这样", "就选", "用这个", "按推荐");

    private static bool IsCancel(string msg) =>
        msg is "取消" or "不" or "不要" or "停" or "先不" or "算了" or "no" ||
        ContainsAny(msg, "取消", "先不要", "先别", "不要执行", "不提交", "暂停");

    private static bool IsStatusQuery(string msg) =>
        ContainsAny(msg, "准备好了吗", "写完了吗", "生成了吗", "在哪", "哪里", "进度", "状态", "怎么样了", "刚才那章", "刚才生成", "草稿呢", "那章呢", "到哪了");

    private static bool IsContinue(string msg) =>
        msg is "继续" or "下一步" or "接着" or "往下" or "继续推进" ||
        ContainsAny(msg, "继续推进", "接着来", "下一步");

    private static bool IsProjectSwitch(string msg) =>
        ContainsAny(msg, "切换到", "换到", "打开项目", "打开小说");

    private static bool IsRevisionRequest(string msg) =>
        ContainsAny(msg, "重写", "改写", "修改", "润色", "修一下", "调整", "按我刚才说的改");

    private static bool IsUserFeedback(string msg) =>
        ContainsAny(msg, "我不喜欢", "我喜欢", "以后", "记住", "偏好", "不要这样", "这种风格");

    private static bool IsNewProjectSeed(string msg, AgentSession session)
    {
        if (session.Phase == "awaiting_user_foundation") return false;
        return ContainsAny(msg, "新小说", "新书", "开一本", "写一本", "创建一本", "我要写") &&
               ContainsAny(msg, "小说", "故事", "书");
    }

    private static bool LooksLikeCreativeBrief(string msg) =>
        ContainsAny(msg, "写", "生成", "规划", "章节", "卷纲", "角色", "设定", "剧情", "冲突", "主角", "反派", "场景", "大纲");

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
        "PlanChapter", "SelectChapterCandidate", "BuildChapterContextPackage",
        "GenerateChapterWithChanges", "ValidateChapterDraft", "RepairChapterDraft",
        "CommitValidatedChapter", "RefreshProjectIndexes", "AnalyzeDependencyImpact", "ReviewChapter",
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
            "QueryProjectStatus" or "SearchCreativeKnowledge" or "StartNewNovelProject" => ToolPolicyResult.Allow(),
            "PlanStoryFoundation" => ToolPolicyResult.Allow(),  // LLM decided, trust it
            "CommitStoryFoundation" => PolicyCommitStoryFoundation(call, session, bible, confirmed),
            "PlanVolumeArc" => PolicyPlanVolumeArc(bible),  // Only structural check
            "CommitVolumeArc" => AllowAutopilot("High", "提交卷规划。"),
            "PlanChapter" => PolicyPlanChapter(call),  // Only structural check
            "SelectChapterCandidate" => PolicyRunExists(call, session, bible, "选择章节候选需要已有章节 Run。"),
            "BuildChapterContextPackage" => PolicyBuildContext(call, session, bible),
            "GenerateChapterWithChanges" => PolicyGenerateDraft(call, session, bible, confirmed),
            "ValidateChapterDraft" => PolicyValidateDraft(call, session, bible),
            "RepairChapterDraft" => PolicyRepairDraft(call, session, bible, confirmed),
            "CommitValidatedChapter" => PolicyCommitChapter(call, session, bible, context, confirmed),
            "RefreshProjectIndexes" or "AnalyzeDependencyImpact" or "ReviewChapter" => ToolPolicyResult.Allow("Medium"),
            _ => ToolPolicyResult.Block($"未知工具：{name}。请使用已注册的工具。"),
        };
    }

    // PlanVolumeArc: only check structural prerequisite (Story Bible must exist)
    private static ToolPolicyResult PolicyPlanVolumeArc(StoryBibleDocument bible)
    {
        if (bible.Constitution == null)
            return ToolPolicyResult.Block("Story Bible 尚未固化，不能规划卷纲。应先补齐并确认故事地基。");
        return ToolPolicyResult.Allow();
    }

    // PlanChapter: only check parameter validity
    private static ToolPolicyResult PolicyPlanChapter(AgentToolCall call)
    {
        if (call.Arguments.ContainsKey("userGoal"))
            return ToolPolicyResult.Block("PlanChapter 已废弃 userGoal 参数。必须使用 creativeBrief/sourceTurnId。");
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
            call.Arguments["selectedMacroCandidateTitle"] = candidate.Title;
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

            if (selectedIndex >= 1 && selectedIndex <= run.MacroCandidates.Count)
                return run.MacroCandidates[selectedIndex - 1];

            error = $"待确认的故事地基候选序号 {selectedIndex} 超出当前 run 的候选范围，请重新选择候选。";
            return null;
        }

        if (call.Arguments.TryGetValue("selectedMacroCandidateTitle", out var selectedTitle) &&
            !string.IsNullOrWhiteSpace(selectedTitle))
        {
            var candidate = run.MacroCandidates.FirstOrDefault(c =>
                string.Equals(c.Title, selectedTitle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (candidate != null) return candidate;
            error = $"待确认的故事地基候选「{selectedTitle}」不在当前 run 中，我不会固化。请重新选择候选。";
            return null;
        }

        return null;
    }

    private static ToolPolicyResult PolicyBuildContext(AgentToolCall call, AgentSession session, StoryBibleDocument bible)
    {
        var run = FindRun(call, session, bible);
        var chapter = FindChapterTask(session.WorkingMemory.MissionPlan, run);
        if (chapter?.Status == "committed")
            return ToolPolicyResult.Block("已提交章节不能自动重建写作上下文，只能复盘或分析依赖影响。");
        if (run?.ChapterBrief == null)
            return ToolPolicyResult.RepairableBlock(
                "构建上下文包需要先有章节候选。",
                "PlanChapter",
                BuildRecommendedArgs(call, run, session, creativeBrief: FirstNonEmpty(session.WorkingMemory.CurrentGoal, session.WorkingMemory.MissionPlan.CurrentObjective, "继续当前章节规划")),
                "chapter_candidates",
                "现在还不能构建上下文包，因为这一章还没有候选。我会先规划章节候选。");
        if (string.IsNullOrWhiteSpace(run.ChapterBrief.SelectedCandidateTitle))
            return ToolPolicyResult.RepairableBlock(
                "构建上下文包前必须先选定章节候选。",
                "SelectChapterCandidate",
                BuildRecommendedArgs(call, run, session),
                "chapter_candidate_selection",
                "现在还不能构建上下文包，因为章节候选还没选定。我会先选择推荐候选。");
        return ToolPolicyResult.Allow();
    }

    private static ToolPolicyResult PolicyGenerateDraft(AgentToolCall call, AgentSession session, StoryBibleDocument bible, bool confirmed)
    {
        var run = FindRun(call, session, bible);
        if (run?.ContextPackage == null || run.ContextPackage.Status is not ("ready" or "context_ready"))
            return ToolPolicyResult.RepairableBlock(
                "生成正文前必须先构建章节上下文包。",
                "BuildChapterContextPackage",
                BuildRecommendedArgs(call, run, session),
                "chapter_context_package",
                "现在还不能生成正文，因为缺章节上下文包。我会先构建上下文包。");
        var chapter = FindChapterTask(session.WorkingMemory.MissionPlan, run);
        if (chapter?.RequiresContextRebuild == true || chapter?.Status == "needs_context_rebuild")
            return ToolPolicyResult.RepairableBlock(
                "章节上下文受依赖影响已过期，必须先重新构建上下文包。",
                "BuildChapterContextPackage",
                BuildRecommendedArgs(call, run, session),
                "chapter_context_package",
                "章节上下文已经过期。我会先重新构建上下文包。");
        if (chapter?.Status == "committed")
            return ToolPolicyResult.Block("已提交章节不能自动重新生成正文，只能复盘或分析依赖影响。");
        return AllowAutopilot("High", "生成章节草稿和 CHANGES。");
    }

    private static ToolPolicyResult PolicyValidateDraft(AgentToolCall call, AgentSession session, StoryBibleDocument bible)
    {
        var run = FindRun(call, session, bible);
        var chapter = FindChapterTask(session.WorkingMemory.MissionPlan, run);
        if (chapter?.Status == "committed")
            return ToolPolicyResult.Block("已提交章节不能自动重新校验并覆盖状态，只能复盘或分析依赖影响。");
        if (chapter?.RequiresContextRebuild == true)
            return ToolPolicyResult.RepairableBlock(
                "章节上下文已过期，需要先重建上下文包。",
                "BuildChapterContextPackage",
                BuildRecommendedArgs(call, run, session),
                "chapter_context_package",
                "章节上下文已经过期。我会先重建上下文包，再继续校验。");
        if (run?.DraftArtifact == null || string.IsNullOrWhiteSpace(run.DraftArtifact.DraftContent))
            return ToolPolicyResult.RepairableBlock(
                "校验章节前必须先生成草稿和 CHANGES。",
                "GenerateChapterWithChanges",
                BuildRecommendedArgs(call, run, session),
                "chapter_draft",
                "现在还不能校验章节，因为还没有草稿。我会先进入正文生成确认。");
        return ToolPolicyResult.Allow("Medium");
    }

    private static ToolPolicyResult PolicyRepairDraft(AgentToolCall call, AgentSession session, StoryBibleDocument bible, bool confirmed)
    {
        var run = FindRun(call, session, bible);
        var gateFailed = run?.GateReport?.Status is "failed" or "gate_failed";
        var chapter = FindChapterTask(session.WorkingMemory.MissionPlan, run);
        if (chapter?.Status == "committed")
            return ToolPolicyResult.Block("已提交章节不能自动修复或覆盖正文，只能复盘或分析依赖影响。");
        var qualityFailed = chapter?.QualityStatus is "quality_failed" or "blocked" || chapter?.Status == "quality_failed";
        if (!gateFailed && !qualityFailed)
            return ToolPolicyResult.Block("修复草稿需要已有 GenerationGate 或质量门禁失败项。");
        return AllowAutopilot("High", "修复章节草稿。");
    }

    private static ToolPolicyResult PolicyCommitChapter(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        AgentObservationContext context,
        bool confirmed)
    {
        var run = FindRun(call, session, bible);
        if (run?.GateReport?.Status != "validated")
            return ToolPolicyResult.RepairableBlock(
                "提交成稿需要 GenerationGate validated。",
                "ValidateChapterDraft",
                BuildRecommendedArgs(call, run, session),
                "generation_gate",
                "现在还不能提交成稿，因为章节还没有通过门禁校验。我会先校验草稿。");

        var chapter = FindChapterTask(context.MissionPlan, run);
        if (chapter?.RequiresRevalidation == true || chapter?.Status == "needs_revalidation")
            return ToolPolicyResult.RepairableBlock(
                "章节受依赖影响需要重新校验，不能提交成稿。",
                "ValidateChapterDraft",
                BuildRecommendedArgs(call, run, session),
                "generation_gate",
                "章节受依赖影响，需要先重新校验。");
        if (chapter?.RequiresContextRebuild == true || chapter?.Status == "needs_context_rebuild")
            return ToolPolicyResult.RepairableBlock(
                "章节上下文已过期，需要重建上下文后才能提交。",
                "BuildChapterContextPackage",
                BuildRecommendedArgs(call, run, session),
                "chapter_context_package",
                "章节上下文已过期，需要先重建上下文。");
        if (chapter != null && chapter.QualityStatus is not ("quality_passed" or "quality_warn"))
            return ToolPolicyResult.RepairableBlock(
                "质量门禁未通过，不能提交成稿。",
                "ValidateChapterDraft",
                BuildRecommendedArgs(call, run, session),
                "quality_gate",
                "质量门禁还没通过，我会先重新校验草稿状态。");
        if (chapter != null && !string.IsNullOrWhiteSpace(chapter.QualityIssueSummary))
            return ToolPolicyResult.Block("质量门禁仍有问题，不能提交成稿。");

        return AllowAutopilot("High", "提交已校验章节进书城。");
    }

    private static ToolPolicyResult PolicyRunExists(AgentToolCall call, AgentSession session, StoryBibleDocument bible, string message) =>
        FindRun(call, session, bible) == null ? ToolPolicyResult.Block(message) : ToolPolicyResult.Allow("Medium");

    private static Dictionary<string, string> BuildRecommendedArgs(
        AgentToolCall call,
        NovelAgentRun? run,
        AgentSession session,
        string creativeBrief = "")
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var runId = FirstNonEmpty(Arg(call, "runId"), run?.RunId, session.ActiveRunId);
        if (!string.IsNullOrWhiteSpace(runId))
            args["runId"] = runId;

        if (!string.IsNullOrWhiteSpace(creativeBrief))
            args["creativeBrief"] = creativeBrief;

        var chapterId = FirstNonEmpty(run?.TargetChapterId, run?.ChapterBrief?.ChapterId);
        if (!string.IsNullOrWhiteSpace(chapterId))
            args["chapterId"] = chapterId;

        return args;
    }

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static ToolPolicyResult RequireConfirmation(bool confirmed, string message) =>
        AllowAutopilot("High", message);

    private static ToolPolicyResult AllowAutopilot(string risk, string message = "") =>
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
