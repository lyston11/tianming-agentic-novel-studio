using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// Custom WebApplicationFactory for E2E tests.
/// Configures test environment with in-memory SQLite database and test services.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<TM.Web.NovelAgentWeb.Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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

            // Override JWT configuration for tests
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var testSecretKey = "test-secret-key-with-at-least-32-characters-for-security";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(testSecretKey)),
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

