# 天命AI写作 API 架构分析

**日期**: 2026-06-11  
**状态**: 🚨 存在严重设计冗余

> **2026-06-12 状态更新**: 关键偏差已收敛。现行项目列表/工作台使用
> `GET /api/workspace`，单项目工作流详情使用
> `GET /api/workflow/project/{projectId}`，项目 CRUD 保持 singular
> `/api/project`，章节列表使用 `/api/chapters/project/{projectId}`。
> 下文保留 2026-06-11 的历史诊断，用于解释为什么要迁移旧的
> `/api/workflow/workspace` 方案。

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
  GET /api/workflow/workspace   ← 历史方案：获取项目列表+统计
  GET /api/workflow/project/{projectId} ← 现行方案：获取单项目工作流详情

后端实现:
  WorkspaceController.Get()
    返回: { projectName, activeProjectId, storageProjectName, projectCount }
    
  WorkflowController.GetProject()
    返回: { project, volumes, chapters, sessions, tasks, runs, artifacts }
```

**问题：**
- 2026-06-11 草案中两个端点返回不同内容但都叫 "workspace"
- 2026-06-12 后 `/api/workspace` 负责项目概览，`/api/workflow/project/{projectId}` 负责详情

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
- `GET /api/project` 返回分页列表，前端工作台使用 `/api/workspace` 获取概览
- 工作台概览与项目 CRUD 已分工，旧 `/api/workflow/workspace` 不再作为现行数据源

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
  ✅ GET /api/workflow/project/{projectId} 单项目工作流详情
```

---

## 2️⃣ 冗余端点清单

| 功能 | 端点 1 | 端点 2 | 前端使用 | 建议 |
|------|--------|--------|---------|------|
| 项目列表 | `GET /api/project` | `GET /api/workspace` | 视图不同 | 保持分工：CRUD 分页 vs 工作台概览 |
| 工作流详情 | `GET /api/workflow/project/{projectId}` | 历史 `/api/workflow/workspace` | 前者 | 历史端点不再作为当前数据源 |

---

## 3️⃣ 推荐重构方案

### 方案 A: 最小改动（推荐）

**目标：** 保持前端工作台入口稳定，只整理后端职责

**步骤：**

1. **将项目概览归口到 `/api/workspace`**
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
   - 前端用 `/api/workspace` 获取概览
   - 管理后台用 `/api/project` 分页查询

3. **清理 WorkflowController 职责**
   ```
   /api/workflow/volumes/*    ← 只管理卷
   /api/workflow/project/{id} ← 单项目工作流详情
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

GET    /api/workflow/volumes?projectId={id} ← 项目的卷
GET    /api/chapters/project/{id}           ← 项目的章节
GET    /api/storybible/characters           ← 角色（按当前项目上下文）
GET    /api/materials?projectId={id}        ← 项目的素材
```

**问题：** 需要大量前端改动

---

## 4️⃣ 决策建议

**2026-06-12 已执行（P0）：**
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
