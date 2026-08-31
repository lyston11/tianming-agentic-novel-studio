using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TM.Web.NovelAgentWeb.Data;

public class NovelAgentDbContextFactory : IDesignTimeDbContextFactory<NovelAgentDbContext>
{
    public NovelAgentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NovelAgentDbContext>();

        // Use the default database path for migrations
        var dbPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "App_Data",
            "Database",
            "novelagent.db"
        );

        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new NovelAgentDbContext(optionsBuilder.Options);
    }
}
