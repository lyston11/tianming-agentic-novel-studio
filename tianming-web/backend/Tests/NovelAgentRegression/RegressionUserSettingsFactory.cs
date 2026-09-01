using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Tests.NovelAgentRegression;

internal static class RegressionUserSettingsFactory
{
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    public static UserSettingsManager CreateDbBacked(
        string userId = "regression-user",
        UserSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILlmApiKeyProtector>(
            new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider()));
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"), DatabaseRoot));
        var provider = services.BuildServiceProvider();

        var manager = new UserSettingsManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new HttpContextAccessor(),
            new FixedBackgroundUserContext(userId),
            provider.GetRequiredService<ILlmApiKeyProtector>());

        manager.SaveAsync(settings ?? new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4o",
            LlmApiKey = "test-key"
        }).GetAwaiter().GetResult();
        return manager;
    }

    private sealed class FixedBackgroundUserContext : IBackgroundUserContext
    {
        public FixedBackgroundUserContext(string userId)
        {
            Current = new BackgroundUserSnapshot(userId, "author", "author@example.com", "author");
        }

        public BackgroundUserSnapshot? Current { get; }

        public IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author") =>
            new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
