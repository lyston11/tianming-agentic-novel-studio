# 小说 Agent 新架构收束记录

更新时间：2026-08-19

## 收束决定

用户明确要求停止扩展测试、提交当前代码、执行 `trellis-finish-work` 并归档任务。本任务按现有实现和证据收束；归档仅表示停止继续在该任务下实施，不表示 PRD 中仍为 `[~]` / `[ ]` 的验收项已经满足。

## 本轮完成

- Worker 的 task claim/renew/complete/fail、失败推进、dependent unblocking 和 AcceptanceGate bridge 由 `AgentControlDbContext` 负责。
- 新增 Application 端口 `ILegacyControlPlaneCommands` 与 Infrastructure 实现 `EfLegacyControlPlaneCommands`。
- Goal submission、Goal compilation、Workflow batch transition、task-failure progression、Canon-branch Artifact 和 controller manual Artifact 等兼容入口改为通过 Application-owned command 委派；Web 保留 DTO、查询和公开兼容入口，不保留这些 mutation 的双写。
- 前端恢复意图、Workflow SSE 过滤和相关 Workflow UI 状态接线保留。
- 本轮 changed-lines-only 复审完成 5 个行为等价简化：移除私有冗余参数、展开嵌套条件表达式、复用已解析 task id、直接返回 controller 结果，以及用 `await using` 管理事务。
- DI 中 `IGoalCompiler` 使用标准 generic scoped 注册；最长可解析构造器包含 `ILegacyControlPlaneCommands`，同时满足现有架构断言。

## 验证证据

- 聚焦 Goal/Workflow/Outbox/Director 行为测试：24/24 通过。
- Unit suite：排除一个已定位的 DI 源码字符串断言后 800/800 通过；该断言在最终 DI 注册收敛后单独复验。
- PostgreSQL `KernelTaskClaimTests`：9/9 通过。
- Frontend tests：7/7 通过。
- Frontend ESLint：通过。
- `git diff --check`：通过。

按用户要求不再扩大到完整 solution、浏览器 E2E 或新的故障注入矩阵；最终提交前只运行与 DI 修正和 diff 完整性直接相关的最小检查。

## 仍未完成的验收项

- `EnforceLegacyControlPlaneReadOnly` 仍保持默认关闭。`GoalControlService` pause/resume/cancel/safe-point 以及其他 legacy production/recovery Artifact writer 尚未全部迁入 Application-owned commands。
- 真实 Conversation Proposal → Goal → Worker → Candidate → 双审 → 人工验收 → Canon merge → Workflow projection 单链路尚未完整闭环。
- Outbox/SSE replay/Canon merge/lease 的完整故障注入、SSE 过期游标、Playwright、AcceptPrefix 并发幂等、Redis/Qdrant 清空重建与真实迁移演练仍缺证据。
- Knowledge ingestion/promotion、Draft Knowledge 边界和权威知识删除合同仍未实现完整验收。

## 后续边界

后续如继续推进，应创建新的、范围更小的 Trellis 任务，不在本归档任务中追加：

1. 迁移剩余 legacy control-plane writers，并在写入口清零后开启全局 guard。
2. 完成真实单批 Candidate/Review/Acceptance/Canon/Projection E2E 与故障恢复矩阵。
3. 独立完成 SSE/Playwright、Knowledge/RAG 和派生状态重建验收。

## Spec update judgment

`.trellis/spec/backend/database-guidelines.md` 已同步本轮跨层合同：Web compatibility adapter 只能调用 `ILegacyControlPlaneCommands`，Infrastructure 使用 `AgentControlDbContext` 负责验证、EF 映射和事务；未配置命令必须失败，不能回退为 legacy context 直写。该规范同时明确全局 guard 的剩余启用门槛。
