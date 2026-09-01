# 实施计划：控制面提升

## 0. 前置检查

- [ ] 确认工作区干净，本任务在 `main` 单线进行（codex worktree 的未提交工作已先落 main，见 `notes.md`）。
- [ ] 确认 SDK 可用：`tianming-web/backend/.dotnet/dotnet --version` 需为 `10.0.400`。系统 `dotnet` 是 8.0.127，**不满足** `global.json`，必须走 wrapper。当前 SDK 实体在 codex worktree 的 `old/.dotnet/`（gitignored），本 worktree 需先安装或复制。
- [ ] 确认 Docker 在跑（`NovelAgentRegression` 的 Testcontainers 前置）。
- [ ] 记录迁移前基线（已实测 2026-08-31）：`AgentArchitecture` 29/29、`NovelAgentRegression` 159/159、`Unit` 821/837（16 失败，`TargetArchitecturePurityTests` 路径缺陷）。

## 1. 阶段 A：Agent 四项目

- [ ] `mkdir -p tianming-web/backend`
- [ ] `git mv old/Agent/Tianming.NovelAgent.{Domain,Contracts,Application,Infrastructure} tianming-web/backend/`
- [ ] 确认 `ProjectReference` 无需改动（同级相对路径关系不变）。
- [ ] 构建四项目通过。
- [ ] commit：`refactor(backend): promote agent control-plane projects out of old/`

## 2. 阶段 B：Web

- [ ] `git mv old/Web/NovelAgentWeb tianming-web/backend/Tianming.Web`
- [ ] `git mv old/global.json tianming-web/backend/global.json`
- [ ] `mkdir -p tianming-web/backend/Scripts && git mv old/Scripts/dotnet tianming-web/backend/Scripts/dotnet`
- [ ] 改 `Tianming.Web/NovelAgentWeb.csproj` 三条 `ProjectReference`：`..\..\Agent\` → `..\`
- [ ] 核对 §4 的 `old/Services` 引用方式（ProjectReference 还是 Compile Include），按 design.md §4 处置。
- [ ] 确认 wrapper `repo_root` 推导在新位置仍指向 `backend/.dotnet`。
- [ ] 构建 `Tianming.Web` 通过。
- [ ] commit：`refactor(backend): promote web host out of old/`

## 3. 阶段 C：Tests

- [ ] `mkdir -p tianming-web/backend/Tests`
- [ ] `git mv old/Tests/{AgentArchitecture,NovelAgentRegression,Unit,AgentKernelRegression,Fixtures} tianming-web/backend/Tests/`
- [ ] 改 4 个测试 csproj 的 `ProjectReference`（见 design.md §2 阶段 C 表）。
- [ ] 四项目构建通过。
- [ ] commit：`refactor(backend): promote test projects out of old/`

## 4. 阶段 D：solution 与路径解析

- [ ] 新建 `tianming-web/backend/TianmingWeb.slnx`（9 项目：Agent 4 + Tianming.Web + Tests 4）。
- [ ] `git rm old/TianmingAgenticNovelStudio.slnx`
- [ ] 修 `Tests/Unit/Architecture/TargetArchitecturePurityTests.cs` 的 `RepositoryRoot()` 双标记为 `global.json` + `Tianming.Web/`，并同步其 `Read()` 调用处的相对路径基准。
- [ ] 修 `Tests/Unit/ProgramConfigurationTests.cs`、`Tests/AgentKernelRegression/Program.cs` 的硬编码路径。
- [ ] 全套测试：`AgentArchitecture` 29/29、`Unit` **837/837**、`NovelAgentRegression` 159/159。
- [ ] `dotnet run --project Tests/AgentKernelRegression` 记录基线。
- [ ] commit：`refactor(backend): add solution and fix repository-root resolution`

## 5. 阶段 E：运行验证与文档

- [ ] backend 以 `ASPNETCORE_URLS=http://+:5002` 从新路径启动成功。
- [ ] `tianming-web/frontend` `npm run dev`（:3002），验证登录 + 一个受保护读接口返回 200。
- [ ] 更新 `CLAUDE.md`：删掉 `.NET 8` / SQLite 三层架构 / `Web/NovelAgentWeb` 旧路径 / 硬编码 `/Users/lyston/PycharmProjects/`；改为 .NET 10、PostgreSQL 为真源、四层布局、worktree 相对路径。
- [ ] 更新 `AGENT_CORE_ARCHITECTURE.md`：Web 层指向 `tianming-web/backend/`；§6 legacy 隔离规则的路径同步；§7 迁移顺序改述为"控制面已提升，后续是退役而非重建"。
- [ ] 更新 `README.md` 顶部重构说明与 `old/` 定位；更新 `AGENTS.md` 路径。
- [ ] commit：`docs: align guidance with promoted backend layout`

## 6. 收口

```bash
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-31-promote-control-plane
git diff --check
```

- [ ] 用 `git log --follow` 抽查 2-3 个文件确认 rename 历史保留。
- [ ] 用 `git diff --stat` 确认 `.cs` 内容改动仅限声明的 3 个路径解析文件 + §4 跨界引用（AC-5）。
- [ ] 记 journal，`task.py finish`，`task.py archive 08-31-promote-control-plane`。

## 7. 回滚点

每阶段独立 commit，任一阶段测试下降即 `git revert` 该阶段并停止。`git mv` 为纯 rename，revert 无残留。**不允许**为了让测试变绿而修改业务逻辑——那属于另一个任务。
