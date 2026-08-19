# PRD: Agent-Workflow 集成 - Proposal 持久化与 Agent 工具

## 1. 背景

当前模块化 Agent 控制面已经具备：

- Conversation Runtime 生成结构化 `GoalContract`；
- `ConversationApplicationService` 通过 `IConversationStore` 持久化 `GoalProposal`；
- `AgentControlDbContext` 管理 `agent_goal_proposals`；
- `WorkflowApplicationService.ConfirmProposalAsync` 复用 Domain 状态机创建 Goal、Revision、Production 和任务图；
- 手动 `POST /api/novel-agent/proposals/{proposalId}/confirm` 作为备用入口。

缺口是 Conversation Runtime 没有受限的 Workflow 工具调用合同，因此自然对话只能停留在 Proposed，无法在明确承诺后自动进入现有确认用例。

## 2. 目标

在不引入第二套数据模型或写入边界的前提下，打通：

```text
对话 Runtime → Proposal 持久化 → confirm_creative_goal 工具
→ WorkflowApplicationService → Goal/Production planned
```

### 目标范围

1. 定义 Provider-neutral 的结构化工具调用合同。
2. 注册 `confirm_creative_goal` Application 工具，并通过 Workflow capability 调用现有确认服务。
3. 在 Proposal 保存成功后执行工具，将确认结果写回 ConversationTurn，并沿用对话幂等键重试。
4. Structured 与 MAF Runtime 都能解析工具调用，但不能直接访问数据库或 Workflow 状态。
5. 保留手动确认 API，保证误判或工具失败时仍可恢复。

### 非目标

- 不新增 `goal_proposals` 表或旧 `creative_goals.source_proposal_id` migration。
- 不修改旧 `CommitmentAssessmentService`、`TargetArchitectureDirector`、旧 `AgentToolRegistry`。
- 不改变 GoalCompiler、KernelTaskScheduler、Production 状态机或 Canon 写入协议。
- 不让 LLM、Runtime、Controller 或前端直接写 Goal/Production/Task 状态。
- 不修改前端页面流程；新增 API 字段必须向后兼容。

## 3. 用户故事与验收标准

### 故事 1：明确承诺后自动启动

作为小说作者，我在对话中明确表达“开始生产”，希望系统无需额外点击即可创建生产计划。

- Runtime 返回 `ProposeGoal`、有效 `GoalContract` 和 `confirm_creative_goal`。
- Application 先保存 Proposed Proposal，再执行工具。
- 成功响应包含 `confirmation.goalId`、`goalRevisionId`、`productionId`；Production 持久化为 `planned`。
- Runtime 的原始响应不得声称已写入 Canon；只有工具成功结果可以表明 Goal 已确认。

### 故事 2：幂等与恢复

同一对话请求或工具重复投递时，系统必须返回同一 Goal/Production/CorrelationId，不创建重复 Goal。

- 工具确认键固定为 `conversation:{turnIdempotencyKey}`。
- 工具失败时 Proposal 保持 Proposed，结果包含 `toolError`，后续相同幂等请求可重试。
- ConversationTurn 结果回写失败时，重试工具复用 Workflow 的确认结果。

### 故事 3：手动确认备用路径

当 Runtime 没有请求工具或工具失败时，用户仍可调用既有确认 API；其结果与自动工具路径使用同一个 Workflow Application 用例。

## 4. 合同要求

### Runtime → Application

- `ConversationRuntimeResult.ToolCalls` 为结构化列表，工具调用名称和 JSON 参数不携带 Provider/MAF 类型。
- `confirm_creative_goal` 只允许在 `ProposeGoal` 且存在有效 Proposal 时执行。
- 本轮工具调用不信任 LLM 生成的 `proposalId`；Application 注入刚持久化的 Proposal ID。
- 未知工具返回结构化错误，不执行写入。

### Application → Workflow

- `IAgentTool`、`IAgentToolRegistry` 和 `IWorkflowCommandPort` 位于 Application 层。
- `ConfirmCreativeGoalTool` 不访问 EF；只调用 `IWorkflowCommandPort.ConfirmProposalAsync`。
- `WorkflowApplicationService` 是唯一 Goal/Production 确认与创建边界，继续使用 Domain `GoalProposal.Confirm()` 和现有幂等查询。

### API → Frontend

`ConversationTurnResult` 增加可选 `confirmation`、`toolError` 和 `decision.autoConfirmRequested` 字段；旧客户端忽略这些字段仍可手动确认。Workflow 查询和 SSE 仍是 Production server state 的唯一来源。

## 5. 交付物

- Contracts：工具调用与确认结果类型。
- Application：工具接口、注册表、确认工具、Conversation 编排和结果回写。
- Infrastructure：MAF/Structured adapter 的工具调用解析、DI 注册、ConversationTurn 更新。
- Web/Frontend：Composition Root 注册与 API 类型同步。
- Tests：工具注册/参数注入/未知工具、Runtime 解析、PostgreSQL 自动确认与重复投递回归。

## 6. 质量门禁

- `AgentControlDbContext` 仍是 Proposal/Goal/Production 唯一控制面写入者。
- Domain/Contracts/Application 不引用 EF、Web、MAF 或 Provider SDK。
- 自动确认失败不会丢失 Proposal，也不会产生半个 Goal。
- 前端 test、lint、TypeScript、build 和 `git diff --check` 通过。
- .NET solution build、AgentArchitecture、Unit 与 NovelAgentRegression 必须在 SDK 10.0.400 环境重跑；当前机器缺少该 SDK 时只能记录阻塞，不能宣称全量通过。
