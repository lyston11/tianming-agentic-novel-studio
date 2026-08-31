using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeConflictResolver : IKnowledgeConflictResolver
{
    private static readonly string[] AllowedDecisions =
    {
        "resolved",
        "rejected",
        "superseded"
    };

    private readonly NovelAgentDbContext _db;
    private readonly IProductionEventWriter? _productionEvents;
    private readonly IAgentRuntimeEventService? _runtimeEvents;
    private readonly ICreativeIntentService? _creativeIntents;
    private readonly IRevisionPlanService? _revisionPlans;
    private readonly IOutputArtifactRecorder? _outputArtifacts;
    private readonly IKnowledgeCanonConflictStatusService? _canonConflictStatus;

    public KnowledgeConflictResolver(
        NovelAgentDbContext db,
        IProductionEventWriter? productionEvents = null,
        IAgentRuntimeEventService? runtimeEvents = null,
        ICreativeIntentService? creativeIntents = null,
        IRevisionPlanService? revisionPlans = null,
        IOutputArtifactRecorder? outputArtifacts = null,
        IKnowledgeCanonConflictStatusService? canonConflictStatus = null)
    {
        _db = db;
        _productionEvents = productionEvents;
        _runtimeEvents = runtimeEvents;
        _creativeIntents = creativeIntents;
        _revisionPlans = revisionPlans;
        _outputArtifacts = outputArtifacts;
        _canonConflictStatus = canonConflictStatus;
    }

    public async Task<KnowledgeConflictResolutionResult> ResolveAsync(
        KnowledgeConflictResolutionRequest request,
        CancellationToken ct = default)
    {
        var status = NormalizeDecision(request.Decision);
        var note = (request.Note ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("解决知识冲突必须写明处理理由。");

        var report = await _db.KnowledgeConflictReports
            .Where(x =>
                x.Id == request.ConflictId &&
                x.UserId == request.UserId &&
                x.ProjectId == request.ProjectId)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (report == null)
            throw new InvalidOperationException("知识冲突报告不存在或无权访问。");

        var previousStatus = report.Status;
        report.Status = status;
        report.ResolutionNote = note;
        report.ResolvedAt = DateTime.UtcNow;
        report.ResolvedBySessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();
        report.ResolvedByRunId = string.IsNullOrWhiteSpace(request.RunId) ? null : request.RunId.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ApplyCanonResolutionStatusAsync(request, report, ct).ConfigureAwait(false);

        if (string.Equals(previousStatus, "open", StringComparison.OrdinalIgnoreCase))
        {
            var runId = FirstNonEmpty(request.RunId, report.SourceRunId);
            var invalidatedPackageIds = string.IsNullOrWhiteSpace(runId)
                ? Array.Empty<string>()
                : await MarkContextPackagesStaleAsync(request.UserId, request.ProjectId, runId, ct)
                    .ConfigureAwait(false);
            var creativeIntent = await RecordResolutionCreativeIntentAsync(request, report, ct).ConfigureAwait(false);
            await RecordResolutionRevisionPlanAsync(request, report, creativeIntent, invalidatedPackageIds, ct)
                .ConfigureAwait(false);
            await AppendRecoverySignalAsync(request, report, invalidatedPackageIds, ct).ConfigureAwait(false);
        }

        return new KnowledgeConflictResolutionResult
        {
            ConflictId = report.Id,
            UserId = report.UserId,
            ProjectId = report.ProjectId,
            KnowledgeId = report.KnowledgeId,
            PreviousStatus = previousStatus,
            Status = report.Status,
            Note = report.ResolutionNote ?? string.Empty,
            ResolvedAt = report.ResolvedAt ?? DateTime.UtcNow
        };
    }

    private async Task ApplyCanonResolutionStatusAsync(
        KnowledgeConflictResolutionRequest request,
        Data.Entities.KnowledgeConflictReport report,
        CancellationToken ct)
    {
        if (_canonConflictStatus == null)
            return;

        await _canonConflictStatus.ApplyResolutionAsync(
                new KnowledgeCanonConflictResolutionStatusRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    KnowledgeId: report.KnowledgeId,
                    ReportId: report.Id,
                    ResolutionStatus: report.Status,
                    ResolutionNote: report.ResolutionNote ?? string.Empty,
                    Severity: report.Severity,
                    ConflictType: report.ConflictType,
                    ConflictingKnowledgeIds: TryParseStringArray(report.ConflictingKnowledgeIdsJson),
                    RunId: FirstNonEmpty(request.RunId, report.SourceRunId)),
                ct)
            .ConfigureAwait(false);
    }

    private static string NormalizeDecision(string decision)
    {
        var normalized = (decision ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "resolve" or "accept" or "accepted" or "解决" or "已解决")
            normalized = "resolved";
        if (normalized is "reject" or "decline" or "ignored" or "拒绝" or "废弃")
            normalized = "rejected";
        if (normalized is "replace" or "replaced" or "supersede" or "替代")
            normalized = "superseded";

        if (!AllowedDecisions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("知识冲突处理状态只能是 resolved、rejected 或 superseded。");

        return normalized;
    }

    private async Task AppendRecoverySignalAsync(
        KnowledgeConflictResolutionRequest request,
        Data.Entities.KnowledgeConflictReport report,
        IReadOnlyList<string> invalidatedPackageIds,
        CancellationToken ct)
    {
        var runId = FirstNonEmpty(request.RunId, report.SourceRunId);
        if (string.IsNullOrWhiteSpace(runId))
            return;

        var sessionId = FirstNonEmpty(request.SessionId, report.SourceSessionId);
        var data = new
        {
            conflictId = report.Id,
            report.KnowledgeId,
            report.ConflictType,
            report.Severity,
            report.ImpactScope,
            resolutionStatus = report.Status,
            resolutionNote = report.ResolutionNote ?? string.Empty,
            invalidatedPackageIds,
            invalidatedPackageCount = invalidatedPackageIds.Count,
            requiresUserDecision = false,
            recommendedAction = "ProduceChapter",
            nextTools = new[] { "QueryProjectKnowledgeBindings", "QueryNovelProductionState", "ProduceChapter" }
        };

        const string message = "知识冲突已处理，章节生产包可以重新构建。";
        ProductionEvent? productionEvent = null;
        if (_productionEvents != null)
        {
            productionEvent = await _productionEvents.AppendChapterStageAsync(
                    new AppendChapterProductionEventRequest(
                        RuntimeRunId: runId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: null,
                        PackageId: null,
                        EventType: "knowledge_conflict_resolved",
                        Stage: NovelAgentProductionStages.KnowledgeResolved,
                        Status: "ready_for_rebuild",
                        Message: message,
                        ArtifactType: "knowledge_conflict_report",
                        ArtifactId: report.Id,
                        Data: data),
                    ct)
                .ConfigureAwait(false);
        }

        if (_outputArtifacts != null && productionEvent != null)
        {
            await _outputArtifacts.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: runId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: null,
                        PackageId: null,
                        ToolName: "ResolveKnowledgeConflict",
                        Stage: NovelAgentProductionStages.KnowledgeResolved,
                        Status: "ready_for_rebuild",
                        ArtifactType: "knowledge_conflict_resolution",
                        ArtifactId: report.Id,
                        OutputKind: "ProcessArtifact",
                        Summary: message,
                        UserVisibleWhere: new[] { "创作工作流", "知识库" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: false,
                        SourceEventType: productionEvent.EventType,
                        SourceEventId: productionEvent.Id,
                        Data: data),
                    ct)
                .ConfigureAwait(false);
        }

        if (_runtimeEvents != null && !string.IsNullOrWhiteSpace(sessionId))
        {
            await _runtimeEvents.AppendAsync(
                    new CreateAgentRuntimeEventRequest(
                        RuntimeRunId: runId,
                        UserId: request.UserId,
                        SessionId: sessionId,
                        ProjectId: request.ProjectId,
                        Type: "production_progress",
                        Message: message,
                        Data: data,
                        Stage: NovelAgentProductionStages.KnowledgeResolved,
                        Status: "ready_for_rebuild",
                        ArtifactType: "knowledge_conflict_report",
                        ArtifactId: report.Id,
                        DisplaySurface: AgentRuntimeEventSurface.Workflow,
                        DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                        PublishToSse: true),
                    ct)
                .ConfigureAwait(false);
        }
    }

    private async Task<CreativeIntentItem?> RecordResolutionCreativeIntentAsync(
        KnowledgeConflictResolutionRequest request,
        Data.Entities.KnowledgeConflictReport report,
        CancellationToken ct)
    {
        if (_creativeIntents == null)
            return null;

        var raw = $"知识冲突 {report.Id} 处理决定：{report.ResolutionNote ?? request.Note}";
        var normalized = BuildResolutionCreativeIntent(report);
        var metadataJson = JsonSerializer.Serialize(new
        {
            source = "knowledge_conflict_resolution",
            conflictId = report.Id,
            report.KnowledgeId,
            conflictingKnowledgeIds = TryParseStringArray(report.ConflictingKnowledgeIdsJson),
            report.ConflictType,
            report.Severity,
            report.ImpactScope,
            resolutionStatus = report.Status,
            resolvedAt = report.ResolvedAt,
            recommendedAction = "ProduceChapter"
        });

        var created = await _creativeIntents.CreateAsync(
                new CreateCreativeIntentRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    SessionId: FirstNonEmpty(request.SessionId, report.SourceSessionId),
                    RunId: FirstNonEmpty(request.RunId, report.SourceRunId),
                    IdempotencyKey: $"knowledge-conflict-resolution:{report.Id}",
                    RawContent: raw,
                    NormalizedIntent: normalized,
                    Source: "knowledge",
                    TargetScope: ResolveCreativeTargetScope(report.ImpactScope),
                    TargetVolumeId: string.Empty,
                    TargetChapterId: string.Empty,
                    TargetCharacterName: string.Empty,
                    ImpactLevel: ResolveCreativeImpactLevel(report),
                    RequiresConfirmation: false,
                    ConflictStatus: "resolved",
                    MetadataJson: metadataJson),
                ct)
            .ConfigureAwait(false);
        if (created == null ||
            string.Equals(created.Status, "executed", StringComparison.OrdinalIgnoreCase))
        {
            return created;
        }

        return await _creativeIntents.DecideAsync(
                new DecideCreativeIntentRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    IntentId: created.Id,
                    Status: "accepted",
                    DecisionReason: $"知识冲突 {report.Id} 已由 Agent/用户处理：{report.ResolutionNote ?? request.Note}",
                    ConflictStatus: "resolved",
                    MarkExecuted: false),
                ct)
            .ConfigureAwait(false);
    }

    private async Task RecordResolutionRevisionPlanAsync(
        KnowledgeConflictResolutionRequest request,
        Data.Entities.KnowledgeConflictReport report,
        CreativeIntentItem? creativeIntent,
        IReadOnlyList<string> invalidatedPackageIds,
        CancellationToken ct)
    {
        if (_revisionPlans == null)
            return;

        var conflictingKnowledgeIds = TryParseStringArray(report.ConflictingKnowledgeIdsJson);
        var requirements = new[]
        {
            $"遵守知识冲突 {report.Id} 的处理决定：{report.ResolutionNote ?? request.Note}",
            "后续生产不得再次使用被该冲突否定的设定。"
        };
        var continuityRequirements = new[]
        {
            "重新构建章节上下文包后再继续生产。",
            "如正文已生成但未提交，应按新知识决定重新修订或重写。"
        };
        var impactAnalysisJson = JsonSerializer.Serialize(new
        {
            source = "knowledge_conflict_resolution",
            conflictId = report.Id,
            report.KnowledgeId,
            conflictingKnowledgeIds,
            report.ConflictType,
            report.Severity,
            report.ImpactScope,
            resolutionStatus = report.Status,
            resolutionNote = report.ResolutionNote ?? string.Empty,
            invalidatedPackageIds,
            invalidatedPackageCount = invalidatedPackageIds.Count,
            requiresDownstreamAnalysis = true
        });

        await _revisionPlans.CreateAsync(
                new CreateRevisionPlanRequest(
                    UserId: request.UserId,
                    ProjectId: request.ProjectId,
                    SessionId: FirstNonEmpty(request.SessionId, report.SourceSessionId),
                    RunId: FirstNonEmpty(request.RunId, report.SourceRunId),
                    IdempotencyKey: $"knowledge-conflict-revision-plan:{report.Id}",
                    Source: "knowledge_conflict_resolution",
                    PlanType: ResolveCreativeImpactLevel(report),
                    TargetScope: ResolveCreativeTargetScope(report.ImpactScope),
                    TargetVolumeId: string.Empty,
                    TargetChapterId: string.Empty,
                    CreativeIntentId: creativeIntent?.Id ?? string.Empty,
                    KnowledgeConflictReportId: report.Id,
                    Status: "accepted",
                    RequirementsJson: JsonSerializer.Serialize(requirements),
                    ContinuityRequirementsJson: JsonSerializer.Serialize(continuityRequirements),
                    ImpactAnalysisJson: impactAnalysisJson,
                    AffectedChapterIdsJson: "[]",
                    InvalidatedPackageIdsJson: JsonSerializer.Serialize(invalidatedPackageIds),
                    RiskLevel: string.Equals(report.Severity, "Hard", StringComparison.OrdinalIgnoreCase) ? "high" : "medium",
                    Recommendation: "QueryNovelProductionState 后由 Agent 自主决定是否调用 ProduceChapter 重建章节生产包。"),
                ct)
            .ConfigureAwait(false);
    }

    private static string BuildResolutionCreativeIntent(Data.Entities.KnowledgeConflictReport report)
    {
        var note = report.ResolutionNote ?? string.Empty;
        return $"知识冲突已{report.Status}：{note}。后续生产必须遵守该处理决定，避免再次使用冲突设定。";
    }

    private static string ResolveCreativeTargetScope(string impactScope)
    {
        var normalized = impactScope.Trim();
        if (normalized.Contains("chapter", StringComparison.OrdinalIgnoreCase))
            return "chapter";
        if (normalized.Contains("volume", StringComparison.OrdinalIgnoreCase))
            return "volume";
        if (normalized.Contains("character", StringComparison.OrdinalIgnoreCase))
            return "character";

        return "project";
    }

    private static string ResolveCreativeImpactLevel(Data.Entities.KnowledgeConflictReport report)
    {
        if (string.Equals(report.Severity, "Hard", StringComparison.OrdinalIgnoreCase))
            return "world_rule_change";
        if (report.ImpactScope.Contains("Chapter", StringComparison.OrdinalIgnoreCase))
            return "chapter_rewrite";

        return "future_carry";
    }

    private static IReadOnlyList<string> TryParseStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> MarkContextPackagesStaleAsync(
        string userId,
        string projectId,
        string runId,
        CancellationToken ct)
    {
        var packages = await _db.TianmingPackages
            .Where(package =>
                package.UserId == userId &&
                package.ProjectId == projectId &&
                package.RuntimeRunId == runId &&
                package.PackageKind == "chapter_context_package" &&
                package.Status != "stale" &&
                package.Status != "invalidated")
            .OrderBy(package => package.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (packages.Count == 0)
            return Array.Empty<string>();

        var now = DateTime.UtcNow;
        foreach (var package in packages)
        {
            package.Status = "stale";
            package.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return packages.Select(package => package.Id).ToArray();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
