using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Reliability;

public sealed class KnowledgeProcessingTaskClaimTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _applicationConnectionString = string.Empty;
    private string _workerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var admin = CreateAdminDbContext();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE ROLE novelagent_app LOGIN PASSWORD 'novelagent_app'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE ROLE novelagent_worker LOGIN PASSWORD 'novelagent_worker'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT CONNECT ON DATABASE postgres TO novelagent_app;
            GRANT CONNECT ON DATABASE postgres TO novelagent_worker;
            GRANT USAGE ON SCHEMA public TO novelagent_app;
            GRANT USAGE ON SCHEMA public TO novelagent_worker;
            """);
        await admin.Database.MigrateAsync();
        await admin.Database.ExecuteSqlRawAsync("""
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO novelagent_app;
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO novelagent_app;
            """);

        _applicationConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_app",
            Password = "novelagent_app"
        }.ConnectionString;
        _workerConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "novelagent_worker",
            Password = "novelagent_worker"
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentWorkers_AtomicallyClaimDifferentUploadsWithoutTenantScope()
    {
        await SeedAsync(
            CreateTask("task-1", "user-1", "blob-1", DateTime.UtcNow.AddSeconds(-1)),
            CreateTask("task-2", "user-2", "blob-2", DateTime.UtcNow));
        await using var dbA = CreateApplicationDbContext();
        await using var dbB = CreateApplicationDbContext();
        Assert.Equal(0, await dbA.KnowledgeProcessingTasks.CountAsync());
        var claimerA = CreateClaimer(dbA);
        var claimerB = CreateClaimer(dbB);

        var claims = await Task.WhenAll(
            claimerA.ClaimNextAsync("worker-a", TimeSpan.FromMinutes(2)),
            claimerB.ClaimNextAsync("worker-b", TimeSpan.FromMinutes(2)));

        Assert.All(claims, Assert.NotNull);
        Assert.Equal(2, claims.Select(claim => claim!.TaskId).Distinct().Count());
        Assert.Equal(["user-1", "user-2"], claims.Select(claim => claim!.UserId).Order().ToArray());
        Assert.All(claims, claim => Assert.Equal("extract", claim!.ProcessingStage));
    }

    [Fact]
    public async Task ClaimNextAsync_RecoversExpiredIndexLeaseWithoutResettingStage()
    {
        var task = CreateTask("task-index", "user-1", "blob-index", DateTime.UtcNow);
        task.Status = "processing";
        task.ProcessingStage = "index";
        task.ProcessingOwner = "dead-worker";
        task.ProcessingLeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        task.Attempt = 1;
        await SeedAsync(task);
        await using var db = CreateApplicationDbContext();
        var claimer = CreateClaimer(db);

        var claim = await claimer.ClaimNextAsync("recovery-worker", TimeSpan.FromMinutes(2));

        Assert.NotNull(claim);
        Assert.Equal("index", claim.ProcessingStage);
        Assert.Equal(2, claim.Attempt);
        Assert.Equal("recovery-worker", claim.LeaseOwner);
    }

    private async Task SeedAsync(params KnowledgeProcessingTask[] tasks)
    {
        await using var admin = CreateAdminDbContext();
        foreach (var userId in tasks.Select(task => task.UserId).Distinct())
        {
            admin.Users.Add(new User
            {
                Id = userId,
                Username = userId,
                Email = $"{userId}@example.test",
                PasswordHash = "test",
                Role = "author"
            });
            admin.NovelProjects.Add(new NovelProject
            {
                Id = $"project-{userId}",
                UserId = userId,
                Title = $"Project {userId}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var task in tasks)
        {
            admin.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
            {
                Id = task.UploadBlobId!,
                UserId = task.UserId,
                ProjectId = task.ProjectId!,
                FileName = $"{task.Id}.txt",
                MimeType = "text/plain",
                Data = "knowledge"u8.ToArray(),
                ContentHash = task.Id,
                KnowledgeVersion = 1,
                Status = "uploaded"
            });
        }
        admin.KnowledgeProcessingTasks.AddRange(tasks);
        await admin.SaveChangesAsync();
    }

    private static KnowledgeProcessingTask CreateTask(
        string id,
        string userId,
        string blobId,
        DateTime createdAt) => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = $"project-{userId}",
        FileName = $"{id}.txt",
        FileSize = 9,
        UploadBlobId = blobId,
        Status = "pending",
        ProcessingStage = "extract",
        MaxAttempts = 3,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private PostgresNovelAgentDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private PostgresNovelAgentDbContext CreateApplicationDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(_applicationConnectionString)
            .Options;
        return new PostgresNovelAgentDbContext(options);
    }

    private PostgresKnowledgeProcessingTaskClaimer CreateClaimer(NovelAgentDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovelAgentWorkerDb"] = _workerConnectionString
            })
            .Build();
        return new PostgresKnowledgeProcessingTaskClaimer(
            db,
            new BackgroundClaimConnectionFactory(configuration));
    }
}
