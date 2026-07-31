using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Rag;

public sealed class LongRangeRecallTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RetrieveAsync_RecallsDistantChapterFromPostgresAndRejectsCrossUserVectorPayload()
    {
        await using var db = CreateDb();
        var planner = new StubPlanner(new RagQueryPlan(
            [RagRoute.Continuity, RagRoute.Promise],
            ["北塔钟声与归家承诺"],
            ["林岚"],
            ["chapter-29"],
            true));
        var retriever = new HybridRetriever(
            planner,
            new PoisonedVectorStore(),
            new StubEmbeddingService(),
            new PassThroughEmbeddingExecutionEnvelope(),
            new PostgresFullTextRetriever(db),
            new EntityDependencyRetriever(db),
            new ReciprocalRankFusion(),
            new EvidenceBundleCompiler(db),
            NullLogger<HybridRetriever>.Instance);

        var bundle = await retriever.RetrieveAsync(new RagRetrievalRequest(
            "user-1",
            "project-1",
            "继续",
            "上一轮正在检查第二十九章是否兑现林岚的归家承诺。",
            12));

        Assert.Contains(bundle.Items, item =>
            item.SourceType == "chapter" &&
            item.SourceId == "chapter-1" &&
            item.Content.Contains("北塔钟声响起时，林岚承诺一定回家。", StringComparison.Ordinal));
        Assert.DoesNotContain(bundle.Items, item => item.UserId == "user-2");
        Assert.DoesNotContain(bundle.Items, item => item.Content.Contains("跨用户秘密", StringComparison.Ordinal));
        Assert.DoesNotContain(bundle.Items, item => item.Content.Contains("Qdrant 伪造正文", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_CreatesPostgresFullTextAndTrigramIndexes()
    {
        await using var db = CreateDb();
        var indexes = await db.Database.SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND indexname LIKE 'ix_rag_%'")
            .ToListAsync();

        Assert.Contains("ix_rag_content_chunks_fts", indexes);
        Assert.Contains("ix_rag_knowledge_chunks_trgm", indexes);
    }

    private PostgresNovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private static async Task SeedAsync(PostgresNovelAgentDbContext db)
    {
        db.Users.AddRange(
            new User { Id = "user-1", Username = "author-1", Email = "author1@example.com", PasswordHash = "hash", Role = "author" },
            new User { Id = "user-2", Username = "author-2", Email = "author2@example.com", PasswordHash = "hash", Role = "author" });
        db.NovelProjects.AddRange(
            new NovelProject { Id = "project-1", UserId = "user-1", Title = "远距召回测试", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new NovelProject { Id = "project-2", UserId = "user-2", Title = "隔离测试", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ContentDocuments.AddRange(
            new ContentDocument
            {
                Id = "document-user-1-chapter-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SourceType = "chapter",
                SourceId = "chapter-1",
                DocumentRole = "chapter_body",
                Title = "第一章",
                ContentHash = "hash-user-1",
                Status = "active"
            },
            new ContentDocument
            {
                Id = "document-user-2-secret",
                UserId = "user-2",
                ProjectId = "project-2",
                SourceType = "chapter",
                SourceId = "chapter-secret",
                DocumentRole = "chapter_body",
                Title = "秘密章",
                ContentHash = "hash-user-2",
                Status = "active"
            });
        db.ContentChunks.AddRange(
            new ContentChunk
            {
                Id = "content-user-1-chapter-1",
                DocumentId = "document-user-1-chapter-1",
                ChunkIndex = 0,
                ChunkText = "北塔钟声响起时，林岚承诺一定回家。这个承诺在二十八章后仍然有效。",
                ContentHash = "chunk-hash-user-1"
            },
            new ContentChunk
            {
                Id = "content-user-2-secret",
                DocumentId = "document-user-2-secret",
                ChunkIndex = 0,
                ChunkText = "跨用户秘密绝不能进入另一个作者的证据包。",
                ContentHash = "chunk-hash-user-2"
            });
        db.ChapterBlueprints.Add(new ChapterBlueprint
        {
            Id = "blueprint-29",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-29",
            ChapterIndex = 29,
            Title = "归途",
            Intent = "兑现归家承诺",
            DependencyChapterIdsJson = "[\"chapter-1\"]",
            CharactersJson = "[\"林岚\"]"
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubPlanner(RagQueryPlan plan) : IQueryPlanner
    {
        public Task<RagQueryPlan> PlanAsync(RagQueryPlanningRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(plan);
    }

    private sealed class StubEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;
        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());
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

    private sealed class PoisonedVectorStore : IVectorStore
    {
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default)
        {
            if (filters == null || !Equals(filters["source_type"], "chapter"))
                return Task.FromResult(new List<SearchResult>());
            return Task.FromResult(new List<SearchResult>
            {
                new()
                {
                    Id = "poison-cross-user",
                    Score = 1,
                    UserId = "user-2",
                    ProjectId = "project-2",
                    SourceType = "chapter",
                    SourceId = "chapter-secret",
                    ChunkIndex = 0,
                    Content = "Qdrant 伪造正文：跨用户秘密",
                    Metadata = new Dictionary<string, object> { ["content_document_id"] = "document-user-2-secret" }
                },
                new()
                {
                    Id = "valid-user-1",
                    Score = 0.9f,
                    UserId = "user-1",
                    ProjectId = "project-1",
                    SourceType = "chapter",
                    SourceId = "chapter-1",
                    ChunkIndex = 0,
                    Content = "Qdrant 伪造正文",
                    Metadata = new Dictionary<string, object> { ["content_document_id"] = "document-user-1-chapter-1" }
                }
            });
        }
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }
}
