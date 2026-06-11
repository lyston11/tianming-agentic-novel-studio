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
- **状态：等待后端实现 workflow API，前端暂不处理**

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
2. **P2 样式系统收敛** - 提升代码可维护性
3. **P3 Auth 页面改造** - 提升视觉一致性
4. **P4 发布流程配置** - 部署必需

**P1 Workflow 数据流** - 等待后端实现，暂不处理

---

## 影响范围

### 修改文件列表

**新增：**
- `src/stores/useProjectStore.ts`

**修改：**
- `src/App.tsx`
- `src/components/layout/Rail.tsx`
- `src/pages/MaterialsPage.tsx`
- `src/pages/WorkflowPage.tsx`
- `src/pages/LibraryPage.tsx`
- `src/pages/LoginPage.tsx`
- `src/pages/RegisterPage.tsx`
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

2. **样式一致性**
   - 验证所有页面背景、字体、颜色一致
   - 验证 Auth 页面设计语言与主应用匹配

3. **发布流程**
   - 运行 `npm run build` → 验证 wwwroot 目录更新
   - 运行后端 `dotnet run` → 访问 http://localhost:5002 验证静态资源加载

### 回归测试
- Agent 对话功能
- Materials 上传/知识库浏览
- Library 项目列表和章节阅读
- Settings 用户设置保存

---

## 风险评估

**高风险：**
- 项目状态管理改造涉及多个页面，可能出现边界情况遗漏

**中风险：**
- 样式文件合并可能引入覆盖冲突

**低风险：**
- Auth 页面改造独立，不影响主流程
- 发布脚本纯工具配置

**缓解措施：**
- 分阶段提交，每个 P 独立测试后再进行下一个
- 保留 git 历史，出问题可快速回滚
- 手动测试核心流程（项目切换、素材上传、页面导航）

---

## 成功标准

1. ✅ 用户在任意页面切换项目，所有页面状态同步更新
2. ✅ 不存在废弃的未使用文件（StudioShell.tsx, styles.css）
3. ✅ CSS 类名遵循统一命名规范
4. ✅ Auth 页面使用中文文案和统一设计风格
5. ✅ `npm run build` 后 `wwwroot/` 自动更新
6. ✅ 所有原有功能正常工作
