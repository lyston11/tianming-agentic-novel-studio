using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using Xunit;
using Moq;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeIngestionTests
{
    [Fact]
    public async Task StoreAndFinalizeAsync_PersistsByteaSemanticStructureVersionsAndAbstractStyle()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        const string text = "雨夜谈判时，短句逐步加密。\n双方对白表面克制，威胁藏在礼貌称谓里。";
        var bytes = Encoding.UTF8.GetBytes(text);
        var model = new StubKnowledgeStructureModel(new KnowledgeStructureAnalysis(
            "克制的雨夜谈判示例",
            [
                new KnowledgeSectionDraft("雨夜谈判", "短句和潜台词组织冲突", 0, text.Length)
            ],
            new AbstractStyleProfileDraft(
                "近距离第三人称",
                "短句递进，关键处延长",
                0.42,
                0.28,
                ["雨声", "礼貌称谓"],
                "克制后骤升",
                "先动作后揭示意图"),
            [1]));
        var service = new KnowledgeDocumentIngestionService(
            db,
            new StubCurrentUserService("user-1"),
            model);

        var blob = await service.StoreUploadAsync(
            "project-1",
            "谈判样章.txt",
            "text/plain",
            bytes);
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-1",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "WritingMethod",
                Title = "潜台词",
                Content = "通过礼貌措辞隐藏威胁"
            },
            new KnowledgeBase
            {
                Id = "knowledge-2",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "主角身份",
                Content = "主角是失踪王女"
            });
        await db.SaveChangesAsync();

        await service.FinalizeProcessingAsync(
            blob.Id,
            text,
            ["knowledge-1", "knowledge-2"]);

        var storedBlob = await db.KnowledgeDocumentBlobs.SingleAsync();
        Assert.Equal(bytes, storedBlob.Data);
        Assert.Equal("bytea", db.Model.FindEntityType(typeof(KnowledgeDocumentBlob))!
            .FindProperty(nameof(KnowledgeDocumentBlob.Data))!
            .FindAnnotation("Relational:ColumnType")?.Value);
        Assert.Equal(1, storedBlob.KnowledgeVersion);
        var section = Assert.Single(await db.KnowledgeSections.ToListAsync());
        var chunk = Assert.Single(await db.KnowledgeChunks.ToListAsync());
        Assert.Equal(section.Id, chunk.SectionId);
        Assert.Equal(text, chunk.Text);
        var entryVersions = await db.KnowledgeEntries.OrderBy(entry => entry.SourceEntryIndex).ToListAsync();
        Assert.Equal(2, entryVersions.Count);
        Assert.Equal("active", entryVersions[0].Status);
        Assert.Equal("proposed", entryVersions[1].Status);
        Assert.All(entryVersions, entry => Assert.Equal(1, entry.Version));
        var style = await db.StyleProfiles.SingleAsync();
        Assert.Equal("abstract_features_only", style.ProfileKind);
        Assert.DoesNotContain("作者", style.FeaturesJson);
        Assert.DoesNotContain(text, style.FeaturesJson);
        Assert.Equal("近距离第三人称",
            JsonDocument.Parse(style.FeaturesJson).RootElement.GetProperty("narrativeDistance").GetString());
    }

    [Fact]
    public async Task StoreUploadAsync_IncrementsUserGlobalKnowledgeVersionAcrossProjects()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        db.NovelProjects.Add(new NovelProject { Id = "project-2", UserId = "user-1", Title = "第二本书" });
        await db.SaveChangesAsync();
        var service = new KnowledgeDocumentIngestionService(
            db,
            new StubCurrentUserService("user-1"),
            new StubKnowledgeStructureModel(null));

        var first = await service.StoreUploadAsync("project-1", "a.txt", "text/plain", [1, 2, 3]);
        var second = await service.StoreUploadAsync("project-2", "b.txt", "text/plain", [4, 5, 6]);

        Assert.Equal(1, first.KnowledgeVersion);
        Assert.Equal(2, second.KnowledgeVersion);
    }

    [Fact]
    public async Task Citation_FreezesEntryVersionAndMarksOnlyTheProjectThatActuallyUsedIt()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        db.KnowledgeEntries.AddRange(
            new KnowledgeEntry
            {
                Id = "entry-v1",
                UserId = "user-1",
                ProjectId = "project-1",
                LogicalKnowledgeId = "knowledge-1",
                DocumentBlobId = "blob-1",
                KnowledgeVersion = 3,
                Version = 1,
                EntryType = "WritingMethod",
                Title = "潜台词",
                Content = "旧版本",
                Status = "active"
            },
            new KnowledgeEntry
            {
                Id = "entry-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                LogicalKnowledgeId = "knowledge-1",
                DocumentBlobId = "blob-2",
                KnowledgeVersion = 4,
                Version = 2,
                EntryType = "WritingMethod",
                Title = "潜台词",
                Content = "新版本",
                Status = "active"
            });
        await db.SaveChangesAsync();
        var usage = new Mock<IProjectKnowledgeUsageService>(MockBehavior.Strict);
        usage.Setup(service => service.MarkReferencedAsync(
                "user-1",
                "project-1",
                "knowledge-1",
                null,
                "goal-1",
                "citation-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var citations = new KnowledgeCitationService(
            db,
            new StubCurrentUserService("user-1"),
            usage.Object);

        var citation = await citations.RecordAsync(new RecordKnowledgeCitationRequest(
            "project-1",
            "entry-v1",
            "goal-1",
            "chapter-1",
            "chapter-version-1",
            "style_reference",
            "evidence-bundle-1",
            "citation-1"));

        Assert.Equal("entry-v1", citation.KnowledgeEntryId);
        Assert.Equal(3, citation.KnowledgeVersion);
        Assert.Equal("chapter-version-1", citation.ChapterVersionId);
        Assert.Equal("旧版本", (await db.KnowledgeEntries.FindAsync(citation.KnowledgeEntryId))!.Content);
        usage.VerifyAll();
    }

    [Fact]
    public async Task FinalizeProcessingAsync_RejectsImitationOrContinuationStyleInstructions()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        var model = new StubKnowledgeStructureModel(new KnowledgeStructureAnalysis(
            "摘要",
            [new KnowledgeSectionDraft("全文", "摘要", 0, 4)],
            new AbstractStyleProfileDraft(
                "模仿某作者的近距离视角",
                "按原文续写",
                0.5,
                0.3,
                [],
                "克制",
                "延迟释放"),
            []));
        var service = new KnowledgeDocumentIngestionService(db, new StubCurrentUserService("user-1"), model);
        var blob = await service.StoreUploadAsync("project-1", "a.txt", "text/plain", Encoding.UTF8.GetBytes("测试正文"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FinalizeProcessingAsync(blob.Id, "测试正文", []));

        Assert.Empty(await db.StyleProfiles.ToListAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedScopeAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "测试小说" });
        await db.SaveChangesAsync();
    }

    private sealed class StubKnowledgeStructureModel(KnowledgeStructureAnalysis? result) : IKnowledgeStructureModelClient
    {
        public Task<KnowledgeStructureAnalysis> AnalyzeAsync(
            KnowledgeStructureRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result ?? throw new InvalidOperationException("本测试不应调用结构模型。"));
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }
}
