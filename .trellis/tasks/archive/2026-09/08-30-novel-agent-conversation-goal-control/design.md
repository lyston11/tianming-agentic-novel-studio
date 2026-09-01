# 技术设计：Durable Conversation and Goal Proposal Control

## 1. Boundary

```text
HTTP/auth/session identity
        ↓
ConversationApplicationService
  append turn, run adapter, persist messages/provenance
        ↓ narrow ports
Novel Agent proposal adapter
  propose_goal → GoalProposal DTO
        ↓
GoalProposal aggregate/repository
  lifecycle, version, hash, confirmation provenance
        ↓ command contract
PostgreSQL AgentControlDbContext (child 2)
```

AgentCore 只产生 message/tool observations。它不保存 Conversation、Proposal status 或 Goal confirmation。

## 2. Durable records

建议记录语义（最终列名遵守 backend database guideline）：

- `conversation`: owner, project, binding, lifecycle, current sequence。
- `conversation_turn`: immutable user input, turn sequence, correlation, idempotency。
- `conversation_message`: user/assistant/tool role, content/artifact reference, run linkage。
- `runtime_run`: run ID, input turn, status, model/policy references, outcome。
- `runtime_checkpoint`: immutable checkpoint pointer/version；不作为业务状态。
- `goal_proposal`: stable proposal ID, current status, current version, content hash。
- `goal_proposal_revision`: append-only payload, source messages, actor/time, decision reason。
- `goal_confirmation`: idempotency key, proposal version/hash, confirmed Goal/Revision IDs, result correlation。

正文较大的部分可以作为 immutable artifact reference，但 query 必须能恢复完整用户可见内容。

## 3. State and command rules

```text
CreateDraft
  → Propose
    ├─ Revise → new revision(proposed)
    ├─ Reject → rejected
    ├─ Discard → discarded
    └─ Confirm(expectedVersion/hash) → confirmed + Goal link
```

- `confirmed` proposal 只允许被一个 GoalRevision 引用。
- Goal confirmation command 的查重、scope、expected version 和 durable result lookup 放在同一 transaction boundary；并发失败后重新读取 result。
- Proposal revision 与 GoalRevision 不互相覆盖；GoalRevision 保存 `sourceProposalId` 和 `sourceProposalVersion`。
- 同一项目的 active Goal unique rule 不阻止历史 Goal，也不删除并行 proposed proposals。

## 4. Port shape

```ts
interface ConversationStore {
  appendTurn(input: AppendTurn): Promise<ConversationTurnRecord>;
  appendMessage(input: AppendMessage): Promise<ConversationMessageRecord>;
  saveRun(input: RuntimeRunRecord): Promise<RuntimeRunRecord>;
  saveCheckpoint(input: CheckpointRecord): Promise<CheckpointRecord>;
}

interface GoalProposalCommands {
  propose(input: ProposalDraft): Promise<GoalProposal>;
  revise(input: ReviseProposal): Promise<GoalProposal>;
  reject(input: RejectProposal): Promise<GoalProposal>;
  discard(input: DiscardProposal): Promise<GoalProposal>;
  confirm(input: ConfirmGoalProposal): Promise<GoalCommitIntent>;
}
```

所有 command 都携带 actor scope、correlation/causation、idempotency 和 expected version/hash。Repository 只返回 typed result/error，不让 Controller 直接操作 EF entity。

## 5. Compatibility

- 现有 `propose_goal` tool 通过 `GoalProposalCommands.propose` 写入；工具只得到 proposal result。
- `handleConversationTurn` 先追加 turn，再运行 adapter，再追加 assistant/runtime records；runtime 失败要保留已写入消息并返回可观察 error。
- 旧 frontend/ASP.NET endpoint 如果暂时存在，只能 adapter 到本命令 owner；不在此 child 建立 dual write。
- old runtime/checkpoint 不能恢复为当前 Conversation state；后续恢复应以 canonical snapshot 创建新 proposal。

## 6. Consistency and security

- 任何资源查询都带 user/project predicate；RLS 由 child 2 提供，Application predicate 不能省略。
- 对同一 idempotency key 的 payload 使用 canonical JSON/SHA-256；重复值返回原结果，不重新生成 ID。
- status transition 采用集中 reducer/command dispatcher，避免在 Controller/Adapter 各自维护一份 if/else 状态表。
- proposal content、决策原因和消息正文按 PII/secret policy 处理，不把凭据放入 runtime provenance。

## 7. Test shape

- pure state-transition tests；canonical hash fixtures；repository contract tests。
- two independent command contexts with a barrier to prove concurrent confirmation deduplication。
- authorization and no-partial-write tests。
- adapter test proving AgentCore tool call creates Proposal but never Goal/Production。
