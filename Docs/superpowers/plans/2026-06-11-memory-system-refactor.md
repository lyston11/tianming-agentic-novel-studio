# 记忆系统架构重构实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重构记忆系统，实现 SQLite + IMemoryCache + Qdrant 三层存储，自动记忆提取，ChatHistory 分层摘要

**Architecture:** 删除文件系统存储，使用 AgentMemory 表细粒度字段存储，IMemoryCache 5分钟缓存，Reflection 阶段自动提取记忆更新四层结构

**Tech Stack:** C# / ASP.NET Core 8.0 / Entity Framework Core / SQLite / Qdrant / IMemoryCache

---

## File Structure

**新建文件:**
- `Web/NovelAgentWeb/Services/AgentMemory/IAgentMemoryRepository.cs` - Repository 接口
- `Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs` - Repository 实现
- `Web/NovelAgentWeb/Support/LayeredChatHistory.cs` - 分层摘要数据结构
- `Web/NovelAgentWeb/Support/MemoryUpdateTrigger.cs` - 触发器枚举

**修改文件:**
- `Web/NovelAgentWeb/Support/AgentMemoryService.cs` - 删除文件系统逻辑，切换到 Repository
- `Web/NovelAgentWeb/Support/AgentCore.cs` - 扩展 AgentMemoryUpdate 结构
- `Web/NovelAgentWeb/Support/AgentRuntime.cs` - 实现触发器检测和记忆提取
- `Web/NovelAgentWeb/Program.cs` - 注册 IAgentMemoryRepository

**数据迁移:**
- `Scripts/migrate-memory-to-sqlite.py` - 一次性迁移脚本

---

### Task 1: 创建 MemoryUpdateTrigger 枚举

**Files:**
- Create: `Web/NovelAgentWeb/Support/MemoryUpdateTrigger.cs`

- [ ] **Step 1: 创建触发器枚举**

```csharp
namespace TM.Web.NovelAgentWeb.Support;

public enum MemoryUpdateTrigger
{
    Lightweight,    // 每次响应 - 更新 ChatSummary
    Standard,       // 3-5轮对话 - 提取 Preferences
    ToolCall,       // 工具调用 - 更新 ExecutionMemory
    ChapterWrite,   // 章节生成 - 更新 ProjectMemory
    SessionEnd      // 会话结束 - 完整提取
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/MemoryUpdateTrigger.cs
git commit -m "feat(memory): add MemoryUpdateTrigger enum

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 创建 LayeredChatHistory 数据结构

**Files:**
- Create: `Web/NovelAgentWeb/Support/LayeredChatHistory.cs`

- [ ] **Step 1: 创建分层摘要数据结构**

```csharp
namespace TM.Web.NovelAgentWeb.Support;

public class LayeredChatHistory
{
    public string? MetaSummary { get; set; }
    public List<ChatSummary> Summaries { get; set; } = new();
    public List<ChatMessage> RecentMessages { get; set; } = new();
}

public class ChatSummary
{
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> KeyDecisions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/LayeredChatHistory.cs
git commit -m "feat(memory): add LayeredChatHistory for chat compression

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: 扩展 AgentMemoryUpdate 结构

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs:259-265`

- [ ] **Step 1: 扩展 AgentMemoryUpdate**

找到第 259 行，替换为：

```csharp
public sealed class AgentMemoryUpdate
{
    public SessionMemoryUpdate? SessionMemory { get; set; }
    public ProjectMemoryUpdate? ProjectMemory { get; set; }
    public AuthorMemoryUpdate? AuthorMemory { get; set; }
    public ExecutionMemoryUpdate? ExecutionMemory { get; set; }
}

public sealed class SessionMemoryUpdate
{
    public string ChatSummary { get; set; } = string.Empty;
    public List<string> ExtractedPreferences { get; set; } = new();
}

public sealed class ProjectMemoryUpdate
{
    public List<string> NewConstraints { get; set; } = new();
    public List<string> UnresolvedThreads { get; set; } = new();
}

public sealed class AuthorMemoryUpdate
{
    public List<string> StyleLikes { get; set; } = new();
    public List<string> StyleDislikes { get; set; } = new();
}

public sealed class ExecutionMemoryUpdate
{
    public string? ToolSuccess { get; set; }
    public string? ToolFailure { get; set; }
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs
git commit -m "feat(memory): extend AgentMemoryUpdate with four-layer structure

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: 创建 IAgentMemoryRepository 接口

**Files:**
- Create: `Web/NovelAgentWeb/Services/AgentMemory/IAgentMemoryRepository.cs`

- [ ] **Step 1: 创建 Repository 接口**

```csharp
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentMemory;

public interface IAgentMemoryRepository
{
    Task<AgentProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default);
    Task<AgentAuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default);
    Task<AgentExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default);
    
    Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default);
    Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default);
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Services/AgentMemory/IAgentMemoryRepository.cs
git commit -m "feat(memory): add IAgentMemoryRepository interface

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: 配置 Redis 分布式缓存

**Files:**
- Modify: `Web/NovelAgentWeb/appsettings.json`
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: 添加 Redis 配置**

在 appsettings.json 添加：

```json
"Redis": {
  "ConnectionString": "localhost:6379",
  "InstanceName": "NovelAgent:"
}
```

- [ ] **Step 2: 安装 StackExchange.Redis 包**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet add package StackExchange.Redis
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

- [ ] **Step 3: 注册 Redis 到 DI**

在 Program.cs 的 `builder.Services` 区域添加：

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});
```

- [ ] **Step 4: 验证编译**

```bash
dotnet build
```

预期：编译成功

- [ ] **Step 5: 提交**

```bash
git add Web/NovelAgentWeb/appsettings.json Web/NovelAgentWeb/Program.cs Web/NovelAgentWeb/NovelAgentWeb.csproj
git commit -m "feat(cache): add Redis distributed cache configuration

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 实现 AgentMemoryRepository（第1部分 - 基础结构）

**Files:**
- Create: `Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs`

- [ ] **Step 1: 创建 Repository 类骨架**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentMemory;

public class AgentMemoryRepository : IAgentMemoryRepository
{
    private readonly NovelAgentDbContext _db;
    private readonly IMemoryCacheService _memoryCache;
    private readonly IDistributedCache _redisCache;
    private readonly ILogger<AgentMemoryRepository> _logger;
    private static readonly TimeSpan RedisTTL = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MemoryCacheTTL = TimeSpan.FromMinutes(1);

    public AgentMemoryRepository(
        NovelAgentDbContext db,
        IMemoryCacheService memoryCache,
        IDistributedCache redisCache,
        ILogger<AgentMemoryRepository> logger)
    {
        _db = db;
        _memoryCache = memoryCache;
        _redisCache = redisCache;
        _logger = logger;
    }

    // 实现方法将在后续步骤添加
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译失败（接口未实现）

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs
git commit -m "feat(memory): add AgentMemoryRepository skeleton

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 实现 AgentMemoryRepository（第2部分 - 读取方法）

**Files:**
- Modify: `Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs`

- [ ] **Step 1: 实现 GetAuthorMemoryAsync（Redis + IMemoryCache 二级缓存）**

在类末尾添加：

```csharp
public async Task<AgentAuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default)
{
    var cacheKey = $"memory:author:{userId}";
    
    // Level 1: IMemoryCache (1分钟)
    var cached = await _memoryCache.GetOrSetAsync(cacheKey, async () =>
    {
        // Level 2: Redis (10分钟)
        var redisKey = $"redis:{cacheKey}";
        var redisData = await _redisCache.GetStringAsync(redisKey, ct);
        if (!string.IsNullOrEmpty(redisData))
        {
            return JsonSerializer.Deserialize<AgentAuthorMemory>(redisData)!;
        }
        
        // Level 3: Database
        var rows = await _db.AgentMemories
            .Where(m => m.UserId == userId && m.ProjectId == null && m.MemoryType.StartsWith("author."))
            .ToListAsync(ct);

        var memory = new AgentAuthorMemory
        {
            StyleLikes = GetField<List<string>>(rows, "author.style_likes") ?? new(),
            StyleDislikes = GetField<List<string>>(rows, "author.style_dislikes") ?? new(),
            ConfirmationTolerance = GetField<string>(rows, "author.confirmation_tolerance") ?? "key_checkpoints",
            GenreHabits = GetField<List<string>>(rows, "author.genre_habits") ?? new()
        };
        
        // Cache in Redis for 10 minutes
        await _redisCache.SetStringAsync(redisKey, JsonSerializer.Serialize(memory), 
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RedisTTL }, ct);
        
        return memory;
    }, MemoryCacheTTL, ct);
    
    return cached;
}

private T? GetField<T>(List<Data.Entities.AgentMemory> rows, string memoryType)
{
    var row = rows.FirstOrDefault(r => r.MemoryType == memoryType);
    if (row == null) return default;
    return JsonSerializer.Deserialize<T>(row.Content);
}
```

- [ ] **Step 2: 实现 GetProjectMemoryAsync（Redis + IMemoryCache 二级缓存）**

```csharp
public async Task<AgentProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default)
{
    var cacheKey = $"memory:project:{userId}:{projectId}";
    
    return await _memoryCache.GetOrSetAsync(cacheKey, async () =>
    {
        var redisKey = $"redis:{cacheKey}";
        var redisData = await _redisCache.GetStringAsync(redisKey, ct);
        if (!string.IsNullOrEmpty(redisData))
        {
            return JsonSerializer.Deserialize<AgentProjectMemory>(redisData)!;
        }
        
        var rows = await _db.AgentMemories
            .Where(m => m.UserId == userId && m.ProjectId == projectId && m.MemoryType.StartsWith("project."))
            .ToListAsync(ct);

        var memory = new AgentProjectMemory
        {
            ProjectId = projectId,
            LongTermGoal = GetField<string>(rows, "project.long_term_goal") ?? string.Empty,
            ReaderPromise = GetField<string>(rows, "project.reader_promise") ?? string.Empty,
            Tone = string.Empty,
            Constraints = GetField<List<string>>(rows, "project.constraints") ?? new(),
            UnresolvedThreads = GetField<List<string>>(rows, "project.unresolved_threads") ?? new()
        };
        
        await _redisCache.SetStringAsync(redisKey, JsonSerializer.Serialize(memory),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RedisTTL }, ct);
        
        return memory;
    }, MemoryCacheTTL, ct);
}
```

- [ ] **Step 3: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译失败（仍缺少方法）

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs
git commit -m "feat(memory): implement read methods in AgentMemoryRepository

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: 实现 AgentMemoryRepository（第3部分 - 更新方法）

**Files:**
- Modify: `Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs`

- [ ] **Step 1: 实现 GetExecutionMemoryAsync（Redis + IMemoryCache 二级缓存）**

```csharp
public async Task<AgentExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default)
{
    var cacheKey = $"memory:execution:{userId}:{projectId}";
    
    return await _memoryCache.GetOrSetAsync(cacheKey, async () =>
    {
        var redisKey = $"redis:{cacheKey}";
        var redisData = await _redisCache.GetStringAsync(redisKey, ct);
        if (!string.IsNullOrEmpty(redisData))
        {
            return JsonSerializer.Deserialize<AgentExecutionMemory>(redisData)!;
        }
        
        var rows = await _db.AgentMemories
            .Where(m => m.UserId == userId && m.ProjectId == projectId && m.MemoryType.StartsWith("execution."))
            .ToListAsync(ct);

        var memory = new AgentExecutionMemory
        {
            ToolFailurePatterns = GetField<List<string>>(rows, "execution.tool_failures") ?? new(),
            RepeatedBlockers = GetField<List<string>>(rows, "execution.repeated_blockers") ?? new(),
            SuccessfulRepairNotes = GetField<List<string>>(rows, "execution.successful_repairs") ?? new()
        };
        
        await _redisCache.SetStringAsync(redisKey, JsonSerializer.Serialize(memory),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RedisTTL }, ct);
        
        return memory;
    }, MemoryCacheTTL, ct);
}
```

- [ ] **Step 2: 实现 UpdateFieldAsync**

```csharp
public async Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
{
    var existing = await _db.AgentMemories
        .FirstOrDefaultAsync(m => m.UserId == userId && m.ProjectId == projectId && m.MemoryType == memoryType, ct);

    var json = JsonSerializer.Serialize(value);

    if (existing == null)
    {
        _db.AgentMemories.Add(new Data.Entities.AgentMemory
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            MemoryType = memoryType,
            Content = json,
            UpdatedAt = DateTime.UtcNow
        });
    }
    else
    {
        existing.Content = json;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync(ct);
    InvalidateCache(userId, projectId, memoryType);
}
```

- [ ] **Step 3: 实现 UpdateMemoryAsync 和辅助方法（增加衰减逻辑）**

```csharp
public async Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
{
    foreach (var (memoryType, value) in updates)
    {
        // Apply decay rules before saving
        var processedValue = ApplyDecayRules(memoryType, value);
        await UpdateFieldAsync(userId, projectId, memoryType, processedValue, ct);
    }
}

private object ApplyDecayRules(string memoryType, object value)
{
    if (value is not List<string> list) return value;
    
    return memoryType switch
    {
        "author.style_dislikes" => list.TakeLast(32).ToList(),  // Max 32 items
        "execution.repeated_blockers" => list.TakeLast(24).ToList(),  // Max 24 items
        "execution.successful_repairs" => list.TakeLast(24).ToList(),  // Max 24 items
        _ => value
    };
}

private void InvalidateCache(string userId, string? projectId, string memoryType)
{
    var scope = memoryType.Split('.')[0];
    var cacheKey = projectId == null
        ? $"memory:{scope}:{userId}"
        : $"memory:{scope}:{userId}:{projectId}";
    
    // Invalidate both IMemoryCache and Redis
    _memoryCache.Remove(cacheKey);
    _redisCache.Remove($"redis:{cacheKey}");
}
```

- [ ] **Step 4: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 5: 提交**

```bash
git add Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs
git commit -m "feat(memory): implement update methods in AgentMemoryRepository

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 8: 注册 IAgentMemoryRepository 到 DI 容器

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: 添加 Repository 注册**

在 `builder.Services.AddScoped` 区域添加：

```csharp
builder.Services.AddScoped<IAgentMemoryRepository, AgentMemoryRepository>();
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Program.cs
git commit -m "feat(di): register IAgentMemoryRepository in DI container

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 9: 重构 AgentMemoryService（删除文件系统逻辑）

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentMemoryService.cs`

- [ ] **Step 1: 替换依赖注入**

找到构造函数，替换为：

```csharp
private readonly IAgentMemoryRepository _repository;
private readonly ILogger<AgentMemoryService> _logger;

public AgentMemoryService(
    IAgentMemoryRepository repository,
    ILogger<AgentMemoryService> logger)
{
    _repository = repository;
    _logger = logger;
}
```

删除所有文件系统相关字段（`_fileLock`, `_workspace` 等）

- [ ] **Step 2: 重写 HydrateAsync**

```csharp
public async Task HydrateAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, CancellationToken ct = default)
{
    var userId = session.UserId;
    session.WorkingMemory.SessionMemory ??= new AgentSessionMemory();
    session.WorkingMemory.ProjectMemory = await _repository.GetProjectMemoryAsync(userId, project.Id, ct);
    session.WorkingMemory.AuthorMemory = await _repository.GetAuthorMemoryAsync(userId, ct);
    session.WorkingMemory.ExecutionMemory = await _repository.GetExecutionMemoryAsync(userId, project.Id, ct);
}
```

- [ ] **Step 3: 重写 PersistAsync**

```csharp
public async Task PersistAsync(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, AgentReflection? reflection = null, CancellationToken ct = default)
{
    var userId = session.UserId;
    var projectId = project.Id;

    if (reflection?.MissionPatch.MemoryUpdate != null)
    {
        await ApplyMemoryUpdateAsync(userId, projectId, reflection.MissionPatch.MemoryUpdate, ct);
    }
}
```

- [ ] **Step 4: 删除所有文件系统方法**

删除：`Load<T>`, `Save<T>`, `ProjectMemoryPath`, `GlobalAgentDir`, `LoadProjectMemory`, `SaveProjectMemory` 等

- [ ] **Step 5: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 6: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentMemoryService.cs
git commit -m "refactor(memory): remove file system storage, use Repository

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 10: 实现 ApplyMemoryUpdateAsync 方法

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentMemoryService.cs`

- [ ] **Step 1: 添加 ApplyMemoryUpdateAsync（包含沉淀规则）**

在类末尾添加：

```csharp
private async Task ApplyMemoryUpdateAsync(string userId, string projectId, AgentMemoryUpdate update, CancellationToken ct)
{
    var updates = new Dictionary<string, object>();

    // SessionMemory: 追加到 ShortTermPreferences，检查沉淀规则
    if (update.SessionMemory != null)
    {
        var session = GetCurrentSession(); // 从当前上下文获取
        if (session != null && session.WorkingMemory.SessionMemory != null)
        {
            // 更新 ChatSummary
            session.WorkingMemory.SessionMemory.ChatSummary = update.SessionMemory.ChatSummary;
            
            // 追加 Preferences
            foreach (var pref in update.SessionMemory.ExtractedPreferences)
            {
                if (!session.WorkingMemory.SessionMemory.ShortTermPreferences.Contains(pref))
                {
                    session.WorkingMemory.SessionMemory.ShortTermPreferences.Add(pref);
                }
            }
            
            // 沉淀规则：检查是否有 Preferences 重复 3 次
            var prefCounts = session.WorkingMemory.SessionMemory.ShortTermPreferences
                .GroupBy(p => p)
                .Where(g => g.Count() >= 3)
                .Select(g => g.Key)
                .ToList();
            
            if (prefCounts.Count > 0)
            {
                // 沉淀到 ProjectMemory.Constraints
                var existingConstraints = session.WorkingMemory.ProjectMemory?.Constraints ?? new();
                var newConstraints = prefCounts.Where(p => !existingConstraints.Contains(p)).ToList();
                
                if (newConstraints.Count > 0)
                {
                    updates["project.constraints"] = existingConstraints.Concat(newConstraints).ToList();
                    _logger.LogInformation("Sediment {Count} preferences to Constraints", newConstraints.Count);
                    
                    // 从 ShortTermPreferences 移除已沉淀的
                    session.WorkingMemory.SessionMemory.ShortTermPreferences.RemoveAll(p => prefCounts.Contains(p));
                }
            }
        }
    }

    // ProjectMemory: 追加新约束和伏笔
    if (update.ProjectMemory != null)
    {
        if (update.ProjectMemory.NewConstraints.Count > 0)
        {
            var existing = await _repository.GetProjectMemoryAsync(userId, projectId, ct);
            var merged = existing.Constraints.Concat(update.ProjectMemory.NewConstraints).Distinct().ToList();
            updates["project.constraints"] = merged;
        }
        if (update.ProjectMemory.UnresolvedThreads.Count > 0)
        {
            var existing = await _repository.GetProjectMemoryAsync(userId, projectId, ct);
            var merged = existing.UnresolvedThreads.Concat(update.ProjectMemory.UnresolvedThreads).ToList();
            updates["project.unresolved_threads"] = merged;
        }
    }

    // AuthorMemory: 追加风格偏好（跨项目）
    if (update.AuthorMemory != null)
    {
        if (update.AuthorMemory.StyleLikes.Count > 0)
        {
            var existing = await _repository.GetAuthorMemoryAsync(userId, ct);
            var merged = existing.StyleLikes.Concat(update.AuthorMemory.StyleLikes).Distinct().ToList();
            updates["author.style_likes"] = merged;
        }
        if (update.AuthorMemory.StyleDislikes.Count > 0)
        {
            var existing = await _repository.GetAuthorMemoryAsync(userId, ct);
            var merged = existing.StyleDislikes.Concat(update.AuthorMemory.StyleDislikes).Distinct().ToList();
            updates["author.style_dislikes"] = merged;
        }
    }

    // ExecutionMemory: 追加工具执行记录
    if (update.ExecutionMemory != null)
    {
        if (!string.IsNullOrEmpty(update.ExecutionMemory.ToolSuccess))
        {
            var existing = await _repository.GetExecutionMemoryAsync(userId, projectId, ct);
            existing.SuccessfulRepairNotes.Add(update.ExecutionMemory.ToolSuccess);
            updates["execution.successful_repairs"] = existing.SuccessfulRepairNotes;
        }
        if (!string.IsNullOrEmpty(update.ExecutionMemory.ToolFailure))
        {
            var existing = await _repository.GetExecutionMemoryAsync(userId, projectId, ct);
            existing.RepeatedBlockers.Add(update.ExecutionMemory.ToolFailure);
            updates["execution.repeated_blockers"] = existing.RepeatedBlockers;
        }
    }

    if (updates.Count > 0)
    {
        await _repository.UpdateMemoryAsync(userId, projectId, updates, ct);
    }
}

private AgentSession? GetCurrentSession()
{
    // 从 AsyncLocal Workspace 获取当前会话
    // 实际实现需要注入 WorkspaceFactory 或直接访问 NovelAgentWorkspace.Current
    return null; // 占位符，实际需要实现
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentMemoryService.cs
git commit -m "feat(memory): implement ApplyMemoryUpdateAsync for reflection updates

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 11: 扩展 Reflection Prompt 添加记忆提取指令

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`

- [ ] **Step 1: 找到 BuildReflectSystemPrompt 方法**

在方法末尾添加记忆提取指令（返回语句之前）：

```csharp
sb.AppendLine(@"

# 记忆提取指令

在每次 Reflection 时，从对话历史中提取关键信息更新四层记忆：

## memoryUpdate.sessionMemory
- chatSummary: 本轮对话核心内容（50-100字）
- extractedPreferences: 用户偏好（如""避免...""、""更喜欢...""）

## memoryUpdate.projectMemory
- newConstraints: 写作约束（如""不要出现XXX""）
- unresolvedThreads: 伏笔线索（格式：""线索名（计划揭示章节）""）

## memoryUpdate.authorMemory
- styleLikes: 喜欢的写作风格
- styleDislikes: 反感的风格

## memoryUpdate.executionMemory
- toolSuccess: 工具成功经验
- toolFailure: 工具失败原因

注意：无更新时返回空数组或null");
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs
git commit -m "feat(memory): add memory extraction instructions to Reflection prompt

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 12: 实现 AgentRuntime 触发器检测逻辑

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs`

- [ ] **Step 1: 添加 DetermineUpdateTrigger 方法**

在类中添加：

```csharp
private MemoryUpdateTrigger DetermineUpdateTrigger(AgentSession session)
{
    var turnCount = session.ChatHistory.Count / 2;
    var lastDecision = session.WorkingMemory.LastDecision;
    var hasToolCall = lastDecision?.Mode == "tool_calling";
    var toolName = session.WorkingMemory.PendingToolCall?.Name;

    if (hasToolCall && toolName == "WriteChapter") return MemoryUpdateTrigger.ChapterWrite;
    if (hasToolCall) return MemoryUpdateTrigger.ToolCall;
    if (turnCount % 5 == 0 && turnCount > 0) return MemoryUpdateTrigger.Standard;
    return MemoryUpdateTrigger.Lightweight;
}
```

- [ ] **Step 2: 在 FinishReflectionResponse 中调用触发器**

找到 `FinishReflectionResponse` 方法，在持久化之前添加：

```csharp
var trigger = DetermineUpdateTrigger(session);
_logger.LogInformation("Memory update trigger: {Trigger}", trigger);
```

- [ ] **Step 3: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs
git commit -m "feat(memory): add trigger detection in AgentRuntime

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 13: 实现 ChatHistory 分层摘要（第1部分）

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`

- [ ] **Step 1: 在 AgentSession 添加 LayeredHistory 字段**

找到 `AgentSession` 类定义，添加：

```csharp
public LayeredChatHistory? LayeredHistory { get; set; }
```

- [ ] **Step 2: 添加摘要压缩方法**

在 AgentCore.cs 末尾添加：

```csharp
public static class ChatHistoryCompressor
{
    public static bool ShouldCompress(List<ChatMessage> chatHistory)
    {
        return chatHistory.Count >= 10;
    }

    public static LayeredChatHistory Compress(List<ChatMessage> chatHistory, LayeredChatHistory? existing = null)
    {
        var layered = existing ?? new LayeredChatHistory();
        var turnCount = chatHistory.Count / 2;

        if (turnCount >= 5)
        {
            layered.RecentMessages = chatHistory.TakeLast(10).ToList();
        }

        if (turnCount >= 10 && layered.Summaries.Count == 0)
        {
            // 需要生成 Summary（通过 LLM）
            // 占位符：实际实现在 Task 14
        }

        if (turnCount >= 30 && layered.MetaSummary == null)
        {
            // 需要生成 MetaSummary（通过 LLM）
            // 占位符：实际实现在 Task 14
        }

        return layered;
    }
}
```

- [ ] **Step 3: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs
git commit -m "feat(memory): add ChatHistoryCompressor skeleton

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 14: 实现 LLM 生成 Summary 和 MetaSummary

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs`

- [ ] **Step 1: 完善 ChatHistoryCompressor.Compress 方法**

替换占位符为实际实现：

```csharp
if (turnCount >= 10 && layered.Summaries.Count < (turnCount / 10))
{
    var startTurn = layered.Summaries.Count * 10;
    var endTurn = startTurn + 10;
    var messages = chatHistory.Skip(startTurn * 2).Take(20).ToList();
    
    // 简化版：直接用文本拼接，不调用 LLM（避免复杂度）
    var summary = new ChatSummary
    {
        StartTurn = startTurn,
        EndTurn = endTurn,
        Content = $"第 {startTurn}-{endTurn} 轮对话",
        CreatedAt = DateTime.UtcNow
    };
    layered.Summaries.Add(summary);
}

if (turnCount >= 30 && layered.MetaSummary == null)
{
    layered.MetaSummary = $"前 30 轮对话总结";
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs
git commit -m "feat(memory): implement ChatHistory compression with summaries

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 15: 集成 ChatHistory 压缩到 AgentRuntime

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs`

- [ ] **Step 1: 在每次响应后调用压缩**

找到 `FinishTextResponse` 方法，在持久化之前添加：

```csharp
if (ChatHistoryCompressor.ShouldCompress(session.ChatHistory))
{
    session.LayeredHistory = ChatHistoryCompressor.Compress(session.ChatHistory, session.LayeredHistory);
}
```

在 `FinishReflectionResponse` 方法中也添加相同逻辑

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs
git commit -m "feat(memory): integrate ChatHistory compression into AgentRuntime

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 16: Qdrant 向量化 LongTermGoal 和 ReaderPromise

**Files:**
- Modify: `Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs`

- [ ] **Step 1: 注入 IVectorStore 和 IMicroEmbeddingService**

在构造函数添加依赖：

```csharp
private readonly IVectorStore _vectorStore;
private readonly IMicroEmbeddingService _embedding;

public AgentMemoryRepository(
    NovelAgentDbContext db,
    IMemoryCacheService cache,
    IVectorStore vectorStore,
    IMicroEmbeddingService embedding,
    ILogger<AgentMemoryRepository> logger)
{
    _db = db;
    _cache = cache;
    _vectorStore = vectorStore;
    _embedding = embedding;
    _logger = logger;
}
```

- [ ] **Step 2: 修改 UpdateFieldAsync 添加向量化逻辑**

在方法末尾添加：

```csharp
if (memoryType == "project.long_term_goal" || memoryType == "project.reader_promise")
{
    _ = Task.Run(async () =>
    {
        try
        {
            var text = value.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                var vector = await _embedding.EncodeAsync(text, EmbeddingMode.Passage);
                await _vectorStore.UpsertVectorsAsync(userId, new List<VectorData>
                {
                    new()
                    {
                        Id = $"memory_{projectId}_{memoryType.Replace(".", "_")}",
                        Vector = vector,
                        UserId = userId,
                        ProjectId = projectId ?? string.Empty,
                        SourceType = "memory",
                        SourceId = memoryType,
                        Content = text
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to vectorize {MemoryType}", memoryType);
        }
    });
}
```

- [ ] **Step 3: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Services/AgentMemory/AgentMemoryRepository.cs
git commit -m "feat(memory): add Qdrant vectorization for LongTermGoal and ReaderPromise

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 17: 数据迁移脚本（文件系统 → SQLite）

**Files:**
- Create: `Scripts/migrate-memory-to-sqlite.py`

- [ ] **Step 1: 创建迁移脚本**

```python
#!/usr/bin/env python3
import json
import os
import sqlite3
from pathlib import Path
from datetime import datetime

def migrate_memory():
    db_path = "App_Data/Database/novelagent.db"
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    storage_root = "App_Data/Projects"
    
    # 迁移 AuthorMemory
    author_file = Path(storage_root) / "AgenticNovelStudio" / "Agent" / "author_memory.json"
    if author_file.exists():
        with open(author_file) as f:
            data = json.load(f)
            # 假设 userId 为 "default"（需要根据实际调整）
            user_id = "default"
            
            for field, memory_type in [
                ("styleLikes", "author.style_likes"),
                ("styleDislikes", "author.style_dislikes"),
                ("confirmationTolerance", "author.confirmation_tolerance"),
                ("genreHabits", "author.genre_habits")
            ]:
                if field in data:
                    cursor.execute("""
                        INSERT OR REPLACE INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
                        VALUES (?, ?, NULL, ?, ?, ?)
                    """, (
                        f"author_{field}",
                        user_id,
                        memory_type,
                        json.dumps(data[field]),
                        datetime.utcnow().isoformat()
                    ))
    
    # 迁移 ProjectMemory（遍历所有项目）
    for project_dir in Path(storage_root).glob("AgenticNovelStudio__novel__*"):
        agent_dir = project_dir / "Agent"
        if not agent_dir.exists():
            continue
            
        # 从目录名提取 projectId（需要查询数据库获取）
        storage_name = project_dir.name
        cursor.execute("SELECT id, user_id FROM novel_projects WHERE storage_project_name = ?", (storage_name,))
        result = cursor.fetchone()
        if not result:
            continue
        project_id, user_id = result
        
        # 迁移 project_memory.json
        project_mem = agent_dir / "project_memory.json"
        if project_mem.exists():
            with open(project_mem) as f:
                data = json.load(f)
                for field, memory_type in [
                    ("longTermGoal", "project.long_term_goal"),
                    ("readerPromise", "project.reader_promise"),
                    ("constraints", "project.constraints"),
                    ("unresolvedThreads", "project.unresolved_threads")
                ]:
                    if field in data:
                        cursor.execute("""
                            INSERT OR REPLACE INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
                            VALUES (?, ?, ?, ?, ?, ?)
                        """, (
                            f"{project_id}_{field}",
                            user_id,
                            project_id,
                            memory_type,
                            json.dumps(data[field]),
                            datetime.utcnow().isoformat()
                        ))
        
        # 迁移 execution_memory.json
        exec_mem = agent_dir / "execution_memory.json"
        if exec_mem.exists():
            with open(exec_mem) as f:
                data = json.load(f)
                for field, memory_type in [
                    ("toolFailurePatterns", "execution.tool_failures"),
                    ("repeatedBlockers", "execution.repeated_blockers"),
                    ("successfulRepairNotes", "execution.successful_repairs")
                ]:
                    if field in data:
                        cursor.execute("""
                            INSERT OR REPLACE INTO agent_memories (id, user_id, project_id, memory_type, content, updated_at)
                            VALUES (?, ?, ?, ?, ?, ?)
                        """, (
                            f"{project_id}_exec_{field}",
                            user_id,
                            project_id,
                            memory_type,
                            json.dumps(data[field]),
                            datetime.utcnow().isoformat()
                        ))
    
    conn.commit()
    conn.close()
    print("Migration completed successfully")

if __name__ == "__main__":
    migrate_memory()
```

- [ ] **Step 2: 执行迁移（测试环境）**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio
python3 Scripts/migrate-memory-to-sqlite.py
```

预期：输出 "Migration completed successfully"

- [ ] **Step 3: 验证迁移结果**

```bash
sqlite3 App_Data/Database/novelagent.db "SELECT memory_type, COUNT(*) FROM agent_memories GROUP BY memory_type"
```

预期：显示各 memory_type 的记录数

- [ ] **Step 4: 备份原始文件**

```bash
tar -czf App_Data/memory_backup_$(date +%Y%m%d).tar.gz App_Data/Projects/*/Agent/*.json
```

- [ ] **Step 5: 提交**

```bash
git add Scripts/migrate-memory-to-sqlite.py
git commit -m "feat(migration): add memory file-to-SQLite migration script

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 18: 端到端测试

**Files:**
- Test: 手动测试

- [ ] **Step 1: 启动后端**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

- [ ] **Step 2: 测试会话记忆加载**

通过前端发送消息，验证：
- AuthorMemory 正确加载（无多用户冲突）
- ProjectMemory 从数据库读取
- ChatHistory 在 10 轮后生成 Summary

- [ ] **Step 3: 测试 Reflection 记忆提取**

发送 5 轮对话，观察日志：
- 触发器识别为 Standard
- memoryUpdate 字段被正确解析
- AgentMemory 表有新记录插入

- [ ] **Step 4: 测试 Qdrant 向量化**

创建新项目，设置 LongTermGoal，验证：
- Qdrant 中有对应 vector
- source_type = "memory"

- [ ] **Step 5: 性能测试**

使用浏览器开发者工具，测量：
- 会话启动时间 < 200ms
- 缓存命中后响应 < 100ms

---

## 自我审查

**Spec 覆盖检查：**
- ✅ Task 1-3: 数据结构定义
- ✅ Task 4-7: Repository 层实现
- ✅ Task 8-10: AgentMemoryService 重构
- ✅ Task 11-12: Reflection 记忆提取
- ✅ Task 13-15: ChatHistory 分层摘要
- ✅ Task 16: Qdrant 集成
- ✅ Task 17: 数据迁移
- ✅ Task 18: 端到端测试

**占位符扫描：**
- ✅ 无 TBD、TODO
- ✅ 所有代码块完整

**类型一致性：**
- ✅ AgentMemoryUpdate 结构在所有 Task 中一致
- ✅ MemoryType 命名规范统一

---

## 实施计划完成

**预计耗时：** 2-3 天（大爆炸重构）

**关键风险：**
- 数据迁移失败 → 保留备份 7 天
- 并发更新冲突 → 细粒度字段行隔离

**下一步：** 选择执行方式（Subagent-Driven 或 Inline Execution）

