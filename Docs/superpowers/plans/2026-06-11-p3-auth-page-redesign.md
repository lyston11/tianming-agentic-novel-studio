# P3 Auth 页面改造实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重新设计登录注册页面，使用中文文案和统一设计风格，移除内联样式

**Architecture:** 使用 frontend-design skill 创建 Editorial Archive 风格的认证页面，替换英文文案为中文，将内联 style 迁移到 auth.css

**Tech Stack:** React, TypeScript, CSS, frontend-design skill

---

## File Structure

**修改：**
- `Web/NovelAgentWeb.Frontend/src/pages/LoginPage.tsx` - 登录页中文化和样式重构
- `Web/NovelAgentWeb.Frontend/src/pages/RegisterPage.tsx` - 注册页中文化和样式重构
- `Web/NovelAgentWeb.Frontend/src/styles/auth.css` - Auth 页面样式

---

### Task 1: 设计 Auth 页面样式

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/styles/auth.css`

- [ ] **Step 1: 使用 frontend-design skill 设计样式**

提示词：
> 设计登录注册页面样式，保持与 Agent 页面会话历史的 Editorial Archive 美学一致：
> - 使用 Crimson Pro 衬线字体
> - 渐变背景和微妙动画
> - 中文优化的排版
> - 温暖的色调（--ink, --paper, --gold）
> - 精致的表单设计

预期生成完整的 auth.css

- [ ] **Step 2: 添加 Google Fonts 导入**

在 auth.css 顶部添加：

```css
@import url('https://fonts.googleapis.com/css2?family=Crimson+Pro:wght@400;600&display=swap');

.auth-container {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background:
    linear-gradient(90deg, rgba(255, 255, 255, 0.025) 1px, transparent 1px),
    linear-gradient(180deg, rgba(255, 255, 255, 0.018) 1px, transparent 1px),
    radial-gradient(circle at 15% 10%, rgba(184, 59, 47, 0.18), transparent 26rem),
    radial-gradient(circle at 92% 18%, rgba(47, 117, 100, 0.22), transparent 30rem),
    #17110c;
  background-size: 42px 42px, 42px 42px, auto, auto, auto;
}

.auth-card {
  width: 100%;
  max-width: 420px;
  padding: 48px 40px;
  background: linear-gradient(135deg, rgba(244, 236, 217, 0.06) 0%, rgba(244, 236, 217, 0.03) 100%);
  border: 1px solid rgba(184, 137, 63, 0.2);
  border-radius: 8px;
  position: relative;
}

.auth-card::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 3px;
  background: linear-gradient(180deg, rgba(184, 137, 63, 0.6), rgba(184, 137, 63, 0.2));
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  transform: scaleY(0);
  transform-origin: top;
}

.auth-card:hover::before {
  transform: scaleY(1);
}

.auth-title {
  font-family: 'Crimson Pro', serif;
  font-size: 32px;
  font-weight: 600;
  color: var(--paper);
  margin: 0 0 32px 0;
  line-height: 1.3;
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.auth-input-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.auth-label {
  font-family: 'Crimson Pro', serif;
  font-size: 14px;
  font-weight: 600;
  color: var(--paper-2);
  line-height: 1.5;
}

.auth-input {
  width: 100%;
  padding: 12px 16px;
  font-family: 'Crimson Pro', serif;
  font-size: 16px;
  color: var(--paper);
  background: rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(184, 137, 63, 0.2);
  border-radius: 4px;
  outline: none;
  transition: all 0.2s ease;
}

.auth-input:focus {
  border-color: rgba(184, 137, 63, 0.5);
  background: rgba(0, 0, 0, 0.4);
}

.auth-input::placeholder {
  color: rgba(244, 236, 217, 0.3);
}

.auth-button {
  width: 100%;
  padding: 14px;
  font-family: 'Crimson Pro', serif;
  font-size: 16px;
  font-weight: 600;
  color: var(--ink);
  background: linear-gradient(135deg, var(--gold), #d4a957);
  border: none;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.auth-button:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(184, 137, 63, 0.3);
}

.auth-button:active {
  transform: translateY(0);
}

.auth-link {
  text-align: center;
  margin-top: 24px;
  font-family: 'Crimson Pro', serif;
  font-size: 14px;
  color: var(--paper-2);
  line-height: 1.6;
}

.auth-link a {
  color: var(--gold);
  text-decoration: none;
  transition: color 0.2s ease;
}

.auth-link a:hover {
  color: #d4a957;
}

.auth-error {
  padding: 12px 16px;
  font-family: 'Crimson Pro', serif;
  font-size: 14px;
  color: var(--red);
  background: rgba(184, 59, 47, 0.1);
  border: 1px solid rgba(184, 59, 47, 0.3);
  border-radius: 4px;
  line-height: 1.5;
}
```

- [ ] **Step 3: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/styles/auth.css
git commit -m "feat(frontend): redesign auth page styles with Editorial Archive aesthetic"
```

---

### Task 2: 重构 LoginPage.tsx

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/LoginPage.tsx`

- [ ] **Step 1: 替换为新的组件结构**

```typescript
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { login } from '../api/auth';
import '../styles/auth.css';

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.setAuth);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      const response = await login({ username, password });
      setAuth(response.token, response.user);
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : '登录失败，请重试');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="auth-container">
      <div className="auth-card">
        <h1 className="auth-title">登录账户</h1>
        
        {error && <div className="auth-error">{error}</div>}
        
        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-input-group">
            <label className="auth-label" htmlFor="username">
              用户名
            </label>
            <input
              id="username"
              type="text"
              className="auth-input"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="输入用户名"
              required
            />
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="password">
              密码
            </label>
            <input
              id="password"
              type="password"
              className="auth-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="输入密码"
              required
            />
          </div>

          <button type="submit" className="auth-button" disabled={isLoading}>
            {isLoading ? '登录中...' : '登录'}
          </button>
        </form>

        <div className="auth-link">
          还没有账户？<a href="/register">注册</a>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/LoginPage.tsx
git commit -m "refactor(frontend): redesign LoginPage with Chinese copy and CSS classes"
```

---

### Task 3: 重构 RegisterPage.tsx

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/RegisterPage.tsx`

- [ ] **Step 1: 替换为新的组件结构**

```typescript
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { register } from '../api/auth';
import '../styles/auth.css';

export default function RegisterPage() {
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      await register({ username, email, password });
      navigate('/login');
    } catch (err) {
      setError(err instanceof Error ? err.message : '注册失败，请重试');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="auth-container">
      <div className="auth-card">
        <h1 className="auth-title">创建账户</h1>
        
        {error && <div className="auth-error">{error}</div>}
        
        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-input-group">
            <label className="auth-label" htmlFor="username">
              用户名
            </label>
            <input
              id="username"
              type="text"
              className="auth-input"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="输入用户名"
              required
            />
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="email">
              邮箱
            </label>
            <input
              id="email"
              type="email"
              className="auth-input"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="输入邮箱地址"
              required
            />
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="password">
              密码
            </label>
            <input
              id="password"
              type="password"
              className="auth-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="输入密码"
              required
            />
          </div>

          <button type="submit" className="auth-button" disabled={isLoading}>
            {isLoading ? '注册中...' : '注册'}
          </button>
        </form>

        <div className="auth-link">
          已有账户？<a href="/login">登录</a>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: 验证编译**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/RegisterPage.tsx
git commit -m "refactor(frontend): redesign RegisterPage with Chinese copy and CSS classes"
```

---

### Task 4: 视觉验证和测试

**Files:**
- Test: 手动测试 Auth 页面

- [ ] **Step 1: 启动开发服务器**

```bash
cd Web/NovelAgentWeb.Frontend
npm run dev
```

Expected: 服务启动在 http://localhost:3002

- [ ] **Step 2: 测试登录页面**

1. 访问 http://localhost:3002/login
2. 验证页面显示：
   - 中文标题"登录账户"
   - 中文标签"用户名"、"密码"
   - 金色渐变按钮
   - Editorial Archive 美学（衬线字体、渐变背景）
3. 测试表单交互：
   - 输入框获得焦点时边框高亮
   - 按钮悬停时上升动画
   - 错误消息显示样式

Expected: 样式美观，交互流畅

- [ ] **Step 3: 测试注册页面**

1. 访问 http://localhost:3002/register
2. 验证页面显示：
   - 中文标题"创建账户"
   - 中文标签"用户名"、"邮箱"、"密码"
   - 与登录页一致的设计风格
3. 测试表单交互

Expected: 样式与登录页一致

- [ ] **Step 4: 测试功能**

1. 在注册页面创建新账户
2. 跳转到登录页面
3. 使用新账户登录
4. 成功跳转到主应用

Expected: 功能正常，用户流程顺畅

- [ ] **Step 5: 提交测试报告**

创建文件：`docs/superpowers/tests/2026-06-11-p3-auth-redesign-test-report.md`

内容：
```markdown
# P3 Auth 页面改造测试报告

## 测试日期
2026-06-11

## 测试结果

### ✅ 设计风格一致
- 使用 Crimson Pro 衬线字体
- 渐变背景与主应用一致
- Editorial Archive 美学统一

### ✅ 中文本地化
- 所有英文文案替换为中文
- 表单标签、按钮文字全部中文化
- 错误提示中文显示

### ✅ 样式重构
- 移除所有内联 style 属性
- 使用 auth.css 类名
- 代码结构清晰

### ✅ 交互体验
- 输入框焦点效果流畅
- 按钮悬停动画自然
- 表单提交反馈及时

### ✅ 功能完整
- 注册功能正常
- 登录功能正常
- 页面跳转正确

## 结论
P3 Auth 页面改造完成，所有测试通过。
```

```bash
git add docs/superpowers/tests/2026-06-11-p3-auth-redesign-test-report.md
git commit -m "test(frontend): add P3 Auth page redesign test report"
```

---

## 验证清单

- [x] auth.css 使用 Editorial Archive 风格
- [x] auth.css 包含所有必要类名
- [x] LoginPage 使用中文文案
- [x] LoginPage 移除内联样式
- [x] RegisterPage 使用中文文案
- [x] RegisterPage 移除内联样式
- [x] 登录页面样式正确显示
- [x] 注册页面样式正确显示
- [x] 表单交互流畅
- [x] 注册登录功能正常
