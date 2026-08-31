using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using Tests.Unit.Support;
using Xunit;

namespace Tests.Unit.Services.Workspace;

public sealed class WorkspaceFactoryProductionKernelTests
{
    [Fact]
    public async Task AcquireAsync_BuildsCurrentRepositoryProductionKernelInsideWorkspace()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName, dbRoot));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "KernelTest",
                ["NovelAgent:StorageRoot"] = Path.Combine(Path.GetTempPath(), "WorkspaceFactoryKernelTests")
            })
            .Build());
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetTempPath());
        services.AddSingleton(env.Object);
        services.AddSingleton(UserSettingsTestFactory.CreateDbBacked());
        services.AddSingleton(Mock.Of<IVectorStore>());
        services.AddSingleton(Mock.Of<IMicroEmbeddingService>());
        services.AddScoped(_ => Mock.Of<ICurrentUserService>());
        services.AddScoped(_ => Mock.Of<IAgentMemoryRepository>());
        services.AddTianmingProductionKernelServices();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
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
                Title = "项目",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var factory = new WorkspaceFactory(Options.Create(new WorkspaceFactoryOptions()), provider);
        var entry = await factory.AcquireAsync("user-1", "project-1");

        var actualKernel = typeof(TM.Services.Framework.AI.NovelAgent.Services.NovelAgentOrchestrator)
            .GetField("_productionKernel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(entry.Workspace.Orchestrator);

        Assert.IsType<HardcoreWritingProductionKernel>(actualKernel);
    }
}
