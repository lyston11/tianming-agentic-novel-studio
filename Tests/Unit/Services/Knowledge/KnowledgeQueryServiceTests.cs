using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_Inventory_ReturnsLightweightCatalogWithoutSearch()
    {
        await using var db = CreateDb();
        db.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
        {
            Id = "blob-1",
            UserId = "user-1",
            ProjectId = "project-1",
            FileName = "setting.txt",
            MimeType = "text/plain",
            Data = [],
            ContentHash = "hash",
            Status = "processed",
            KnowledgeVersion = 4
        });
        await db.SaveChangesAsync();
        var knowledge = CreateKnowledgeServiceMock();
        var service = new KnowledgeQueryService(knowledge.Object, new StubCurrentUserService(), db);

        var result = await service.QueryAsync(new KnowledgeQueryRequest(
            KnowledgeQueryIntent.Inventory,
            KnowledgeQueryScope.CurrentProject,
            "project-1",
            Limit: 5));

        Assert.Equal("Knowledge.Query", result.Context.ToolName);
        Assert.Equal("inventory", result.Context.Intent);
        Assert.Equal("knowledge:user-1:v4", result.Context.KnowledgeVersion);
        Assert.Equal(2, result.Context.TotalCount);
        Assert.Equal("灯城规则", Assert.Single(result.Context.Directories).SampleTitles[0]);
        Assert.Equal(2, result.Context.Items.Count);
        Assert.All(result.Context.Items, item => Assert.True(item.Excerpt.Length <= 241));
        knowledge.Verify(service => service.SearchKnowledgeAsync(
            It.IsAny<SearchKnowledgeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_Retrieve_ReturnsCatalogAndRelevantItemsThroughOneContract()
    {
        await using var db = CreateDb();
        var knowledge = CreateKnowledgeServiceMock();
        knowledge.Setup(service => service.SearchKnowledgeAsync(
                It.Is<SearchKnowledgeRequest>(request =>
                    request.ProjectId == "project-1" &&
                    request.Query == "主角能力边界" &&
                    request.TopK == 8),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new KnowledgeSearchResult
                {
                    Id = "knowledge-1",
                    EntryType = "Setting",
                    Title = "灯城规则",
                    Content = "灯城每夜熄灭一盏灯。",
                    Score = 0.92f,
                    SourceType = "manual",
                    ProjectUsageStatus = "referenced"
                }
            ]);
        var service = new KnowledgeQueryService(knowledge.Object, new StubCurrentUserService(), db);

        var result = await service.QueryAsync(new KnowledgeQueryRequest(
            KnowledgeQueryIntent.Retrieve,
            KnowledgeQueryScope.CurrentProject,
            "project-1",
            "主角能力边界",
            Limit: 8));

        Assert.Equal("retrieve", result.Context.Intent);
        Assert.Equal("主角能力边界", result.Context.Query);
        var item = Assert.Single(result.Context.Items);
        Assert.Equal("knowledge-1", item.Id);
        Assert.Equal(0.92f, item.Score);
        Assert.Single(result.Matches);
    }

    private static Mock<IKnowledgeService> CreateKnowledgeServiceMock()
    {
        var inventory = new List<KnowledgeResponse>
        {
            new()
            {
                Id = "knowledge-1",
                EntryType = "Setting",
                Title = "灯城规则",
                Content = "灯城每夜熄灭一盏灯。",
                Weight = 9,
                SourceType = "manual",
                ProjectUsageStatus = "imported",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = "knowledge-2",
                EntryType = "Setting",
                Title = "主角代价",
                Content = "每次点亮旧灯都会失去一段记忆。",
                Weight = 8,
                SourceType = "upload",
                ProjectUsageStatus = "none",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        };
        var service = new Mock<IKnowledgeService>(MockBehavior.Strict);
        service.Setup(item => item.ListKnowledgeAsync("project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);
        service.Setup(item => item.ListKnowledgeDirectoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new KnowledgeDirectoryResponse
                {
                    Key = "Setting",
                    Name = "设定",
                    EntryCount = 2,
                    IsSystem = true
                }
            ]);
        return service;
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public string GetUserId() => "user-1";
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => "user-1";
    }
}
