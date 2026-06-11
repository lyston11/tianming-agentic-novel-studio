# 前端技术债全面修复设计规范

## 目标

解决前端架构中的多个技术债累积问题，提升代码质量和用户体验一致性。

## 问题清单

### P0：项目上下文不统一（核心痛点）
- Materials/Rail 依赖 `sessionStorage.currentProjectId`（通过 `projectService.getCurrentProject()`）
- WorkflowPage 维护独立的 `selectedProjectId` 状态
- LibraryPage 维护独立的 `selectedProjectId` 状态
- 跨页面项目状态不同步，用户体验："我在操作哪个项目？"不稳定

### P1：Workflow 数据流断点
- WorkflowPage.tsx (line 287) `books` 被写死为空数组
- 注释说明完整 workflow API 迁移未完成
- **状态：需要实现完整的 Workflow API 和前端集成**

### P2：样式系统历史层叠
- StudioShell.tsx 和 styles.css 是废弃的旧版布局（无任何引用）
- 当前实际入口：App.tsx → Rail.tsx + layout.css/components.css
- library.css、workflow.css 存在新旧命名混杂
- 全局样式分散在多个文件

### P3：Auth 页面体验断层
- 英文文案（"Sign in", "Create account"）
- 内联样式，与主应用设计语言脱节
- auth.css 存在但未充分利用

### P4：发布流程缺失
- 前端 build 产物在 `dist/`
- 后端静态目录 `wwwroot/` 当前为空
- Program.cs (line 248) 配置了 UseStaticFiles() 和 SPA fallback
- 缺少 `dist/` → `wwwree/` 同步机制

## 架构设计

### 1. 项目上下文统一（P0）

**核心原则：单一数据源 + 全局状态管理**

#### 新增文件：`src/stores/useProjectStore.ts`

```typescript
import { create } from 'zustand';

interface ProjectState {
  currentProjectId: string | null;
  setCurrentProject: (id: string | null) => void;
  initializeFromStorage: () => void;
}

export const useProjectStore = create<ProjectState>((set) => ({
  currentProjectId: null,

  setCurrentProject: (id) => {
    set({ currentProjectId: id });
    if (id) {
      sessionStorage.setItem('currentProjectId', id);
    } else {
      sessionStorage.removeItem('currentProjectId');
    }
  },

  initializeFromStorage: () => {
    const stored = sessionStorage.getItem('currentProjectId');
    if (stored) set({ currentProjectId: stored });
  },
}));
```

**职责：**
- 唯一的项目选择状态来源
- 自动同步到 sessionStorage 持久化
- 提供跨页面一致的 currentProjectId

#### 迁移路径

**1. Rail.tsx 改造**
```typescript
// 旧：useQuery(['currentProject'], projectService.getCurrentProject)
// 新：useProjectStore()
const currentProjectId = useProjectStore((s) => s.currentProjectId);
const { data: projects } = useQuery(['projects'], () => projectService.listProjects());
const currentProject = projects?.find((p) => p.id === currentProjectId);
```

**2. MaterialsPage.tsx 改造**
```typescript
// 删除：
// const { data: currentProject } = useQuery({
//   queryKey: ['currentProject'],
//   queryFn: () => projectService.getCurrentProject(),
// });
// const currentProjectId = currentProject?.id ?? null;

// 新：
const currentProjectId = useProjectStore((s) => s.currentProjectId);
```

**3. WorkflowPage.tsx 改造**
```typescript
// 删除：
// const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
// const effectiveProjectId = selectedProjectId ?? fallbackProjectId;

// 新：
const currentProjectId = useProjectStore((s) => s.currentProjectId);
const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
```

**4. LibraryPage.tsx 改造**
```typescript
// 删除本地 selectedProjectId 状态
// 新：直接使用 useProjectStore
const currentProjectId = useProjectStore((s) => s.currentProjectId);
const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
```

**5. App.tsx 初始化**
```typescript
// 在 AppLayout 组件中调用
const initializeFromStorage = useProjectStore((s) => s.initializeFromStorage);
useEffect(() => {
  initializeFromStorage();
}, [initializeFromStorage]);
```

**6. 删除废弃 API**
- `projectService.getCurrentProject()` 在 `src/services/projectService.ts` 中删除

---

### 2. 样式系统收敛（P2）

**核心原则：单一职责 + 命名规范 + 减少冗余**

#### 删除废弃文件
- `src/components/layout/StudioShell.tsx`（无任何 import 引用）
- `src/styles/styles.css`（旧版全局样式）

#### 样式文件职责

**`variables.css`** - CSS 变量定义
```css
:root {
  /* 从 styles.css 合并的变量 */
  --ink: #16110d;
  --ink-2: #2a211a;
  --paper: #f4ecd9;
  --paper-2: #e5d5b7;
  --line: rgba(40, 29, 19, 0.18);
  --red: #b83b2f;
  --red-dark: #7e241f;
  --jade: #2f7564;
  --gold: #b8893f;
  --blue: #315f7d;
  --muted: #786b5c;
  --shadow: 0 24px 70px rgba(13, 9, 6, 0.26);
}
```

**`global.css`** - 全局重置和 body 样式
```css
*, *::before, *::after {
  box-sizing: border-box;
}

html, body {
  min-height: 100%;
  margin: 0;
}

body {
  color: var(--ink);
  background:
    linear-gradient(90deg, rgba(255, 255, 255, 0.025) 1px, transparent 1px),
    linear-gradient(180deg, rgba(255, 255, 255, 0.018) 1px, transparent 1px),
    radial-gradient(circle at 15% 10%, rgba(184, 59, 47, 0.18), transparent 26rem),
    radial-gradient(circle at 92% 18%, rgba(47, 117, 100, 0.22), transparent 30rem),
    #17110c;
  background-size: 42px 42px, 42px 42px, auto, auto, auto;
  font-family: "Songti SC", "Noto Serif SC", "STSong", Georgia, serif;
}

button, input, textarea {
  font: inherit;
}

button {
  cursor: pointer;
}

a {
  color: inherit;
  text-decoration: none;
}
```

**`layout.css`** - 布局结构
- `.studio-shell` - 主容器
- `.rail` - 左侧导航栏
- `.desk` - 主内容区

**`components.css`** - 通用组件样式
- 按钮、卡片、模态框等可复用组件

**页面专属样式文件：**
- `agent.css` - Agent 对话页
- `materials.css` - 创意知识库页
- `workflow.css` - 创作工作流页
- `library.css` - 小说书城页
- `settings.css` - 用户设置页
- `auth.css` - 登录注册页

#### CSS 类名规范

**页面级前缀：**
```css
.agent-*       /* Agent 页面 */
.workflow-*    /* Workflow 页面 */
.library-*     /* Library 页面 */
.materials-*   /* Materials 页面 */
```

**布局级前缀：**
```css
.rail-*        /* 左侧导航栏组件 */
.desk-*        /* 主内容区组件 */
```

**通用组件前缀：**
```css
.btn-*         /* 按钮 */
.card-*        /* 卡片 */
.modal-*       /* 模态框 */
```

#### 迁移步骤

1. 从 `styles.css` 提取 CSS 变量追加到 `variables.css`
2. 从 `styles.css` 提取 body 样式迁移到 `global.css`
3. 删除 `styles.css` 和 `StudioShell.tsx`
4. 清理各页面样式文件中的重复定义
5. 统一类名前缀

---

### 2. Workflow 数据流接入（P1）

**核心原则：分级加载 + 按需查询**

#### API 设计

**新增端点：GET /api/workflow/workspace**

**响应格式：**
```typescript
{
  projects: NovelBookView[],  // 精简版项目列表
  totalCount: number
}
```

**NovelBookView 字段（精简版）：**
- 基础信息：`projectId`, `title`, `genre`, `subGenre`, `coreHook`, `status`, `isActive`
- 统计信息：`volumeCount`, `generatedChapterCount`, `plannedChapterCount`, `needsRewriteCount`
- 时间戳：`updatedAt`
- **不包含**：`volumes[]`, `chapters[]`, `selectedChapter`（按需加载）

**排序规则：**
- 按项目最近活动时间降序（`Project.UpdatedAt` 或关联 `AgentSession` 的最新 `UpdatedAt`）
- 仅返回活跃项目（`status != "archived"`）
- 限制前 50 个（性能考虑）

#### 后端实现

**文件：`Web/NovelAgentWeb/Services/WorkflowService.cs`（新增）**

**方法：`GetWorkspaceAsync()`**

实现步骤：
1. 查询用户所有活跃项目（`NovelProjects` 表，`Status != "archived"`，按 `UpdatedAt DESC`）
2. 批量查询统计信息（使用 `GROUP BY` 避免 N+1）：
   - `VolumeCount`：`COUNT(Volumes WHERE ProjectId IN ...)`
   - `GeneratedChapterCount`：`COUNT(Chapters WHERE Status = "committed")`
   - `PlannedChapterCount`：`COUNT(Chapters WHERE Status IN ("planned", "draft_generated", ...))`
   - `NeedsRewriteCount`：`COUNT(Chapters WHERE NeedsRewrite = true)`
3. 查询最近活动时间（可选优化）：每个项目关联的最新 `AgentSession.UpdatedAt`
4. 映射到 `NovelBookView`：组装统计数据，**不加载导航属性**（`Volumes.Include`/`Chapters.Include`）

**性能优化：**
- 使用批量查询和 `GROUP BY` 聚合统计
- 不加载 `Chapter.Content` 字段（大文本）
- 限制返回前 50 个项目
- 添加数据库索引：`Chapters(ProjectId, Status)`, `AgentSessions(ActiveProjectId, UpdatedAt)`

**Controller 端点：`Web/NovelAgentWeb/Controllers/WorkflowController.cs`**

```csharp
[HttpGet("workspace")]
[Authorize]
public async Task<IActionResult> GetWorkspace(CancellationToken ct)
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId))
        return Unauthorized();

    var workspace = await _workflowService.GetWorkspaceAsync(userId, ct);
    return Ok(workspace);
}
```

#### 前端集成

**修改：`Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx`**

**删除硬编码：**
```typescript
// 删除：const books: NovelBookView[] = [];
```

**新增查询：**
```typescript
const { data: workspaceData } = useQuery({
  queryKey: ['workspace'],
  queryFn: () => getWorkspace(),
  staleTime: 30_000,
});
const books = workspaceData?.projects ?? [];
```

**新增 API 函数：`src/api/index.ts`**

```typescript
export interface WorkspaceResponse {
  projects: NovelBookView[];
  totalCount: number;
}

export const getWorkspace = () => 
  get<WorkspaceResponse>('/workflow/workspace');
```

**保持现有 UI 逻辑不变：**
- 项目卡片展示基础信息 + 统计（`volumeCount`, `generatedChapterCount` 等）
- 点击项目后，通过现有端点加载详情（如 `/api/chapters?projectId=xxx`）

#### 数据流架构

```
用户打开 Workflow 页面
  ↓
GET /api/workflow/workspace
  ↓
返回 50 个项目 + 统计信息（轻量级）
  ↓
用户选择某个项目
  ↓
GET /api/chapters?projectId=xxx（现有端点）
GET /api/runs?projectId=xxx（现有端点）
  ↓
加载该项目的完整 volumes/chapters/runs
```

#### Artifacts 数据来源

**前端期望的 `WorkflowChapterArtifactSummary` 从哪里来？**

根据代码分析（`ProjectWorkflow.cs` line 103-131），artifacts 是从 `AgentRun` 聚合而来：

```csharp
BuildChapterArtifacts(runs) {
  foreach (var run in runs.Where(r => !string.IsNullOrEmpty(r.TargetChapterId))) {
    yield return new WorkflowChapterArtifactSummary(
      run.TargetChapterId,
      run.RunId,
      run.Intent,
      run.Status,
      run.DraftArtifact?.Status,           // 从 AgentRun.DraftArtifact
      run.DraftArtifact?.DraftContent,     // 草稿内容（临时）
      run.GateReport?.Status,              // 门禁报告
      run.PostGenerationReview,            // 质量评审
      run.ContextPackage?.Warnings         // 上下文警告
    );
  }
}
```

**存储位置：**
- **草稿阶段**：`AgentRun.DraftArtifact.DraftContent`（JSON 字段）
- **提交阶段**：`AgentRun.DraftArtifact.CommittedContent` + 文件系统
- **入库阶段**：`Chapter.ContentPath`（文件路径）

**前端无需单独查询 artifacts 表**，通过 `AgentRun` 即可获取所有产物信息。

#### 依赖关系

- WorkflowPage 依赖 `/api/workflow/workspace` 返回项目列表
- 详情加载依赖现有端点：
  - `/api/chapters?projectId=xxx`
  - `/api/runs?projectId=xxx`
  - `/api/storybible?projectId=xxx`

#### 测试验证

1. **空状态测试**：新用户无项目，返回空数组
2. **单项目测试**：返回项目基础信息 + 统计正确
3. **多项目测试**：验证排序正确（最近活动在前）
4. **性能测试**：50 个项目加载时间 < 500ms
5. **点击项目测试**：验证详情加载正常

---

### 3. Auth 页面改造（P3）

**核心原则：设计语言一致 + 中文本地化**

#### 问题
- LoginPage.tsx 和 RegisterPage.tsx 使用英文文案
- 大量内联 style 属性
- 与主应用的"Editorial Archive"设计语言脱节

#### 设计方向

使用 frontend-design skill 重新设计，保持与 Agent 页面会话历史一致的美学：
- 衬线字体（Crimson Pro）
- 中文文案
- 渐变背景和动画效果
- 独立的 auth.css 文件

#### 文案本地化

**LoginPage.tsx:**
- "Sign in to your account" → "登录账户"
- "Username" → "用户名"
- "Password" → "密码"
- "Sign In" → "登录"
- "Don't have an account?" → "还没有账户？"
- "Register" → "注册"

**RegisterPage.tsx:**
- "Create your account" → "创建账户"
- "Username" → "用户名"
- "Email" → "邮箱"
- "Password" → "密码"
- "Register" → "注册"
- "Already have an account?" → "已有账户？"
- "Sign in" → "登录"

#### 样式改造

**移除所有内联 style，使用 auth.css 类名：**
```css
.auth-container
.auth-card
.auth-title
.auth-form
.auth-input-group
.auth-label
.auth-input
.auth-button
.auth-link
.auth-error
```

---

### 4. 发布流程配置（P4）

**核心原则：自动化 + 文档化**

#### 方案 A：npm 脚本自动同步（推荐）

**修改 `package.json`:**
```json
{
  "scripts": {
    "build": "vite build",
    "postbuild": "npm run sync-to-backend",
    "sync-to-backend": "rm -rf ../NovelAgentWeb/wwwroot/* && cp -r dist/* ../NovelAgentWeb/wwwroot/"
  }
}
```

**工作流：**
1. 开发者运行 `npm run build`
2. Vite 构建产物到 `dist/`
3. `postbuild` 钩子自动执行，同步到 `wwwroot/`

#### 方案 B：文档化手动步骤

**在 `README.md` 中添加：**

```markdown
## 前端构建和部署

### 开发环境
```bash
cd Web/NovelAgentWeb.Frontend
npm run dev
```

### 生产构建
```bash
cd Web/NovelAgentWeb.Frontend
npm run build

# 同步到后端静态目录
rm -rf ../NovelAgentWeb/wwwroot/*
cp -r dist/* ../NovelAgentWeb/wwwroot/
```

### 部署
```bash
cd Web/NovelAgentWeb
dotnet publish -c Release
```

**采用方案 A（自动化脚本）**

---

## 实现优先级

1. **P0 项目上下文统一** - 最高优先级，影响所有页面
2. **P1 Workflow 数据流接入** - 前后端协同，核心功能
3. **P2 样式系统收敛** - 提升代码可维护性
4. **P3 Auth 页面改造** - 提升视觉一致性
5. **P4 发布流程配置** - 部署必需

---

## 影响范围

### 修改文件列表

**新增：**
- `src/stores/useProjectStore.ts`
- `Web/NovelAgentWeb/Services/WorkflowService.cs`
- `Web/NovelAgentWeb/Controllers/WorkflowController.cs`

**修改：**
- `src/App.tsx`
- `src/components/layout/Rail.tsx`
- `src/pages/MaterialsPage.tsx`
- `src/pages/WorkflowPage.tsx`
- `src/pages/LibraryPage.tsx`
- `src/pages/LoginPage.tsx`
- `src/pages/RegisterPage.tsx`
- `src/api/index.ts`
- `src/styles/variables.css`
- `src/styles/global.css`
- `src/styles/auth.css`
- `Web/NovelAgentWeb.Frontend/package.json`

**删除：**
- `src/components/layout/StudioShell.tsx`
- `src/styles/styles.css`
- `src/services/projectService.ts` 中的 `getCurrentProject()` 方法

---

## 测试策略

### 功能测试
1. **项目切换一致性**
   - 在 Library 切换项目 → 验证 Materials 页面显示正确项目
   - 在 Materials 上传素材 → 验证上传到正确项目
   - 刷新页面 → 验证项目状态从 sessionStorage 恢复

2. **Workflow 数据加载**
   - 打开 Workflow 页面 → 验证项目列表加载正确
   - 验证统计信息准确（volumeCount、generatedChapterCount 等）
   - 验证排序正确（最近活动的项目在前）
   - 性能测试：50 个项目加载时间 < 500ms

3. **样式一致性**
   - 验证所有页面背景、字体、颜色一致
   - 验证 Auth 页面设计语言与主应用匹配

4. **发布流程**
   - 运行 `npm run build` → 验证 wwwroot 目录更新
   - 运行后端 `dotnet run` → 访问 http://localhost:5002 验证静态资源加载

### 回归测试
- Agent 对话功能
- Materials 上传/知识库浏览
- Library 项目列表和章节阅读
- Workflow 项目卡片和统计显示
- Settings 用户设置保存

---

## 风险评估

**高风险：**
- 项目状态管理改造涉及多个页面，可能出现边界情况遗漏
- Workflow API 新增端点，后端查询性能需验证

**中风险：**
- 样式文件合并可能引入覆盖冲突
- Workflow 数据聚合逻辑复杂（统计查询、排序）

**低风险：**
- Auth 页面改造独立，不影响主流程
- 发布脚本纯工具配置

**缓解措施：**
- 分阶段提交，每个 P 独立测试后再进行下一个
- 保留 git 历史，出问题可快速回滚
- 手动测试核心流程（项目切换、素材上传、Workflow 加载、页面导航）
- 后端添加数据库索引优化查询性能
- 使用批量查询和 GROUP BY 避免 N+1 问题

---

## 成功标准

1. ✅ 用户在任意页面切换项目，所有页面状态同步更新
2. ✅ Workflow 页面显示活跃项目列表，统计信息准确
3. ✅ Workflow 项目列表加载时间 < 500ms（50 个项目）
4. ✅ 不存在废弃的未使用文件（StudioShell.tsx, styles.css）
5. ✅ CSS 类名遵循统一命名规范
6. ✅ Auth 页面使用中文文案和统一设计风格
7. ✅ `npm run build` 后 `wwwroot/` 自动更新
8. ✅ 所有原有功能正常工作
