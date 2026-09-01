using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public sealed class NovelProjectCatalogTests
{
    [Fact]
    public async Task CreateAsync_PersistsProjectToSqliteWithoutLegacyCatalogFile()
    {
        using var fixture = CatalogFixture.Create("project-seed");
        var catalog = fixture.CreateCatalog();

        var project = await catalog.CreateAsync(
            new NovelProjectCreateRequest("新书", "玄幻", "种子"),
            CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var saved = await db.NovelProjects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("新书", saved.Title);
        Assert.Equal("玄幻", saved.Genre);
        Assert.Equal("种子", saved.CoreHook);
        Assert.False(File.Exists(fixture.LegacyCatalogPath));
    }

    [Fact]
    public void Constructor_RequiresDatabaseScopeFactory()
    {
        using var fixture = CatalogFixture.Create("project-1");

        var exception = Assert.Throws<ArgumentNullException>(() => new NovelProjectCatalog(fixture.Workspace, null!));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task ActivateAndWithProject_RunAgainstDatabaseTruthOnly()
    {
        using var fixture = CatalogFixture.Create("project-1");
        await using (var db = fixture.CreateDbContext())
        {
            db.NovelProjects.AddRange(
                new NovelProject
                {
                    Id = "project-1",
                    UserId = "user-1",
                    Title = "Project One",
                    Status = "draft",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new NovelProject
                {
                    Id = "project-2",
                    UserId = "user-1",
                    Title = "Project Two",
                    Status = "draft",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        }

        var catalog = fixture.CreateCatalog();
        var project = await catalog.FindAsync("project-2", CancellationToken.None);
        Assert.NotNull(project);

        await catalog.ActivateAsync(project.Id, CancellationToken.None);
        var result = await catalog.WithProjectAsync(
            project,
            () => Task.FromResult(project.Id),
            CancellationToken.None);

        Assert.Equal("project-2", result);
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        private CatalogFixture(string root, ServiceProvider provider, NovelAgentWorkspace workspace)
        {
            Root = root;
            _provider = provider;
            Workspace = workspace;
        }

        public string Root { get; }
        public NovelAgentWorkspace Workspace { get; }
        public string LegacyCatalogPath => Path.Combine(
            Workspace.StorageRoot,
            "Projects",
            Workspace.ProjectName,
            "NovelProjects",
            "projects.json");

        public static CatalogFixture Create(string workspaceProjectId)
        {
            var root = Path.Combine(Path.GetTempPath(), $"novel-project-catalog-{Guid.NewGuid():N}");
            var databaseName = Guid.NewGuid().ToString("N");
            var services = new ServiceCollection();
            services.AddDbContext<NovelAgentDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.AddMemoryCache();
            services.AddSingleton<IMemoryCacheService>(sp =>
                new MemoryCacheService(
                    sp.GetRequiredService<IMemoryCache>(),
                    NullLogger<MemoryCacheService>.Instance));
            services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
            services.AddScoped<IContentDocumentService, ContentDocumentService>();

            var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
                db.Users.Add(new User
                {
                    Id = "user-1",
                    Username = "user-1",
                    Email = "user-1@example.com",
                    PasswordHash = "hash",
                    Role = "author"
                });
                db.SaveChanges();
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NovelAgent:StorageRoot"] = root,
                    ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
                })
                .Build();
            var settings = UserSettingsTestFactory.CreateDbBacked();
            var workspace = new NovelAgentWorkspace(
                new TestWebHostEnvironment(root),
                configuration,
                settings,
                new WorkspaceProductionRuntimeBuilder(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                "user-1",
                workspaceProjectId
                );

            return new CatalogFixture(root, provider, workspace);
        }

        public NovelAgentDbContext CreateDbContext() =>
            _provider.GetRequiredService<NovelAgentDbContext>();

        public NovelProjectCatalog CreateCatalog() =>
            new(Workspace, _provider.GetRequiredService<IServiceScopeFactory>());

        public void Dispose()
        {
            _provider.Dispose();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NoopDistributedCacheService : IDistributedCacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
            Task.FromResult<T?>(null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
