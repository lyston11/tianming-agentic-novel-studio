# Tianming Agent Core 实施计划

## Step 1 — 规划与架构记录

- [x] 建立本 Trellis 任务。
- [x] 写入 PRD、设计和实施计划。
- [x] 将架构基线同步到项目知识文档，并注明历史计划已降级为背景。（`Docs/AGENT_CORE_ARCHITECTURE.md`；旧 spec 头部已标记降级）
- [x] 运行 task manifest/工作区校验。（task.py validate 通过）

## Step 2 — 建立 AI package

- [x] 创建 `Agent/Tianming.Agent.Ai` ESM TypeScript package。
- [x] 固定 `@mariozechner/pi-ai@0.57.1`，暴露受控的模型、消息、工具 schema、流事件和 stream function 类型。
- [x] 添加 package build/type-check/test 命令和 lockfile。
- [x] 用最小 fake stream test 验证 adapter 不要求外部模型。

## Step 3 — 建立 Agent Core package

- [x] 创建 `Agent/Tianming.Agent.Core` ESM TypeScript package，仅依赖 `@tianming/agent-ai` 和必要的 schema 校验依赖。
- [x] 实现 `AgentCore` 状态、prompt/run、事件订阅、消息追加和 reset。
- [x] 实现 Pi 风格 natural stop；max turns 仅作安全上限。
- [x] 实现 assistant stream 聚合和消息更新事件。
- [x] 实现串行 tool execution、工具错误 result、steering、follow-up 和 abort。
- [x] 不添加小说、Web、数据库或 C# 依赖。

## Step 4 — Targeted regression

- [x] Core tests：无工具停止。
- [x] Core tests：工具调用后继续，第二轮无工具后停止。
- [x] Core tests：工具错误可观察且 loop 行为确定。
- [x] Core tests：steering 跳过剩余工具并进入下一轮。
- [x] Core tests：follow-up 只在自然停止点追加。
- [x] Core tests：abort 停止模型/工具及后续循环（含外部 AbortSignal 中断模型流）。
- [x] Core tests：max turns 终止无界循环。
- [x] Core tests：完整事件顺序稳定。
- [x] 运行新包 type-check/build/test，再运行现有 Pi Runtime targeted tests。（Ai 3/3、Core 9/9、PiRuntime 4/4 通过）

## Step 5 — 文档与质量门

- [x] 更新项目架构文档，记录实际新增 package 路径和当前未迁移 legacy callers。（含迁移策略、dsh-story 借鉴边界、Playwright 前置条件）
- [x] 运行 `task.py validate .trellis/tasks/08-22-tianming-agent-core-foundation`。（通过）
- [x] 运行 `git diff --check`。（通过）
- [x] 使用 trellis-check 检查边界、依赖方向、测试覆盖和文档一致性。（通过；补齐多工具串行执行测试）
- [ ] 任务完成前不声明 Playwright/AC-15 已完成。

## 回滚点

- AI/Core package 是新增旁路；若 Core 实现不稳定，可删除其新目录而不影响现有 ASP.NET/Node Runtime。
- 不修改现有生产 API/SSE 或旧 Runtime 注册；新包未被生产 caller 引用前不会改变线上路径。
- 若现有 npm lockfile 或 TypeScript 版本不兼容，保留架构文档和契约，修正新 package 的独立依赖，不升级全仓依赖。
