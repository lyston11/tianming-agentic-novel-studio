using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Materials;

public class MaterialServiceTests
{
    [Fact]
    public async Task CreateMaterialAsync_PersistsContentAndEnqueuesIndexOutbox()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateService(db);

        var response = await service.CreateMaterialAsync(new CreateMaterialRequest
        {
            ProjectId = "project-1",
            Title = "素材标题",
            Content = "素材内容用于向量化",
            ContentType = "text/plain"
        });

        var material = await db.Materials.SingleAsync(m => m.Id == response.Id);
        Assert.False(string.IsNullOrWhiteSpace(material.RawDocumentId));
        Assert.Equal(0, material.VectorChunkCount);

        var outbox = await db.OutboxEvents.SingleAsync(e => e.AggregateId == response.Id);
        Assert.Equal("index_material_content", outbox.EventType);
        Assert.Equal("material", outbox.AggregateType);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("pending", outbox.Status);

        var storedContent = await new ContentDocumentService(db)
            .GetTextAsync("user-1", "project-1", "material", response.Id, "material_raw");
        Assert.Equal("素材内容用于向量化", storedContent);
        Assert.Equal(material.RawDocumentId, await db.ContentDocuments
            .Where(d => d.SourceType == "material" && d.SourceId == response.Id)
            .Select(d => d.Id)
            .SingleAsync());
    }

    [Fact]
    public async Task CreateMaterialAsync_WithSameIdempotencyKey_ReturnsExistingMaterial()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);
        var request = new CreateMaterialRequest
        {
            ProjectId = "project-1",
            Title = "废土邮路素材",
            Content = "旧邮路每次开启都会暴露坐标。",
            ContentType = "text/plain",
            Category = "设定",
            Tags = "邮路,代价",
            IdempotencyKey = "material-key-001"
        };

        var first = await service.CreateMaterialAsync(request);
        var second = await service.CreateMaterialAsync(request);

        Assert.Equal(first.Id, second.Id);
        var material = await db.Materials.SingleAsync();
        Assert.Equal("material-key-001", material.IdempotencyKey);
        Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Single(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task UploadMaterialAsync_WithSameIdempotencyKey_ReturnsExistingMaterial()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);

        var first = await service.UploadMaterialAsync(new UploadMaterialRequest
        {
            ProjectId = "project-1",
            Title = "废土邮路素材.txt",
            Category = "设定",
            Tags = "邮路,代价",
            File = BuildTextFile("废土邮路素材.txt", "旧邮路每次开启都会暴露坐标。"),
            IdempotencyKey = "material-upload-key-001"
        });
        var second = await service.UploadMaterialAsync(new UploadMaterialRequest
        {
            ProjectId = "project-1",
            Title = "废土邮路素材.txt",
            Category = "设定",
            Tags = "邮路,代价",
            File = BuildTextFile("废土邮路素材.txt", "旧邮路每次开启都会暴露坐标。"),
            IdempotencyKey = "material-upload-key-001"
        });

        Assert.Equal(first.Id, second.Id);
        var material = await db.Materials.SingleAsync();
        Assert.Equal("material-upload-key-001", material.IdempotencyKey);
        Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Single(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task DeleteMaterialAsync_EnqueuesVectorCleanupBeforeRemovingRow()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.Materials.Add(new Material
        {
            Id = "material-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "素材标题",
            CreatedAt = DateTime.UtcNow,
            VectorChunkCount = 2
        });
        await db.SaveChangesAsync();
        await new ContentDocumentService(db).SaveTextAsync(
            "user-1",
            "project-1",
            "material",
            "material-1",
            "material_raw",
            "素材标题",
            "素材内容");

        var service = CreateService(db);

        await service.DeleteMaterialAsync("material-1");

        Assert.Null(await db.Materials.FindAsync("material-1"));
        var delete = await db.OutboxEvents.SingleAsync(e => e.EventType == "delete_material_content");
        Assert.Equal("project-1", delete.ProjectId);
        Assert.Equal("material", delete.AggregateType);
        Assert.Equal("material-1", delete.AggregateId);
        Assert.Equal("pending", delete.Status);
    }

    [Fact]
    public async Task CountProjectMaterialsAsync_ReturnsOnlyUserProjectMaterialCount()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.Users.Add(new User
        {
            Id = "user-2",
            Username = "other",
            Email = "other@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-1",
            Title = "其他项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Materials.AddRange(
            new Material { Id = "material-1", UserId = "user-1", ProjectId = "project-1", Title = "本项目素材", CreatedAt = DateTime.UtcNow },
            new Material { Id = "material-2", UserId = "user-1", ProjectId = "project-2", Title = "其他项目素材", CreatedAt = DateTime.UtcNow },
            new Material { Id = "material-3", UserId = "user-2", ProjectId = "project-1", Title = "其他用户素材", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var count = await service.CountProjectMaterialsAsync("user-1", "project-1");

        Assert.Equal(1, count);
    }

    private static MaterialService CreateService(NovelAgentDbContext db)
    {
        return new MaterialService(
            db,
            new FixedCurrentUserService("user-1"),
            NullLogger<MaterialService>.Instance,
            new ContentDocumentService(db),
            new ProductionTruthStore(db));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static IFormFile BuildTextFile(string fileName, string content)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "File", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }

    private static void SeedUserProject(NovelAgentDbContext db)
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
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly string _userId;

        public FixedCurrentUserService(string userId)
        {
            _userId = userId;
        }

        public string GetUserId() => _userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => _userId;
    }

}
