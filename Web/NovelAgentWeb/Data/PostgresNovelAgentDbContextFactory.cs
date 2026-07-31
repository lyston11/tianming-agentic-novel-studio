using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TM.Web.NovelAgentWeb.Data;

public sealed class PostgresNovelAgentDbContextFactory
    : IDesignTimeDbContextFactory<PostgresNovelAgentDbContext>
{
    public PostgresNovelAgentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NovelAgentDb")
            ?? "Host=localhost;Port=5432;Database=novelagent;Username=novelagent_app;Password=novelagent_app";
        var options = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresNovelAgentDbContext(options);
    }
}
