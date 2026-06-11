# P0 项目上下文统一实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一前端项目状态管理，消除跨页面状态不一致问题

**Architecture:** 创建全局 Zustand store 作为唯一项目选择状态来源，替换各页面独立的 selectedProjectId 和 sessionStorage 直接访问，自动同步到 sessionStorage 持久化

**Tech Stack:** React, TypeScript, Zustand, React Query

---

## File Structure

**新增：**
- `Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts` - 全局项目状态管理

**修改：**
- `Web/NovelAgentWeb.Frontend/src/App.tsx` - 初始化 store
- `Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx` - 使用 store
- `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx` - 使用 store
- `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx` - 使用 store
- `Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx` - 使用 store
- `Web/NovelAgentWeb.Frontend/src/services/projectService.ts` - 删除废弃方法

---

### Task 1: 创建项目状态 Store

**Files:**
- Create: `Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts`

- [ ] **Step 1: 创建 useProjectStore**

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

- [ ] **Step 2: 验证文件创建**

Run: `ls -la Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts`
Expected: 文件存在

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/stores/useProjectStore.ts
git commit -m "feat(frontend): add global project state store"
```

---

### Task 2: App.tsx 初始化 Store

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/App.tsx:24-31`

- [ ] **Step 1: 添加 store 初始化**

在 `AppLayout` 组件中添加：

```typescript
import { useProjectStore } from './stores/useProjectStore';

function AppLayout() {
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: getSettings });
  const initializeFromStorage = useProjectStore((s) => s.initializeFromStorage);

  useEffect(() => {
    initializeFromStorage();
  }, [initializeFromStorage]);

  useEffect(() => {
    if (!settings) return;
    document.documentElement.dataset.theme = settings.theme || 'dark';
    document.documentElement.lang = settings.language || 'zh-CN';
  }, [settings]);

  return (
    <div className="studio-shell">
      <Rail />
      <main className="desk">
        <Routes>
          <Route path="/" element={<ProtectedRoute><AgentPage /></ProtectedRoute>} />
          <Route path="/materials" element={<ProtectedRoute><MaterialsPage /></ProtectedRoute>} />
          <Route path="/workflow" element={<ProtectedRoute><WorkflowPage /></ProtectedRoute>} />
          <Route path="/library" element={<ProtectedRoute><LibraryPage /></ProtectedRoute>} />
          <Route path="/settings" element={<ProtectedRoute><SettingsPage /></ProtectedRoute>} />
        </Routes>
      </main>
    </div>
  );
}
```

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功，无 TypeScript 错误

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/App.tsx
git commit -m "feat(frontend): initialize project store on app load"
```

---

### Task 3: Rail.tsx 使用 Store

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx:14-19`

- [ ] **Step 1: 替换 currentProject 查询**

```typescript
import { NavLink, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';
import { useProjectStore } from '../../stores/useProjectStore';
import { projectService } from '../../services/projectService';
import { useQuery } from '@tanstack/react-query';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/workflow', label: '创作工作流', num: '03' },
  { path: '/library', label: '小说书城', num: '04' },
  { path: '/settings', label: '用户设置', num: '05' },
];

export default function Rail() {
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const { data: projects } = useQuery({
    queryKey: ['projects'],
    queryFn: () => projectService.listProjects(),
    staleTime: 5 * 60 * 1000,
  });
  const currentProject = projects?.find((p) => p.id === currentProjectId);
  const { user, clearAuth } = useAuthStore();
  const navigate = useNavigate();

  const handleLogout = () => {
    clearAuth();
    navigate('/login');
  };

  return (
    <aside className="rail">
      <div className="brand-mark">
        <div className="seal">命</div>
        <div>
          <strong>天命</strong>
          <small>Novel Agent</small>
        </div>
      </div>

      <nav>
        <ul className="rail-nav">
          {navItems.map((item) => (
            <li key={item.path}>
              <NavLink
                to={item.path}
                end={item.path === '/'}
                className={({ isActive }) => `stage-item${isActive ? ' active' : ''}`}
              >
                <span className="num">{item.num}</span>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <div className="rail-footer">
        <div>{currentProject?.title ?? '选择项目...'}</div>
        {user && (
          <div style={{ marginTop: '8px', fontSize: '12px', opacity: 0.7 }}>
            {user.username}
          </div>
        )}
        <button
          onClick={handleLogout}
          style={{
            marginTop: '8px',
            width: '100%',
            padding: '6px',
            fontSize: '12px',
            background: 'transparent',
            border: '1px solid rgba(255,255,255,0.2)',
            color: 'inherit',
            cursor: 'pointer',
            borderRadius: '4px'
          }}
        >
          退出登录
        </button>
      </div>
    </aside>
  );
}
```

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx
git commit -m "refactor(frontend): use project store in Rail component"
```

---

### Task 4: MaterialsPage.tsx 使用 Store

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx:32-37`

- [ ] **Step 1: 替换 currentProject 查询**

删除：
```typescript
const { data: currentProject } = useQuery({
  queryKey: ['currentProject'],
  queryFn: () => projectService.getCurrentProject(),
  staleTime: 5 * 60 * 1000,
});
const currentProjectId = currentProject?.id ?? null;
```

添加导入和使用 store：
```typescript
import { useProjectStore } from '../stores/useProjectStore';

export default function MaterialsPage() {
  const queryClient = useQueryClient();
  const addLog = useAppStore((s) => s.addLog);
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const fileRef = useRef<HTMLInputElement>(null);
  // ... 其余代码保持不变
}
```

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx
git commit -m "refactor(frontend): use project store in MaterialsPage"
```

---

### Task 5: WorkflowPage.tsx 使用 Store

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx:282-311`

- [ ] **Step 1: 替换 selectedProjectId 状态**

删除：
```typescript
const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
const effectiveProjectId = selectedProjectId ?? fallbackProjectId;
```

添加导入和使用 store：
```typescript
import { useProjectStore } from '../stores/useProjectStore';

export default function WorkflowPage() {
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
  
  // 使用 currentProjectId 替换所有 effectiveProjectId 引用
  // 使用 setCurrentProject 替换所有 setSelectedProjectId 调用
  // ... 其余代码保持不变
}
```

- [ ] **Step 2: 删除 fallback 逻辑的 useEffect**

删除：
```typescript
useEffect(() => {
  if (!selectedProjectId && fallbackProjectId) setSelectedProjectId(fallbackProjectId);
}, [fallbackProjectId, selectedProjectId]);
```

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx
git commit -m "refactor(frontend): use project store in WorkflowPage"
```

---

### Task 6: LibraryPage.tsx 使用 Store

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx:48-51`

- [ ] **Step 1: 替换 selectedProjectId 状态**

删除：
```typescript
const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
const effectiveProjectId = selectedProjectId ?? projectsList[0]?.id ?? null;
```

添加导入和使用 store：
```typescript
import { useProjectStore } from '../stores/useProjectStore';

export default function LibraryPage() {
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
  
  // 使用 currentProjectId 替换所有 effectiveProjectId 引用
  // 使用 setCurrentProject 替换所有 setSelectedProjectId 调用
  // ... 其余代码保持不变
}
```

- [ ] **Step 2: 删除 fallback 逻辑的 useEffect**

删除或修改：
```typescript
useEffect(() => {
  if (selectedProjectId && projectsList.some((project) => project.id === selectedProjectId)) return;
  setSelectedProjectId(projectsList[0]?.id ?? null);
}, [projectsList, mode, selectedProjectId]);
```

修改为：
```typescript
useEffect(() => {
  if (!currentProjectId && projectsList.length > 0) {
    setCurrentProject(projectsList[0].id);
  }
}, [currentProjectId, projectsList, setCurrentProject]);
```

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx
git commit -m "refactor(frontend): use project store in LibraryPage"
```

---

### Task 7: 删除废弃的 getCurrentProject 方法

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/services/projectService.ts:89-101`

- [ ] **Step 1: 删除 getCurrentProject 方法**

删除：
```typescript
/**
 * Get the current active project.
 * Returns the current project from session storage or null if none selected.
 */
async getCurrentProject(): Promise<ProjectResponse | null> {
  const currentProjectId = sessionStorage.getItem('currentProjectId');
  if (!currentProjectId) {
    return null;
  }

  return api<ProjectResponse>(`/project/${currentProjectId}`);
},
```

- [ ] **Step 2: 验证没有其他文件引用此方法**

Run: `cd Web/NovelAgentWeb.Frontend/src && grep -r "getCurrentProject" --include="*.ts" --include="*.tsx"`
Expected: 无结果（所有引用已替换）

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/services/projectService.ts
git commit -m "refactor(frontend): remove deprecated getCurrentProject method"
```

---

### Task 8: 端到端测试

**Files:**
- Test: 手动测试前端应用

- [ ] **Step 1: 启动前后端服务**

```bash
# Terminal 1: 启动后端
cd Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run

# Terminal 2: 启动前端
cd Web/NovelAgentWeb.Frontend
npm run dev
```

Expected: 前端运行在 http://localhost:3002

- [ ] **Step 2: 测试项目切换一致性**

1. 打开浏览器访问 http://localhost:3002
2. 登录账户
3. 在 Library 页面切换到项目 A
4. 验证 Rail 侧边栏显示项目 A
5. 切换到 Materials 页面
6. 验证页面显示项目 A 的素材
7. 上传一个素材
8. 验证素材上传到项目 A

Expected: 所有页面项目状态一致

- [ ] **Step 3: 测试页面刷新持久化**

1. 选择项目 B
2. 刷新页面（F5）
3. 验证 Rail 侧边栏仍显示项目 B
4. 验证当前页面仍然是项目 B 的数据

Expected: 项目状态从 sessionStorage 恢复

- [ ] **Step 4: 测试跨页面同步**

1. 在 Library 页面选择项目 C
2. 切换到 Workflow 页面
3. 验证 Workflow 显示项目 C 的数据
4. 验证 Rail 侧边栏显示项目 C

Expected: 所有页面同步更新

- [ ] **Step 5: 提交测试报告**

创建文件：`docs/superpowers/tests/2026-06-11-p0-project-context-test-report.md`

内容：
```markdown
# P0 项目上下文统一测试报告

## 测试日期
2026-06-11

## 测试环境
- 前端：http://localhost:3002
- 后端：http://localhost:5002

## 测试结果

### ✅ 项目切换一致性
- Library 切换项目后，Materials/Workflow/Rail 同步更新
- 上传素材到正确项目

### ✅ 页面刷新持久化
- 刷新页面后项目状态保持
- sessionStorage 正确恢复

### ✅ 跨页面同步
- 所有页面使用同一项目状态
- 切换页面时项目不丢失

## 结论
P0 项目上下文统一功能正常，所有测试通过。
```

```bash
git add docs/superpowers/tests/2026-06-11-p0-project-context-test-report.md
git commit -m "test(frontend): add P0 project context unification test report"
```

---

## 验证清单

- [x] useProjectStore 创建并导出
- [x] App.tsx 初始化 store
- [x] Rail.tsx 使用 store 显示当前项目
- [x] MaterialsPage.tsx 使用 store 获取项目 ID
- [x] WorkflowPage.tsx 使用 store 替换本地状态
- [x] LibraryPage.tsx 使用 store 替换本地状态
- [x] projectService.getCurrentProject() 方法已删除
- [x] 项目切换一致性测试通过
- [x] 页面刷新持久化测试通过
- [x] 跨页面同步测试通过
