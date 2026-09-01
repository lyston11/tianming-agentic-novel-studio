using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class EntityDependencyRetriever : IEntityDependencyRetriever
{
    private readonly NovelAgentDbContext _db;

    public EntityDependencyRetriever(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        string userId,
        string projectId,
        RagQueryPlan plan,
        int topK,
        RagSnapshotScope? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
            return [];

        var result = new List<RetrievalCandidate>();
        foreach (var entity in plan.EntityReferences)
        {
            var canon = await _db.CanonChanges.AsNoTracking()
                .Where(item => item.UserId == userId && item.ProjectId == projectId &&
                    item.Status == "committed" && item.Subject.Contains(entity))
                .Where(item => snapshot == null || item.CreatedAt <= snapshot.FrozenAt)
                .OrderByDescending(item => item.CreatedAt)
                .Take(topK)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            AddRanked(result, "canon_change", canon, $"entity:canon:{entity}");

            var continuity = IsPostgres
                ? await QueryIdsAsync("""
                    SELECT id AS "Value" FROM continuity_summaries
                    WHERE user_id = @userId AND project_id = @projectId AND status = 'committed'
                      AND summary_json::text ILIKE '%' || @term || '%'
                    ORDER BY created_at DESC LIMIT @topK
                    """, userId, projectId, entity, topK, cancellationToken)
                : await _db.ContinuitySummaries.AsNoTracking()
                    .Where(item => item.UserId == userId && item.ProjectId == projectId &&
                        item.Status == "committed" && item.SummaryJson.Contains(entity))
                    .OrderByDescending(item => item.CreatedAt)
                    .Take(topK)
                    .Select(item => item.Id)
                    .ToListAsync(cancellationToken);
            AddRanked(result, "continuity_summary", continuity, $"entity:continuity:{entity}");

            var knowledge = snapshot == null
                ? await _db.KnowledgeBases.AsNoTracking()
                .Where(item => item.UserId == userId && !item.IsArchived &&
                    (item.Title.Contains(entity) || (item.Tags != null && item.Tags.Contains(entity))))
                .OrderByDescending(item => item.Weight)
                .ThenByDescending(item => item.CreatedAt)
                .Take(topK)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)
                : [];
            AddRanked(result, "knowledge_base", knowledge, $"entity:knowledge:{entity}");

            var chapters = IsPostgres
                ? await QueryIdsAsync("""
                    SELECT chapter_id AS "Value" FROM chapter_blueprints
                    WHERE user_id = @userId AND project_id = @projectId
                      AND characters_json::text ILIKE '%' || @term || '%'
                    ORDER BY chapter_index DESC LIMIT @topK
                    """, userId, projectId, entity, topK, cancellationToken)
                : await _db.ChapterBlueprints.AsNoTracking()
                    .Where(item => item.UserId == userId && item.ProjectId == projectId &&
                        item.CharactersJson.Contains(entity))
                    .OrderByDescending(item => item.ChapterIndex)
                    .Take(topK)
                    .Select(item => item.ChapterId)
                    .ToListAsync(cancellationToken);
            AddRanked(result, "chapter", chapters, $"entity:chapter:{entity}");
        }

        if (plan.TargetChapterIds.Count > 0)
        {
            var targetIds = plan.TargetChapterIds.Distinct(StringComparer.Ordinal).ToArray();
            var blueprints = await _db.ChapterBlueprints.AsNoTracking()
                .Where(item => item.UserId == userId && item.ProjectId == projectId && targetIds.Contains(item.ChapterId))
                .Select(item => new { item.ChapterId, item.DependencyChapterIdsJson, item.RequiredKnowledgeIdsJson })
                .ToListAsync(cancellationToken);
            AddRanked(result, "chapter", targetIds, "dependency:target");
            foreach (var blueprint in blueprints)
            {
                AddRanked(
                    result,
                    "chapter",
                    ParseIds(blueprint.DependencyChapterIdsJson),
                    $"dependency:chapter:{blueprint.ChapterId}");
                AddRanked(
                    result,
                    "knowledge_base",
                    ParseIds(blueprint.RequiredKnowledgeIdsJson),
                    $"dependency:knowledge:{blueprint.ChapterId}");
            }
        }

        return result;
    }

    private bool IsPostgres =>
        _db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    private async Task<List<string>> QueryIdsAsync(
        string sql,
        string userId,
        string projectId,
        string term,
        int topK,
        CancellationToken cancellationToken) =>
        await _db.Database.SqlQueryRaw<string>(
                sql,
                new NpgsqlParameter("userId", userId),
                new NpgsqlParameter("projectId", projectId),
                new NpgsqlParameter("term", term),
                new NpgsqlParameter("topK", topK))
            .ToListAsync(cancellationToken);

    private static string[] ParseIds(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static void AddRanked(
        ICollection<RetrievalCandidate> output,
        string sourceType,
        IReadOnlyList<string> sourceIds,
        string channel)
    {
        for (var rank = 0; rank < sourceIds.Count; rank++)
        {
            if (string.IsNullOrWhiteSpace(sourceIds[rank]))
                continue;
            output.Add(new RetrievalCandidate(
                sourceType,
                sourceIds[rank],
                channel,
                rank + 1,
                1d / (rank + 1),
                null));
        }
    }
}
