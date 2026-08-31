using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Grpc.Core;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Rag;

public sealed class HybridRetrieverTests
{
    [Fact]
    public async Task RetrieveAsync_RoutesDenseQueryEmbeddingThroughGoalExecutionEnvelope()
    {
        await using var db = CreateDb();
        var embedding = new Mock<IMicroEmbeddingService>(MockBehavior.Strict);
        var envelope = new Mock<IGoalEmbeddingExecutionEnvelope>(MockBehavior.Strict);
        envelope.Setup(service => service.ExecuteAsync(
                "身份公开",
                EmbeddingMode.Query,
                It.IsAny<Func<CancellationToken, Task<float[]>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 0.1f, 0.2f, 0.3f });
        var retriever = new HybridRetriever(
            new StubPlanner(new RagQueryPlan([RagRoute.Knowledge], ["身份公开"], [], [], false)),
            new StubVectorStore([]),
            embedding.Object,
            envelope.Object,
            new StubFullTextRetriever([]),
            new StubEntityDependencyRetriever([]),
            new ReciprocalRankFusion(),
            new EvidenceBundleCompiler(db),
            NullLogger<HybridRetriever>.Instance);

        await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1", "project-1", "检查身份公开", ""));

        envelope.VerifyAll();
        embedding.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenUserCollectionDoesNotExist_ContinuesWithPostgresEvidence()
    {
        await using var db = CreateDb();
        await SeedChunksAsync(db);
        var retriever = new HybridRetriever(
            new StubPlanner(new RagQueryPlan([RagRoute.Knowledge], ["身份公开"], [], [], true)),
            new MissingCollectionVectorStore(),
            new StubEmbeddingService(),
            new PassThroughEmbeddingExecutionEnvelope(),
            new StubFullTextRetriever([
                new RetrievalCandidate("knowledge_entry", "entry-1", "fts", 1, 1, null)
            ]),
            new StubEntityDependencyRetriever([]),
            new ReciprocalRankFusion(),
            new EvidenceBundleCompiler(db),
            NullLogger<HybridRetriever>.Instance);

        var result = await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1", "project-1", "身份公开后发生了什么", ""));

        Assert.Contains(result.Items, item =>
            item.SourceId == "entry-1" && item.Content.Contains("议会谈判规则改变"));
    }

    [Fact]
    public async Task DefaultRagQueryPlanningModelClient_ParsesPromptSpecifiedStringRoutes()
    {
        var client = new DefaultRagQueryPlanningModelClient(new StubCompletionService("""
            {"routes":["Continuity","Knowledge"],"semanticQueries":["旧城区封锁"],"entityReferences":["旧城区"],"targetChapterIds":[],"requiresLongRangeRecall":true}
            """));

        var result = await client.PlanAsync(new RagQueryPlanningRequest(
            "user-1", "project-1", "调查旧城区", ""));

        Assert.Equal([RagRoute.Continuity, RagRoute.Knowledge], result.Routes);
    }

    [Fact]
    public async Task QueryPlanner_UsesConversationSemanticsFromModelWithoutCommandKeywordRouting()
    {
        var model = new Mock<IRagQueryPlanningModelClient>(MockBehavior.Strict);
        model.Setup(client => client.PlanAsync(
                It.Is<RagQueryPlanningRequest>(request => request.UserMessage == "继续"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryPlan(
                [RagRoute.Promise, RagRoute.Continuity],
                ["主角尚未兑现的归家承诺"],
                ["林岚"],
                ["chapter-27"],
                true));
        var planner = new QueryPlanner(model.Object);

        var result = await planner.PlanAsync(new RagQueryPlanningRequest(
            "user-1",
            "project-1",
            "继续",
            "上一轮正在讨论林岚是否已经兑现归家承诺。"));

        Assert.Equal(new[] { RagRoute.Promise, RagRoute.Continuity }, result.Routes);
        Assert.DoesNotContain(RagRoute.Knowledge, result.Routes);
        model.VerifyAll();
    }

    [Fact]
    public async Task RetrieveAsync_FusesChannelsButLoadsAuthoritativeTextAndNeighborsFromPostgres()
    {
        await using var db = CreateDb();
        await SeedChunksAsync(db);
        var planner = new StubPlanner(new RagQueryPlan(
            [RagRoute.Knowledge, RagRoute.Continuity],
            ["失踪王女身份公开后的谈判"],
            ["林岚"],
            [],
            true));
        var vectorStore = new StubVectorStore([
            new SearchResult
            {
                Id = "point-poisoned",
                Score = 0.95f,
                UserId = "user-1",
                ProjectId = "project-1",
                SourceType = "knowledge_chunk",
                SourceId = "chunk-middle",
                Content = "Qdrant 中不可信且不应被采用的伪造正文",
                Metadata = new Dictionary<string, object> { ["document_blob_id"] = "blob-1" }
            }
        ]);
        var fts = new StubFullTextRetriever([
            new RetrievalCandidate("knowledge_chunk", "chunk-middle", "fts", 1, 0.8, null)
        ]);
        var dependencies = new StubEntityDependencyRetriever([
            new RetrievalCandidate("knowledge_entry", "entry-1", "entity", 1, 1, null)
        ]);
        var compiler = new EvidenceBundleCompiler(db);
        var retriever = new HybridRetriever(
            planner,
            vectorStore,
            new StubEmbeddingService(),
            new PassThroughEmbeddingExecutionEnvelope(),
            fts,
            dependencies,
            new ReciprocalRankFusion(),
            compiler,
            NullLogger<HybridRetriever>.Instance);

        var bundle = await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1",
            "project-1",
            "继续检查林岚身份线",
            "上一轮讨论身份公开后的长期影响。",
            8));

        Assert.Contains(bundle.Items, item => item.SourceId == "chunk-middle" && item.Content == "数据库中的中段正文");
        Assert.Contains(bundle.Items, item => item.SourceId == "chunk-before" && item.ExpansionReason == "neighbor");
        Assert.Contains(bundle.Items, item => item.SourceId == "chunk-after" && item.ExpansionReason == "neighbor");
        Assert.Contains(bundle.Items, item => item.SourceId == "section-1" && item.ExpansionReason == "parent");
        Assert.Contains(bundle.Items, item => item.SourceId == "entry-1" && item.Content == "林岚公开失踪王女身份后，议会谈判规则改变。");
        Assert.DoesNotContain(bundle.Items, item => item.Content.Contains("Qdrant 中不可信", StringComparison.Ordinal));
        Assert.All(bundle.Items, item => Assert.Equal("user-1", item.UserId));
    }

    [Fact]
    public async Task EvidenceCompiler_DropsCrossUserSourceIdsEvenWhenQdrantReturnsThem()
    {
        await using var db = CreateDb();
        await SeedChunksAsync(db);
        db.KnowledgeChunks.Add(new KnowledgeChunk
        {
            Id = "other-user-chunk",
            UserId = "user-2",
            ProjectId = "project-2",
            DocumentBlobId = "blob-2",
            SectionId = "section-2",
            KnowledgeVersion = 1,
            ChunkIndex = 0,
            Text = "另一个用户的秘密正文",
            ContentHash = "other-hash"
        });
        await db.SaveChangesAsync();
        var compiler = new EvidenceBundleCompiler(db);

        var result = await compiler.CompileAsync(
            "user-1",
            "project-1",
            new RagQueryPlan([RagRoute.Knowledge], ["秘密"], [], [], false),
            [new FusedRetrievalCandidate("knowledge_chunk", "other-user-chunk", 1, ["dense"], null)]);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task RetrieveAsync_RejectsKnowledgeNewerThanFrozenSnapshotAcrossDenseAndAuthority()
    {
        await using var db = CreateDb();
        await SeedChunksAsync(db);
        var vectors = new RecordingVectorStore([
            new SearchResult
            {
                Id = "point-newer",
                Score = 1,
                UserId = "user-1",
                ProjectId = "project-1",
                SourceType = "knowledge_entry",
                SourceId = "entry-1",
                Metadata = new Dictionary<string, object> { ["knowledge_version"] = 4L }
            }
        ]);
        var retriever = new HybridRetriever(
            new StubPlanner(new RagQueryPlan([RagRoute.Knowledge], ["身份"], [], [], false)),
            vectors,
            new StubEmbeddingService(),
            new PassThroughEmbeddingExecutionEnvelope(),
            new StubFullTextRetriever([
                new RetrievalCandidate("knowledge_entry", "entry-1", "fts", 1, 1, null)
            ]),
            new StubEntityDependencyRetriever([]),
            new ReciprocalRankFusion(),
            new EvidenceBundleCompiler(db),
            NullLogger<HybridRetriever>.Instance);

        var bundle = await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1",
            "project-1",
            "检查身份",
            "",
            8,
            new RagSnapshotScope("canon-7", 3, DateTime.UtcNow)));

        Assert.DoesNotContain(bundle.Items, item => item.SourceId == "entry-1");
        var range = Assert.IsType<VectorNumericRange>(vectors.LastFilters!["knowledge_version"]);
        Assert.Equal(3, range.LessThanOrEqual);
    }

    [Fact]
    public async Task RetrieveAsync_FrozenStoryScopeFiltersDenseSourcesByCreationTime()
    {
        await using var db = CreateDb();
        var frozenAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        var vectors = new RecordingVectorStore([]);
        var retriever = new HybridRetriever(
            new StubPlanner(new RagQueryPlan([RagRoute.Continuity], ["旧承诺"], [], [], true)),
            vectors,
            new StubEmbeddingService(),
            new PassThroughEmbeddingExecutionEnvelope(),
            new StubFullTextRetriever([]),
            new StubEntityDependencyRetriever([]),
            new ReciprocalRankFusion(),
            new EvidenceBundleCompiler(db),
            NullLogger<HybridRetriever>.Instance);

        await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1",
            "project-1",
            "检查旧承诺",
            "",
            8,
            new RagSnapshotScope("canon-7", 3, frozenAt)));

        var expectedUnix = new DateTimeOffset(frozenAt).ToUnixTimeSeconds();
        foreach (var sourceType in new[] { "chapter", "continuity_summary", "canon_change" })
        {
            var filters = vectors.FiltersBySourceType[sourceType];
            var range = Assert.IsType<VectorNumericRange>(filters["created_at_unix"]);
            Assert.Equal(expectedUnix, range.LessThanOrEqual);
        }
    }

    [Fact]
    public void ReciprocalRankFusion_CountsEachSourceOnlyOncePerChannel()
    {
        var fusion = new ReciprocalRankFusion();

        var result = fusion.Fuse(
            [
                new RetrievalCandidate("chapter", "duplicated", "dense", 10, 0.9, null),
                new RetrievalCandidate("chapter", "duplicated", "dense", 11, 0.8, null),
                new RetrievalCandidate("chapter", "single", "dense", 2, 0.7, null)
            ],
            10);

        Assert.Equal("single", result[0].SourceId);
        Assert.Single(result.Single(item => item.SourceId == "duplicated").Channels);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedChunksAsync(NovelAgentDbContext db)
    {
        db.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
        {
            Id = "blob-1",
            UserId = "user-1",
            ProjectId = "project-1",
            FileName = "identity.txt",
            Data = [1],
            ContentHash = "blob-hash",
            KnowledgeVersion = 4,
            Status = "processed"
        });
        db.KnowledgeSections.Add(new KnowledgeSection
        {
            Id = "section-1",
            UserId = "user-1",
            ProjectId = "project-1",
            DocumentBlobId = "blob-1",
            KnowledgeVersion = 4,
            SectionIndex = 0,
            Title = "身份公开后的议会谈判",
            Summary = "身份变化重塑谈判关系",
            Text = "前段。数据库中的中段正文。后段。",
            CharEnd = 17
        });
        db.KnowledgeChunks.AddRange(
            new KnowledgeChunk
            {
                Id = "chunk-before",
                UserId = "user-1",
                ProjectId = "project-1",
                DocumentBlobId = "blob-1",
                SectionId = "section-1",
                KnowledgeVersion = 4,
                ChunkIndex = 0,
                Text = "数据库中的前段正文",
                ContentHash = "before-hash",
                NextChunkId = "chunk-middle"
            },
            new KnowledgeChunk
            {
                Id = "chunk-middle",
                UserId = "user-1",
                ProjectId = "project-1",
                DocumentBlobId = "blob-1",
                SectionId = "section-1",
                KnowledgeVersion = 4,
                ChunkIndex = 1,
                Text = "数据库中的中段正文",
                ContentHash = "middle-hash",
                PreviousChunkId = "chunk-before",
                NextChunkId = "chunk-after"
            },
            new KnowledgeChunk
            {
                Id = "chunk-after",
                UserId = "user-1",
                ProjectId = "project-1",
                DocumentBlobId = "blob-1",
                SectionId = "section-1",
                KnowledgeVersion = 4,
                ChunkIndex = 2,
                Text = "数据库中的后段正文",
                ContentHash = "after-hash",
                PreviousChunkId = "chunk-middle"
            });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "logical-1",
            UserId = "user-1",
            EntryType = "HardFact",
            Title = "林岚身份",
            Content = "林岚是失踪王女"
        });
        db.KnowledgeEntries.Add(new KnowledgeEntry
        {
            Id = "entry-1",
            UserId = "user-1",
            ProjectId = "project-1",
            LogicalKnowledgeId = "logical-1",
            DocumentBlobId = "blob-1",
            KnowledgeVersion = 4,
            Version = 1,
            EntryType = "HardFact",
            Title = "身份公开影响",
            Content = "林岚公开失踪王女身份后，议会谈判规则改变。",
            Status = "active"
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubPlanner(RagQueryPlan plan) : IQueryPlanner
    {
        public Task<RagQueryPlan> PlanAsync(RagQueryPlanningRequest request, CancellationToken cancellationToken = default) => Task.FromResult(plan);
    }

    private sealed class StubCompletionService(string result) : IWritingModelCompletionService
    {
        public Task<string> CompleteAsync(
            string userId,
            string system,
            string user,
            CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class StubFullTextRetriever(IReadOnlyList<RetrievalCandidate> candidates) : IPostgresFullTextRetriever
    {
        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string userId, string projectId, RagQueryPlan plan, int topK, RagSnapshotScope? snapshot = null, CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    private sealed class StubEntityDependencyRetriever(IReadOnlyList<RetrievalCandidate> candidates) : IEntityDependencyRetriever
    {
        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string userId, string projectId, RagQueryPlan plan, int topK, RagSnapshotScope? snapshot = null, CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    private sealed class StubEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;
        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) => Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) => Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());
        public void ReleaseSession() { }
        public bool IsModelReady() => true;
    }

    private sealed class PassThroughEmbeddingExecutionEnvelope : IGoalEmbeddingExecutionEnvelope
    {
        public Task<float[]> ExecuteAsync(
            string text,
            EmbeddingMode mode,
            Func<CancellationToken, Task<float[]>> providerCall,
            CancellationToken cancellationToken = default) => providerCall(cancellationToken);
    }

    private sealed class StubVectorStore(IReadOnlyList<SearchResult> results) : IVectorStore
    {
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(results.ToList());
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingVectorStore(IReadOnlyList<SearchResult> results) : IVectorStore
    {
        public Dictionary<string, object>? LastFilters { get; private set; }
        public Dictionary<string, Dictionary<string, object>> FiltersBySourceType { get; } = [];
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default)
        {
            LastFilters = filters;
            if (filters != null && filters.TryGetValue("source_type", out var sourceType))
                FiltersBySourceType[(string)sourceType] = new Dictionary<string, object>(filters);
            return Task.FromResult(results.ToList());
        }
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MissingCollectionVectorStore : IVectorStore
    {
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) =>
            Task.FromException<List<SearchResult>>(new RpcException(new Status(StatusCode.NotFound, "collection missing")));
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }
}
