# 技术设计：TS 边界收敛

## 裁决

提案生命周期归 C# Application/Domain/Infrastructure 控制面。`old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` 及其 Application 工作流是唯一生产状态机；`confirmProposal` 的结果必须能和 Goal、GoalRevision、Production、Batch、Task 在 `AgentControlDbContext` 的同一事务中提交。TypeScript 不再实现提案 reducer，也不再把提案状态转换为本地 durable 记录。

continuity gate 分两层但只有一个生产权威：C# `ChapterGatekeeper` 负责生产阶段的硬门禁及其持久化结果；TS `runContinuityGate` 仅保留为候选意图的纯预检，输出 Review 供 Application adapter 提交，不改变任何 durable 状态，不得替代 C# 门禁或执行 Canon merge。两者输入边界不同：TS 只检查本 vertical slice 的章节号、冻结包 hash 和引用完整性，C# 负责完整生产门禁规则。

## 数据流

```text
Core/Role/Skill
  -> DomainTool: ProposeGoalInput
  -> ProposalPort.submitProposalIntent
  -> C# Application + AgentControlDbContext
  -> GoalProposal result / read model

Goal confirmation command
  -> C# transaction: proposal + Goal + Revision + Production + Batch + Task
  -> GoalCommitResult/read model + frozen context reference

Core/Role
  -> DomainTool: ChapterCandidateIntent
  -> CandidatePort.submitCandidateIntent
  -> C# Application / test-only deterministic adapter
  -> CandidateChapter
  -> TS pure preflight Review
  -> CandidatePort.saveReview (adapter persists result)
  -> C# ChapterGatekeeper remains production acceptance authority

Accepted candidate
  -> Production/Canon application command
  -> C# transaction/outbox handshake
  -> Canon/read model
```

## 接口变化

- 从 `ports.ts` 删除完整的 `GoalProposalCommands` 生命周期接口（propose/revise/reject/discard/confirmProposal 及其对应列表/intent 方法）。
- `ProposalPort` 改为 `submitProposalIntent(ProposeGoalInput)` 加提案读取方法；DomainTool 只组装输入并转发，不构造 `GoalProposal`。
- `ProductionCommandPort` 保留为 Application-facing command adapter；确认、候选接受和失败任务都由宿主实现，TS 不声称拥有事务。
- `NovelApplicationPorts` 继续作为 vertical-slice 测试所需的聚合接口，但不包含 proposal reducer 或 Canon/ledger/projection 的生产实现。

## 测试替身

将 `InMemoryNovelStore` 移到 `tianming-novel-agent/test/fixtures/` 并改名为 `InMemoryNovelTestStore`，仅测试导入。它可以模拟 C# Application/Infrastructure 的端口结果以保留垂直切片的负向语义，但文件头明确标注：它不是生产适配器，不证明 PostgreSQL、事务、Outbox 或 Worker 已实现。`src/` 不再包含 Canon merge、账本更新或 WorkflowProjection 构造实现。

原 `InMemoryContextProvider` 继续作为 ContextProvider 的确定性测试适配器使用；其快照冻结语义由现有测试锁定，生产持久化仍由宿主负责。

## 兼容与风险

- 删除 `proposal-lifecycle.ts` 前，将 expected version/hash、终态不可重开、显式拒绝原因等检查记录到 `notes.md`，交由 `09-01-reliability-gap-check` 核对 C# 覆盖。
- 提案幂等冲突由 `submitProposalIntent` 的宿主适配器负责；Application 不再重复计算或校验提案 content hash。
- 11 项现有行为测试仍通过测试替身验证端口链路；属于 C# durable 语义的项在 notes 中逐条标明“测试替身模拟，生产覆盖由 C# 控制面负责”。
