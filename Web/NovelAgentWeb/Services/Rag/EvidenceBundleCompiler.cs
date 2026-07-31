using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class EvidenceBundleCompiler
{
    private readonly NovelAgentDbContext _db;

    public EvidenceBundleCompiler(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<EvidenceBundle> CompileAsync(
        string userId,
        string projectId,
        RagQueryPlan plan,
        IReadOnlyList<FusedRetrievalCandidate> candidates,
        RagSnapshotScope? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        var evidence = new Dictionary<(string SourceType, string SourceId), EvidenceItem>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            switch (candidate.SourceType)
            {
                case "knowledge_chunk":
                    await AddKnowledgeChunkAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "knowledge_section":
                    await AddKnowledgeSectionAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "knowledge_entry":
                    await AddKnowledgeEntryAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "knowledge_document":
                    await AddKnowledgeDocumentAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "knowledge_base":
                    await AddKnowledgeBaseAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "style_profile":
                    await AddStyleProfileAsync(userId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "chapter":
                    await AddChapterAsync(userId, projectId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "continuity_summary":
                    await AddContinuitySummaryAsync(userId, projectId, candidate, evidence, snapshot, cancellationToken);
                    break;
                case "canon_change":
                    await AddCanonChangeAsync(userId, projectId, candidate, evidence, snapshot, cancellationToken);
                    break;
            }
        }

        return new EvidenceBundle(plan, evidence.Values.ToArray());
    }

    private async Task AddKnowledgeChunkAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var chunk = await _db.KnowledgeChunks.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion),
            cancellationToken);
        if (chunk == null)
            return;

        Add(output, Item(chunk.UserId, chunk.ProjectId, "knowledge_chunk", chunk.Id, chunk.Text, candidate));
        var section = await _db.KnowledgeSections.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == chunk.SectionId && item.UserId == userId &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion),
            cancellationToken);
        if (section != null)
        {
            Add(output, Item(
                section.UserId,
                section.ProjectId,
                "knowledge_section",
                section.Id,
                FormatSection(section),
                candidate,
                "parent",
                0.85d));
        }

        var neighborIds = new[] { chunk.PreviousChunkId, chunk.NextChunkId }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (neighborIds.Length == 0)
            return;
        var neighbors = await _db.KnowledgeChunks.AsNoTracking()
            .Where(item => item.UserId == userId && item.SectionId == chunk.SectionId &&
                neighborIds.Contains(item.Id) &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion))
            .OrderBy(item => item.ChunkIndex)
            .ToListAsync(cancellationToken);
        foreach (var neighbor in neighbors)
        {
            Add(output, Item(
                neighbor.UserId,
                neighbor.ProjectId,
                "knowledge_chunk",
                neighbor.Id,
                neighbor.Text,
                candidate,
                "neighbor",
                0.8d));
        }
    }

    private async Task AddKnowledgeSectionAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var section = await _db.KnowledgeSections.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion),
            cancellationToken);
        if (section != null)
            Add(output, Item(section.UserId, section.ProjectId, "knowledge_section", section.Id, FormatSection(section), candidate));
    }

    private async Task AddKnowledgeEntryAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var entry = await _db.KnowledgeEntries.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId && item.Status == "active" &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion),
            cancellationToken);
        if (entry != null)
            Add(output, Item(entry.UserId, entry.ProjectId, "knowledge_entry", entry.Id, entry.Content, candidate));
    }

    private async Task AddKnowledgeDocumentAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var blob = await _db.KnowledgeDocumentBlobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId && item.Status == "processed" &&
                (snapshot == null || item.KnowledgeVersion <= snapshot.KnowledgeVersion),
            cancellationToken);
        if (blob == null)
            return;
        var summaries = await _db.KnowledgeSections.AsNoTracking()
            .Where(item => item.UserId == userId && item.DocumentBlobId == blob.Id)
            .OrderBy(item => item.SectionIndex)
            .Select(item => item.Summary)
            .ToListAsync(cancellationToken);
        var content = string.Join("\n", summaries.Where(summary => !string.IsNullOrWhiteSpace(summary)));
        if (content.Length > 0)
            Add(output, Item(blob.UserId, blob.ProjectId, "knowledge_document", blob.Id, content, candidate));
    }

    private async Task AddKnowledgeBaseAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot != null)
            return;
        var entry = await _db.KnowledgeBases.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId && !item.IsArchived,
            cancellationToken);
        if (entry != null)
            Add(output, Item(entry.UserId, entry.SourceProjectId ?? string.Empty, "knowledge_base", entry.Id, entry.Content, candidate));
    }

    private async Task AddStyleProfileAsync(
        string userId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var style = await _db.StyleProfiles.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId &&
                item.Status == "active" && item.ProfileKind == "abstract_features_only",
                // A frozen Goal may only use profiles derived from knowledge available at commit time.
                cancellationToken);
        if (style != null && snapshot != null && style.KnowledgeVersion > snapshot.KnowledgeVersion)
            style = null;
        if (style != null)
            Add(output, Item(style.UserId, style.ProjectId, "style_profile", style.Id, style.FeaturesJson, candidate));
    }

    private async Task AddChapterAsync(
        string userId,
        string projectId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var documentId = MetadataString(candidate.Metadata, "content_document_id");
        var query = _db.ContentDocuments.AsNoTracking().Where(document =>
            document.UserId == userId &&
            document.ProjectId == projectId &&
            document.SourceType == "chapter" &&
            document.SourceId == candidate.SourceId &&
            document.DocumentRole == "chapter_body" &&
            (snapshot == null ? document.Status == "active" : document.CreatedAt <= snapshot.FrozenAt));
        if (documentId != null && snapshot == null)
            query = query.Where(document => document.Id == documentId);
        var document = await query
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (document == null)
            return;

        var chunkIndex = MetadataInt(candidate.Metadata, "chunk_index");
        var chunks = _db.ContentChunks.AsNoTracking().Where(chunk => chunk.DocumentId == document.Id);
        if (chunkIndex.HasValue)
            chunks = chunks.Where(chunk => chunk.ChunkIndex == chunkIndex.Value);
        var text = string.Concat(await chunks.OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.ChunkText)
            .ToListAsync(cancellationToken));
        if (text.Length > 0)
            Add(output, Item(userId, projectId, "chapter", candidate.SourceId, text, candidate));
    }

    private async Task AddContinuitySummaryAsync(
        string userId,
        string projectId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var summary = await _db.ContinuitySummaries.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId &&
                item.ProjectId == projectId && item.Status == "committed",
            cancellationToken);
        if (summary != null && snapshot != null && summary.CreatedAt > snapshot.FrozenAt)
            summary = null;
        if (summary != null)
            Add(output, Item(summary.UserId, summary.ProjectId, "continuity_summary", summary.Id, summary.SummaryJson, candidate));
    }

    private async Task AddCanonChangeAsync(
        string userId,
        string projectId,
        FusedRetrievalCandidate candidate,
        IDictionary<(string, string), EvidenceItem> output,
        RagSnapshotScope? snapshot,
        CancellationToken cancellationToken)
    {
        var change = await _db.CanonChanges.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == candidate.SourceId && item.UserId == userId &&
                item.ProjectId == projectId && item.Status == "committed",
            cancellationToken);
        if (change != null && snapshot != null && change.CreatedAt > snapshot.FrozenAt)
            change = null;
        if (change != null)
            Add(output, Item(change.UserId, change.ProjectId, "canon_change", change.Id, change.ChangeJson, candidate));
    }

    private static EvidenceItem Item(
        string userId,
        string projectId,
        string sourceType,
        string sourceId,
        string content,
        FusedRetrievalCandidate candidate,
        string? expansionReason = null,
        double scoreMultiplier = 1d) => new(
            userId,
            projectId,
            sourceType,
            sourceId,
            content,
            candidate.Score * scoreMultiplier,
            candidate.Channels,
            expansionReason,
            candidate.Metadata);

    private static string FormatSection(KnowledgeSection section) =>
        string.Join("\n", new[] { section.Title, section.Summary, section.Text }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static void Add(
        IDictionary<(string, string), EvidenceItem> output,
        EvidenceItem item)
    {
        var key = (item.SourceType, item.SourceId);
        if (!output.TryGetValue(key, out var existing) ||
            (existing.ExpansionReason != null && item.ExpansionReason == null) ||
            item.Score > existing.Score)
        {
            output[key] = item;
        }
    }

    private static string? MetadataString(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
            return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? MetadataInt(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        var text = MetadataString(metadata, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
