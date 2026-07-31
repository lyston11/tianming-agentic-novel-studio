using Microsoft.EntityFrameworkCore;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class PostgresFullTextRetriever : IPostgresFullTextRetriever
{
    private readonly NovelAgentDbContext _db;

    public PostgresFullTextRetriever(NovelAgentDbContext db)
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
        var terms = plan.SemanticQueries.Concat(plan.EntityReferences)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (terms.Length == 0 || topK <= 0)
            return [];

        if (_db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            return await SearchPostgresAsync(userId, projectId, plan, terms, topK, snapshot, cancellationToken);

        var candidates = new List<RetrievalCandidate>();
        for (var termIndex = 0; termIndex < terms.Length; termIndex++)
        {
            var term = terms[termIndex];
            if (plan.Routes.Contains(RagRoute.Knowledge))
                await SearchKnowledgeAsync(userId, term, termIndex, topK, candidates, snapshot, cancellationToken);
            if (plan.Routes.Contains(RagRoute.Style))
                await SearchStylesAsync(userId, term, termIndex, topK, candidates, snapshot, cancellationToken);
            if (HasStoryRoute(plan))
                await SearchStoryAsync(userId, projectId, term, termIndex, topK, candidates, snapshot, cancellationToken);
        }

        return candidates;
    }

    private async Task<IReadOnlyList<RetrievalCandidate>> SearchPostgresAsync(
        string userId,
        string projectId,
        RagQueryPlan plan,
        IReadOnlyList<string> terms,
        int topK,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var candidates = new List<RetrievalCandidate>();
        for (var termIndex = 0; termIndex < terms.Count; termIndex++)
        {
            var term = terms[termIndex];
            if (plan.Routes.Contains(RagRoute.Knowledge))
            {
                AddSqlHits(candidates, "knowledge_chunk", $"fts:knowledge_chunk:{termIndex}",
                    await QueryAsync(KnowledgeChunksSql, userId, null, term, topK, snapshot, cancellationToken));
                AddSqlHits(candidates, "knowledge_section", $"fts:knowledge_section:{termIndex}",
                    await QueryAsync(KnowledgeSectionsSql, userId, null, term, topK, snapshot, cancellationToken));
                AddSqlHits(candidates, "knowledge_entry", $"fts:knowledge_entry:{termIndex}",
                    await QueryAsync(KnowledgeEntriesSql, userId, null, term, topK, snapshot, cancellationToken));
            }
            if (plan.Routes.Contains(RagRoute.Style))
            {
                AddSqlHits(candidates, "style_profile", $"fts:style_profile:{termIndex}",
                    await QueryAsync(StyleProfilesSql, userId, null, term, topK, snapshot, cancellationToken));
            }
            if (HasStoryRoute(plan))
            {
                AddSqlHits(candidates, "chapter", $"fts:chapter:{termIndex}",
                    await QueryAsync(ContentChunksSql, userId, projectId, term, topK, snapshot, cancellationToken));
                AddSqlHits(candidates, "continuity_summary", $"fts:continuity_summary:{termIndex}",
                    await QueryAsync(ContinuitySummariesSql, userId, projectId, term, topK, snapshot, cancellationToken));
                AddSqlHits(candidates, "canon_change", $"fts:canon_change:{termIndex}",
                    await QueryAsync(CanonChangesSql, userId, projectId, term, topK, snapshot, cancellationToken));
            }
        }
        return candidates;
    }

    private async Task<List<RagSqlHit>> QueryAsync(
        string sql,
        string userId,
        string? projectId,
        string term,
        int topK,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var parameters = new List<object>
        {
            new NpgsqlParameter("userId", userId),
            new NpgsqlParameter("term", term),
            new NpgsqlParameter("topK", topK),
            new NpgsqlParameter("knowledgeVersion", snapshot?.KnowledgeVersion ?? long.MaxValue),
            new NpgsqlParameter("frozenAt", snapshot?.FrozenAt ?? DateTime.MaxValue),
            new NpgsqlParameter("useSnapshot", snapshot != null)
        };
        if (projectId != null)
            parameters.Add(new NpgsqlParameter("projectId", projectId));
        return await _db.Database.SqlQueryRaw<RagSqlHit>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
    }

    private static void AddSqlHits(
        ICollection<RetrievalCandidate> output,
        string sourceType,
        string channel,
        IReadOnlyList<RagSqlHit> hits)
    {
        for (var rank = 0; rank < hits.Count; rank++)
        {
            var metadata = new Dictionary<string, object>();
            var chunkIndex = hits[rank].ChunkIndex;
            if (chunkIndex is int value)
                metadata["chunk_index"] = value;
            var documentId = hits[rank].DocumentId;
            if (!string.IsNullOrWhiteSpace(documentId))
                metadata["content_document_id"] = documentId;
            output.Add(new RetrievalCandidate(
                sourceType,
                hits[rank].SourceId,
                channel,
                rank + 1,
                hits[rank].Score,
                metadata));
        }
    }

    private sealed class RagSqlHit
    {
        public string SourceId { get; set; } = string.Empty;
        public double Score { get; set; }
        public int? ChunkIndex { get; set; }
        public string? DocumentId { get; set; }
    }

    private const string KnowledgeChunksSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(text, '')), websearch_to_tsquery('simple', @term)), similarity(text, @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM knowledge_chunks
        WHERE user_id = @userId AND knowledge_version <= @knowledgeVersion AND (
            to_tsvector('simple', coalesce(text, '')) @@ websearch_to_tsquery('simple', @term)
            OR text ILIKE '%' || @term || '%' OR similarity(text, @term) > 0.18)
        ORDER BY "Score" DESC, id
        LIMIT @topK
        """;

    private const string KnowledgeSectionsSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, '')), websearch_to_tsquery('simple', @term)), similarity(coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, ''), @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM knowledge_sections
        WHERE user_id = @userId AND knowledge_version <= @knowledgeVersion AND (
            to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, '')) @@ websearch_to_tsquery('simple', @term)
            OR (coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, '')) ILIKE '%' || @term || '%'
            OR similarity(coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, ''), @term) > 0.18)
        ORDER BY "Score" DESC, id
        LIMIT @topK
        """;

    private const string KnowledgeEntriesSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, '')), websearch_to_tsquery('simple', @term)), similarity(coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, ''), @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM knowledge_entries
        WHERE user_id = @userId AND status = 'active' AND knowledge_version <= @knowledgeVersion AND (
            to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, '')) @@ websearch_to_tsquery('simple', @term)
            OR (coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, '')) ILIKE '%' || @term || '%'
            OR similarity(coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, ''), @term) > 0.18)
        ORDER BY "Score" DESC, id
        LIMIT @topK
        """;

    private const string StyleProfilesSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(features_json::text, '')), websearch_to_tsquery('simple', @term)), similarity(features_json::text, @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM style_profiles
        WHERE user_id = @userId AND status = 'active' AND profile_kind = 'abstract_features_only'
          AND knowledge_version <= @knowledgeVersion AND (
            to_tsvector('simple', coalesce(features_json::text, '')) @@ websearch_to_tsquery('simple', @term)
            OR features_json::text ILIKE '%' || @term || '%' OR similarity(features_json::text, @term) > 0.18)
        ORDER BY "Score" DESC, id
        LIMIT @topK
        """;

    private const string ContentChunksSql = """
        SELECT "SourceId", "Score", "ChunkIndex", "DocumentId"
        FROM (
            SELECT DISTINCT ON (d.source_id)
                   d.source_id AS "SourceId",
                   GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(c.chunk_text, '')), websearch_to_tsquery('simple', @term)), similarity(c.chunk_text, @term))::double precision AS "Score",
                   c.chunk_index AS "ChunkIndex", d.id AS "DocumentId"
            FROM content_chunks c
            JOIN content_documents d ON d.id = c.document_id
            WHERE d.user_id = @userId AND d.project_id = @projectId
              AND d.source_type = 'chapter' AND d.document_role = 'chapter_body'
              AND ((NOT @useSnapshot AND d.status = 'active') OR (@useSnapshot AND d.created_at <= @frozenAt))
              AND (to_tsvector('simple', coalesce(c.chunk_text, '')) @@ websearch_to_tsquery('simple', @term)
                   OR c.chunk_text ILIKE '%' || @term || '%' OR similarity(c.chunk_text, @term) > 0.18)
            ORDER BY d.source_id, "Score" DESC, d.version DESC, c.chunk_index
        ) ranked
        ORDER BY "Score" DESC, "SourceId"
        LIMIT @topK
        """;

    private const string ContinuitySummariesSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(summary_json::text, '')), websearch_to_tsquery('simple', @term)), similarity(summary_json::text, @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM continuity_summaries
        WHERE user_id = @userId AND project_id = @projectId AND status = 'committed'
          AND created_at <= @frozenAt AND (
            to_tsvector('simple', coalesce(summary_json::text, '')) @@ websearch_to_tsquery('simple', @term)
            OR summary_json::text ILIKE '%' || @term || '%' OR similarity(summary_json::text, @term) > 0.18)
        ORDER BY "Score" DESC, created_at DESC
        LIMIT @topK
        """;

    private const string CanonChangesSql = """
        SELECT id AS "SourceId",
               GREATEST(ts_rank_cd(to_tsvector('simple', coalesce(subject, '') || ' ' || coalesce(change_json::text, '')), websearch_to_tsquery('simple', @term)), similarity(coalesce(subject, '') || ' ' || coalesce(change_json::text, ''), @term))::double precision AS "Score",
               NULL::integer AS "ChunkIndex", NULL::text AS "DocumentId"
        FROM canon_changes
        WHERE user_id = @userId AND project_id = @projectId AND status = 'committed'
          AND created_at <= @frozenAt AND (
            to_tsvector('simple', coalesce(subject, '') || ' ' || coalesce(change_json::text, '')) @@ websearch_to_tsquery('simple', @term)
            OR (coalesce(subject, '') || ' ' || coalesce(change_json::text, '')) ILIKE '%' || @term || '%'
            OR similarity(coalesce(subject, '') || ' ' || coalesce(change_json::text, ''), @term) > 0.18)
        ORDER BY "Score" DESC, created_at DESC
        LIMIT @topK
        """;

    private async Task SearchKnowledgeAsync(
        string userId,
        string term,
        int termIndex,
        int topK,
        List<RetrievalCandidate> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var chunks = await _db.KnowledgeChunks.AsNoTracking()
            .Where(item => item.UserId == userId && item.Text.Contains(term) &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion))
            .OrderBy(item => item.ChunkIndex)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "knowledge_chunk", chunks, $"fts:knowledge_chunk:{termIndex}");

        var sections = await _db.KnowledgeSections.AsNoTracking()
            .Where(item => item.UserId == userId &&
                (item.Title.Contains(term) || item.Summary.Contains(term) || item.Text.Contains(term)))
            .Where(item => snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion)
            .OrderBy(item => item.SectionIndex)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "knowledge_section", sections, $"fts:knowledge_section:{termIndex}");

        var entries = await _db.KnowledgeEntries.AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "active" &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion) &&
                (item.Title.Contains(term) || item.Content.Contains(term) || item.Summary.Contains(term)))
            .OrderByDescending(item => item.KnowledgeVersion)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "knowledge_entry", entries, $"fts:knowledge_entry:{termIndex}");
    }

    private async Task SearchStylesAsync(
        string userId,
        string term,
        int termIndex,
        int topK,
        List<RetrievalCandidate> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var styles = await _db.StyleProfiles.AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "active" && item.FeaturesJson.Contains(term) &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion))
            .OrderByDescending(item => item.KnowledgeVersion)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "style_profile", styles, $"fts:style_profile:{termIndex}");
    }

    private async Task SearchStoryAsync(
        string userId,
        string projectId,
        string term,
        int termIndex,
        int topK,
        List<RetrievalCandidate> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var chunks = await _db.ContentChunks.AsNoTracking()
            .Where(item =>
                item.Document.UserId == userId &&
                item.Document.ProjectId == projectId &&
                item.Document.SourceType == "chapter" &&
                (snapshot == null ? item.Document.Status == "active" : item.Document.CreatedAt <= snapshot.FrozenAt) &&
                item.ChunkText.Contains(term))
            .OrderByDescending(item => item.Document.Version)
            .ThenBy(item => item.ChunkIndex)
            .Take(topK)
            .Select(item => new { item.Document.SourceId, item.ChunkIndex, item.DocumentId })
            .ToListAsync(cancellationToken);
        for (var rank = 0; rank < chunks.Count; rank++)
        {
            output.Add(new RetrievalCandidate(
                "chapter",
                chunks[rank].SourceId,
                $"fts:chapter:{termIndex}",
                rank + 1,
                1d / (rank + 1),
                new Dictionary<string, object>
                {
                    ["chunk_index"] = chunks[rank].ChunkIndex,
                    ["content_document_id"] = chunks[rank].DocumentId
                }));
        }

        var summaries = await _db.ContinuitySummaries.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId &&
                item.Status == "committed" && item.SummaryJson.Contains(term))
            .Where(item => snapshot == null || item.CreatedAt <= snapshot.FrozenAt)
            .OrderByDescending(item => item.CreatedAt)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "continuity_summary", summaries, $"fts:continuity_summary:{termIndex}");

        var changes = await _db.CanonChanges.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId &&
                item.Status == "committed" &&
                (item.Subject.Contains(term) || item.ChangeJson.Contains(term)))
            .Where(item => snapshot == null || item.CreatedAt <= snapshot.FrozenAt)
            .OrderByDescending(item => item.CreatedAt)
            .Take(topK)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        AddRanked(output, "canon_change", changes, $"fts:canon_change:{termIndex}");
    }

    private static bool HasStoryRoute(RagQueryPlan plan) => plan.Routes.Any(route => route is
        RagRoute.Setting or RagRoute.Character or RagRoute.Continuity or RagRoute.Promise);

    private static void AddRanked(
        ICollection<RetrievalCandidate> output,
        string sourceType,
        IReadOnlyList<string> sourceIds,
        string channel)
    {
        for (var rank = 0; rank < sourceIds.Count; rank++)
        {
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
