using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using Xunit;

namespace Tests.Unit.Services.Rag;

public sealed class LegacyVectorSourceRebuilderTests
{
    [Fact]
    public async Task RebuildUserAsync_RestoresMaterialKnowledgeLatestMemoryAndStoryBibleCanon()
    {
        await using var db = new NovelAgentDbContext(new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        await SeedAsync(db);
        var vectors = new RecordingVectorStore();
        var materials = new RecordingMaterialIndexer();
        var rebuilder = new LegacyVectorSourceRebuilder(
            db,
            vectors,
            new StubEmbeddingService(),
            materials);

        var count = await rebuilder.RebuildUserAsync("user-1");

        Assert.Equal(5, count);
        Assert.Equal(["material-1"], materials.MaterialIds);
        Assert.Equal(
            new[] { "knowledge", "memory", "story_bible_canon" },
            vectors.Upserted.Select(vector => vector.SourceType).OrderBy(value => value).ToArray());
        Assert.Single(vectors.Upserted.Where(vector => vector.SourceType == "memory"));
        Assert.Contains(vectors.Upserted, vector =>
            vector.SourceType == "memory" && vector.Content == "最新长期目标");
        Assert.All(vectors.Upserted, vector => Assert.Equal("user-1", vector.UserId));
    }

    private static async Task SeedAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user-1",
            Email = "user-1@example.test",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "project-1"
        });
        db.Materials.Add(new Material
        {
            Id = "material-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "素材",
            VectorChunkCount = 2
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "manual-knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "WritingMethod",
            Title = "谈判技巧",
            Content = "礼貌措辞隐藏威胁"
        });
        db.AgentMemories.AddRange(
            new AgentMemory
            {
                Id = "memory-old",
                UserId = "user-1",
                ProjectId = "project-1",
                MemoryType = "long_term_goal",
                Content = "旧长期目标",
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new AgentMemory
            {
                Id = "memory-new",
                UserId = "user-1",
                ProjectId = "project-1",
                MemoryType = "long_term_goal",
                Content = JsonSerializer.Serialize("最新长期目标"),
                UpdatedAt = DateTime.UtcNow
            });
        var storyBible = new StoryBibleDocument
        {
            CanonLedger =
            [
                new CanonLedgerEntry
                {
                    Id = "canon-1",
                    Title = "代价规则",
                    Content = "每次施法都消耗记忆",
                    Status = CanonLedgerEntryStatus.Canon,
                    Type = CanonLedgerEntryType.WorldRule
                }
            ]
        };
        var json = JsonSerializer.Serialize(storyBible, JsonHelper.CnDefault);
        db.ContentDocuments.Add(new ContentDocument
        {
            Id = "story-bible-document-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "story_bible",
            SourceId = "project-1",
            DocumentRole = "aggregate_json",
            Title = "Story Bible",
            ContentHash = "story-hash",
            Status = "active"
        });
        db.ContentChunks.Add(new ContentChunk
        {
            Id = "story-bible-chunk-1",
            DocumentId = "story-bible-document-1",
            ChunkIndex = 0,
            ChunkText = json,
            CharEnd = json.Length,
            ContentHash = "story-chunk-hash"
        });
        await db.SaveChangesAsync();
    }

    private sealed class RecordingMaterialIndexer : IOutboxMaterialVectorIndexingService
    {
        public List<string> MaterialIds { get; } = [];
        public Task IndexMaterialAsync(string materialId, string userId, CancellationToken ct = default)
        {
            MaterialIds.Add(materialId);
            return Task.CompletedTask;
        }
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
        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
    }
}
