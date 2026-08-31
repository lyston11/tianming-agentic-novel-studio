# Implementation Plan: Agent-Workflow 集成（当前模块化控制面）

## Phase 1：上下文与合同

- [x] 在 Contracts 增加结构化 `AgentToolCall`，为 `ConversationRuntimeResult` 增加 `ToolCalls`，为 `ConversationTurnResult` 增加可选确认结果/错误。
- [x] 在 Application Ports 增加 `IAgentTool`、`IAgentToolRegistry`、`IWorkflowCommandPort` 和工具执行上下文/结果。
- [x] 保持旧构造调用兼容，未知工具不触发写入。

## Phase 2：工具与 Workflow capability

- [x] 实现 Application 层 `ConfirmCreativeGoalTool`，只通过 `IWorkflowCommandPort` 调用现有 Workflow 确认用例。
- [x] 让 `WorkflowApplicationService` 实现 capability，并在 Composition Root 注册 registry/tool/interface。
- [x] 使用 `conversation:{request.IdempotencyKey}` 作为确认幂等键。

## Phase 3：Conversation 集成

- [x] `ConversationApplicationService` 在 Proposal 保存成功后执行受限工具调用。
- [x] 成功结果回写 ConversationTurn；失败保留 Proposed Proposal 并返回 `toolError`。
- [x] 同一 turn 重试时重放工具并复用 Workflow 幂等结果；未知工具不执行。

## Phase 4：Runtime 适配器

- [x] 更新 Structured runtime prompt/JSON parser，支持 `toolCalls`。
- [x] 更新 MAF adapter parser/prompt，保持 MAF 类型隔离在 Infrastructure。
- [x] 保持“Runtime 不声称已启动生产，只有工具成功后 Application 结果包含 confirmation”的约束。

## Phase 5：测试与验证

- [x] 增加工具注册、参数注入和未知工具拒绝单元测试；Runtime 工具调用解析回归测试已添加。
- [x] 扩展 PostgreSQL vertical slice 覆盖自然对话 → Proposal → Confirmation → duplicate。
- [x] 运行 `dotnet build TianmingAgenticNovelStudio.slnx --no-restore -warnaserror`；使用临时安装的 SDK 10.0.400 验证，0 warning / 0 error。
- [x] 运行 AgentArchitecture（22）、Unit（798）、AgentKernelRegression 和 NovelAgentRegression（157）测试，全部通过；并发同幂等键确认回归包含在 AgentArchitecture 中。
- [x] 运行前端 test（7）、ESLint、TypeScript build、Vite production build 和 `git diff --check`，全部通过；Vite 仅报告既有的大 chunk 非阻塞提示。

## 明确不做

- 不新增 `goal_proposals`/旧 `creative_goals.source_proposal_id` migration。
- 不修改旧 `CommitmentAssessmentService`、`TargetArchitectureDirector`、旧 `AgentToolRegistry`。
- 不提交 Git commit；不宣称完成尚未通过的全量检查。
