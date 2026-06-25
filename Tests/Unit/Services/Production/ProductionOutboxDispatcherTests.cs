using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using Xunit;

namespace Tests.Unit.Services.Production;

public class ProductionOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchPendingAsync_IndexChapterContent_UpsertsVectorsAndMarksOutboxCompleted()
    {
        await using var db = CreateDb();
        var (version, outbox) = await SeedChapterVersionOutboxAsync(db);
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var point = await db.ContentVectorPoints.SingleAsync(p => p.DocumentId == version.ContentDocumentId);
        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var vector = Assert.Single(vectorStore.Upserted);

        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal("completed", point.IndexStatus);
        Assert.Equal(nameof(FixedEmbeddingService), point.VectorModel);
        Assert.NotNull(point.IndexedAt);
        Assert.Equal(point.QdrantPointId, vector.Id);
        Assert.True(Guid.TryParse(vector.Id, out _));
        Assert.Equal("project-1", vector.ProjectId);
        Assert.Equal("project-1-chapter-001", vector.SourceId);
        Assert.Contains("第一章", vector.Content);
        Assert.NotNull(vector.Metadata);
        Assert.Equal(version.Id, vector.Metadata["version_id"]);
        Assert.Equal(version.Id, vector.Metadata["chapter_version_id"]);
        Assert.Equal(version.ContentDocumentId, vector.Metadata["content_document_id"]);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "chapter") &&
            Equals(filter["source_id"], "project-1-chapter-001"));
    }

    [Fact]
    public async Task DispatchPendingAsync_ExtractChapterContinuityFacts_DelegatesToFactProcessorAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "extract_chapter_continuity_facts",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: "{}"));
        var processor = new RecordingChapterFactOutboxProcessor();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            chapterFactProcessor: processor);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Same(outbox, processor.ProcessedEvent);
    }

    [Fact]
    public async Task DispatchPendingAsync_FinalizeChapterCommitMetadata_DelegatesToFinalizerAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "finalize_chapter_commit_metadata",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: JsonSerializer.Serialize(new ChapterCommitPostCommitPayload
            {
                RuntimeRunId = "run-1",
                UserId = "user-1",
                ProjectId = "project-1",
                TargetChapterId = "project-1-chapter-001",
                Message = "章节已通过硬门禁并提交成稿。"
            })));
        var finalizer = new RecordingChapterCommitPostCommitFinalizer();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            chapterCommitFinalizer: finalizer);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Same(outbox, finalizer.ProcessedEvent);
    }

    [Fact]
    public async Task DispatchPendingAsync_ChapterFactOutboxPersistsStoryBibleSnapshotAndFeedsNextChapterContext()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var dbRoot = new InMemoryDatabaseRoot();
        await using var db = CreateDb(dbName, dbRoot);
        await SeedProjectAndChapterAsync(db);
        var contentDocuments = new ContentDocumentService(db);
        var document = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-001",
            "chapter_body",
            "第一章 黑雨邮徽",
            "第一章 黑雨邮徽\n沈砚在废弃维修站拿到银蓝邮徽，邮徽只能识别旧邮路。",
            CancellationToken.None);
        var truthStore = new ProductionTruthStore(db);
        var eventWriter = new ProductionEventWriter(truthStore);
        var version = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-001",
            ContentDocumentId: document.Id,
            Title: "第一章 黑雨邮徽",
            WordCount: 34,
            Status: "committed",
            RuntimeRunId: "run-chapter-001",
            PackageId: "pkg-chapter-001",
            GateReportJson: "{\"status\":\"validated\"}",
            AgentReviewJson: "{\"decision\":\"commit\"}"));
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-001",
            EventType: "extract_chapter_continuity_facts",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: JsonSerializer.Serialize(new
            {
                run = new NovelAgentRun
                {
                    RunId = "run-chapter-001",
                    TargetChapterId = "project-1-chapter-001"
                },
                contextPackage = new ChapterContextPackageSummary
                {
                    ChapterId = "project-1-chapter-001",
                    PackageId = "pkg-chapter-001"
                },
                committedContent = "沈砚在废弃维修站拿到银蓝邮徽，邮徽只能识别旧邮路。"
            })));
        using var services = BuildStoryBibleServices(dbName, dbRoot);
        var processor = new ChapterFactOutboxProcessor(
            new FixedChapterFactWriter(),
            new NoopWritingModelCompletionService(),
            new StoryBibleChapterContinuityFactPersister(services.GetRequiredService<IServiceScopeFactory>()),
            new ChapterFactSnapshotUpdater(db, truthStore, eventWriter),
            NullLogger<ChapterFactOutboxProcessor>.Instance);
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            eventWriter,
            processor);

        var dispatched = await dispatcher.DispatchPendingAsync();
        var context = await new ChapterContextEnrichmentService(
                db,
                new EmptyKnowledgeService(),
                new ProjectKnowledgeBindingQueryService(db),
                NullLogger<ChapterContextEnrichmentService>.Instance)
            .BuildAsync(new ChapterContextEnrichmentRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                SessionId: "session-1",
                RunId: "run-chapter-002",
                Query: "第二章 黑雨异兽围攻 银蓝邮徽",
                Package: new ChapterContextPackageSummary { ChapterId = "project-1-chapter-002" }));

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var snapshot = await db.ProjectFactSnapshots.SingleAsync(s => s.ChapterVersionId == version.Id);
        var storyBibleJson = await new ContentDocumentService(db).GetTextAsync(
            "user-1",
            "project-1",
            "story_bible",
            "project-1",
            "aggregate_json");
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("chapter_fact_extraction", snapshot.Source);
        Assert.Contains("沈砚", snapshot.SnapshotJson);
        Assert.Contains("废弃维修站外黑雨异兽正在围拢", snapshot.SnapshotJson);
        Assert.Contains("沈砚", storyBibleJson);
        Assert.Contains("银蓝邮徽", storyBibleJson);
        Assert.Contains(context.PreviousSummaries, summary => summary.Contains("第一章 黑雨邮徽", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("主角：沈砚", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("身份：旧邮局幸存投递员", StringComparison.Ordinal));
        Assert.Contains(context.CharacterStates, state => state.Contains("右手被邮徽灼伤", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("当前位置：废弃维修站", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("系统状态：银蓝邮徽只能识别旧邮路，不能主动攻击", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("装备状态：银蓝邮徽贴在右掌，处于发烫警示状态", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("结尾状态：废弃维修站外黑雨异兽正在围拢", StringComparison.Ordinal));
        Assert.Contains(context.HardFacts, fact => fact.Contains("下一章必须承接：黑雨异兽围攻维修站", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenOutboxIsStaleProcessing_RecoversAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "extract_chapter_continuity_facts",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: "{}"));
        outbox.Status = "processing";
        outbox.Attempts = 2;
        outbox.LastError = "previous worker stopped";
        outbox.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        outbox.UpdatedAt = DateTime.UtcNow.AddMinutes(-20);
        await db.SaveChangesAsync();

        var processor = new RecordingChapterFactOutboxProcessor();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            chapterFactProcessor: processor);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Null(completed.LastError);
        Assert.Null(completed.NextAttemptAt);
        Assert.Same(outbox, processor.ProcessedEvent);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenChapterFactOutboxFails_AppendsPostCommitFailureEvent()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "extract_chapter_continuity_facts",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: "{}"));
        var processor = new RecordingChapterFactOutboxProcessor
        {
            Error = new InvalidOperationException("facts writer timeout")
        };
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            new ProductionEventWriter(truthStore),
            processor);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var failed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var events = await db.ProductionEvents
            .Where(e => e.ArtifactId == outbox.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
        Assert.Equal(0, dispatched);
        Assert.Equal("retryable_failed", failed.Status);
        Assert.Collection(events,
            started =>
            {
                Assert.Equal("outbox_processing", started.EventType);
                Assert.Equal("post_commit_facts", started.Stage);
            },
            failure =>
            {
                Assert.Equal("outbox_failed", failure.EventType);
                Assert.Equal("post_commit_facts", failure.Stage);
                Assert.Equal("retryable_failed", failure.Status);
                Assert.Contains("extract_chapter_continuity_facts", failure.DataJson);
                Assert.Contains("facts writer timeout", failure.DataJson);
            });
    }

    [Fact]
    public async Task DispatchPendingAsync_IndexChapterContent_AppendsVisibleProductionEvents()
    {
        await using var db = CreateDb();
        var (_, outbox) = await SeedChapterVersionOutboxAsync(db);
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            new ProductionEventWriter(new ProductionTruthStore(db)));

        var dispatched = await dispatcher.DispatchPendingAsync();

        var events = await db.ProductionEvents
            .Where(e => e.RuntimeRunId == "run-1" && e.ArtifactId == outbox.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        Assert.Equal(1, dispatched);
        Assert.Collection(events,
            started =>
            {
                Assert.Equal("outbox_processing", started.EventType);
                Assert.Equal("index_outbox", started.Stage);
                Assert.Equal("running", started.Status);
                Assert.Equal("project-1-chapter-001", started.ChapterId);
                Assert.Equal("pkg-1", started.PackageId);
                Assert.Equal("outbox_event", started.ArtifactType);
                Assert.Contains("index_chapter_content", started.DataJson);
            },
            completed =>
            {
                Assert.Equal("outbox_completed", completed.EventType);
                Assert.Equal("index_outbox", completed.Stage);
                Assert.Equal("completed", completed.Status);
                Assert.Equal("project-1-chapter-001", completed.ChapterId);
                Assert.Equal("pkg-1", completed.PackageId);
                Assert.Equal("outbox_event", completed.ArtifactType);
                Assert.Contains("index_chapter_content", completed.DataJson);
            });
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenQdrantFails_MarksOutboxRetryableAndVectorPointFailed()
    {
        await using var db = CreateDb();
        var (version, outbox) = await SeedChapterVersionOutboxAsync(db);
        var vectorStore = new RecordingVectorStore { ThrowOnUpsert = true };
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var point = await db.ContentVectorPoints.SingleAsync(p => p.DocumentId == version.ContentDocumentId);
        var failed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");

        Assert.Equal(0, dispatched);
        Assert.Equal("retryable_failed", failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.NotNull(failed.NextAttemptAt);
        Assert.Contains("Qdrant timeout", failed.LastError);
        Assert.Equal("failed", point.IndexStatus);
        Assert.Contains("Qdrant timeout", point.ErrorMessage);
        Assert.Equal("committed", chapter.Status);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenQdrantFails_AppendsVisibleFailureEventWithoutBlockingCommittedChapter()
    {
        await using var db = CreateDb();
        var (_, outbox) = await SeedChapterVersionOutboxAsync(db);
        var vectorStore = new RecordingVectorStore { ThrowOnUpsert = true };
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            new ProductionEventWriter(new ProductionTruthStore(db)));

        var dispatched = await dispatcher.DispatchPendingAsync();

        var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
        var events = await db.ProductionEvents
            .Where(e => e.RuntimeRunId == "run-1" && e.ArtifactId == outbox.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        Assert.Equal(0, dispatched);
        Assert.Equal("committed", chapter.Status);
        Assert.Collection(events,
            started =>
            {
                Assert.Equal("outbox_processing", started.EventType);
                Assert.Equal("running", started.Status);
            },
            failed =>
            {
                Assert.Equal("outbox_failed", failed.EventType);
                Assert.Equal("retryable_failed", failed.Status);
                Assert.Contains("Qdrant timeout", failed.DataJson);
                Assert.Contains("index_chapter_content", failed.DataJson);
            });
    }

    [Fact]
    public async Task DispatchPendingAsync_DeleteChapterContent_RemovesChapterVectorsAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "delete_chapter_content",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-001",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "chapter") &&
            Equals(filter["source_id"], "project-1-chapter-001"));
    }

    [Fact]
    public async Task DispatchPendingAsync_DeleteProjectContent_RemovesProjectVectorsAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "delete_project_content",
            AggregateType: "project",
            AggregateId: "project-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1"));
    }

    [Fact]
    public async Task DispatchPendingAsync_IndexMaterialContent_DelegatesToMaterialVectorizationAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        db.Materials.Add(new Material
        {
            Id = "material-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "素材",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_material_content",
            AggregateType: "material",
            AggregateId: "material-1",
            PayloadJson: "{}"));
        var indexing = new RecordingMaterialVectorIndexingService();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new RecordingVectorStore(),
            new FixedEmbeddingService(),
            indexing,
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(new[] { "material-1:user-1" }, indexing.Calls);
    }

    [Fact]
    public async Task DispatchPendingAsync_DeleteMaterialContent_RemovesMaterialVectorsAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "delete_material_content",
            AggregateType: "material",
            AggregateId: "material-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "material") &&
            Equals(filter["source_id"], "material-1"));
    }

    [Fact]
    public async Task DispatchPendingAsync_IndexKnowledgeContent_UpsertsKnowledgeVectorAndStoresVectorId()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "HardFact",
            Title = "邮徽边界",
            Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_knowledge_content",
            AggregateType: "knowledge",
            AggregateId: "knowledge-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var knowledge = await db.KnowledgeBases.SingleAsync(k => k.Id == "knowledge-1");
        var vector = Assert.Single(vectorStore.Upserted);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.True(Guid.TryParse(knowledge.VectorId, out _));
        Assert.Equal(knowledge.VectorId, vector.Id);
        Assert.Equal("knowledge", vector.SourceType);
        Assert.Equal("knowledge-1", vector.SourceId);
        Assert.Equal("银蓝邮徽只能识别旧邮路，不能攻击。", vector.Content);
        Assert.NotNull(vector.Metadata);
        Assert.Equal(knowledge.Id, vector.Metadata["version_id"]);
    }

    [Fact]
    public async Task DispatchPendingAsync_DeleteKnowledgeContent_RemovesKnowledgeVectorsAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "delete_knowledge_content",
            AggregateType: "knowledge",
            AggregateId: "knowledge-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "knowledge") &&
            Equals(filter["source_id"], "knowledge-1"));
    }

    [Fact]
    public async Task DispatchPendingAsync_IndexStoryBibleCanon_IndexesOnlyCanonLedgerEntries()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var storyBible = new StoryBibleDocument
        {
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "canon-boundary",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "邮徽能力边界",
                    Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
                    Rationale = "KnowledgeId=knowledge-boundary"
                },
                new CanonLedgerEntry
                {
                    Id = "canon-conflict",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Conflict,
                    Title = "冲突设定",
                    Content = "银蓝邮徽可以释放蓝焰攻击。",
                    Rationale = "KnowledgeId=knowledge-blue-flame"
                }
            }
        };
        await new ContentDocumentService(db).SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "story_bible",
            "project-1",
            "aggregate_json",
            "Story Bible",
            JsonSerializer.Serialize(storyBible),
            CancellationToken.None);
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_story_bible_canon",
            AggregateType: "story_bible",
            AggregateId: "project-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var vector = Assert.Single(vectorStore.Upserted);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("story_bible_canon", vector.SourceType);
        Assert.Equal("canon-boundary", vector.SourceId);
        Assert.Equal("project-1", vector.ProjectId);
        Assert.Contains("银蓝邮徽只能识别旧邮路", vector.Content);
        Assert.NotNull(vector.Metadata);
        Assert.Equal("Constraint", vector.Metadata["canon_type"]);
        Assert.Equal("Canon", vector.Metadata["canon_status"]);
        Assert.Equal("邮徽能力边界", vector.Metadata["title"]);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "story_bible_canon"));
    }

    [Fact]
    public async Task DispatchPendingAsync_IndexMemoryContent_UpsertsMemoryVectorAndCompletesOutbox()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        await SeedProjectAndChapterAsync(db);
        var updatedAt = new DateTime(2026, 6, 21, 8, 30, 0, DateTimeKind.Utc);
        db.AgentMemories.Add(new AgentMemory
        {
            Id = "memory-1",
            UserId = "user-1",
            ProjectId = "project-1",
            MemoryType = "project.long_term_goal",
            MemoryKey = "long_term_goal",
            Content = "\"写一部旧邮路末世升级小说\"",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();
        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_memory_content",
            AggregateType: "memory",
            AggregateId: "memory-1",
            PayloadJson: "{}"));
        var vectorStore = new RecordingVectorStore();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new RecordingMaterialVectorIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingAsync();

        var completed = await db.OutboxEvents.SingleAsync(e => e.Id == outbox.Id);
        var vector = Assert.Single(vectorStore.Upserted);
        Assert.Equal(1, dispatched);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("memory", vector.SourceType);
        Assert.Equal("project.long_term_goal", vector.SourceId);
        Assert.True(Guid.TryParse(vector.Id, out _));
        Assert.Equal("写一部旧邮路末世升级小说", vector.Content);
        Assert.Equal("project-1", vector.ProjectId);
        Assert.NotNull(vector.Metadata);
        Assert.Equal($"memory-1:{updatedAt.Ticks}", vector.Metadata["version_id"]);
        Assert.Contains(vectorStore.DeletedFilters, filter =>
            Equals(filter["project_id"], "project-1") &&
            Equals(filter["source_type"], "memory") &&
            Equals(filter["source_id"], "project.long_term_goal"));
    }

    private static NovelAgentDbContext CreateDb(
        string? databaseName = null,
        InMemoryDatabaseRoot? databaseRoot = null)
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"), databaseRoot)
            .Options;

        return new NovelAgentDbContext(options);
    }

    private static async Task<(ChapterVersion Version, OutboxEvent Outbox)> SeedChapterVersionOutboxAsync(
        NovelAgentDbContext db)
    {
        await SeedProjectAndChapterAsync(db);

        var contentDocuments = new ContentDocumentService(db);
        var document = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-001",
            "chapter_body",
            "第一章 旧邮徽",
            "第一章 旧邮徽\n陈默在黑雨里握住银蓝邮徽。",
            CancellationToken.None);

        var truthStore = new ProductionTruthStore(db);
        var version = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-001",
            ContentDocumentId: document.Id,
            Title: "第一章 旧邮徽",
            WordCount: 24,
            Status: "committed",
            RuntimeRunId: "run-1",
            PackageId: "pkg-1",
            GateReportJson: "{\"status\":\"validated\"}",
            AgentReviewJson: "{\"decision\":\"commit\"}"));

        var outbox = await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "index_chapter_content",
            AggregateType: "chapter_version",
            AggregateId: version.Id,
            PayloadJson: "{}"));

        return (version, outbox);
    }

    private static async Task SeedProjectAndChapterAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author",
            IsActive = true
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "旧邮路",
            Genre = "末世",
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-001",
            ProjectId = "project-1",
            Title = "第一章 旧邮徽",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
    }

    private sealed class RecordingMaterialVectorIndexingService : IOutboxMaterialVectorIndexingService
    {
        public List<string> Calls { get; } = new();

        public Task IndexMaterialAsync(string materialId, string userId, CancellationToken ct = default)
        {
            Calls.Add($"{materialId}:{userId}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingChapterFactOutboxProcessor : IChapterFactOutboxProcessor
    {
        public Exception? Error { get; init; }
        public OutboxEvent? ProcessedEvent { get; private set; }

        public Task ProcessAsync(OutboxEvent evt, CancellationToken ct = default)
        {
            if (Error != null)
                throw Error;

            ProcessedEvent = evt;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingChapterCommitPostCommitFinalizer : IChapterCommitPostCommitFinalizer
    {
        public OutboxEvent? ProcessedEvent { get; private set; }

        public Task FinalizeAsync(
            ChapterCommitPostCommitPayload payload,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessOutboxAsync(
            OutboxEvent evt,
            CancellationToken cancellationToken = default)
        {
            ProcessedEvent = evt;
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildStoryBibleServices(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        return services.BuildServiceProvider();
    }

    private sealed class FixedChapterFactWriter : IChapterFactWriter
    {
        public async Task<ChapterFactWriteResult> ExtractAndPersistAsync(
            ChapterFactWriteRequest request,
            CancellationToken ct = default)
        {
            var facts = new ChapterContinuityFacts
            {
                ChapterId = "project-1-chapter-001",
                ChapterTitle = "第一章 黑雨邮徽",
                ProtagonistName = "沈砚",
                ProtagonistIdentity = "旧邮局幸存投递员",
                ProtagonistStatus = "右手被邮徽灼伤，但仍能行动",
                CurrentLocation = "废弃维修站",
                SystemState = "银蓝邮徽只能识别旧邮路，不能主动攻击",
                EquipmentState = "银蓝邮徽贴在右掌，处于发烫警示状态",
                KeyEvents =
                {
                    "沈砚获得银蓝邮徽",
                    "邮徽第一次识别出维修站后的旧邮路"
                },
                EndingState = "废弃维修站外黑雨异兽正在围拢",
                NextChapterMustCarry =
                {
                    "黑雨异兽围攻维修站",
                    "沈砚不能把银蓝邮徽当攻击武器"
                },
                SourceRunId = request.Run.RunId
            };
            var commit = request.PersistAsync == null
                ? new StoryBibleCommitResult { Success = true, Message = "ok" }
                : await request.PersistAsync(facts, ct);

            return new ChapterFactWriteResult
            {
                Success = commit.Success,
                Message = commit.Message,
                Facts = facts,
                CommitResult = commit
            };
        }
    }

    private sealed class NoopWritingModelCompletionService : IWritingModelCompletionService
    {
        public Task<string> CompleteAsync(
            string userId,
            string system,
            string user,
            CancellationToken ct = default) =>
            Task.FromResult("{}");
    }

    private sealed class EmptyKnowledgeService : IKnowledgeService
    {
        public Task<KnowledgeResponse> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(CreateExtractedKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<List<KnowledgeDirectoryResponse>> ListKnowledgeDirectoriesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<KnowledgeDirectoryResponse> CreateKnowledgeDirectoryAsync(CreateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<KnowledgeDirectoryResponse> UpdateKnowledgeDirectoryAsync(string key, UpdateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteKnowledgeDirectoryAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<KnowledgeResponse> UpdateKnowledgeAsync(string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken ct = default) =>
            Task.FromResult(new List<KnowledgeSearchResult>());
        public Task IncrementUsageAsync(string knowledgeId, string projectId, string? sessionId = null, string? runId = null, string? idempotencyKey = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopDistributedCacheService : IDistributedCacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
            Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class =>
            Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class NoopMemoryCacheService : IMemoryCacheService
    {
        public Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            factory().ContinueWith(task => (T?)task.Result, cancellationToken);
        public T? Get<T>(string key) => default;
        public void Set<T>(string key, T value, TimeSpan expiration) { }
        public void Remove(string key) { }
        public void RemoveByPrefix(string keyPrefix) { }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public bool ThrowOnUpsert { get; set; }
        public List<VectorData> Upserted { get; } = new();
        public List<Dictionary<string, object>> DeletedFilters { get; } = new();

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());

        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
        {
            DeletedFilters.Add(new Dictionary<string, object>(filters));
            return Task.CompletedTask;
        }

        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            if (ThrowOnUpsert)
                throw new InvalidOperationException("Qdrant timeout");

            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }
    }
}
