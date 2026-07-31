using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public interface ILegacyVectorSourceRebuilder
{
    Task<int> RebuildUserAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class LegacyVectorSourceRebuilder : ILegacyVectorSourceRebuilder
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IOutboxMaterialVectorIndexingService _materials;

    public LegacyVectorSourceRebuilder(
        NovelAgentDbContext db,
        IVectorStore vectors,
        IMicroEmbeddingService embedding,
        IOutboxMaterialVectorIndexingService materials)
    {
        _db = db;
        _vectors = vectors;
        _embedding = embedding;
        _materials = materials;
    }

    public async Task<int> RebuildUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var materialIds = await _db.Materials.AsNoTracking()
            .Where(material => material.UserId == userId)
            .OrderBy(material => material.CreatedAt)
            .Select(material => material.Id)
            .ToListAsync(cancellationToken);
        foreach (var materialId in materialIds)
            await _materials.IndexMaterialAsync(materialId, userId, cancellationToken);
        var materialPointCount = await _db.Materials.AsNoTracking()
            .Where(material => material.UserId == userId)
            .SumAsync(material => material.VectorChunkCount, cancellationToken);

        var sources = await LoadSourcesAsync(userId, cancellationToken);
        if (sources.Count == 0)
            return materialPointCount;
        var embeddings = await _embedding.EncodeBatchAsync(
            sources.Select(source => source.Text).ToArray(),
            EmbeddingMode.Passage,
            cancellationToken);
        if (embeddings.Length != sources.Count)
            throw new InvalidOperationException("Embedding 批量结果数量与兼容索引 source 数量不一致。");

        var payloads = sources.Select((source, index) => new VectorData
        {
            Id = MultiScaleVectorIndexer.DeterministicPointId(
                userId,
                source.SourceType,
                source.ProjectId,
                source.SourceId,
                source.VersionId),
            Vector = embeddings[index],
            UserId = userId,
            ProjectId = source.ProjectId,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            Content = source.Text,
            Metadata = source.Metadata
        }).ToList();
        await _vectors.UpsertVectorsAsync(userId, payloads, cancellationToken);

        var pointIdByKnowledgeId = payloads
            .Where(payload => payload.SourceType == "knowledge")
            .ToDictionary(payload => payload.SourceId, payload => payload.Id, StringComparer.Ordinal);
        var knowledgeRows = await _db.KnowledgeBases
            .Where(knowledge => knowledge.UserId == userId && pointIdByKnowledgeId.Keys.Contains(knowledge.Id))
            .ToListAsync(cancellationToken);
        foreach (var knowledge in knowledgeRows)
            knowledge.VectorId = pointIdByKnowledgeId[knowledge.Id];
        await _db.SaveChangesAsync(cancellationToken);
        return materialPointCount + payloads.Count;
    }

    private async Task<List<LegacyVectorSource>> LoadSourcesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var result = new List<LegacyVectorSource>();
        var knowledge = await _db.KnowledgeBases.AsNoTracking()
            .Where(entry => entry.UserId == userId && !entry.IsArchived)
            .OrderBy(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
        result.AddRange(knowledge.Select(entry => new LegacyVectorSource(
            entry.SourceProjectId ?? string.Empty,
            "knowledge",
            entry.Id,
            $"{entry.Title} {entry.Content}",
            entry.Id,
            new Dictionary<string, object>
            {
                ["entry_type"] = entry.EntryType,
                ["title"] = entry.Title,
                ["version_id"] = entry.Id
            })));

        var memories = await _db.AgentMemories.AsNoTracking()
            .Where(memory => memory.UserId == userId)
            .OrderBy(memory => memory.UpdatedAt)
            .ToListAsync(cancellationToken);
        var latestMemories = memories
            .GroupBy(memory => (ProjectId: memory.ProjectId ?? string.Empty, memory.MemoryType))
            .Select(group => group.OrderByDescending(memory => memory.UpdatedAt).ThenByDescending(memory => memory.Id).First())
            .Where(memory => !string.IsNullOrWhiteSpace(memory.Content));
        result.AddRange(latestMemories.Select(memory =>
        {
            var content = NormalizeMemoryContent(memory.Content);
            return new LegacyVectorSource(
                memory.ProjectId ?? string.Empty,
                "memory",
                memory.MemoryType,
                content,
                $"{memory.Id}:{memory.UpdatedAt.Ticks}",
                new Dictionary<string, object>
                {
                    ["memory_id"] = memory.Id,
                    ["memory_type"] = memory.MemoryType,
                    ["version_id"] = $"{memory.Id}:{memory.UpdatedAt.Ticks}"
                });
        }));

        var documents = await _db.ContentDocuments.AsNoTracking()
            .Where(document =>
                document.UserId == userId &&
                document.SourceType == "story_bible" &&
                document.DocumentRole == "aggregate_json" &&
                document.Status == "active")
            .OrderBy(document => document.ProjectId)
            .ThenByDescending(document => document.Version)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.ProjectId))
                continue;
            var chunks = await _db.ContentChunks.AsNoTracking()
                .Where(chunk => chunk.DocumentId == document.Id)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync(cancellationToken);
            var json = string.Concat(chunks);
            var storyBible = JsonSerializer.Deserialize<StoryBibleDocument>(json, JsonHelper.CnDefault)
                ?? throw new InvalidOperationException($"Story Bible 文档 {document.Id} 不是有效 JSON。");
            result.AddRange(storyBible.CanonLedger
                .Where(entry => entry.Status == CanonLedgerEntryStatus.Canon)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => new LegacyVectorSource(
                    document.ProjectId,
                    "story_bible_canon",
                    entry.Id,
                    FormatCanonEntry(entry),
                    $"{document.Id}:{entry.Id}",
                    new Dictionary<string, object>
                    {
                        ["version_id"] = $"{document.Id}:{entry.Id}",
                        ["content_document_id"] = document.Id,
                        ["title"] = entry.Title ?? string.Empty,
                        ["canon_type"] = entry.Type.ToString(),
                        ["canon_status"] = entry.Status.ToString(),
                        ["rationale"] = entry.Rationale ?? string.Empty
                    })));
        }
        return result.Where(source => !string.IsNullOrWhiteSpace(source.Text)).ToList();
    }

    private static string NormalizeMemoryContent(string raw)
    {
        try
        {
            var value = JsonSerializer.Deserialize<string>(raw);
            return string.IsNullOrWhiteSpace(value) ? raw : value;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string FormatCanonEntry(CanonLedgerEntry entry) =>
        string.Join("\n", new[] { entry.Title, entry.Type.ToString(), entry.Content, entry.Rationale }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private sealed record LegacyVectorSource(
        string ProjectId,
        string SourceType,
        string SourceId,
        string Text,
        string VersionId,
        Dictionary<string, object> Metadata);
}
