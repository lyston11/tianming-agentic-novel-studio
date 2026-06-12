using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace TM.Web.NovelAgentWeb.Scripts;

public static class MigrateMemoryToSqlite
{
    public static async Task RunAsync(IServiceProvider serviceProvider)
    {
        var configuration = (IConfiguration)serviceProvider.GetService(typeof(IConfiguration))!;
        var connectionString = configuration.GetConnectionString("NovelAgentDb") ?? "Data Source=novelagent.db";

        Console.WriteLine("=== Memory Migration Tool ===");
        Console.WriteLine($"Database: {connectionString}");

        // Check if already migrated
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var existingCount = await GetExistingCountAsync(connection);
        if (existingCount > 0)
        {
            Console.WriteLine($"WARNING: Database already contains {existingCount} memory records.");
            Console.Write("Continue anyway? (y/n): ");
            var answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y") return;
        }

        var projectsRoot = Path.Combine("App_Data", "Projects");
        if (!Directory.Exists(projectsRoot))
        {
            Console.WriteLine($"ERROR: Projects directory not found: {projectsRoot}");
            return;
        }

        var projectDirs = Directory.GetDirectories(projectsRoot);
        Console.WriteLine($"Found {projectDirs.Length} project directories.");

        // Get first user from database
        var firstUserId = await GetFirstUserIdAsync(connection);
        if (string.IsNullOrEmpty(firstUserId))
        {
            Console.WriteLine("ERROR: No users found in database. Please create a user first.");
            return;
        }
        Console.WriteLine($"Using user ID: {firstUserId}");

        // Load project ID mappings from database
        var projectIdMap = await GetProjectIdMapAsync(connection);
        Console.WriteLine($"Loaded {projectIdMap.Count} project mappings from database.");

        var stats = new MigrationStats();

        foreach (var projectDir in projectDirs)
        {
            var projectName = Path.GetFileName(projectDir);
            var agentDir = Path.Combine(projectDir, "Agent");
            if (!Directory.Exists(agentDir)) continue;

            Console.WriteLine($"\nProcessing: {projectName}");

            // Map directory name to actual project ID from database
            if (!projectIdMap.TryGetValue(projectName, out var projectId))
            {
                Console.WriteLine("  WARNING: Project not found in database, skipping.");
                continue;
            }

            await MigrateProjectMemory(agentDir, firstUserId, projectId, connection, stats);
            await MigrateExecutionMemory(agentDir, firstUserId, projectId, connection, stats);
        }

        // Migrate global author_memory.json (cross-project)
        await MigrateAuthorMemory(projectsRoot, firstUserId, connection, stats);

        Console.WriteLine("\n=== Migration Summary ===");
        Console.WriteLine($"OK: Projects migrated: {stats.ProjectCount}");
        Console.WriteLine($"OK: Execution memories migrated: {stats.ExecutionCount}");
        Console.WriteLine($"OK: Author memories migrated: {stats.AuthorCount}");
        Console.WriteLine($"OK: Total fields written: {stats.TotalFields}");
    }

    private static async Task<int> GetExistingCountAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_memories";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<string?> GetFirstUserIdAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM users LIMIT 1";
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    private static async Task<Dictionary<string, string>> GetProjectIdMapAsync(SqliteConnection connection)
    {
        var map = new Dictionary<string, string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, storage_project_name FROM novel_projects";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var storageName = reader.GetString(1);
            map[storageName] = id;
        }
        return map;
    }

    private static async Task InsertMemoryFieldAsync(SqliteConnection connection, string userId, string? projectId, string memoryType, object value)
    {
        var json = JsonSerializer.Serialize(value);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
            VALUES (@id, @userId, @projectId, @memoryType, @content, @updatedAt)";

        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@projectId", projectId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@memoryType", memoryType);
        cmd.Parameters.AddWithValue("@content", json);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MigrateProjectMemory(string agentDir, string userId, string projectId, SqliteConnection connection, MigrationStats stats)
    {
        var filePath = Path.Combine(agentDir, "project_memory.json");
        if (!File.Exists(filePath)) return;

        var json = await File.ReadAllTextAsync(filePath);
        var mem = JsonSerializer.Deserialize<ProjectMemoryJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (mem == null) return;

        var fieldCount = 0;
        if (!string.IsNullOrEmpty(mem.LongTermGoal))
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "project.long_term_goal", mem.LongTermGoal);
            fieldCount++;
        }
        if (!string.IsNullOrEmpty(mem.ReaderPromise))
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "project.reader_promise", mem.ReaderPromise);
            fieldCount++;
        }
        if (mem.Constraints?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "project.constraints", mem.Constraints);
            fieldCount++;
        }
        if (mem.UnresolvedThreads?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "project.unresolved_threads", mem.UnresolvedThreads);
            fieldCount++;
        }

        if (fieldCount > 0)
        {
            stats.ProjectCount++;
            stats.TotalFields += fieldCount;
            Console.WriteLine($"  OK: project_memory: {fieldCount} fields");
        }
    }

    private static async Task MigrateExecutionMemory(string agentDir, string userId, string projectId, SqliteConnection connection, MigrationStats stats)
    {
        var filePath = Path.Combine(agentDir, "execution_memory.json");
        if (!File.Exists(filePath)) return;

        var json = await File.ReadAllTextAsync(filePath);
        var mem = JsonSerializer.Deserialize<ExecutionMemoryJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (mem == null) return;

        var fieldCount = 0;
        if (mem.ToolFailurePatterns?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "execution.tool_failures", mem.ToolFailurePatterns);
            fieldCount++;
        }
        if (mem.RepeatedBlockers?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "execution.repeated_blockers", mem.RepeatedBlockers);
            fieldCount++;
        }
        if (mem.SuccessfulRepairNotes?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, projectId, "execution.successful_repairs", mem.SuccessfulRepairNotes);
            fieldCount++;
        }

        if (fieldCount > 0)
        {
            stats.ExecutionCount++;
            stats.TotalFields += fieldCount;
            Console.WriteLine($"  OK: execution_memory: {fieldCount} fields");
        }
    }

    private static async Task MigrateAuthorMemory(string projectsRoot, string userId, SqliteConnection connection, MigrationStats stats)
    {
        // Search for author_memory.json in any project's Agent directory
        var authorFiles = Directory.GetFiles(projectsRoot, "author_memory.json", SearchOption.AllDirectories);
        if (authorFiles.Length == 0) return;

        if (authorFiles.Length > 1)
        {
            Console.WriteLine($"\nWARNING: Found {authorFiles.Length} author_memory.json files (multi-user conflict). Skipping author memory migration.");
            return;
        }

        var filePath = authorFiles[0];
        var json = await File.ReadAllTextAsync(filePath);
        var mem = JsonSerializer.Deserialize<AuthorMemoryJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (mem == null) return;

        var fieldCount = 0;
        if (mem.StyleLikes?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, null, "author.style_likes", mem.StyleLikes);
            fieldCount++;
        }
        if (mem.StyleDislikes?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, null, "author.style_dislikes", mem.StyleDislikes);
            fieldCount++;
        }
        if (!string.IsNullOrEmpty(mem.ConfirmationTolerance))
        {
            await InsertMemoryFieldAsync(connection, userId, null, "author.confirmation_tolerance", mem.ConfirmationTolerance);
            fieldCount++;
        }
        if (mem.GenreHabits?.Count > 0)
        {
            await InsertMemoryFieldAsync(connection, userId, null, "author.genre_habits", mem.GenreHabits);
            fieldCount++;
        }

        if (fieldCount > 0)
        {
            stats.AuthorCount++;
            stats.TotalFields += fieldCount;
            Console.WriteLine($"\nOK: author_memory: {fieldCount} fields");
        }
    }

    private class MigrationStats
    {
        public int ProjectCount { get; set; }
        public int ExecutionCount { get; set; }
        public int AuthorCount { get; set; }
        public int TotalFields { get; set; }
    }

    private class ProjectMemoryJson
    {
        public string? ProjectId { get; set; }
        public string? LongTermGoal { get; set; }
        public string? ReaderPromise { get; set; }
        public string? Tone { get; set; }
        public List<string>? Constraints { get; set; }
        public List<string>? UnresolvedThreads { get; set; }
    }

    private class AuthorMemoryJson
    {
        public List<string>? StyleLikes { get; set; }
        public List<string>? StyleDislikes { get; set; }
        public string? ConfirmationTolerance { get; set; }
        public List<string>? GenreHabits { get; set; }
    }

    private class ExecutionMemoryJson
    {
        public List<string>? ToolFailurePatterns { get; set; }
        public List<string>? RepeatedBlockers { get; set; }
        public List<string>? SuccessfulRepairNotes { get; set; }
    }
}
