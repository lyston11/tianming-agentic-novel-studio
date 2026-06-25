using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterContextEnrichmentServiceTests
{
    [Fact]
    public async Task BuildAsync_MergesBoundKnowledgeSearchHardFactsAndFactSnapshots()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var knowledgeService = new RecordingKnowledgeService(new KnowledgeSearchResult
        {
            Id = "knowledge-search-hardfact",
            Title = "邮徽限制",
            Content = "银蓝邮徽只能识别旧邮路，不能主动攻击。",
            EntryType = "HardFact",
            Score = 0.92f
        });
        IChapterContextEnrichmentService service = new ChapterContextEnrichmentService(
            db,
            knowledgeService,
            new ProjectKnowledgeBindingQueryService(db),
            NullLogger<ChapterContextEnrichmentService>.Instance);

        var result = await service.BuildAsync(new ChapterContextEnrichmentRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-chapter-002",
            Query: "第二章 怪物围攻 银蓝邮徽",
            Package: new ChapterContextPackageSummary { ChapterId = "chapter-002" }));

        Assert.Contains(result.KnowledgeBindings, binding => binding.KnowledgeId == "knowledge-bound-hardfact");
        Assert.Contains(result.HardFacts, fact => fact.Contains("邮徽限制", StringComparison.Ordinal));
        Assert.Contains(result.HardFacts, fact => fact.Contains("旧邮路必须付出记忆代价", StringComparison.Ordinal));
        Assert.Contains(result.HardFacts, fact => fact.Contains("下一章必须承接：陈默刚拿到银蓝邮徽", StringComparison.Ordinal));
        Assert.Contains(result.HardFacts, fact =>
            fact.Contains("上一章知识约束证据：银蓝邮徽能力边界", StringComparison.Ordinal) &&
            fact.Contains("已满足", StringComparison.Ordinal));
        Assert.Contains(result.HardFacts, fact =>
            fact.Contains("上一章知识分类规则：银蓝邮徽能力边界", StringComparison.Ordinal) &&
            fact.Contains("银蓝邮徽只能辨认旧邮路，不能攻击。", StringComparison.Ordinal));
        Assert.Contains(result.PreviousSummaries, summary => summary.Contains("chapter-001", StringComparison.Ordinal));
        Assert.Contains(result.CharacterStates, state => state.Contains("陈默右手灼伤", StringComparison.Ordinal));
        Assert.Contains(result.ActiveConflicts, conflict => conflict.Contains("维修站外黑潮异兽围拢", StringComparison.Ordinal));
        Assert.Contains(result.SourceWarnings, source => source.Contains("snapshot-1", StringComparison.Ordinal));
        Assert.Equal(("knowledge-search-hardfact", "project-1", "session-1", "run-chapter-002"), knowledgeService.LastUsage);
    }

    [Fact]
    public async Task BuildAsync_MergesStoryBibleCanonSemanticHitsIntoHardFacts()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var vectorStore = new RecordingVectorStore(new SearchResult
        {
            Id = "point-canon",
            Score = 0.94f,
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "story_bible_canon",
            SourceId = "canon-boundary",
            Content = "Story Bible Canon：邮徽能力边界：银蓝邮徽只能识别旧邮路，不能攻击。"
        });
        var semanticSearch = new SemanticSearchService(
            vectorStore,
            new FixedEmbeddingService(),
            NullLogger<SemanticSearchService>.Instance);
        IChapterContextEnrichmentService service = new ChapterContextEnrichmentService(
            db,
            new RecordingKnowledgeService(),
            new ProjectKnowledgeBindingQueryService(db),
            NullLogger<ChapterContextEnrichmentService>.Instance,
            semanticSearch);

        var result = await service.BuildAsync(new ChapterContextEnrichmentRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-chapter-002",
            Query: "第二章 银蓝邮徽 旧邮路",
            Package: new ChapterContextPackageSummary { ChapterId = "chapter-002" }));

        Assert.Contains(result.HardFacts, fact =>
            fact.Contains("Story Bible Canon", StringComparison.Ordinal) &&
            fact.Contains("银蓝邮徽只能识别旧邮路", StringComparison.Ordinal));
        Assert.NotNull(vectorStore.LastFilters);
        Assert.Equal("project-1", vectorStore.LastFilters["project_id"]);
        Assert.Equal("story_bible_canon", vectorStore.LastFilters["source_type"]);
    }

    [Fact]
    public async Task BuildAsync_ReturnsEmptyResultWithoutProjectContext()
    {
        await using var db = CreateDb();
        IChapterContextEnrichmentService service = new ChapterContextEnrichmentService(
            db,
            new RecordingKnowledgeService(),
            new ProjectKnowledgeBindingQueryService(db),
            NullLogger<ChapterContextEnrichmentService>.Instance);

        var result = await service.BuildAsync(new ChapterContextEnrichmentRequest(
            UserId: "",
            ProjectId: "",
            SessionId: "session-1",
            RunId: "run-1",
            Query: "anything",
            Package: new ChapterContextPackageSummary { ChapterId = "chapter-001" }));

        Assert.Empty(result.KnowledgeBindings);
        Assert.Empty(result.HardFacts);
        Assert.Empty(result.PreviousSummaries);
        Assert.Empty(result.CharacterStates);
        Assert.Empty(result.ActiveConflicts);
        Assert.Empty(result.SourceWarnings);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedProjectAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "上下文增强测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-bound-hardfact",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "旧邮路代价",
                Content = "旧邮路必须付出记忆代价。",
                Tags = "[\"世界规则\"]",
                Weight = 9,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new KnowledgeBase
            {
                Id = "knowledge-global-hardfact",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "黑雨规则",
                Content = "黑雨会放大异兽嗅觉。",
                Weight = 6,
                CreatedAt = DateTime.UtcNow.AddMinutes(-8)
            });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-bound-hardfact",
            Status = "referenced",
            SourceSessionId = "session-1",
            SourceRunId = "run-chapter-001",
            UsageCount = 3,
            FirstSeenAt = DateTime.UtcNow.AddMinutes(-9),
            LastUsedAt = DateTime.UtcNow.AddMinutes(-7)
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "snapshot-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            VersionNumber = 1,
            Source = "chapter_commit",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            SnapshotJson = """
            {
              "chapterId": "chapter-001",
              "chapterTitle": "第一章：黑雨维修站",
              "nextChapterMustCarry": ["陈默刚拿到银蓝邮徽"],
              "characterStates": ["陈默右手灼伤，但能行走"],
              "activeConflicts": ["维修站外黑潮异兽围拢"],
              "worldRules": ["邮徽不能主动攻击"],
              "knowledgeConstraintEvidence": [
                {
                  "knowledgeId": "knowledge-bound-hardfact",
                  "title": "银蓝邮徽能力边界",
                  "entryType": "HardFact",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter",
                  "gateStatus": "validated",
                  "evidenceStatus": "satisfied",
                  "classificationRule": "银蓝邮徽只能辨认旧邮路，不能攻击。",
                  "shouldEnterFactSnapshot": true
                }
              ]
            }
            """
        });
        await db.SaveChangesAsync();
    }

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        private readonly KnowledgeSearchResult? _result;

        public RecordingKnowledgeService(KnowledgeSearchResult? result = null)
        {
            _result = result;
        }

        public (string KnowledgeId, string ProjectId, string? SessionId, string? RunId)? LastUsage { get; private set; }

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
            Task.FromResult(_result == null ? new List<KnowledgeSearchResult>() : new List<KnowledgeSearchResult> { _result });

        public Task IncrementUsageAsync(
            string knowledgeId,
            string projectId,
            string? sessionId = null,
            string? runId = null,
            string? idempotencyKey = null,
            CancellationToken ct = default)
        {
            LastUsage = (knowledgeId, projectId, sessionId, runId);
            return Task.CompletedTask;
        }
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
            return Task.FromResult(new List<SearchResult> { _result });
        }
    }
}
