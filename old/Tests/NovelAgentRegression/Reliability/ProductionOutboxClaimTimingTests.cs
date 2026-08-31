using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class ProductionOutboxClaimTimingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _workerConnectionString = string.Empty;

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
        _workerConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DispatchPendingAsync_ClaimsNextPostgresEventOnlyWhenItsHandlerCanStart()
    {
        await SeedAsync();
        await using var db = CreateDb();
        var processor = new BlockingChapterFactProcessor();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        var dispatcher = new ProductionOutboxDispatcher(
            db,
            new NoOpVectorStore(),
            new FixedEmbeddingService(),
            new NoOpMaterialIndexingService(),
            NullLogger<ProductionOutboxDispatcher>.Instance,
            chapterFactProcessor: processor,
            backgroundUsers: new BackgroundUserContext(),
            claimConnections: new BackgroundClaimConnectionFactory(configuration),
            processingLeaseTimeout: TimeSpan.FromSeconds(30));

        var dispatchTask = dispatcher.DispatchPendingAsync(2);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var observer = CreateDb())
        {
            Assert.Equal(1, await observer.OutboxEvents.CountAsync(item => item.Status == "processing"));
            Assert.Equal(1, await observer.OutboxEvents.CountAsync(item => item.Status == "pending"));
        }

        processor.Release();
        Assert.Equal(2, await dispatchTask);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            Id = "user-outbox-claim",
            Username = "outbox-author",
            Email = "outbox-author@example.com",
            PasswordHash = "hash",
            Role = "author",
            IsActive = true,
            CreatedAt = now
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-outbox-claim",
            UserId = "user-outbox-claim",
            Title = "即时领取测试",
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-outbox-claim",
            ProjectId = "project-outbox-claim",
            Title = "第一章",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        });
        for (var index = 0; index < 2; index++)
        {
            db.OutboxEvents.Add(new OutboxEvent
            {
                Id = $"outbox-claim-{index}",
                UserId = "user-outbox-claim",
                ProjectId = "project-outbox-claim",
                EventType = "extract_chapter_continuity_facts",
                AggregateType = "chapter",
                AggregateId = "chapter-outbox-claim",
                IdempotencyKey = $"outbox-claim-key-{index}",
                Status = "pending",
                CreatedAt = now.AddSeconds(index),
                UpdatedAt = now.AddSeconds(index)
            });
        }
        await db.SaveChangesAsync();
    }

    private PostgresNovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private sealed class BlockingChapterFactProcessor : IChapterFactOutboxProcessor
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started => _started;

        public async Task ProcessAsync(OutboxEvent evt, CancellationToken ct = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(ct);
        }

        public void Release() => _release.TrySetResult();
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

    private sealed class NoOpMaterialIndexingService : IOutboxMaterialVectorIndexingService
    {
        public Task IndexMaterialAsync(string materialId, string userId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpVectorStore : IVectorStore
    {
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }
}
