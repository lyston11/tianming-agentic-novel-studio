using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionOutboxDispatcher : IProductionOutboxDispatcher
{
    private const int MaxSerialClaimBatchSize = 5;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan DefaultProcessingLeaseTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumLeaseRenewalInterval = TimeSpan.FromMilliseconds(20);

    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IOutboxMaterialVectorIndexingService _materialIndexing;
    private readonly ILogger<ProductionOutboxDispatcher> _logger;
    private readonly IProductionEventWriter? _events;
    private readonly IChapterFactOutboxProcessor? _chapterFactProcessor;
    private readonly IChapterCommitPostCommitFinalizer? _chapterCommitFinalizer;
    private readonly IAgentRuntimeEventService? _runtimeEvents;
    private readonly IBackgroundUserContext? _backgroundUsers;
    private readonly IBackgroundClaimConnectionFactory? _claimConnections;
    private readonly TimeSpan _processingLeaseTimeout;
    private readonly string _processingOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public ProductionOutboxDispatcher(
        NovelAgentDbContext db,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        IOutboxMaterialVectorIndexingService materialIndexing,
        ILogger<ProductionOutboxDispatcher> logger,
        IProductionEventWriter? events = null,
        IChapterFactOutboxProcessor? chapterFactProcessor = null,
        IChapterCommitPostCommitFinalizer? chapterCommitFinalizer = null,
        IAgentRuntimeEventService? runtimeEvents = null,
        IBackgroundUserContext? backgroundUsers = null,
        IBackgroundClaimConnectionFactory? claimConnections = null,
        TimeSpan? processingLeaseTimeout = null)
    {
        _db = db;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _materialIndexing = materialIndexing;
        _logger = logger;
        _events = events;
        _chapterFactProcessor = chapterFactProcessor;
        _chapterCommitFinalizer = chapterCommitFinalizer;
        _runtimeEvents = runtimeEvents;
        _backgroundUsers = backgroundUsers;
        _claimConnections = claimConnections;
        _processingLeaseTimeout = processingLeaseTimeout ?? DefaultProcessingLeaseTimeout;
        if (_processingLeaseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(processingLeaseTimeout));
    }

    public async Task<int> DispatchPendingAsync(
        int maxItems = 20,
        CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(maxItems, 1, MaxSerialClaimBatchSize);
        var usePostgresClaims = _db.Database.IsRelational() &&
            _db.Database.GetDbConnection() is NpgsqlConnection;
        IReadOnlyList<string> candidateIds = [];
        if (!usePostgresClaims)
        {
            var now = DateTime.UtcNow;
            var staleProcessingCutoff = now.Subtract(_processingLeaseTimeout);
            candidateIds = await _db.OutboxEvents
                .AsNoTracking()
                .Where(e =>
                    e.Status == "pending" ||
                    (e.Status == "retryable_failed" &&
                     (e.NextAttemptAt == null || e.NextAttemptAt <= now)) ||
                    (e.Status == "processing" &&
                     ((e.ProcessingLeaseExpiresAt == null && e.UpdatedAt <= staleProcessingCutoff) ||
                      e.ProcessingLeaseExpiresAt <= now)))
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var dispatched = 0;
        for (var itemIndex = 0; itemIndex < batchSize; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimedOutboxEvent? workItem;
            if (usePostgresClaims)
            {
                workItem = (await ClaimPostgresAsync(1, cancellationToken).ConfigureAwait(false))
                    .SingleOrDefault();
                if (workItem == null)
                    break;
            }
            else
            {
                if (itemIndex >= candidateIds.Count)
                    break;
                workItem = new ClaimedOutboxEvent(candidateIds[itemIndex], null);
            }

            IDisposable? backgroundUserScope = null;
            OutboxEvent? evt;
            if (workItem.UserId != null)
            {
                if (_backgroundUsers == null)
                    throw new InvalidOperationException("PostgreSQL Outbox 后台处理必须提供用户作用域。");
                backgroundUserScope = _backgroundUsers.Push(workItem.UserId);
                _db.ChangeTracker.Clear();
                evt = await _db.OutboxEvents.SingleOrDefaultAsync(item =>
                    item.Id == workItem.EventId &&
                    item.UserId == workItem.UserId &&
                    item.Status == "processing" &&
                    item.ProcessingOwner == _processingOwner,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                evt = await TryClaimAsync(workItem.EventId, cancellationToken).ConfigureAwait(false);
                if (evt != null)
                    backgroundUserScope = _backgroundUsers?.Push(evt.UserId);
            }
            if (evt == null)
            {
                backgroundUserScope?.Dispose();
                continue;
            }

            using (backgroundUserScope)
            {
                using var heartbeatStop = new CancellationTokenSource();
                var heartbeatTask = RenewProcessingLeaseUntilStoppedAsync(
                    evt.Id,
                    evt.UserId,
                    heartbeatStop.Token);
                try
                {
                    await AppendOutboxProductionEventAsync(evt, "outbox_processing", "running", "后台 outbox 开始处理。", null, cancellationToken)
                        .ConfigureAwait(false);
                    await DispatchOneAsync(evt, cancellationToken).ConfigureAwait(false);
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await StopLeaseRenewalAsync(heartbeatStop, heartbeatTask).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await StopLeaseRenewalAsync(heartbeatStop, heartbeatTask).ConfigureAwait(false);
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    var attempts = checked(evt.Attempts + 1);
                    var status = attempts >= MaxAttempts ? "failed" : "retryable_failed";
                    DateTime? nextAttemptAt = status == "retryable_failed"
                        ? DateTime.UtcNow.Add(ComputeRetryDelay(attempts))
                        : null;
                    var transitioned = await TryTransitionOwnedAsync(
                            evt,
                            status,
                            attempts,
                            ex.Message,
                            nextAttemptAt,
                            completedAt: null,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (transitioned)
                    {
                        var message = status == "failed"
                            ? "后台 outbox 重试耗尽，已终止处理。"
                            : "后台 outbox 处理失败，已排队重试。";
                        await AppendOutboxProductionEventAsync(evt, "outbox_failed", status, message, ex.Message, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    _logger.LogWarning(
                        ex,
                        transitioned
                            ? "Production outbox event {EventId} failed at attempt {Attempt} with status {Status}"
                            : "Production outbox event {EventId} lost lease before failure could be recorded",
                        evt.Id,
                        attempts,
                        status);
                    continue;
                }

                await StopLeaseRenewalAsync(heartbeatStop, heartbeatTask).ConfigureAwait(false);
                var completed = await TryTransitionOwnedAsync(
                        evt,
                        "completed",
                        evt.Attempts,
                        lastError: null,
                        nextAttemptAt: null,
                        completedAt: DateTime.UtcNow,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!completed)
                {
                    _logger.LogWarning(
                        "Production outbox event {EventId} lost lease before completion could be recorded",
                        evt.Id);
                    continue;
                }

                await AppendOutboxProductionEventAsync(evt, "outbox_completed", "completed", "后台 outbox 处理完成。", null, cancellationToken)
                    .ConfigureAwait(false);
                await TryAppendRunCompletedAsync(evt, cancellationToken)
                    .ConfigureAwait(false);
                dispatched++;
            }
        }

        return dispatched;
    }

    private async Task<IReadOnlyList<ClaimedOutboxEvent>> ClaimPostgresAsync(
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (_claimConnections == null)
            throw new InvalidOperationException("PostgreSQL Outbox claim 必须使用专用 worker 数据库连接。");
        await using var connection = await _claimConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_id, user_id FROM claim_outbox_events(@owner, @lease_seconds, @max_items)";
        command.Parameters.Add(new NpgsqlParameter("owner", _processingOwner));
        command.Parameters.Add(new NpgsqlParameter("lease_seconds", checked((int)_processingLeaseTimeout.TotalSeconds)));
        command.Parameters.Add(new NpgsqlParameter("max_items", maxItems));
        var claims = new List<ClaimedOutboxEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claims.Add(new ClaimedOutboxEvent(
                reader.GetString(reader.GetOrdinal("event_id")),
                reader.GetString(reader.GetOrdinal("user_id"))));
        }
        return claims;
    }

    private async Task<OutboxEvent?> TryClaimAsync(string eventId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now.Add(_processingLeaseTimeout);
        var staleProcessingCutoff = now.Subtract(_processingLeaseTimeout);

        if (_db.Database.IsRelational())
        {
            var affected = await _db.OutboxEvents
                .Where(e =>
                    e.Id == eventId &&
                    (e.Status == "pending" ||
                     (e.Status == "retryable_failed" &&
                      (e.NextAttemptAt == null || e.NextAttemptAt <= now)) ||
                     (e.Status == "processing" &&
                      ((e.ProcessingLeaseExpiresAt == null && e.UpdatedAt <= staleProcessingCutoff) ||
                       e.ProcessingLeaseExpiresAt <= now))))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(e => e.Status, "processing")
                        .SetProperty(e => e.LastError, (string?)null)
                        .SetProperty(e => e.NextAttemptAt, (DateTime?)null)
                        .SetProperty(e => e.ProcessingOwner, _processingOwner)
                        .SetProperty(e => e.ProcessingLeaseExpiresAt, leaseExpiresAt)
                        .SetProperty(e => e.UpdatedAt, now),
                    ct)
                .ConfigureAwait(false);
            if (affected == 0)
                return null;

            return await _db.OutboxEvents.SingleAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        }

        var evt = await _db.OutboxEvents.SingleOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (evt == null || !CanClaim(evt, now, staleProcessingCutoff))
            return null;

        evt.Status = "processing";
        evt.LastError = null;
        evt.NextAttemptAt = null;
        evt.ProcessingOwner = _processingOwner;
        evt.ProcessingLeaseExpiresAt = leaseExpiresAt;
        evt.UpdatedAt = now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return evt;
    }

    private static bool CanClaim(OutboxEvent evt, DateTime now, DateTime staleProcessingCutoff)
    {
        if (evt.Status == "pending")
            return true;
        if (evt.Status == "retryable_failed")
            return evt.NextAttemptAt == null || evt.NextAttemptAt <= now;
        if (evt.Status != "processing")
            return false;

        return evt.ProcessingLeaseExpiresAt == null
            ? evt.UpdatedAt <= staleProcessingCutoff
            : evt.ProcessingLeaseExpiresAt <= now;
    }

    private async Task RenewProcessingLeaseUntilStoppedAsync(
        string eventId,
        string userId,
        CancellationToken cancellationToken)
    {
        var renewalInterval = TimeSpan.FromTicks(Math.Max(
            MinimumLeaseRenewalInterval.Ticks,
            _processingLeaseTimeout.Ticks / 3));
        try
        {
            while (true)
            {
                await Task.Delay(renewalInterval, cancellationToken).ConfigureAwait(false);
                var renewed = await TryRenewProcessingLeaseAsync(eventId, userId, cancellationToken)
                    .ConfigureAwait(false);
                if (!renewed)
                {
                    _logger.LogWarning(
                        "Production outbox event {EventId} lease renewal stopped because ownership changed",
                        eventId);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Production outbox event {EventId} lease renewal failed", eventId);
        }
    }

    private async Task<bool> TryRenewProcessingLeaseAsync(
        string eventId,
        string userId,
        CancellationToken cancellationToken)
    {
        var options = _db.GetService<IDbContextOptions>() as DbContextOptions<NovelAgentDbContext>
            ?? throw new InvalidOperationException("Outbox lease renewal requires configured NovelAgent DbContext options.");
        await using var leaseDb = new NovelAgentDbContext(options);
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now.Add(_processingLeaseTimeout);
        if (leaseDb.Database.IsRelational())
        {
            return await leaseDb.OutboxEvents
                    .Where(evt =>
                        evt.Id == eventId &&
                        evt.UserId == userId &&
                        evt.Status == "processing" &&
                        evt.ProcessingOwner == _processingOwner)
                    .ExecuteUpdateAsync(setters => setters
                            .SetProperty(evt => evt.ProcessingLeaseExpiresAt, leaseExpiresAt)
                            .SetProperty(evt => evt.UpdatedAt, now),
                        cancellationToken)
                    .ConfigureAwait(false) == 1;
        }

        var outbox = await leaseDb.OutboxEvents.SingleOrDefaultAsync(
                evt => evt.Id == eventId && evt.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);
        if (outbox == null ||
            outbox.Status != "processing" ||
            !string.Equals(outbox.ProcessingOwner, _processingOwner, StringComparison.Ordinal))
        {
            return false;
        }

        outbox.ProcessingLeaseExpiresAt = leaseExpiresAt;
        outbox.UpdatedAt = now;
        await leaseDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryTransitionOwnedAsync(
        OutboxEvent evt,
        string status,
        int attempts,
        string? lastError,
        DateTime? nextAttemptAt,
        DateTime? completedAt,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_db.Database.IsRelational())
        {
            var affected = await _db.OutboxEvents
                .Where(current =>
                    current.Id == evt.Id &&
                    current.UserId == evt.UserId &&
                    current.Status == "processing" &&
                    current.ProcessingOwner == _processingOwner)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(current => current.Status, status)
                        .SetProperty(current => current.Attempts, attempts)
                        .SetProperty(current => current.LastError, lastError)
                        .SetProperty(current => current.NextAttemptAt, nextAttemptAt)
                        .SetProperty(current => current.CompletedAt, completedAt)
                        .SetProperty(current => current.ProcessingOwner, (string?)null)
                        .SetProperty(current => current.ProcessingLeaseExpiresAt, (DateTime?)null)
                        .SetProperty(current => current.UpdatedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
                return false;

            _db.Entry(evt).State = EntityState.Detached;
        }
        else
        {
            await _db.Entry(evt).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (evt.Status != "processing" ||
                !string.Equals(evt.ProcessingOwner, _processingOwner, StringComparison.Ordinal))
            {
                return false;
            }

            ApplyTransition(evt, status, attempts, lastError, nextAttemptAt, completedAt, now);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        ApplyTransition(evt, status, attempts, lastError, nextAttemptAt, completedAt, now);
        return true;
    }

    private static void ApplyTransition(
        OutboxEvent evt,
        string status,
        int attempts,
        string? lastError,
        DateTime? nextAttemptAt,
        DateTime? completedAt,
        DateTime updatedAt)
    {
        evt.Status = status;
        evt.Attempts = attempts;
        evt.LastError = lastError;
        evt.NextAttemptAt = nextAttemptAt;
        evt.CompletedAt = completedAt;
        evt.ProcessingOwner = null;
        evt.ProcessingLeaseExpiresAt = null;
        evt.UpdatedAt = updatedAt;
    }

    private static async Task StopLeaseRenewalAsync(
        CancellationTokenSource heartbeatStop,
        Task heartbeatTask)
    {
        heartbeatStop.Cancel();
        await heartbeatTask.ConfigureAwait(false);
    }

    private sealed record ClaimedOutboxEvent(string EventId, string? UserId);

    private async Task DispatchOneAsync(OutboxEvent evt, CancellationToken ct)
    {
        if (evt.EventType == "project_domain_event" && evt.AggregateType == "domain_event")
        {
            await ValidateProjectDomainEventAsync(evt, ct).ConfigureAwait(false);
            return;
        }

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

    private async Task ValidateProjectDomainEventAsync(OutboxEvent evt, CancellationToken ct)
    {
        var exists = await _db.DomainEvents.AsNoTracking().AnyAsync(domainEvent =>
                domainEvent.Id == evt.AggregateId &&
                domainEvent.UserId == evt.UserId &&
                domainEvent.ProjectId == evt.ProjectId,
            ct).ConfigureAwait(false);
        if (!exists)
            throw new KeyNotFoundException(
                $"Domain event {evt.AggregateId} was not found in the authoritative user/project scope.");
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
        await AppendRuntimeProgressEventAsync(
                evt.RuntimeRunId,
                evt.UserId,
                evt.ProjectId,
                NovelAgentProductionStages.ToCanonicalStage(ResolveOutboxStage(evt)),
                status,
                $"{message} {evt.EventType}/{evt.AggregateType}",
                "outbox_event",
                evt.Id,
                new
                {
                    outboxEventId = evt.Id,
                    evt.EventType,
                    evt.AggregateType,
                    evt.AggregateId,
                    evt.Attempts,
                    error
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task TryAppendRunCompletedAsync(OutboxEvent completedOutbox, CancellationToken ct)
    {
        if (_events == null ||
            string.IsNullOrWhiteSpace(completedOutbox.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(completedOutbox.ProjectId))
        {
            return;
        }

        var runtimeRunId = completedOutbox.RuntimeRunId.Trim();
        var projectId = completedOutbox.ProjectId.Trim();
        var hasPendingRunOutbox = await _db.OutboxEvents
            .AsNoTracking()
            .AnyAsync(evt =>
                evt.RuntimeRunId == runtimeRunId &&
                evt.ProjectId == projectId &&
                evt.Status != "completed",
                ct)
            .ConfigureAwait(false);
        if (hasPendingRunOutbox)
            return;

        var existingCompletion = await _db.ProductionEvents
            .AsNoTracking()
            .AnyAsync(evt =>
                evt.RuntimeRunId == runtimeRunId &&
                evt.ProjectId == projectId &&
                evt.EventType == "chapter_production_completed",
                ct)
            .ConfigureAwait(false);
        if (existingCompletion)
            return;

        var commitEvent = await _db.ProductionEvents
            .AsNoTracking()
            .Where(evt =>
                evt.RuntimeRunId == runtimeRunId &&
                evt.ProjectId == projectId &&
                evt.EventType == "chapter_committed")
            .OrderByDescending(evt => evt.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (commitEvent == null)
            return;

        var context = await ResolveOutboxEventContextAsync(completedOutbox, ct).ConfigureAwait(false);
        var completedOutboxCount = await _db.OutboxEvents
            .AsNoTracking()
            .CountAsync(evt =>
                evt.RuntimeRunId == runtimeRunId &&
                evt.ProjectId == projectId &&
                evt.Status == "completed",
                ct)
            .ConfigureAwait(false);

        await _events.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: runtimeRunId,
                    UserId: completedOutbox.UserId,
                    ProjectId: projectId,
                    ChapterId: FirstNonEmpty(commitEvent.ChapterId, context.ChapterId, completedOutbox.AggregateId),
                    PackageId: FirstNonEmpty(commitEvent.PackageId, context.PackageId),
                    EventType: "chapter_production_completed",
                    Stage: NovelAgentProductionStages.RunCompleted,
                    Status: "completed",
                    Message: "本轮章节生产的提交、事实沉淀和后台索引已全部完成。",
                    ArtifactType: "chapter_production_run",
                    ArtifactId: runtimeRunId,
                    Data: new
                    {
                        runtimeRunId,
                        completedOutboxCount,
                        finalOutboxEventId = completedOutbox.Id,
                        chapterId = FirstNonEmpty(commitEvent.ChapterId, context.ChapterId, completedOutbox.AggregateId),
                        packageId = FirstNonEmpty(commitEvent.PackageId, context.PackageId)
                    }),
                ct)
            .ConfigureAwait(false);
        await AppendRuntimeProgressEventAsync(
                runtimeRunId,
                completedOutbox.UserId,
                projectId,
                NovelAgentProductionStages.RunCompleted,
                "completed",
                "本轮章节生产的提交、事实沉淀和后台索引已全部完成。",
                "chapter_production_run",
                runtimeRunId,
                new
                {
                    runtimeRunId,
                    completedOutboxCount,
                    finalOutboxEventId = completedOutbox.Id,
                    chapterId = FirstNonEmpty(commitEvent.ChapterId, context.ChapterId, completedOutbox.AggregateId),
                    packageId = FirstNonEmpty(commitEvent.PackageId, context.PackageId)
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task AppendRuntimeProgressEventAsync(
        string? runtimeRunId,
        string userId,
        string? projectId,
        string stage,
        string status,
        string message,
        string artifactType,
        string artifactId,
        object data,
        CancellationToken ct)
    {
        if (_runtimeEvents == null ||
            string.IsNullOrWhiteSpace(runtimeRunId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var runId = runtimeRunId.Trim();
        var runtimeRun = await _db.AgentRuntimeRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(run => run.Id == runId, ct)
            .ConfigureAwait(false);
        if (runtimeRun == null || string.IsNullOrWhiteSpace(runtimeRun.SessionId))
            return;

        await _runtimeEvents.AppendAsync(
                new CreateAgentRuntimeEventRequest(
                    RuntimeRunId: runId,
                    UserId: userId,
                    SessionId: runtimeRun.SessionId,
                    ProjectId: FirstNonEmpty(runtimeRun.ProjectId, runtimeRun.LockedProjectId, projectId),
                    Type: "production_progress",
                    Message: message,
                    Data: data,
                    Stage: NovelAgentProductionStages.ToCanonicalStage(stage),
                    Status: status,
                    ArtifactType: artifactType,
                    ArtifactId: artifactId,
                    DisplaySurface: AgentRuntimeEventSurface.Workflow,
                    DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                    PublishToSse: true),
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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

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
