# Workflow Workspace 端点设计

> **日期**: 2026-06-11  
> **目标**: 实现 GET /api/workflow/workspace 端点，返回用户工作台项目列表，解决前端 WorkflowPage.tsx 数据流断点

---

## 1. 背景

### 当前问题

WorkflowPage.tsx 第 287 行硬编码 `const books: NovelBookView[] = []`，注释说明完整 workflow API 迁移未完成。前端有完整的工作台 UI，但真实项目/章节数据流未接通。

### 缺失的后端能力

- ❌ 没有返回用户所有项目 + 统计信息的聚合端点
- ✅ 已有 `/api/workflow/volumes` （仅卷 CRUD）
- ✅ 已有 `/api/projects`（项目 CRUD，但缺乏工作流视角的统计）

### 需求

创建 `/api/workflow/workspace` 端点：
- 返回当前用户所有项目（按最近活动排序）
- 包含每个项目的统计信息（卷数、章节数、生成状态）
- 支持分级加载：概览数据 + 按需详情

---

## 2. 设计方案

### 架构选择：分级加载

**端点分工：**
- `GET /api/workflow/workspace` → 项目列表 + 统计（概览）
- `GET /api/workflow/projects/{id}` → 单个项目完整数据（详情）

**理由：**
- 性能：初始加载只查统计，避免一次性加载所有 volumes/chapters/runs
- 职责清晰：workspace = 工作台视图，project = 项目详情
- 可扩展：未来可支持分页、筛选

---

## 3. API 设计

### 3.1 端点定义

**请求：**
```
GET /api/workflow/workspace
Authorization: Bearer {token}
```

**响应：**
```json
{
  "projects": [
    {
      "projectId": "abc123",
      "title": "玄幻小说",
      "genre": "玄幻",
      "subGenre": "东方玄幻",
      "coreHook": "...",
      "readerPromise": "...",
      "status": "active",
      "isActive": true,
      "volumeCount": 3,
      "generatedChapterCount": 12,
      "plannedChapterCount": 45,
      "needsRewriteCount": 2,
      "updatedAt": "2026-06-11T10:30:00Z",
      "selectedChapter": null
    }
  ],
  "totalCount": 15
}
```

### 3.2 NovelBookView 字段说明

**包含字段（精简版）：**
- `projectId`, `title`, `genre`, `subGenre` - 基础信息
- `coreHook`, `readerPromise` - 故事核心（来自 StoryConstitution）
- `status`, `isActive` - 项目状态
- `volumeCount` - 卷数量
- `generatedChapterCount` - 已生成章节数（Status = "committed"）
- `plannedChapterCount` - 已规划章节数（Status IN ["planned", "draft_generated", ...])
- `needsRewriteCount` - 需要重写的章节数
- `updatedAt` - 最后更新时间
- `selectedChapter` - 当前选中章节（始终为 null，由前端管理）

**不包含字段（按需加载）：**
- `volumes[]` - 卷列表（通过 `/api/workflow/volumes?projectId={id}` 获取）
- `chapters[]` - 章节列表（通过 `/api/chapters?projectId={id}` 获取）

### 3.3 排序规则

按项目最近活动时间降序：
1. 优先使用 `Project.UpdatedAt`
2. 如果项目有关联 AgentSession，使用最新 Session.UpdatedAt

### 3.4 数量限制

- 返回所有项目（不分页）
- 实际限制：前 50 个项目（性能保护）
- 理由：工作台场景通常不会有超过 50 个活跃项目

---

## 4. 后端实现

### 4.1 文件结构

**修改文件：**
- `Web/NovelAgentWeb/Controllers/WorkflowController.cs` - 新增 GetWorkspace 端点
- `Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs` - 新增接口定义
- `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs` - 新增实现

**新增 DTO（如需要）：**
- `Web/NovelAgentWeb/DTOs/WorkspaceResponse.cs`

### 4.2 WorkflowService.GetWorkspaceAsync() 实现

**伪代码：**
```csharp
public async Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct)
{
    // 1. 查询用户所有项目（按 UpdatedAt 降序，限制 50 个）
    var projects = await _db.NovelProjects
        .Where(p => p.UserId == userId)
        .OrderByDescending(p => p.UpdatedAt)
        .Take(50)
        .ToListAsync(ct);
    
    var projectIds = projects.Select(p => p.Id).ToList();
    
    // 2. 批量统计卷数量
    var volumeCounts = await _db.VolumeArcs
        .Where(v => projectIds.Contains(v.ProjectId))
        .GroupBy(v => v.ProjectId)
        .Select(g => new { ProjectId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
    
    // 3. 批量统计章节信息
    var chapterStats = await _db.Chapters
        .Where(c => projectIds.Contains(c.ProjectId))
        .GroupBy(c => c.ProjectId)
        .Select(g => new {
            ProjectId = g.Key,
            GeneratedCount = g.Count(c => c.Status == "committed"),
            PlannedCount = g.Count(c => c.Status != "committed"),
            NeedsRewriteCount = g.Count(c => c.NeedsRewrite == true)
        })
        .ToDictionaryAsync(x => x.ProjectId, ct);
    
    // 4. 查询 StoryConstitution（coreHook, readerPromise）
    var constitutions = await _db.StoryConstitutions
        .Where(sc => projectIds.Contains(sc.ProjectId))
        .ToDictionaryAsync(sc => sc.ProjectId, ct);
    
    // 5. 映射到 NovelBookView
    var bookViews = projects.Select(p => new NovelBookView {
        ProjectId = p.Id,
        Title = p.Title,
        Genre = p.Genre ?? "",
        SubGenre = p.SubGenre ?? "",
        CoreHook = constitutions.GetValueOrDefault(p.Id)?.CoreHook ?? "",
        ReaderPromise = constitutions.GetValueOrDefault(p.Id)?.ReaderPromise ?? "",
        Status = p.Status,
        IsActive = p.Status != "archived",
        VolumeCount = volumeCounts.GetValueOrDefault(p.Id, 0),
        GeneratedChapterCount = chapterStats.GetValueOrDefault(p.Id)?.GeneratedCount ?? 0,
        PlannedChapterCount = chapterStats.GetValueOrDefault(p.Id)?.PlannedCount ?? 0,
        NeedsRewriteCount = chapterStats.GetValueOrDefault(p.Id)?.NeedsRewriteCount ?? 0,
        UpdatedAt = p.UpdatedAt.ToString("o"),
        SelectedChapter = null
    }).ToList();
    
    return new WorkspaceResponse {
        Projects = bookViews,
        TotalCount = bookViews.Count
    };
}
```

### 4.3 性能优化

**批量查询：**
- 使用 GROUP BY 聚合统计，避免 N+1 查询
- 一次性查询所有 projectIds 的数据

**字段限制：**
- 不加载导航属性（`.Include(p => p.Volumes)` 等）
- 不查询大字段（`Chapter.Content`）

**数据库索引（如缺失）：**
- `Chapters(ProjectId, Status)` - 用于统计查询
- `VolumeArcs(ProjectId)` - 用于卷计数
- `NovelProjects(UserId, UpdatedAt)` - 用于排序查询

---

## 5. 前端集成

### 5.1 WorkflowPage.tsx 修改

**删除硬编码：**
```typescript
// 删除第 290 行
const books: NovelBookView[] = [];
```

**新增数据查询：**
```typescript
const { data: workspaceData, isLoading } = useQuery({
  queryKey: ['workspace'],
  queryFn: getWorkspace
});

const books = workspaceData?.projects ?? [];
```

### 5.2 API 函数（src/api/index.ts）

**新增类型定义：**
```typescript
export interface WorkspaceResponse {
  projects: NovelBookView[];
  totalCount: number;
}
```

**新增 API 函数：**
```typescript
export const getWorkspace = async (): Promise<WorkspaceResponse> => {
  const response = await api.get('/workflow/workspace');
  return response.data;
};
```

### 5.3 现有逻辑保持不变

- 项目卡片展示基础信息 + 统计（已有 UI）
- 点击项目后，通过现有端点加载详情：
  - `/api/chapters?projectId={id}` - 章节列表
  - `/api/workflow/volumes?projectId={id}` - 卷列表

---

## 6. Artifacts 来源说明

**问题：** WorkflowChapterArtifactSummary 数据来自哪里？

**答案：** 从 AgentRun 聚合生成，不是单独的表。

### 6.1 数据流程

1. **生成阶段：** AgentRun.DraftArtifact.DraftContent 存储草稿（JSON）
2. **修复阶段：** 草稿在 DraftArtifact 中迭代修改
3. **提交阶段：** 草稿 → DraftArtifact.CommittedContent + 写入文件系统
4. **入库阶段：** 文件路径存入 Chapter.ContentPath

### 6.2 ChapterDraftArtifact 结构

```csharp
// HardcoreWritingModels.cs line 76-107
public sealed class ChapterDraftArtifact {
    public string ArtifactId { get; set; }
    public string ChapterId { get; set; }
    public string Status { get; set; }
    public string DraftContent { get; set; }        // 草稿内容（临时）
    public string CommittedContent { get; set; }   // 已提交内容
    public string ChangesJson { get; set; }        // 修改记录
    public int RepairAttemptCount { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? CommittedAt { get; set; }
}
```

### 6.3 前端 Artifacts 显示

WorkflowPage 显示的 artifacts = AgentRun 的聚合视图：
- 从 AgentRun.DraftArtifact 提取 Status、DraftContent（预览）
- 从 AgentRun.GateReport 提取门禁结果
- 从 AgentRun.ContextPackage 提取上下文信息

---

## 7. 测试验证

### 7.1 单元测试

**测试场景：**
- 用户无项目 → 返回空列表
- 用户有多个项目 → 按 UpdatedAt 降序
- 统计信息正确（volumeCount, generatedChapterCount）
- 归档项目不返回（isActive = false）

### 7.2 集成测试

**端到端流程：**
1. 创建项目、卷、章节
2. 调用 `/api/workflow/workspace`
3. 验证返回数据完整性和顺序

### 7.3 性能测试

**负载场景：**
- 用户有 50 个项目，每个项目 100 章节
- 响应时间 < 500ms
- 数据库查询次数 ≤ 5 次（批量查询）

---

## 8. 风险与限制

### 8.1 限制

- 返回所有项目（不分页），限制前 50 个
- 不包含 volumes/chapters 详情（需二次加载）
- 统计信息为快照（不保证实时精确，缓存 1 分钟可选）

### 8.2 已知风险

**性能风险：**
- 用户有大量项目时查询慢 → 限制 50 个 + 数据库索引
- 统计查询复杂 → 批量 GROUP BY + 缓存优化

**数据一致性：**
- 项目更新后统计延迟 → 可接受（工作台场景允许秒级延迟）

---

## 9. 后续优化（可选）

### 9.1 缓存优化

- 项目列表缓存 1 分钟（IMemoryCacheService）
- 统计信息缓存 5 分钟

### 9.2 分页支持

```
GET /api/workflow/workspace?page=1&pageSize=20
```

### 9.3 筛选支持

```
GET /api/workflow/workspace?status=active&genre=玄幻
```

---

## 10. 总结

**设计目标：**
- ✅ 提供工作台项目列表 + 统计
- ✅ 分级加载（概览 + 按需详情）
- ✅ 性能可控（批量查询 + 字段限制）

**实现范围：**
- 后端：WorkflowController + WorkflowService
- 前端：WorkflowPage.tsx + API 函数
- 数据库：现有表结构，无需迁移

**预期效果：**
WorkflowPage.tsx 显示真实项目数据，解除 `books = []` 硬编码限制。
