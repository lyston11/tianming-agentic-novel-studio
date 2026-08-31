# Durable Conversation and Goal Proposal Control

## 1. Goal and user value

把用户的小说创作对话变成可恢复、可审计、可确认的控制面输入。用户可以在同一会话中看到原始请求、Agent 提案、提案版本和确认结果；任何未确认的提案都不会偷偷启动生产或改变 Canon。

## 2. Requirements

### R1 — durable conversation provenance

持久化 `Conversation`、`ConversationTurn`、assistant response/message、runtime run/checkpoint provenance，至少关联：

- `conversationId`、`turnId`、`projectId`、`userId`；
- actor/session binding；
- client idempotency key；
- correlation/causation ID；
- source message IDs；
- runtime `runId`、状态和 checkpoint reference；
- created/occurred timestamps。

原始用户消息和 assistant 结果不可通过新的请求覆盖；修订以新记录追加。

### R2 — GoalProposal lifecycle

实现并持久化明确状态：

```text
draft → proposed → confirmed
                 ↘ rejected
                 ↘ superseded
                 ↘ discarded
```

每个版本保留 `proposalVersion`、canonical content hash、createdBy、source turn/message、决策 actor/time/reason 和关联 Goal。确认只能作用于当前 proposed 版本。

### R3 — confirmation boundary

Proposal 只是非执行合同。确认命令必须：

- 校验 user/project/session scope；
- 校验 expected proposal version/hash；
- 校验 proposal 仍为 proposed；
- 使用 idempotency key；
- 产生可供后续控制面消费的 `ConfirmGoal` 结果；
- 不在本任务中直接把 Production 置为 running 或调用模型。

### R4 — rejection and revision

支持显式 reject、discard 和 revise。拒绝/废弃必须记录原因；修订不能覆盖历史版本，且新版本回到 proposed。旧版本被 superseded 后不可再次确认。

### R5 — proposal multiplicity

一个项目可以保留多个历史和并行 proposed proposals；同一时间最多一个 active confirmed Goal/Production。该约束必须由 Application 合同和数据库唯一/并发策略表达，而不是只依赖内存检查。

### R6 — authorization and errors

所有读写都执行 user/project predicate，区分 not-found、forbidden、conflict、invalid-transition 和 cancelled。跨项目访问不得泄漏 proposal 是否存在，也不得产生部分写入。

### R7 — compatibility adapter

为当前 `tianming-novel-agent` vertical slice 提供兼容 port，使 `GoalProposal` 的现有 `propose_goal` tool flow 能写入新的持久化边界；不得把 Conversation/Proposal 生命周期塞入 AgentCore，也不得直接导入 Pi/EF。

## 3. Acceptance criteria

- [ ] A1：追加一条用户 turn 后，可以按 user/project/conversation 查询原文、assistant result、run provenance 和 source message IDs。
- [ ] A2：相同 command idempotency key 与相同 canonical payload 返回同一结果；相同 key 与不同 payload 返回 conflict，历史记录不变。
- [ ] A3：Proposal 可按 proposed/confirmed/rejected/superseded/discarded 迁移；每次迁移有 actor、时间、原因和版本证据。
- [ ] A4：未确认 Proposal 不创建 Goal、Production、Batch、Task，也不改变 Canon。
- [ ] A5：确认只接受当前 proposal version/hash；stale version、错误 scope、重复确认和终态确认均得到可观察拒绝且无部分写入。
- [ ] A6：同一项目的并发确认最多提交一个 active Goal/Production；竞争请求能重读并返回已提交的 durable result，而非暴露唯一键异常。
- [ ] A7：Goal 明确引用被确认的 proposal/version；proposal revision 与 GoalRevision provenance 可双向查询。
- [ ] A8：兼容 adapter 仍使现有 Novel Agent proposal vertical-slice tests 通过；Novel Agent/Core 没有新增数据库或 Pi 直连。
- [ ] A9：测试覆盖越权读写、取消/终态、重复/冲突 idempotency、并发确认和失败回滚。
- [ ] A10：文档明确本任务只完成会话/提案控制面，不宣称 Production Start、Worker、Canon merge、Outbox relay 或前端完成。

## 4. Out of scope

- AgentControlDbContext 的完整 migration/RLS 实现（由 PostgreSQL child 负责）。
- Production/Batch/Task 实际启动、Worker lease/fence、ProviderRequest 和 OutcomeUnknown。
- Review、Acceptance、Canon merge、EditorialDraft/Rework 和 Outbox relay。
- 真实模型调用、模型预算、完整 checkpoint resume、整书生产和 legacy 删除。

## 5. Dependencies and constraints

- 依赖当前 vertical slice 的 contracts/ports 和 `@tianming/agent-core` public boundary。
- 不得修改 `tianming-agent-core` 以添加小说字段。
- 不得直接复用 `old/NovelAgentRun` 作为新 Conversation 或 Goal 真源；只提取其 provenance 语义。
- 后续 PostgreSQL child 必须能够消费本任务定义的 command/result 语义。
