using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.DesignRules;

/// <summary>
/// Default implementation of <see cref="IDesignRuleAggregationService"/>.
/// Aggregates knowledge classifications into design rules grouped by RuleType.
/// </summary>
public class DesignRuleAggregationService : IDesignRuleAggregationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly NovelAgentDbContext _db;
    private readonly ILogger<DesignRuleAggregationService> _logger;

    public DesignRuleAggregationService(
        NovelAgentDbContext db,
        ILogger<DesignRuleAggregationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProjectDesignRule>> AggregateFromKnowledgeAsync(
        string userId,
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("UserId and ProjectId are required");

        // Get all active classifications for this project
        var classifications = await _db.KnowledgeClassifications
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ProjectId == projectId)
            .Join(_db.KnowledgeBases.AsNoTracking(),
                cls => cls.KnowledgeId,
                kb => kb.Id,
                (cls, kb) => new
                {
                    Classification = cls,
                    KnowledgeContent = kb.Content,
                    KnowledgeTitle = kb.Title
                })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (classifications.Count == 0)
        {
            _logger.LogInformation(
                "No classifications found for project {ProjectId}, skipping aggregation",
                projectId);
            return Array.Empty<ProjectDesignRule>();
        }

        // Map classification Role to design rule RuleType
        var groupedByRuleType = classifications
            .Select(c => new
            {
                c.Classification,
                c.KnowledgeContent,
                c.KnowledgeTitle,
                RuleType = MapRoleToRuleType(c.Classification.Role)
            })
            .Where(x => !string.IsNullOrEmpty(x.RuleType))
            .GroupBy(x => x.RuleType!)
            .ToList();

        var newRules = new List<ProjectDesignRule>();

        foreach (var group in groupedByRuleType)
        {
            // Archive existing rules of this type
            var existingRules = await _db.ProjectDesignRules
                .Where(r => r.UserId == userId
                    && r.ProjectId == projectId
                    && r.RuleType == group.Key
                    && r.Status == DesignRuleStatuses.Active)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var maxVersion = existingRules.Count > 0 ? existingRules.Max(r => r.Version) : 0;

            foreach (var existing in existingRules)
            {
                existing.Status = DesignRuleStatuses.Superseded;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            // Aggregate classifications by ConstraintLevel
            var byConstraintLevel = group
                .GroupBy(x => MapConstraintLevel(x.Classification.ConstraintLevel))
                .ToList();

            foreach (var levelGroup in byConstraintLevel)
            {
                var ruleContent = string.Join("\n",
                    levelGroup.Select(x => $"- [{x.KnowledgeTitle}] {Truncate(x.KnowledgeContent, 200)}"));

                var sourceKnowledgeIds = levelGroup
                    .Select(x => x.Classification.KnowledgeId)
                    .Distinct()
                    .ToList();

                var priority = levelGroup.Max(x => x.Classification.Priority);

                var newRule = new ProjectDesignRule
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = projectId,
                    UserId = userId,
                    RuleType = group.Key,
                    RuleContent = ruleContent,
                    SourceKnowledgeIdsJson = JsonSerializer.Serialize(sourceKnowledgeIds, JsonOptions),
                    ConstraintLevel = levelGroup.Key,
                    Scope = "ProjectWide",
                    ScopeTarget = null,
                    Priority = priority,
                    Version = maxVersion + 1,
                    Status = DesignRuleStatuses.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    PreviousVersionId = existingRules.FirstOrDefault()?.Id
                };

                newRules.Add(newRule);
            }
        }

        if (newRules.Count > 0)
        {
            _db.ProjectDesignRules.AddRange(newRules);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Aggregated {Count} design rules for project {ProjectId} from {ClassificationCount} classifications",
                newRules.Count,
                projectId,
                classifications.Count);
        }

        return newRules;
    }

    public async Task<IReadOnlyList<ProjectDesignRule>> GetActiveDesignRulesAsync(
        string userId,
        string projectId,
        CancellationToken ct = default)
    {
        return await _db.ProjectDesignRules
            .AsNoTracking()
            .Where(r => r.UserId == userId
                && r.ProjectId == projectId
                && r.Status == DesignRuleStatuses.Active)
            .OrderBy(r => r.RuleType)
            .ThenByDescending(r => r.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectDesignRule>> GetDesignRulesByTypeAsync(
        string userId,
        string projectId,
        string ruleType,
        CancellationToken ct = default)
    {
        return await _db.ProjectDesignRules
            .AsNoTracking()
            .Where(r => r.UserId == userId
                && r.ProjectId == projectId
                && r.RuleType == ruleType
                && r.Status == DesignRuleStatuses.Active)
            .OrderByDescending(r => r.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Maps a knowledge classification Role to a design rule RuleType.
    /// </summary>
    private static string? MapRoleToRuleType(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return role.Trim().ToLowerInvariant() switch
        {
            "worldrule" or "worldcorerule" or "worldsetting" or "world" => DesignRuleTypes.WorldCoreRule,
            "characterrule" or "characterpsycherule" or "characterpsyche" or "character" => DesignRuleTypes.CharacterPsycheRule,
            "conflict" or "conflictengine" or "conflictrule" => DesignRuleTypes.ConflictEngine,
            "writingtech" or "writingtechnique" or "craft" => DesignRuleTypes.WritingTech,
            "readerpromise" or "promise" => DesignRuleTypes.ReaderPromise,
            "styleguide" or "style" or "stylerule" => DesignRuleTypes.StyleGuide,
            "itemrule" or "world_setting" => DesignRuleTypes.WorldCoreRule,
            _ => null
        };
    }

    /// <summary>
    /// Maps a knowledge classification ConstraintLevel to a design rule ConstraintLevel.
    /// </summary>
    private static string MapConstraintLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return DesignRuleConstraintLevels.Reference;

        return level.Trim().ToLowerInvariant() switch
        {
            "hardconstraint" or "mustsatisfy" or "hard" => DesignRuleConstraintLevels.MustSatisfy,
            "mustmention" or "soft" or "softconstraint" => DesignRuleConstraintLevels.MustMention,
            "forbidden" or "禁用" or "ban" => DesignRuleConstraintLevels.Forbidden,
            _ => DesignRuleConstraintLevels.Reference
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }
}
