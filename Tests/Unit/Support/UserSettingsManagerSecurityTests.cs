using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public sealed class UserSettingsManagerSecurityTests
{
    [Fact]
    public async Task LoadAsync_WithoutCurrentUser_ThrowsInsteadOfUsingDefaultFallback()
    {
        var manager = UserSettingsTestFactory.CreateDbBackedWithoutCurrentUser();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.LoadAsync());

        Assert.Contains("database-backed user settings", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_WithoutCurrentUser_ThrowsInsteadOfUsingMemoryFallback()
    {
        var manager = UserSettingsTestFactory.CreateDbBackedWithoutCurrentUser();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsync(new UserSettings()));

        Assert.Contains("database-backed user settings", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_StoresProtectedApiKeyAndLoadAsyncRestoresPlaintext()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddSingleton<ILlmApiKeyProtector>(
            new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider()));
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        using var provider = services.BuildServiceProvider();
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "user-settings-security") },
                    authenticationType: "unit-test"))
            }
        };

        var manager = new UserSettingsManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            httpContextAccessor,
            new NoCurrentUserContext(),
            provider.GetRequiredService<ILlmApiKeyProtector>());

        await manager.SaveAsync(new UserSettings
        {
            LlmApiKey = "sk-unit-secret"
        });

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var entity = await db.UserSettings.SingleAsync();

            Assert.False(string.IsNullOrWhiteSpace(entity.LlmApiKeyEncrypted));
            Assert.NotEqual("sk-unit-secret", entity.LlmApiKeyEncrypted);
        }

        var loaded = await manager.LoadAsync();

        Assert.Equal("sk-unit-secret", loaded.LlmApiKey);
    }

    private sealed class NoCurrentUserContext : IBackgroundUserContext
    {
        public BackgroundUserSnapshot? Current => null;

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
