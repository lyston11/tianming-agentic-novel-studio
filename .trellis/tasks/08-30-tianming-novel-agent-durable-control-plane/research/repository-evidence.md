# 研究证据：Durable Control Plane 任务边界

## Current vertical slice

- `tianming-novel-agent/src/application/novel-agent-application.ts` 只有 conversation、confirm、run task、accept、query 五类用例；模型使用 deterministic fake。
- `tianming-novel-agent/src/store/in-memory-store.ts` 用 Map 和 promise queue 验证 scope/idempotency/version/hash，但不提供数据库隔离、崩溃恢复或分布式 lease。
- `tianming-novel-agent/src/runtime/event-mapper.ts` 可验证 run sequence/replay/dedupe 形状，但不是生产 Outbox 或 Web SSE envelope。
- `tianming-agent-core` 的 natural stop、tool result、abort/maxTurns 是 generic observation，不得被映射成业务完成。

## Backend control-plane authority

`.trellis/spec/backend/database-guidelines.md` 明确：

- `AgentControlDbContext` 写 `creative_goals`、`goal_revisions`、`book_productions`、`production_batches`、`task_graph_versions`、`kernel_tasks`、`domain_events`、`outbox_events`。
- Conversation/checkpoint/stream/lease 是 Agent-control-only tables，并需 tenant RLS。
- Confirmed production/root task 为 `planned`；`ProductionApplicationService.StartAsync` 才推进运行态。
- Worker completion 和 acceptance gate bridge outbox 必须同事务；重复 bridge delivery 是 no-op。
- Concurrent confirmation 必须在 Serializable transaction 中查重、写入并在竞争失败后重读 durable result。

## Legacy novel semantics

- `old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs`：proposal 非 executable，必须显式 confirm/reject/revise。
- `old/.../GoalContract.cs`：objective、must preserve/happen/not-change、acceptance/rework policy、baseline/version references。
- `old/.../Production/TaskGraph.cs`：versioned DAG、typed artifacts、human acceptance gate、Canon merge dependency。
- `old/Services/Framework/AI/NovelAgent/Models/StoryCreativeConstitution.cs`：reader promise、hook/theme、world rule、conflict/protagonist engine、forbidden directions。
- `old/.../CharacterLedgerModels.cs` 与 `ForeshadowLedgerModels.cs`：演化状态、planned/actual history、evidence 和 chapter provenance。
- `old/.../CanonBranchMergeTests.cs`：accepted prefix、conflict、human protection、merge record 和 idempotency。

## Frontend boundary

`.trellis/spec/frontend/state-management.md` 要求 REST snapshot 是状态真源，SSE 只触发共享 invalidate；streamKind/streamId/sequence/cursor 必须验证；token delta 不得推进业务状态。当前 frontend DTO 仍含 legacy `CreativeGoal`/task graph/artifact 形状，不能直接当作新 `WorkflowProjection`。

## EcomGen transferable patterns

EcomGen 可供参考的工程模式为 task ID 传递、Worker 重读事实、request fingerprint/provider key、attempt policy、cancel boundary、output dedupe、snapshot/artifact lineage、structured validation、bounded timeout/repair。其无 Outbox、无 lease/fence、弱 auth、ephemeral Pub/Sub 和 mutable prompt 问题不应迁移。
