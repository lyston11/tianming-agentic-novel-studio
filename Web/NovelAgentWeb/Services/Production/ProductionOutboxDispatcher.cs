using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionOutboxDispatcher : IProductionOutboxDispatcher
{
    private static readonly TimeSpan ProcessingLeaseTimeout = TimeSpan.FromMinutes(15);

    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IOutboxMaterialVectorIndexingService _materialIndexing;
    private readonly ILogger<ProductionOutboxDispatcher> _logger;
    private readonly IProductionEventWriter? _events;
    private readonly IChapterFactOutboxProcessor? _chapterFactProcessor;
    private readonly IChapterCommitPostCommitFinalizer? _chapterCommitFinalizer;

    public ProductionOutboxDispatcher(
        NovelAgentDbContext db,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        IOutboxMaterialVectorIndexingService materialIndexing,
        ILogger<ProductionOutboxDispatcher> logger,
        IProductionEventWriter? events = null,
        IChapterFactOutboxProcessor? chapterFactProcessor = null,
        IChapterCommitPostCommitFinalizer? chapterCommitFinalizer = null)
    {
        _db = db;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _materialIndexing = materialIndexing;
        _logger = logger;
        _events = events;
        _chapterFactProcessor = chapterFactProcessor;
        _chapterCommitFinalizer = chapterCommitFinalizer;
    }

    public async Task<int> DispatchPendingAsync(
        int maxItems = 20,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var staleProcessingCutoff = now.Subtract(ProcessingLeaseTimeout);
        var events = await _db.OutboxEvents
            .Where(e =>
                e.Status == "pending" ||
                (e.Status == "retryable_failed" &&
                 (e.NextAttemptAt == null || e.NextAttemptAt <= now)) ||
                (e.Status == "processing" &&
                 e.UpdatedAt <= staleProcessingCutoff))
            .OrderBy(e => e.CreatedAt)
            .Take(Math.Clamp(maxItems, 1, 100))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dispatched = 0;
        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evt.Status = "processing";
            evt.LastError = null;
            evt.NextAttemptAt = null;
            evt.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await AppendOutboxProductionEventAsync(evt, "outbox_processing", "running", "后台 outbox 开始处理。", null, cancellationToken)
                    .ConfigureAwait(false);
                await DispatchOneAsync(evt, cancellationToken).ConfigureAwait(false);
                evt.Status = "completed";
                evt.CompletedAt = DateTime.UtcNow;
                evt.LastError = null;
                evt.NextAttemptAt = null;
                evt.UpdatedAt = DateTime.UtcNow;
                await AppendOutboxProductionEventAsync(evt, "outbox_completed", "completed", "后台 outbox 处理完成。", null, cancellationToken)
                    .ConfigureAwait(false);
                dispatched++;
            }
            catch (Exception ex)
            {
                evt.Attempts += 1;
                evt.Status = "retryable_failed";
                evt.LastError = ex.Message;
                evt.NextAttemptAt = DateTime.UtcNow.Add(ComputeRetryDelay(evt.Attempts));
                evt.UpdatedAt = DateTime.UtcNow;
                await AppendOutboxProductionEventAsync(evt, "outbox_failed", "retryable_failed", "后台 outbox 处理失败，已排队重试。", ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogWarning(ex, "Production outbox event {EventId} failed at attempt {Attempt}", evt.Id, evt.Attempts);
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return dispatched;
    }

    private async Task DispatchOneAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (evt.EventType == "index_chapter_content" && evt.AggregateType == "chapter_version")
        {
            await IndexChapterVersionAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "delete_chapter_content" && evt.AggregateType == "chapter")
        {
            await DeleteChapterContentVectorsAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "delete_project_content" && evt.AggregateType == "project")
        {
            await DeleteProjectContentVectorsAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "index_material_content" && evt.AggregateType == "material")
        {
            await IndexMaterialContentAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "delete_material_content" && evt.AggregateType == "material")
        {
            await DeleteMaterialContentVectorsAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "index_knowledge_content" && evt.AggregateType == "knowledge")
        {
            await IndexKnowledgeContentAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "delete_knowledge_content" && evt.AggregateType == "knowledge")
        {
            await DeleteKnowledgeContentVectorsAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "index_memory_content" && evt.AggregateType == "memory")
        {
            await IndexMemoryContentAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "index_story_bible_canon" && evt.AggregateType == "story_bible")
        {
            await IndexStoryBibleCanonAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "extract_chapter_continuity_facts" && evt.AggregateType == "chapter")
        {
            if (_chapterFactProcessor == null)
                throw new InvalidOperationException("Chapter fact outbox processor is not registered.");

            await _chapterFactProcessor.ProcessAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        if (evt.EventType == "finalize_chapter_commit_metadata" && evt.AggregateType == "chapter")
        {
            if (_chapterCommitFinalizer == null)
                throw new InvalidOperationException("Chapter commit post-commit finalizer is not registered.");

            await _chapterCommitFinalizer.ProcessOutboxAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        throw new NotSupportedException($"Unsupported production outbox event {evt.EventType}/{evt.AggregateType}");
    }

    private async Task DeleteChapterContentVectorsAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.ProjectId))
            throw new InvalidOperationException($"Chapter vector deletion requires project id for outbox event {evt.Id}.");

        await _vectorStore.DeleteVectorsByFilterAsync(
                evt.UserId,
                new Dictionary<string, object>
                {
                    ["project_id"] = evt.ProjectId,
                    ["source_type"] = "chapter",
                    ["source_id"] = evt.AggregateId
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task DeleteProjectContentVectorsAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.ProjectId))
            throw new InvalidOperationException($"Project vector deletion requires project id for outbox event {evt.Id}.");

        await _vectorStore.DeleteVectorsByFilterAsync(
                evt.UserId,
                new Dictionary<string, object>
                {
                    ["project_id"] = evt.ProjectId
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task IndexMaterialContentAsync(OutboxEvent evt, CancellationToken ct)
    {
        await _materialIndexing.IndexMaterialAsync(evt.AggregateId, evt.UserId, ct)
            .ConfigureAwait(false);
    }

    private async Task DeleteMaterialContentVectorsAsync(OutboxEvent evt, CancellationToken ct)
    {
        var filters = new Dictionary<string, object>
        {
            ["source_type"] = "material",
            ["source_id"] = evt.AggregateId
        };

        if (!string.IsNullOrWhiteSpace(evt.ProjectId))
            filters["project_id"] = evt.ProjectId;

        await _vectorStore.DeleteVectorsByFilterAsync(evt.UserId, filters, ct)
            .ConfigureAwait(false);
    }

    private async Task IndexKnowledgeContentAsync(OutboxEvent evt, CancellationToken ct)
    {
        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == evt.AggregateId && k.UserId == evt.UserId, ct)
            .ConfigureAwait(false);
        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge {evt.AggregateId} not found for outbox event {evt.Id}.");

        if (knowledge.IsArchived)
        {
            await DeleteKnowledgeContentVectorsAsync(evt, ct).ConfigureAwait(false);
            return;
        }

        var pointId = Guid.TryParse(knowledge.VectorId, out _)
            ? knowledge.VectorId!
            : Guid.NewGuid().ToString();

        await _vectorStore.DeleteVectorsByFilterAsync(
                evt.UserId,
                BuildKnowledgeVectorFilter(evt),
                ct)
            .ConfigureAwait(false);

        var vector = await _embedding
            .EncodeAsync($"{knowledge.Title} {knowledge.Content}", EmbeddingMode.Passage, ct)
            .ConfigureAwait(false);

        if (!await _vectorStore.CollectionExistsAsync(evt.UserId, ct).ConfigureAwait(false))
            await _vectorStore.InitializeUserCollectionAsync(evt.UserId, ct).ConfigureAwait(false);

        await _vectorStore.UpsertVectorsAsync(
                evt.UserId,
                new List<VectorData>
                {
                    new()
                    {
                        Id = pointId,
                        Vector = vector,
                        UserId = evt.UserId,
                        ProjectId = knowledge.SourceProjectId ?? string.Empty,
                        SourceType = "knowledge",
                        SourceId = knowledge.Id,
                        Content = knowledge.Content,
                        Metadata = new Dictionary<string, object>
                        {
                            ["entry_type"] = knowledge.EntryType,
                            ["title"] = knowledge.Title,
                            ["version_id"] = knowledge.Id
                        }
                    }
                },
                ct)
            .ConfigureAwait(false);

        knowledge.VectorId = pointId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task DeleteKnowledgeContentVectorsAsync(OutboxEvent evt, CancellationToken ct)
    {
        await _vectorStore.DeleteVectorsByFilterAsync(evt.UserId, BuildKnowledgeVectorFilter(evt), ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, object> BuildKnowledgeVectorFilter(OutboxEvent evt)
    {
        var filters = new Dictionary<string, object>
        {
            ["source_type"] = "knowledge",
            ["source_id"] = evt.AggregateId
        };
        if (!string.IsNullOrWhiteSpace(evt.ProjectId))
            filters["project_id"] = evt.ProjectId;
        return filters;
    }

    private async Task IndexStoryBibleCanonAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.ProjectId))
            throw new InvalidOperationException($"StoryBible canon indexing requires project id for outbox event {evt.Id}.");

        var document = await _db.ContentDocuments
            .AsNoTracking()
            .Where(d =>
                d.UserId == evt.UserId &&
                d.ProjectId == evt.ProjectId &&
                d.SourceType == "story_bible" &&
                d.SourceId == evt.AggregateId &&
                d.DocumentRole == "aggregate_json" &&
                d.Status == "active")
            .OrderByDescending(d => d.Version)
            .ThenByDescending(d => d.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (document == null)
            throw new KeyNotFoundException($"StoryBible aggregate document for project {evt.AggregateId} not found.");

        var json = await LoadDocumentTextAsync(document.Id, ct).ConfigureAwait(false);
        var storyBible = JsonSerializer.Deserialize<StoryBibleDocument>(json, JsonHelper.CnDefault)
            ?? new StoryBibleDocument();
        var entries = storyBible.CanonLedger
            .Where(entry => entry.Status == CanonLedgerEntryStatus.Canon)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Title) || !string.IsNullOrWhiteSpace(entry.Content))
            .ToList();

        await _vectorStore.DeleteVectorsByFilterAsync(
                evt.UserId,
                new Dictionary<string, object>
                {
                    ["project_id"] = evt.ProjectId,
                    ["source_type"] = "story_bible_canon"
                },
                ct)
            .ConfigureAwait(false);
        if (entries.Count == 0)
            return;

        var vectorTexts = entries.Select(FormatCanonLedgerVectorText).ToList();
        var vectors = await _embedding
            .EncodeBatchAsync(vectorTexts, EmbeddingMode.Passage, ct)
            .ConfigureAwait(false);
        var payloads = entries
            .Select((entry, index) => new VectorData
            {
                Id = EnsureUuidPointId(null),
                Vector = vectors[index],
                UserId = evt.UserId,
                ProjectId = evt.ProjectId,
                SourceType = "story_bible_canon",
                SourceId = entry.Id,
                Content = vectorTexts[index],
                Metadata = new Dictionary<string, object>
                {
                    ["version_id"] = $"{document.Id}:{entry.Id}",
                    ["content_document_id"] = document.Id,
                    ["title"] = entry.Title ?? string.Empty,
                    ["canon_type"] = entry.Type.ToString(),
                    ["canon_status"] = entry.Status.ToString(),
                    ["rationale"] = entry.Rationale ?? string.Empty
                }
            })
            .ToList();

        if (!await _vectorStore.CollectionExistsAsync(evt.UserId, ct).ConfigureAwait(false))
            await _vectorStore.InitializeUserCollectionAsync(evt.UserId, ct).ConfigureAwait(false);

        await _vectorStore.UpsertVectorsAsync(evt.UserId, payloads, ct).ConfigureAwait(false);
    }

    private async Task<string> LoadDocumentTextAsync(string documentId, CancellationToken ct)
    {
        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkText)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return string.Concat(chunks);
    }

    private static string FormatCanonLedgerVectorText(CanonLedgerEntry entry)
    {
        var parts = new[]
        {
            entry.Title,
            entry.Type.ToString(),
            entry.Content,
            entry.Rationale
        };
        return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private async Task IndexMemoryContentAsync(OutboxEvent evt, CancellationToken ct)
    {
        var memory = await _db.AgentMemories
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == evt.AggregateId && m.UserId == evt.UserId, ct)
            .ConfigureAwait(false);
        if (memory == null)
            throw new KeyNotFoundException($"Memory {evt.AggregateId} not found for outbox event {evt.Id}.");

        var content = NormalizeMemoryContent(memory.Content);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var vector = await _embedding.EncodeAsync(content, EmbeddingMode.Passage, ct)
            .ConfigureAwait(false);

        if (!await _vectorStore.CollectionExistsAsync(evt.UserId, ct).ConfigureAwait(false))
            await _vectorStore.InitializeUserCollectionAsync(evt.UserId, ct).ConfigureAwait(false);

        var filters = new Dictionary<string, object>
        {
            ["source_type"] = "memory",
            ["source_id"] = memory.MemoryType,
            ["project_id"] = memory.ProjectId ?? string.Empty
        };
        await _vectorStore.DeleteVectorsByFilterAsync(evt.UserId, filters, ct)
            .ConfigureAwait(false);

        await _vectorStore.UpsertVectorsAsync(
                evt.UserId,
                new List<VectorData>
                {
                    new()
                    {
                        Id = EnsureUuidPointId(null),
                        Vector = vector,
                        UserId = evt.UserId,
                        ProjectId = memory.ProjectId ?? string.Empty,
                        SourceType = "memory",
                        SourceId = memory.MemoryType,
                        Content = content,
                        Metadata = new Dictionary<string, object>
                        {
                            ["memory_id"] = memory.Id,
                            ["memory_type"] = memory.MemoryType,
                            ["version_id"] = $"{memory.Id}:{memory.UpdatedAt.Ticks}"
                        }
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static string NormalizeMemoryContent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        try
        {
            var value = JsonSerializer.Deserialize<string>(raw);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        catch (JsonException)
        {
        }

        return raw;
    }

    private async Task IndexChapterVersionAsync(OutboxEvent evt, CancellationToken ct)
    {
        var version = await _db.ChapterVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v =>
                v.Id == evt.AggregateId &&
                v.UserId == evt.UserId &&
                (evt.ProjectId == null || v.ProjectId == evt.ProjectId),
                ct)
            .ConfigureAwait(false);
        if (version == null)
            throw new KeyNotFoundException($"Chapter version {evt.AggregateId} not found for outbox event {evt.Id}.");

        var document = await _db.ContentDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == version.ContentDocumentId &&
                d.UserId == version.UserId &&
                d.ProjectId == version.ProjectId,
                ct)
            .ConfigureAwait(false);
        if (document == null)
            throw new KeyNotFoundException($"Content document {version.ContentDocumentId} not found for chapter version {version.Id}.");

        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (chunks.Count == 0)
            return;

        var points = await _db.ContentVectorPoints
            .Where(p => p.DocumentId == document.Id && p.ChunkId != null)
            .ToDictionaryAsync(p => p.ChunkId!, ct)
            .ConfigureAwait(false);
        var bindings = chunks
            .Where(chunk => points.ContainsKey(chunk.Id))
            .Select(chunk => new ChapterVectorBinding(chunk, points[chunk.Id], EnsureUuidPointId(points[chunk.Id].QdrantPointId)))
            .ToList();
        if (bindings.Count == 0)
            return;

        try
        {
            await _vectorStore.DeleteVectorsByFilterAsync(
                    version.UserId,
                    new Dictionary<string, object>
                    {
                        ["project_id"] = version.ProjectId,
                        ["source_type"] = "chapter",
                        ["source_id"] = version.ChapterId
                    },
                    ct)
                .ConfigureAwait(false);

            var vectors = await _embedding
                .EncodeBatchAsync(bindings.Select(b => b.Chunk.ChunkText).ToList(), EmbeddingMode.Passage, ct)
                .ConfigureAwait(false);

            var payloads = bindings
                .Select((binding, index) => new VectorData
                {
                    Id = binding.VectorId,
                    Vector = vectors[index],
                    UserId = version.UserId,
                    ProjectId = version.ProjectId,
                    SourceType = "chapter",
                    SourceId = version.ChapterId,
                    ChapterId = version.ChapterId,
                    ChunkIndex = binding.Chunk.ChunkIndex,
                    Content = binding.Chunk.ChunkText,
                    Metadata = new Dictionary<string, object>
                    {
                        ["version_id"] = version.Id,
                        ["chapter_title"] = version.Title,
                        ["chapter_version_id"] = version.Id,
                        ["content_document_id"] = document.Id
                    }
                })
                .ToList();

            if (!await _vectorStore.CollectionExistsAsync(version.UserId, ct).ConfigureAwait(false))
                await _vectorStore.InitializeUserCollectionAsync(version.UserId, ct).ConfigureAwait(false);

            await _vectorStore.UpsertVectorsAsync(version.UserId, payloads, ct).ConfigureAwait(false);
            MarkVectorPointsCompleted(version.UserId, bindings);
        }
        catch (Exception ex)
        {
            MarkVectorPointsFailed(bindings.Select(binding => binding.Point), ex.Message);
            throw;
        }
    }

    private async Task AppendOutboxProductionEventAsync(
        OutboxEvent evt,
        string eventType,
        string status,
        string message,
        string? error,
        CancellationToken ct)
    {
        if (_events == null ||
            string.IsNullOrWhiteSpace(evt.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(evt.ProjectId))
        {
            return;
        }

        var context = await ResolveOutboxEventContextAsync(evt, ct).ConfigureAwait(false);
        await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: evt.RuntimeRunId,
                    UserId: evt.UserId,
                    ProjectId: evt.ProjectId,
                    ChapterId: context.ChapterId,
                    PackageId: context.PackageId,
                    EventType: eventType,
                    Stage: ResolveOutboxStage(evt),
                    Status: status,
                    Message: $"{message} {evt.EventType}/{evt.AggregateType}",
                    ArtifactType: "outbox_event",
                    ArtifactId: evt.Id,
                    Data: new
                    {
                        outboxEventId = evt.Id,
                        evt.EventType,
                        evt.AggregateType,
                        evt.AggregateId,
                        evt.Attempts,
                        error
                    }),
                ct)
            .ConfigureAwait(false);
    }

    private static string ResolveOutboxStage(OutboxEvent evt)
    {
        return evt.EventType switch
        {
            "extract_chapter_continuity_facts" => "post_commit_facts",
            "finalize_chapter_commit_metadata" => "post_commit_metadata",
            _ when evt.EventType.StartsWith("index_", StringComparison.OrdinalIgnoreCase) => "index_outbox",
            _ when evt.EventType.StartsWith("delete_", StringComparison.OrdinalIgnoreCase) => "index_outbox",
            _ => "outbox"
        };
    }

    private async Task<OutboxEventContext> ResolveOutboxEventContextAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (evt.AggregateType == "chapter_version")
        {
            var version = await _db.ChapterVersions
                .AsNoTracking()
                .Where(v => v.Id == evt.AggregateId && v.UserId == evt.UserId)
                .Select(v => new OutboxEventContext(v.ChapterId, v.PackageId))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (version != null)
                return version;
        }

        if (evt.AggregateType == "chapter")
            return new OutboxEventContext(evt.AggregateId, null);

        return new OutboxEventContext(null, null);
    }

    private void MarkVectorPointsCompleted(
        string userId,
        IReadOnlyList<ChapterVectorBinding> bindings)
    {
        var indexedAt = DateTime.UtcNow;
        foreach (var binding in bindings)
        {
            binding.Point.QdrantCollection = QdrantVectorStore.GetCollectionName(userId);
            binding.Point.QdrantPointId = binding.VectorId;
            binding.Point.VectorModel = _embedding.GetType().Name;
            binding.Point.IndexStatus = "completed";
            binding.Point.IndexedAt = indexedAt;
            binding.Point.ErrorMessage = null;
        }
    }

    private void MarkVectorPointsFailed(IEnumerable<ContentVectorPoint> points, string errorMessage)
    {
        foreach (var point in points)
        {
            point.VectorModel = _embedding.GetType().Name;
            point.IndexStatus = "failed";
            point.ErrorMessage = errorMessage;
            point.IndexedAt = null;
        }
    }

    private static TimeSpan ComputeRetryDelay(int attempts)
    {
        var seconds = attempts switch
        {
            <= 1 => 30,
            2 => 120,
            3 => 300,
            _ => 900
        };
        return TimeSpan.FromSeconds(seconds);
    }

    private static string EnsureUuidPointId(string? existing) =>
        Guid.TryParse(existing, out _) ? existing : Guid.NewGuid().ToString();

    private sealed record ChapterVectorBinding(
        ContentChunk Chunk,
        ContentVectorPoint Point,
        string VectorId);

    private sealed record OutboxEventContext(string? ChapterId, string? PackageId);
}
