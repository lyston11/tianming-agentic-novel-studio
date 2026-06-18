using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public static class NovelLibrary
{
    public static async Task<NovelLibraryDocument> BuildAsync(
        NovelAgentWorkspace workspace,
        NovelProjectCatalog catalog,
        CancellationToken ct = default)
    {
        return await BuildAsync(workspace, catalog, null, ct).ConfigureAwait(false)
            ?? new NovelLibraryDocument(Array.Empty<NovelBookView>(), null, Array.Empty<NovelVolumeView>(), null, 0, 0, 0);
    }

    public static async Task<NovelLibraryDocument?> BuildAsync(
        NovelAgentWorkspace workspace,
        NovelProjectCatalog catalog,
        string? projectId,
        CancellationToken ct = default)
    {
        var catalogDocument = await catalog.GetAsync(ct).ConfigureAwait(false);
        var books = new List<NovelBookView>();
        NovelLibraryDocument? selectedLibrary = null;
        var hasProjectScope = !string.IsNullOrWhiteSpace(projectId);

        foreach (var project in catalogDocument.Projects)
        {
            var isActive = string.Equals(project.Id, catalogDocument.ActiveProjectId, StringComparison.OrdinalIgnoreCase);
            var projectLibrary = await catalog.WithProjectAsync(
                project,
                async () =>
                {
                    var bible = await workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                    return BuildSingleProject(project, bible, isActive);
                },
                ct).ConfigureAwait(false);

            books.Add(projectLibrary.ActiveBook!);
            if (hasProjectScope)
            {
                if (string.Equals(project.Id, projectId, StringComparison.OrdinalIgnoreCase))
                    selectedLibrary = projectLibrary;
            }
            else if (projectLibrary.ActiveBook?.IsActive == true)
            {
                selectedLibrary = projectLibrary;
            }
        }

        if (hasProjectScope && selectedLibrary == null)
            return null;

        selectedLibrary ??= books.Count > 0
            ? new NovelLibraryDocument(books, books[0], Array.Empty<NovelVolumeView>(), null, 0, 0, 0)
            : new NovelLibraryDocument(Array.Empty<NovelBookView>(), null, Array.Empty<NovelVolumeView>(), null, 0, 0, 0);

        return selectedLibrary with
        {
            Books = books
                .OrderByDescending(b => b.IsActive)
                .ThenByDescending(b => b.UpdatedAt)
                .ToList()
        };
    }

    private static NovelLibraryDocument BuildSingleProject(
        NovelProjectInfo project,
        StoryBibleDocument bible,
        bool isActive)
    {
        var runs = bible.AgentRuns
            .Where(r => !string.IsNullOrWhiteSpace(r.TargetChapterId))
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();
        var latestRunByChapter = runs
            .GroupBy(r => r.TargetChapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var volumes = new List<NovelVolumeView>();
        var committedVolumeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var volume in bible.VolumeArcs)
        {
            var chapters = BuildChaptersForVolume(volume, latestRunByChapter).ToList();
            var volumeId = FirstNonEmpty(volume.VolumeId, volume.Id);
            committedVolumeIds.Add(volumeId);
            volumes.Add(new NovelVolumeView(
                volumeId,
                FirstNonEmpty(volume.Title, "未命名卷"),
                volume.Status.ToString(),
                volume.StartChapterId,
                volume.EndChapterId,
                volume.ExpectedChapterCount,
                chapters));
        }

        var draftVolumeRuns = bible.AgentRuns
            .Where(r => r.VolumeArcPlan != null)
            .Where(r => r.Status != NovelAgentRunStatus.Completed)
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();

        foreach (var run in draftVolumeRuns)
        {
            var draft = run.VolumeArcPlan!;
            var volumeId = FirstNonEmpty(draft.VolumeId, draft.Id);
            if (committedVolumeIds.Contains(volumeId))
                continue;

            committedVolumeIds.Add(volumeId);
            var chapters = BuildChaptersForVolume(draft, latestRunByChapter)
                .Select(chapter => chapter with
                {
                    Status = "卷草案",
                    RunId = run.RunId,
                    Intent = run.Intent.ToString(),
                    UpdatedAt = run.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                })
                .ToList();

            volumes.Add(new NovelVolumeView(
                volumeId,
                FirstNonEmpty(draft.Title, "待确认卷规划"),
                "DraftRun",
                draft.StartChapterId,
                draft.EndChapterId,
                draft.ExpectedChapterCount,
                chapters));
        }

        var coveredChapterIds = volumes
            .SelectMany(v => v.Chapters)
            .Select(c => c.ChapterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var looseRuns = runs
            .Where(r => !coveredChapterIds.Contains(r.TargetChapterId))
            .ToList();

        if (looseRuns.Count > 0 || volumes.Count == 0)
        {
            var looseChapters = looseRuns
                .Select((run, index) => BuildChapterFromRun(run.TargetChapterId, "未归档章节", index + 1, null, run))
                .OrderBy(c => c.ChapterId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (looseChapters.Count > 0 || volumes.Count == 0)
            {
                volumes.Add(new NovelVolumeView(
                    "unassigned",
                    "未归档章节",
                    "Draft",
                    looseChapters.FirstOrDefault()?.ChapterId ?? string.Empty,
                    looseChapters.LastOrDefault()?.ChapterId ?? string.Empty,
                    looseChapters.Count,
                    looseChapters));
            }
        }

        var allChapters = volumes.SelectMany(v => v.Chapters).ToList();
        var selected = allChapters
            .OrderByDescending(c => c.HasGeneratedContent)
            .ThenBy(c => c.VolumeId)
            .ThenBy(c => c.BeatIndex == 0 ? int.MaxValue : c.BeatIndex)
            .FirstOrDefault();
        var generatedCount = allChapters.Count(c => c.HasGeneratedContent);
        var plannedCount = allChapters.Count;
        var needsRewriteCount = allChapters.Count(c => c.NeedsRewrite);
        var constitution = bible.Constitution;
        var book = new NovelBookView(
            project.Id,
            FirstNonEmpty(project.Title, constitution?.CoreHook, "未命名小说"),
            FirstNonEmpty(project.Genre, constitution?.Genre),
            FirstNonEmpty(project.SubGenre, constitution?.SubGenre),
            FirstNonEmpty(project.CoreHook, constitution?.CoreHook),
            FirstNonEmpty(project.ReaderPromise, constitution?.ReaderPromise),
            project.Status,
            isActive,
            volumes.Count(v => v.VolumeId != "unassigned"),
            generatedCount,
            plannedCount,
            needsRewriteCount,
            project.UpdatedAt.ToString("O"),
            selected);

        return new NovelLibraryDocument(
            new[] { book },
            book,
            volumes,
            selected,
            generatedCount,
            plannedCount,
            needsRewriteCount);
    }

    private static IEnumerable<NovelChapterView> BuildChaptersForVolume(
        VolumeArcPlan volume,
        Dictionary<string, NovelAgentRun> latestRunByChapter)
    {
        if (volume.ChapterBeats.Count == 0)
        {
            var startId = FirstNonEmpty(volume.StartChapterId, "chapter-001");
            latestRunByChapter.TryGetValue(startId, out var run);
            yield return BuildChapterFromRun(startId, FirstNonEmpty(volume.Title, "未命名卷"), 1, volume, run);
            yield break;
        }

        foreach (var beat in volume.ChapterBeats.OrderBy(b => b.Index))
        {
            var chapterId = ResolveBeatChapterId(volume, beat.Index);
            latestRunByChapter.TryGetValue(chapterId, out var run);
            yield return BuildChapterFromRun(chapterId, FirstNonEmpty(volume.Title, "未命名卷"), beat.Index, volume, run, beat);
        }
    }

    private static NovelChapterView BuildChapterFromRun(
        string chapterId,
        string volumeTitle,
        int beatIndex,
        VolumeArcPlan? volume,
        NovelAgentRun? run,
        VolumeChapterBeat? beat = null)
    {
        var visibleInLibrary = IsLibraryVisible(run);
        var content = visibleInLibrary ? ExtractGeneratedContent(run) : string.Empty;
        var hasGeneratedContent = !string.IsNullOrWhiteSpace(content);
        var brief = run?.ChapterBrief;
        var review = run?.PostGenerationReview;
        var writingStatus = ResolveWritingStatus(run, hasGeneratedContent, brief != null);
        var gateIssues = run?.GateReport?.Issues.ToList() ?? new List<string>();
        var repairHints = run?.GateReport?.RepairHints.ToList() ?? new List<string>();
        var contextWarnings = run?.ContextPackage?.Warnings.ToList() ?? new List<string>();
        var dependencyWarnings = BuildDependencyWarnings(run).ToList();
        var title = FirstNonEmpty(
            brief?.SelectedCandidateTitle,
            brief?.RecommendedCandidateTitle,
            beat?.Role,
            chapterId);
        var summary = FirstNonEmpty(
            review?.Summary,
            brief?.CoreIdea,
            beat?.Goal,
            "等待 Agent 生成章节。");
        var status = ResolveDisplayStatus(writingStatus, review, brief);
        var userVisibleStatus = ResolveUserVisibleStatus(run, writingStatus, visibleInLibrary);
        var artifactStatus = ResolveArtifactStatus(run, writingStatus);

        return new NovelChapterView(
            chapterId,
            title,
            FirstNonEmpty(volume?.VolumeId, "unassigned"),
            volumeTitle,
            beatIndex,
            beat?.Role ?? string.Empty,
            FirstNonEmpty(beat?.Goal, brief?.CoreIdea),
            FirstNonEmpty(beat?.Turn, brief?.ConflictMove),
            FirstNonEmpty(beat?.Cost, brief?.CostOrConsequence),
            status,
            run?.RunId ?? string.Empty,
            run?.Intent.ToString() ?? string.Empty,
            run?.UpdatedAt.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            hasGeneratedContent,
            review?.RequiresRewrite == true || run?.Status == NovelAgentRunStatus.Repairing,
            CountWords(content),
            summary,
            hasGeneratedContent ? content : BuildPlaceholderContent(chapterId, beat, brief),
            FirstNonEmpty(brief?.SelectedCandidateTitle, brief?.RecommendedCandidateTitle),
            review?.QualityScore ?? 0,
            run?.RewriteAttempts.Count ?? 0,
            review?.Checks.Select(c => $"{c.Name}：{c.Status} · {c.Message}").ToList() ?? new List<string>(),
            review?.NextChapterSuggestions.ToList() ?? new List<string>(),
            writingStatus,
            run?.ContextPackage?.Status ?? string.Empty,
            run?.DraftArtifact?.Status ?? string.Empty,
            run?.GateReport?.Status ?? string.Empty,
            run?.GateReport?.ProtocolPassed ?? false,
            run?.GateReport?.FactSnapshotPassed ?? false,
            run?.GateReport?.BlueprintPassed ?? false,
            run?.GateReport?.RagPassed ?? false,
            run?.ContextPackage?.LongDistanceRecall.Count ?? 0,
            run?.DraftArtifact?.RepairAttemptCount ?? run?.RewriteAttempts.Count ?? 0,
            gateIssues,
            repairHints,
            dependencyWarnings,
            contextWarnings,
            run != null,
            visibleInLibrary,
            userVisibleStatus,
            artifactStatus,
            run?.DraftArtifact?.ArtifactId ?? string.Empty,
            run?.GateReport == null ? string.Empty : $"gate:{run.RunId}:{run.GateReport.ValidatedAt:O}",
            run?.PostGenerationReview == null ? string.Empty : $"review:{run.RunId}:{run.PostGenerationReview.ReviewId}");
    }

    private static bool IsLibraryVisible(NovelAgentRun? run)
    {
        if (run?.DraftArtifact == null)
            return false;
        var draftCommitted = string.Equals(run.DraftArtifact.Status, "committed", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(run.DraftArtifact.CommittedContent);
        var gatePassed = string.Equals(run.GateReport?.Status, "validated", StringComparison.OrdinalIgnoreCase);
        var qualityOk = run.PostGenerationReview != null &&
                        !run.PostGenerationReview.RequiresRewrite &&
                        run.PostGenerationReview.OverallResult is not "Fail" and not "Failed";
        return draftCommitted && gatePassed && qualityOk;
    }

    private static string ResolveArtifactStatus(NovelAgentRun? run, string writingStatus)
    {
        if (run == null) return "none";
        if (IsLibraryVisible(run)) return "committed";
        if (run.PostGenerationReview?.RequiresRewrite == true) return "quality_failed";
        if (string.Equals(run.GateReport?.Status, "validated", StringComparison.OrdinalIgnoreCase)) return "validated";
        if (!string.IsNullOrWhiteSpace(run.GateReport?.Status) && run.GateReport.Status != "pending") return "gate_failed";
        if (!string.IsNullOrWhiteSpace(run.DraftArtifact?.Status)) return run.DraftArtifact.Status;
        return writingStatus;
    }

    private static string ResolveUserVisibleStatus(NovelAgentRun? run, string writingStatus, bool visibleInLibrary)
    {
        if (visibleInLibrary) return "成稿已进入书城";
        if (run?.PostGenerationReview?.RequiresRewrite == true) return "质量门禁未通过，留在工作流";
        return writingStatus switch
        {
            "validated" => "结构门禁通过，等待提交确认",
            "gate_failed" => "结构门禁未通过，留在工作流",
            "repairing" => "草稿修复中，留在工作流",
            "draft_generated" => "草稿已生成，留在工作流",
            "context_ready" => "上下文包已就绪",
            "candidate_selected" => "章节候选已选定",
            "candidates_ready" => "章节候选已生成",
            _ => "等待 Agent 推进",
        };
    }

    private static string ResolveDisplayStatus(
        string writingStatus,
        NovelAgentPostGenerationReview? review,
        ChapterCreativeBrief? brief)
    {
        return writingStatus switch
        {
            "committed" => review?.RequiresRewrite == true ? "需返工" : "已入库",
            "validated" => "已校验",
            "repairing" => "修复中",
            "gate_failed" => "门禁失败",
            "draft_generated" => "草稿已生成",
            "context_ready" => "上下文就绪",
            "candidate_selected" => "候选已选",
            "candidates_ready" => "候选中",
            "planned" => "已规划",
            _ => brief == null ? "未规划" : "已规划"
        };
    }

    private static string ResolveWritingStatus(
        NovelAgentRun? run,
        bool hasGeneratedContent,
        bool hasBrief)
    {
        if (hasGeneratedContent) return "committed";
        if (run?.PostGenerationReview?.RequiresRewrite == true)
            return "quality_failed";
        if (string.Equals(run?.GateReport?.Status, "validated", StringComparison.OrdinalIgnoreCase))
            return "validated";
        if (run?.Status == NovelAgentRunStatus.Repairing)
            return string.Equals(run.DraftArtifact?.Status, "gate_failed", StringComparison.OrdinalIgnoreCase)
                ? "gate_failed"
                : "repairing";
        if (!string.IsNullOrWhiteSpace(run?.DraftArtifact?.Status))
            return run.DraftArtifact.Status;
        if (string.Equals(run?.ContextPackage?.Status, "context_ready", StringComparison.OrdinalIgnoreCase))
            return "context_ready";
        if (!string.IsNullOrWhiteSpace(run?.ChapterBrief?.SelectedCandidateTitle))
            return "candidate_selected";
        if ((run?.ChapterBrief?.Candidates.Count ?? 0) > 0)
            return "candidates_ready";
        return hasBrief ? "planned" : "unstarted";
    }

    private static IEnumerable<string> BuildDependencyWarnings(NovelAgentRun? run)
    {
        if (run?.DependencyImpact == null)
            yield break;

        if (!string.IsNullOrWhiteSpace(run.DependencyImpact.Summary))
            yield return run.DependencyImpact.Summary;

        foreach (var module in run.DependencyImpact.ImpactedModules.Take(4))
            yield return $"影响模块：{module}";

        foreach (var chapter in run.DependencyImpact.ImpactedChapters.Take(4))
            yield return $"需复核章节：{chapter}";
    }

    private static string ExtractGeneratedContent(NovelAgentRun? run)
    {
        if (run == null) return string.Empty;
        var draft = run.DraftArtifact;
        if (draft != null
            && string.Equals(draft.Status, "committed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(draft.CommittedContent))
        {
            return draft.CommittedContent;
        }

        return string.Empty;
    }

    private static string BuildPlaceholderContent(
        string chapterId,
        VolumeChapterBeat? beat,
        ChapterCreativeBrief? brief)
    {
        var lines = new[]
        {
            $"{chapterId} 尚未生成正文。",
            FirstNonEmpty(beat?.Goal, brief?.CoreIdea, "可以先在创作工作流中生成章节候选，再执行章节生成。"),
            FirstNonEmpty(beat?.Turn, brief?.ConflictMove),
            FirstNonEmpty(beat?.Cost, brief?.CostOrConsequence)
        };
        return string.Join("\n\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string ResolveBeatChapterId(VolumeArcPlan volume, int beatIndex)
    {
        var startId = FirstNonEmpty(volume.StartChapterId, volume.EndChapterId, "chapter-001");
        var numberToken = ExtractTrailingNumberToken(startId);
        if (string.IsNullOrWhiteSpace(numberToken)) return startId;

        var prefix = startId[..^numberToken.Length];
        var startNumber = int.Parse(numberToken);
        return $"{prefix}{(startNumber + Math.Max(0, beatIndex - 1)).ToString().PadLeft(numberToken.Length, '0')}";
    }

    private static string ExtractTrailingNumberToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 ? string.Empty : value[(index + 1)..];
    }

    private static int CountWords(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0
            : value.Count(c => !char.IsWhiteSpace(c));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
