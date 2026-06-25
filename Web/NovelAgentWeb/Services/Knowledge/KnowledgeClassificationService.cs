using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeClassificationService : IKnowledgeClassificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly NovelAgentDbContext _db;
    private readonly IKnowledgeClassificationModelClient _model;
    private readonly ILogger<KnowledgeClassificationService> _logger;
    private readonly IKnowledgeStoryBiblePromotionService? _storyBiblePromotion;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public KnowledgeClassificationService(
        NovelAgentDbContext db,
        IKnowledgeClassificationModelClient model,
        ILogger<KnowledgeClassificationService> logger,
        IKnowledgeStoryBiblePromotionService? storyBiblePromotion = null,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _db = db;
        _model = model;
        _logger = logger;
        _storyBiblePromotion = storyBiblePromotion;
        _outputArtifacts = outputArtifacts;
    }

    public async Task<KnowledgeClassificationResult> ClassifyAndApplyAsync(
        KnowledgeClassificationRequest request,
        CancellationToken ct = default)
    {
        var project = await _db.NovelProjects
            .AsNoTracking()
            .Where(x => x.Id == request.ProjectId && x.UserId == request.UserId)
            .Select(x => new { x.Id, x.UserId, x.Title })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (project == null)
            throw new InvalidOperationException("项目不存在或无权访问。");

        var knowledge = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(x => x.Id == request.KnowledgeId && x.UserId == request.UserId)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Title,
                x.EntryType,
                x.Content,
                x.Weight,
                x.IdempotencyKey
            })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (knowledge == null)
            throw new InvalidOperationException("知识条目不存在或无权访问。");

        var prompt = new KnowledgeClassificationPrompt(
            request.UserId,
            request.ProjectId,
            request.KnowledgeId,
            project.Title,
            knowledge.Title,
            knowledge.EntryType,
            knowledge.Content,
            knowledge.Weight);
        var decision = await _model.ClassifyAsync(prompt, ct).ConfigureAwait(false);
        Normalize(decision);

        var rawJson = string.IsNullOrWhiteSpace(decision.RawJson)
            ? JsonSerializer.Serialize(decision, JsonOptions)
            : decision.RawJson;
        var classification = new KnowledgeClassification
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            KnowledgeId = request.KnowledgeId,
            Model = decision.Model,
            ClassificationJson = rawJson,
            Role = decision.Role,
            Scope = decision.Scope,
            Priority = decision.Priority,
            ConstraintLevel = decision.ConstraintLevel,
            PackagePolicy = decision.PackagePolicy,
            Confidence = decision.Confidence,
            SourceSessionId = request.SessionId,
            SourceRunId = request.RunId,
            CreatedAt = DateTime.UtcNow
        };
        _db.KnowledgeClassifications.Add(classification);

        var usage = await _db.ProjectKnowledgeUsages
            .SingleOrDefaultAsync(x =>
                x.UserId == request.UserId &&
                x.ProjectId == request.ProjectId &&
                x.KnowledgeId == request.KnowledgeId, ct)
            .ConfigureAwait(false);
        if (usage == null)
        {
            usage = new ProjectKnowledgeUsage
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                KnowledgeId = request.KnowledgeId,
                Status = "imported",
                SourceSessionId = request.SessionId,
                SourceRunId = request.RunId,
                FirstSeenAt = DateTime.UtcNow
            };
            _db.ProjectKnowledgeUsages.Add(usage);
        }

        usage.Role = decision.Role;
        usage.Scope = decision.Scope;
        usage.Priority = decision.Priority;
        usage.ConstraintLevel = decision.ConstraintLevel;
        usage.PackagePolicy = decision.PackagePolicy;
        usage.BoundVersion = knowledge.IdempotencyKey ?? knowledge.Id;
        usage.SourceSessionId = request.SessionId ?? usage.SourceSessionId;
        usage.SourceRunId = request.RunId ?? usage.SourceRunId;
        usage.Note = AppendClassificationNote(usage.Note, classification.Id);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (_outputArtifacts != null)
        {
            await _outputArtifacts.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: FirstNonEmpty(request.RunId, $"knowledge-classification:{classification.Id}"),
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: null,
                        PackageId: null,
                        ToolName: "KnowledgeClassification",
                        Stage: "knowledge_classification",
                        Status: "completed",
                        ArtifactType: "knowledge_classification",
                        ArtifactId: classification.Id,
                        OutputKind: "ProcessArtifact",
                        Summary: $"知识 {knowledge.Title} 已分类为 {decision.Role}/{decision.ConstraintLevel}，规则进入策略 {decision.PackagePolicy}。",
                        UserVisibleWhere: new[] { "创作工作流", "知识库" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: false,
                        SourceEventType: "knowledge_classified",
                        SourceEventId: classification.Id,
                        Data: new
                        {
                            classification.Id,
                            request.KnowledgeId,
                            knowledge.Title,
                            decision.Model,
                            decision.Role,
                            decision.Scope,
                            decision.ConstraintLevel,
                            decision.PackagePolicy,
                            decision.Priority,
                            decision.TargetEntities,
                            decision.Rule,
                            decision.ShouldEnterGate,
                            decision.ShouldEnterBlueprint,
                            decision.ShouldEnterFactSnapshot,
                            decision.Confidence
                        }),
                    ct)
                .ConfigureAwait(false);
        }

        if (_storyBiblePromotion != null)
        {
            await _storyBiblePromotion.PromoteAsync(
                    new KnowledgeStoryBiblePromotionRequest(
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        KnowledgeId: request.KnowledgeId,
                        KnowledgeTitle: knowledge.Title,
                        KnowledgeEntryType: knowledge.EntryType,
                        ClassificationId: classification.Id,
                        Model: decision.Model,
                        Role: decision.Role,
                        Scope: decision.Scope,
                        ConstraintLevel: decision.ConstraintLevel,
                        PackagePolicy: decision.PackagePolicy,
                        Rule: decision.Rule,
                        TargetEntities: decision.TargetEntities,
                        ShouldEnterGate: decision.ShouldEnterGate,
                        ShouldEnterBlueprint: decision.ShouldEnterBlueprint,
                        ShouldEnterFactSnapshot: decision.ShouldEnterFactSnapshot,
                        Confidence: decision.Confidence,
                        SessionId: request.SessionId,
                        RunId: request.RunId),
                    ct)
                .ConfigureAwait(false);
        }
        _logger.LogInformation(
            "Classified knowledge {KnowledgeId} for project {ProjectId} as {Role}/{ConstraintLevel}",
            request.KnowledgeId,
            request.ProjectId,
            decision.Role,
            decision.ConstraintLevel);

        return new KnowledgeClassificationResult
        {
            Id = classification.Id,
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            KnowledgeId = request.KnowledgeId,
            Model = decision.Model,
            Role = decision.Role,
            Scope = decision.Scope,
            Priority = decision.Priority,
            ConstraintLevel = decision.ConstraintLevel,
            PackagePolicy = decision.PackagePolicy,
            TargetEntities = decision.TargetEntities,
            Rule = decision.Rule,
            ShouldEnterGate = decision.ShouldEnterGate,
            ShouldEnterBlueprint = decision.ShouldEnterBlueprint,
            ShouldEnterFactSnapshot = decision.ShouldEnterFactSnapshot,
            Confidence = decision.Confidence
        };
    }

    private static void Normalize(KnowledgeClassificationDecision decision)
    {
        decision.Model = string.IsNullOrWhiteSpace(decision.Model) ? "llm" : decision.Model.Trim();
        decision.Role = string.IsNullOrWhiteSpace(decision.Role) ? "Reference" : decision.Role.Trim();
        decision.Scope = string.IsNullOrWhiteSpace(decision.Scope) ? "ProjectWide" : decision.Scope.Trim();
        decision.Priority = Math.Clamp(decision.Priority <= 0 ? 50 : decision.Priority, 1, 100);
        decision.ConstraintLevel = string.IsNullOrWhiteSpace(decision.ConstraintLevel)
            ? "Reference"
            : decision.ConstraintLevel.Trim();
        decision.PackagePolicy = string.IsNullOrWhiteSpace(decision.PackagePolicy)
            ? "RelevantOnly"
            : decision.PackagePolicy.Trim();
        decision.TargetEntities = decision.TargetEntities
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        decision.Rule = decision.Rule?.Trim() ?? "";
        decision.Confidence = Math.Clamp(decision.Confidence, 0, 1);
    }

    private static string AppendClassificationNote(string? note, string classificationId)
    {
        var marker = $"classification:{classificationId}";
        if (string.IsNullOrWhiteSpace(note))
            return marker;
        if (note.Contains(marker, StringComparison.OrdinalIgnoreCase))
            return note;
        return $"{note.Trim()}; {marker}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
