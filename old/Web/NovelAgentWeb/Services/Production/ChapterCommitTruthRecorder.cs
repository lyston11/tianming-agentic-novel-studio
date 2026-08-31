using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterCommitTruthRecorder : IChapterCommitTruthRecorder
{
    private readonly NovelAgentDbContext _db;
    private readonly IProductionTruthStore _truthStore;
    private readonly IProductionEventWriter _eventWriter;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public ChapterCommitTruthRecorder(
        NovelAgentDbContext db,
        IProductionTruthStore truthStore,
        IProductionEventWriter eventWriter,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _db = db;
        _truthStore = truthStore;
        _eventWriter = eventWriter;
        _outputArtifacts = outputArtifacts;
    }

    public async Task<ChapterCommitTruthRecord> RecordAsync(
        RecordChapterCommitTruthRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new InvalidOperationException("Chapter commit truth requires runtimeRunId, userId and projectId.");
        }

        var chapter = await ResolveCommittedChapterAsync(request.ProjectId, request.TargetChapterId, cancellationToken)
            .ConfigureAwait(false);
        if (chapter == null)
            throw new InvalidOperationException($"Committed chapter cannot be resolved for project {request.ProjectId} and target {request.TargetChapterId}.");

        var existingRecord = await FindExistingCommitRecordAsync(request, chapter, cancellationToken)
            .ConfigureAwait(false);
        if (existingRecord != null)
            return existingRecord;

        await RebindPreCommitProductionEvidenceAsync(request, chapter, cancellationToken)
            .ConfigureAwait(false);

        var gateJson = request.GateReport == null
            ? null
            : JsonSerializer.Serialize(request.GateReport);
        var reviewJson = request.PostGenerationReview == null
            ? null
            : JsonSerializer.Serialize(request.PostGenerationReview);

        var version = await _truthStore.AttachLatestChapterVersionToRunAsync(
                request.ProjectId,
                chapter.Id,
                request.RuntimeRunId,
                PackageId: request.ContextPackage?.PackageId,
                GateReportJson: gateJson,
                AgentReviewJson: reviewJson,
                cancellationToken)
            .ConfigureAwait(false);
        var factSnapshot = await _truthStore.SaveFactSnapshotAsync(
                new SaveProjectFactSnapshotRequest(
                    request.UserId,
                    request.ProjectId,
                    chapter.Id,
                    version.Id,
                    BuildCommittedChapterFactSnapshotJson(request, chapter, version),
                    "chapter_commit"),
                cancellationToken)
            .ConfigureAwait(false);
        await _truthStore.MarkChapterChangesAppliedToFactSnapshotAsync(
                new MarkChapterChangesAppliedToFactSnapshotRequest(
                    request.UserId,
                    request.ProjectId,
                    request.RuntimeRunId,
                    chapter.Id,
                    request.ContextPackage?.PackageId ?? version.PackageId),
                cancellationToken)
            .ConfigureAwait(false);

        var evt = await _eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapter.Id,
                    PackageId: version.PackageId,
                    EventType: "chapter_committed",
                    Stage: NovelAgentProductionStages.ChapterCommitted,
                    Status: "completed",
                    Message: request.Message,
                    ArtifactType: "chapter_version",
                    ArtifactId: version.Id,
                    Data: new
                    {
                        chapterId = chapter.Id,
                        chapterNumber = chapter.ChapterNumber,
                        chapterTitle = chapter.Title,
                        contentDocumentId = version.ContentDocumentId,
                        versionNumber = version.VersionNumber,
                        wordCount = version.WordCount,
                        factSnapshotId = factSnapshot.Id,
                        factSnapshotVersion = factSnapshot.VersionNumber
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        if (_outputArtifacts != null)
        {
            await _outputArtifacts.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: request.RuntimeRunId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: chapter.Id,
                        PackageId: version.PackageId,
                        ToolName: "ProduceChapter",
                        Stage: NovelAgentProductionStages.ChapterCommitted,
                        Status: "completed",
                        ArtifactType: "chapter_version",
                        ArtifactId: version.Id,
                        OutputKind: "FinalArtifact",
                        Summary: request.Message,
                        UserVisibleWhere: new[] { "小说书城", "创作工作流" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: true,
                        SourceEventType: evt.EventType,
                        SourceEventId: evt.Id,
                        Data: new
                        {
                            chapterId = chapter.Id,
                            chapterNumber = chapter.ChapterNumber,
                            chapterTitle = chapter.Title,
                            contentDocumentId = version.ContentDocumentId,
                            versionNumber = version.VersionNumber,
                            wordCount = version.WordCount,
                            factSnapshotId = factSnapshot.Id,
                            factSnapshotVersion = factSnapshot.VersionNumber
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await AppendKnowledgeBindingsUsedEventAsync(request, chapter, version, cancellationToken)
            .ConfigureAwait(false);

        await MarkSourceRevisionPlansExecutedAsync(request, chapter, version, cancellationToken)
            .ConfigureAwait(false);

        return new ChapterCommitTruthRecord(chapter, version, factSnapshot, evt);
    }

    private async Task AppendKnowledgeBindingsUsedEventAsync(
        RecordChapterCommitTruthRequest request,
        Chapter chapter,
        ChapterVersion version,
        CancellationToken cancellationToken)
    {
        var package = request.ContextPackage;
        if (package == null || package.KnowledgeBindings.Count == 0)
            return;

        var bindings = package.KnowledgeBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId) ||
                              !string.IsNullOrWhiteSpace(binding.Title))
            .Select(binding => new
            {
                knowledgeId = binding.KnowledgeId,
                title = binding.Title,
                entryType = binding.EntryType,
                projectUsageStatus = "used",
                weight = binding.Weight,
                role = binding.Role,
                scope = binding.Scope,
                priority = binding.Priority,
                constraintLevel = binding.ConstraintLevel,
                packagePolicy = binding.PackagePolicy,
                boundVersion = binding.BoundVersion,
                classificationId = binding.ClassificationId,
                classificationModel = binding.ClassificationModel,
                classificationRule = binding.ClassificationRule,
                targetEntities = binding.TargetEntities,
                shouldEnterGate = binding.ShouldEnterGate,
                shouldEnterBlueprint = binding.ShouldEnterBlueprint,
                shouldEnterFactSnapshot = binding.ShouldEnterFactSnapshot,
                classificationConfidence = binding.ClassificationConfidence
            })
            .Take(24)
            .ToList();
        if (bindings.Count == 0)
            return;

        await _eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: request.RuntimeRunId,
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    ChapterId: chapter.Id,
                    PackageId: version.PackageId,
                    EventType: "knowledge_bindings_used",
                    Stage: NovelAgentProductionStages.FactsPersisted,
                    Status: "completed",
                    Message: $"本章生产包实际使用 {bindings.Count} 条项目知识绑定。",
                    ArtifactType: "knowledge_bindings",
                    ArtifactId: chapter.Id,
                    Data: new
                    {
                        packageId = version.PackageId,
                        chapterId = chapter.Id,
                        packageChapterId = package.ChapterId,
                        bindingCount = bindings.Count,
                        knowledgeIds = bindings
                            .Select(binding => binding.knowledgeId)
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .ToList(),
                        bindings
                    }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChapterCommitTruthRecord?> FindExistingCommitRecordAsync(
        RecordChapterCommitTruthRequest request,
        Chapter chapter,
        CancellationToken cancellationToken)
    {
        var version = await _db.ChapterVersions
            .Where(v => v.ProjectId == request.ProjectId && v.ChapterId == chapter.Id)
            .OrderByDescending(v => v.VersionNumber)
            .ThenByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (version == null ||
            !string.Equals(version.RuntimeRunId, request.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var evt = await _db.ProductionEvents
            .Where(e =>
                e.UserId == request.UserId &&
                e.ProjectId == request.ProjectId &&
                e.RuntimeRunId == request.RuntimeRunId &&
                e.ChapterId == chapter.Id &&
                e.EventType == "chapter_committed" &&
                e.ArtifactType == "chapter_version" &&
                e.ArtifactId == version.Id)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (evt == null)
            return null;

        var factSnapshot = await _db.ProjectFactSnapshots
            .Where(snapshot =>
                snapshot.UserId == request.UserId &&
                snapshot.ProjectId == request.ProjectId &&
                snapshot.ChapterId == chapter.Id &&
                snapshot.ChapterVersionId == version.Id &&
                snapshot.Source == "chapter_commit")
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ThenByDescending(snapshot => snapshot.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (factSnapshot == null)
            return null;

        return new ChapterCommitTruthRecord(chapter, version, factSnapshot, evt);
    }

    private async Task RebindPreCommitProductionEvidenceAsync(
        RecordChapterCommitTruthRequest request,
        Chapter chapter,
        CancellationToken cancellationToken)
    {
        var logicalChapterId = request.TargetChapterId?.Trim();
        if (string.IsNullOrWhiteSpace(logicalChapterId) ||
            string.Equals(logicalChapterId, chapter.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var reviews = await _db.AgentReviews
            .Where(review =>
                review.UserId == request.UserId &&
                review.ProjectId == request.ProjectId &&
                review.RuntimeRunId == request.RuntimeRunId &&
                review.ChapterId == logicalChapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var review in reviews)
        {
            review.ChapterId = chapter.Id;
        }

        var gates = await _db.GenerationGateReports
            .Where(report =>
                report.UserId == request.UserId &&
                report.ProjectId == request.ProjectId &&
                report.RuntimeRunId == request.RuntimeRunId &&
                report.ChapterId == logicalChapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var gate in gates)
            gate.ChapterId = chapter.Id;

        var drafts = await _db.ChapterDrafts
            .Where(draft =>
                draft.UserId == request.UserId &&
                draft.ProjectId == request.ProjectId &&
                draft.RuntimeRunId == request.RuntimeRunId &&
                draft.ChapterId == logicalChapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var draft in drafts)
        {
            draft.ChapterId = chapter.Id;
            draft.UpdatedAt = now;
        }

        var changes = await _db.ChapterChanges
            .Where(change =>
                change.UserId == request.UserId &&
                change.ProjectId == request.ProjectId &&
                change.RuntimeRunId == request.RuntimeRunId &&
                change.ChapterId == logicalChapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var change in changes)
            change.ChapterId = chapter.Id;

        var events = await _db.ProductionEvents
            .Where(evt =>
                evt.UserId == request.UserId &&
                evt.ProjectId == request.ProjectId &&
                evt.RuntimeRunId == request.RuntimeRunId &&
                evt.ChapterId == logicalChapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var evt in events)
            evt.ChapterId = chapter.Id;

        var usages = await _db.ProjectKnowledgeUsages
            .Where(usage =>
                usage.UserId == request.UserId &&
                usage.ProjectId == request.ProjectId &&
                usage.UsedByChaptersJson != null &&
                usage.UsedByChaptersJson != string.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var usageRebindCount = 0;
        foreach (var usage in usages)
        {
            var chapters = ParseUsedByChapters(usage.UsedByChaptersJson);
            if (!chapters.Contains(logicalChapterId, StringComparer.OrdinalIgnoreCase))
                continue;

            chapters = chapters
                .Where(id => !string.Equals(id, logicalChapterId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!chapters.Contains(chapter.Id, StringComparer.OrdinalIgnoreCase))
                chapters.Add(chapter.Id);

            usage.UsedByChaptersJson = JsonSerializer.Serialize(chapters);
            usageRebindCount++;
        }

        if (reviews.Count + gates.Count + drafts.Count + changes.Count + events.Count + usageRebindCount > 0)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<string> ParseUsedByChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string BuildCommittedChapterFactSnapshotJson(
        RecordChapterCommitTruthRequest request,
        Chapter chapter,
        ChapterVersion version)
    {
        var package = request.ContextPackage;
        var draft = request.DraftArtifact;
        var review = request.PostGenerationReview;
        var gate = request.GateReport;

        var snapshot = new
        {
            chapterId = chapter.Id,
            targetChapterId = request.TargetChapterId ?? string.Empty,
            chapterTitle = chapter.Title,
            chapterNumber = chapter.ChapterNumber,
            chapterVersionId = version.Id,
            versionNumber = version.VersionNumber,
            status = version.Status,
            wordCount = version.WordCount,
            committedAt = version.CreatedAt,
            packageId = package?.PackageId ?? string.Empty,
            packageStatus = package?.Status ?? string.Empty,
            sourceRevisionPlans = package?.SourceRevisionPlans ?? new List<RevisionPlanSnapshot>(),
            worldRules = package?.WorldRules ?? new List<string>(),
            characterStates = package?.CharacterStates ?? new List<string>(),
            activeConflicts = package?.ActiveConflicts ?? new List<string>(),
            activeForeshadowing = package?.ActiveForeshadowing ?? new List<string>(),
            hardContinuityFacts = package?.HardContinuityFacts ?? new List<string>(),
            previousSummaries = package?.PreviousSummaries ?? new List<string>(),
            longDistanceRecall = package?.LongDistanceRecall ?? new List<string>(),
            chapterBlueprints = package?.ChapterBlueprints ?? new List<string>(),
            gateStatus = gate?.Status ?? string.Empty,
            gateIssues = gate?.Issues ?? new List<string>(),
            reviewResult = review?.OverallResult ?? string.Empty,
            reviewSummary = review?.Summary ?? string.Empty,
            changesJson = draft?.ChangesJson ?? string.Empty,
            knowledgeConstraintEvidence = BuildKnowledgeConstraintEvidence(package, gate),
            protagonistName = request.ContinuityFacts?.ProtagonistName ?? string.Empty,
            protagonistIdentity = request.ContinuityFacts?.ProtagonistIdentity ?? string.Empty,
            protagonistStatus = request.ContinuityFacts?.ProtagonistStatus ?? string.Empty,
            currentLocation = request.ContinuityFacts?.CurrentLocation ?? string.Empty,
            systemState = request.ContinuityFacts?.SystemState ?? string.Empty,
            equipmentState = request.ContinuityFacts?.EquipmentState ?? string.Empty,
            keyEvents = request.ContinuityFacts?.KeyEvents ?? new List<string>(),
            endingState = request.ContinuityFacts?.EndingState ?? string.Empty,
            chapterEndingState = request.ContinuityFacts?.EndingState ?? string.Empty,
            continuityFactsExtractedAt = request.ContinuityFacts?.ExtractedAt,
            continuityFactsSourceRunId = request.ContinuityFacts?.SourceRunId ?? string.Empty,
            nextChapterMustCarry = BuildNextChapterCarry(package, request.ContinuityFacts)
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private async Task MarkSourceRevisionPlansExecutedAsync(
        RecordChapterCommitTruthRequest request,
        Chapter chapter,
        ChapterVersion version,
        CancellationToken cancellationToken)
    {
        var revisionPlanIds = request.ContextPackage?.SourceRevisionPlans
            .Select(plan => plan.RevisionPlanId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        if (revisionPlanIds.Length == 0)
            return;

        var plans = await _db.RevisionPlans
            .Where(plan =>
                plan.UserId == request.UserId &&
                plan.ProjectId == request.ProjectId &&
                revisionPlanIds.Contains(plan.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (plans.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var plan in plans)
        {
            plan.Status = "executed";
            plan.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var plan in plans)
        {
            await _eventWriter.AppendChapterStageAsync(
                    new AppendChapterProductionEventRequest(
                        RuntimeRunId: request.RuntimeRunId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: chapter.Id,
                        PackageId: null,
                        EventType: "revision_plan_executed",
                        Stage: NovelAgentProductionStages.ChapterCommitted,
                        Status: "executed",
                        Message: $"修订计划已随章节提交完成执行：{plan.Recommendation}",
                        ArtifactType: "RevisionPlan",
                        ArtifactId: plan.Id,
                        Data: new
                        {
                            revisionPlanId = plan.Id,
                            chapterId = chapter.Id,
                            chapterVersionId = version.Id,
                            contentDocumentId = version.ContentDocumentId,
                            packageId = version.PackageId,
                            plan.PlanType,
                            plan.TargetScope,
                            plan.TargetChapterId,
                            plan.RequirementsJson,
                            plan.ContinuityRequirementsJson
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<object> BuildKnowledgeConstraintEvidence(
        ChapterContextPackageSummary? package,
        GenerationGateReport? gate)
    {
        if (package?.KnowledgeBindings == null || package.KnowledgeBindings.Count == 0)
            return Array.Empty<object>();

        return package.KnowledgeBindings
            .Where(binding => binding.ShouldEnterFactSnapshot)
            .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId) ||
                              !string.IsNullOrWhiteSpace(binding.Title) ||
                              !string.IsNullOrWhiteSpace(binding.Content))
            .Take(24)
            .Select(binding => new
            {
                knowledgeId = binding.KnowledgeId,
                title = binding.Title,
                entryType = binding.EntryType,
                role = binding.Role,
                scope = binding.Scope,
                priority = binding.Priority,
                constraintLevel = binding.ConstraintLevel,
                packagePolicy = binding.PackagePolicy,
                boundVersion = binding.BoundVersion,
                usedByChapters = binding.UsedByChapters,
                classificationId = binding.ClassificationId,
                classificationModel = binding.ClassificationModel,
                classificationRule = binding.ClassificationRule,
                targetEntities = binding.TargetEntities,
                shouldEnterGate = binding.ShouldEnterGate,
                shouldEnterBlueprint = binding.ShouldEnterBlueprint,
                shouldEnterFactSnapshot = binding.ShouldEnterFactSnapshot,
                classificationConfidence = binding.ClassificationConfidence,
                gateStatus = gate?.Status ?? string.Empty,
                evidenceStatus = InferKnowledgeEvidenceStatus(binding, gate)
            })
            .Cast<object>()
            .ToList();
    }

    private static string InferKnowledgeEvidenceStatus(
        BoundKnowledgeSnapshot binding,
        GenerationGateReport? gate)
    {
        if (gate == null || string.IsNullOrWhiteSpace(gate.Status))
            return "unknown";
        if (string.Equals(gate.Status, "validated", StringComparison.OrdinalIgnoreCase))
            return "satisfied";

        var haystack = string.Join("\n", gate.Issues.Concat(gate.RepairHints));
        if (ContainsKnowledgeToken(haystack, binding.KnowledgeId) ||
            ContainsKnowledgeToken(haystack, binding.Title) ||
            ContainsKnowledgeToken(haystack, binding.Content))
        {
            return "violated";
        }

        return "unknown";
    }

    private static bool ContainsKnowledgeToken(string haystack, string? value)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Length <= 24)
            return haystack.Contains(text, StringComparison.OrdinalIgnoreCase);

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Take(16)
            .Any(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildNextChapterCarry(
        ChapterContextPackageSummary? package,
        ChapterContinuityFacts? facts)
    {
        var carry = new List<string>();
        if (facts?.NextChapterMustCarry.Any(line => !string.IsNullOrWhiteSpace(line)) == true)
        {
            carry.AddRange(facts.NextChapterMustCarry);
        }
        else if (package != null)
        {
            carry.AddRange(package.HardContinuityFacts);
            carry.AddRange(package.ActiveConflicts);
            carry.AddRange(package.ActiveForeshadowing);
            carry.AddRange(package.CharacterStates);
        }

        return carry
            .Select(NormalizeNarrativeCarryLine)
            .Where(IsNarrativeCarryLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static string NormalizeNarrativeCarryLine(string? line)
    {
        var value = line?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return string.Empty;

        string[] recursivePrefixes =
        {
            "上一章硬事实：",
            "上一章硬事实:",
            "上一章世界规则：",
            "上一章世界规则:",
            "上一章冲突：",
            "上一章冲突:",
            "上一章伏笔：",
            "上一章伏笔:",
            "上一章角色状态：",
            "上一章角色状态:",
            "知识库硬事实：",
            "知识库硬事实:"
        };

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var prefix in recursivePrefixes)
            {
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                value = value[prefix.Length..].Trim();
                changed = true;
            }
        }

        return value;
    }

    private static bool IsNarrativeCarryLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var value = line.Trim();
        if (value.StartsWith("上一章已提交", StringComparison.Ordinal) ||
            value.StartsWith("上一章章节ID", StringComparison.Ordinal) ||
            value.StartsWith("上一章评审结论", StringComparison.Ordinal) ||
            value.StartsWith("上一章门禁关注", StringComparison.Ordinal) ||
            value.StartsWith("AgentReview修订要求", StringComparison.Ordinal) ||
            value.StartsWith("质量评审", StringComparison.Ordinal) ||
            value.StartsWith("评审阻塞项", StringComparison.Ordinal) ||
            value.StartsWith("修订建议", StringComparison.Ordinal) ||
            value.Contains("章节ID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<Chapter?> ResolveCommittedChapterAsync(
        string projectId,
        string? chapterId,
        CancellationToken cancellationToken)
    {
        var canonicalChapterId = await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                projectId,
                chapterId,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonicalChapterId))
            return null;

        return await _db.Chapters
            .Where(c => c.ProjectId == projectId)
            .FirstOrDefaultAsync(c => c.Id == canonicalChapterId, cancellationToken)
            .ConfigureAwait(false);
    }
}
