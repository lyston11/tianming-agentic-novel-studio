# Journal - lyston (Part 1)

> AI development session journal
> Started: 2026-08-17

---



## Session 1: Agent workflow proposal integration

**Date**: 2026-08-19
**Task**: Agent workflow proposal integration
**Branch**: `codex/unify-agent-context-production`

### Summary

Implemented provider-neutral conversation tool calls, persisted GoalProposal before automatic confirmation, routed confirm_creative_goal through the Workflow application boundary, hardened concurrent confirmation idempotency, synchronized API contracts, and verified clean .NET 10 builds plus backend/frontend test suites.

### Git Commits

| Hash | Message |
|------|---------|
| `d9bae690` | (see git log) |

### Status

[OK] **Completed**

## Session 2: Novel Agent framework separation — paused handoff

**Date**: 2026-08-19
**Task**: 小说 Agent 新架构框架设计与旧代码分离
**Branch**: `codex/unify-agent-context-production`

### Summary

Paused the active Trellis task at the user's request. The Worker ownership vertical slice is implemented and recorded: `PostgresKernelTaskScheduler` now uses `AgentControlDbContext` for task claim/renew/complete/fail, failure progression, dependent unblocking, and the AcceptanceGate bridge; the claim migration/function is owned by the Agent migration history. Focused Worker, migration, architecture/guard, build, diff, and Trellis context validations passed. No Git commit, task finish, or archive was performed.

The task remains `in_progress`. The next continuation point is migrating the remaining legacy Goal submission/compiler, Workflow batch transition, and manual Artifact write paths to Application-owned commands. `EnforceLegacyControlPlaneReadOnly` remains disabled until those writes are removed; the full Candidate/Review/Acceptance/Canon/Projection E2E and failure-injection matrix remain outstanding.

### Validation

- Worker PostgreSQL regression: 9/9 passed.
- Agent migration regression: 1/1 passed.
- Guard/architecture focused tests: 15/15 passed.
- Project-local .NET build and `git diff --check`: passed.
- Trellis context validation: passed.

### Status

[PAUSED] **In progress — resumable from legacy Workflow write-path migration**

## Session 3: Novel Agent framework separation — user-directed closure

**Date**: 2026-08-19
**Task**: 小说 Agent 新架构框架设计与旧代码分离
**Branch**: `codex/unify-agent-context-production`

### Summary

Migrated the selected legacy Goal, Workflow, task-failure, Canon-branch Artifact, and controller manual Artifact mutations behind the Application-owned `ILegacyControlPlaneCommands` port. Infrastructure now owns their validation, EF mapping, and `AgentControlDbContext` transactions; Web retains compatibility DTOs and public adapters without a long-lived dual-write path. Completed the bounded simplification review, synchronized the database ownership specification, committed the task implementation, and archived the task at the user's explicit request.

The archive is a scope-closure decision, not evidence that every original PRD acceptance criterion passed. The global legacy write guard remains disabled because `GoalControlService` and other production/recovery Artifact writers still require migration. Full Candidate/Review/Acceptance/Canon/Projection E2E, browser/SSE edge cases, failure-injection coverage, and Knowledge/RAG acceptance remain follow-up work for new focused tasks.

### Validation

- Focused Goal/Workflow/Outbox/Director tests: 24/24 passed.
- Unit suite excluding the then-stale DI text assertion: 800/800 passed.
- Final targeted DI architecture assertion after registration cleanup: 1/1 passed.
- PostgreSQL `KernelTaskClaimTests`: 9/9 passed.
- Frontend tests: 7/7 passed; ESLint passed.
- Trellis context validation and `git diff --check`: passed.

### Git Commits

| Hash | Message |
|------|---------|
| `d7956db3` | `feat: separate novel agent control-plane ownership` |
| `90d1785c` | `chore(task): archive 08-17-novel-agent-framework-separation` |

### Status

[ARCHIVED] **Closed at the user's request with residual acceptance gaps documented in task notes**


## Session 2: 架构审计任务收口：Agent→Canon 闭环、guard 启用与遗留缺陷修复

**Date**: 2026-08-22
**Task**: 架构审计任务收口：Agent→Canon 闭环、guard 启用与遗留缺陷修复
**Branch**: `codex/unify-agent-context-production`

### Summary

完成 08-18-architecture-audit 六切片并归档：Unbound Session/Conversation、项目发现与显式绑定、独立 Node Pi Runtime（pi-agent-core 0.57.1）、聊天持久化收口到 ConversationMessages。新增三层 E2E：Application/DB（真实双迁移 PostgreSQL+单 Worker）、API/SSE（托管 Outbox+SSE 仅通知）。修复评审阻断项：Rework 嵌套事务（移除控制器 Serializable 包装）、E2E 工厂 AgentControlDbContext 未替换导致的 legacy 验收 400、PrefixMerge 跨上下文 kernel_tasks 双写 40001。全部合法 legacy writer 迁移至 Application command 后在两个 E2E 工厂启用 EnforceLegacyControlPlaneReadOnly=true，回归 159/159+Unit 837/837+Architecture 29/29 全绿。剩余：Playwright 浏览器 E2E（AC-15 阶段 3，仓库无既有基础设施）与迁移期代码退役（TargetArchitectureDirector 等），已在 audit-report.md 待实施清单记录。

### Git Commits

| Hash | Message |
|------|---------|
| `8c7840eb` | (see git log) |
| `497dba7d` | (see git log) |
| `2ce58c18` | (see git log) |
| `5b45b18a` | (see git log) |
| `f7d52a2f` | (see git log) |

### Status

[OK] **Completed**


## Session 3: Tianming Agent Core 分层基线落地

**Date**: 2026-08-22
**Task**: Tianming Agent Core 分层基线落地
**Branch**: `codex/unify-agent-context-production`

### Summary

实现 08-22 任务：新增 @tianming/agent-ai（固定 pi-ai 0.57.1 的受控模型边界）与 @tianming/agent-core（通用 Agent Loop：natural stop、串行工具、steering/follow-up、abort、maxTurns、10 种稳定事件），13 个确定性测试全绿，PiRuntime 回归 4/4 通过；写入 Docs/AGENT_CORE_ARCHITECTURE.md 基线并将 2026-07-12 目标架构 spec 降级为历史计划。子代理通道两次空响应后改内联实现，check 阶段补齐多工具串行测试。

### Git Commits

| Hash | Message |
|------|---------|
| `e3b14f4d` | (see git log) |

### Status

[OK] **Completed**


## Session 4: 仓库重构：四部分代码基础

**Date**: 2026-08-22
**Task**: 仓库重构：四部分代码基础
**Branch**: `codex/unify-agent-context-production`

### Summary

按用户确认方案重构仓库：Agent 两 Node 包迁出为根目录 tianming-ai / tianming-agent-core（file: 依赖改 ../tianming-ai，重装后 3/3 与 10/10 全绿）；其余全部旧代码/文档/配置 git mv 进 old/ 冻结（历史 100% rename 保留）；权威架构文档上移根目录并更新路径；新建 tianming-novel-agent 与 tianming-web 骨架 README；pi-agent/ 存 badlogic/pi-mono v0.57.1 浅克隆参考源码（gitignored）。PiRuntime 回归从 old/ 路径 4/4 通过。

### Git Commits

| Hash | Message |
|------|---------|
| `4e379a24` | (see git log) |

### Status

[OK] **Completed**


## Session 5: tianming-web/frontend 全量重建前端（learngraph 栈）

**Date**: 2026-08-29
**Task**: tianming-web/frontend 全量重建前端（learngraph 栈）
**Branch**: `codex/unify-agent-context-production`

### Summary

基于 learngraph 栈在 tianming-web/frontend/ 全量重建前端，对接 old 后端 :5002；六大功能域落地，17 测试+四件套全绿

### Main Changes

- 新建 tianming-web/frontend：React19+Tailwind4+shadcn(ui 28)+react-query+oxlint+vitest，无 zustand（module store）
- api 层移植：types.ts 2817 行 + runtime-events 894 行原样搬移；client 含信封合同/幂等键/GET 重试/SSE 工厂；auth-store 兼容旧 persist 格式
- 六大功能域：auth/library(书城+阅读器+版本回滚)/materials(知识库浏览器)/agent(SSE 执行块+streamdown 流式)/workflow(Goal 验收台+双 SSE)/settings
- SSE 三流遵循 state-management.md 合同；index.css 以 shadcn oklch token 承载旧版中式色板

### Git Commits

(No commits - planning session)

### Testing

- [OK] typecheck/lint/build 全绿；vitest 17/17（信封/幂等/退避/401/auth store）；dev 3002 冒烟 200

### Status

[OK] **Completed**

### Next Steps

- tianming-web 后端就位后切 API 基址；Conversation Runtime 接通后接 Conversation SSE 流


## Session 6: TS 边界收敛任务完成

**Date**: 2026-09-01
**Task**: TS 边界收敛任务完成
**Branch**: `lyston11/09-01-ts-boundary-convergence`

### Summary

完成 Novel Agent TS 边界收敛：提案生命周期归 C#，删除 TS reducer，隔离测试内存替身，收窄 ports 并补齐规范记录。AI 3/3、Core 10/10、Novel Agent type-check/build/11 tests、Trellis validate 和 diff check 全部通过。任务已提交并归档。

### Git Commits

| Hash | Message |
|------|---------|
| `6564aaa9` | (see git log) |
| `37eb3e18` | (see git log) |

### Status

[OK] **Completed**
