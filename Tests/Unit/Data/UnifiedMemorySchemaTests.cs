using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace Tests.Unit.Data;

public class UnifiedMemorySchemaTests
{
    private const string BeforeUnifiedMemoryMigration = "20260612070000_AddTagsAndWeightToKnowledgeBase";
    private const string UnifiedMemoryMigration = "20260613054958_AddUnifiedMemoryPipeline";
    private const string BeforeKnowledgeUserOwnedMigration = "20260614150000_AddAgentRunOutputDocumentPointer";
    private const string KnowledgeUserOwnedMigration = "20260614162000_MakeKnowledgeBaseUserOwned";

    [Fact]
    public async Task DbContext_CanPersistUnifiedMemoryAndKnowledgeUsageRows()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new NovelAgentDbContext(options);
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-a", UserId = "user-1", Title = "A" });
        db.NovelProjects.Add(new NovelProject { Id = "project-b", UserId = "user-1", Title = "B" });
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-a", EntryType = "ReaderPromise", Title = "代价", Content = "胜利要有代价" });

        db.AgentChatTurns.Add(new AgentChatTurn { Id = "turn-1", SessionId = "session-1", UserId = "user-1", ProjectId = "project-a", TurnIndex = 1, Role = "user", Content = "记住这个设定" });
        db.AgentChatSummaries.Add(new AgentChatSummary { Id = "summary-1", SessionId = "session-1", UserId = "user-1", ProjectId = "project-a", StartTurn = 1, EndTurn = 10, SummaryType = "summary", Content = "用户确认设定" });
        db.AgentMemoryEvents.Add(new AgentMemoryEvent { Id = "event-1", UserId = "user-1", ProjectId = "project-a", SessionId = "session-1", SourceType = "knowledge_used", TriggerType = "tool_call", MemoryScope = "project", MemoryKey = "project.referenced_knowledge_ids", PayloadJson = """{"knowledgeId":"knowledge-1"}""" });
        db.AgentMemoryVersions.Add(new AgentMemoryVersion { UserId = "user-1", ProjectId = "project-a", SessionId = "session-1", Scope = "project", Version = 1 });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage { Id = "usage-a", UserId = "user-1", ProjectId = "project-a", KnowledgeId = "knowledge-1", Status = "referenced", UsageCount = 1 });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage { Id = "usage-b", UserId = "user-1", ProjectId = "project-b", KnowledgeId = "knowledge-1", Status = "imported", UsageCount = 0 });

        await db.SaveChangesAsync();

        Assert.Equal("referenced", await db.ProjectKnowledgeUsages.Where(x => x.ProjectId == "project-a").Select(x => x.Status).SingleAsync());
        Assert.Equal("imported", await db.ProjectKnowledgeUsages.Where(x => x.ProjectId == "project-b").Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.AgentMemoryVersions.Where(x => x.Scope == "project").Select(x => x.Version).SingleAsync());
    }

    [Fact]
    public void KnowledgeBase_IsUserOwned_WithSourceProjectOnly()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new NovelAgentDbContext(options);

        var knowledge = db.Model.FindEntityType(typeof(KnowledgeBase));
        var project = db.Model.FindEntityType(typeof(NovelProject));

        Assert.NotNull(knowledge);
        Assert.NotNull(project);
        Assert.NotNull(knowledge!.FindProperty(nameof(KnowledgeBase.UserId)));
        Assert.NotNull(knowledge.FindProperty(nameof(KnowledgeBase.SourceProjectId)));
        Assert.NotNull(knowledge.FindNavigation(nameof(KnowledgeBase.User)));
        Assert.NotNull(knowledge.FindNavigation(nameof(KnowledgeBase.SourceProject)));
        Assert.Null(project!.FindNavigation("KnowledgeBases"));

        var sourceProjectFk = knowledge.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(NovelProject));
        Assert.False(sourceProjectFk.IsRequired);
        Assert.Equal(DeleteBehavior.SetNull, sourceProjectFk.DeleteBehavior);
    }

    [Fact]
    public async Task Migration_PreservesExistingAgentMemoryRowsWhenAddingUnifiedColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(BeforeUnifiedMemoryMigration);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO users (id, username, email, password_hash, role)
                VALUES ('user-1', 'u', 'u@example.com', 'h', 'author');

                INSERT INTO novel_projects (id, user_id, title)
                VALUES ('project-1', 'user-1', 'A');

                INSERT INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
                VALUES ('memory-1', 'user-1', 'project-1', 'project', 'old memory', '2026-06-01 12:00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(UnifiedMemoryMigration);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COUNT(*), memory_key, created_at, session_id
                FROM agent_memories
                WHERE id = 'memory-1'
                GROUP BY memory_key, created_at, session_id;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(string.Empty, reader.GetString(1));
            Assert.False(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
        }
    }

    [Fact]
    public async Task Migration_BackfillsKnowledgeUserAndKeepsProjectAsSourceOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(BeforeKnowledgeUserOwnedMigration);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO users (id, username, email, password_hash, role)
                VALUES ('user-1', 'u', 'u@example.com', 'h', 'author');

                INSERT INTO novel_projects (id, user_id, title)
                VALUES ('project-1', 'user-1', 'A');

                INSERT INTO knowledge_base (
                    id,
                    project_id,
                    entry_type,
                    title,
                    content,
                    usage_count,
                    source_type,
                    tags,
                    weight
                )
                VALUES (
                    'knowledge-1',
                    'project-1',
                    'ReaderPromise',
                    '代价',
                    '胜利要有代价',
                    3,
                    'upload',
                    '["代价"]',
                    8
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = new NovelAgentDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(KnowledgeUserOwnedMigration);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT user_id, project_id, usage_count, source_type, tags, weight
                FROM knowledge_base
                WHERE id = 'knowledge-1';
                """;

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("user-1", reader.GetString(0));
            Assert.Equal("project-1", reader.GetString(1));
            Assert.Equal(3, reader.GetInt32(2));
            Assert.Equal("upload", reader.GetString(3));
            Assert.Equal("""["代价"]""", reader.GetString(4));
            Assert.Equal(8, reader.GetInt32(5));
        }

        await using (var deleteProject = connection.CreateCommand())
        {
            deleteProject.CommandText = "DELETE FROM novel_projects WHERE id = 'project-1';";
            await deleteProject.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT user_id, project_id FROM knowledge_base WHERE id = 'knowledge-1';";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("user-1", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
        }
    }

    [Fact]
    public async Task AgentMemoryVersions_EnforceLogicalUniquenessWhenScopeColumnsAreNull()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.AgentMemoryVersions.Add(new AgentMemoryVersion { UserId = "user-1", Scope = "author", Version = 1 });
        await db.SaveChangesAsync();

        db.AgentMemoryVersions.Add(new AgentMemoryVersion { UserId = "user-1", Scope = "author", Version = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AgentSession_DeleteCascadesToChatTurnsAndSummaries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.AgentSessions.Add(new AgentSession { Id = "session-1", UserId = "user-1", Title = "session" });
        db.AgentChatTurns.Add(new AgentChatTurn { Id = "turn-1", SessionId = "session-1", UserId = "user-1", TurnIndex = 1, Role = "user", Content = "hello" });
        db.AgentChatSummaries.Add(new AgentChatSummary { Id = "summary-1", SessionId = "session-1", UserId = "user-1", StartTurn = 1, EndTurn = 1, Content = "hello" });
        await db.SaveChangesAsync();

        var session = await db.AgentSessions.SingleAsync(x => x.Id == "session-1");
        db.AgentSessions.Remove(session);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.AgentChatTurns.CountAsync());
        Assert.Equal(0, await db.AgentChatSummaries.CountAsync());
    }

    [Fact]
    public async Task ContentVectorPoints_RejectChunkFromDifferentDocument()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.ContentDocuments.AddRange(
            new ContentDocument { Id = "doc-a", UserId = "user-1", SourceType = "manual", SourceId = "a", DocumentRole = "source", Title = "A", ContentHash = "hash-a" },
            new ContentDocument { Id = "doc-b", UserId = "user-1", SourceType = "manual", SourceId = "b", DocumentRole = "source", Title = "B", ContentHash = "hash-b" });
        db.ContentChunks.Add(new ContentChunk { Id = "chunk-b", DocumentId = "doc-b", ChunkIndex = 0, ChunkText = "chunk", ContentHash = "chunk-hash" });
        await db.SaveChangesAsync();

        db.ContentVectorPoints.Add(new ContentVectorPoint
        {
            Id = "vector-1",
            DocumentId = "doc-a",
            ChunkId = "chunk-b",
            QdrantCollection = "content",
            QdrantPointId = "point-1",
            VectorModel = "model"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
