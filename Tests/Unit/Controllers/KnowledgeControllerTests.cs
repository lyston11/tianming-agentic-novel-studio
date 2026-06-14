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
            new ContentDocumentService(db));

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
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static KnowledgeController CreateController(NovelAgentDbContext db, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);

        return new KnowledgeController(
            Mock.Of<IKnowledgeService>(),
            db,
            currentUser.Object,
            NullLogger<KnowledgeController>.Instance,
            new ContentDocumentService(db));
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
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "knowledge.txt");
    }
}
