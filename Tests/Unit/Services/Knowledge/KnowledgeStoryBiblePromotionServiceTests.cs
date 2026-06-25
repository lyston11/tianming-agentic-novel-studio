using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeStoryBiblePromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_WritesClassificationRuleIntoCanonLedger()
    {
        var store = new InMemoryStore();
        var storyBible = new StoryBibleService(store);
        var outputArtifacts = new RecordingOutputArtifactRecorder();
        IKnowledgeStoryBiblePromotionService service = new KnowledgeStoryBiblePromotionService(
            storyBible,
            outputArtifacts);

        await service.PromoteAsync(new KnowledgeStoryBiblePromotionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-badge",
            KnowledgeTitle: "银蓝邮徽能力边界",
            KnowledgeEntryType: "ReaderPromise",
            ClassificationId: "classification-1",
            Model: "fake-llm",
            Role: "ItemRule",
            Scope: "ProjectWide",
            ConstraintLevel: "HardConstraint",
            PackagePolicy: "DefaultEveryChapter",
            Rule: "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
            TargetEntities: new[] { "银蓝邮徽" },
            ShouldEnterGate: true,
            ShouldEnterBlueprint: true,
            ShouldEnterFactSnapshot: true,
            Confidence: 0.88,
            SessionId: "session-1",
            RunId: "run-1"), CancellationToken.None);

        var document = await storyBible.LoadAsync();
        var entry = Assert.Single(document.CanonLedger);
        Assert.Equal("knowledge-classification:classification-1", entry.Id);
        Assert.Equal(CanonLedgerEntryType.Constraint, entry.Type);
        Assert.Equal(CanonLedgerEntryStatus.Canon, entry.Status);
        Assert.Equal("知识规则：银蓝邮徽能力边界", entry.Title);
        Assert.Contains("银蓝邮徽只能辨认旧邮路", entry.Content);
        Assert.Contains("KnowledgeId=knowledge-badge", entry.Rationale);
        Assert.Contains("ClassificationId=classification-1", entry.Rationale);
        Assert.Contains("ShouldEnterGate=True", entry.Rationale);
        Assert.Equal("ProjectWide", entry.ImpactScope);
        Assert.Equal("run-1", entry.SourceRunId);
        Assert.Contains(document.Revisions, revision =>
            revision.Action == "AddLedgerEntry" &&
            revision.Summary.Contains("银蓝邮徽能力边界", StringComparison.Ordinal));

        var outputArtifact = Assert.Single(outputArtifacts.Requests);
        Assert.Equal("KnowledgeStoryBiblePromotion", outputArtifact.ToolName);
        Assert.Equal("story_bible_canon_promotion", outputArtifact.Stage);
        Assert.Equal("story_bible_canon_knowledge", outputArtifact.ArtifactType);
        Assert.Equal("knowledge-classification:classification-1", outputArtifact.ArtifactId);
        Assert.Equal("ProcessArtifact", outputArtifact.OutputKind);
        Assert.Contains("创作工作流", outputArtifact.UserVisibleWhere);
        Assert.Contains("知识库", outputArtifact.UserVisibleWhere);
        Assert.Contains("Story Bible", outputArtifact.Summary);
        Assert.Equal("story_bible_canon_promoted", outputArtifact.SourceEventType);
    }

    [Fact]
    public async Task PromoteAsync_EnqueuesStoryBibleCanonIndexOutbox()
    {
        var services = BuildServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        SeedUserProject(db);
        var truthStore = scope.ServiceProvider.GetRequiredService<IProductionTruthStore>();
        IKnowledgeStoryBiblePromotionService service = new KnowledgeStoryBiblePromotionService(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new OutputArtifactRecorder(new ProductionEventWriter(truthStore)));

        await service.PromoteAsync(new KnowledgeStoryBiblePromotionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-badge",
            KnowledgeTitle: "银蓝邮徽能力边界",
            KnowledgeEntryType: "ReaderPromise",
            ClassificationId: "classification-1",
            Model: "fake-llm",
            Role: "ItemRule",
            Scope: "ProjectWide",
            ConstraintLevel: "HardConstraint",
            PackagePolicy: "DefaultEveryChapter",
            Rule: "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
            TargetEntities: new[] { "银蓝邮徽" },
            ShouldEnterGate: true,
            ShouldEnterBlueprint: true,
            ShouldEnterFactSnapshot: true,
            Confidence: 0.88,
            SessionId: "session-1",
            RunId: "run-1"), CancellationToken.None);

        var outbox = await db.OutboxEvents.SingleAsync();
        Assert.Equal("index_story_bible_canon", outbox.EventType);
        Assert.Equal("story_bible", outbox.AggregateType);
        Assert.Equal("project-1", outbox.AggregateId);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("run-1", outbox.RuntimeRunId);

        var outputArtifact = await db.ProductionEvents.SingleAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "story_bible_canon_knowledge" &&
            evt.ArtifactId == "knowledge-classification:classification-1");
        Assert.Equal("story_bible_canon_promotion", outputArtifact.Stage);
        Assert.Equal("completed", outputArtifact.Status);
        Assert.Contains("Story Bible", outputArtifact.Message);
        Assert.Contains("story_bible_canon_promoted", outputArtifact.DataJson);
    }

    private sealed class InMemoryStore : IStoryBibleDocumentStore
    {
        private StoryBibleDocument? _document;

        public Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_document);

        public Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default)
        {
            _document = document;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutputArtifactRecorder : IOutputArtifactRecorder
    {
        public List<OutputArtifactRecordRequest> Requests { get; } = new();

        public Task<OutputArtifactRecordResult> RecordAsync(
            OutputArtifactRecordRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new OutputArtifactRecordResult(
                new ProductionEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RuntimeRunId = request.RuntimeRunId,
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    ChapterId = request.ChapterId,
                    PackageId = request.PackageId,
                    EventType = OutputArtifactRecorder.EventType,
                    Stage = request.Stage,
                    Status = request.Status,
                    Message = request.Summary,
                    ArtifactType = request.ArtifactType,
                    ArtifactId = request.ArtifactId,
                    CreatedAt = DateTime.UtcNow
                },
                new TM.Web.NovelAgentWeb.Support.AgentToolProducedArtifact
                {
                    ArtifactType = request.ArtifactType,
                    ArtifactId = request.ArtifactId,
                    OutputKind = request.OutputKind,
                    Summary = request.Summary
                }));
        }
    }

    private static ServiceProvider BuildServices()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        return services.BuildServiceProvider();
    }

    private static void SeedUserProject(NovelAgentDbContext db)
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
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
}
