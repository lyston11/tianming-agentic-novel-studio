using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class AgentControlDbContextFactory : IDesignTimeDbContextFactory<AgentControlDbContext>
{
    public AgentControlDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOVEL_AGENT_MIGRATION_DB")
            ?? throw new InvalidOperationException("NOVEL_AGENT_MIGRATION_DB is required for AgentControl migrations.");
        var options = new DbContextOptionsBuilder<AgentControlDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__AgentControlMigrationsHistory"))
            .Options;
        return new AgentControlDbContext(options);
    }
}
