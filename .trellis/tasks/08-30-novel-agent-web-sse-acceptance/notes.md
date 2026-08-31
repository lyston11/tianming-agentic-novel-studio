# 研究笔记：Novel Agent Web SSE and Acceptance Workbench

## Evidence

- 当前 `tianming-web` 目录只有 README 和 frontend，没有新 ASP.NET backend caller/adapter；新 package 也没有外部 caller。
- frontend `src/api/types.ts` 当前包含 legacy CreativeGoal、BookProduction、TaskGraph、Candidate/Artifact DTO；不能直接当新 `WorkflowProjection`。
- frontend `src/api/novel-agent.ts`、`features/workflow/use-novel-agent-workflow-stream.ts` 和 `use-goal-workflow.ts` 已有 endpoint/SSE/query invalidation 形状，可扩展但需显式 DTO mapper。
- `.trellis/spec/frontend/state-management.md` 要求 REST 是状态真源、SSE 只通知刷新；Conversation/Workflow stream 必须有不同 streamKind/streamId/cursor。
- backend database guideline 要求 outbox/bridge 先持久化，再由消费者/stream relay 投递；发送失败不能回滚业务事实。

## Decisions

- Web DTO v1 独立于 Core event、EF entity 和 legacy DTO。
- 前端使用 React Query server state；SSE 只做 validated invalidate；transient token 与业务事件隔离。
- 如果缺少 backend host，先做 contract/adapter discovery，不凭空创建 production endpoint。

## Deferred

- 真实 SSE relay、Redis fanout、auth provider 和 browser E2E。
- legacy 全量 UI cutover 与旧 endpoint 删除。
