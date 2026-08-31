using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.ChapterBlueprints;
using TM.Web.NovelAgentWeb.Services.DesignRules;

namespace TM.Web.NovelAgentWeb.Services.Production;

/// <summary>
/// Enriches a chapter context package with persisted DesignRules and ChapterBlueprint.
/// Acts as a decorator over the pure <see cref="IChapterPackageBuilder"/>, integrating
/// the new structured persistence layer into the production package.
/// </summary>
public interface IChapterPackageEnrichmentService
{
    /// <summary>
    /// Enriches an already-built package with design rules and persisted blueprint from DB.
    /// </summary>
    Task<ChapterContextPackageSummary> EnrichAsync(
        ChapterContextPackageSummary package,
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default);
}

public sealed class ChapterPackageEnrichmentService : IChapterPackageEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NovelAgentDbContext _db;
    private readonly IDesignRuleAggregationService _designRules;
    private readonly IChapterBlueprintService _blueprints;
    private readonly ILogger<ChapterPackageEnrichmentService> _logger;

    public ChapterPackageEnrichmentService(
        NovelAgentDbContext db,
        IDesignRuleAggregationService designRules,
        IChapterBlueprintService blueprints,
        ILogger<ChapterPackageEnrichmentService> logger)
    {
        _db = db;
        _designRules = designRules;
        _blueprints = blueprints;
        _logger = logger;
    }

    public async Task<ChapterContextPackageSummary> EnrichAsync(
        ChapterContextPackageSummary package,
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning("Cannot enrich package without userId/projectId");
            return package;
        }

        // 1. Inject active design rules
        try
        {
            var activeRules = await _designRules
                .GetActiveDesignRulesAsync(userId, projectId, ct)
                .ConfigureAwait(false);

            package.DesignRules = activeRules
                .Select(MapToSnapshot)
                .ToList();

            _logger.LogInformation(
                "Enriched package for chapter {ChapterId} with {Count} design rules",
                chapterId,
                package.DesignRules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich package with design rules");
        }

        // 2. Inject persisted chapter blueprint (if exists)
        if (!string.IsNullOrWhiteSpace(chapterId))
        {
            try
            {
                var blueprint = await _blueprints
                    .GetActiveBlueprintAsync(userId, projectId, chapterId, ct)
                    .ConfigureAwait(false);

                if (blueprint != null)
                {
                    package.PersistedBlueprint = MapToBlueprintSnapshot(blueprint);
                    _logger.LogInformation(
                        "Enriched package for chapter {ChapterId} with blueprint v{Version}",
                        chapterId,
                        blueprint.Version);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich package with persisted blueprint");
            }
        }

        return package;
    }

    private static DesignRuleSnapshot MapToSnapshot(ProjectDesignRule rule)
    {
        var sourceIds = new List<string>();
        try
        {
            sourceIds = JsonSerializer.Deserialize<List<string>>(
                rule.SourceKnowledgeIdsJson ?? "[]",
                JsonOptions) ?? new List<string>();
        }
        catch
        {
            // Ignore JSON deserialization errors
        }

        return new DesignRuleSnapshot
        {
            RuleId = rule.Id,
            RuleType = rule.RuleType,
            RuleContent = rule.RuleContent,
            ConstraintLevel = rule.ConstraintLevel,
            Scope = rule.Scope,
            ScopeTarget = rule.ScopeTarget,
            Priority = rule.Priority,
            Version = rule.Version,
            SourceKnowledgeIds = sourceIds
        };
    }

    private static PersistedChapterBlueprintSnapshot MapToBlueprintSnapshot(ChapterBlueprint bp)
    {
        var keyEvents = DeserializeStrings(bp.KeyEventsJson);
        var characters = DeserializeStrings(bp.CharactersJson);
        var requiredKnowledgeIds = DeserializeStrings(bp.RequiredKnowledgeIdsJson);
        var appliedDesignRuleIds = DeserializeStrings(bp.AppliedDesignRuleIdsJson);
        var dependencyChapterIds = DeserializeStrings(bp.DependencyChapterIdsJson);

        return new PersistedChapterBlueprintSnapshot
        {
            BlueprintId = bp.Id,
            ChapterId = bp.ChapterId,
            ChapterIndex = bp.ChapterIndex,
            Title = bp.Title,
            Intent = bp.Intent,
            KeyEvents = keyEvents,
            Characters = characters,
            ConflictNote = bp.ConflictNote,
            EndingNote = bp.EndingNote,
            RequiredKnowledgeIds = requiredKnowledgeIds,
            AppliedDesignRuleIds = appliedDesignRuleIds,
            DependencyChapterIds = dependencyChapterIds,
            Version = bp.Version,
            Status = bp.Status,
            TargetWordCount = bp.TargetWordCount
        };
    }

    private static List<string> DeserializeStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
