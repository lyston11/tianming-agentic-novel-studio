using Microsoft.EntityFrameworkCore;

namespace TM.Web.NovelAgentWeb.Data;

public sealed class PostgresNovelAgentDbContext : NovelAgentDbContext
{
    public PostgresNovelAgentDbContext(DbContextOptions<PostgresNovelAgentDbContext> options)
        : base(options)
    {
    }
}
