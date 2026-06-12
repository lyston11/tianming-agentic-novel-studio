using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Materials;

public class MaterialServiceTests
{
    [Fact]
    public async Task CreateMaterialAsync_VectorizesMaterialBeforeReturning()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var vectorization = new RecordingMaterialVectorizationService(db);
        var service = CreateService(db, vectorization, new RecordingVectorStore());

        var response = await service.CreateMaterialAsync(new CreateMaterialRequest
        {
            ProjectId = "project-1",
            Title = "素材标题",
            Content = "素材内容用于向量化",
            ContentType = "text/plain"
        });

        Assert.Equal(new[] { $"{response.Id}:user-1" }, vectorization.Calls);
        Assert.Equal(2, response.VectorChunkCount);
    }

    [Fact]
    public async Task DeleteMaterialAsync_DeletesMaterialVectorsBeforeRemovingRow()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.Materials.Add(new Material
        {
            Id = "material-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "素材标题",
            Content = "素材内容",
            CreatedAt = DateTime.UtcNow,
            VectorChunkCount = 2
        });
        await db.SaveChangesAsync();

        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, new RecordingMaterialVectorizationService(db), vectorStore);

        await service.DeleteMaterialAsync("material-1");

        Assert.Null(await db.Materials.FindAsync("material-1"));
        var delete = Assert.Single(vectorStore.DeletedFilters);
        Assert.Equal("project-1", delete["project_id"]);
        Assert.Equal("material", delete["source_type"]);
        Assert.Equal("material-1", delete["source_id"]);
    }

    private static MaterialService CreateService(
        NovelAgentDbContext db,
        IMaterialVectorizationService vectorization,
        IVectorStore vectorStore)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = Path.GetTempPath()
            })
            .Build();

        return new MaterialService(
            db,
            new FixedCurrentUserService("user-1"),
            configuration,
            vectorization,
            vectorStore,
            NullLogger<MaterialService>.Instance);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
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

    private sealed class RecordingMaterialVectorizationService : IMaterialVectorizationService
    {
        private readonly NovelAgentDbContext _db;

        public RecordingMaterialVectorizationService(NovelAgentDbContext db)
        {
            _db = db;
        }

        public List<string> Calls { get; } = new();

        public async Task VectorizeMaterialAsync(string materialId, string userId, CancellationToken ct = default)
        {
            Calls.Add($"{materialId}:{userId}");
            var material = await _db.Materials.FirstAsync(m => m.Id == materialId && m.UserId == userId, ct);
            material.VectorChunkCount = 2;
            await _db.SaveChangesAsync(ct);
        }

        public Task<int> VectorizeAllMaterialsAsync(string projectId, string userId, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<Dictionary<string, object>> DeletedFilters { get; } = new();

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);

        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
        {
            DeletedFilters.Add(new Dictionary<string, object>(filters));
            return Task.CompletedTask;
        }
    }
}
