using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Rag;

public sealed class MultiScaleIndexTests
{
    [Fact]
    public async Task IndexDocumentAsync_IndexesFiveScalesWithCompleteVersionedMetadataAndNoBodyPayload()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "user-1", "project-1", "blob-1");
        var vectors = new RecordingVectorStore();
        var indexer = CreateIndexer(db, vectors);

        await indexer.IndexDocumentAsync("user-1", "blob-1");

        Assert.Equal(
            new[] { "knowledge_chunk", "knowledge_document", "knowledge_entry", "knowledge_section", "style_profile" },
            vectors.Upserted.Select(vector => vector.SourceType).OrderBy(value => value).ToArray());
        Assert.All(vectors.Upserted, vector =>
        {
            Assert.Equal("user-1", vector.UserId);
            Assert.Equal("project-1", vector.ProjectId);
            Assert.Null(vector.Content);
            Assert.Equal("", vector.Metadata!["branch_id"]);
            Assert.Equal(5L, vector.Metadata["knowledge_version"]);
            Assert.False(string.IsNullOrWhiteSpace((string)vector.Metadata["content_hash"]));
            Assert.Equal("bge-small-zh-v1.5", vector.Metadata["embedding_version"]);
            Assert.Equal("blob-1", vector.Metadata["document_blob_id"]);
        });
        var records = await db.VectorIndexRecords.ToListAsync();
        Assert.Equal(5, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal("completed", record.Status);
            Assert.Equal("bge-small-zh-v1.5", record.EmbeddingVersion);
            Assert.True(Guid.TryParse(record.QdrantPointId, out _));
        });
    }

    [Fact]
    public async Task RebuildUserAsync_ClearsCollectionAndRecreatesOnlyThatUsersPointsFromPostgres()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "user-1", "project-1", "blob-1");
        await SeedAsync(db, "user-2", "project-2", "blob-2");
        var vectors = new RecordingVectorStore();
        var indexer = CreateIndexer(db, vectors);
        var storyIndexer = CreateStoryIndexer(db, vectors);
        var rebuilder = new VectorIndexRebuilder(db, vectors, indexer, storyIndexer, new EmptyLegacyRebuilder());

        var count = await rebuilder.RebuildUserAsync("user-1");

        Assert.Equal(8, count);
        Assert.Equal(new[] { "user-1" }, vectors.DeletedUsers);
        Assert.All(vectors.Upserted, vector => Assert.Equal("user-1", vector.UserId));
        Assert.Equal(
            new[]
            {
                "canon_change", "chapter", "continuity_summary", "knowledge_chunk",
                "knowledge_document", "knowledge_entry", "knowledge_section", "style_profile"
            },
            vectors.Upserted.Select(vector => vector.SourceType).OrderBy(value => value).ToArray());
        Assert.Equal(8, await db.VectorIndexRecords.CountAsync(record => record.UserId == "user-1"));
        Assert.Equal(0, await db.VectorIndexRecords.CountAsync(record => record.UserId == "user-2"));
    }

    private static MultiScaleVectorIndexer CreateIndexer(NovelAgentDbContext db, IVectorStore vectors)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Model"] = "bge-small-zh-v1.5"
            })
            .Build();
        return new MultiScaleVectorIndexer(
            db,
            vectors,
            new StubEmbeddingService(),
            configuration,
            NullLogger<MultiScaleVectorIndexer>.Instance);
    }

    private static AuthoritativeStoryVectorIndexer CreateStoryIndexer(NovelAgentDbContext db, IVectorStore vectors)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Model"] = "bge-small-zh-v1.5"
            })
            .Build();
        return new AuthoritativeStoryVectorIndexer(
            db,
            vectors,
            new StubEmbeddingService(),
            configuration,
            NullLogger<AuthoritativeStoryVectorIndexer>.Instance);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedAsync(NovelAgentDbContext db, string userId, string projectId, string blobId)
    {
        db.Users.Add(new User
        {
            Id = userId,
            Username = userId,
            Email = $"{userId}@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject { Id = projectId, UserId = userId, Title = projectId });
        db.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
        {
            Id = blobId,
            UserId = userId,
            ProjectId = projectId,
            FileName = "source.txt",
            MimeType = "text/plain",
            Data = [1, 2, 3],
            ContentHash = $"hash-{blobId}",
            KnowledgeVersion = 5,
            Status = "processed"
        });
        db.KnowledgeSections.Add(new KnowledgeSection
        {
            Id = $"section-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            DocumentBlobId = blobId,
            KnowledgeVersion = 5,
            SectionIndex = 0,
            Title = "谈判",
            Summary = "克制对白",
            Text = "双方进行谈判",
            CharEnd = 6
        });
        db.KnowledgeChunks.Add(new KnowledgeChunk
        {
            Id = $"chunk-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            DocumentBlobId = blobId,
            SectionId = $"section-{blobId}",
            KnowledgeVersion = 5,
            ChunkIndex = 0,
            Text = "双方进行谈判",
            CharEnd = 6,
            ContentHash = $"chunk-hash-{blobId}"
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = $"logical-{blobId}",
            UserId = userId,
            SourceProjectId = projectId,
            EntryType = "WritingMethod",
            Title = "潜台词",
            Content = "威胁藏在礼貌措辞中"
        });
        db.KnowledgeEntries.Add(new KnowledgeEntry
        {
            Id = $"entry-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            LogicalKnowledgeId = $"logical-{blobId}",
            DocumentBlobId = blobId,
            KnowledgeVersion = 5,
            Version = 1,
            EntryType = "WritingMethod",
            Title = "潜台词",
            Content = "威胁藏在礼貌措辞中",
            Summary = "潜台词方法",
            Status = "active"
        });
        db.StyleProfiles.Add(new StyleProfile
        {
            Id = $"style-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            DocumentBlobId = blobId,
            KnowledgeVersion = 5,
            FeaturesJson = "{\"sentenceRhythm\":\"短句递进\"}",
            Status = "active"
        });
        db.ContentDocuments.Add(new ContentDocument
        {
            Id = $"chapter-document-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            SourceType = "chapter",
            SourceId = $"chapter-{blobId}",
            DocumentRole = "chapter_body",
            Title = "谈判章",
            ContentHash = $"chapter-document-hash-{blobId}",
            Version = 1,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ContentChunks.Add(new ContentChunk
        {
            Id = $"chapter-chunk-{blobId}",
            DocumentId = $"chapter-document-{blobId}",
            ChunkIndex = 0,
            ChunkText = "主角在雨夜完成谈判",
            CharEnd = 9,
            ContentHash = $"chapter-chunk-hash-{blobId}"
        });
        db.ContinuitySummaries.Add(new ContinuitySummary
        {
            Id = $"summary-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            ChapterId = $"chapter-{blobId}",
            ChapterVersionId = $"version-{blobId}",
            SummaryJson = "{\"promise\":\"谈判后必须付出代价\"}",
            Status = "committed"
        });
        db.CanonChanges.Add(new CanonChange
        {
            Id = $"change-{blobId}",
            UserId = userId,
            ProjectId = projectId,
            ChapterId = $"chapter-{blobId}",
            ChapterVersionId = $"version-{blobId}",
            ChangeType = "relationship",
            Subject = "主角与盟友",
            ChangeJson = "{\"state\":\"暂时结盟\"}",
            Status = "committed"
        });
        await db.SaveChangesAsync();
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

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorData> Upserted { get; } = [];
        public List<string> DeletedUsers { get; } = [];
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default)
        {
            DeletedUsers.Add(userId);
            return Task.CompletedTask;
        }
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class EmptyLegacyRebuilder : ILegacyVectorSourceRebuilder
    {
        public Task<int> RebuildUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
