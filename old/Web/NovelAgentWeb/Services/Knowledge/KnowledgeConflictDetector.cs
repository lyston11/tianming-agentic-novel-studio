using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeConflictDetector : IKnowledgeConflictDetector
{
    private static readonly string[] AllowedUsageStatuses = { "imported", "referenced" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly NovelAgentDbContext _db;
    private readonly IKnowledgeConflictModelClient _model;
    private readonly ILogger<KnowledgeConflictDetector> _logger;
    private readonly IKnowledgeCanonConflictStatusService? _canonConflictStatus;

    public KnowledgeConflictDetector(
        NovelAgentDbContext db,
        IKnowledgeConflictModelClient model,
        ILogger<KnowledgeConflictDetector> logger,
        IKnowledgeCanonConflictStatusService? canonConflictStatus = null)
    {
        _db = db;
        _model = model;
        _logger = logger;
        _canonConflictStatus = canonConflictStatus;
    }

    public async Task<KnowledgeConflictDetectionResult> DetectAsync(
        KnowledgeConflictDetectionRequest request,
        CancellationToken ct = default)
    {
        var project = await _db.NovelProjects
            .AsNoTracking()
            .Where(x => x.Id == request.ProjectId && x.UserId == request.UserId)
            .Select(x => new { x.Id, x.Title })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (project == null)
            throw new InvalidOperationException("项目不存在或无权访问。");

        var rows = await (
                from usage in _db.ProjectKnowledgeUsages.AsNoTracking()
                join knowledge in _db.KnowledgeBases.AsNoTracking()
                    on usage.KnowledgeId equals knowledge.Id
                where usage.UserId == request.UserId
                      && usage.ProjectId == request.ProjectId
                      && knowledge.UserId == request.UserId
                      && !knowledge.IsArchived
                      && AllowedUsageStatuses.Contains(usage.Status)
                orderby usage.Priority descending,
                    usage.Status == "referenced" descending,
                    usage.LastUsedAt ?? usage.FirstSeenAt descending
                select new
                {
                    Usage = usage,
                    Knowledge = knowledge
                })
            .Take(32)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var candidateRow = rows.FirstOrDefault(x =>
            string.Equals(x.Knowledge.Id, request.KnowledgeId, StringComparison.OrdinalIgnoreCase));
        if (candidateRow == null)
        {
            var candidateKnowledge = await _db.KnowledgeBases
                .AsNoTracking()
                .Where(x => x.Id == request.KnowledgeId && x.UserId == request.UserId && !x.IsArchived)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (candidateKnowledge == null)
                throw new InvalidOperationException("知识条目不存在或无权访问。");

            rows.Insert(0, new
            {
                Usage = new ProjectKnowledgeUsage
                {
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    KnowledgeId = candidateKnowledge.Id,
                    Status = "candidate",
                    Role = string.IsNullOrWhiteSpace(candidateKnowledge.EntryType) ? "Reference" : candidateKnowledge.EntryType,
                    Scope = "ProjectWide",
                    Priority = candidateKnowledge.Weight,
                    ConstraintLevel = string.Equals(candidateKnowledge.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase)
                        ? "HardConstraint"
                        : "Reference",
                    PackagePolicy = string.Equals(candidateKnowledge.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase)
                        ? "DefaultEveryChapter"
                        : "RelevantOnly"
                },
                Knowledge = candidateKnowledge
            });
            candidateRow = rows[0];
        }

        var candidate = ToPromptItem(candidateRow.Usage, candidateRow.Knowledge);
        var existing = rows
            .Where(x => !string.Equals(x.Knowledge.Id, request.KnowledgeId, StringComparison.OrdinalIgnoreCase))
            .Select(x => ToPromptItem(x.Usage, x.Knowledge))
            .ToList();

        var decision = await _model.DetectAsync(
                new KnowledgeConflictPrompt(
                    request.UserId,
                    request.ProjectId,
                    project.Title,
                    candidate,
                    existing),
                ct)
            .ConfigureAwait(false);
        Normalize(decision);

        KnowledgeConflictReport? report = null;
        if (decision.HasConflict)
        {
            report = new KnowledgeConflictReport
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                KnowledgeId = request.KnowledgeId,
                ConflictingKnowledgeIdsJson = JsonSerializer.Serialize(decision.ConflictingKnowledgeIds, JsonOptions),
                ConflictType = decision.ConflictType,
                Severity = decision.Severity,
                ImpactScope = decision.ImpactScope,
                Explanation = decision.Explanation,
                RecommendedAction = decision.RecommendedAction,
                RequiresUserDecision = decision.RequiresUserDecision,
                Status = "open",
                DetectionJson = string.IsNullOrWhiteSpace(decision.RawJson)
                    ? JsonSerializer.Serialize(decision, JsonOptions)
                    : decision.RawJson,
                SourceSessionId = request.SessionId,
                SourceRunId = request.RunId,
                CreatedAt = DateTime.UtcNow
            };
            _db.KnowledgeConflictReports.Add(report);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (_canonConflictStatus != null)
        {
            await _canonConflictStatus.ApplyAsync(
                    new KnowledgeCanonConflictStatusRequest(
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        KnowledgeId: request.KnowledgeId,
                        HasConflict: decision.HasConflict,
                        ReportId: report?.Id ?? string.Empty,
                        Severity: decision.Severity,
                        ConflictType: decision.ConflictType,
                        Explanation: decision.Explanation,
                        ConflictingKnowledgeIds: decision.ConflictingKnowledgeIds,
                        RunId: request.RunId),
                    ct)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Detected knowledge conflict for {KnowledgeId} in project {ProjectId}: {HasConflict}/{Severity}",
            request.KnowledgeId,
            request.ProjectId,
            decision.HasConflict,
            decision.Severity);

        return new KnowledgeConflictDetectionResult
        {
            ReportId = report?.Id ?? string.Empty,
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            KnowledgeId = request.KnowledgeId,
            HasConflict = decision.HasConflict,
            ConflictType = decision.ConflictType,
            Severity = decision.Severity,
            ImpactScope = decision.ImpactScope,
            ConflictingKnowledgeIds = decision.ConflictingKnowledgeIds,
            Explanation = decision.Explanation,
            RecommendedAction = decision.RecommendedAction,
            RequiresUserDecision = decision.RequiresUserDecision
        };
    }

    private static KnowledgeConflictKnowledgeItem ToPromptItem(ProjectKnowledgeUsage usage, KnowledgeBase knowledge) =>
        new()
        {
            KnowledgeId = knowledge.Id,
            Title = knowledge.Title,
            EntryType = knowledge.EntryType,
            Content = knowledge.Content,
            Role = string.IsNullOrWhiteSpace(usage.Role) ? knowledge.EntryType : usage.Role,
            Scope = string.IsNullOrWhiteSpace(usage.Scope) ? "ProjectWide" : usage.Scope,
            Priority = usage.Priority <= 0 ? knowledge.Weight : usage.Priority,
            ConstraintLevel = string.IsNullOrWhiteSpace(usage.ConstraintLevel) ? "Reference" : usage.ConstraintLevel,
            PackagePolicy = string.IsNullOrWhiteSpace(usage.PackagePolicy) ? "RelevantOnly" : usage.PackagePolicy
        };

    private static void Normalize(KnowledgeConflictDecision decision)
    {
        decision.Model = string.IsNullOrWhiteSpace(decision.Model) ? "llm" : decision.Model.Trim();
        if (!decision.HasConflict)
        {
            decision.ConflictType = "None";
            decision.Severity = "None";
            decision.ImpactScope = "None";
            decision.ConflictingKnowledgeIds = new List<string>();
            decision.RequiresUserDecision = false;
            return;
        }

        decision.ConflictType = string.IsNullOrWhiteSpace(decision.ConflictType)
            ? "KnowledgeConflict"
            : decision.ConflictType.Trim();
        decision.Severity = string.IsNullOrWhiteSpace(decision.Severity) ? "Medium" : decision.Severity.Trim();
        decision.ImpactScope = string.IsNullOrWhiteSpace(decision.ImpactScope)
            ? "ProjectWide"
            : decision.ImpactScope.Trim();
        decision.ConflictingKnowledgeIds = decision.ConflictingKnowledgeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        decision.Explanation = decision.Explanation?.Trim() ?? "";
        decision.RecommendedAction = decision.RecommendedAction?.Trim() ?? "";
    }
}
