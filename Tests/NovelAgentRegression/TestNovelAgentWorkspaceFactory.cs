using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;
using DbNovelProject = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;
using DbUser = TM.Web.NovelAgentWeb.Data.Entities.User;

namespace TM.Tests.NovelAgentRegression;

internal static class TestNovelAgentWorkspaceFactory
{
    public static NovelAgentWorkspace Create(string? storageRoot = null, string projectName = "NovelAgentRegression")
    {
        storageRoot ??= Path.Combine(Path.GetTempPath(), "novelagent-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = storageRoot,
                ["NovelAgent:ProjectName"] = projectName
            })
            .Build();
        var settings = RegressionUserSettingsFactory.CreateDbBacked("regression-user");

        return new NovelAgentWorkspace(
            new RegressionWebHostEnvironment(storageRoot),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            "regression-project"
            );
    }

    public static void SeedCatalog(NovelAgentWorkspace workspace)
    {
        _ = workspace;
    }

    public static void BindWorkspace(NovelAgentWorkspace workspace)
    {
        PhaseContextBuilder.SetWorkspace(workspace);
    }

    public static void ClearWorkspace()
    {
        PhaseContextBuilder.ClearWorkspace();
    }

    private static IServiceScopeFactory CreateWorkspaceScopeFactory()
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        services.AddSingleton(connection);
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, DirectMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new DbUser
        {
            Id = "regression-user",
            Username = "regression-user",
            Email = "regression-user@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new DbNovelProject
        {
            Id = "regression-project",
            UserId = "regression-user",
            Title = "regression-project",
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class RegressionWebHostEnvironment : IWebHostEnvironment
    {
        public RegressionWebHostEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
            ContentRootFileProvider = new PhysicalFileProvider(root);
            WebRootFileProvider = new PhysicalFileProvider(root);
        }

        public string ApplicationName { get; set; } = "NovelAgentRegression";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
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

    private sealed class DirectMemoryCacheService : IMemoryCacheService
    {
        public async Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            await factory().ConfigureAwait(false);

        public T? Get<T>(string key) => default;

        public void Set<T>(string key, T value, TimeSpan expiration) { }

        public void Remove(string key) { }

        public void RemoveByPrefix(string keyPrefix) { }
    }
}
