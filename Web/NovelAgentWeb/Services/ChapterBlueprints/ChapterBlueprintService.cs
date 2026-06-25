using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.ChapterBlueprints;

/// <summary>
/// Default implementation of <see cref="IChapterBlueprintService"/>.
/// </summary>
public class ChapterBlueprintService : IChapterBlueprintService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly NovelAgentDbContext _db;
    private readonly ILogger<ChapterBlueprintService> _logger;

    public ChapterBlueprintService(
        NovelAgentDbContext db,
        ILogger<ChapterBlueprintService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ChapterBlueprint> CreateOrUpdateAsync(
        ChapterBlueprintCreateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
        {
            throw new ArgumentException("UserId, ProjectId, and ChapterId are required");
        }

        // Supersede existing active blueprints for this chapter
        var existingActive = await _db.ChapterBlueprints
            .Where(b => b.UserId == request.UserId
                && b.ProjectId == request.ProjectId
                && b.ChapterId == request.ChapterId
                && (b.Status == "Draft" || b.Status == "Approved"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var maxVersion = existingActive.Count > 0 ? existingActive.Max(b => b.Version) : 0;
        var previousId = existingActive.OrderByDescending(b => b.Version).FirstOrDefault()?.Id;

        foreach (var existing in existingActive)
        {
            existing.Status = "Superseded";
            existing.UpdatedAt = DateTime.UtcNow;
        }

        var blueprint = new ChapterBlueprint
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = request.ProjectId,
            UserId = request.UserId,
            VolumeId = request.VolumeId,
            ChapterId = request.ChapterId,
            ChapterIndex = request.ChapterIndex,
            Title = request.Title ?? string.Empty,
            Intent = request.Intent ?? string.Empty,
            KeyEventsJson = JsonSerializer.Serialize(request.KeyEvents ?? Array.Empty<string>(), JsonOptions),
            CharactersJson = JsonSerializer.Serialize(request.Characters ?? Array.Empty<string>(), JsonOptions),
            ConflictNote = request.ConflictNote,
            EndingNote = request.EndingNote,
            RequiredKnowledgeIdsJson = JsonSerializer.Serialize(request.RequiredKnowledgeIds ?? Array.Empty<string>(), JsonOptions),
            AppliedDesignRuleIdsJson = JsonSerializer.Serialize(request.AppliedDesignRuleIds ?? Array.Empty<string>(), JsonOptions),
            DependencyChapterIdsJson = JsonSerializer.Serialize(request.DependencyChapterIds ?? Array.Empty<string>(), JsonOptions),
            Version = maxVersion + 1,
            Status = "Draft",
            TargetWordCount = request.TargetWordCount,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            PreviousVersionId = previousId
        };

        _db.ChapterBlueprints.Add(blueprint);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created blueprint {BlueprintId} (v{Version}) for chapter {ChapterId} in project {ProjectId}",
            blueprint.Id,
            blueprint.Version,
            request.ChapterId,
            request.ProjectId);

        return blueprint;
    }

    public async Task<ChapterBlueprint?> GetActiveBlueprintAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default)
    {
        return await _db.ChapterBlueprints
            .AsNoTracking()
            .Where(b => b.UserId == userId
                && b.ProjectId == projectId
                && b.ChapterId == chapterId
                && (b.Status == "Draft" || b.Status == "Approved"))
            .OrderByDescending(b => b.Version)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChapterBlueprint>> GetProjectBlueprintsAsync(
        string userId,
        string projectId,
        CancellationToken ct = default)
    {
        return await _db.ChapterBlueprints
            .AsNoTracking()
            .Where(b => b.UserId == userId
                && b.ProjectId == projectId
                && (b.Status == "Draft" || b.Status == "Approved"))
            .OrderBy(b => b.ChapterIndex)
            .ThenByDescending(b => b.Version)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChapterBlueprint>> GetBlueprintHistoryAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default)
    {
        return await _db.ChapterBlueprints
            .AsNoTracking()
            .Where(b => b.UserId == userId
                && b.ProjectId == projectId
                && b.ChapterId == chapterId)
            .OrderByDescending(b => b.Version)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> ApproveBlueprintAsync(
        string userId,
        string blueprintId,
        CancellationToken ct = default)
    {
        var blueprint = await _db.ChapterBlueprints
            .FirstOrDefaultAsync(b => b.Id == blueprintId && b.UserId == userId, ct)
            .ConfigureAwait(false);

        if (blueprint == null || blueprint.Status != "Draft")
            return false;

        blueprint.Status = "Approved";
        blueprint.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Approved blueprint {BlueprintId} for chapter {ChapterId}",
            blueprintId,
            blueprint.ChapterId);

        return true;
    }

    public async Task<bool> AttachContextPackageAsync(
        string userId,
        string blueprintId,
        string contextPackageId,
        CancellationToken ct = default)
    {
        var blueprint = await _db.ChapterBlueprints
            .FirstOrDefaultAsync(b => b.Id == blueprintId && b.UserId == userId, ct)
            .ConfigureAwait(false);

        if (blueprint == null)
            return false;

        blueprint.ContextPackageId = contextPackageId;
        blueprint.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }
}
