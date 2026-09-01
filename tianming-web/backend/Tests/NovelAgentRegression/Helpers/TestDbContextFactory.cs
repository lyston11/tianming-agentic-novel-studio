using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Tests.NovelAgentRegression.Helpers;

/// <summary>
/// Factory for creating in-memory SQLite database contexts for testing.
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new in-memory SQLite connection and DbContext.
    /// The connection must be kept open for the lifetime of the in-memory database.
    /// </summary>
    /// <returns>Tuple containing the connection (to keep open) and the context.</returns>
    public static (SqliteConnection Connection, NovelAgentDbContext Context) CreateInMemoryDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new NovelAgentDbContext(options);
        context.Database.EnsureCreated();

        return (connection, context);
    }

    /// <summary>
    /// Creates a disposable test context that automatically cleans up.
    /// Use with 'using' statement or 'using var' declaration.
    /// </summary>
    public static TestDbContext CreateDisposableContext()
    {
        return new TestDbContext();
    }
}

/// <summary>
/// Disposable wrapper for test database context that handles cleanup.
/// </summary>
public sealed class TestDbContext : IDisposable
{
    public SqliteConnection Connection { get; }
    public NovelAgentDbContext Context { get; }

    public TestDbContext()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(Connection)
            .Options;

        Context = new NovelAgentDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context?.Dispose();
        Connection?.Close();
        Connection?.Dispose();
    }
}
