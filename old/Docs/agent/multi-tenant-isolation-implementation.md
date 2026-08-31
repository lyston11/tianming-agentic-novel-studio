# AgentSessionManager 多租户隔离实施方案

## 问题分析

### 当前架构缺陷（P0安全漏洞）

**Support/AgentSession.cs 第55-168行的 AgentSessionManager：**

```csharp
public sealed class AgentSessionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly string _sessionsPath; // 单一JSON文件存储所有用户
    
    public AgentSession GetOrCreateSession(string? sessionId = null)
    {
        // ❌ 没有userId过滤
        sessionId ??= Guid.NewGuid().ToString("N");
        var session = _sessions.GetOrAdd(sessionId, id => new AgentSession { SessionId = id });
        return session;
    }
    
    public IReadOnlyList<AgentSession> ListSessions() =>
        _sessions.Values
            .Where(session => !session.IsArchived)
            .ToList(); // ❌ 返回所有用户的会话
}
```

**严重性：**
- 用户A可以看到用户B的所有会话
- 会话可能引用其他用户的projectId
- JSON文件存储无法支持高并发多用户场景
- 无法利用数据库外键约束保证数据完整性

## 解决方案：数据库存储 + 用户路径隔离

### 方案2：数据库存储（已有表结构）

**现有资源：**
- 数据库表：`agent_sessions` (Data/NovelAgentDbContext.cs:309-332)
- 实体类：`Data.Entities.AgentSession` (完整EF Core实体)
- 外键约束：`user_id` → `users.id` (CASCADE DELETE)

**数据映射策略：**

| 内存模型 (Support/AgentSession.cs) | 数据库实体 (Data.Entities.AgentSession) |
|-----------------------------------|----------------------------------------|
| `SessionId` | `Id` |
| `UserId` | `UserId` |
| `Title` | `Title` |
| `ActiveProjectId` | `ProjectId` |
| `IsArchived` | `IsArchived` |
| `CreatedAt` / `UpdatedAt` | `CreatedAt` / `UpdatedAt` |
| `Phase`, `ActiveRunId`, `RunHistory`, `ChatHistory`, `WorkingMemory`, `DiscoveredPhase`, `DiscoveredTools`, `LastToolSearchAt` | **序列化到 `SessionData` JSON字段** |

**序列化内容（SessionData JSON）：**
```json
{
  "phase": "conversation",
  "activeRunId": "run-123",
  "runHistory": ["run-001", "run-002"],
  "chatHistory": [...],
  "workingMemory": {...},
  "toolSearchCache": {
    "discoveredPhase": "conversation",
    "discoveredTools": [...],
    "lastToolSearchAt": "2026-06-11T10:30:00Z"
  }
}
```

### 方案3：用户隔离的Workspace路径

**当前路径结构：**
```
App_Data/
  Projects/
    AgenticNovelStudio/  # ❌ 所有用户共享
      Agent/
        sessions.json
      Settings/
        user_settings.json
```

**新路径结构：**
```
App_Data/
  Users/
    {userId}/                      # ✅ 每用户独立目录
      Projects/
        {storageProjectName}/
          Agent/
            # 会话数据已迁移到数据库，此目录可删除
          Settings/
            user_settings.json
          Chapters/
          Knowledge/
```

## 详细实施步骤

### 第1步：重构 AgentSessionManager（数据库存储）

**文件：** `Web/NovelAgentWeb/Support/AgentSession.cs`

**修改范围：** 第55-168行 AgentSessionManager 类

**新构造函数：**
```csharp
public sealed class AgentSessionManager
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ConcurrentDictionary<string, Channel<AgentSseEvent>> _channels = new();

    public AgentSessionManager(
        NovelAgentDbContext db,
        ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }
}
```

**新GetOrCreateSession：**
```csharp
public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId = null, CancellationToken ct = default)
{
    var userId = _currentUser.UserId;
    
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        // 创建新会话
        var session = new AgentSession { UserId = userId };
        return session;
    }
    
    // 查询现有会话（强制userId过滤）
    var entity = await _db.AgentSessions
        .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
    
    if (entity == null)
    {
        return new AgentSession { SessionId = sessionId, UserId = userId };
    }
    
    return DeserializeSession(entity);
}
```

**新ListSessions：**
```csharp
public async Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default)
{
    var userId = _currentUser.UserId;
    
    var entities = await _db.AgentSessions
        .Where(s => s.UserId == userId && !s.IsArchived)
        .OrderByDescending(s => s.UpdatedAt)
        .ToListAsync(ct);
    
    return entities.Select(DeserializeSession).ToList();
}
```

**新SaveSession：**
```csharp
public async Task SaveSessionAsync(AgentSession session, CancellationToken ct = default)
{
    var entity = await _db.AgentSessions
        .FirstOrDefaultAsync(s => s.Id == session.SessionId, ct);
    
    if (entity == null)
    {
        entity = new Data.Entities.AgentSession
        {
            Id = session.SessionId,
            UserId = session.UserId,
            Title = session.Title,
            ProjectId = session.ActiveProjectId,
            IsArchived = session.IsArchived,
            SessionData = SerializeSessionData(session),
            CreatedAt = session.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
        _db.AgentSessions.Add(entity);
    }
    else
    {
        // 安全检查：防止跨用户覆写
        if (entity.UserId != session.UserId)
            throw new UnauthorizedAccessException($"Session {session.SessionId} belongs to another user");
        
        entity.Title = session.Title;
        entity.ProjectId = session.ActiveProjectId;
        entity.IsArchived = session.IsArchived;
        entity.SessionData = SerializeSessionData(session);
        entity.UpdatedAt = DateTime.UtcNow;
    }
    
    await _db.SaveChangesAsync(ct);
}
```

**序列化辅助方法：**
```csharp
private static string SerializeSessionData(AgentSession session) =>
    JsonSerializer.Serialize(new
    {
        phase = session.Phase,
        activeRunId = session.ActiveRunId,
        runHistory = session.RunHistory,
        chatHistory = session.ChatHistory,
        workingMemory = session.WorkingMemory,
        toolSearchCache = new
        {
            discoveredPhase = session.DiscoveredPhase,
            discoveredTools = session.DiscoveredTools,
            lastToolSearchAt = session.LastToolSearchAt
        }
    });

private static AgentSession DeserializeSession(Data.Entities.AgentSession entity)
{
    var data = string.IsNullOrWhiteSpace(entity.SessionData)
        ? null
        : JsonSerializer.Deserialize<SessionData>(entity.SessionData);
    
    return new AgentSession
    {
        SessionId = entity.Id,
        UserId = entity.UserId,
        Title = entity.Title,
        Phase = data?.Phase ?? "idle",
        ActiveProjectId = entity.ProjectId ?? string.Empty,
        ActiveRunId = data?.ActiveRunId,
        IsArchived = entity.IsArchived,
        RunHistory = data?.RunHistory ?? new(),
        ChatHistory = data?.ChatHistory ?? new(),
        WorkingMemory = data?.WorkingMemory ?? new(),
        DiscoveredPhase = data?.ToolSearchCache?.DiscoveredPhase,
        DiscoveredTools = data?.ToolSearchCache?.DiscoveredTools ?? new(),
        LastToolSearchAt = data?.ToolSearchCache?.LastToolSearchAt,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}

private class SessionData
{
    public string Phase { get; set; } = "idle";
    public string? ActiveRunId { get; set; }
    public List<string> RunHistory { get; set; } = new();
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public ToolSearchCache? ToolSearchCache { get; set; }
}

private class ToolSearchCache
{
    public string? DiscoveredPhase { get; set; }
    public List<ToolSchema> DiscoveredTools { get; set; } = new();
    public DateTime? LastToolSearchAt { get; set; }
}
```

### 第2步：修改所有调用方（异步化）

**涉及文件：**
1. `Controllers/AgentController.cs` - Chat, ListSessions, GetSession, ArchiveSession等端点
2. `Support/AgentRuntime.cs` - 加载/保存会话的地方

**修改模式：**
```csharp
// Before
var session = _sessionManager.GetOrCreateSession(sessionId);
_sessionManager.SaveSession(session);

// After
var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, ct);
await _sessionManager.SaveSessionAsync(session, ct);
```

### 第3步：用户隔离的Workspace路径

**文件：** `Services/Workspace/NovelAgentWorkspace.cs` (假设存在构造函数)

**修改StorageRoot计算逻辑：**
```csharp
public NovelAgentWorkspace(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    UserSettingsManager settingsManager)
{
    var baseStorageRoot = configuration["NovelAgent:StorageRoot"] 
        ?? Path.Combine(environment.ContentRootPath, "App_Data");
    
    // ✅ 新逻辑：每用户独立目录
    if (!string.IsNullOrWhiteSpace(UserId))
    {
        StorageRoot = Path.Combine(baseStorageRoot, "Users", UserId);
    }
    else
    {
        // Fallback for system tasks
        StorageRoot = Path.Combine(baseStorageRoot, "System");
    }
    
    Directory.CreateDirectory(StorageRoot);
    
    // 用户设置路径也需要更新
    _settingsManager = settingsManager;
}
```

**影响范围：**
- `App_Data/Users/{userId}/Projects/{storageProjectName}/Chapters/`
- `App_Data/Users/{userId}/Projects/{storageProjectName}/Knowledge/`
- `App_Data/Users/{userId}/Projects/{storageProjectName}/Settings/user_settings.json`

### 第4步：数据迁移脚本（可选）

**场景：** 如果现有环境有sessions.json文件需要迁移

**迁移工具类：** `Services/Migrations/AgentSessionMigration.cs`

```csharp
public class AgentSessionMigration
{
    public async Task MigrateFromJsonAsync(
        string jsonPath,
        string defaultUserId,
        NovelAgentDbContext db,
        CancellationToken ct)
    {
        if (!File.Exists(jsonPath))
            return;
        
        var json = await File.ReadAllTextAsync(jsonPath, ct);
        var sessions = JsonSerializer.Deserialize<List<AgentSession>>(json);
        
        foreach (var session in sessions ?? new())
        {
            // 如果没有UserId，分配给默认用户
            if (string.IsNullOrWhiteSpace(session.UserId))
                session.UserId = defaultUserId;
            
            var entity = new Data.Entities.AgentSession
            {
                Id = session.SessionId,
                UserId = session.UserId,
                Title = session.Title,
                ProjectId = session.ActiveProjectId,
                IsArchived = session.IsArchived,
                SessionData = SerializeSessionData(session),
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            };
            
            db.AgentSessions.Add(entity);
        }
        
        await db.SaveChangesAsync(ct);
        
        // 备份后删除JSON文件
        File.Move(jsonPath, jsonPath + ".migrated");
    }
}
```

## 风险评估与缓解

### 风险1：现有会话丢失

**缓解措施：**
- 保留sessions.json文件作为备份（重命名为.migrated）
- 提供迁移工具将现有数据导入数据库
- 迁移时将无UserId的会话分配给管理员用户

### 风险2：性能影响（数据库查询）

**缓解措施：**
- 在userId字段上已有索引（外键自动创建）
- 添加复合索引 `(user_id, updated_at DESC)` 优化ListSessions查询
- 考虑在内存缓存热会话（当前SSE Channel机制已部分实现）

### 风险3：并发写入冲突

**缓解措施：**
- 使用EF Core的乐观并发控制（添加RowVersion列，可选）
- 当前场景下每个会话同时只有一个Agent实例操作，冲突概率低
- SaveChangesAsync会抛出DbUpdateConcurrencyException，上层捕获重试

## 验证测试清单

1. **用户隔离验证：**
   - [ ] 用户A创建会话后，用户B无法看到
   - [ ] 用户A无法通过sessionId访问用户B的会话
   - [ ] ListSessions只返回当前用户的会话

2. **数据完整性验证：**
   - [ ] ChatHistory正确序列化/反序列化
   - [ ] WorkingMemory（包括MissionPlan）保持完整
   - [ ] ToolSearchCache正确保存和恢复

3. **路径隔离验证：**
   - [ ] 新创建的项目文件位于`App_Data/Users/{userId}/`
   - [ ] 用户设置文件路径正确
   - [ ] 章节、知识库文件路径正确

4. **迁移验证：**
   - [ ] 现有sessions.json成功导入数据库
   - [ ] 所有会话字段完整迁移
   - [ ] 旧JSON文件被重命名为.migrated

## 实施顺序建议

**阶段1（P0）：** AgentSessionManager数据库重构
- 修改AgentSessionManager类（异步化）
- 修改Controller调用方
- 部署后验证用户隔离

**阶段2（P1）：** Workspace路径隔离
- 修改NovelAgentWorkspace
- 测试新路径结构
- 考虑迁移现有用户数据（如需要）

**阶段3（可选）：** 数据迁移
- 运行迁移工具（仅首次部署需要）
- 验证旧数据完整性

## 关键依赖

- ✅ 数据库表结构已存在（`agent_sessions`）
- ✅ EF Core实体已定义（`Data.Entities.AgentSession`）
- ✅ ICurrentUserService已实现（用户上下文）
- ⚠️ 需要验证UserSettingsManager是否支持用户路径隔离
