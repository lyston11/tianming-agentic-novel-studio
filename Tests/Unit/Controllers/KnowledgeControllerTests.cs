using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Controllers;

public class KnowledgeControllerTests
{
    [Fact]
    public async Task UploadFile_RequiresProjectId()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, "user-1");

        var result = await controller.UploadFile(CreateFormFile("知识原文"), null, "上传知识");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadFile_RejectsProjectOutsideCurrentUser()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        SeedUser(db, "user-2");
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-2",
            Title = "Other Project",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, "user-1");

        var result = await controller.UploadFile(CreateFormFile("知识原文"), "project-2", "上传知识");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UploadFile_StoresTaskAndRawContentInProjectScope()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "Project One",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, "user-1");

        var result = await controller.UploadFile(CreateFormFile("知识原文"), "project-1", "上传知识");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var task = await db.KnowledgeProcessingTasks.SingleAsync();
        Assert.Equal("project-1", task.ProjectId);
        Assert.False(string.IsNullOrWhiteSpace(task.UploadDocumentId));
        Assert.False(string.IsNullOrWhiteSpace(task.UploadBlobId));
        Assert.Equal(Encoding.UTF8.GetBytes("知识原文"), (await db.KnowledgeDocumentBlobs.SingleAsync()).Data);
        var content = await new ContentDocumentService(db).GetTextAsync(
            "user-1",
            "project-1",
            "knowledge_upload",
            task.Id,
            "upload_raw",
            CancellationToken.None);
        Assert.Equal("知识原文", content);
        Assert.Equal(task.UploadDocumentId, await db.ContentDocuments
            .Where(d => d.SourceType == "knowledge_upload" && d.SourceId == task.Id)
            .Select(d => d.Id)
            .SingleAsync());
    }

    [Fact]
    public async Task UploadFile_WithSameIdempotencyKey_ReturnsExistingTaskWithoutDuplicatingRawDocument()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "Project One",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, "user-1");
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "knowledge-upload-key-001";

        var firstResult = await controller.UploadFile(CreateFormFile("知识原文"), "project-1", "上传知识");
        var secondResult = await controller.UploadFile(CreateFormFile("知识原文"), "project-1", "上传知识");

        var firstTaskId = ReadTaskId(Assert.IsType<OkObjectResult>(firstResult).Value);
        var secondTaskId = ReadTaskId(Assert.IsType<OkObjectResult>(secondResult).Value);
        Assert.Equal(firstTaskId, secondTaskId);
        var task = await db.KnowledgeProcessingTasks.SingleAsync();
        Assert.Equal("knowledge-upload-key-001", task.IdempotencyKey);
        Assert.Equal(1, await db.ContentDocuments.CountAsync(d =>
            d.SourceType == "knowledge_upload" &&
            d.SourceId == task.Id &&
            d.DocumentRole == "upload_raw"));
    }

    [Fact]
    public async Task CreateKnowledge_WithSameIdempotencyKey_ReturnsExistingKnowledge()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "Project One",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = CreateRealController(db, "user-1");
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "knowledge-key-001";
        var request = new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。"
        };

        var firstResult = await controller.CreateKnowledge(request, CancellationToken.None);
        var secondResult = await controller.CreateKnowledge(request, CancellationToken.None);

        var first = Assert.IsType<KnowledgeResponse>(Assert.IsType<OkObjectResult>(firstResult).Value);
        var second = Assert.IsType<KnowledgeResponse>(Assert.IsType<OkObjectResult>(secondResult).Value);
        Assert.Equal(first.Id, second.Id);
        var knowledge = await db.KnowledgeBases.SingleAsync();
        Assert.Equal("knowledge-key-001", knowledge.IdempotencyKey);
    }

    [Fact]
    public async Task CreateDirectory_WithSameIdempotencyKey_ReturnsExistingDirectory()
    {
        await using var db = CreateDb();
        SeedUser(db, "user-1");
        var controller = CreateRealController(db, "user-1");
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "directory-key-001";
        var request = new CreateKnowledgeDirectoryRequest
        {
            Name = "人物设定"
        };

        var firstResult = await controller.CreateDirectory(request, CancellationToken.None);
        var secondResult = await controller.CreateDirectory(request, CancellationToken.None);

        var first = Assert.IsType<KnowledgeDirectoryResponse>(Assert.IsType<OkObjectResult>(firstResult).Value);
        var second = Assert.IsType<KnowledgeDirectoryResponse>(Assert.IsType<OkObjectResult>(secondResult).Value);
        Assert.Equal(first.Key, second.Key);
        var directory = await db.KnowledgeDirectories.SingleAsync();
        Assert.Equal("directory-key-001", directory.IdempotencyKey);
    }

    [Fact]
    public async Task IncrementUsage_RequiresProjectContext()
    {
        await using var db = CreateDb();
        var service = new Mock<IKnowledgeService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns("user-1");
        var controller = new KnowledgeController(
            service.Object,
            db,
            currentUser.Object,
            NullLogger<KnowledgeController>.Instance,
            new ContentDocumentService(db),
            CreateIngestionService(db, currentUser.Object));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "usage-key-001";

        var result = await controller.IncrementUsage(
            "knowledge-1",
            new IncrementKnowledgeUsageRequest { ProjectId = "project-1", SessionId = "session-1", RunId = "run-1" },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.Verify(x => x.IncrementUsageAsync(
            "knowledge-1",
            "project-1",
            "session-1",
            "run-1",
            "usage-key-001",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static KnowledgeController CreateController(NovelAgentDbContext db, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);

        return WithHttpContext(new KnowledgeController(
            Mock.Of<IKnowledgeService>(),
            db,
            currentUser.Object,
            NullLogger<KnowledgeController>.Instance,
            new ContentDocumentService(db),
            CreateIngestionService(db, currentUser.Object)));
    }

    private static KnowledgeController CreateRealController(NovelAgentDbContext db, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);
        var service = new KnowledgeService(
            db,
            currentUser.Object,
            new SemanticSearchService(
                Mock.Of<IVectorStore>(),
                Mock.Of<TM.Services.Framework.AI.Embedding.IMicroEmbeddingService>(),
                NullLogger<SemanticSearchService>.Instance),
            NullLogger<KnowledgeService>.Instance,
            new TM.Web.NovelAgentWeb.Services.Production.ProductionTruthStore(db));

        return WithHttpContext(new KnowledgeController(
            service,
            db,
            currentUser.Object,
            NullLogger<KnowledgeController>.Instance,
            new ContentDocumentService(db),
            CreateIngestionService(db, currentUser.Object)));
    }

    private static IKnowledgeDocumentIngestionService CreateIngestionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser) =>
        new KnowledgeDocumentIngestionService(db, currentUser, new NeverCalledKnowledgeStructureModel());

    private sealed class NeverCalledKnowledgeStructureModel : IKnowledgeStructureModelClient
    {
        public Task<KnowledgeStructureAnalysis> AnalyzeAsync(
            KnowledgeStructureRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("上传测试不应执行文档结构分析。");
    }

    private static KnowledgeController WithHttpContext(KnowledgeController controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedUser(NovelAgentDbContext db, string userId)
    {
        db.Users.Add(new User
        {
            Id = userId,
            Username = userId,
            Email = $"{userId}@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
    }

    private static IFormFile CreateFormFile(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "knowledge.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }

    private static string ReadTaskId(object? value)
    {
        Assert.NotNull(value);
        var property = value!.GetType().GetProperty("taskId");
        Assert.NotNull(property);
        return Assert.IsType<string>(property!.GetValue(value));
    }
}
