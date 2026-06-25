using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeClassificationServiceTests
{
    [Fact]
    public async Task ClassifyAndApplyAsync_PersistsLlmClassificationAndUpdatesProjectBindingOnly()
    {
        await using var db = CreateDb();
        Seed(db);
        var model = new FakeKnowledgeClassificationModelClient(new KnowledgeClassificationDecision
        {
            Model = "fake-llm",
            Role = "ItemRule",
            Scope = "ProjectWide",
            Priority = 92,
            ConstraintLevel = "HardConstraint",
            PackagePolicy = "DefaultEveryChapter",
            TargetEntities = new List<string> { "银蓝邮徽" },
            Rule = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
            ShouldEnterGate = true,
            ShouldEnterBlueprint = true,
            ShouldEnterFactSnapshot = true,
            Confidence = 0.88,
            RawJson = """
            {
              "role": "ItemRule",
              "scope": "ProjectWide",
              "priority": 92,
              "constraintLevel": "HardConstraint",
              "packagePolicy": "DefaultEveryChapter",
              "targetEntities": ["银蓝邮徽"],
              "rule": "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
              "shouldEnterGate": true,
              "shouldEnterBlueprint": true,
              "shouldEnterFactSnapshot": true,
              "confidence": 0.88
            }
            """
        });
        var promoter = new RecordingKnowledgeStoryBiblePromotionService();
        var truthStore = new ProductionTruthStore(db);
        var outputArtifacts = new OutputArtifactRecorder(new ProductionEventWriter(truthStore));
        IKnowledgeClassificationService service = new KnowledgeClassificationService(
            db,
            model,
            NullLogger<KnowledgeClassificationService>.Instance,
            promoter,
            outputArtifacts);

        var result = await service.ClassifyAndApplyAsync(new KnowledgeClassificationRequest(
            UserId: "user-1",
            ProjectId: "project-a",
            KnowledgeId: "knowledge-1",
            SessionId: "session-1",
            RunId: "run-1"));

        Assert.Equal("knowledge-1", result.KnowledgeId);
        Assert.Equal("project-a", result.ProjectId);
        Assert.Equal("ItemRule", result.Role);
        Assert.Equal("HardConstraint", result.ConstraintLevel);
        Assert.True(result.ShouldEnterGate);
        Assert.True(result.ShouldEnterBlueprint);
        Assert.True(result.ShouldEnterFactSnapshot);

        var saved = await db.KnowledgeClassifications.SingleAsync();
        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("project-a", saved.ProjectId);
        Assert.Equal("knowledge-1", saved.KnowledgeId);
        Assert.Equal("fake-llm", saved.Model);
        Assert.Equal(0.88, saved.Confidence, precision: 2);
        Assert.Contains("shouldEnterGate", saved.ClassificationJson);
        Assert.Equal("session-1", saved.SourceSessionId);
        Assert.Equal("run-1", saved.SourceRunId);

        var projectAUsage = await db.ProjectKnowledgeUsages.SingleAsync(usage => usage.ProjectId == "project-a");
        Assert.Equal("ItemRule", projectAUsage.Role);
        Assert.Equal("ProjectWide", projectAUsage.Scope);
        Assert.Equal(92, projectAUsage.Priority);
        Assert.Equal("HardConstraint", projectAUsage.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", projectAUsage.PackagePolicy);
        Assert.Equal("knowledge-v1", projectAUsage.BoundVersion);
        Assert.Contains(saved.Id, projectAUsage.Note);

        var projectBUsage = await db.ProjectKnowledgeUsages.SingleAsync(usage => usage.ProjectId == "project-b");
        Assert.Equal("Reference", projectBUsage.Role);
        Assert.Equal("Reference", projectBUsage.ConstraintLevel);
        Assert.Equal("RelevantOnly", projectBUsage.PackagePolicy);

        var promoted = Assert.Single(promoter.Requests);
        Assert.Equal(saved.Id, promoted.ClassificationId);
        Assert.Equal("knowledge-1", promoted.KnowledgeId);
        Assert.Equal("银蓝邮徽能力边界", promoted.KnowledgeTitle);
        Assert.Equal("ItemRule", promoted.Role);
        Assert.Equal("HardConstraint", promoted.ConstraintLevel);
        Assert.Equal("银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。", promoted.Rule);
        Assert.True(promoted.ShouldEnterGate);
        Assert.True(promoted.ShouldEnterBlueprint);
        Assert.True(promoted.ShouldEnterFactSnapshot);

        var outputArtifact = await db.ProductionEvents.SingleAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "knowledge_classification" &&
            evt.ArtifactId == saved.Id);
        Assert.Equal("knowledge_classification", outputArtifact.Stage);
        Assert.Equal("completed", outputArtifact.Status);
        Assert.Contains("HardConstraint", outputArtifact.Message);
        Assert.Contains("knowledge_classified", outputArtifact.DataJson);
        Assert.Contains("知识库", outputArtifact.DataJson);

        INovelProductionStateQueryService stateQuery = new NovelProductionStateQueryService(
            db,
            new ProductionChainProjectionService());
        var state = await stateQuery.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-a",
            RunId: "run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));
        Assert.NotNull(state);
        Assert.Contains(state!.OutputArtifacts, artifact =>
            artifact.ArtifactType == "knowledge_classification" &&
            artifact.ArtifactId == saved.Id &&
            artifact.SourceEventType == "knowledge_classified");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void Seed(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.AddRange(
            new NovelProject { Id = "project-a", UserId = "user-1", Title = "项目A" },
            new NovelProject { Id = "project-b", UserId = "user-1", Title = "项目B" });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-a",
            IdempotencyKey = "knowledge-v1",
            EntryType = "ReaderPromise",
            Title = "银蓝邮徽能力边界",
            Content = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
            Weight = 7,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.AddRange(
            new ProjectKnowledgeUsage
            {
                Id = "usage-a",
                UserId = "user-1",
                ProjectId = "project-a",
                KnowledgeId = "knowledge-1",
                Status = "imported",
                Role = "Reference",
                Scope = "ProjectWide",
                Priority = 50,
                ConstraintLevel = "Reference",
                PackagePolicy = "RelevantOnly",
                BoundVersion = "knowledge-v1",
                FirstSeenAt = DateTime.UtcNow
            },
            new ProjectKnowledgeUsage
            {
                Id = "usage-b",
                UserId = "user-1",
                ProjectId = "project-b",
                KnowledgeId = "knowledge-1",
                Status = "imported",
                Role = "Reference",
                Scope = "ProjectWide",
                Priority = 50,
                ConstraintLevel = "Reference",
                PackagePolicy = "RelevantOnly",
                BoundVersion = "knowledge-v1",
                FirstSeenAt = DateTime.UtcNow
            });
        db.SaveChanges();
    }

    private sealed class FakeKnowledgeClassificationModelClient : IKnowledgeClassificationModelClient
    {
        private readonly KnowledgeClassificationDecision _decision;

        public FakeKnowledgeClassificationModelClient(KnowledgeClassificationDecision decision)
        {
            _decision = decision;
        }

        public Task<KnowledgeClassificationDecision> ClassifyAsync(
            KnowledgeClassificationPrompt prompt,
            CancellationToken ct = default)
        {
            Assert.Equal("user-1", prompt.UserId);
            Assert.Equal("project-a", prompt.ProjectId);
            Assert.Equal("knowledge-1", prompt.KnowledgeId);
            Assert.Contains("银蓝邮徽", prompt.KnowledgeContent);
            return Task.FromResult(_decision);
        }
    }

    private sealed class RecordingKnowledgeStoryBiblePromotionService : IKnowledgeStoryBiblePromotionService
    {
        public List<KnowledgeStoryBiblePromotionRequest> Requests { get; } = new();

        public Task PromoteAsync(KnowledgeStoryBiblePromotionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

}
