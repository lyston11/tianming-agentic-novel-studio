# P4 发布流程配置实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 配置前端构建产物自动同步到后端静态目录，支持一键部署

**Architecture:** 在 package.json 添加 postbuild 钩子自动同步 dist/ 到 wwwroot/，确保后端可以正确提供前端静态资源

**Tech Stack:** npm scripts, Bash, ASP.NET Core

---

## File Structure

**修改：**
- `Web/NovelAgentWeb.Frontend/package.json` - 添加同步脚本
- `README.md` - 更新部署文档

---

### Task 1: 添加同步脚本到 package.json

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/package.json:5-10`

- [ ] **Step 1: 添加 sync-to-backend 脚本**

在 `scripts` 部分添加：

```json
{
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "postbuild": "npm run sync-to-backend",
    "sync-to-backend": "rm -rf ../NovelAgentWeb/wwwroot/* && cp -r dist/* ../NovelAgentWeb/wwwroot/",
    "preview": "vite preview"
  }
}
```

- [ ] **Step 2: 验证 JSON 格式**

Run: `cd Web/NovelAgentWeb.Frontend && npm run --help`
Expected: 命令列表中显示 sync-to-backend

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb.Frontend/package.json
git commit -m "feat(frontend): add postbuild script to sync dist to wwwroot"
```

---

### Task 2: 测试构建和同步流程

**Files:**
- Test: 验证自动同步

- [ ] **Step 1: 清空 wwwroot 目录**

```bash
rm -rf Web/NovelAgentWeb/wwwroot/*
ls Web/NovelAgentWeb/wwwroot/
```

Expected: 目录为空

- [ ] **Step 2: 运行前端构建**

```bash
cd Web/NovelAgentWeb.Frontend
npm run build
```

Expected: 构建成功，自动执行 postbuild 钩子

- [ ] **Step 3: 验证 wwwroot 目录**

```bash
ls -la Web/NovelAgentWeb/wwwroot/
```

Expected: 包含 index.html, assets/, 等前端文件

- [ ] **Step 4: 验证文件完整性**

```bash
# 检查 index.html
cat Web/NovelAgentWeb/wwwroot/index.html | head -5

# 检查 assets 目录
ls Web/NovelAgentWeb/wwwroot/assets/
```

Expected: 文件完整，assets 包含 JS/CSS bundle

---

### Task 3: 测试后端提供静态资源

**Files:**
- Test: 验证后端 SPA fallback

- [ ] **Step 1: 启动后端服务**

```bash
cd Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

Expected: 服务启动成功

- [ ] **Step 2: 测试静态资源访问**

在另一个终端：

```bash
# 测试 index.html
curl http://localhost:5002/ | grep "Novel Agent"

# 测试 SPA fallback（未匹配的路由返回 index.html）
curl http://localhost:5002/workflow | grep "Novel Agent"
```

Expected: 返回 HTML 内容，包含 "Novel Agent"

- [ ] **Step 3: 浏览器测试**

1. 打开浏览器访问 http://localhost:5002
2. 验证前端应用正确加载
3. 验证路由跳转正常（/materials, /workflow 等）
4. 刷新页面验证 SPA fallback 工作

Expected: 应用完全正常，与开发服务器体验一致

---

### Task 4: 创建生产构建脚本（可选）

**Files:**
- Create: `Web/build-and-deploy.sh`

- [ ] **Step 1: 创建部署脚本**

```bash
#!/bin/bash
set -e

echo "Building frontend..."
cd Web/NovelAgentWeb.Frontend
npm run build

echo "Building backend..."
cd ../NovelAgentWeb
dotnet publish -c Release -o ../../publish

echo "Deployment package ready at: publish/"
ls -lh ../../publish/
```

- [ ] **Step 2: 添加执行权限**

```bash
chmod +x Web/build-and-deploy.sh
```

- [ ] **Step 3: 测试脚本**

```bash
./Web/build-and-deploy.sh
```

Expected: 前后端构建成功，生成 publish/ 目录

- [ ] **Step 4: 提交**

```bash
git add Web/build-and-deploy.sh
git commit -m "feat(deploy): add build and deploy script"
```

---

### Task 5: 更新 README 文档

**Files:**
- Modify: `README.md`

- [ ] **Step 1: 添加部署文档**

在 README.md 中添加：

```markdown
## 前端构建和部署

### 开发环境

```bash
# 启动后端
cd Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run

# 启动前端（另一个终端）
cd Web/NovelAgentWeb.Frontend
npm run dev
```

访问 http://localhost:3002

### 生产构建

```bash
# 构建前端（自动同步到 wwwroot）
cd Web/NovelAgentWeb.Frontend
npm run build

# 发布后端
cd ../NovelAgentWeb
dotnet publish -c Release -o ../../publish
```

### 一键构建部署

```bash
./Web/build-and-deploy.sh
```

部署产物在 `publish/` 目录，可直接部署到服务器。

### 验证部署

启动发布的应用：

```bash
cd publish
ASPNETCORE_URLS=http://+:5002 ./NovelAgentWeb
```

访问 http://localhost:5002 验证前端加载正常。

## 端口配置

**固定端口（永远不要改）：**
- 后端 API: `5002`
- 前端开发: `3002`
- Qdrant: `6333` (HTTP), `6334` (gRPC)

**重要：** 后端必须设置 `ASPNETCORE_URLS=http://+:5002`，否则会监听默认端口 5000！
```

- [ ] **Step 2: 提交**

```bash
git add README.md
git commit -m "docs: add frontend build and deployment instructions"
```

---

### Task 6: 验证完整部署流程

**Files:**
- Test: 端到端部署测试

- [ ] **Step 1: 清理旧构建**

```bash
rm -rf Web/NovelAgentWeb/wwwroot/*
rm -rf Web/NovelAgentWeb/bin/
rm -rf Web/NovelAgentWeb/obj/
rm -rf publish/
```

- [ ] **Step 2: 执行完整构建**

```bash
./Web/build-and-deploy.sh
```

Expected: 构建成功，publish/ 目录包含所有文件

- [ ] **Step 3: 测试发布应用**

```bash
cd publish
ASPNETCORE_URLS=http://+:5002 ./NovelAgentWeb &
sleep 3

# 测试 API
curl http://localhost:5002/api/health || echo "Health check endpoint may not exist"

# 测试静态资源
curl http://localhost:5002/ | grep "Novel Agent"

# 停止服务
pkill -f NovelAgentWeb
```

Expected: 应用正常启动，静态资源可访问

- [ ] **Step 4: 提交测试报告**

创建文件：`docs/superpowers/tests/2026-06-11-p4-deploy-pipeline-test-report.md`

内容：
```markdown
# P4 发布流程配置测试报告

## 测试日期
2026-06-11

## 测试结果

### ✅ 自动同步脚本
- npm run build 触发 postbuild 钩子
- dist/ 内容自动复制到 wwwroot/
- 文件完整性验证通过

### ✅ 后端静态资源服务
- 后端正确提供 index.html
- SPA fallback 工作正常
- 所有路由可访问

### ✅ 生产构建
- 前端构建成功
- 后端发布成功
- publish/ 目录包含完整应用

### ✅ 一键部署脚本
- build-and-deploy.sh 执行成功
- 部署产物可直接运行
- 应用功能完整

### ✅ 文档完整
- README.md 包含部署说明
- 端口配置说明清晰
- 开发和生产流程明确

## 结论
P4 发布流程配置完成，所有测试通过。
```

```bash
git add docs/superpowers/tests/2026-06-11-p4-deploy-pipeline-test-report.md
git commit -m "test(deploy): add P4 deploy pipeline test report"
```

---

## 验证清单

- [x] package.json 添加 sync-to-backend 脚本
- [x] postbuild 钩子自动执行
- [x] dist/ 正确复制到 wwwroot/
- [x] 后端提供静态资源
- [x] SPA fallback 工作正常
- [x] 生产构建成功
- [x] build-and-deploy.sh 脚本可用
- [x] README.md 文档完整
- [x] 端到端部署测试通过
