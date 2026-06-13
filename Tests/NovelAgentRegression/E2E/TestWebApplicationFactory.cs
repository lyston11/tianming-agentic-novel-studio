using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// Custom WebApplicationFactory for E2E tests.
/// Configures test environment with in-memory SQLite database and test services.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<TM.Web.NovelAgentWeb.Program>
{
    private const string TestJwtSecretKey = "test-secret-key-with-at-least-32-characters-for-security";
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSecretKey,
                ["JwtSettings:Issuer"] = "NovelAgentWeb",
                ["JwtSettings:Audience"] = "NovelAgentWeb",
                ["JwtSettings:ExpiryDays"] = "7"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<NovelAgentDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Create in-memory SQLite connection that persists for the lifetime of the factory
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            // Add test DbContext with in-memory SQLite
            services.AddDbContext<NovelAgentDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.RemoveAll<IVectorStore>();
            services.AddSingleton<IVectorStore, NoopVectorStore>();

            // Override JWT configuration for tests
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = "NovelAgentWeb",
                    ValidateAudience = true,
                    ValidAudience = "NovelAgentWeb",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

            // Build service provider and ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<NovelAgentDbContext>();

            db.Database.EnsureCreated();
        });

        // Use test environment
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class NoopVectorStore : IVectorStore
{
    public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<SearchResult>> SearchSimilarAsync(
        string userId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken ct = default) =>
        Task.FromResult(new List<SearchResult>());

    public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
    public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;
}
