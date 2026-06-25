using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionRelationStrengthSourceService : IRelationStrengthSourceService
{
    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionRelationStrengthSourceService(
        NovelAgentDbContext db,
        string userId,
        string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionRelationStrengthSourceService(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<IReadOnlyList<RelationStrengthFact>> LoadRelationStrengthFactsAsync()
    {
        if (string.IsNullOrWhiteSpace(_projectId))
            return Array.Empty<RelationStrengthFact>();

        if (_db != null)
            return await QueryAsync(_db).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Relation strength source requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await QueryAsync(db).ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
    }

    private async Task<IReadOnlyList<RelationStrengthFact>> QueryAsync(NovelAgentDbContext db)
    {
        var query = db.Characters
            .AsNoTracking()
            .Where(character => character.ProjectId == _projectId);
        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(character => character.UserId == _userId);

        var characters = await query.ToListAsync().ConfigureAwait(false);
        var projectIds = characters
            .Select(character => character.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return characters
            .SelectMany(character => ExtractFacts(character, projectIds))
            .GroupBy(fact => BuildPairKey(fact.LeftId, fact.RightId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(fact => fact.Strength).First())
            .ToList();
    }

    private static IEnumerable<RelationStrengthFact> ExtractFacts(
        Character character,
        HashSet<string> projectCharacterIds)
    {
        if (string.IsNullOrWhiteSpace(character.Relationships) ||
            string.IsNullOrWhiteSpace(character.Id))
        {
            yield break;
        }

        using var document = TryParse(character.Relationships);
        if (document == null)
            yield break;

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var fact = ExtractFact(character.Id, item, null, projectCharacterIds);
                if (fact != null)
                    yield return fact;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            var directFact = ExtractFact(character.Id, root, null, projectCharacterIds);
            if (directFact != null)
            {
                yield return directFact;
                yield break;
            }

            foreach (var property in root.EnumerateObject())
            {
                var fact = ExtractFact(character.Id, property.Value, property.Name, projectCharacterIds);
                if (fact != null)
                    yield return fact;
            }
        }
    }

    private static RelationStrengthFact? ExtractFact(
        string sourceId,
        JsonElement element,
        string? propertyTargetId,
        HashSet<string> projectCharacterIds)
    {
        var targetId = propertyTargetId;
        string? relationshipType = null;
        string? strengthHint = null;

        if (element.ValueKind == JsonValueKind.String)
        {
            relationshipType = element.GetString();
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            targetId = FirstPropertyString(
                element,
                "targetCharacterId",
                "TargetCharacterId",
                "targetId",
                "TargetId",
                "characterId",
                "CharacterId",
                "id",
                "Id") ?? targetId;
            relationshipType = FirstPropertyString(
                element,
                "relationshipType",
                "RelationshipType",
                "relationType",
                "RelationType",
                "relation",
                "Relation",
                "type",
                "Type");
            strengthHint = FirstPropertyString(
                element,
                "strengthHint",
                "StrengthHint",
                "strength",
                "Strength");
        }

        if (string.IsNullOrWhiteSpace(targetId) ||
            string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase) ||
            !projectCharacterIds.Contains(targetId))
        {
            return null;
        }

        return new RelationStrengthFact(
            sourceId,
            targetId,
            DetermineStrength(relationshipType, strengthHint));
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstPropertyString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static RelationStrength DetermineStrength(string? relationshipType, string? strengthHint)
    {
        if (string.Equals(strengthHint, "Strong", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(strengthHint, "强", StringComparison.OrdinalIgnoreCase))
        {
            return RelationStrength.Strong;
        }

        if (string.Equals(strengthHint, "Medium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(strengthHint, "中", StringComparison.OrdinalIgnoreCase))
        {
            return RelationStrength.Medium;
        }

        return relationshipType switch
        {
            "仇敌" or "师徒" or "恋人" or "血亲" or "宿敌" or "挚友" or "主仆" => RelationStrength.Strong,
            "同门" or "战友" or "同阵营" or "朋友" or "盟友" or "对手" or "同伴" => RelationStrength.Medium,
            _ => RelationStrength.Weak
        };
    }

    private static string BuildPairKey(string leftId, string rightId) =>
        string.Compare(leftId, rightId, StringComparison.Ordinal) < 0
            ? $"{leftId}_{rightId}"
            : $"{rightId}_{leftId}";
}
