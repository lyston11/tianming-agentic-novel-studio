# Original Design Full Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the project back to the approved original architecture: frontend on `3002`, backend on `5002`, Redis required, SQLite + Qdrant persistence, unified agent memory, project-isolated knowledge memory, tool-search caching, and real API-driven frontend flows.

**Architecture:** Implement the repair as a sequence of independently testable layers. First add the SQLite schema and cache/version primitives, then build `MemoryContext + MemoryEvent`, then connect ChatHistory, Session/Project/Author/Execution memory, knowledge upload/use memory, `tool_search` cache, ports/CORS, and frontend API state. Every semantic vector in Qdrant must be rebuildable from SQLite; Redis is a required runtime/cache layer, not a content source of truth.

**Tech Stack:** ASP.NET Core 8, EF Core 8 SQLite, Redis via `Microsoft.Extensions.Caching.StackExchangeRedis`, Qdrant.Client, React 19, TanStack Query, Zustand, xUnit, Moq.

---

## Scope And Execution Notes

This plan is intentionally a master plan because the approved spec requires cross-cutting fixes. Execute tasks in order. Each task ends at a working, testable checkpoint and should be committed separately.

Use the current repository root:

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio
```

Recommended execution mode is subagent-driven: one fresh implementation agent per task, followed by review and verification before the next task starts.

## File Structure

Create these focused backend files:

- `Web/NovelAgentWeb/Data/Entities/ContentDocument.cs` - SQLite true source for long content documents.
- `Web/NovelAgentWeb/Data/Entities/ContentChunk.cs` - ordered content chunks.
- `Web/NovelAgentWeb/Data/Entities/ContentVectorPoint.cs` - Qdrant point mapping and index status.
- `Web/NovelAgentWeb/Data/Entities/AgentChatTurn.cs` - complete ChatHistory truth source.
- `Web/NovelAgentWeb/Data/Entities/AgentChatSummary.cs` - Summary and MetaSummary truth source.
- `Web/NovelAgentWeb/Data/Entities/AgentMemoryEvent.cs` - audit/source event for memory changes.
- `Web/NovelAgentWeb/Data/Entities/AgentMemoryVersion.cs` - cache invalidation version state.
- `Web/NovelAgentWeb/Data/Entities/ProjectKnowledgeUsage.cs` - project-scoped knowledge imported/referenced state.
- `Web/NovelAgentWeb/Services/Memory/AgentMemoryKeys.cs` - canonical Redis/MemoryCache key builder.
- `Web/NovelAgentWeb/Services/Memory/IAgentMemoryVersionService.cs` and `AgentMemoryVersionService.cs` - version read/bump.
- `Web/NovelAgentWeb/Services/Memory/IAgentMemoryEventService.cs` and `AgentMemoryEventService.cs` - append memory events and invalidate caches.
- `Web/NovelAgentWeb/Services/Memory/IChatHistoryRepository.cs` and `ChatHistoryRepository.cs` - ChatHistory hot cache + SQLite truth.
- `Web/NovelAgentWeb/Services/Memory/IAgentMemoryContextService.cs` and `AgentMemoryContextService.cs` - one prompt-facing memory context.
- `Web/NovelAgentWeb/Services/Knowledge/IProjectKnowledgeUsageService.cs` and `ProjectKnowledgeUsageService.cs` - project-level knowledge state.
- `Web/NovelAgentWeb/Services/AgentTools/IToolSearchCacheService.cs` and `ToolSearchCacheService.cs` - MemoryCache + Redis + SQLite tool cache.

Modify these existing backend files:

- `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs` - DbSets and mappings.
- `Web/NovelAgentWeb/Data/Entities/AgentMemory.cs` - add `SessionId`, `MemoryKey`, `CreatedAt`.
- `Web/NovelAgentWeb/Data/Entities/KnowledgeBase.cs` - keep global row fields and rely on `ProjectKnowledgeUsage` for per-project usage.
- `Web/NovelAgentWeb/Program.cs` - required Redis registration, health checks, DI registrations, CORS `3002`.
- `Web/NovelAgentWeb/appsettings.json` and `appsettings.Development.json` - Redis enabled by default, backend `5002`, Qdrant HTTP/gRPC clarity.
- `Web/NovelAgentWeb/Support/AgentSession.cs` - keep snapshot but stop treating ChatHistory as the only truth source.
- `Web/NovelAgentWeb/Support/AgentRuntime.cs` - write chat turns through `IChatHistoryRepository`; emit memory events.
- `Web/NovelAgentWeb/Support/AgentCore.cs` - build prompt from `AgentMemoryContext`; load tool cache through service.
- `Web/NovelAgentWeb/Support/AgentToolRegistry.cs` - `tool_search` writes through `IToolSearchCacheService`; `SearchCreativeKnowledge` records project-scoped usage.
- `Web/NovelAgentWeb/Support/AgentMemoryService.cs` - persist rule-based changes even when `MemoryUpdate` is absent.
- `Web/NovelAgentWeb/Services/Memory/IAgentMemoryRepository.cs` and `AgentMemoryRepository.cs` - add SessionMemory and new knowledge/execution fields.
- `Web/NovelAgentWeb/Services/Knowledge/KnowledgeService.cs` - imported/referenced state and cache invalidation.
- `Web/NovelAgentWeb/Services/Knowledge/KnowledgeProcessingService.cs` - uploaded/processed/failure memory events.
- `Web/NovelAgentWeb/Controllers/KnowledgeController.cs` - upload event response plus `projectUsageStatus`, `projectUsageCount`, `projectLastUsedAt` response fields.

Modify these frontend files in Tasks 10-11 after the backend DTO fields compile:

- `Web/NovelAgentWeb.Frontend/src/api/client.ts`
- `Web/NovelAgentWeb.Frontend/src/api/index.ts`
- `Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts`
- `Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx`
- `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx`
- `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx`

Create or modify tests:

- `Tests/Unit/Services/Memory/AgentMemoryVersionServiceTests.cs`
- `Tests/Unit/Services/Memory/ChatHistoryRepositoryTests.cs`
- `Tests/Unit/Services/Memory/AgentMemoryContextServiceTests.cs`
- `Tests/Unit/Services/Memory/ToolSearchCacheServiceTests.cs`
- `Tests/Unit/Services/Knowledge/ProjectKnowledgeUsageServiceTests.cs`
- `Tests/Unit/Services/Knowledge/KnowledgeMemoryFlowTests.cs`
- existing `Tests/Unit/Services/Memory/AgentMemoryRepositoryTests.cs`
- existing `Tests/Unit/Support/AgentMemoryServiceTests.cs`
- existing `Tests/Unit/Services/Knowledge/KnowledgeServiceTests.cs`
- existing `Tests/AgentKernelRegression/Program.cs`

---

### Task 1: Required Redis And Port Baseline

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`
- Modify: `Web/NovelAgentWeb/appsettings.json`
- Modify: `Web/NovelAgentWeb/appsettings.Development.json`
- Modify: `docker-compose.yml`
- Test: `Tests/Unit/Services/Embedding/EmbeddingServiceCollectionExtensionsTests.cs` or new `Tests/Unit/ProgramConfigurationTests.cs`

- [ ] **Step 1: Write the failing configuration test**

Create `Tests/Unit/ProgramConfigurationTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Unit;

public class ProgramConfigurationTests
{
    [Fact]
    public void StandardConfiguration_UsesRequiredRuntimePortsAndRedis()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("../../../../Web/NovelAgentWeb/appsettings.json", optional: false)
            .Build();

        Assert.Equal("true", config["Redis:Enabled"], ignoreCase: true);
        Assert.Equal("localhost:6379", config["Redis:ConnectionString"]);
        Assert.Equal("http://localhost:6333", config["Qdrant:BaseUrl"]);
        Assert.Equal("6334", config["Qdrant:Port"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ProgramConfigurationTests.StandardConfiguration_UsesRequiredRuntimePortsAndRedis
```

Expected: FAIL because `Redis:Enabled` is currently `false` or the test file does not exist yet.

- [ ] **Step 3: Make Redis required in configuration**

Change `Web/NovelAgentWeb/appsettings.json`:

```json
"Redis": {
  "Enabled": true,
  "ConnectionString": "localhost:6379",
  "InstanceName": "NovelAgent:",
  "DefaultExpiration": "00:10:00",
  "AllowInMemoryFallback": false
}
```

Change `Web/NovelAgentWeb/appsettings.Development.json` to include:

```json
"Redis": {
  "Enabled": true,
  "ConnectionString": "localhost:6379",
  "InstanceName": "NovelAgent:",
  "DefaultExpiration": "00:10:00",
  "AllowInMemoryFallback": false
}
```

- [ ] **Step 4: Update backend cache registration**

In `Web/NovelAgentWeb/Program.cs`, replace the current Redis opt-in block with:

```csharp
var redisEnabled = builder.Configuration.GetValue("Redis:Enabled", true);
var redisAllowFallback = builder.Configuration.GetValue("Redis:AllowInMemoryFallback", false);
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
var redisInstanceName = builder.Configuration["Redis:InstanceName"];

if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = redisInstanceName ?? "NovelAgent:";
    });
}
else if (redisAllowFallback)
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    throw new InvalidOperationException("Redis is required. Set Redis:Enabled=true and Redis:ConnectionString, or set Redis:AllowInMemoryFallback=true only for tests.");
}
```

Also update CORS to allow the frontend origin:

```csharp
policy.WithOrigins("http://localhost:3002")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials();
```

- [ ] **Step 5: Run baseline tests**

Run:

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ProgramConfigurationTests.StandardConfiguration_UsesRequiredRuntimePortsAndRedis
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: both PASS.

- [ ] **Step 6: Commit**

```bash
git add Web/NovelAgentWeb/Program.cs Web/NovelAgentWeb/appsettings.json Web/NovelAgentWeb/appsettings.Development.json Tests/Unit/ProgramConfigurationTests.cs
git commit -m "feat(infra): require redis and align runtime ports"
```

---

### Task 2: SQLite Schema For Content, Memory, Chat, And Knowledge Usage

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/ContentDocument.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ContentChunk.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ContentVectorPoint.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentChatTurn.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentChatSummary.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentMemoryEvent.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentMemoryVersion.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ProjectKnowledgeUsage.cs`
- Modify: `Web/NovelAgentWeb/Data/Entities/AgentMemory.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`
- Test: `Tests/Unit/Data/UnifiedMemorySchemaTests.cs`

- [ ] **Step 1: Write the failing schema test**

Create `Tests/Unit/Data/UnifiedMemorySchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Xunit;

namespace Tests.Unit.Data;

public class UnifiedMemorySchemaTests
{
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
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", ProjectId = "project-a", EntryType = "ReaderPromise", Title = "代价", Content = "胜利要有代价" });

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
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~UnifiedMemorySchemaTests.DbContext_CanPersistUnifiedMemoryAndKnowledgeUsageRows
```

Expected: FAIL with missing entity/type errors.

- [ ] **Step 3: Add entity classes**

Add the eight entity files named in this task. Use these property sets exactly:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryVersion
{
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string Scope { get; set; } = null!;
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ProjectKnowledgeUsage
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string KnowledgeId { get; set; } = null!;
    public string Status { get; set; } = "imported";
    public string? SourceSessionId { get; set; }
    public string? SourceRunId { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public string? Note { get; set; }
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

Create `AgentChatTurn.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentChatTurn
{
    public string Id { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public int TurnIndex { get; set; }
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CompressedIntoSummaryId { get; set; }
}
```

Create `AgentChatSummary.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentChatSummary
{
    public string Id { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public string SummaryType { get; set; } = "summary";
    public string Content { get; set; } = null!;
    public string? KeyDecisionsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Create `AgentMemoryEvent.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemoryEvent
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RunId { get; set; }
    public string SourceType { get; set; } = null!;
    public string TriggerType { get; set; } = null!;
    public string MemoryScope { get; set; } = null!;
    public string MemoryKey { get; set; } = null!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Create `ContentDocument.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentDocument
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string SourceType { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string DocumentRole { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string MimeType { get; set; } = "text/plain";
    public string ContentHash { get; set; } = null!;
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ContentChunk> Chunks { get; set; } = new();
}
```

Create `ContentChunk.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentChunk
{
    public string Id { get; set; } = null!;
    public string DocumentId { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = null!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public string ContentHash { get; set; } = null!;
    public ContentDocument Document { get; set; } = null!;
}
```

Create `ContentVectorPoint.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ContentVectorPoint
{
    public string Id { get; set; } = null!;
    public string DocumentId { get; set; } = null!;
    public string? ChunkId { get; set; }
    public string QdrantCollection { get; set; } = null!;
    public string QdrantPointId { get; set; } = null!;
    public string VectorModel { get; set; } = null!;
    public DateTime? IndexedAt { get; set; }
    public string IndexStatus { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public ContentDocument Document { get; set; } = null!;
}
```

- [ ] **Step 4: Update `AgentMemory`**

Add:

```csharp
public string? SessionId { get; set; }
public string MemoryKey { get; set; } = string.Empty;
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

- [ ] **Step 5: Configure DbContext mappings**

Add DbSets and mapping blocks in `NovelAgentDbContext`. Important indexes:

```csharp
entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.Scope }).IsUnique();
entity.HasIndex(e => new { e.UserId, e.ProjectId, e.KnowledgeId }).IsUnique();
entity.HasIndex(e => new { e.SessionId, e.TurnIndex }).IsUnique();
entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex }).IsUnique();
```

- [ ] **Step 6: Create migration**

```bash
dotnet ef migrations add AddUnifiedMemoryPipeline --project Web/NovelAgentWeb/NovelAgentWeb.csproj --startup-project Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: migration adds the new tables and nullable columns without dropping existing data.

- [ ] **Step 7: Run schema tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~UnifiedMemorySchemaTests.DbContext_CanPersistUnifiedMemoryAndKnowledgeUsageRows
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Data Web/NovelAgentWeb/Migrations Tests/Unit/Data/UnifiedMemorySchemaTests.cs
git commit -m "feat(db): add unified memory and knowledge usage schema"
```

---

### Task 3: Memory Keys, Versions, And Events

**Files:**
- Create: `Web/NovelAgentWeb/Services/Memory/AgentMemoryKeys.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/IAgentMemoryVersionService.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/AgentMemoryVersionService.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/IAgentMemoryEventService.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/AgentMemoryEventService.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/Unit/Services/Memory/AgentMemoryVersionServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/Unit/Services/Memory/AgentMemoryVersionServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryVersionServiceTests
{
    [Fact]
    public async Task BumpAsync_IncrementsScopedVersionAndInvalidatesContextCaches()
    {
        await using var db = CreateDb();
        var memoryCache = new Mock<IMemoryCacheService>();
        var redis = new Mock<IDistributedCacheService>();
        var service = new AgentMemoryVersionService(db, memoryCache.Object, redis.Object, NullLogger<AgentMemoryVersionService>.Instance);

        var version = await service.BumpAsync("user-1", "project-1", "session-1", "project", CancellationToken.None);

        Assert.Equal(1, version);
        Assert.Equal(1, await db.AgentMemoryVersions.CountAsync());
        memoryCache.Verify(x => x.RemoveByPrefix("memory-context:user-1:session-1:project-1"), Times.Once);
        memoryCache.Verify(x => x.RemoveByPrefix("toolcache:user-1:session-1:project-1"), Times.Once);
        redis.Verify(x => x.RemoveAsync(It.Is<string>(k => k.Contains("memory-context:user-1:session-1:project-1")), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetCombinedVersionAsync_ChangesAfterBump()
    {
        await using var db = CreateDb();
        var service = new AgentMemoryVersionService(db, Mock.Of<IMemoryCacheService>(), Mock.Of<IDistributedCacheService>(), NullLogger<AgentMemoryVersionService>.Instance);

        var before = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1", CancellationToken.None);
        await service.BumpAsync("user-1", "project-1", "session-1", "execution", CancellationToken.None);
        var after = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.NotEqual(before, after);
        Assert.Contains("execution=1", after);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~AgentMemoryVersionServiceTests
```

Expected: FAIL because services do not exist.

- [ ] **Step 3: Add canonical key builder**

Create `AgentMemoryKeys.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Services.Memory;

public static class AgentMemoryKeys
{
    public static string ChatHot(string userId, string sessionId) => $"chat:{userId}:{sessionId}:hot";
    public static string Session(string userId, string sessionId, string projectId, string version) => $"memory:session:{userId}:{sessionId}:{projectId}:v{version}";
    public static string Project(string userId, string projectId, string version) => $"memory:project:{userId}:{projectId}:v{version}";
    public static string Author(string userId, string version) => $"memory:author:{userId}:v{version}";
    public static string Execution(string userId, string projectId, string version) => $"memory:execution:{userId}:{projectId}:v{version}";
    public static string MemoryContext(string userId, string sessionId, string projectId, string combinedVersion) => $"memory-context:{userId}:{sessionId}:{projectId}:v{combinedVersion}";
    public static string ToolCache(string userId, string sessionId, string projectId, string phase, string combinedVersion) => $"toolcache:{userId}:{sessionId}:{projectId}:{phase}:v{combinedVersion}";
}
```

- [ ] **Step 4: Implement version service**

Create `IAgentMemoryVersionService.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Services.Memory;

public interface IAgentMemoryVersionService
{
    Task<long> BumpAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct = default);
    Task<string> GetCombinedVersionAsync(string userId, string? projectId, string? sessionId, CancellationToken ct = default);
}
```

Create `AgentMemoryVersionService.cs` with:

```csharp
public async Task<long> BumpAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct = default)
{
    var row = await _db.AgentMemoryVersions.FirstOrDefaultAsync(v =>
        v.UserId == userId && v.ProjectId == projectId && v.SessionId == sessionId && v.Scope == scope, ct);
    if (row == null)
    {
        row = new AgentMemoryVersion { UserId = userId, ProjectId = projectId, SessionId = sessionId, Scope = scope, Version = 0 };
        _db.AgentMemoryVersions.Add(row);
    }
    row.Version += 1;
    row.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    if (!string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(sessionId))
    {
        _memoryCache.RemoveByPrefix($"memory-context:{userId}:{sessionId}:{projectId}");
        _memoryCache.RemoveByPrefix($"toolcache:{userId}:{sessionId}:{projectId}");
        await _redis.RemoveAsync($"memory-context:{userId}:{sessionId}:{projectId}", ct);
        await _redis.RemoveAsync($"toolcache:{userId}:{sessionId}:{projectId}", ct);
    }
    return row.Version;
}
```

Implement `GetCombinedVersionAsync` with this exact ordering behavior:

```csharp
var rows = await _db.AgentMemoryVersions
    .AsNoTracking()
    .Where(v => v.UserId == userId)
    .Where(v => v.ProjectId == projectId || v.ProjectId == null)
    .Where(v => v.SessionId == sessionId || v.SessionId == null)
    .OrderBy(v => v.Scope)
    .ThenBy(v => v.ProjectId)
    .ThenBy(v => v.SessionId)
    .ToListAsync(ct);

return rows.Count == 0
    ? "none=0"
    : string.Join("|", rows.Select(r => $"{r.Scope}:{r.ProjectId ?? "*"}:{r.SessionId ?? "*"}={r.Version}"));
```

- [ ] **Step 5: Implement event service**

Create `IAgentMemoryEventService.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Services.Memory;

public interface IAgentMemoryEventService
{
    Task AppendAsync(string userId, string? projectId, string? sessionId, string? runId, string sourceType, string triggerType, string memoryScope, string memoryKey, object payload, CancellationToken ct = default);
}
```

Create `AgentMemoryEventService.cs`; it must serialize `payload`, insert `AgentMemoryEvent`, save, then call `_versions.BumpAsync(userId, projectId, sessionId, memoryScope, ct)`.

- [ ] **Step 6: Register services**

In `Program.cs`:

```csharp
builder.Services.AddScoped<IAgentMemoryVersionService, AgentMemoryVersionService>();
builder.Services.AddScoped<IAgentMemoryEventService, AgentMemoryEventService>();
```

- [ ] **Step 7: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~AgentMemoryVersionServiceTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Services/Memory Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Memory/AgentMemoryVersionServiceTests.cs
git commit -m "feat(memory): add memory versions and events"
```

---

### Task 4: ChatHistory Truth Source And Prompt Summaries

**Files:**
- Create: `Web/NovelAgentWeb/Services/Memory/IChatHistoryRepository.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/ChatHistoryRepository.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentSession.cs`
- Modify: `Web/NovelAgentWeb/Support/ChatHistoryCompressor.cs`
- Test: `Tests/Unit/Services/Memory/ChatHistoryRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/Unit/Services/Memory/ChatHistoryRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ChatHistoryRepositoryTests
{
    [Fact]
    public async Task AppendAsync_WritesRedisHotWindowAndSqliteTruth()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var repo = new ChatHistoryRepository(db, redis.Object, memory.Object, NullLogger<ChatHistoryRepository>.Instance);

        await repo.AppendAsync("user-1", "project-1", "session-1", "user", "你好", CancellationToken.None);

        var saved = await db.AgentChatTurns.SingleAsync();
        Assert.Equal("session-1", saved.SessionId);
        Assert.Equal(1, saved.TurnIndex);
        Assert.Equal("你好", saved.Content);
        redis.Verify(x => x.SetAsync(It.Is<string>(k => k == "chat:user-1:session-1:hot"), It.IsAny<List<ChatHistoryTurnDto>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPromptWindowAsync_ReturnsMetaSummarySummariesAndRecentMessages()
    {
        await using var db = CreateDb();
        var repo = new ChatHistoryRepository(db, Mock.Of<IDistributedCacheService>(), Mock.Of<IMemoryCacheService>(), NullLogger<ChatHistoryRepository>.Instance);
        await repo.AppendAsync("user-1", "project-1", "session-1", "user", "第一条", CancellationToken.None);
        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 10, "summary", "前十轮摘要", new[] { "决定一" }, CancellationToken.None);
        await repo.SaveSummaryAsync("user-1", "project-1", "session-1", 1, 30, "meta", "总体摘要", Array.Empty<string>(), CancellationToken.None);

        var window = await repo.GetPromptWindowAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("总体摘要", window.MetaSummary);
        Assert.Contains(window.Summaries, s => s.Content == "前十轮摘要");
        Assert.Contains(window.RecentMessages, m => m.Content == "第一条");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ChatHistoryRepositoryTests
```

Expected: FAIL because repository and DTOs do not exist.

- [ ] **Step 3: Implement repository contracts**

`IChatHistoryRepository.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Services.Memory;

public record ChatHistoryTurnDto(string Role, string Content, DateTime CreatedAt);
public record ChatHistorySummaryDto(int StartTurn, int EndTurn, string SummaryType, string Content, IReadOnlyList<string> KeyDecisions);
public record ChatPromptWindowDto(string? MetaSummary, IReadOnlyList<ChatHistorySummaryDto> Summaries, IReadOnlyList<ChatHistoryTurnDto> RecentMessages);

public interface IChatHistoryRepository
{
    Task AppendAsync(string userId, string? projectId, string sessionId, string role, string content, CancellationToken ct = default);
    Task SaveSummaryAsync(string userId, string? projectId, string sessionId, int startTurn, int endTurn, string summaryType, string content, IReadOnlyList<string> keyDecisions, CancellationToken ct = default);
    Task<ChatPromptWindowDto> GetPromptWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default);
}
```

`ChatHistoryRepository.AppendAsync` must compute `TurnIndex` as `max + 1`, insert `AgentChatTurn`, load last 20 turns, then write `chat:{userId}:{sessionId}:hot` to Redis and MemoryCache with 10 minute TTL.

- [ ] **Step 4: Replace hard truncation in runtime**

In `AgentRuntime`, replace the private `AddChatTurn` call sites with an async helper:

```csharp
private async Task AddChatTurnAsync(AgentSession session, string role, string content, CancellationToken ct)
{
    var trimmed = content.Trim();
    session.ChatHistory.Add(new AgentConversationTurn { Role = role, Content = trimmed, CreatedAt = DateTime.UtcNow });
    await _chatHistory.AppendAsync(session.UserId, string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId, session.SessionId, role, trimmed, ct);
}
```

Keep `session.ChatHistory` as a UI/runtime snapshot. Remove the `if (session.ChatHistory.Count > 40)` truth cleanup from the old `AddChatTurn`.

- [ ] **Step 5: Persist summaries**

After `ChatHistoryCompressor.CompressAsync`, save each summary and meta summary through `IChatHistoryRepository.SaveSummaryAsync`.

- [ ] **Step 6: Register repository**

In `Program.cs`:

```csharp
builder.Services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
```

- [ ] **Step 7: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ChatHistoryRepositoryTests
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Services/Memory Web/NovelAgentWeb/Support/AgentRuntime.cs Web/NovelAgentWeb/Support/ChatHistoryCompressor.cs Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Memory/ChatHistoryRepositoryTests.cs
git commit -m "feat(memory): persist chat history truth and summaries"
```

---

### Task 5: SessionMemory, ProjectMemory, AuthorMemory, ExecutionMemory Repository Closure

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Memory/IAgentMemoryRepository.cs`
- Modify: `Web/NovelAgentWeb/Services/Memory/AgentMemoryRepository.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentMemoryService.cs`
- Test: `Tests/Unit/Services/Memory/AgentMemoryRepositoryTests.cs`
- Test: `Tests/Unit/Support/AgentMemoryServiceTests.cs`

- [ ] **Step 1: Add failing repository test for SessionMemory and new fields**

Add to `AgentMemoryRepositoryTests.cs`:

```csharp
[Fact]
public async Task GetSessionMemoryAsync_ReadsRedisThenSqliteAndIncludesUploadedKnowledge()
{
    var userId = "user123";
    var projectId = "proj456";
    var sessionId = "session789";
    _dbContext.AgentMemories.Add(new AgentMemory
    {
        Id = Guid.NewGuid().ToString(),
        UserId = userId,
        ProjectId = projectId,
        SessionId = sessionId,
        MemoryType = "session.recent_uploaded_knowledge_ids",
        MemoryKey = "recent_uploaded_knowledge_ids",
        Content = JsonSerializer.Serialize(new List<string> { "knowledge-1" })
    });
    await _dbContext.SaveChangesAsync();

    _mockMemoryCache.Setup(x => x.GetOrSetAsync(It.IsAny<string>(), It.IsAny<Func<Task<SessionMemory>>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string _, Func<Task<SessionMemory>> f, TimeSpan _, CancellationToken _) => f().Result);
    _mockRedisCache.Setup(x => x.GetAsync<SessionMemory>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((SessionMemory?)null);

    var result = await _repository.GetSessionMemoryAsync(userId, projectId, sessionId);

    Assert.Contains("knowledge-1", result.RecentUploadedKnowledgeIds);
}
```

- [ ] **Step 2: Add failing no-MemoryUpdate persistence test**

Replace `PersistAsync_WithNoMemoryUpdate_DoesNotWriteRepository` expectation in `AgentMemoryServiceTests.cs` with a new test:

```csharp
[Fact]
public async Task PersistAsync_WithNoMemoryUpdate_PersistsRuleBasedExecutionAndAuthorChanges()
{
    var repository = new RecordingMemoryRepository();
    var service = new AgentMemoryService(repository, NullLogger<AgentMemoryService>.Instance);
    var session = new AgentSession { UserId = "user-1" };
    session.WorkingMemory.ExecutionMemory.RepeatedBlockers.Add("ValidateChapterDraft 失败：节奏拖慢");
    session.WorkingMemory.AuthorMemory.StyleDislikes.Add("文风重复");

    await service.PersistAsync(session, new NovelProjectInfo { Id = "project-1" }, new StoryBibleDocument(), new AgentReflection
    {
        QualityGate = new AgentQualityGate { Status = "fail", RewriteDecision = "节奏拖慢" }
    });

    Assert.True(repository.ProjectUpdates.ContainsKey("execution.repeated_blockers"));
    Assert.True(repository.AuthorUpdates.ContainsKey("author.style_dislikes"));
}
```

- [ ] **Step 3: Run tests to verify failure**

```bash
dotnet test Tests/Unit/Unit.csproj --filter "FullyQualifiedName~AgentMemoryRepositoryTests.GetSessionMemoryAsync|FullyQualifiedName~AgentMemoryServiceTests.PersistAsync_WithNoMemoryUpdate"
```

Expected: FAIL due missing `SessionMemory` and current early return.

- [ ] **Step 4: Extend memory models**

In `IAgentMemoryRepository.cs`, add:

```csharp
Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default);
```

Add model properties:

```csharp
public class SessionMemory
{
    public string CurrentGoal { get; set; } = string.Empty;
    public List<string> OpenQuestions { get; set; } = new();
    public List<string> ShortTermPreferences { get; set; } = new();
    public List<string> RecentObservations { get; set; } = new();
    public List<string> RecentUploadedKnowledgeIds { get; set; } = new();
    public string? PendingToolName { get; set; }
    public string? LastIntent { get; set; }
}
```

Extend:

```csharp
ProjectMemory.ImportedKnowledgeIds
ProjectMemory.KnowledgeInventory
ExecutionMemory.KnowledgeProcessingFailures
```

Use a small DTO:

```csharp
public class KnowledgeInventoryItem
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Weight { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ProjectUsageStatus { get; set; } = "imported";
    public int ProjectUsageCount { get; set; }
    public DateTime? ProjectLastUsedAt { get; set; }
}
```

- [ ] **Step 5: Implement SessionMemory repository path**

In `AgentMemoryRepository`, implement `GetSessionMemoryAsync` with MemoryCache -> Redis -> SQLite. Use key:

```csharp
var cacheKey = $"memory:session:{userId}:{sessionId}:{projectId}";
```

Load rows where `MemoryType.StartsWith("session.")` and `SessionId == sessionId`.

- [ ] **Step 6: Persist rule-based changes without MemoryUpdate**

In `AgentMemoryService.PersistAsync`, remove the early return that exits before persisting `ApplyReflection` changes. Build `updates` and `authorUpdates` from current working memory even when `reflection.MissionPatch.MemoryUpdate == null`.

Keep explicit `MemoryUpdate` as the richer path, but always persist non-empty changes in:

```csharp
execution.repeated_blockers
execution.successful_repairs
author.style_dislikes
project.referenced_knowledge_ids
project.used_trope_patterns
```

- [ ] **Step 7: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter "FullyQualifiedName~AgentMemoryRepositoryTests|FullyQualifiedName~AgentMemoryServiceTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Services/Memory Web/NovelAgentWeb/Support/AgentMemoryService.cs Tests/Unit/Services/Memory/AgentMemoryRepositoryTests.cs Tests/Unit/Support/AgentMemoryServiceTests.cs
git commit -m "feat(memory): close layered memory repository loop"
```

---

### Task 6: AgentMemoryContext For Prompt Injection

**Files:**
- Create: `Web/NovelAgentWeb/Services/Memory/IAgentMemoryContextService.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/AgentMemoryContextService.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/Unit/Services/Memory/AgentMemoryContextServiceTests.cs`

- [ ] **Step 1: Write failing context test**

Create `AgentMemoryContextServiceTests.cs`:

```csharp
using Moq;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryContextServiceTests
{
    [Fact]
    public async Task BuildAsync_ComposesChatSessionProjectAuthorExecutionAndToolContext()
    {
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetPromptWindowAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatPromptWindowDto("总体摘要", Array.Empty<ChatHistorySummaryDto>(), new[] { new ChatHistoryTurnDto("user", "刚上传了知识", DateTime.UtcNow) }));
        var repo = new Mock<IAgentMemoryRepository>();
        repo.Setup(x => x.GetSessionMemoryAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMemory { RecentUploadedKnowledgeIds = new List<string> { "knowledge-1" } });
        repo.Setup(x => x.GetProjectMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectMemory { ImportedKnowledgeIds = new List<string> { "knowledge-1" }, ReferencedKnowledgeIds = new List<string>() });
        repo.Setup(x => x.GetAuthorMemoryAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(new AuthorMemory());
        repo.Setup(x => x.GetExecutionMemoryAsync("user-1", "project-1", It.IsAny<CancellationToken>())).ReturnsAsync(new ExecutionMemory());

        var service = new AgentMemoryContextService(chat.Object, repo.Object);

        var context = await service.BuildAsync("user-1", "project-1", "session-1", CancellationToken.None);

        Assert.Equal("总体摘要", context.Chat.MetaSummary);
        Assert.Contains("knowledge-1", context.Session.RecentUploadedKnowledgeIds);
        Assert.Contains("knowledge-1", context.Project.ImportedKnowledgeIds);
        Assert.Empty(context.Project.ReferencedKnowledgeIds);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~AgentMemoryContextServiceTests
```

Expected: FAIL because context service does not exist.

- [ ] **Step 3: Implement context DTOs and service**

Create DTOs:

```csharp
public record AgentMemoryContextDto(ChatMemoryContext Chat, SessionMemory Session, ProjectMemory Project, AuthorMemory Author, ExecutionMemory Execution);
public record ChatMemoryContext(string? MetaSummary, IReadOnlyList<ChatHistorySummaryDto> RecentSummaries, IReadOnlyList<ChatHistoryTurnDto> RecentMessages);
```

`BuildAsync` loads chat window and all four memory repository layers.

- [ ] **Step 4: Update AgentCore observation building**

In `AgentObservationBuilder.BuildAsync`, load memory context and set:

```csharp
var memoryContext = await _memoryContextService.BuildAsync(session.UserId, project.Id, session.SessionId, ct);
```

Then use:

```csharp
RecentMessages = memoryContext.Chat.RecentMessages.Select(t => $"{t.Role}: {t.Content}").ToList(),
SessionMemory = MapSession(memoryContext.Session),
ProjectMemory = MapProject(memoryContext.Project, project.Id),
AuthorMemory = MapAuthor(memoryContext.Author),
ExecutionMemory = MapExecution(memoryContext.Execution),
```

Keep `session.WorkingMemory` synchronized after mapping so existing tools keep working.

- [ ] **Step 5: Register service**

```csharp
builder.Services.AddScoped<IAgentMemoryContextService, AgentMemoryContextService>();
```

- [ ] **Step 6: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~AgentMemoryContextServiceTests
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Web/NovelAgentWeb/Services/Memory Web/NovelAgentWeb/Support/AgentCore.cs Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Memory/AgentMemoryContextServiceTests.cs
git commit -m "feat(agent): build prompts from unified memory context"
```

---

### Task 7: Knowledge Upload, Processing, And Project-Isolated Usage Memory

**Files:**
- Create: `Web/NovelAgentWeb/Services/Knowledge/IProjectKnowledgeUsageService.cs`
- Create: `Web/NovelAgentWeb/Services/Knowledge/ProjectKnowledgeUsageService.cs`
- Modify: `Web/NovelAgentWeb/Services/Knowledge/KnowledgeService.cs`
- Modify: `Web/NovelAgentWeb/Services/Knowledge/KnowledgeProcessingService.cs`
- Modify: `Web/NovelAgentWeb/Controllers/KnowledgeController.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentToolRegistry.cs`
- Test: `Tests/Unit/Services/Knowledge/ProjectKnowledgeUsageServiceTests.cs`
- Test: `Tests/Unit/Services/Knowledge/KnowledgeMemoryFlowTests.cs`

- [ ] **Step 1: Write failing project isolation tests**

Create `ProjectKnowledgeUsageServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public class ProjectKnowledgeUsageServiceTests
{
    [Fact]
    public async Task MarkReferencedAsync_DoesNotPolluteOtherProjects()
    {
        await using var db = CreateDb();
        Seed(db);
        var events = new Mock<IAgentMemoryEventService>();
        var service = new ProjectKnowledgeUsageService(db, events.Object, NullLogger<ProjectKnowledgeUsageService>.Instance);

        await service.MarkImportedAsync("user-1", "project-b", "knowledge-1", "session-b", "upload", CancellationToken.None);
        await service.MarkReferencedAsync("user-1", "project-a", "knowledge-1", "session-a", "run-a", CancellationToken.None);

        var a = await db.ProjectKnowledgeUsages.SingleAsync(x => x.ProjectId == "project-a");
        var b = await db.ProjectKnowledgeUsages.SingleAsync(x => x.ProjectId == "project-b");

        Assert.Equal("referenced", a.Status);
        Assert.Equal(1, a.UsageCount);
        Assert.Equal("imported", b.Status);
        Assert.Equal(0, b.UsageCount);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new NovelAgentDbContext(options);
    }

    private static void Seed(NovelAgentDbContext db)
    {
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-a", UserId = "user-1", Title = "A" });
        db.NovelProjects.Add(new NovelProject { Id = "project-b", UserId = "user-1", Title = "B" });
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", ProjectId = "project-a", EntryType = "ReaderPromise", Title = "代价", Content = "胜利要有代价" });
        db.SaveChanges();
    }
}
```

- [ ] **Step 2: Run failing test**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ProjectKnowledgeUsageServiceTests
```

Expected: FAIL because service does not exist.

- [ ] **Step 3: Implement project knowledge usage service**

`IProjectKnowledgeUsageService.cs`:

```csharp
public interface IProjectKnowledgeUsageService
{
    Task MarkImportedAsync(string userId, string projectId, string knowledgeId, string? sessionId, string source, CancellationToken ct = default);
    Task MarkReferencedAsync(string userId, string projectId, string knowledgeId, string? sessionId, string? runId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectKnowledgeUsage>> ListForProjectAsync(string userId, string projectId, CancellationToken ct = default);
}
```

`MarkImportedAsync` upserts `(userId, projectId, knowledgeId)` with `Status = "imported"` only if no row exists. `MarkReferencedAsync` upserts with `Status = "referenced"`, increments `UsageCount`, and updates `LastUsedAt`.

Each method calls `IAgentMemoryEventService.AppendAsync` with `sourceType` equal to `knowledge_processed` or `knowledge_used`.

- [ ] **Step 4: Connect create/upload processing to imported memory**

In `KnowledgeService.CreateKnowledgeAsync`, after vector upsert:

```csharp
await _projectKnowledgeUsage.MarkImportedAsync(userId, knowledge.ProjectId, knowledge.Id, request.SourceFileId, knowledge.SourceType, ct);
```

In `KnowledgeProcessingService.SaveExtractedEntriesAsync`, collect created IDs and append a `knowledge_processed` event after the loop.

- [ ] **Step 5: Connect actual use to referenced memory**

In `KnowledgeService.IncrementUsageAsync`, after global `knowledge.UsageCount++`:

```csharp
await _projectKnowledgeUsage.MarkReferencedAsync(userId, knowledge.ProjectId, knowledge.Id, null, null, ct);
```

In `AgentToolRegistry.SearchDatabaseKnowledgeAsync`, after DB hits are selected, call `IncrementUsageAsync` only for results the Agent returns as usable hits. This records current project usage.

- [ ] **Step 6: Add memory fields update**

When a knowledge entry is imported, update:

```csharp
project.imported_knowledge_ids
project.knowledge_inventory
session.recent_uploaded_knowledge_ids
```

When referenced, update:

```csharp
project.referenced_knowledge_ids
```

Use `IAgentMemoryRepository.UpdateMemoryAsync` with `projectId` and `sessionId` where available.

- [ ] **Step 7: Register service**

```csharp
builder.Services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
```

- [ ] **Step 8: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter "FullyQualifiedName~ProjectKnowledgeUsageServiceTests|FullyQualifiedName~KnowledgeServiceTests"
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add Web/NovelAgentWeb/Services/Knowledge Web/NovelAgentWeb/Controllers/KnowledgeController.cs Web/NovelAgentWeb/Support/AgentToolRegistry.cs Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Knowledge
git commit -m "feat(knowledge): record project-isolated knowledge memory"
```

---

### Task 8: Tool Search Cache With Redis, SQLite Snapshot, TTL, And Memory Version

**Files:**
- Create: `Web/NovelAgentWeb/Services/AgentTools/IToolSearchCacheService.cs`
- Create: `Web/NovelAgentWeb/Services/AgentTools/ToolSearchCacheService.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentToolRegistry.cs`
- Modify: `Web/NovelAgentWeb/Support/AgentSession.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/Unit/Services/Memory/ToolSearchCacheServiceTests.cs`

- [ ] **Step 1: Write failing cache tests**

Create `ToolSearchCacheServiceTests.cs`:

```csharp
using Moq;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ToolSearchCacheServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenMemoryVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=2|execution=1");
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object);
        var session = new AgentSession { UserId = "user-1", SessionId = "session-1", ActiveProjectId = "project-1", DiscoveredPhase = "Planning", ToolSearchCacheVersion = "project=1|execution=1" };

        var result = await service.GetAsync(session, "Planning", CancellationToken.None);

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run failing test**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ToolSearchCacheServiceTests
```

Expected: FAIL because `ToolSearchCacheService` and `ToolSearchCacheVersion` do not exist.

- [ ] **Step 3: Add session cache version**

In `AgentSession`, add:

```csharp
public string? ToolSearchCacheVersion { get; set; }
```

Include it in `SerializeSessionData` and `DeserializeSession`.

- [ ] **Step 4: Implement tool cache service**

Service behavior:

```csharp
public async Task<IReadOnlyList<ToolSchema>?> GetAsync(AgentSession session, string phase, CancellationToken ct)
{
    var version = await _versions.GetCombinedVersionAsync(session.UserId, session.ActiveProjectId, session.SessionId, ct);
    if (!string.Equals(session.ToolSearchCacheVersion, version, StringComparison.Ordinal))
        return null;
    if (string.IsNullOrWhiteSpace(session.DiscoveredPhase) || session.DiscoveredTools.Count == 0)
        return null;
    if (!string.Equals(session.DiscoveredPhase, phase, StringComparison.OrdinalIgnoreCase))
        return null;
    if (session.LastToolSearchAt == null || DateTime.UtcNow - session.LastToolSearchAt.Value > TimeSpan.FromMinutes(5))
        return null;
    return session.DiscoveredTools;
}
```

`SaveAsync` writes MemoryCache, Redis, and session snapshot with the current combined version.

- [ ] **Step 5: Use service in AgentCore and ToolRegistry**

In `AgentObservationBuilder.BuildAsync`, replace direct session cache checks with `IToolSearchCacheService.GetAsync`. If null, expose only `tool_search`.

In `AgentToolRegistry.ToolSearchAsync`, after building tools, call `SaveAsync(session, phaseArg, session.DiscoveredTools, ct)`.

- [ ] **Step 6: Add runtime trace fields**

When cache is used or refreshed, add trace data:

```csharp
toolCacheStatus = "hit|miss|expired|version_mismatch";
cachedPhase = session.DiscoveredPhase;
refreshReason = reason;
```

- [ ] **Step 7: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ToolSearchCacheServiceTests
dotnet test Tests/AgentKernelRegression/AgentKernelRegression.csproj --filter SearchCreativeKnowledgeReturnsDbKnowledge
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Services/AgentTools Web/NovelAgentWeb/Support/AgentCore.cs Web/NovelAgentWeb/Support/AgentToolRegistry.cs Web/NovelAgentWeb/Support/AgentSession.cs Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Memory/ToolSearchCacheServiceTests.cs
git commit -m "feat(agent): cache tool search with redis and memory versions"
```

---

### Task 9: Content Document Layer For Long Text Truth Sources

**Files:**
- Create: `Web/NovelAgentWeb/Services/Content/IContentDocumentService.cs`
- Create: `Web/NovelAgentWeb/Services/Content/ContentDocumentService.cs`
- Modify: `Web/NovelAgentWeb/Services/Materials/MaterialService.cs`
- Modify: `Web/NovelAgentWeb/Services/Knowledge/KnowledgeProcessingService.cs`
- Modify: `Web/NovelAgentWeb/Services/Chapters/ChapterService.cs`
- Modify: `Web/NovelAgentWeb/Services/StoryBible/StoryBibleService.cs`
- Test: `Tests/Unit/Services/Content/ContentDocumentServiceTests.cs`

- [ ] **Step 1: Write failing content service test**

Create `ContentDocumentServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Content;
using Xunit;

namespace Tests.Unit.Services.Content;

public class ContentDocumentServiceTests
{
    [Fact]
    public async Task SaveTextAsync_CreatesDocumentChunksAndPendingVectorRows()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new NovelAgentDbContext(options);
        var service = new ContentDocumentService(db);

        var document = await service.SaveTextAsync("user-1", "project-1", "knowledge", "knowledge-1", "raw_upload", "素材.txt", "第一段\n\n第二段", CancellationToken.None);

        Assert.Equal("knowledge", document.SourceType);
        Assert.Equal(2, await db.ContentChunks.CountAsync());
        Assert.Equal(2, await db.ContentVectorPoints.CountAsync());
        Assert.All(await db.ContentVectorPoints.ToListAsync(), p => Assert.Equal("pending", p.IndexStatus));
    }
}
```

- [ ] **Step 2: Run failing test**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ContentDocumentServiceTests
```

Expected: FAIL because service does not exist.

- [ ] **Step 3: Implement content service contract**

Create `IContentDocumentService.cs`:

```csharp
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Content;

public interface IContentDocumentService
{
    Task<ContentDocument> SaveTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `ContentDocumentService.SaveTextAsync`**

The implementation must perform these exact operations:

1. Compute SHA256 hash for full content.
2. Create `ContentDocument`.
3. Split text into chunks by paragraph, with a maximum of 4000 characters per chunk.
4. Create `ContentChunk` rows.
5. Create `ContentVectorPoint` rows with `IndexStatus = "pending"` and `QdrantCollection = $"novel_agent_{userId}"`.

Use this chunking helper inside `ContentDocumentService`:

```csharp
private static IReadOnlyList<(string Text, int Start, int End)> SplitIntoChunks(string content)
{
    var chunks = new List<(string Text, int Start, int End)>();
    var start = 0;
    while (start < content.Length)
    {
        var length = Math.Min(4000, content.Length - start);
        var end = start + length;
        if (end < content.Length)
        {
            var paragraphBreak = content.LastIndexOf("\n\n", end - 1, length, StringComparison.Ordinal);
            if (paragraphBreak > start + 500)
            {
                end = paragraphBreak + 2;
            }
        }
        var text = content[start..end].Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            chunks.Add((text, start, end));
        }
        start = end;
    }
    return chunks;
}
```

- [ ] **Step 5: Integrate new write paths without legacy migration**

For new knowledge uploads, materials, chapters, StoryBible snapshots, and AgentRun artifacts, write content documents first and keep legacy `file_path/content_path` only as migration input or compatibility output. Old-file migration is implemented in Task 9A after this new write path is passing.

- [ ] **Step 6: Register service**

```csharp
builder.Services.AddScoped<IContentDocumentService, ContentDocumentService>();
```

- [ ] **Step 7: Run tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ContentDocumentServiceTests
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Web/NovelAgentWeb/Services/Content Web/NovelAgentWeb/Services/Materials Web/NovelAgentWeb/Services/Knowledge Web/NovelAgentWeb/Services/Chapters Web/NovelAgentWeb/Services/StoryBible Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Content/ContentDocumentServiceTests.cs
git commit -m "feat(content): add sqlite content document truth layer"
```

---

### Task 9A: Legacy File Migration Into Content Documents

**Files:**
- Create: `Web/NovelAgentWeb/Scripts/MigrateLegacyContentToSqlite.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/Unit/Services/Content/LegacyContentMigrationTests.cs`

- [ ] **Step 1: Write failing migration test**

Create `Tests/Unit/Services/Content/LegacyContentMigrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Scripts;
using TM.Web.NovelAgentWeb.Services.Content;
using Xunit;

namespace Tests.Unit.Services.Content;

public class LegacyContentMigrationTests
{
    [Fact]
    public async Task RunAsync_MigratesChapterContentPathIntoContentDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var chapterPath = Path.Combine(root, "chapter_001.md");
        await File.WriteAllTextAsync(chapterPath, "第一章正文");

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "书" });
        db.Chapters.Add(new Chapter { Id = "chapter-1", ProjectId = "project-1", Title = "第一章", ContentPath = chapterPath, ChapterNumber = 1 });
        await db.SaveChangesAsync();

        var content = new ContentDocumentService(db);
        var migrated = await MigrateLegacyContentToSqlite.RunAsync(db, content, root, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, migrated.ChaptersMigrated);
        var document = await db.ContentDocuments.SingleAsync(x => x.SourceType == "chapter" && x.SourceId == "chapter-1");
        Assert.Equal("chapter_body", document.DocumentRole);
        Assert.Equal(1, await db.ContentChunks.CountAsync(x => x.DocumentId == document.Id));
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~LegacyContentMigrationTests
```

Expected: FAIL because `MigrateLegacyContentToSqlite` does not exist.

- [ ] **Step 3: Implement migration script**

Create `Web/NovelAgentWeb/Scripts/MigrateLegacyContentToSqlite.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Scripts;

public record LegacyContentMigrationResult(int ChaptersMigrated, int MaterialsMigrated, int KnowledgeMigrated, int StoryBibleSnapshotsMigrated, int AgentRunArtifactsMigrated);

public static class MigrateLegacyContentToSqlite
{
    public static async Task<LegacyContentMigrationResult> RunAsync(
        NovelAgentDbContext db,
        IContentDocumentService content,
        string storageRoot,
        ILogger logger,
        CancellationToken ct = default)
    {
        var chapters = 0;
        foreach (var chapter in await db.Chapters.Include(c => c.Project).ToListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(chapter.ContentPath))
                continue;
            var path = Path.IsPathRooted(chapter.ContentPath)
                ? chapter.ContentPath
                : Path.Combine(storageRoot, chapter.ContentPath);
            if (!File.Exists(path))
                continue;
            var already = await db.ContentDocuments.AnyAsync(d => d.SourceType == "chapter" && d.SourceId == chapter.Id && d.DocumentRole == "chapter_body", ct);
            if (already)
                continue;
            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(chapter.Project.UserId, chapter.ProjectId, "chapter", chapter.Id, "chapter_body", chapter.Title, text, ct);
            chapters++;
        }

        var materials = 0;
        foreach (var material in await db.Materials.Include(m => m.Project).ToListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(material.FilePath))
                continue;
            var path = Path.IsPathRooted(material.FilePath)
                ? material.FilePath
                : Path.Combine(storageRoot, material.FilePath);
            if (!File.Exists(path))
                continue;
            var already = await db.ContentDocuments.AnyAsync(d => d.SourceType == "material" && d.SourceId == material.Id && d.DocumentRole == "material_raw", ct);
            if (already)
                continue;
            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(material.Project.UserId, material.ProjectId, "material", material.Id, "material_raw", material.Title, text, ct);
            materials++;
        }

        var knowledgeUploads = 0;
        foreach (var task in await db.KnowledgeProcessingTasks.Include(t => t.Project).ToListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(task.FilePath))
                continue;
            var path = Path.IsPathRooted(task.FilePath)
                ? task.FilePath
                : Path.Combine(storageRoot, task.FilePath);
            if (!File.Exists(path))
                continue;
            var already = await db.ContentDocuments.AnyAsync(d => d.SourceType == "knowledge_upload" && d.SourceId == task.Id && d.DocumentRole == "upload_raw", ct);
            if (already)
                continue;
            var text = await File.ReadAllTextAsync(path, ct);
            await content.SaveTextAsync(task.UserId, task.ProjectId, "knowledge_upload", task.Id, "upload_raw", task.FileName, text, ct);
            knowledgeUploads++;
        }

        var storyBibleSnapshots = 0;
        foreach (var storyBible in await db.StoryBibles.Include(s => s.Project).ToListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(storyBible.ContentJson))
                continue;
            var already = await db.ContentDocuments.AnyAsync(d => d.SourceType == "story_bible" && d.SourceId == storyBible.Id && d.DocumentRole == "snapshot", ct);
            if (already)
                continue;
            await content.SaveTextAsync(storyBible.Project.UserId, storyBible.ProjectId, "story_bible", storyBible.Id, "snapshot", storyBible.Title, storyBible.ContentJson, ct);
            storyBibleSnapshots++;
        }

        var agentRunArtifacts = 0;
        foreach (var run in await db.AgentRuns.ToListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(run.OutputData) || run.OutputData.Length < 512)
                continue;
            var already = await db.ContentDocuments.AnyAsync(d => d.SourceType == "agent_run" && d.SourceId == run.Id && d.DocumentRole == "artifact", ct);
            if (already)
                continue;
            await content.SaveTextAsync(run.UserId, run.ProjectId, "agent_run", run.Id, "artifact", run.TaskType, run.OutputData, ct);
            agentRunArtifacts++;
        }

        logger.LogInformation(
            "Migrated legacy content into content_documents: chapters={ChapterCount}, materials={MaterialCount}, knowledgeUploads={KnowledgeUploadCount}, storyBibleSnapshots={StoryBibleSnapshotCount}, agentRunArtifacts={AgentRunArtifactCount}",
            chapters,
            materials,
            knowledgeUploads,
            storyBibleSnapshots,
            agentRunArtifacts);

        return new LegacyContentMigrationResult(chapters, materials, knowledgeUploads, storyBibleSnapshots, agentRunArtifacts);
    }
}
```

The migration deliberately reads legacy file paths only as input. Runtime reads after this task must use `content_documents` and `content_chunks`.

- [ ] **Step 4: Wire an explicit migration switch**

In `Program.cs`, add after database migration startup code:

```csharp
if (args.Contains("--migrate-legacy-content", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
    var content = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MigrateLegacyContentToSqlite");
    var storageRoot = app.Configuration["NovelAgent:StorageRoot"] ?? "App_Data";
    await MigrateLegacyContentToSqlite.RunAsync(db, content, storageRoot, logger);
    return;
}
```

- [ ] **Step 5: Run migration tests**

```bash
dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~LegacyContentMigrationTests
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Web/NovelAgentWeb/Scripts/MigrateLegacyContentToSqlite.cs Web/NovelAgentWeb/Program.cs Tests/Unit/Services/Content/LegacyContentMigrationTests.cs
git commit -m "feat(migration): move legacy content into sqlite documents"
```

---

### Task 10: API Contracts, Knowledge Routes, And Frontend Port

**Files:**
- Modify: `Web/NovelAgentWeb/Controllers/KnowledgeController.cs`
- Modify: `Web/NovelAgentWeb/DTOs/KnowledgeDTOs.cs`
- Modify: `Web/NovelAgentWeb.Frontend/src/api/client.ts`
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts`
- Modify: `Web/NovelAgentWeb.Frontend/vite.config.ts`
- Test: `Tests/NovelAgentRegression/E2E/UserJourneyTests.cs`

- [ ] **Step 1: Add API response fields for project usage**

Extend `KnowledgeResponse`:

```csharp
public string ProjectUsageStatus { get; set; } = "none";
public int ProjectUsageCount { get; set; }
public DateTime? ProjectLastUsedAt { get; set; }
```

Map from `ProjectKnowledgeUsage` in `KnowledgeService.ListKnowledgeAsync`, `GetKnowledgeAsync`, and `SearchKnowledgeAsync`.

- [ ] **Step 2: Align frontend base URL**

In `Web/NovelAgentWeb.Frontend/src/api/client.ts`, keep env override but set development default:

```ts
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5002/api';
```

In `vite.config.ts`, set:

```ts
server: {
  port: 3002,
  strictPort: true
}
```

- [ ] **Step 3: Add upload task APIs**

In frontend `api/index.ts`, add:

```ts
export const uploadKnowledgeFile = (projectId: string, file: File, title?: string) => {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('projectId', projectId);
  if (title) formData.append('title', title);
  return api<{ taskId: string; status: string; message: string }>('/knowledge/upload', { method: 'POST', body: formData });
};

export const getKnowledgeTask = (taskId: string) =>
  get<{ id: string; status: string; progress: number; extractedEntriesCount: number; errorMessage?: string }>(`/knowledge/tasks/${encodeURIComponent(taskId)}`);
```

- [ ] **Step 4: Run builds**

```bash
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
cd Web/NovelAgentWeb.Frontend && npm run build
```

Expected: backend build PASS; frontend build PASS and syncs to `Web/NovelAgentWeb/wwwroot`.

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb/Controllers/KnowledgeController.cs Web/NovelAgentWeb/DTOs/KnowledgeDTOs.cs Web/NovelAgentWeb.Frontend/src/api Web/NovelAgentWeb.Frontend/vite.config.ts Web/NovelAgentWeb/wwwroot
git commit -m "feat(api): expose knowledge usage and align frontend port"
```

---

### Task 11: Frontend Project Context And Real Knowledge/Agent Flow

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts`
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx`
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx`
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx`
- Modify: `Web/NovelAgentWeb.Frontend/src/services/projectService.ts`

- [ ] **Step 1: Confirm single project store contract**

`useProjectStore` must expose:

```ts
currentProjectId: string | null;
currentProject: NovelProjectInfo | null;
setCurrentProject(project: NovelProjectInfo | null): void;
ensureProjectSelected(projects: NovelProjectInfo[]): void;
```

All pages must read project id from this store, not a local long-lived `selectedProjectId`.

- [ ] **Step 2: Replace local project state in pages**

In `MaterialsPage`, `WorkflowPage`, and `AgentPage`, replace page-local selected project ids with:

```ts
const { currentProjectId, setCurrentProject } = useProjectStore();
```

Route all material/knowledge/workflow calls through `currentProjectId`.

- [ ] **Step 3: Show real task and usage state**

In `MaterialsPage`, render these backend fields in every knowledge/material row:

```ts
entry.projectUsageStatus
entry.projectUsageCount
entry.projectLastUsedAt
```

Use labels:

```ts
const usageLabel = entry.projectUsageStatus === 'referenced'
  ? '本项目已引用'
  : entry.projectUsageStatus === 'imported'
    ? '本项目已导入'
    : '未用于本项目';
```

- [ ] **Step 4: Build frontend**

```bash
cd Web/NovelAgentWeb.Frontend
npm run build
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb.Frontend/src Web/NovelAgentWeb/wwwroot
git commit -m "feat(frontend): unify project context and knowledge usage UI"
```

---

### Task 12: End-To-End Regression And Local Runtime Verification

**Files:**
- Modify: `Tests/AgentKernelRegression/Program.cs`
- Modify: `Tests/NovelAgentRegression/E2E/UserJourneyTests.cs`
- Create: `Docs/superpowers/plans/2026-06-13-original-design-full-implementation-test-report.md`

- [ ] **Step 1: Add regression scenario**

Add an AgentKernel regression named:

```csharp
("Knowledge usage remains project-scoped", KnowledgeUsageRemainsProjectScoped)
```

Test behavior:

1. Create two projects for same user.
2. Create one knowledge row.
3. Mark it referenced in project A.
4. Mark it imported in project B.
5. Assert project A memory context shows referenced.
6. Assert project B memory context does not show referenced.

- [ ] **Step 2: Run regression**

```bash
dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj
```

Expected: all existing checks plus new scenario pass.

- [ ] **Step 3: Run backend and frontend builds**

```bash
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
dotnet test Tests/Unit/Unit.csproj
dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj
cd Web/NovelAgentWeb.Frontend && npm run build
```

Expected: all PASS.

- [ ] **Step 4: Verify local services**

Start dependencies:

```bash
docker compose up -d redis qdrant
```

Start backend:

```bash
dotnet run --project Web/NovelAgentWeb/NovelAgentWeb.csproj --urls http://localhost:5002
```

Start frontend:

```bash
cd Web/NovelAgentWeb.Frontend
npm run dev -- --host 127.0.0.1 --port 3002
```

Verify:

```bash
curl http://localhost:5002/health
curl http://localhost:6333/health
redis-cli -h localhost -p 6379 ping
```

Expected:

```text
PONG
```

Backend health expected JSON must contain Redis and Qdrant entries with either `Healthy` or `Degraded` status. When a dependency is degraded, its entry must include a non-empty reason:

```json
{
  "status": "Healthy",
  "entries": {
    "redis": { "status": "Healthy" },
    "qdrant": { "status": "Healthy" }
  }
}
```

- [ ] **Step 5: Write test report**

Create `Docs/superpowers/plans/2026-06-13-original-design-full-implementation-test-report.md` with:

```markdown
# Original Design Full Implementation Test Report

## Commands

- `dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj`
- `dotnet test Tests/Unit/Unit.csproj`
- `dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj`
- `dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj`
- `npm run build`

## Results

Record PASS/FAIL for each command with the date, commit hash, and any remaining degraded dependency.

## Manual Flow

- Frontend: http://localhost:3002
- Backend: http://localhost:5002
- Redis: localhost:6379
- Qdrant HTTP: localhost:6333
- Qdrant gRPC: localhost:6334

Flow checked:
Login -> create project -> upload knowledge -> process knowledge -> Agent search knowledge -> generate/plan -> memory persistence -> project-isolated knowledge usage.
```

- [ ] **Step 6: Commit**

```bash
git add Tests/AgentKernelRegression/Program.cs Tests/NovelAgentRegression/E2E/UserJourneyTests.cs Docs/superpowers/plans/2026-06-13-original-design-full-implementation-test-report.md
git commit -m "test(regression): verify full original design repair"
```

---

## Final Verification Checklist

Run after all tasks:

```bash
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
dotnet test Tests/Unit/Unit.csproj
dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj
dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj
cd Web/NovelAgentWeb.Frontend && npm run build
```

Expected final state:

- Frontend uses `http://localhost:3002`.
- Backend uses `http://localhost:5002`.
- Redis runs on `6379` and is required for standard local/dev runtime.
- Qdrant HTTP is `6333`; gRPC is `6334`.
- Chat turns persist to SQLite and Redis hot cache every turn.
- Prompt context comes from `AgentMemoryContext`.
- SessionMemory, ProjectMemory, AuthorMemory, ExecutionMemory persist through Redis + SQLite and recover next turn.
- Uploaded knowledge becomes current-session and project inventory memory.
- Referenced knowledge is project-scoped and cannot leak across projects.
- `tool_search` cache uses MemoryCache + Redis + SQLite snapshot, TTL, and memory version invalidation.
- Knowledge, materials, workflow, and agent UI use the single project store and real API data.

## Plan Self-Review

Spec coverage:

- Ports and Redis required runtime: Task 1.
- SQLite/Qdrant/Redis storage architecture: Tasks 2, 3, 9.
- Legacy file migration into SQLite content documents: Task 9A.
- Unified ChatHistory and memory pipeline: Tasks 3, 4, 5, 6.
- Knowledge upload, processing, project-isolated usage memory: Task 7 and Task 12.
- `tool_search` cache and ExecutionMemory invalidation: Task 8.
- API/frontend consistency: Tasks 10 and 11.
- End-to-end evidence: Task 12.

All approved spec requirements have a task above. During execution, behavior is governed by the tests and acceptance criteria in this plan; if a current local type has a slightly different name, update the implementation and tests together in that task while preserving the same observable behavior.
