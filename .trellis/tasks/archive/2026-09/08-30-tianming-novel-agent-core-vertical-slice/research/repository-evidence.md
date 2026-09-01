# Repository Evidence Digest

本文件是供实现/检查阶段加载的研究入口；详细的产品决策与完整证据见同目录 `notes.md`，技术合同见 `design.md`。

## 权威当前实现

- `AGENT_CORE_ARCHITECTURE.md:8-49`：四层依赖 `tianming-web → tianming-novel-agent → tianming-agent-core → tianming-ai`，依赖只能向下；Web/Application 是 durable truth 权威，AI 是 Pi AI 边界。
- `AGENT_CORE_ARCHITECTURE.md:51-59`：Core Loop 只有 natural stop、串行工具、错误 tool result、steering/follow-up、abort、maxTurns 和稳定事件；不得引入小说业务阶段。
- `tianming-agent-core/src/types.ts:34-121`：Core tool、事件、状态和 options 的公共合同；Core 明确不知小说/Web/数据库。
- `tianming-agent-core/src/agent-core.ts:41-237,339-437`：run、natural stop、steering/follow-up、abort、TypeBox 校验、未知工具/异常/跳过工具的 error result。
- `tianming-ai/src/index.ts:1-84`：唯一直接导入 `@mariozechner/pi-ai` 的包；当前版本 `0.57.1`，提供受控 `streamSimple` 和参数校验。
- `tianming-novel-agent/README.md:1-13`：小说领域层仍是骨架；职责包括 Skill、Role、DomainTool、ContextProvider、Hook；禁止直接导入 Pi 系列包。
- `tianming-web/README.md:1-9`：前端是 UI/API/SSE 壳，后端规划为认证、授权、durable truth、事务、Outbox、Worker、Read Model。

## Legacy 与历史参考

- `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts:1-95`：旧 runtime 直接使用 `pi-agent-core.Agent`，将 durable messages 映射到 Agent、监听 token/tool/completed/failed；可借鉴 adapter 形状，不可作为新运行时真源。
- `old/Web/NovelAgentWeb/Program.cs`：注册 `TargetArchitectureDirector`、`IAgentContextAssembler`、`IGoalCompiler`、Pi/Structured Runtime 和 Redis 事件设施，证明旧能力存在多写入/多上下文路径。
- Obsidian《天命小说 Agent 统一上下文与生产架构整合计划》:98-155、194-259、261-294：提供 AgentApplicationService、ContextPackage、Goal/Production 状态机、Outbox、Workflow projection、LegacyExecutionArchive/RecoveryGoalProposal 语义；与当前 Core 分层冲突的固定生产链不继承。
- Obsidian《天命AI写作 - Agent 决策系统设计》:7-45、47-82：保留自然对话优先和按意图提议工具的产品语义，不复制旧 AgentRuntime。
- Obsidian《天命AI写作 - 记忆系统架构》:4-74：提供 ChatHistory/SessionMemory/ProjectMemory 作用域语义；本切片不实现完整物理记忆层。
- Obsidian《天命AI写作 - 数据库设计》:22-75、以及《API 设计文档》:45-80：提供旧项目/会话/API 观察点；不把 SQLite schema 或旧 phase 字段当作新 PostgreSQL/生产合同。

## EcomGen 边界

- `EcomGen/` 是独立电商项目副本；不加入 Tianming package graph，不复制商品/电商领域模型，不修改其源码。最多借鉴结构化候选、审核和领域 adapter 的工程观察，且必须重新验证。

## 实施提醒

- 先 contracts/ports，再 Novel Agent adapter，再 deterministic fake model/store vertical slice，再 Application/worker，再最小 API/SSE/frontend evidence。
- 业务写入只能由 Application/Domain/Worker 完成；DomainTool 只能调用 port；Core event 需要宿主映射为 durable message/SSE。
- acceptance 必须检查 project/user scope、idempotency key、expected version、Review passed 和 context hash；重复接受不得重复 merge。
- PostgreSQL、真实 Outbox relay、lease/fence、RLS、真实 provider、完整 RAG/整书生产、Playwright E2E、legacy migration/delete 均是后续门槛。
