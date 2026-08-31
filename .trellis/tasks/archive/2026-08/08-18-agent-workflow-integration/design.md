# Design: Agent-Workflow 集成（当前模块化控制面）

## 1. 目标与边界

对话 Runtime 只产生结构化决策和受限工具调用；Application 层负责持久化 Proposal，工具通过 Workflow capability 确认 Proposal 并创建 Goal/Production。Domain、数据库和 Production 编译器保持单一写入边界。

本设计不新增旧 `NovelAgentDbContext` 表，不复活旧 `TargetArchitectureDirector` 的写路径，不让 Runtime 或 LLM 直接改变 Goal/Production/Task 状态。

## 2. 数据流

```text
Conversation Runtime
  └─ ConversationRuntimeResult { ProposeGoal, contract, toolCalls }
       ↓
ConversationApplicationService
  ├─ GoalProposal.Propose()
  ├─ IConversationStore.SaveTurnAsync → AgentControlDbContext.agent_goal_proposals
  └─ IAgentToolRegistry.Execute(confirm_creative_goal)
       ↓
ConfirmCreativeGoalTool
  └─ IWorkflowCommandPort.ConfirmProposalAsync
       ↓
WorkflowApplicationService
  ├─ GoalProposal.Confirm()
  ├─ CreativeGoal + GoalRevision
  ├─ planned Production / task graph
  └─ GoalConfirmed Workflow event
```

Proposal 持久化与确认分为两个短事务：先确保对话和 Proposal 可审计，再执行确认。确认使用 `conversation:{turnIdempotencyKey}` 作为稳定幂等键；工具失败时保留 Proposed Proposal，重复请求可安全重试。

## 3. 合同

### 3.1 Runtime → Application

- `ConversationRuntimeResult.ToolCalls` 为结构化工具调用列表。
- 只有 `ProposeGoal` 且包含有效 `GoalContract` 时，`confirm_creative_goal` 才可执行。
- 工具参数中的 `proposalId` 可省略；Application 注入本轮新建 Proposal ID，避免 Runtime 伪造内部 ID。
- 未知工具名返回结构化失败，不执行任何数据库写入。

### 3.2 Application → Workflow

- `IAgentTool`/`IAgentToolRegistry` 位于 Application 边界。
- `ConfirmCreativeGoalTool` 只调用 `IWorkflowCommandPort`，不访问 EF 或 Domain 持久化细节。
- `WorkflowApplicationService` 实现 `IWorkflowCommandPort`，复用现有 `ConfirmProposalAsync`、Domain 状态机和 `FindConfirmationResultAsync` 幂等查询。

### 3.3 API 结果

`ConversationTurnResult` 增加可选 `confirmation` 和 `toolError` 字段；旧客户端忽略新字段仍可走手动确认 endpoint。

## 4. 失败与恢复

- Proposal 创建失败：整个对话事务回滚，不执行工具。
- 工具未知/参数无效：保留 Proposed Proposal，返回 `toolError`。
- Workflow 确认失败：保留 Proposed Proposal；同一对话幂等键重试时复用确认幂等键。
- 确认成功但 ConversationTurn 结果回写失败：下次同幂等请求依据 `autoConfirmRequested` 重放工具，Workflow 返回既有结果。
- 已确认 Proposal 不允许第二次 Domain 确认；不得通过 raw SQL 或旧 EF Context 绕过。

## 5. 验证

- Application 单元：工具注册、Proposal ID 注入、未知工具拒绝、确认结果回写和重试。
- Runtime 适配器：Structured/MAF 均能解析 `toolCalls`，仍不引用 Provider/MAF 类型到 Contracts/Application。
- PostgreSQL vertical slice：自然对话一次返回 Confirmation；重复 turn 与重复工具调用返回相同 Goal/Production/CorrelationId。
- 架构测试：新 Application/Contracts 不引用 EF、Web、MAF/OpenAI。
