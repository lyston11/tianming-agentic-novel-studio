# 研究证据：Conversation 与 GoalProposal

## Repository anchors

- `tianming-novel-agent/src/contracts.ts`：当前 ConversationTurn/GoalProposal 合同。
- `tianming-novel-agent/src/application/novel-agent-application.ts`：proposal flow 和当前非 durable Application 编排。
- `tianming-novel-agent/src/tools/domain-tools.ts`：`propose_goal` → `ProposalPort` 适配。
- `tianming-novel-agent/test/vertical-slice.test.ts`：未确认不创建 Production 的回归证据。
- `old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs`：proposal lifecycle 和显式 decision 语义。
- `old/Agent/Tianming.NovelAgent.Domain/Goals/GoalContract.cs`：创作合同与 frozen baseline references。
- `old/Agent/Tianming.NovelAgent.Domain/Goals/CreativeGoal.cs`：append-only Goal revisions 的历史语义。
- `.trellis/spec/backend/database-guidelines.md:173-233`：并发确认必须 transaction 内查重、写入、竞争后重读。

## Boundary conclusion

Proposal 是 durable user-facing control-plane record，不能等同于模型输出文本、AgentCore natural stop 或已启动 Production。新实现必须保留 source message、版本/hash、决策 actor 和 Goal link，并让后续 Postgres adapter 承担原子写入。
