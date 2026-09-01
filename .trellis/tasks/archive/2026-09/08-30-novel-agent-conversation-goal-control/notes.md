# 研究笔记：Durable Conversation and Goal Proposal Control

## Confirmed evidence

- 当前 `tianming-novel-agent/src/contracts.ts` 有 `ConversationTurn` 和 `GoalProposal`，但 Proposal 没有 lifecycle status，ConversationTurn 只是 Application 输入。
- 当前 `tianming-novel-agent/src/application/novel-agent-application.ts` 每次创建新的 AgentCore，只处理当前消息；没有 durable Conversation、assistant message、run 或 checkpoint store。
- 当前 `tianming-novel-agent/src/tools/domain-tools.ts` 的 `propose_goal` 已经通过 `ProposalPort` 提交提案，适合保留为 adapter boundary。
- `old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` 提供 Draft/Proposed/Confirmed/Rejected/Superseded/Discarded 等 proposal 语义和显式决策。
- `old/.../GoalContract.cs` 与 `CreativeGoal.cs` 提供 structured contract、append-only revisions、frozen references 和 proposal→Goal provenance。
- backend database guideline 的 Concurrent Proposal confirmation 场景要求在同一 Serializable transaction 中查询/写入，竞争失败后重读 durable result。

## Decisions

- Conversation 是 Web/Application durable state；Novel Agent 不拥有会话真源。
- Proposal 可多条并存；active confirmed Goal/Production 由数据库唯一策略限制为一个。
- GoalRevision 追加保存 proposal source/version，不以“Goal status = revision superseded”混淆两个生命周期。
- checkpoint 只是 recoverable execution metadata，不恢复旧 Pi/MAF loop，也不等于 Goal/Canon 状态。

## Deferred

- 实际表名、EF migration、RLS 和 PostgreSQL transaction 由 control-plane child 实现。
- 真实 provider、budget、token accounting、完整 conversation summarization 另建任务。
