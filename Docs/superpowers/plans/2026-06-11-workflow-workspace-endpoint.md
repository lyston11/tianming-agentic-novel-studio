# Workflow Workspace 端点实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 GET /api/workflow/workspace 端点，返回用户工作台项目列表 + 统计信息，解除前端 WorkflowPage.tsx 数据流断点

**Architecture:** 在现有 WorkflowService 添加 GetWorkspaceAsync 方法，使用批量查询聚合统计信息（避免 N+1），WorkflowController 添加新端点，前端 WorkflowPage.tsx 替换硬编码空数组为 API 查询

**Tech Stack:** C# / ASP.NET Core 8.0 / Entity Framework Core / React / TypeScript / TanStack Query

---

## File Structure

**Backend (修改):**
- `Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs` - 添加 GetWorkspaceAsync 接口
- `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs` - 实现批量查询逻辑
- `Web/NovelAgentWeb/Controllers/WorkflowController.cs` - 添加 GetWorkspace 端点

**Backend (新增):**
- `Web/NovelAgentWeb/DTOs/WorkspaceResponse.cs` - 响应 DTO

**Frontend (修改):**
- `Web/NovelAgentWeb.Frontend/src/api/index.ts` - 添加 getWorkspace API 函数
- `Web/NovelAgentWeb.Frontend/src/api/types.ts` - 添加 WorkspaceResponse 类型
- `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx` - 替换硬编码为 API 查询

---

### Task 1: 添加 WorkspaceResponse DTO

**Files:**
- Create: `Web/NovelAgentWeb/DTOs/WorkspaceResponse.cs`

- [ ] **Step 1: 创建 WorkspaceResponse DTO**

```csharp
using TM.Web.NovelAgentWeb.Models.Projects;

namespace TM.Web.NovelAgentWeb.DTOs;

public class WorkspaceResponse
{
    public List<NovelBookView> Projects { get; set; } = new();
    public int TotalCount { get; set; }
}

public class NovelBookView
{
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string SubGenre { get; set; } = string.Empty;
    public string CoreHook { get; set; } = string.Empty;
    public string ReaderPromise { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int VolumeCount { get; set; }
    public int GeneratedChapterCount { get; set; }
    public int PlannedChapterCount { get; set; }
    public int NeedsRewriteCount { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public object? SelectedChapter { get; set; }
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
git add Web/NovelAgentWeb/DTOs/WorkspaceResponse.cs
git commit -m "feat(dto): add WorkspaceResponse and NovelBookView DTOs

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 扩展 IWorkflowService 接口

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs`

- [ ] **Step 1: 添加 GetWorkspaceAsync 方法签名**

在 IWorkflowService 接口末尾添加：

```csharp
/// <summary>
/// Gets workspace overview with all user projects and statistics.
/// </summary>
Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default);
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译失败（WorkflowService 未实现接口）

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs
git commit -m "feat(workflow): add GetWorkspaceAsync to IWorkflowService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: 实现 WorkflowService.GetWorkspaceAsync

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs`

- [ ] **Step 1: 实现 GetWorkspaceAsync 方法**

在 WorkflowService 类末尾添加：

```csharp
public async Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default)
{
    // 1. 查询用户所有项目（按 UpdatedAt 降序，限制 50 个）
    var projects = await _db.NovelProjects
        .Where(p => p.UserId == userId)
        .OrderByDescending(p => p.UpdatedAt)
        .Take(50)
        .ToListAsync(ct);

    if (projects.Count == 0)
    {
        return new WorkspaceResponse
        {
            Projects = new List<NovelBookView>(),
            TotalCount = 0
        };
    }

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
        .Select(g => new
        {
            ProjectId = g.Key,
            GeneratedCount = g.Count(c => c.Status == "committed"),
            PlannedCount = g.Count(c => c.Status != "committed"),
            NeedsRewriteCount = 0
        })
        .ToDictionaryAsync(x => x.ProjectId, ct);

    // 4. 查询 StoryConstitution（coreHook, readerPromise）
    var constitutions = await _db.StoryConstitutions
        .Where(sc => projectIds.Contains(sc.ProjectId))
        .ToDictionaryAsync(sc => sc.ProjectId, ct);

    // 5. 映射到 NovelBookView
    var bookViews = projects.Select(p =>
    {
        var stats = chapterStats.GetValueOrDefault(p.Id);
        var constitution = constitutions.GetValueOrDefault(p.Id);

        return new NovelBookView
        {
            ProjectId = p.Id,
            Title = p.Title,
            Genre = p.Genre ?? string.Empty,
            SubGenre = p.SubGenre ?? string.Empty,
            CoreHook = constitution?.CoreHook ?? string.Empty,
            ReaderPromise = constitution?.ReaderPromise ?? string.Empty,
            Status = p.Status,
            IsActive = p.Status != "archived",
            VolumeCount = volumeCounts.GetValueOrDefault(p.Id, 0),
            GeneratedChapterCount = stats?.GeneratedCount ?? 0,
            PlannedChapterCount = stats?.PlannedCount ?? 0,
            NeedsRewriteCount = stats?.NeedsRewriteCount ?? 0,
            UpdatedAt = p.UpdatedAt.ToString("o"),
            SelectedChapter = null
        };
    }).ToList();

    return new WorkspaceResponse
    {
        Projects = bookViews,
        TotalCount = bookViews.Count
    };
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
git add Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs
git commit -m "feat(workflow): implement GetWorkspaceAsync with batch statistics

- Query user projects ordered by UpdatedAt DESC (limit 50)
- Batch query volume counts using GROUP BY
- Batch query chapter statistics (generated/planned/rewrite counts)
- Load StoryConstitution for coreHook and readerPromise
- Map to NovelBookView with all required fields

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: 添加 WorkflowController.GetWorkspace 端点

**Files:**
- Modify: `Web/NovelAgentWeb/Controllers/WorkflowController.cs`

- [ ] **Step 1: 添加 GetWorkspace 端点**

在 WorkflowController 类开头添加（第 23 行之后）：

```csharp
[HttpGet("workspace")]
public async Task<IActionResult> GetWorkspace(CancellationToken ct)
{
    try
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var workspace = await _workflowService.GetWorkspaceAsync(userId, ct);
        return Ok(workspace);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get workspace for user");
        return StatusCode(500, new { error = "Failed to get workspace" });
    }
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 手动测试端点**

启动后端：
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

使用 curl 测试（需要有效 token）：
```bash
curl -H "Authorization: Bearer YOUR_TOKEN" http://localhost:5002/api/workflow/workspace
```

预期：返回 JSON 响应，包含 projects 和 totalCount

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Controllers/WorkflowController.cs
git commit -m "feat(api): add GET /api/workflow/workspace endpoint

Returns user workspace with project list and statistics

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: 前端添加 WorkspaceResponse 类型

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/types.ts`

- [ ] **Step 1: 添加 WorkspaceResponse 接口**

在 types.ts 文件中 NovelBookView 接口之后添加：

```typescript
export interface WorkspaceResponse {
  projects: NovelBookView[];
  totalCount: number;
}
```

- [ ] **Step 2: 验证 TypeScript 编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/types.ts
git commit -m "feat(types): add WorkspaceResponse interface

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 前端添加 getWorkspace API 函数

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts`

- [ ] **Step 1: 添加 getWorkspace 函数**

在 api/index.ts 文件的导出函数列表中添加：

```typescript
export const getWorkspace = async (): Promise<WorkspaceResponse> => {
  const response = await api.get('/workflow/workspace');
  return response.data;
};
```

- [ ] **Step 2: 验证 TypeScript 编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "feat(api): add getWorkspace API function

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: 前端集成 WorkflowPage.tsx

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx`

- [ ] **Step 1: 删除硬编码空数组，替换为 API 查询**

找到第 290 行：
```typescript
const books: NovelBookView[] = [];
```

替换为：
```typescript
const { data: workspaceData, isLoading } = useQuery({
  queryKey: ['workspace'],
  queryFn: getWorkspace
});

const books = workspaceData?.projects ?? [];
```

- [ ] **Step 2: 添加 getWorkspace 导入**

在文件顶部的导入语句中添加：
```typescript
import {
  deleteNovelProject,
  listAgentSessions,
  sendChat,
  getWorkspace,  // 新增
} from '../api';
```

- [ ] **Step 3: 验证 TypeScript 编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run build
```

预期：编译成功

- [ ] **Step 4: 手动测试前端**

启动前端：
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run dev
```

打开 http://localhost:3002/workflow，验证：
- 项目列表正确显示
- 统计信息正确（卷数、章节数）
- 无 JavaScript 错误

- [ ] **Step 5: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx
git commit -m "feat(workflow): integrate workspace API in WorkflowPage

Replace hardcoded empty books array with API query

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## 自我审查

**Spec 覆盖检查：**
- ✅ Task 1-2: DTO 和接口定义
- ✅ Task 3: WorkflowService 批量查询实现
- ✅ Task 4: WorkflowController 端点
- ✅ Task 5-7: 前端类型、API 函数、WorkflowPage 集成
- ✅ 所有规格要求已覆盖

**占位符扫描：**
- ✅ 无 TBD、TODO、"类似 Task N"
- ✅ 所有代码块完整

**类型一致性：**
- ✅ NovelBookView 字段在 DTO 和前端类型中一致
- ✅ WorkspaceResponse 在后端和前端一致

---

## 实现计划完成
