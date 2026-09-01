using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class ProjectKnowledgeBindingQueryService : IProjectKnowledgeBindingQueryService
{
    private static readonly string[] AllowedUsageStatuses = { "imported", "referenced" };
    private readonly NovelAgentDbContext _db;

    public ProjectKnowledgeBindingQueryService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<ProjectKnowledgeBindingsQueryResult?> QueryBindingsAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
            return null;

        var bindings = await GetBoundKnowledgeAsync(userId, projectId, cancellationToken)
            .ConfigureAwait(false);
        var hardFacts = bindings
            .Where(binding => string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
            .Select(FormatBoundKnowledgeHardFact)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var conflictReports = await GetProjectConflictReportsAsync(userId, projectId, cancellationToken)
            .ConfigureAwait(false);
        var canonLedger = await GetProjectCanonLedgerAsync(userId, projectId, cancellationToken)
            .ConfigureAwait(false);

        return new ProjectKnowledgeBindingsQueryResult
        {
            ProjectId = projectId,
            Bindings = bindings.ToList(),
            HardFacts = hardFacts,
            CanonLedger = canonLedger.ToList(),
            ConflictReports = conflictReports.ToList(),
            BindingCount = bindings.Count,
            HardFactCount = hardFacts.Count,
            CanonCount = canonLedger.Count(entry =>
                string.Equals(entry.Status, CanonLedgerEntryStatus.Canon.ToString(), StringComparison.OrdinalIgnoreCase)),
            StatusSummary = BuildStatusSummary(bindings, canonLedger, conflictReports)
        };
    }

    private static ProjectKnowledgeBindingStatusSummary BuildStatusSummary(
        IReadOnlyList<BoundKnowledgeSnapshot> bindings,
        IReadOnlyList<ProjectKnowledgeCanonLedgerSummary> canonLedger,
        IReadOnlyList<ProjectKnowledgeConflictReportSummary> conflictReports)
    {
        return new ProjectKnowledgeBindingStatusSummary
        {
            ImportedCount = bindings.Count(binding =>
                string.Equals(binding.ProjectUsageStatus, "imported", StringComparison.OrdinalIgnoreCase)),
            ReferencedCount = bindings.Count(binding =>
                string.Equals(binding.ProjectUsageStatus, "referenced", StringComparison.OrdinalIgnoreCase)),
            ClassifiedCount = bindings.Count(binding =>
                !string.IsNullOrWhiteSpace(binding.ClassificationId)),
            PendingClassificationCount = bindings.Count(binding =>
                string.IsNullOrWhiteSpace(binding.ClassificationId)),
            ShouldEnterGateCount = bindings.Count(binding => binding.ShouldEnterGate),
            ShouldEnterBlueprintCount = bindings.Count(binding => binding.ShouldEnterBlueprint),
            ShouldEnterFactSnapshotCount = bindings.Count(binding => binding.ShouldEnterFactSnapshot),
            CanonLedgerCount = canonLedger.Count(entry =>
                string.Equals(entry.Status, CanonLedgerEntryStatus.Canon.ToString(), StringComparison.OrdinalIgnoreCase)),
            ConflictCanonLedgerCount = canonLedger.Count(entry =>
                string.Equals(entry.Status, CanonLedgerEntryStatus.Conflict.ToString(), StringComparison.OrdinalIgnoreCase)),
            OpenConflictCount = conflictReports.Count(report =>
                string.Equals(report.Status, "open", StringComparison.OrdinalIgnoreCase))
        };
    }

    public async Task<IReadOnlyList<BoundKnowledgeSnapshot>> GetBoundKnowledgeAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
            return Array.Empty<BoundKnowledgeSnapshot>();

        var rows = await (
                from usage in _db.ProjectKnowledgeUsages.AsNoTracking()
                join knowledge in _db.KnowledgeBases.AsNoTracking()
                    on usage.KnowledgeId equals knowledge.Id
                where usage.UserId == userId
                      && usage.ProjectId == projectId
                      && AllowedUsageStatuses.Contains(usage.Status)
                      && !knowledge.IsArchived
                      && knowledge.UserId == userId
                orderby usage.Status == "referenced" descending,
                    usage.LastUsedAt ?? usage.FirstSeenAt descending,
                    knowledge.Weight descending,
                    knowledge.CreatedAt descending
                select new
                {
                    Usage = usage,
                    Knowledge = knowledge
                })
            .Take(24)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var knowledgeIds = rows
            .Select(row => row.Knowledge.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var latestClassifications = knowledgeIds.Count == 0
            ? new Dictionary<string, KnowledgeClassificationSnapshot>(StringComparer.OrdinalIgnoreCase)
            : await LoadLatestClassificationsAsync(userId, projectId, knowledgeIds, cancellationToken)
                .ConfigureAwait(false);

        return rows
            .Select(row => new BoundKnowledgeSnapshot
            {
                KnowledgeId = row.Knowledge.Id,
                Title = row.Knowledge.Title,
                EntryType = row.Knowledge.EntryType,
                Content = row.Knowledge.Content,
                Tags = ParseKnowledgeTags(row.Knowledge.Tags),
                Weight = row.Knowledge.Weight,
                SourceProjectId = row.Knowledge.SourceProjectId ?? string.Empty,
                ProjectUsageStatus = row.Usage.Status,
                ProjectUsageCount = row.Usage.UsageCount,
                SourceSessionId = row.Usage.SourceSessionId ?? string.Empty,
                SourceRunId = row.Usage.SourceRunId ?? string.Empty,
                Note = row.Usage.Note ?? string.Empty,
                Role = NormalizeBindingRole(row.Usage.Role, row.Knowledge.EntryType),
                Scope = NormalizeBindingScope(row.Usage.Scope),
                Priority = row.Usage.Priority <= 0 ? row.Knowledge.Weight : row.Usage.Priority,
                ConstraintLevel = NormalizeConstraintLevel(row.Usage.ConstraintLevel, row.Knowledge.EntryType),
                PackagePolicy = NormalizePackagePolicy(row.Usage.PackagePolicy, row.Knowledge.EntryType),
                BoundVersion = row.Usage.BoundVersion ?? string.Empty,
                UsedByChapters = ParseUsedByChapters(row.Usage.UsedByChaptersJson),
                ClassificationId = latestClassifications.TryGetValue(row.Knowledge.Id, out var classification)
                    ? classification.Id
                    : string.Empty,
                ClassificationModel = classification?.Model ?? string.Empty,
                ClassificationRule = classification?.Rule ?? string.Empty,
                TargetEntities = classification?.TargetEntities.ToList() ?? new List<string>(),
                ShouldEnterGate = classification?.ShouldEnterGate ?? false,
                ShouldEnterBlueprint = classification?.ShouldEnterBlueprint ?? false,
                ShouldEnterFactSnapshot = classification?.ShouldEnterFactSnapshot ?? false,
                ClassificationConfidence = classification?.Confidence ?? 0
            })
            .ToList();
    }

    private async Task<Dictionary<string, KnowledgeClassificationSnapshot>> LoadLatestClassificationsAsync(
        string userId,
        string projectId,
        IReadOnlyList<string> knowledgeIds,
        CancellationToken cancellationToken)
    {
        var classifications = await _db.KnowledgeClassifications
            .AsNoTracking()
            .Where(classification =>
                classification.UserId == userId &&
                classification.ProjectId == projectId &&
                knowledgeIds.Contains(classification.KnowledgeId))
            .OrderByDescending(classification => classification.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return classifications
            .GroupBy(classification => classification.KnowledgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ToClassificationSnapshot(group.First()),
                StringComparer.OrdinalIgnoreCase);
    }

    private static KnowledgeClassificationSnapshot ToClassificationSnapshot(KnowledgeClassification classification)
    {
        var json = ParseClassificationJson(classification.ClassificationJson);
        return new KnowledgeClassificationSnapshot(
            classification.Id,
            classification.Model,
            FirstNonEmpty(GetJsonString(json, "rule"), string.Empty),
            GetJsonStringArray(json, "targetEntities"),
            GetJsonBool(json, "shouldEnterGate"),
            GetJsonBool(json, "shouldEnterBlueprint"),
            GetJsonBool(json, "shouldEnterFactSnapshot"),
            classification.Confidence);
    }

    private static JsonElement ParseClassificationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async Task<IReadOnlyList<string>> GetProjectHardFactLinesAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
            return Array.Empty<string>();

        var bindings = await GetBoundKnowledgeAsync(userId, projectId, cancellationToken)
            .ConfigureAwait(false);

        return bindings
            .Where(binding => string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
            .Select(FormatBoundKnowledgeHardFact)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public static string FormatBoundKnowledgeHardFact(BoundKnowledgeSnapshot binding)
    {
        var title = binding.Title.Trim();
        var content = binding.Content.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return content;
        if (string.IsNullOrWhiteSpace(content))
            return title;
        return $"{title}：{content}";
    }

    private async Task<IReadOnlyList<ProjectKnowledgeConflictReportSummary>> GetProjectConflictReportsAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var reports = await _db.KnowledgeConflictReports
            .AsNoTracking()
            .Where(report => report.UserId == userId && report.ProjectId == projectId)
            .OrderBy(report => report.Status == "open" ? 0 : 1)
            .ThenByDescending(report => report.CreatedAt)
            .Take(16)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return reports
            .Select(report => new ProjectKnowledgeConflictReportSummary
            {
                ConflictId = report.Id,
                KnowledgeId = report.KnowledgeId,
                ConflictingKnowledgeIds = ParseStringList(report.ConflictingKnowledgeIdsJson, 32),
                ConflictType = report.ConflictType,
                Severity = report.Severity,
                ImpactScope = report.ImpactScope,
                Explanation = report.Explanation,
                RecommendedAction = report.RecommendedAction,
                RequiresUserDecision = report.RequiresUserDecision,
                Status = report.Status,
                ResolutionNote = report.ResolutionNote ?? string.Empty,
                CreatedAt = report.CreatedAt,
                ResolvedAt = report.ResolvedAt
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ProjectKnowledgeCanonLedgerSummary>> GetProjectCanonLedgerAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var document = await _db.ContentDocuments
            .AsNoTracking()
            .Where(d =>
                d.UserId == userId &&
                d.ProjectId == projectId &&
                d.SourceType == "story_bible" &&
                d.SourceId == projectId &&
                d.DocumentRole == "aggregate_json" &&
                d.Status == "active")
            .OrderByDescending(d => d.Version)
            .ThenByDescending(d => d.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (document == null)
            return Array.Empty<ProjectKnowledgeCanonLedgerSummary>();

        var json = await LoadDocumentTextAsync(document.Id, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ProjectKnowledgeCanonLedgerSummary>();

        StoryBibleDocument? storyBible;
        try
        {
            storyBible = JsonSerializer.Deserialize<StoryBibleDocument>(json, JsonHelper.CnDefault);
        }
        catch (JsonException)
        {
            return Array.Empty<ProjectKnowledgeCanonLedgerSummary>();
        }

        return (storyBible?.CanonLedger ?? new List<CanonLedgerEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .OrderBy(entry => CanonStatusRank(entry.Status))
            .ThenByDescending(entry => entry.UpdatedAt)
            .ThenByDescending(entry => entry.CreatedAt)
            .Take(32)
            .Select(entry => new ProjectKnowledgeCanonLedgerSummary
            {
                Id = entry.Id,
                Type = entry.Type.ToString(),
                Status = entry.Status.ToString(),
                Title = entry.Title ?? string.Empty,
                Content = entry.Content ?? string.Empty,
                Rationale = entry.Rationale ?? string.Empty,
                ImpactScope = entry.ImpactScope ?? string.Empty,
                ConflictCheck = entry.ConflictCheck ?? string.Empty
            })
            .ToList();
    }

    private async Task<string> LoadDocumentTextAsync(string documentId, CancellationToken cancellationToken)
    {
        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.ChunkText)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.Concat(chunks);
    }

    private static int CanonStatusRank(CanonLedgerEntryStatus status) => status switch
    {
        CanonLedgerEntryStatus.Conflict => 0,
        CanonLedgerEntryStatus.Canon => 1,
        CanonLedgerEntryStatus.Proposed => 2,
        CanonLedgerEntryStatus.Draft => 3,
        CanonLedgerEntryStatus.Deprecated => 4,
        CanonLedgerEntryStatus.Rejected => 5,
        _ => 9
    };

    private static List<string> ParseKnowledgeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return new List<string>();

        try
        {
            return ParseStringList(tags, 16);
        }
        catch (JsonException)
        {
            // Fall back to delimiter parsing for older manually entered tags.
        }

        return tags
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
    }

    private static List<string> ParseStringList(string? json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed != null)
                return parsed
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
                    .ToList();
        }
        catch (JsonException)
        {
            // Fall back to delimiter parsing below.
        }

        return json
            .Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static string NormalizeBindingRole(string? role, string? entryType)
    {
        if (!string.IsNullOrWhiteSpace(role))
            return role.Trim();
        return string.IsNullOrWhiteSpace(entryType) ? "Reference" : entryType.Trim();
    }

    private static string NormalizeBindingScope(string? scope)
    {
        if (!string.IsNullOrWhiteSpace(scope))
            return scope.Trim();
        return "ProjectWide";
    }

    private static string NormalizeConstraintLevel(string? constraintLevel, string? entryType)
    {
        if (!string.IsNullOrWhiteSpace(constraintLevel))
            return constraintLevel.Trim();
        return string.Equals(entryType, "HardFact", StringComparison.OrdinalIgnoreCase)
            ? "HardConstraint"
            : "Reference";
    }

    private static string NormalizePackagePolicy(string? packagePolicy, string? entryType)
    {
        if (!string.IsNullOrWhiteSpace(packagePolicy))
            return packagePolicy.Trim();
        return string.Equals(entryType, "HardFact", StringComparison.OrdinalIgnoreCase)
            ? "DefaultEveryChapter"
            : "RelevantOnly";
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
                    .Take(64)
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
            .Take(64)
            .ToList();
    }

    private static string GetJsonString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetJsonBool(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static List<string> GetJsonStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record KnowledgeClassificationSnapshot(
        string Id,
        string Model,
        string Rule,
        IReadOnlyList<string> TargetEntities,
        bool ShouldEnterGate,
        bool ShouldEnterBlueprint,
        bool ShouldEnterFactSnapshot,
        double Confidence);
}
