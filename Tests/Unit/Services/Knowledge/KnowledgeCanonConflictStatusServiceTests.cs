using System.Text.Json;
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

public sealed class KnowledgeCanonConflictStatusServiceTests
{
    [Fact]
    public async Task ApplyAsync_MarksPromotedCanonLedgerEntryConflictOrClear()
    {
        var store = new InMemoryStore(new StoryBibleDocument
        {
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:classification-blue-flame",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "知识规则：银蓝邮徽蓝焰",
                    Content = "银蓝邮徽可以释放蓝焰攻击。",
                    Rationale = "KnowledgeId=knowledge-blue-flame; ClassificationId=classification-blue-flame",
                    ConflictCheck = "pending",
                    SourceRunId = "run-classify"
                },
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:classification-unrelated",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "知识规则：无关",
                    Content = "无关规则。",
                    Rationale = "KnowledgeId=knowledge-unrelated; ClassificationId=classification-unrelated",
                    ConflictCheck = "pending"
                }
            }
        });
        var storyBible = new StoryBibleService(store);
        IKnowledgeCanonConflictStatusService service = new KnowledgeCanonConflictStatusService(storyBible);

        await service.ApplyAsync(new KnowledgeCanonConflictStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-blue-flame",
            HasConflict: true,
            ReportId: "conflict-a",
            Severity: "Hard",
            ConflictType: "HardConstraintContradiction",
            Explanation: "银蓝邮徽蓝焰攻击与能力边界冲突。",
            ConflictingKnowledgeIds: new[] { "knowledge-boundary" },
            RunId: "run-detect"), CancellationToken.None);

        var afterConflict = await storyBible.LoadAsync();
        var conflictEntry = afterConflict.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-blue-flame");
        Assert.Equal(CanonLedgerEntryStatus.Conflict, conflictEntry.Status);
        Assert.Contains("conflict-a", conflictEntry.ConflictCheck);
        Assert.Contains("HardConstraintContradiction", conflictEntry.ConflictCheck);
        Assert.Contains("knowledge-boundary", conflictEntry.ConflictCheck);

        var unrelated = afterConflict.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-unrelated");
        Assert.Equal(CanonLedgerEntryStatus.Canon, unrelated.Status);
        Assert.Equal("pending", unrelated.ConflictCheck);

        await service.ApplyAsync(new KnowledgeCanonConflictStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-unrelated",
            HasConflict: false,
            ReportId: "",
            Severity: "None",
            ConflictType: "None",
            Explanation: "",
            ConflictingKnowledgeIds: Array.Empty<string>(),
            RunId: "run-detect"), CancellationToken.None);

        var afterClear = await storyBible.LoadAsync();
        var clearEntry = afterClear.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-unrelated");
        Assert.Equal(CanonLedgerEntryStatus.Canon, clearEntry.Status);
        Assert.Contains("clear", clearEntry.ConflictCheck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyResolutionAsync_RewritesOrDemotesResolvedCanonLedgerEntry()
    {
        var store = new InMemoryStore(new StoryBibleDocument
        {
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:classification-blue-flame",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Conflict,
                    Title = "知识规则：银蓝邮徽蓝焰",
                    Content = "银蓝邮徽可以释放蓝焰攻击。",
                    Rationale = "KnowledgeId=knowledge-blue-flame; ClassificationId=classification-blue-flame",
                    ConflictCheck = "conflict_open; ReportId=conflict-a",
                    SourceRunId = "run-classify"
                },
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:classification-boundary",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "知识规则：邮徽能力边界",
                    Content = "银蓝邮徽只能辨认旧邮路，不能攻击。",
                    Rationale = "KnowledgeId=knowledge-boundary; ClassificationId=classification-boundary",
                    ConflictCheck = "clear"
                }
            }
        });
        var storyBible = new StoryBibleService(store);
        IKnowledgeCanonConflictStatusService service = new KnowledgeCanonConflictStatusService(storyBible);

        await service.ApplyResolutionAsync(new KnowledgeCanonConflictResolutionStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-blue-flame",
            ReportId: "conflict-a",
            ResolutionStatus: "resolved",
            ResolutionNote: "用户确认保留能力边界，蓝焰改为环境现象。",
            Severity: "Hard",
            ConflictType: "HardConstraintContradiction",
            ConflictingKnowledgeIds: new[] { "knowledge-boundary" },
            RunId: "run-resolve"), CancellationToken.None);

        var afterResolved = await storyBible.LoadAsync();
        var resolved = afterResolved.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-blue-flame");
        Assert.Equal(CanonLedgerEntryStatus.Canon, resolved.Status);
        Assert.Contains("冲突处理决定", resolved.Content);
        Assert.Contains("蓝焰改为环境现象", resolved.Content);
        Assert.Contains("resolved", resolved.ConflictCheck);
        Assert.Contains("conflict-a", resolved.ConflictCheck);
        var retainedBoundary = afterResolved.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-boundary");
        Assert.Equal(CanonLedgerEntryStatus.Canon, retainedBoundary.Status);
        Assert.Contains("retained_by_resolution", retainedBoundary.ConflictCheck);
        Assert.Contains("conflict-a", retainedBoundary.ConflictCheck);

        await service.ApplyResolutionAsync(new KnowledgeCanonConflictResolutionStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-blue-flame",
            ReportId: "conflict-a",
            ResolutionStatus: "rejected",
            ResolutionNote: "用户拒绝蓝焰攻击设定。",
            Severity: "Hard",
            ConflictType: "HardConstraintContradiction",
            ConflictingKnowledgeIds: new[] { "knowledge-boundary" },
            RunId: "run-resolve"), CancellationToken.None);

        var afterRejected = await storyBible.LoadAsync();
        var rejected = afterRejected.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-blue-flame");
        Assert.Equal(CanonLedgerEntryStatus.Rejected, rejected.Status);
        Assert.Contains("rejected", rejected.ConflictCheck);

        await service.ApplyResolutionAsync(new KnowledgeCanonConflictResolutionStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-blue-flame",
            ReportId: "conflict-a",
            ResolutionStatus: "superseded",
            ResolutionNote: "用户决定蓝焰攻击替代原有能力边界。",
            Severity: "Hard",
            ConflictType: "HardConstraintContradiction",
            ConflictingKnowledgeIds: new[] { "knowledge-boundary" },
            RunId: "run-resolve"), CancellationToken.None);

        var afterSuperseded = await storyBible.LoadAsync();
        var supersededBoundary = afterSuperseded.CanonLedger.Single(e => e.Id == "knowledge-classification:classification-boundary");
        Assert.Equal(CanonLedgerEntryStatus.Deprecated, supersededBoundary.Status);
        Assert.Contains("superseded_by_resolution", supersededBoundary.ConflictCheck);
    }

    [Fact]
    public async Task ApplyAsync_EnqueuesStoryBibleCanonIndexOutboxWhenCanonStatusChanges()
    {
        var services = BuildServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        SeedUserProject(db);
        var storyBible = new StoryBibleDocument
        {
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "knowledge-classification:classification-blue-flame",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "知识规则：银蓝邮徽蓝焰",
                    Content = "银蓝邮徽可以释放蓝焰攻击。",
                    Rationale = "KnowledgeId=knowledge-blue-flame; ClassificationId=classification-blue-flame",
                    ConflictCheck = "pending"
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
        IKnowledgeCanonConflictStatusService service = new KnowledgeCanonConflictStatusService(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        await service.ApplyAsync(new KnowledgeCanonConflictStatusRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            KnowledgeId: "knowledge-blue-flame",
            HasConflict: true,
            ReportId: "conflict-a",
            Severity: "Hard",
            ConflictType: "HardConstraintContradiction",
            Explanation: "银蓝邮徽蓝焰攻击与能力边界冲突。",
            ConflictingKnowledgeIds: Array.Empty<string>(),
            RunId: "run-detect"), CancellationToken.None);

        var outbox = await db.OutboxEvents.SingleAsync();
        Assert.Equal("index_story_bible_canon", outbox.EventType);
        Assert.Equal("story_bible", outbox.AggregateType);
        Assert.Equal("project-1", outbox.AggregateId);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("run-detect", outbox.RuntimeRunId);
    }

    private sealed class InMemoryStore : IStoryBibleDocumentStore
    {
        private StoryBibleDocument? _document;

        public InMemoryStore(StoryBibleDocument document)
        {
            _document = document;
        }

        public Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_document);

        public Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default)
        {
            _document = document;
            return Task.CompletedTask;
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
