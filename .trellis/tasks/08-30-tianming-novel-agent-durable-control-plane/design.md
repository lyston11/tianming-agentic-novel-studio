# 技术设计：Tianming Novel Agent Durable Control Plane

## 1. 目标架构

```text
HTTP/SSE + Auth
      ↓
Web Application / Domain / Worker
  durable commands, authorization, transactions, Outbox, projections
      ↓ narrow ports
Novel Agent
  roles, skills, context compiler, reviews, structured intents
      ↓ generic AgentCore adapter
AgentCore
  model/tool loop, validation, abort, steering, observation events
      ↓ controlled provider boundary
Tianming AI
  only direct Pi AI import and provider adapter
```

写入所有权固定为：Web/Application/Domain/Worker 写 Goal、Production、Task、Candidate、Review、Acceptance、Canon、Events、Outbox；Novel Agent 只提交 typed intent；AgentCore 不知道任何小说或业务生命周期。

## 2. Durable data flow

```text
ConversationTurn
  → ConversationStore + RuntimeRun provenance
  → GoalProposal(proposed)
  → ConfirmGoal transaction
  → Goal/GoalRevision + Production/Batch/TaskGraph/Task(planned)
  → StartProduction transaction + Outbox
  → Worker claim(TaskAttempt, Lease, FenceToken)
  → ContextCompiler freezes NovelContextPackage
  → Novel Agent / AgentCore execution
  → Candidate artifact + hard/soft Review
  → awaiting_acceptance projection
  → human Acceptance
  → Canon baseline CAS merge
  → Character/Foreshadow changes + DomainEvent + Outbox
  → projection reducer
  → REST snapshot and SSE notification/replay
```

每个边界都携带 user/project scope、correlation/causation、idempotency、expected version 和 source provenance。Outbox 是 durable delivery boundary；SSE/Redis 是其后续通知实现，不拥有业务状态。

## 3. Child ownership and dependency graph

### 3.1 Conversation and Goal

Child 1 定义 Conversation/Turn/assistant/run/proposal 的领域合同、状态机、命令和应用 port，并提供 repository-independent deterministic tests。它不决定 PostgreSQL 表实现。

### 3.2 PostgreSQL control plane

Child 2 实现 `AgentControlDbContext`、migration history、共享控制表映射、事务、唯一约束、CAS、RLS 和 `planned → start → running` 状态边界。它消费 Child 1 的 domain contracts，不复制 legacy context 的写路径。

### 3.3 Worker reliability

Child 3 在 Child 2 的 Task 表和事务上实现 claim/renew/release、attempt、lease expiry、fence、cancel、retry、ProviderRequest 和 OutcomeUnknown。Worker 只能通过 Application/Domain commands 提交结果，不能直接修改 Production/Canon。

### 3.4 Review, Canon and Outbox

Child 4 实现硬门禁、软审查、evidence、EditorialDraft/Rework、Acceptance、Canon version-vector CAS、merge record、Domain Event、Outbox 和 projection reducer。Canon merge 的数据库写入必须在其权威写上下文中完成，不能假设跨 DbContext 的假事务。

### 3.5 Web, SSE and frontend

Child 5 将上述命令和投影映射为稳定的 DTO v1；Controller 只转换 DTO/身份/headers，Application 执行命令。Workflow/Conversation SSE 使用不同 streamKind/streamId，按 cursor replay；React Query 持有 server state，SSE 只触发 invalidate。

## 4. Core contracts

### 4.1 State ownership

```text
Goal: proposed → confirmed → active/completed/blocked/cancelled/superseded
GoalRevision: append-only current/superseded/invalidated
Production: planned → running → awaiting_acceptance → merging_canon → completed
Batch: planned → running → blocked/failed/completed/cancelled
Task: planned → ready → queued → claimed → running → awaiting_user → completed
Task: running → retryable → queued
Task: running → outcome_unknown → reconcile
Candidate: generated → awaiting_acceptance → merged/rejected/blocked/superseded
```

`natural_stop` 只代表 AgentCore run observation；`Task completed` 只代表技术执行和业务后置条件均满足；`Production completed` 需要 Canon/acceptance 条件。

### 4.2 Proposal and conversation

Conversation 由 Web/Application 持久化原始 turn、assistant result、run ID、checkpoint、source message IDs 和 actor scope。Proposal 具有状态、版本、content hash、确认/拒绝 provenance 和 Goal link。一个 project 可有多个历史 proposal，但 active Goal/Production 的唯一性由数据库约束守护。

### 4.3 ContextPackage and evidence

ContextPackage 是 immutable execution artifact，至少包含 GoalRevision、TaskGraph version、Canon baseline、Story Constitution revision、相关 Character/Foreshadow、evidence/source references、style/model/policy/schema versions、versionVector 和 contentHash。required evidence 缺失必须 fail closed；retrieval 只能定位来源，不能直接成为事实。

### 4.4 Candidate and rework

Candidate 以 `lineageId + candidateVersion + parentCandidateId` 形成不可变链。EditorialDraft 是可编辑工作区，不是 Canon。ReworkRequest 记录 scope、选区、问题、期望效果、preserve/mayChange/mustNotChange、acceptance criteria、impact level 和 attempt budget；提交后生成新 Candidate。

### 4.5 Runtime and Web envelope

内部 runtime observation 可映射为 durable event，但 Web envelope 独立定义：

```text
schemaVersion, eventId, streamKind, streamId, sequence,
eventType, projectId, sessionId, goalId, productionId,
taskId, candidateId, correlationId, causationId, transient, data
```

Conversation stream 与 Workflow stream 不能混用。Projection reducer 只从 durable facts 更新，不从单次 Core callback 推断状态。

## 5. Transaction and recovery rules

- ConfirmGoal 的查重、scope、Goal/Revision/Production/Batch/Task 创建和 `GoalConfirmed` event 在一个 transaction 内；竞争失败后重新读取 durable result。
- StartProduction 的状态迁移和 dispatch outbox 在一个 transaction 内；重复启动是 no-op。
- Worker completion、dependent unblocking 和 acceptance-gate bridge outbox 在一个 control-plane transaction 内；lease/fence 失败整体回滚。
- Canon merge 必须校验 persisted accepted Acceptance、candidate/version/context hash、current Canon baseline 和 protected human version；冲突不写 Canon。
- Domain Event + Outbox 与其拥有者的事实同事务提交；relay 失败不能回滚已提交业务事实。
- OutcomeUnknown 只产生可恢复状态和执行证据；迟到 provider result 保存为 unadopted artifact，不能自动改变 Canon。
- 所有 mutation 都需要显式 user/project predicates；RLS 是纵深防御，不替代应用层授权。

## 6. Compatibility and rollout

1. 先以新 Application command/port 和 deterministic contract tests 验证，不触碰旧写路径。
2. 再启用 AgentControlDbContext 与真实 migration；旧兼容入口只能 adapter 到新 command owner，禁止 dual write。
3. Worker、Outbox 和 SSE 在 feature flag 下逐步接入；旧 stream 可与新 stream 并存，但不能共享 cursor 或业务 reducer。
4. 前端先消费新 DTO/projection，再逐页切换 Acceptance/Rework；保留 legacy fallback 直到等价回归和历史数据读取证据完成。
5. 旧 Pi/MAF runtime 只读冻结；需要恢复时基于 Canon snapshot 创建新 Proposal/Goal，不恢复旧 loop/checkpoint。

## 7. Rollback shape

- Contract/adapter 回滚：保留已写入 immutable artifacts，不删除历史事实；关闭新 endpoint/feature flag，恢复 legacy read path。
- Migration 回滚：只回滚未启用的新表/索引；共享既有控制表迁移必须提供前向兼容，不执行破坏性 drop/recreate。
- Worker 回滚：停止新 claim，保留 running/outcome_unknown attempts，使用 reconcile 工具恢复；禁止直接把 unknown 改成 success。
- Canon/Outbox 回滚：不能反向删除已提交 Canon；通过显式 corrective revision/rejection 和补偿 projection 修正。

## 8. Required final integration evidence

- 独立 DbContext 并发 ConfirmGoal 只有一个 Goal/Revision/Production 结果，双方得到同一 durable result。
- 并发 claim、lease expiry、renew、fence rejection、cancel 和 outcome_unknown 均有 integration tests。
- hard gate、rework lineage、Acceptance 权限、Canon baseline conflict、Outbox atomicity 和 duplicate delivery 均有 negative-path tests。
- REST/SSE DTO round-trip、stream identity、cursor replay、duplicate event 和 frontend invalidate-only 行为有 contract tests。
