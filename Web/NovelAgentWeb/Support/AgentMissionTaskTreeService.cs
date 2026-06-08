using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentMissionTaskTreeService
{
    public void Sync(
        AgentSession session,
        NovelProjectInfo project,
        StoryBibleDocument bible,
        AgentToolExecutionResult? result = null,
        AgentAction? action = null,
        AgentReflection? reflection = null)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        EnsurePlanIdentity(plan, session, project, bible);
        SyncFoundation(plan, bible);
        SyncVolumesAndRuns(plan, bible);
        ApplyToolResult(plan, session, result, action);
        ApplyReflection(plan, reflection);
        SyncAllowedActions(plan, bible);
        SyncScheduler(plan, session, project);
        SyncVerifiedState(plan, session);
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsurePlanIdentity(AgentMissionPlan plan, AgentSession session, NovelProjectInfo project, StoryBibleDocument bible)
    {
        if (string.IsNullOrWhiteSpace(plan.MissionId))
            plan.MissionId = Guid.NewGuid().ToString("N");
        plan.BlackboardVersion = Math.Max(plan.BlackboardVersion, 2);
        plan.ProjectId = project.Id;
        plan.ProjectTitle = project.Title;
        plan.CurrentRunId = session.ActiveRunId ?? AgentRunSelector.SelectCurrentRun(bible)?.RunId ?? plan.CurrentRunId;
        plan.CurrentObjective = FirstNonEmpty(plan.CurrentObjective, session.WorkingMemory.CurrentGoal, bible.Constitution?.CoreHook, project.CoreHook);
        plan.BookTaskTree ??= new AgentBookTaskTree();
        plan.BookTaskTree.BookId = string.IsNullOrWhiteSpace(plan.BookTaskTree.BookId) ? plan.MissionId : plan.BookTaskTree.BookId;
        plan.BookTaskTree.ProjectId = project.Id;
        plan.BookTaskTree.Title = project.Title;
        if (string.IsNullOrWhiteSpace(plan.BookTaskTree.CurrentFocus))
            plan.BookTaskTree.CurrentFocus = plan.CurrentRunId;
        if (string.IsNullOrWhiteSpace(plan.OverallGoal))
            plan.OverallGoal = project.CoreHook;
        if (string.IsNullOrWhiteSpace(plan.CurrentNovelGoal))
            plan.CurrentNovelGoal = bible.Constitution?.CoreHook ?? project.CoreHook;
        if (plan.Milestones.Count == 0)
            plan.Milestones.AddRange(new[] { "故事地基", "卷规划", "章节规划", "上下文包", "草稿生成", "质量门禁", "成稿入库" });
        PruneResolvedDependencyImpacts(plan);
    }

    private static void SyncFoundation(AgentMissionPlan plan, StoryBibleDocument bible)
    {
        var foundation = plan.BookTaskTree.Foundation ??= new AgentFoundationTask();
        if (bible.Constitution == null)
        {
            foundation.Status = "unstarted";
        }
        else
        {
            foundation.Status = "confirmed";
            foundation.ConfirmedAt ??= DateTime.UtcNow;
            AddUnique(foundation.Decisions, $"核心钩子：{bible.Constitution.CoreHook}");
            AddUnique(foundation.Decisions, $"读者承诺：{bible.Constitution.ReaderPromise}");
        }
        foundation.UpdatedAt = DateTime.UtcNow;
    }

    private static void SyncVolumesAndRuns(AgentMissionPlan plan, StoryBibleDocument bible)
    {
        foreach (var arc in bible.VolumeArcs)
        {
            var volume = EnsureVolume(plan, arc.VolumeId, arc.Title);
            volume.Goal = FirstNonEmpty(arc.VolumePromise, arc.CoreQuestion, volume.Goal);
            volume.StartChapterId = arc.StartChapterId;
            volume.EndChapterId = arc.EndChapterId;
            volume.Status = arc.Status == VolumeArcStatus.Canon ? "active" : "planned";
            foreach (var beat in arc.ChapterBeats)
            {
                var chapterId = BuildChapterId(arc.StartChapterId, beat.Index);
                var chapter = EnsureChapter(volume, chapterId);
                chapter.Title = FirstNonEmpty(chapter.Title, $"{arc.Title} · 第{beat.Index}章");
                chapter.Status = chapter.Status == "unstarted" ? "planned" : chapter.Status;
                chapter.NextAction = FirstNonEmpty(chapter.NextAction, "PlanChapter");
            }
            volume.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var run in bible.AgentRuns.Where(r => r.Intent == NovelAgentIntent.PlanChapter || !string.IsNullOrWhiteSpace(r.TargetChapterId)))
        {
            var volume = FindOrCreateVolumeForRun(plan, bible, run);
            var chapter = EnsureChapter(volume, FirstNonEmpty(run.TargetChapterId, run.ChapterBrief?.ChapterId, run.RunId));
            chapter.RunId = run.RunId;
            chapter.Title = FirstNonEmpty(chapter.Title, run.ChapterBrief?.SelectedCandidateTitle, run.ChapterBrief?.RecommendedCandidateTitle, run.TargetChapterId);
            SyncChapterFromRun(chapter, run);
            if (run.DependencyImpact != null)
                ApplyDependencyImpact(plan, run.RunId, run.DependencyImpact);
        }

        foreach (var run in bible.AgentRuns.Where(r => r.Intent == NovelAgentIntent.PlanVolumeArc && r.VolumeArcPlan != null))
        {
            var volume = EnsureVolume(plan, run.VolumeArcPlan!.VolumeId, run.VolumeArcPlan.Title);
            volume.Goal = FirstNonEmpty(run.VolumeArcPlan.VolumePromise, run.UserGoal, volume.Goal);
            volume.StartChapterId = run.VolumeArcPlan.StartChapterId;
            volume.EndChapterId = run.VolumeArcPlan.EndChapterId;
            volume.Status = run.Status == NovelAgentRunStatus.Completed ? "active" : "planned";
            volume.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static void SyncChapterFromRun(AgentChapterTask chapter, NovelAgentRun run)
    {
        chapter.CandidateStatus = run.ChapterBrief == null
            ? chapter.CandidateStatus
            : !string.IsNullOrWhiteSpace(run.ChapterBrief.SelectedCandidateTitle)
                ? "candidate_selected"
                : run.ChapterBrief.Candidates.Count > 0 ? "candidates_ready" : chapter.CandidateStatus;
        chapter.ContextStatus = run.ContextPackage?.Status ?? chapter.ContextStatus;
        chapter.DraftStatus = run.DraftArtifact?.Status ?? chapter.DraftStatus;
        chapter.GateStatus = run.GateReport?.Status ?? chapter.GateStatus;
        chapter.RepairAttemptCount = run.DraftArtifact?.RepairAttemptCount ?? chapter.RepairAttemptCount;
        chapter.CommitStatus = run.DraftArtifact?.Status == "committed" ? "committed" : chapter.CommitStatus;
        chapter.GateIssueSummary = run.GateReport?.Issues.Count > 0
            ? string.Join("；", run.GateReport.Issues.Take(3))
            : chapter.GateIssueSummary;
        chapter.LastArtifactIds = BuildArtifactIds(run);
        if (run.GateReport?.Status == "validated" && chapter.QualityStatus == "not_reviewed")
            chapter.QualityStatus = "pending_quality_review";
        chapter.DraftArtifactId = run.DraftArtifact?.ArtifactId ?? chapter.DraftArtifactId;
        chapter.GateReportId = run.GateReport == null ? chapter.GateReportId : $"gate:{run.RunId}:{run.GateReport.ValidatedAt:O}";
        chapter.ArtifactStatus = ComputeArtifactStatus(run, chapter);
        chapter.Status = ComputeChapterStatus(run, chapter);
        chapter.NextAction = ComputeNextAction(chapter);
        chapter.AllowedNextActions = ComputeAllowedNextActions(chapter);
        chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
        chapter.LastTransitionReason = $"run:{run.Intent}/{run.Status}";
        chapter.UpdatedAt = DateTime.UtcNow;
    }

    private static void ApplyToolResult(AgentMissionPlan plan, AgentSession session, AgentToolExecutionResult? result, AgentAction? action)
    {
        if (result == null)
            return;

        plan.CurrentRunId = result.RunId ?? session.ActiveRunId ?? plan.CurrentRunId;
        if (!string.IsNullOrWhiteSpace(result.Phase))
            plan.Stage = MapMissionStage(result.Phase);
        else if (!string.IsNullOrWhiteSpace(action?.Intent))
            plan.Stage = action.Intent;

        plan.Status = result.Success ? FirstNonEmpty(plan.Stage, "active") : "blocked";

        if (result.Artifact != null && !string.IsNullOrWhiteSpace(result.Artifact.Summary))
            AddUnique(plan.CompletedItems, result.Artifact.Summary);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.Message))
            AddUnique(plan.Blockers, result.Message);
        if (result.Success && result.Artifact?.NextHints.Count > 0)
            foreach (var hint in result.Artifact.NextHints)
                AddUnique(plan.TodoQueue, hint);

        if (result.Data is NovelAgentExecutionResult execution && execution.DependencyImpact != null)
            ApplyDependencyImpact(plan, result.RunId ?? session.ActiveRunId ?? string.Empty, execution.DependencyImpact);

        if (result.Artifact != null)
        {
            plan.ActiveArtifactCursor = FirstNonEmpty(result.Artifact.ArtifactId, result.Artifact.RunId, plan.ActiveArtifactCursor);
            plan.ArtifactCursor = FirstNonEmpty(result.Artifact.RunId, result.Artifact.ArtifactId, plan.ArtifactCursor);
            plan.LastUserVisibleState = FirstNonEmpty(result.Artifact.UserVisibleStatus, result.Artifact.Summary, plan.LastUserVisibleState);
        }
    }

    private static void ApplyReflection(AgentMissionPlan plan, AgentReflection? reflection)
    {
        if (reflection == null)
            return;

        if (!string.IsNullOrWhiteSpace(reflection.MissionPatch.Stage))
            plan.Stage = reflection.MissionPatch.Stage;
        if (!string.IsNullOrWhiteSpace(reflection.MissionPatch.Status))
            plan.Status = reflection.MissionPatch.Status;
        if (!string.IsNullOrWhiteSpace(reflection.MissionPatch.CurrentFocus))
            plan.BookTaskTree.CurrentFocus = reflection.MissionPatch.CurrentFocus;

        foreach (var patch in reflection.MissionPatch.ChapterPatches)
        {
            var chapter = FindChapter(plan, patch.ChapterId);
            if (chapter == null) continue;
            if (!string.IsNullOrWhiteSpace(patch.Status)) chapter.Status = patch.Status;
            if (!string.IsNullOrWhiteSpace(patch.GateStatus)) chapter.GateStatus = patch.GateStatus;
            if (!string.IsNullOrWhiteSpace(patch.QualityIssueSummary)) chapter.QualityIssueSummary = patch.QualityIssueSummary;
            if (!string.IsNullOrWhiteSpace(patch.NextAction)) chapter.NextAction = patch.NextAction;
            ApplyQualityGate(chapter, reflection.QualityGate);
            if (reflection.QualityGate.Status != "not_applicable")
                chapter.QualityReportId = $"quality:{chapter.RunId}:{DateTime.UtcNow:O}";
            chapter.ArtifactStatus = FirstNonEmpty(chapter.QualityStatus, chapter.ArtifactStatus);
            chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
            chapter.AllowedNextActions = ComputeAllowedNextActions(chapter);
            chapter.LastTransitionReason = $"reflect:{reflection.QualityGate.Status}";
            chapter.UpdatedAt = DateTime.UtcNow;
        }

        if (reflection.QualityGate.Status is "fail" or "needs_rewrite" or "needs_user_input")
        {
            plan.Status = reflection.QualityGate.RequiresUserInput ? "blocked" : "chapter_work";
            if (!string.IsNullOrWhiteSpace(reflection.QualityGate.RewriteDecision))
                AddUnique(plan.Blockers, reflection.QualityGate.RewriteDecision);
        }
        else if (reflection.GoalSatisfied)
        {
            plan.Status = FirstNonEmpty(plan.Stage, "active");
        }

        foreach (var item in reflection.CompletedItems)
            AddUnique(plan.CompletedItems, item);
        foreach (var item in reflection.NewTodoItems)
            AddUnique(plan.TodoQueue, item);
        foreach (var item in reflection.Blockers)
            AddUnique(plan.Blockers, item);
    }

    private static void SyncScheduler(AgentMissionPlan plan, AgentSession session, NovelProjectInfo project)
    {
        plan.SchedulerState ??= new AgentTaskSchedulerState();
        var tasks = new List<AgentScheduledTask>();

        foreach (var chapter in plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters))
        {
            if (chapter.Status == "committed")
                continue;
            var task = new AgentScheduledTask
            {
                TaskId = string.IsNullOrWhiteSpace(chapter.RunId) ? $"{chapter.ChapterId}:plan" : $"{chapter.RunId}:{chapter.NextAction}",
                SessionId = session.SessionId,
                ProjectId = project.Id,
                ProjectTitle = project.Title,
                ChapterId = chapter.ChapterId,
                RunId = chapter.RunId,
                TaskType = FirstNonEmpty(chapter.NextAction, "PlanChapter"),
                Status = MapTaskStatus(chapter, session),
                NextAction = FirstNonEmpty(chapter.NextAction, "PlanChapter"),
                BlockedReason = FirstNonEmpty(chapter.QualityIssueSummary, chapter.GateIssueSummary),
                Risk = chapter.NextAction is "GenerateChapterWithChanges" or "RepairChapterDraft" or "CommitValidatedChapter" ? "High" : "Medium",
                RequiresConfirmation = false,
                Reason = FirstNonEmpty(chapter.LastTransitionReason, chapter.DependencyStatus, chapter.QualityStatus, chapter.Status),
                UpdatedAt = chapter.UpdatedAt,
            };
            tasks.Add(task);
        }

        plan.SchedulerState.Tasks = tasks
            .OrderBy(t => t.Status == "running" ? 0 : t.Status == "queued" ? 1 : 2)
            .ThenBy(t => t.ChapterId)
            .Take(80)
            .ToList();
        var active = plan.SchedulerState.Tasks.FirstOrDefault(t => t.Status == "running")
                     ?? plan.SchedulerState.Tasks.FirstOrDefault(t => t.Status == "queued")
                     ?? plan.SchedulerState.Tasks.FirstOrDefault();
        plan.SchedulerState.ActiveTaskId = active?.TaskId ?? string.Empty;
        plan.SchedulerState.ActiveRunId = active?.RunId ?? string.Empty;
        plan.SchedulerState.ActiveChapterId = active?.ChapterId ?? string.Empty;
        plan.SchedulerState.LastDecisionReason = active?.Reason ?? string.Empty;
        plan.ActiveChapterId = active?.ChapterId ?? plan.ActiveChapterId;
        plan.SchedulerState.UpdatedAt = DateTime.UtcNow;
        plan.BookTaskTree.Status = plan.Status switch
        {
            "blocked" => "blocked",
            "completed" => "completed",
            _ => bibleStageToBookStatus(plan.Stage),
        };
        plan.BookTaskTree.UpdatedAt = DateTime.UtcNow;
    }

    private static void SyncAllowedActions(AgentMissionPlan plan, StoryBibleDocument bible)
    {
        foreach (var chapter in plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters))
        {
            chapter.NextAction = ComputeNextAction(chapter);
            chapter.AllowedNextActions = ComputeAllowedNextActions(chapter);
        }

        var allowed = new List<string>();
        if (bible.Constitution == null)
        {
            allowed.Add("PlanStoryFoundation");
        }
        else if (bible.VolumeArcs.Count == 0)
        {
            allowed.Add("PlanVolumeArc");
        }
        else
        {
            allowed.AddRange(plan.BookTaskTree.Volumes
                .SelectMany(v => v.Chapters)
                .SelectMany(c => c.AllowedNextActions));
            if (allowed.Count == 0)
                allowed.Add("PlanChapter");
        }

        plan.AllowedNextActions = allowed
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static void SyncVerifiedState(AgentMissionPlan plan, AgentSession session)
    {
        plan.ActiveArtifacts = session.WorkingMemory.RecentObservations
            .Where(o => o.Artifact != null)
            .Select(o => o.Artifact!)
            .Where(a => a.VisibleInWorkflow)
            .GroupBy(a => $"{a.ArtifactType}:{a.ArtifactId}:{a.RunId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .TakeLast(12)
            .ToList();
        var latestArtifact = plan.ActiveArtifacts.LastOrDefault();
        if (latestArtifact != null)
        {
            plan.ActiveArtifactCursor = FirstNonEmpty(latestArtifact.ArtifactId, latestArtifact.RunId, plan.ActiveArtifactCursor);
            plan.ArtifactCursor = FirstNonEmpty(latestArtifact.RunId, latestArtifact.ArtifactId, plan.ArtifactCursor);
        }
        plan.BlockedReason = plan.Blockers.LastOrDefault() ??
                             plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters)
                                 .Select(c => FirstNonEmpty(c.QualityIssueSummary, c.GateIssueSummary))
                                 .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ??
                             string.Empty;
        var activeTask = plan.SchedulerState?.Tasks.FirstOrDefault(t => t.Status is "running" or "blocked")
                         ?? plan.SchedulerState?.Tasks.FirstOrDefault(t => t.Status == "queued");
        plan.LastVerifiedState = activeTask == null
            ? $"{plan.ProjectTitle} / {plan.Stage} / {plan.Status}"
            : $"{activeTask.ProjectTitle} / {activeTask.ChapterId} / {activeTask.NextAction} / {activeTask.Status}";
        plan.LastUserVisibleState = FirstNonEmpty(
            latestArtifact?.UserVisibleStatus,
            activeTask == null ? string.Empty : $"{activeTask.ChapterId}：{TaskUserVisibleStatus(activeTask)}",
            plan.LastVerifiedState);
        plan.LifecycleStatus = ComputeLifecycleStatus(plan);
    }

    private static void ApplyDependencyImpact(AgentMissionPlan plan, string sourceRunId, DependencyImpactReport impact)
    {
        if (impact == null) return;
        var key = $"{sourceRunId}:{impact.CreatedAt:O}:{impact.Summary}";
        if (plan.DependencyImpacts.Any(i => string.Equals($"{i.SourceRunId}:{i.CreatedAt:O}:{i.Summary}", key, StringComparison.Ordinal)))
            return;

        plan.DependencyVersion++;
        plan.DependencyImpacts.Add(new AgentDependencyImpactState
        {
            SourceRunId = sourceRunId,
            Status = impact.Status,
            ChangedModules = impact.ChangedModules.ToList(),
            ImpactedModules = impact.ImpactedModules.ToList(),
            ImpactedChapters = impact.ImpactedChapters.ToList(),
            Summary = impact.Summary,
            CreatedAt = impact.CreatedAt,
        });
        if (plan.DependencyImpacts.Count > 30)
            plan.DependencyImpacts.RemoveRange(0, plan.DependencyImpacts.Count - 30);

        if (string.Equals(impact.Status, "clean", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var chapter in plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters))
        {
            if (!IsChapterImpacted(chapter, impact))
                continue;

            var mode = ClassifyDependencyImpact(impact);
            chapter.DependencyStatus = mode;
            chapter.LastTransitionReason = FirstNonEmpty(impact.Summary, $"dependency:{mode}");
            if (chapter.Status == "committed")
            {
                chapter.Status = "requires_review";
                chapter.NextAction = "ReviewChapter";
                chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
                continue;
            }

            if (mode == "needs_context_rebuild")
            {
                chapter.RequiresContextRebuild = true;
                chapter.RequiresRevalidation = false;
                chapter.Status = "needs_context_rebuild";
                chapter.ContextStatus = "stale";
                chapter.NextAction = "BuildChapterContextPackage";
                chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
            }
            else if (mode == "needs_revalidation")
            {
                chapter.RequiresRevalidation = true;
                chapter.Status = "needs_revalidation";
                chapter.GateStatus = "stale";
                chapter.NextAction = "ValidateChapterDraft";
                chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
            }
        }
    }

    private static bool IsChapterImpacted(AgentChapterTask chapter, DependencyImpactReport impact)
    {
        if (impact.ImpactedChapters.Count == 0 && impact.ImpactedModules.Count == 0)
            return false;
        return impact.ImpactedChapters.Any(item =>
            item.Contains(chapter.ChapterId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(chapter.RunId) && item.Contains(chapter.RunId, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ClassifyDependencyImpact(DependencyImpactReport impact)
    {
        var joined = string.Join(" ", impact.ChangedModules.Concat(impact.ImpactedModules)).ToLowerInvariant();
        if (joined.Contains("world") || joined.Contains("rule") || joined.Contains("location") || joined.Contains("faction") ||
            joined.Contains("世界") || joined.Contains("规则") || joined.Contains("地点") || joined.Contains("势力") ||
            joined.Contains("blueprint") || joined.Contains("volume") || joined.Contains("卷") || joined.Contains("蓝图"))
            return "needs_context_rebuild";
        if (joined.Contains("character") || joined.Contains("relationship") || joined.Contains("secret") ||
            joined.Contains("角色") || joined.Contains("关系") || joined.Contains("秘密"))
            return "needs_revalidation";
        return "needs_revalidation";
    }

    private static void ApplyQualityGate(AgentChapterTask chapter, AgentQualityGateReport qualityGate)
    {
        if (qualityGate.Status == "not_applicable")
            return;
        chapter.QualityStatus = qualityGate.Status switch
        {
            "pass" => "quality_passed",
            "warn" => "quality_warn",
            "fail" => "quality_failed",
            "needs_rewrite" => "quality_failed",
            "needs_user_input" => "blocked",
            _ => qualityGate.Status,
        };
        chapter.QualityScores = qualityGate.Scores ?? new AgentQualityScores();
        if (qualityGate.Issues.Count > 0)
            chapter.QualityIssueSummary = string.Join("；", qualityGate.Issues.Take(3));
        if (qualityGate.Status is "fail" or "needs_rewrite")
        {
            chapter.Status = "quality_failed";
            chapter.NextAction = "RepairChapterDraft";
        }
        else if (qualityGate.Status is "pass" or "warn")
        {
            chapter.Status = chapter.GateStatus == "validated" ? "quality_passed" : chapter.Status;
            if (chapter.GateStatus == "validated")
                chapter.NextAction = "CommitValidatedChapter";
        }
        chapter.UserVisibleStatus = MapUserVisibleStatus(chapter);
    }

    private static List<string> BuildArtifactIds(NovelAgentRun run)
    {
        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(run.ContextPackage?.ChapterId)) ids.Add($"context:{run.ContextPackage.ChapterId}");
        if (!string.IsNullOrWhiteSpace(run.DraftArtifact?.ArtifactId)) ids.Add($"draft:{run.DraftArtifact.ArtifactId}");
        if (run.GateReport != null) ids.Add($"gate:{run.RunId}:{run.GateReport.ValidatedAt:O}");
        if (run.DependencyImpact != null) ids.Add($"dependency:{run.RunId}:{run.DependencyImpact.CreatedAt:O}");
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void PruneResolvedDependencyImpacts(AgentMissionPlan plan)
    {
        foreach (var chapter in plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters))
        {
            if (chapter.Status is "context_ready" or "draft_generated" or "validated" or "quality_passed" or "committed")
                chapter.RequiresContextRebuild = false;
            if (chapter.Status is "validated" or "quality_passed" or "committed")
                chapter.RequiresRevalidation = false;
            if (!chapter.RequiresContextRebuild && !chapter.RequiresRevalidation && chapter.DependencyStatus != "clean" &&
                chapter.Status is not ("needs_context_rebuild" or "needs_revalidation" or "requires_review"))
                chapter.DependencyStatus = "clean";
        }
    }

    private static AgentVolumeTask EnsureVolume(AgentMissionPlan plan, string volumeId, string title)
    {
        volumeId = FirstNonEmpty(volumeId, "volume-auto");
        var volume = plan.BookTaskTree.Volumes.FirstOrDefault(v => string.Equals(v.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase));
        if (volume != null)
            return volume;

        volume = new AgentVolumeTask
        {
            VolumeId = volumeId,
            Title = FirstNonEmpty(title, volumeId),
            Status = "planned",
        };
        plan.BookTaskTree.Volumes.Add(volume);
        return volume;
    }

    private static AgentChapterTask EnsureChapter(AgentVolumeTask volume, string chapterId)
    {
        chapterId = FirstNonEmpty(chapterId, "chapter-auto");
        var chapter = volume.Chapters.FirstOrDefault(c => string.Equals(c.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase));
        if (chapter != null)
            return chapter;

        chapter = new AgentChapterTask
        {
            ChapterId = chapterId,
            Title = chapterId,
            Status = "unstarted",
            NextAction = "PlanChapter",
        };
        volume.Chapters.Add(chapter);
        return chapter;
    }

    private static AgentVolumeTask FindOrCreateVolumeForRun(AgentMissionPlan plan, StoryBibleDocument bible, NovelAgentRun run)
    {
        var chapterId = FirstNonEmpty(run.TargetChapterId, run.ChapterBrief?.ChapterId);
        var arc = bible.VolumeArcs.FirstOrDefault(v => ChapterBelongsToVolume(chapterId, v));
        return arc != null
            ? EnsureVolume(plan, arc.VolumeId, arc.Title)
            : EnsureVolume(plan, "volume-unassigned", "未分卷任务");
    }

    private static AgentChapterTask? FindChapter(AgentMissionPlan plan, string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId)) return null;
        return plan.BookTaskTree.Volumes
            .SelectMany(v => v.Chapters)
            .FirstOrDefault(c => string.Equals(c.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeChapterStatus(NovelAgentRun run, AgentChapterTask chapter)
    {
        if (chapter.CommitStatus == "committed" || chapter.DraftStatus == "committed")
            return "committed";
        if (chapter.Status is "requires_review")
            return "requires_review";
        if (chapter.RequiresContextRebuild)
            return "needs_context_rebuild";
        if (chapter.RequiresRevalidation)
            return "needs_revalidation";
        if (chapter.QualityStatus is "quality_failed" or "blocked")
            return "quality_failed";
        if (chapter.QualityStatus is "quality_passed" && chapter.GateStatus == "validated")
            return "quality_passed";
        if (chapter.GateStatus == "validated")
            return "validated";
        if (chapter.GateStatus is "failed" or "gate_failed" || run.Status == NovelAgentRunStatus.Repairing)
            return run.Status == NovelAgentRunStatus.Repairing ? "repairing" : "gate_failed";
        if (chapter.DraftStatus == "draft_generated")
            return "draft_generated";
        if (chapter.ContextStatus is "ready" or "context_ready")
            return "context_ready";
        if (chapter.CandidateStatus == "candidate_selected")
            return "candidate_selected";
        if (chapter.CandidateStatus == "candidates_ready")
            return "planned";
        return chapter.Status == "unstarted" ? "planned" : chapter.Status;
    }

    private static string ComputeArtifactStatus(NovelAgentRun run, AgentChapterTask chapter)
    {
        if (chapter.CommitStatus == "committed" || run.DraftArtifact?.Status == "committed") return "committed";
        if (chapter.QualityStatus is "quality_failed" or "blocked") return "quality_failed";
        if (chapter.QualityStatus is "quality_passed" or "quality_warn") return chapter.QualityStatus;
        if (run.GateReport?.Status == "validated") return "validated";
        if (!string.IsNullOrWhiteSpace(run.GateReport?.Status) && run.GateReport.Status != "pending") return "gate_failed";
        if (!string.IsNullOrWhiteSpace(run.DraftArtifact?.Status)) return run.DraftArtifact.Status;
        if (run.ContextPackage?.Status == "context_ready") return "context_ready";
        if (!string.IsNullOrWhiteSpace(run.ChapterBrief?.SelectedCandidateTitle)) return "candidate_selected";
        if ((run.ChapterBrief?.Candidates.Count ?? 0) > 0) return "candidates_ready";
        return FirstNonEmpty(chapter.Status, "unstarted");
    }

    private static string ComputeNextAction(AgentChapterTask chapter) =>
        chapter.Status switch
        {
            "planned" => chapter.CandidateStatus == "candidate_selected" ? "BuildChapterContextPackage" : "SelectChapterCandidate",
            "candidate_selected" => "BuildChapterContextPackage",
            "context_ready" => "GenerateChapterWithChanges",
            "draft_generated" => "ValidateChapterDraft",
            "gate_failed" => "RepairChapterDraft",
            "repairing" => "ValidateChapterDraft",
            "validated" => "CommitValidatedChapter",
            "quality_failed" => "RepairChapterDraft",
            "quality_passed" => "CommitValidatedChapter",
            "needs_context_rebuild" => "BuildChapterContextPackage",
            "needs_revalidation" => "ValidateChapterDraft",
            "requires_review" => "ReviewChapter",
            "committed" => "ReviewChapter",
            _ => "PlanChapter",
        };

    private static List<string> ComputeAllowedNextActions(AgentChapterTask chapter)
    {
        var next = ComputeNextAction(chapter);
        var actions = new List<string>();
        if (!string.IsNullOrWhiteSpace(next))
            actions.Add(next);
        if (chapter.Status is "validated" or "quality_passed")
            actions.Add("ReviewChapter");
        if (chapter.Status is "gate_failed" or "repairing" or "quality_failed")
            actions.Add("ValidateChapterDraft");
        if (chapter.Status is "needs_context_rebuild" || chapter.RequiresContextRebuild)
            actions.Add("BuildChapterContextPackage");
        if (chapter.Status is "needs_revalidation" || chapter.RequiresRevalidation)
            actions.Add("ValidateChapterDraft");
        if (chapter.Status == "requires_review")
            actions.Add("AnalyzeDependencyImpact");
        return actions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string MapUserVisibleStatus(AgentChapterTask chapter) =>
        chapter.Status switch
        {
            "committed" => "成稿已进入书城",
            "quality_passed" => "质量门禁通过，准备提交",
            "validated" => "结构门禁通过，等待质量反思",
            "quality_failed" => "质量门禁未通过，等待修复",
            "gate_failed" => "结构门禁未通过，等待修复",
            "repairing" => "草稿修复中",
            "draft_generated" => "章节草稿已生成，等待门禁校验",
            "context_ready" => "章节上下文包已就绪",
            "candidate_selected" => "章节候选已选定",
            "planned" => chapter.CandidateStatus == "candidates_ready" ? "章节候选已生成" : "章节已规划",
            "needs_context_rebuild" => "上下文已过期，需要重建",
            "needs_revalidation" => "设定变化后需要重新校验",
            "requires_review" => "已提交章节受影响，需要复盘",
            "blocked" => "任务阻塞，等待补充信息",
            _ => "等待 Agent 推进",
        };

    private static string MapTaskStatus(AgentChapterTask chapter, AgentSession session)
    {
        if (chapter.Status is "gate_failed" or "blocked" or "quality_failed" or "needs_context_rebuild" or "needs_revalidation" or "requires_review")
            return "blocked";
        if (chapter.Status == "committed")
            return "done";
        if (!string.IsNullOrWhiteSpace(chapter.RunId) && session.ActiveRunId == chapter.RunId)
            return "running";
        return "queued";
    }

    private static string TaskUserVisibleStatus(AgentScheduledTask task) =>
        task.Status switch
        {
            "blocked" => FirstNonEmpty(task.BlockedReason, $"任务阻塞：{task.NextAction}"),
            "running" => $"正在推进 {task.NextAction}",
            "queued" => $"排队等待 {task.NextAction}",
            "done" => "任务已完成",
            _ => FirstNonEmpty(task.NextAction, task.Status),
        };

    private static string MapMissionStage(string phase) =>
        phase switch
        {
            "foundation_candidates" or "foundation_committed" or "awaiting_user_foundation" => "foundation",
            "volume_plan" or "volume_committed" => "volume_planning",
            "chapter_candidates" or "candidate_selected" => "chapter_planning",
            "context_ready" => "context_building",
            "draft_generated" => "draft_generation",
            "validated" or "failed" or "gate_failed" => "gate_validation",
            "repairing" => "repair",
            "committed" => "commit",
            "chapter_reviewed" => "review",
            _ => phase,
        };

    private static string BuildChapterId(string startChapterId, int beatIndex)
    {
        var start = ExtractTrailingNumber(startChapterId);
        if (start <= 0) return $"chapter-{beatIndex:000}";
        return $"chapter-{start + Math.Max(beatIndex - 1, 0):000}";
    }

    private static bool ChapterBelongsToVolume(string chapterId, VolumeArcPlan volume)
    {
        var chapter = ExtractTrailingNumber(chapterId);
        var start = ExtractTrailingNumber(volume.StartChapterId);
        var end = ExtractTrailingNumber(volume.EndChapterId);
        return chapter > 0 && start > 0 && end > 0 && chapter >= start && chapter <= end;
    }

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static string bibleStageToBookStatus(string stage) =>
        stage switch
        {
            "blocked" => "blocked",
            "completed" => "completed",
            "foundation" or "volume_planning" => "planning",
            _ => "drafting",
        };

    private static string ComputeLifecycleStatus(AgentMissionPlan plan)
    {
        if (plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters).Any(c => c.Status is "gate_failed" or "quality_failed" or "blocked"))
            return "blocked";
        if (plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters).Any(c => c.Status is "draft_generated" or "validated" or "quality_passed" or "repairing"))
            return "drafting";
        if (plan.BookTaskTree.Volumes.SelectMany(v => v.Chapters).Any(c => c.Status == "committed"))
            return "committed";
        return FirstNonEmpty(plan.Stage, plan.Status, "idle");
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value))
            list.Add(value);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
