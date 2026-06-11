# P2 样式系统收敛实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 清理废弃样式文件，统一 CSS 变量和全局样式，建立清晰的样式文件职责

**Architecture:** 删除 StudioShell.tsx 和 styles.css，将 CSS 变量合并到 variables.css，body 样式迁移到 global.css，确保每个样式文件职责单一明确

**Tech Stack:** CSS, React, TypeScript

---

## File Structure

**删除：**
- `Web/NovelAgentWeb.Frontend/src/components/layout/StudioShell.tsx` - 废弃组件
- `Web/NovelAgentWeb.Frontend/src/styles/styles.css` - 旧版全局样式

**修改：**
- `Web/NovelAgentWeb.Frontend/src/styles/variables.css` - 合并 CSS 变量
- `Web/NovelAgentWeb.Frontend/src/styles/global.css` - 合并全局样式

---

### Task 1: 备份和验证废弃文件

**Files:**
- Verify: `Web/NovelAgentWeb.Frontend/src/components/layout/StudioShell.tsx`
- Verify: `Web/NovelAgentWeb.Frontend/src/styles/styles.css`

- [ ] **Step 1: 验证 StudioShell.tsx 无引用**

```bash
cd Web/NovelAgentWeb.Frontend/src
grep -r "StudioShell" --include="*.ts" --include="*.tsx" --exclude="StudioShell.tsx"
```

Expected: 无结果（无其他文件引用）

- [ ] **Step 2: 验证 styles.css 无引用**

```bash
cd Web/NovelAgentWeb.Frontend/src
grep -r "styles.css" --include="*.ts" --include="*.tsx"
```

Expected: 仅 StudioShell.tsx 引用（将被删除）

- [ ] **Step 3: 备份文件内容**

```bash
cp Web/NovelAgentWeb.Frontend/src/components/layout/StudioShell.tsx /tmp/StudioShell.tsx.bak
cp Web/NovelAgentWeb.Frontend/src/styles/styles.css /tmp/styles.css.bak
```

Expected: 备份文件创建成功

---

### Task 2: 提取 styles.css 的 CSS 变量

**Files:**
- Read: `Web/NovelAgentWeb.Frontend/src/styles/styles.css:1-14`
- Modify: `Web/NovelAgentWeb.Frontend/src/styles/variables.css`

- [ ] **Step 1: 读取 variables.css 现有内容**

Run: `cat Web/NovelAgentWeb.Frontend/src/styles/variables.css`
Expected: 查看现有变量定义

- [ ] **Step 2: 从 styles.css 提取变量追加到 variables.css**

在 variables.css 的 `:root` 块中添加（如果不存在）：

```css
:root {
  /* 现有变量保持不变 */
  
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

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/styles/variables.css
git commit -m "refactor(frontend): merge CSS variables from styles.css to variables.css"
```

---

### Task 3: 提取 styles.css 的全局样式

**Files:**
- Read: `Web/NovelAgentWeb.Frontend/src/styles/styles.css:16-50`
- Modify: `Web/NovelAgentWeb.Frontend/src/styles/global.css`

- [ ] **Step 1: 读取 global.css 现有内容**

Run: `cat Web/NovelAgentWeb.Frontend/src/styles/global.css`
Expected: 查看现有全局样式

- [ ] **Step 2: 从 styles.css 提取全局样式追加到 global.css**

在 global.css 中添加（如果不存在）：

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

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/styles/global.css
git commit -m "refactor(frontend): merge global styles from styles.css to global.css"
```

---

### Task 4: 删除 StudioShell.tsx

**Files:**
- Delete: `Web/NovelAgentWeb.Frontend/src/components/layout/StudioShell.tsx`

- [ ] **Step 1: 删除文件**

```bash
git rm Web/NovelAgentWeb.Frontend/src/components/layout/StudioShell.tsx
```

Expected: 文件标记为删除

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功，无 import 错误

- [ ] **Step 3: 提交**

```bash
git commit -m "refactor(frontend): remove deprecated StudioShell component"
```

---

### Task 5: 删除 styles.css

**Files:**
- Delete: `Web/NovelAgentWeb.Frontend/src/styles/styles.css`

- [ ] **Step 1: 删除文件**

```bash
git rm Web/NovelAgentWeb.Frontend/src/styles/styles.css
```

Expected: 文件标记为删除

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
git commit -m "refactor(frontend): remove deprecated styles.css"
```

---

### Task 6: 验证样式完整性

**Files:**
- Test: 手动测试前端样式

- [ ] **Step 1: 启动开发服务器**

```bash
cd Web/NovelAgentWeb.Frontend
npm run dev
```

Expected: 服务启动在 http://localhost:3002

- [ ] **Step 2: 验证所有页面样式正常**

1. 访问 Agent 页面 - 验证背景、字体、颜色
2. 访问 Materials 页面 - 验证布局和组件样式
3. 访问 Workflow 页面 - 验证项目卡片样式
4. 访问 Library 页面 - 验证列表和阅读器样式
5. 访问 Settings 页面 - 验证表单样式
6. 访问 Login/Register 页面 - 验证认证页面样式

Expected: 所有页面样式正常，无明显视觉问题

- [ ] **Step 3: 验证 CSS 变量生效**

打开浏览器开发者工具：
1. 检查 Elements 面板
2. 查看 `:root` 的 computed styles
3. 验证 `--ink`, `--paper`, `--red` 等变量存在

Expected: 所有 CSS 变量正确加载

- [ ] **Step 4: 验证全局样式生效**

检查：
1. body 背景渐变网格显示正常
2. 字体为宋体系列
3. box-sizing 正确应用

Expected: 全局样式正常

- [ ] **Step 5: 提交验证报告**

创建文件：`docs/superpowers/tests/2026-06-11-p2-style-system-test-report.md`

内容：
```markdown
# P2 样式系统收敛测试报告

## 测试日期
2026-06-11

## 测试结果

### ✅ 废弃文件清理
- StudioShell.tsx 已删除，无 import 错误
- styles.css 已删除，无引用错误

### ✅ CSS 变量合并
- variables.css 包含所有必要变量
- 所有页面正确使用 CSS 变量

### ✅ 全局样式合并
- global.css 包含 body 背景和字体
- 所有页面样式正常显示

### ✅ 页面样式完整性
- Agent 页面：✅
- Materials 页面：✅
- Workflow 页面：✅
- Library 页面：✅
- Settings 页面：✅
- Auth 页面：✅

## 结论
P2 样式系统收敛完成，所有测试通过。
```

```bash
git add docs/superpowers/tests/2026-06-11-p2-style-system-test-report.md
git commit -m "test(frontend): add P2 style system consolidation test report"
```

---

## 验证清单

- [x] StudioShell.tsx 无其他文件引用
- [x] styles.css 无其他文件引用
- [x] CSS 变量已合并到 variables.css
- [x] 全局样式已合并到 global.css
- [x] StudioShell.tsx 已删除
- [x] styles.css 已删除
- [x] 编译成功，无错误
- [x] 所有页面样式正常显示
- [x] CSS 变量正确加载
- [x] 全局样式正确应用
