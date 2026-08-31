# 研究笔记：Tianming Novel Agent Durable Control Plane

## 1. 证据分类

- **当前实现证据**：`tianming-novel-agent` 已完成 deterministic in-memory 单章节链路，但没有 Web caller、PostgreSQL、Worker、Outbox relay 或真实 provider。
- **当前规范证据**：`.trellis/spec/backend/database-guidelines.md` 要求 `AgentControlDbContext` 作为新控制面写 owner；确认后 Production/Task 为 `planned`，显式 Start 才运行；Worker completion 与 acceptance bridge outbox 同事务。
- **前端规范证据**：`.trellis/spec/frontend/state-management.md` 要求 REST 持有 commands/snapshots，SSE 只通知刷新；Conversation/Workflow 使用不同 stream identity；React Query 是 server-state owner。
- **历史领域参考**：`old/Agent/Tianming.NovelAgent.Domain/Goals/`、`old/.../Production/`、`old/Services/Framework/AI/NovelAgent/Models/` 提供 GoalContract、GoalRevision、TaskGraph、Story Constitution、Character/Foreshadow Ledger、Acceptance/Canon 语义。
- **工程模式参考**：`EcomGen/` 提供 task ID、snapshot、fingerprint、provider request key、retry、artifact lineage、structured validation 等可迁移模式，但不是 Tianming 的可靠性权威。

## 2. Load-bearing findings

1. Proposal、Goal、Production、Batch、Task 必须分离；模型输出不能直接创建执行状态。
2. `StoryBibleDocument`、`NovelAgentRun` 和 `NovelAgentOrchestrator` 都是历史语义容器，不应整体迁移为新 aggregate 或真源。
3. Frozen Context 必须包含 source references、version vector、policy/model/style versions 和 content hash；执行时不能重新读 mutable snapshot。
4. Canon merge 必须经过 persisted accepted Acceptance，并比较当前 Canon baseline；review passed 不等于 Canon adopted。
5. Worker 需要 lease owner/expiry/fence 三元组；lost lease 的迟到结果必须拒绝。
6. Provider outcome unknown 是独立状态；late result 只能作为 unadopted evidence。
7. Domain Event + Outbox 必须与事实写入同一事务，SSE/Redis 不能承担业务真源。
8. 现有新 frontend DTO 与新 TypeScript `WorkflowProjection` 不同，必须显式定义 adapter，不让 UI 本地 cast 或自行推进状态。

## 3. Known current gaps to close

- `ConversationTurn` 尚未由 Web/Application 持久化；没有 ConversationPort、assistant message 或 durable checkpoint。
- `GoalProposal` 没有完整 status/rejection/revision provenance。
- in-memory confirmation 直接写 running，与 backend guideline 的 planned/start 不一致。
- `runChapterTask` 没有 durable claim、TaskAttempt、lease/fence 或 run idempotency。
- Candidate、Review、Task、Canon 在内存 Map 中不是跨实体数据库事务。
- Canon merge 还没有 current version-vector CAS。
- `DurableRuntimeMessage` 不是前端 SSE v1 envelope，也没有生产级 durable cursor。
- `tianming-web` 当前没有新 backend implementation；frontend 仍面向较丰富 legacy DTO。

## 4. Migration matrix

| 来源 | 迁移 | 不迁移 |
|---|---|---|
| old Goal/Production/TaskGraph | 领域状态、revision、human gate、lease/attempt/fence 语义 | 旧 aggregate 的直接持久化和固定 universal graph |
| old Story/Character/Foreshadow | Constitution、结构化演化状态、planned/actual payoff、evidence/provenance | 巨大 mutable Story Bible、文件 guide、keyword-only truth |
| old runtime | adapter/checkpoint/event port 形状 | Pi/MAF runtime、旧 ReAct loop 恢复 |
| EcomGen | task/snapshot/fingerprint/provider key/retry/artifact patterns | 电商模型、旧 auth/transaction/SSE 弱点、直接依赖 |

## 5. Planning conclusion

下一阶段按五个 child 顺序落地；任何实现都必须保留 Core → Novel Agent → Web/Application 的边界，并把生产可靠性放在 PostgreSQL/Outbox/Worker 层。当前研究不足以授权删除 legacy 或直接提交代码。
