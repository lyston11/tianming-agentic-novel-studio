using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class KnowledgeConflictBlockedException : InvalidOperationException
{
    public KnowledgeConflictBlockedException(
        IReadOnlyList<string> conflictIds,
        string firstConflictId,
        string explanation,
        string recommendedAction)
        : base($"Open hard knowledge conflict blocks chapter context package: {firstConflictId}. {explanation}")
    {
        ConflictIds = conflictIds;
        FirstConflictId = firstConflictId;
        Explanation = explanation;
        ConflictRecommendedAction = recommendedAction;
    }

    public IReadOnlyList<string> ConflictIds { get; }
    public string FirstConflictId { get; }
    public string Explanation { get; }
    public string ConflictRecommendedAction { get; }
}

public sealed class ChapterContextPackageRecorder : IChapterContextPackageRecorder
{
    private readonly IProductionTruthStore _truthStore;
    private readonly IProductionEventWriter _eventWriter;
    private readonly IAgentRuntimeEventService _runtimeEvents;
    private readonly NovelAgentDbContext? _db;

    public ChapterContextPackageRecorder(
        IProductionTruthStore truthStore,
        IProductionEventWriter eventWriter,
        IAgentRuntimeEventService runtimeEvents,
        NovelAgentDbContext? db = null)
    {
        _truthStore = truthStore;
        _eventWriter = eventWriter;
        _runtimeEvents = runtimeEvents;
        _db = db;
    }

    public async Task<TianmingPackage> RecordAsync(
        RecordChapterContextPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new InvalidOperationException("Chapter context package requires runtimeRunId, userId and projectId.");
        }

        var package = request.Package;
        if (!string.IsNullOrWhiteSpace(package.PackageId))
        {
            var existingPackage = await _truthStore.GetPackageAsync(
                    request.ProjectId,
                    request.RuntimeRunId,
                    package.PackageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingPackage != null)
            {
                package.PackageId = existingPackage.Id;
                return existingPackage;
            }
        }

        var chapterIdentity = await ResolveChapterIdentityAsync(request.ProjectId, package.ChapterId, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfOpenHardKnowledgeConflictAsync(request, chapterIdentity.CanonicalChapterId, cancellationToken)
            .ConfigureAwait(false);

        var createdPackage = await _truthStore.CreatePackageAsync(
                new CreateTianmingPackageRequest(
                    Id: package.PackageId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapterIdentity.CanonicalChapterId,
                    RuntimeRunId: request.RuntimeRunId,
                    PackageKind: "chapter_context_package",
                    InputJson: BuildInputJson(request, chapterIdentity),
                    DependencyVersionsJson: JsonSerializer.Serialize(new
                    {
                        storyBibleRunId = request.RuntimeRunId,
                        updatedAt = request.RunUpdatedAt
                    }),
                    KnowledgeSnapshotJson: BuildKnowledgeSnapshotJson(package),
                    FactSnapshotJson: BuildFactSnapshotJson(package),
                    PromptVersion: "chapter-context-v1",
                    KernelVersion: "agentic-tianming-v1"),
                cancellationToken)
            .ConfigureAwait(false);

        package.PackageId = createdPackage.Id;
        await MarkKnowledgeBindingsUsedAsync(request, chapterIdentity.CanonicalChapterId, cancellationToken)
            .ConfigureAwait(false);
        var rebuiltFromPackageIds = ResolveRebuiltFromPackageIds(package);

        var eventData = new
        {
            productionRunId = request.RuntimeRunId,
            packageId = createdPackage.Id,
            packageKind = createdPackage.PackageKind,
            chapterId = chapterIdentity.CanonicalChapterId,
            logicalChapterId = chapterIdentity.LogicalChapterId,
            worldRuleCount = package.WorldRules.Count,
            characterStateCount = package.CharacterStates.Count,
            hardFactCount = package.HardContinuityFacts.Count,
            knowledgeBindingCount = package.KnowledgeBindings.Count,
            acceptedCreativeIntentCount = package.AcceptedCreativeIntents.Count,
            sourceRevisionPlanCount = package.SourceRevisionPlans.Count,
            rebuiltFromPackageIds,
            longDistanceRecallCount = package.LongDistanceRecall.Count,
            warningCount = package.Warnings.Count
        };
        const string message = "章节生产包已构建并持久化。";

        await AppendPrePackageStageEventsAsync(
                request,
                chapterIdentity.CanonicalChapterId,
                createdPackage.Id,
                package,
                cancellationToken)
            .ConfigureAwait(false);

        await _eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapterIdentity.CanonicalChapterId,
                    PackageId: createdPackage.Id,
                    EventType: "chapter_context_package_built",
                    Stage: NovelAgentProductionStages.PackageBuilt,
                    Status: "completed",
                    Message: message,
                    ArtifactType: "tianming_package",
                    ArtifactId: createdPackage.Id,
                    Data: eventData),
                cancellationToken)
            .ConfigureAwait(false);
        await _runtimeEvents.AppendAsync(
                new CreateAgentRuntimeEventRequest(
                    RuntimeRunId: string.IsNullOrWhiteSpace(request.AgentRuntimeRunId)
                        ? request.RuntimeRunId
                        : request.AgentRuntimeRunId.Trim(),
                    UserId: request.UserId,
                    SessionId: request.SessionId,
                    ProjectId: request.ProjectId,
                    Type: "production_progress",
                    Message: message,
                    Data: eventData,
                    Stage: NovelAgentProductionStages.PackageBuilt,
                    Status: "completed",
                    ArtifactType: "tianming_package",
                    ArtifactId: createdPackage.Id,
                    DisplaySurface: AgentRuntimeEventSurface.Workflow,
                    DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                    PublishToSse: true),
                cancellationToken)
            .ConfigureAwait(false);

        return createdPackage;
    }

    private async Task AppendPrePackageStageEventsAsync(
        RecordChapterContextPackageRequest request,
        string chapterId,
        string packageId,
        ChapterContextPackageSummary package,
        CancellationToken cancellationToken)
    {
        var stages = BuildPrePackageStageEvents(package);
        foreach (var stage in stages)
        {
            await _eventWriter.AppendChapterStageAsync(
                    new AppendChapterProductionEventRequest(
                        RuntimeRunId: request.RuntimeRunId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: chapterId,
                        PackageId: packageId,
                        EventType: stage.EventType,
                        Stage: stage.Stage,
                        Status: "completed",
                        Message: stage.Message,
                        ArtifactType: "tianming_package",
                        ArtifactId: packageId,
                        Data: stage.Data),
                    cancellationToken)
                .ConfigureAwait(false);

            await _runtimeEvents.AppendAsync(
                    new CreateAgentRuntimeEventRequest(
                        RuntimeRunId: string.IsNullOrWhiteSpace(request.AgentRuntimeRunId)
                            ? request.RuntimeRunId
                            : request.AgentRuntimeRunId.Trim(),
                        UserId: request.UserId,
                        SessionId: request.SessionId,
                        ProjectId: request.ProjectId,
                        Type: "production_progress",
                        Message: stage.Message,
                        Data: stage.Data,
                        Stage: stage.Stage,
                        Status: "completed",
                        ArtifactType: "tianming_package",
                        ArtifactId: packageId,
                        DisplaySurface: AgentRuntimeEventSurface.Workflow,
                        DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                        PublishToSse: true),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<PrePackageStageEvent> BuildPrePackageStageEvents(
        ChapterContextPackageSummary package)
    {
        var bindings = package.KnowledgeBindings;
        var classifiedCount = bindings.Count(binding => !string.IsNullOrWhiteSpace(binding.ClassificationId));
        var pendingClassificationCount = bindings.Count - classifiedCount;
        var hardConstraintCount = bindings.Count(binding =>
            string.Equals(binding.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase));
        var referenceCount = bindings.Count(binding =>
            string.Equals(binding.ConstraintLevel, "Reference", StringComparison.OrdinalIgnoreCase));
        var importedCount = bindings.Count(binding =>
            string.Equals(binding.ProjectUsageStatus, "imported", StringComparison.OrdinalIgnoreCase));
        var referencedCount = bindings.Count(binding =>
            string.Equals(binding.ProjectUsageStatus, "referenced", StringComparison.OrdinalIgnoreCase));
        var hardDesignRuleCount = package.DesignRules.Count(rule =>
            string.Equals(rule.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase));
        var blueprint = package.PersistedBlueprint;

        return new[]
        {
            new PrePackageStageEvent(
                "chapter_knowledge_resolved",
                NovelAgentProductionStages.KnowledgeResolved,
                $"项目知识已进入章节生产包：绑定 {bindings.Count} 条，硬事实 {package.HardContinuityFacts.Count} 条，RAG 查询 {package.RagQueries.Count} 条。",
                new
                {
                    knowledgeBindingCount = bindings.Count,
                    hardContinuityFactCount = package.HardContinuityFacts.Count,
                    ragQueryCount = package.RagQueries.Count,
                    hardConstraintCount,
                    referenceCount,
                    importedCount,
                    referencedCount,
                    shouldEnterGateCount = bindings.Count(binding => binding.ShouldEnterGate),
                    shouldEnterBlueprintCount = bindings.Count(binding => binding.ShouldEnterBlueprint),
                    shouldEnterFactSnapshotCount = bindings.Count(binding => binding.ShouldEnterFactSnapshot)
                }),
            new PrePackageStageEvent(
                "chapter_knowledge_classified",
                NovelAgentProductionStages.KnowledgeClassified,
                $"项目知识分类状态已汇总：已分类 {classifiedCount} 条，待分类 {pendingClassificationCount} 条。",
                new
                {
                    knowledgeBindingCount = bindings.Count,
                    classifiedCount,
                    pendingClassificationCount,
                    classificationModels = bindings
                        .Select(binding => binding.ClassificationModel)
                        .Where(model => !string.IsNullOrWhiteSpace(model))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToArray(),
                    hardConstraintCount,
                    referenceCount
                }),
            new PrePackageStageEvent(
                "chapter_story_design_built",
                NovelAgentProductionStages.StoryDesignBuilt,
                $"故事规则输入已整理：世界规则 {package.WorldRules.Count} 条，角色状态 {package.CharacterStates.Count} 条，设计规则 {package.DesignRules.Count} 条。",
                new
                {
                    worldRuleCount = package.WorldRules.Count,
                    characterStateCount = package.CharacterStates.Count,
                    activeConflictCount = package.ActiveConflicts.Count,
                    activeForeshadowingCount = package.ActiveForeshadowing.Count,
                    designRuleCount = package.DesignRules.Count,
                    hardDesignRuleCount,
                    acceptedCreativeIntentCount = package.AcceptedCreativeIntents.Count,
                    sourceRevisionPlanCount = package.SourceRevisionPlans.Count
                }),
            new PrePackageStageEvent(
                "chapter_blueprint_built",
                NovelAgentProductionStages.ChapterBlueprintBuilt,
                blueprint == null
                    ? $"章节蓝图输入已整理：蓝图片段 {package.ChapterBlueprints.Count} 条。"
                    : $"章节蓝图已进入生产包：{blueprint.Title}。",
                new
                {
                    blueprintLineCount = package.ChapterBlueprints.Count,
                    blueprintId = blueprint?.BlueprintId ?? string.Empty,
                    blueprintTitle = blueprint?.Title ?? string.Empty,
                    blueprintVersion = blueprint?.Version ?? 0,
                    blueprintStatus = blueprint?.Status ?? string.Empty,
                    keyEventCount = blueprint?.KeyEvents.Count ?? 0,
                    characterCount = blueprint?.Characters.Count ?? 0,
                    requiredKnowledgeCount = blueprint?.RequiredKnowledgeIds.Count ?? 0,
                    appliedDesignRuleCount = blueprint?.AppliedDesignRuleIds.Count ?? 0,
                    dependencyChapterCount = blueprint?.DependencyChapterIds.Count ?? 0,
                    blueprint?.TargetWordCount,
                    longDistanceRecallCount = package.LongDistanceRecall.Count,
                    previousSummaryCount = package.PreviousSummaries.Count
                })
        };
    }

    private async Task ThrowIfOpenHardKnowledgeConflictAsync(
        RecordChapterContextPackageRequest request,
        string chapterId,
        CancellationToken cancellationToken)
    {
        if (_db == null)
            return;

        var conflicts = await _db.KnowledgeConflictReports
            .AsNoTracking()
            .Where(report =>
                report.UserId == request.UserId &&
                report.ProjectId == request.ProjectId &&
                report.Status == "open" &&
                report.Severity == "Hard")
            .OrderByDescending(report => report.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (conflicts.Count == 0)
            return;

        var first = conflicts[0];
        var conflictIds = conflicts.Select(x => x.Id).ToArray();
        var data = new
        {
            conflictIds,
            conflictCount = conflicts.Count,
            firstConflictId = first.Id,
            firstKnowledgeId = first.KnowledgeId,
            first.ConflictType,
            first.Severity,
            first.ImpactScope,
            first.Explanation,
            conflictRecommendedAction = first.RecommendedAction,
            requiresUserDecision = true,
            failedStage = NovelAgentProductionStages.PackageBuilt,
            recommendedAction = "ResolveKnowledgeConflict"
        };

        await _eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapterId,
                    PackageId: null,
                    EventType: "knowledge_conflict_blocked",
                    Stage: NovelAgentProductionStages.PackageBuilt,
                    Status: "blocked",
                    Message: "存在未解决的硬知识冲突，章节生产包已暂停构建。",
                    ArtifactType: "knowledge_conflict_report",
                    ArtifactId: first.Id,
                    Data: data),
                cancellationToken)
            .ConfigureAwait(false);

        await _runtimeEvents.AppendAsync(
                new CreateAgentRuntimeEventRequest(
                    RuntimeRunId: string.IsNullOrWhiteSpace(request.AgentRuntimeRunId)
                        ? request.RuntimeRunId
                        : request.AgentRuntimeRunId.Trim(),
                    UserId: request.UserId,
                    SessionId: request.SessionId,
                    ProjectId: request.ProjectId,
                    Type: "production_progress",
                    Message: "存在未解决的硬知识冲突，章节生产包已暂停构建。",
                    Data: data,
                    Stage: NovelAgentProductionStages.PackageBuilt,
                    Status: "blocked",
                    ArtifactType: "knowledge_conflict_report",
                    ArtifactId: first.Id,
                    DisplaySurface: AgentRuntimeEventSurface.Workflow,
                    DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                    PublishToSse: true),
                cancellationToken)
            .ConfigureAwait(false);

        throw new KnowledgeConflictBlockedException(
            conflictIds,
            first.Id,
            first.Explanation,
            first.RecommendedAction);
    }

    public Task<bool> ExistsAsync(
        string projectId,
        string runtimeRunId,
        string packageId,
        CancellationToken cancellationToken = default) =>
        _truthStore.PackageExistsAsync(projectId, runtimeRunId, packageId, cancellationToken);

    private static string BuildInputJson(
        RecordChapterContextPackageRequest request,
        ResolvedChapterIdentity chapterIdentity)
    {
        var package = request.Package;
        return JsonSerializer.Serialize(new
        {
            runId = request.RuntimeRunId,
            request.UserGoal,
            chapterId = chapterIdentity.CanonicalChapterId,
            logicalChapterId = chapterIdentity.LogicalChapterId,
            status = package.Status,
            builtAt = package.BuiltAt,
            worldRules = package.WorldRules,
            characterStates = package.CharacterStates,
            activeConflicts = package.ActiveConflicts,
            activeForeshadowing = package.ActiveForeshadowing,
            hardContinuityFacts = package.HardContinuityFacts,
            chapterBlueprints = package.ChapterBlueprints,
            previousSummaries = package.PreviousSummaries,
            longDistanceRecall = package.LongDistanceRecall,
            ragQueries = package.RagQueries,
            knowledgeBindings = package.KnowledgeBindings,
            acceptedCreativeIntents = package.AcceptedCreativeIntents,
            sourceRevisionPlans = package.SourceRevisionPlans,
            rebuiltFromPackageIds = ResolveRebuiltFromPackageIds(package),
            warnings = package.Warnings
        });
    }

    private static string BuildKnowledgeSnapshotJson(ChapterContextPackageSummary package)
    {
        return JsonSerializer.Serialize(new
        {
            hardContinuityFacts = package.HardContinuityFacts,
            ragQueries = package.RagQueries,
            knowledgeBindingSummary = BuildKnowledgeBindingSummary(package),
            knowledgeBindings = package.KnowledgeBindings,
            acceptedCreativeIntents = package.AcceptedCreativeIntents,
            sourceRevisionPlans = package.SourceRevisionPlans,
            rebuiltFromPackageIds = ResolveRebuiltFromPackageIds(package)
        });
    }

    private static object BuildKnowledgeBindingSummary(ChapterContextPackageSummary package)
    {
        var bindings = package.KnowledgeBindings;
        return new
        {
            bindingCount = bindings.Count,
            shouldEnterGateCount = bindings.Count(binding => binding.ShouldEnterGate),
            shouldEnterBlueprintCount = bindings.Count(binding => binding.ShouldEnterBlueprint),
            shouldEnterFactSnapshotCount = bindings.Count(binding => binding.ShouldEnterFactSnapshot),
            hardConstraintCount = bindings.Count(binding =>
                string.Equals(binding.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase)),
            referenceCount = bindings.Count(binding =>
                string.Equals(binding.ConstraintLevel, "Reference", StringComparison.OrdinalIgnoreCase)),
            classifiedCount = bindings.Count(binding =>
                !string.IsNullOrWhiteSpace(binding.ClassificationId)),
            pendingClassificationCount = bindings.Count(binding =>
                string.IsNullOrWhiteSpace(binding.ClassificationId)),
            importedCount = bindings.Count(binding =>
                string.Equals(binding.ProjectUsageStatus, "imported", StringComparison.OrdinalIgnoreCase)),
            referencedCount = bindings.Count(binding =>
                string.Equals(binding.ProjectUsageStatus, "referenced", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static IReadOnlyList<string> ResolveRebuiltFromPackageIds(ChapterContextPackageSummary package)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in package.SourceRevisionPlans)
        {
            foreach (var id in ParseJsonStringArray(plan.InvalidatedPackageIdsJson))
                ids.Add(id);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    yield return item.GetString()!.Trim();
            }
        }
    }

    private static string BuildFactSnapshotJson(ChapterContextPackageSummary package)
    {
        return JsonSerializer.Serialize(new
        {
            package.WorldRules,
            package.CharacterStates,
            package.ActiveConflicts,
            package.ActiveForeshadowing,
            package.PreviousSummaries,
            package.HardContinuityFacts,
            package.Warnings
        });
    }

    private async Task MarkKnowledgeBindingsUsedAsync(
        RecordChapterContextPackageRequest request,
        string chapterId,
        CancellationToken cancellationToken)
    {
        if (_db == null ||
            string.IsNullOrWhiteSpace(chapterId) ||
            request.Package.KnowledgeBindings.Count == 0)
        {
            return;
        }

        var knowledgeIds = request.Package.KnowledgeBindings
            .Select(binding => binding.KnowledgeId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (knowledgeIds.Count == 0)
            return;

        var usages = await _db.ProjectKnowledgeUsages
            .Where(usage =>
                usage.UserId == request.UserId &&
                usage.ProjectId == request.ProjectId &&
                knowledgeIds.Contains(usage.KnowledgeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (usages.Count == 0)
            return;

        var changed = false;
        chapterId = chapterId.Trim();
        foreach (var usage in usages)
        {
            var chapters = ParseUsedByChapters(usage.UsedByChaptersJson);
            var firstUseInChapter = !chapters.Contains(chapterId, StringComparer.OrdinalIgnoreCase);
            if (firstUseInChapter)
                chapters.Add(chapterId);

            usage.Status = "referenced";
            usage.LastUsedAt = DateTime.UtcNow;
            usage.UsedByChaptersJson = JsonSerializer.Serialize(chapters);
            if (firstUseInChapter)
                usage.UsageCount++;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<string> ParseUsedByChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed != null)
            {
                return parsed
                    .Where(chapter => !string.IsNullOrWhiteSpace(chapter))
                    .Select(chapter => chapter.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(128)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fall back to delimiter parsing for manually repaired rows.
        }

        return json
            .Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToList();
    }

    private async Task<ResolvedChapterIdentity> ResolveChapterIdentityAsync(
        string projectId,
        string? chapterId,
        CancellationToken cancellationToken)
    {
        var logicalChapterId = chapterId?.Trim() ?? string.Empty;
        if (_db == null || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(logicalChapterId))
            return new ResolvedChapterIdentity(logicalChapterId, logicalChapterId);

        var canonicalChapterId = await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                projectId,
                logicalChapterId,
                cancellationToken)
            .ConfigureAwait(false);

        return new ResolvedChapterIdentity(canonicalChapterId ?? logicalChapterId, logicalChapterId);
    }

    private sealed record ResolvedChapterIdentity(string CanonicalChapterId, string LogicalChapterId);

    private sealed record PrePackageStageEvent(
        string EventType,
        string Stage,
        string Message,
        object Data);
}
