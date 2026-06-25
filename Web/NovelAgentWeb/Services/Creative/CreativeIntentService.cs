using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Creative;

public sealed class CreativeIntentService : ICreativeIntentService
{
    private readonly NovelAgentDbContext _db;

    public CreativeIntentService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<CreativeIntentItem?> CreateAsync(
        CreateCreativeIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.RawContent))
        {
            return null;
        }

        var projectExists = await _db.NovelProjects
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.ProjectId && p.UserId == request.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (!projectExists)
            return null;

        var idempotencyKey = EmptyToNull(request.IdempotencyKey);
        if (idempotencyKey != null)
        {
            var existing = await FindByIdempotencyKeyAsync(
                    request.UserId,
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return ToItem(existing);
        }

        var now = DateTime.UtcNow;
        var intent = new CreativeIntent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            SessionId = EmptyToNull(request.SessionId),
            RuntimeRunId = EmptyToNull(request.RunId),
            IdempotencyKey = idempotencyKey,
            Source = NormalizeSource(request.Source),
            RawContent = request.RawContent.Trim(),
            NormalizedIntent = FirstNonEmpty(request.NormalizedIntent, request.RawContent),
            TargetScope = NormalizeScope(request.TargetScope),
            TargetVolumeId = EmptyToNull(request.TargetVolumeId),
            TargetChapterId = EmptyToNull(request.TargetChapterId),
            TargetCharacterName = EmptyToNull(request.TargetCharacterName),
            Status = "candidate",
            ImpactLevel = NormalizeImpact(request.ImpactLevel),
            RequiresConfirmation = request.RequiresConfirmation,
            ConflictStatus = NormalizeConflictStatus(request.ConflictStatus),
            MetadataJson = NormalizeJsonObject(request.MetadataJson),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.CreativeIntents.Add(intent);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (idempotencyKey != null)
        {
            _db.Entry(intent).State = EntityState.Detached;
            var existing = await FindByIdempotencyKeyAsync(
                    request.UserId,
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return ToItem(existing);

            throw;
        }

        return ToItem(intent);
    }

    public async Task<CreativeIntentItem?> DecideAsync(
        DecideCreativeIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.IntentId))
        {
            return null;
        }

        var intent = await _db.CreativeIntents
            .FirstOrDefaultAsync(i =>
                i.Id == request.IntentId &&
                i.UserId == request.UserId &&
                i.ProjectId == request.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (intent == null)
            return null;

        var status = request.MarkExecuted
            ? "executed"
            : NormalizeStatus(request.Status);
        var now = DateTime.UtcNow;

        intent.Status = status;
        intent.ConflictStatus = NormalizeConflictStatus(FirstNonEmpty(request.ConflictStatus, intent.ConflictStatus));
        intent.DecisionReason = EmptyToNull(FirstNonEmpty(request.DecisionReason, intent.DecisionReason));
        intent.DecidedAt ??= now;
        intent.UpdatedAt = now;
        if (status == "executed")
            intent.ExecutedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToItem(intent);
    }

    public async Task<CreativeIntentQueryResult> QueryAsync(
        QueryCreativeIntentsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return new CreativeIntentQueryResult
            {
                ProjectId = request.ProjectId,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "all" : NormalizeStatus(request.Status),
                TargetChapterId = request.TargetChapterId
            };
        }

        var status = string.IsNullOrWhiteSpace(request.Status) ? string.Empty : NormalizeStatus(request.Status);
        var targetChapterId = request.TargetChapterId.Trim();
        var limit = Math.Clamp(request.Limit, 1, 80);
        var query = _db.CreativeIntents
            .AsNoTracking()
            .Where(i => i.UserId == request.UserId && i.ProjectId == request.ProjectId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(targetChapterId))
            query = query.Where(i => i.TargetChapterId == targetChapterId || i.TargetChapterId == null);

        var intents = await query
            .OrderByDescending(i => i.UpdatedAt)
            .ThenByDescending(i => i.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CreativeIntentQueryResult
        {
            ProjectId = request.ProjectId,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            TargetChapterId = targetChapterId,
            Items = intents.Select(ToItem).ToList()
        };
    }

    public async Task<IReadOnlyList<AcceptedCreativeIntentSnapshot>> GetAcceptedSnapshotsForPackageAsync(
        string userId,
        string projectId,
        ChapterContextPackageSummary package,
        int limit = 48,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(package.ChapterId))
        {
            return Array.Empty<AcceptedCreativeIntentSnapshot>();
        }

        var normalizedLimit = Math.Clamp(limit, 1, 80);
        var accepted = await _db.CreativeIntents
            .AsNoTracking()
            .Where(intent =>
                intent.UserId == userId &&
                intent.ProjectId == projectId &&
                (intent.Status == "accepted" || intent.Status == "executed"))
            .OrderByDescending(intent => intent.UpdatedAt)
            .ThenByDescending(intent => intent.CreatedAt)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accepted
            .Where(intent => AppliesToPackage(intent, package))
            .Select(ToSnapshot)
            .ToList();
    }

    public async Task<CreativeIntentExecutionResult> MarkPackageIntentsExecutedAsync(
        string userId,
        string projectId,
        ChapterContextPackageSummary package,
        string decisionReason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            package.AcceptedCreativeIntents.Count == 0)
        {
            return new CreativeIntentExecutionResult();
        }

        var intentIds = package.AcceptedCreativeIntents
            .Select(intent => intent.IntentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (intentIds.Length == 0)
            return new CreativeIntentExecutionResult();

        var intents = await _db.CreativeIntents
            .Where(intent =>
                intent.UserId == userId &&
                intent.ProjectId == projectId &&
                intent.Status == "accepted" &&
                intentIds.Contains(intent.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (intents.Count == 0)
            return new CreativeIntentExecutionResult();

        var now = DateTime.UtcNow;
        var reason = FirstNonEmpty(decisionReason, $"章节 {package.ChapterId} 已提交书城，生产包内创意已执行。");
        foreach (var intent in intents)
        {
            intent.Status = "executed";
            intent.DecisionReason = reason;
            intent.DecidedAt ??= now;
            intent.ExecutedAt = now;
            intent.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CreativeIntentExecutionResult
        {
            ExecutedCount = intents.Count,
            IntentIds = intents.Select(intent => intent.Id).ToList()
        };
    }

    private static CreativeIntentItem ToItem(CreativeIntent intent) => new()
    {
        Id = intent.Id,
        ProjectId = intent.ProjectId,
        SessionId = intent.SessionId ?? string.Empty,
        RunId = intent.RuntimeRunId ?? string.Empty,
        Source = intent.Source,
        RawContent = intent.RawContent,
        NormalizedIntent = intent.NormalizedIntent,
        TargetScope = intent.TargetScope,
        TargetVolumeId = intent.TargetVolumeId ?? string.Empty,
        TargetChapterId = intent.TargetChapterId ?? string.Empty,
        TargetCharacterName = intent.TargetCharacterName ?? string.Empty,
        Status = intent.Status,
        ImpactLevel = intent.ImpactLevel,
        RequiresConfirmation = intent.RequiresConfirmation,
        ConflictStatus = intent.ConflictStatus,
        DecisionReason = intent.DecisionReason ?? string.Empty,
        CreatedAt = intent.CreatedAt,
        UpdatedAt = intent.UpdatedAt,
        DecidedAt = intent.DecidedAt,
        ExecutedAt = intent.ExecutedAt
    };

    private static AcceptedCreativeIntentSnapshot ToSnapshot(CreativeIntent intent) => new()
    {
        IntentId = intent.Id,
        NormalizedIntent = intent.NormalizedIntent,
        TargetScope = intent.TargetScope,
        TargetChapterId = intent.TargetChapterId ?? string.Empty,
        TargetVolumeId = intent.TargetVolumeId ?? string.Empty,
        TargetCharacterName = intent.TargetCharacterName ?? string.Empty,
        ImpactLevel = intent.ImpactLevel,
        Source = intent.Source,
        DecisionReason = intent.DecisionReason ?? string.Empty,
        CreatedAt = intent.CreatedAt
    };

    private Task<CreativeIntent?> FindByIdempotencyKeyAsync(
        string userId,
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _db.CreativeIntents
            .AsNoTracking()
            .FirstOrDefaultAsync(intent =>
                    intent.UserId == userId &&
                    intent.ProjectId == projectId &&
                    intent.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private static bool AppliesToPackage(CreativeIntent intent, ChapterContextPackageSummary package)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetChapterId))
            return true;

        return string.Equals(intent.TargetChapterId, package.ChapterId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSource(string value) =>
        NormalizeAllowed(value, "chat", "chat", "workflow_comment", "agent_suggestion", "knowledge", "user_upload", "system");

    private static string NormalizeScope(string value) =>
        NormalizeAllowed(value, "project", "book", "project", "volume", "chapter", "character", "subplot", "world_rule", "session");

    private static string NormalizeImpact(string value) =>
        NormalizeAllowed(value, "future_carry", "minor_edit", "chapter_rewrite", "future_carry", "mainline_change", "world_rule_change", "character_arc_change");

    private static string NormalizeConflictStatus(string value) =>
        NormalizeAllowed(value, "unknown", "unknown", "none", "potential_conflict", "conflicts_fact", "resolved");

    private static string NormalizeStatus(string value) =>
        NormalizeAllowed(value, "candidate", "candidate", "accepted", "rejected", "replaced", "executed");

    private static string NormalizeAllowed(string value, string fallback, params string[] allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return allowed.FirstOrDefault(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "{}";

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object ? value.Trim() : "{}";
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { raw = value.Trim() });
        }
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
