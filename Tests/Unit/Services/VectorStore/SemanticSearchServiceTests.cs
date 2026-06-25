using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.VectorStore;

public sealed class SemanticSearchServiceTests
{
    [Fact]
    public async Task SearchStoryBibleCanonAsync_SearchesProjectScopedCanonVectors()
    {
        var vectorStore = new RecordingVectorStore(new SearchResult
        {
            Id = "point-1",
            Score = 0.91f,
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "story_bible_canon",
            SourceId = "canon-boundary",
            Content = "Story Bible Canon：邮徽能力边界：银蓝邮徽只能识别旧邮路，不能攻击。"
        });
        var service = new SemanticSearchService(
            vectorStore,
            new FixedEmbeddingService(),
            NullLogger<SemanticSearchService>.Instance);

        var results = await service.SearchStoryBibleCanonAsync(
            "user-1",
            "project-1",
            "银蓝邮徽 能力边界",
            topK: 4);

        var result = Assert.Single(results);
        Assert.Equal("story_bible_canon", result.EntityType);
        Assert.Equal("canon-boundary", result.EntityId);
        Assert.Contains("银蓝邮徽只能识别旧邮路", result.Content);
        Assert.Equal(0.91f, result.Score);
        Assert.NotNull(vectorStore.LastFilters);
        Assert.Equal("project-1", vectorStore.LastFilters["project_id"]);
        Assert.Equal("story_bible_canon", vectorStore.LastFilters["source_type"]);
        Assert.Equal(4, vectorStore.LastTopK);
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(
            string text,
            EmbeddingMode mode = EmbeddingMode.Passage,
            CancellationToken ct = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<float[][]> EncodeBatchAsync(
            IReadOnlyList<string> texts,
            EmbeddingMode mode = EmbeddingMode.Passage,
            CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        private readonly SearchResult _result;

        public RecordingVectorStore(SearchResult result)
        {
            _result = result;
        }

        public Dictionary<string, object>? LastFilters { get; private set; }
        public int LastTopK { get; private set; }

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<SearchResult>> SearchSimilarAsync(
            string userId,
            float[] queryVector,
            int topK = 10,
            Dictionary<string, object>? filters = null,
            CancellationToken ct = default)
        {
            LastFilters = filters == null ? null : new Dictionary<string, object>(filters);
            LastTopK = topK;
            return Task.FromResult(new List<SearchResult> { _result });
        }
    }
}
