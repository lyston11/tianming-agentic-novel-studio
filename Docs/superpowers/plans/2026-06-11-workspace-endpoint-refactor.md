# Workspace 端点重构计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一 workspace 端点到 /api/workspace，消除 WorkflowService 职责混乱，删除冗余端点

**Architecture:** 创建 WorkspaceService 承载 GetWorkspaceAsync 方法，WorkspaceController 返回完整项目列表数据，删除 WorkflowController.GetWorkspace 端点，前端更新 API 路径

**Tech Stack:** C# / ASP.NET Core 8.0 / Entity Framework Core / React / TypeScript

---

## File Structure

**Backend (创建):**
- `Web/NovelAgentWeb/Services/Workspace/IWorkspaceService.cs` - 新接口
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceService.cs` - 实现 GetWorkspaceAsync

**Backend (修改):**
- `Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs` - 删除 GetWorkspaceAsync 方法
- `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs` - 删除 GetWorkspaceAsync 实现
- `Web/NovelAgentWeb/Controllers/WorkspaceController.cs` - 调用 WorkspaceService.GetWorkspaceAsync
- `Web/NovelAgentWeb/Controllers/WorkflowController.cs` - 删除 GetWorkspace 端点
- `Web/NovelAgentWeb/Program.cs` - 注册 WorkspaceService

**Frontend (修改):**
- `Web/NovelAgentWeb.Frontend/src/api/index.ts` - 更新 getWorkflowWorkspace 路径
- `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx` - 更新函数名（可选）

---

### Task 1: 创建 IWorkspaceService 接口

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/IWorkspaceService.cs`

- [ ] **Step 1: 创建接口文件**

```csharp
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Service for managing workspace overview and project listings.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Gets workspace overview with all user projects and statistics.
    /// </summary>
    Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default);
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
git add Web/NovelAgentWeb/Services/Workspace/IWorkspaceService.cs
git commit -m "feat(workspace): add IWorkspaceService interface

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 创建 WorkspaceService 实现

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/WorkspaceService.cs`

- [ ] **Step 1: 创建实现文件**

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public class WorkspaceService : IWorkspaceService
{
    private readonly NovelAgentDbContext _db;

    public WorkspaceService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default)
    {
        var projects = await _db.NovelProjects
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (projects.Count == 0)
        {
            return new WorkspaceResponse(new List<NovelBookView>(), 0);
        }

        var projectIds = projects.Select(p => p.Id).ToList();

        var volumeCounts = await _db.VolumeArcs
            .Where(v => projectIds.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

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

        var constitutions = await _db.StoryConstitutions
            .Where(sc => projectIds.Contains(sc.ProjectId))
            .ToDictionaryAsync(sc => sc.ProjectId, ct);

        var bookViews = projects.Select(p =>
        {
            var stats = chapterStats.GetValueOrDefault(p.Id);
            var constitution = constitutions.GetValueOrDefault(p.Id);

            return new NovelBookView(
                p.Id,
                p.Title,
                p.Genre ?? string.Empty,
                p.SubGenre ?? string.Empty,
                constitution?.CoreHook ?? string.Empty,
                constitution?.ReaderPromise ?? string.Empty,
                p.Status,
                p.Status != "archived",
                volumeCounts.GetValueOrDefault(p.Id, 0),
                stats?.GeneratedCount ?? 0,
                stats?.PlannedCount ?? 0,
                stats?.NeedsRewriteCount ?? 0,
                p.UpdatedAt.ToString("o"),
                null);
        }).ToList();

        return new WorkspaceResponse(bookViews, bookViews.Count);
    }
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
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceService.cs
git commit -m "feat(workspace): implement WorkspaceService with batch queries

Move GetWorkspaceAsync from WorkflowService to dedicated WorkspaceService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: 注册 WorkspaceService 到 DI 容器

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: 添加服务注册**

在 `Program.cs` 中找到服务注册区域（通常在 `builder.Services.AddScoped<IWorkflowService, WorkflowService>();` 附近），添加：

```csharp
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
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
git commit -m "feat(di): register WorkspaceService in DI container

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: 更新 WorkspaceController 使用 WorkspaceService

**Files:**
- Modify: `Web/NovelAgentWeb/Controllers/WorkspaceController.cs`

- [ ] **Step 1: 替换 WorkspaceController 实现**

完整替换文件内容为：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/workspace")]
[Authorize]
public sealed class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        IWorkspaceService workspaceService,
        ICurrentUserService currentUserService,
        ILogger<WorkspaceController> logger)
    {
        _workspaceService = workspaceService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var workspace = await _workspaceService.GetWorkspaceAsync(userId, ct);
            return Ok(workspace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace");
            return StatusCode(500, new { error = "Failed to get workspace" });
        }
    }
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
git add Web/NovelAgentWeb/Controllers/WorkspaceController.cs
git commit -m "refactor(workspace): use WorkspaceService in WorkspaceController

Replace NovelProjectCatalog logic with WorkspaceService.GetWorkspaceAsync

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: 从 IWorkflowService 删除 GetWorkspaceAsync

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs`

- [ ] **Step 1: 删除 GetWorkspaceAsync 方法**

删除第 35-38 行：

```csharp
/// <summary>
/// Gets workspace overview with all user projects and statistics.
/// </summary>
Task<WorkspaceResponse> GetWorkspaceAsync(string userId, CancellationToken ct = default);
```

保留文件为：

```csharp
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Workflow;

/// <summary>
/// Service interface for managing volume arc workflow planning.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Creates a new volume arc for a project.
    /// </summary>
    Task<VolumeArcResponse> CreateVolumeArcAsync(CreateVolumeArcRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists all volume arcs for a specific project.
    /// </summary>
    Task<List<VolumeArcResponse>> ListVolumeArcsAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Gets a volume arc by ID.
    /// </summary>
    Task<VolumeArcResponse> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default);

    /// <summary>
    /// Updates a volume arc.
    /// </summary>
    Task<VolumeArcResponse> UpdateVolumeArcAsync(string volumeArcId, UpdateVolumeArcRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a volume arc.
    /// </summary>
    Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default);
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译失败（WorkflowService 仍有实现）

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Services/Workflow/IWorkflowService.cs
git commit -m "refactor(workflow): remove GetWorkspaceAsync from IWorkflowService

Workspace queries moved to WorkspaceService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 从 WorkflowService 删除 GetWorkspaceAsync 实现

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs`

- [ ] **Step 1: 删除 GetWorkspaceAsync 方法实现**

删除 WorkflowService.cs 第 176-237 行的 GetWorkspaceAsync 方法（整个方法体）

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs
git commit -m "refactor(workflow): remove GetWorkspaceAsync from WorkflowService

Method moved to WorkspaceService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: 删除 WorkflowController.GetWorkspace 端点

**Files:**
- Modify: `Web/NovelAgentWeb/Controllers/WorkflowController.cs`

- [ ] **Step 1: 删除 GetWorkspace 端点**

删除 WorkflowController.cs 第 24-41 行的 GetWorkspace 方法

- [ ] **Step 2: 验证编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 手动测试后端**

启动后端：
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

验证：
```bash
# 旧端点应该返回 404
curl -H "Authorization: Bearer YOUR_TOKEN" http://localhost:5002/api/workflow/workspace
# 预期：404

# 新端点应该返回项目列表
curl -H "Authorization: Bearer YOUR_TOKEN" http://localhost:5002/api/workspace
# 预期：200 + WorkspaceResponse JSON
```

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb/Controllers/WorkflowController.cs
git commit -m "refactor(workflow): remove GET /api/workflow/workspace endpoint

Use GET /api/workspace instead

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 8: 更新前端 API 路径

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts`

- [ ] **Step 1: 更新 getWorkflowWorkspace 路径**

找到第 198 行：

```typescript
export const getWorkflowWorkspace = () => get<WorkspaceResponse>('/workflow/workspace');
```

替换为：

```typescript
export const getWorkflowWorkspace = () => get<WorkspaceResponse>('/workspace');
```

- [ ] **Step 2: 验证 TypeScript 编译**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run build
```

预期：编译成功

- [ ] **Step 3: 手动测试前端**

启动前端：
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run dev
```

打开 http://localhost:3002/workflow，验证：
- 项目列表正确显示
- 统计信息正确
- Network 面板显示调用 GET /api/workspace（非 /api/workflow/workspace）

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "refactor(api): update workspace API path to /workspace

Replace /workflow/workspace with /workspace

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## 自我审查

**Spec 覆盖检查：**
- ✅ Task 1-2: 创建 WorkspaceService
- ✅ Task 3: DI 注册
- ✅ Task 4: 更新 WorkspaceController
- ✅ Task 5-6: 从 WorkflowService 删除 GetWorkspaceAsync
- ✅ Task 7: 删除 WorkflowController.GetWorkspace
- ✅ Task 8: 前端路径更新
- ✅ 所有规格要求已覆盖

**占位符扫描：**
- ✅ 无 TBD、TODO
- ✅ 所有代码块完整

**类型一致性：**
- ✅ WorkspaceResponse 在后端和前端一致
- ✅ GetWorkspaceAsync 方法签名一致

---

## 实现计划完成
