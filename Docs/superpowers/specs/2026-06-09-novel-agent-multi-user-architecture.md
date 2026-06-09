# Novel Agent Multi-User Architecture Migration

> **Implementation Plan**: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this design task-by-task.

**Goal:** 将小说Agent系统从单用户文件系统架构迁移到多用户SaaS数据库架构

**Architecture:** 三层存储架构（SQLite + 文件系统 + Qdrant）+ 项目级Workspace单例池

**Tech Stack:** ASP.NET Core 8.0, Entity Framework Core, SQLite, Qdrant v1.8.0, JWT认证

---

## 1. 架构概览

### 1.1 当前问题

**旧式单用户架构：**
- NovelAgentWorkspace单例服务（全局共享）
- 基于StoragePathHelper.CurrentProjectName的文件系统存储
- MaterialLibrary、NovelLibrary、StoryBibleService等直接操作文件
- 无认证、无用户隔离、无并发控制

**已删除但依赖仍存在的Controllers：**
- ChapterController, StoryFoundationController, VolumeArcController
- RunController, LedgerController, MaterialController
- CreativeKnowledgeController, NovelLibraryController, StoryBibleController
- WorkspaceController

**新架构已部分实现：**
- AuthController - JWT认证 ✅
- SettingsController - 用户设置（数据库）✅
- ProjectController - 项目管理（数据库）✅
- ChaptersController - 章节CRUD（数据库+文件）✅
- AgentController - Agent对话（仍依赖旧Workspace）⚠️

### 1.2 目标架构

**三层存储架构（B+增强版）：**

```
┌─────────────────────────────────────────────────────────┐
│ Layer 1: SQLite（结构化元数据 + 关系）                    │
│ - Users, Projects, Materials, KnowledgeEntries          │
│ - StoryBible: Constitutions, VolumeArcs, Characters     │
│ - ForeshadowLedger, WorldSettings, AgentRuns            │
│ - Chapters（元数据）                                      │
│ - 索引、标签、关系、权限控制                              │
├─────────────────────────────────────────────────────────┤
│ Layer 2: 文件系统（大文本原文）                           │
│ - Materials原文: Users/{uid}/Projects/{pid}/Materials/  │
│ - Chapter草稿/成稿: Users/{uid}/Projects/{pid}/Chapters/│
│ - 按userId/projectId物理隔离                             │
├─────────────────────────────────────────────────────────┤
│ Layer 3: Qdrant（语义向量 + 富元数据）                    │
│ - Collection: novel_agent_{userId}                       │
│ - Payload: project_id, entity_type, category, content   │
│ - 支持语义检索、混合检索、相似度检测                       │
└─────────────────────────────────────────────────────────┘
```

**Workspace生命周期管理（方案D）：**
- 项目级单例：Key = (userId, projectId)
- 引用计数：多Session共享同一Workspace
- 软LRU淘汰：30分钟无访问+引用计数=0时淘汰
- 最大缓存：50个Workspace实例
- 后台清理：每30秒检查淘汰

---

## 2. 数据库表设计

### 2.1 StoryBible核心表

#### story_constitutions（故事基石）

```sql
CREATE TABLE story_constitutions (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL UNIQUE,
    Genre TEXT NOT NULL,
    SubGenre TEXT,
    CoreHook TEXT NOT NULL,
    ReaderPromise TEXT,
    GenreProfile TEXT,
    TargetAudience TEXT,
    Taboos TEXT,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE
);
```

#### volume_arcs（卷架构）

```sql
CREATE TABLE volume_arcs (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    VolumeNumber INTEGER NOT NULL,
    VolumeTitle TEXT NOT NULL,
    VolumeTheme TEXT,
    TargetChapters INTEGER,
    CurrentChapters INTEGER DEFAULT 0,
    Act1_Setup TEXT,
    Act2_Confrontation TEXT,
    Act3_Climax TEXT,
    Act4_Resolution TEXT,
    KeyEvents TEXT,
    MajorConflict TEXT,
    ConflictEscalation TEXT,
    Status TEXT DEFAULT 'planned',
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    CompletedAt DATETIME,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE,
    UNIQUE(ProjectId, VolumeNumber)
);
```

#### characters（角色）

```sql
CREATE TABLE characters (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Role TEXT NOT NULL,
    Alias TEXT,
    Age INTEGER,
    Gender TEXT,
    Appearance TEXT,
    Personality TEXT,
    Background TEXT,
    InitialPowerLevel TEXT,
    CurrentPowerLevel TEXT,
    SpecialAbilities TEXT,
    CoreGoal TEXT,
    Motivation TEXT,
    Relationships TEXT,
    Status TEXT DEFAULT 'active',
    FirstAppearChapter TEXT,
    LastAppearChapter TEXT,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE
);
```

#### foreshadow_ledger（伏笔账本）

```sql
CREATE TABLE foreshadow_ledger (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    Title TEXT NOT NULL,
    Content TEXT NOT NULL,
    Category TEXT NOT NULL,
    PlantedInChapter TEXT NOT NULL,
    PlantedContext TEXT,
    Status TEXT DEFAULT 'planted',
    ResolvedInChapter TEXT,
    ResolvedContext TEXT,
    PlantedAt DATETIME NOT NULL,
    ResolvedAt DATETIME,
    Priority INTEGER DEFAULT 5,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE
);
```

#### world_settings（设定库）

```sql
CREATE TABLE world_settings (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    Category TEXT NOT NULL,
    SubCategory TEXT,
    Title TEXT NOT NULL,
    Content TEXT NOT NULL,
    FirstMentionedChapter TEXT,
    ReferencedChapters TEXT,
    Version INTEGER DEFAULT 1,
    PreviousVersion TEXT,
    ChangeLog TEXT,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE
);
```

#### agent_runs（创作历史）

```sql
CREATE TABLE agent_runs (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    RunType TEXT NOT NULL,
    TargetChapterId TEXT,
    Status TEXT DEFAULT 'running',
    InputParams TEXT,
    OutputData TEXT,
    ContextPackageSize INTEGER,
    ContextPackagePath TEXT,
    GateReportPath TEXT,
    StartedAt DATETIME NOT NULL,
    CompletedAt DATETIME,
    DurationMs INTEGER,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    FOREIGN KEY (UserId) REFERENCES users(Id),
    FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE CASCADE
);
```

### 2.2 Materials与KnowledgeEntries表

已在现有数据库中存在，需要添加向量关联字段：

```sql
ALTER TABLE materials ADD COLUMN VectorChunkCount INTEGER DEFAULT 0;
ALTER TABLE knowledge_entries ADD COLUMN VectorId TEXT;
```

---

## 3. Qdrant向量存储设计

### 3.1 Collection命名策略

**Collection命名：** `novel_agent_{userId}`

**优势：**
- Collection数量 = 用户数（可控）
- 避免项目级Collection爆炸（1000用户×5项目=5000个Collection）
- 支持跨项目检索（通过Filter限定project_id）

### 3.2 Payload结构

```json
{
  "user_id": "user_123",
  "project_id": "proj_456",
  "entity_type": "material",
  "entity_id": "mat_789",
  "chunk_index": 0,
  "chunk_total": 5,
  "chunk_id": "mat_789_chunk_0",
  "category": "角色设定",
  "sub_category": "主角",
  "tags": ["玄幻", "爽文", "升级"],
  "title": "林枫角色卡",
  "summary": "主角，16岁，获得上古传承...",
  "content": "原文片段（500-1000字）",
  "full_text_path": "Users/u123/Projects/p456/Materials/mat_789.txt",
  "created_at": 1717920000,
  "updated_at": 1717920000,
  "weight": 8,
  "source_type": "UserUpload",
  "source_reference": "mat_789",
  "genre": "玄幻",
  "volume_id": "vol_1",
  "chapter_id": "chapter_003"
}
```

### 3.3 索引配置

```python
client.create_collection(
    collection_name=f"novel_agent_{user_id}",
    vectors_config=VectorParams(size=1536, distance=Distance.COSINE),
    payload_schema={
        "project_id": PayloadSchemaType.KEYWORD,
        "entity_type": PayloadSchemaType.KEYWORD,
        "category": PayloadSchemaType.KEYWORD,
        "tags": PayloadSchemaType.KEYWORD,
        "created_at": PayloadSchemaType.INTEGER,
        "weight": PayloadSchemaType.INTEGER,
        "genre": PayloadSchemaType.KEYWORD,
    },
    shard_number=2,
    replication_factor=1
)
```

---

## 4. WorkspaceFactory设计（方案D：项目级单例+引用计数）

### 4.1 核心原则

1. **Workspace是项目级单例**：Key = (userId, projectId)
2. **Session借用Workspace**：多Session共享同一Workspace
3. **引用计数管理**：活跃引用>0时不能淘汰
4. **软LRU淘汰**：30分钟无访问+引用计数=0时淘汰
5. **线程安全**：Workspace内部使用SemaphoreSlim保护并发操作

### 4.2 WorkspaceEntry数据结构

```csharp
public sealed class WorkspaceEntry
{
    public string UserId { get; init; }
    public string ProjectId { get; init; }
    public NovelAgentWorkspace Workspace { get; init; }
    
    private int _activeReferences = 0;
    public int ActiveReferences => _activeReferences;
    public DateTime LastAccessTime { get; private set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public long TotalAccesses { get; private set; } = 0;
    
    public void AcquireLease()
    {
        Interlocked.Increment(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
        Interlocked.Increment(ref TotalAccesses);
    }
    
    public void ReleaseLease()
    {
        Interlocked.Decrement(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
    }
    
    public bool IsEvictable(TimeSpan idleTimeout)
    {
        return _activeReferences == 0 
            && DateTime.UtcNow - LastAccessTime > idleTimeout;
    }
}
```

### 4.3 WorkspaceFactory接口

```csharp
public interface IWorkspaceFactory
{
    Task<WorkspaceEntry> AcquireAsync(string userId, string projectId, CancellationToken ct);
    void Release(string userId, string projectId);
    void Touch(string userId, string projectId);
    WorkspaceFactoryStats GetStats();
}
```

### 4.4 生命周期管理

**创建时机：**
- AgentSession创建时调用`AcquireAsync(userId, projectId)`
- 如果缓存未命中，初始化新Workspace（100-500ms）
- 引用计数+1

**使用期间：**
- 每次API请求调用`Touch(userId, projectId)`更新LastAccessTime
- Session内多轮对话复用同一Workspace

**释放时机：**
- AgentSession销毁时调用`Release(userId, projectId)`
- 引用计数-1
- 如果引用计数=0且超过30分钟无访问，后台任务淘汰

**后台清理：**
- Timer每30秒执行一次`EvictIdleWorkspacesAsync()`
- 检查所有Entry，淘汰`IsEvictable()==true`的实例
- 强制淘汰：当缓存超过MaxCachedWorkspaces（50个）时，淘汰最老的可淘汰项

### 4.5 配置选项

```csharp
public sealed class WorkspaceFactoryOptions
{
    public string StorageRoot { get; set; } = "App_Data";
    public int MaxCachedWorkspaces { get; set; } = 50;
    public int IdleTimeoutMinutes { get; set; } = 30;
}
```

---

## 5. 服务层重构

### 5.1 StoryBibleRepository

```csharp
public sealed class StoryBibleRepository
{
    private readonly NovelAgentDbContext _db;
    
    public async Task<StoryBibleDocument> LoadStoryBibleAsync(
        string userId, string projectId, CancellationToken ct);
    
    public async Task SaveConstitutionAsync(
        StoryCreativeConstitution constitution, CancellationToken ct);
    
    public async Task<VolumeArc> SaveVolumeArcAsync(
        VolumeArc volumeArc, CancellationToken ct);
    
    public async Task<Character> SaveCharacterAsync(
        Character character, CancellationToken ct);
    
    public async Task<ForeshadowEntry> PlantForeshadowAsync(
        ForeshadowEntry foreshadow, CancellationToken ct);
    
    public async Task<ForeshadowEntry> ResolveForeshadowAsync(
        string foreshadowId, string resolvedInChapter, 
        string resolvedContext, CancellationToken ct);
}
```

### 5.2 MaterialService

```csharp
public sealed class MaterialService
{
    private readonly NovelAgentDbContext _db;
    private readonly IQdrantVectorStore _vectorStore;
    private readonly ICurrentUserService _currentUserService;
    
    public async Task<Material> UploadMaterialAsync(
        string projectId, IFormFile file, CancellationToken ct);
    
    public async Task<Material> IngestMaterialAsync(
        string projectId, string fileName, string content, 
        string sourceType, CancellationToken ct);
    
    public async Task AnalyzeMaterialAsync(
        string materialId, CancellationToken ct);
    
    public async Task<List<Material>> ListMaterialsAsync(
        string projectId, CancellationToken ct);
    
    public async Task DeleteMaterialAsync(
        string materialId, CancellationToken ct);
}
```

### 5.3 QdrantRetrievalService

```csharp
public sealed class QdrantRetrievalService
{
    private readonly IQdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    
    // 单项目内检索
    public async Task<List<SearchResult>> SearchInProjectAsync(
        string userId, string projectId, string query,
        string[]? entityTypes = null, string[]? categories = null,
        int topK = 10, CancellationToken ct = default);
    
    // 跨项目检索（用户级知识库）
    public async Task<List<SearchResult>> SearchAcrossProjectsAsync(
        string userId, string query, 
        string[]? excludeProjectIds = null,
        int topK = 5, CancellationToken ct = default);
    
    // 已用桥段相似度检测
    public async Task<List<DuplicateWarning>> DetectSimilarPatternsAsync(
        string userId, string projectId, string newPattern,
        float similarityThreshold = 0.85f, CancellationToken ct = default);
    
    // 混合检索（向量+BM25）
    public async Task<List<SearchResult>> HybridSearchAsync(
        string userId, string projectId, string query,
        Dictionary<string, float>? boostFields = null,
        CancellationToken ct = default);
}
```

---

## 6. API重构计划

### 6.1 需要重建的Controllers

#### MaterialsController（新建）
- `POST /api/materials/upload` - 上传素材文件
- `POST /api/materials` - 文本录入素材
- `GET /api/materials` - 列出项目素材
- `GET /api/materials/{id}` - 获取素材详情
- `GET /api/materials/{id}/content` - 获取素材原文
- `PATCH /api/materials/{id}` - 更新素材元数据
- `DELETE /api/materials/{id}` - 删除素材
- `POST /api/materials/{id}/analyze` - 触发素材分析

#### KnowledgeController（新建）
- `POST /api/knowledge/search` - 语义检索知识条目
- `GET /api/knowledge/entries` - 列出知识条目
- `POST /api/knowledge/entries` - 创建知识条目
- `PATCH /api/knowledge/entries/{id}` - 更新知识条目
- `DELETE /api/knowledge/entries/{id}` - 删除知识条目
- `POST /api/knowledge/detect-duplicates` - 相似度检测

#### StoryBibleController（新建）
- `GET /api/story-bible` - 获取完整StoryBible
- `POST /api/story-bible/constitution` - 创建/更新故事基石
- `POST /api/story-bible/volumes` - 创建卷架构
- `PATCH /api/story-bible/volumes/{id}` - 更新卷架构
- `POST /api/story-bible/characters` - 创建角色
- `PATCH /api/story-bible/characters/{id}` - 更新角色
- `POST /api/story-bible/foreshadows` - 种植伏笔
- `PATCH /api/story-bible/foreshadows/{id}/resolve` - 回收伏笔
- `POST /api/story-bible/world-settings` - 创建设定
- `PATCH /api/story-bible/world-settings/{id}` - 更新设定

#### WorkflowController（新建）
- `GET /api/workflow` - 获取项目工作流视图
- `POST /api/workflow/plan-foundation` - 规划故事基石
- `POST /api/workflow/plan-volume` - 规划卷架构
- `POST /api/workflow/plan-chapter` - 规划章节
- `POST /api/workflow/generate-chapter` - 生成章节
- `POST /api/workflow/review-chapter` - 评审章节
- `POST /api/workflow/commit-chapter` - 提交章节

### 6.2 AgentController重构

保留现有端点，内部改用WorkspaceFactory：

```csharp
public class AgentController : ControllerBase
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly IAgentSessionService _sessionService;
    
    [HttpPost("agent/session")]
    public async Task<IActionResult> CreateSession([FromQuery] string projectId, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var session = await _sessionService.CreateSessionAsync(userId, projectId, ct);
        return Ok(session);
    }
    
    [HttpPost("agent/chat")]
    public async Task<IActionResult> Chat([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var response = await _sessionService.SendMessageAsync(request.SessionId, request.Message, ct);
        return Ok(response);
    }
}
```

---

## 7. 前端迁移

### 7.1 需要更新的API调用

**src/api/index.ts：**
- 删除旧的`getWorkspace`、`getStoryBible`、`getMaterials`等
- 添加新的`getStoryBible()`, `getMaterials(projectId)`, `uploadMaterial(projectId, file)`
- 更新`getNovelLibrary(projectId)`、`getProjectWorkflow(projectId)`

**受影响的页面：**
- MaterialsPage.tsx - 改用新Materials API
- WorkflowPage.tsx - 改用新Workflow API
- LibraryPage.tsx - 改用新StoryBible API
- AgentPage.tsx - 已使用新Agent API，无需改动

### 7.2 Rail组件更新

删除`getWorkspace`调用，改用当前项目信息：

```typescript
// 旧代码
const { data: workspace } = useQuery({ 
  queryKey: ['workspace'], 
  queryFn: getWorkspace 
});

// 新代码
const { data: currentProject } = useQuery({ 
  queryKey: ['currentProject'], 
  queryFn: getCurrentProject 
});
```

---

## 8. 数据迁移策略

### 8.1 迁移步骤

**Phase 1: 数据库表创建**
1. 运行EF Core Migration创建新表
2. 保留现有users, projects, materials, chapters, user_settings表
3. 添加StoryBible相关表

**Phase 2: 文件系统重组**
1. 扫描现有`App_Data/Projects/AgenticNovelStudio/`
2. 创建默认用户（管理员账号）
3. 将文件移动到`App_Data/Users/{adminUserId}/Projects/{defaultProjectId}/`

**Phase 3: JSON to SQLite迁移**
1. 读取`StoryBible.json` → 解析 → 写入story_constitutions, volume_arcs, characters表
2. 读取`Materials.json` → 元数据已在materials表，文件移动到新路径
3. 读取`CreativeKnowledge.json` → 写入knowledge_entries表

**Phase 4: 向量化**
1. 扫描所有Materials原文 → 分块 → Embedding → 写入Qdrant
2. 扫描所有KnowledgeEntries → Embedding → 写入Qdrant

### 8.2 回滚计划

**如果迁移失败：**
1. 保留原始文件备份（`App_Data/Backup_{timestamp}/`）
2. DROP新建的StoryBible表
3. 恢复Program.cs中的旧服务注册
4. 恢复被删除的旧Controllers（从git历史恢复）

---

## 9. 实施顺序

### Phase 1: 基础架构（3-5天）
1. **数据库表创建**
   - 创建EF Core实体类（StoryConstitution, VolumeArc, Character等）
   - 生成并运行Migration
   - 验证表结构

2. **WorkspaceFactory实现**
   - 实现WorkspaceEntry和WorkspaceFactory
   - 配置DI注册
   - 单元测试（LRU逻辑、引用计数、线程安全）

3. **Repository层**
   - StoryBibleRepository
   - MaterialService重构
   - 集成测试

### Phase 2: 向量存储（2-3天）
1. **QdrantRetrievalService实现**
   - Collection管理
   - Payload结构实现
   - 检索方法（单项目、跨项目、相似度检测）

2. **向量化Pipeline**
   - Material分块与Embedding
   - KnowledgeEntry向量化
   - 批量导入工具

### Phase 3: API重构（5-7天）
1. **新Controllers实现**
   - MaterialsController
   - KnowledgeController
   - StoryBibleController
   - WorkflowController

2. **AgentController重构**
   - 集成WorkspaceFactory
   - AgentSessionService改造
   - SSE流式响应测试

3. **权限控制**
   - 所有Controller添加[Authorize]
   - 数据访问层添加userId过滤

### Phase 4: 前端适配（3-4天）
1. **API层更新**
   - src/api/index.ts重写
   - 类型定义更新（src/api/types.ts）

2. **页面组件更新**
   - MaterialsPage.tsx
   - WorkflowPage.tsx
   - LibraryPage.tsx
   - Rail.tsx

3. **E2E测试**
   - 登录 → 创建项目 → 上传素材 → 创建章节

### Phase 5: 数据迁移（1-2天）
1. **迁移脚本**
   - 文件系统重组
   - JSON转SQLite
   - 向量化所有内容

2. **验证与回滚测试**
   - 数据完整性检查
   - 功能回归测试
   - 回滚演练

---

## 10. 测试策略

### 10.1 单元测试
- WorkspaceFactory（LRU、引用计数、淘汰逻辑）
- Repository层（CRUD操作、事务）
- QdrantRetrievalService（检索、过滤）

### 10.2 集成测试
- Workspace生命周期（创建→使用→淘汰）
- 数据库+向量存储一致性
- 多Session共享Workspace场景

### 10.3 性能测试
- 100并发用户创建Session
- Workspace缓存命中率
- 向量检索延迟（p50, p95, p99）

### 10.4 安全测试
- JWT认证绕过测试
- 跨用户数据访问测试
- SQL注入测试

---

## 11. 监控与运维

### 11.1 关键指标

**WorkspaceFactory：**
- `total_workspaces`: 当前缓存的Workspace数量
- `active_references`: 总引用计数
- `cache_hit_rate`: 缓存命中率
- `eviction_count`: 淘汰次数

**Qdrant：**
- `collection_count`: Collection数量
- `vector_count_per_collection`: 每个Collection的向量数
- `search_latency`: 检索延迟

**数据库：**
- `connection_pool_usage`: 连接池使用率
- `query_duration`: 查询耗时
- `table_size`: 各表大小

### 11.2 健康检查端点

```csharp
[HttpGet("/api/health")]
public IActionResult Health()
{
    var workspaceStats = _workspaceFactory.GetStats();
    var dbHealth = _db.Database.CanConnect();
    var qdrantHealth = await _qdrant.HealthCheckAsync();
    
    return Ok(new {
        status = "healthy",
        workspace = workspaceStats,
        database = new { connected = dbHealth },
        qdrant = new { connected = qdrantHealth }
    });
}
```

---

## 12. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| NovelAgentOrchestrator不是线程安全 | 多Session并发崩溃 | 添加SemaphoreSlim锁，压测验证 |
| Workspace初始化慢（>500ms） | 首次访问延迟高 | 预热常用项目，优化Orchestrator初始化 |
| Qdrant Collection过多 | 内存占用高 | 按userId而非projectId隔离 |
| 数据迁移失败 | 系统不可用 | 完整备份，回滚脚本，灰度迁移 |
| 向量化耗时长（10万素材） | 迁移时间>1小时 | 批量并行处理，断点续传 |

---

## 13. 成功标准

**功能完整性：**
- ✅ 所有旧API功能在新架构中可用
- ✅ 多用户隔离正确（无数据泄露）
- ✅ 语义检索准确率>85%

**性能指标：**
- ✅ Workspace初始化时间<300ms
- ✅ 向量检索延迟p95<500ms
- ✅ 支持100并发用户

**稳定性：**
- ✅ 7×24小时运行无崩溃
- ✅ 内存占用<2GB（50个Workspace）
- ✅ 数据迁移成功率100%

