# 天命AI写作 API 架构分析

**日期**: 2026-06-11  
**状态**: 🚨 存在严重设计冗余

---

## 1️⃣ 前后端对比

### ✅ 匹配良好的模块

| 模块 | 前端调用 | 后端实现 | 状态 |
|------|---------|---------|------|
| Materials | 7 个 | ChaptersController | ✅ 匹配 |
| Knowledge | 5 个 | KnowledgeController | ✅ 匹配 |
| StoryBible | 7 个 | StoryBibleController | ✅ 匹配 |
| Agent | 7 个 | AgentController | ✅ 匹配 |
| Settings | 4 个 | SettingsController | ✅ 匹配 |

### 🔴 问题模块

#### **问题 1: Workspace 概念混乱**

```
前端调用:
  GET /api/workspace            ← 获取当前工作空间状态
  GET /api/workflow/workspace   ← 获取项目列表+统计 (P1新增)

后端实现:
  WorkspaceController.Get()
    返回: { projectName, activeProjectId, storageProjectName, projectCount }
    
  WorkflowController.GetWorkspace()
    返回: { projects: [...], totalCount: 50 }
```

**问题：**
- 两个端点返回不同内容但都叫 "workspace"
- `WorkspaceController` 返回的是 `NovelProjectCatalog`（内部状态）
- `WorkflowController` 返回的是项目列表（用户界面需要的）

#### **问题 2: Project 端点未被前端使用**

```
前端:
  POST /api/project         ← 创建项目
  PUT /api/project/{id}     ← 更新项目  
  DELETE /api/project/{id}  ← 删除项目

后端:
  ProjectController 实现完整 CRUD:
    GET /api/project              ← 分页列表 (前端不用！)
    GET /api/project/{id}         ← 单个项目
    POST /api/project
    PUT /api/project/{id}
    DELETE /api/project/{id}
```

**问题：**
- `GET /api/project` 返回分页列表，但前端用 `/api/workflow/workspace` 获取列表
- 功能重复！

#### **问题 3: Chapters 端点确认**

```
前端期望:
  GET /api/chapters/project/{projectId}  ← 获取项目所有章节

后端实现:
  ChaptersController:
    ✅ GET /api/chapters/project/{projectId}  存在
    GET /api/chapters/{id}
    PUT /api/chapters/{id}
    DELETE /api/chapters/{id}
```

**状态：** ✅ 端点存在，前端可用

#### **问题 4: Workflow 职责不清**

```
WorkflowController (/api/workflow):
  ✅ GET /api/workflow/volumes          卷管理
  ✅ POST /api/workflow/volumes
  ✅ GET /api/workflow/volumes/{id}
  ❌ GET /api/workflow/workspace        项目列表（不属于 workflow！）
```

---

## 2️⃣ 冗余端点清单

| 功能 | 端点 1 | 端点 2 | 前端使用 | 建议 |
|------|--------|--------|---------|------|
| 项目列表 | `GET /api/project` | `GET /api/workflow/workspace` | 后者 | 删除前者或合并 |
| 工作空间状态 | `GET /api/workspace` | `GET /api/workflow/workspace` | 都用 | 合并为一个端点 |

---

## 3️⃣ 推荐重构方案

### 方案 A: 最小改动（推荐）

**目标：** 保持前端不变，只整理后端

**步骤：**

1. **删除 `/api/workflow/workspace`，移动到 `/api/workspace`**
   ```csharp
   // WorkspaceController.cs
   [HttpGet]
   public async Task<IActionResult> Get(CancellationToken ct)
   {
       var userId = _currentUserService.GetUserId();
       var workspace = await _workflowService.GetWorkspaceAsync(userId, ct);
       return Ok(workspace);
   }
   ```

2. **保留 `GET /api/project` 用于管理后台**
   - 前端用 `/api/workspace` 获取列表
   - 管理后台用 `/api/project` 分页查询

3. **清理 WorkflowController 职责**
   ```
   /api/workflow/volumes/*    ← 只管理卷
   /api/workspace             ← 管理工作空间和项目列表
   ```

4. **更新前端 API 调用**
   ```typescript
   // src/api/index.ts
   - export const getWorkflowWorkspace = () => get('/workflow/workspace');
   + export const getWorkspace = () => get<WorkspaceResponse>('/workspace');
   ```

---

### 方案 B: 彻底重构

**目标：** RESTful 规范化

```
GET    /api/workspace                    ← 工作空间概览
GET    /api/workspace/projects           ← 项目列表（带统计）
POST   /api/workspace/projects           ← 创建项目
GET    /api/workspace/projects/{id}      ← 项目详情
PUT    /api/workspace/projects/{id}      ← 更新项目
DELETE /api/workspace/projects/{id}      ← 删除项目

GET    /api/projects/{id}/volumes        ← 项目的卷
GET    /api/projects/{id}/chapters       ← 项目的章节
GET    /api/projects/{id}/characters     ← 项目的角色
GET    /api/projects/{id}/materials      ← 项目的素材
```

**问题：** 需要大量前端改动

---

## 4️⃣ 决策建议

**立即执行（P0）：**
1. ✅ 移动 `GetWorkspaceAsync` 从 `WorkflowService` 到 `WorkspaceService`
2. ✅ 更新 `WorkspaceController.Get()` 调用新方法
3. ✅ 前端改用 `/api/workspace` 而不是 `/api/workflow/workspace`
4. ✅ 删除 `WorkflowController.GetWorkspace()`

**未来优化（P2）：**
- 重构为嵌套资源路由
- 统一错误响应格式
- 添加 API 版本控制

---

## 5️⃣ 当前 API 数量统计

- **后端实现**: 11 个 Controller, 60+ 个端点
- **前端调用**: 8 个模块, 50+ 个接口
- **冗余端点**: 2 个（workspace 相关）
- **未使用端点**: 10+ 个（admin 监控相关）
