# 研究笔记：Tianming Novel Agent Core Vertical Slice

## 记录目的

本文件把本任务的研究证据、决策来源和迁移边界固定下来。每条内容标记为：

- **当前代码证据**：仓库现状，可直接由路径/符号核对。
- **历史参考**：old/ 或 Obsidian 设计中的既有语义，不代表当前权威实现。
- **目标设计**：本任务建议实现的未来合同。
- **尚未验证**：需要实现或后续部署才能证明。

## 1. 当前四层架构

### 当前代码证据

- `AGENT_CORE_ARCHITECTURE.md:8-49`：Web、Novel Agent、Agent Core、AI 四层和单向依赖；Web 是 durable truth/Application/Worker 权威，AI 是 Pi AI 边界。
- `tianming-agent-core/package.json:1-21`：独立 Node ESM package，Node >=22，脚本为 `test`/`type-check`/`build`，依赖 `@tianming/agent-ai` 和 TypeBox。
- `tianming-ai/package.json:1-29`：独立 Node ESM package，唯一直接依赖 `@mariozechner/pi-ai:0.57.1`。
- `tianming-agent-core/src/types.ts:1-121`：Core 类型明确声明不知小说/Web/数据库；AgentCoreTool 只有名称、描述、schema、execute；AgentCoreOptions 通过 `streamFn` 和 `maxTurns` 注入。
- `tianming-agent-core/src/agent-core.ts:41-237`：loop 的 natural stop、steering/follow-up、abort、maxTurns；`src/agent-core.ts:339-437`：工具查找、TypeBox 校验、错误结果、跳过结果。
- `tianming-agent-core/src/index.ts:1-14`：公共导出只暴露 Core loop/事件/类型，没有领域合同。
- `tianming-ai/src/index.ts:1-84`：受控导出 Pi AI 类型、`streamSimple` 和 `validateToolArguments`。
- `tianming-novel-agent/README.md:1-13`：小说层目前是骨架，规划职责为 Skill、Role、DomainTool、ContextProvider、Hook；不得直接导入 Pi 系列包。
- `tianming-web/README.md:1-9`：前端已落地；后端新架构仍规划中，负责认证、授权、durable truth、事务、Outbox、Worker、Read Model。

### 目标设计

实现最小 Novel Agent adapter，不向 Core 加小说字段；用 port 连接 Web/Application，先用 deterministic fake 证明链路。

## 2. 旧 Agent Runtime 的证据与限制

### 历史参考

- `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts:1-29`：旧运行时直接导入 `@mariozechner/pi-agent-core` 的 `Agent`，并通过 `NovelPiRuntimeOptions` 注入 model、ASP.NET API、discovery skill、streamFn 和 maxToolCalls。
- `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts:30-95`：旧 runtime 将 durable messages 恢复为 Agent messages，监听事件并输出 token/tool/completed/failed；它体现了 runtime adapter 和 message checkpoint 的需要，但使用的是待淘汰的 Pi Agent 路径。
- `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts:98-105`：旧 system prompt 直接拼接 binding、discovery skill 和 project snapshot，说明上下文/绑定需要独立 adapter；新设计改为冻结、版本化 `NovelContextPackage`。
- `AGENT_CORE_ARCHITECTURE.md:84-104`：旧 Pi Runtime、MAF/Structured Runtime、TargetArchitectureDirector 和 legacy Web turn path 被冻结为 legacy；删除需要 callers 为零、替代回归通过、历史数据可读和完整回归。

### 不继承

- 不搬回 `pi-agent-core.Agent` 作为新 Core 实现。
- 不将旧 `maxToolCalls` 直接当作小说生产状态机；Core 的 `maxTurns` 只是安全护栏。
- 不让旧的 `AspNetAgentApi` 形状取代 Application port；新 port 必须携带 actor/project/correlation/idempotency/version。
- 不复用文件系统或旧 checkpoint 作为 Canon 真源。

## 3. 旧 Web/生产与上下文语义

### 当前/历史证据

- `old/Web/NovelAgentWeb/Program.cs` 注册了 `TargetArchitectureDirector`、`IAgentContextAssembler`、`IConversationContextAssembler`、`IGoalCompiler`、`IConversationAgentRuntime`、`IAgentRuntimeEventService` 和 Redis fanout/consumer；这证明读上下文、生产编译和事件路径曾被多个宿主拼装。
- Obsidian《天命小说 Agent 统一上下文与生产架构整合计划》:98-155 提出 `AgentApplicationService`、`IAgentContextAssembler`、`AgentContextEnvelope`、SourceReferences、VersionVector、ContentHashes。
- 同文档:157-192 提出统一 `IMemoryStore` 和数据所有权；作品事实必须进 Canon，记忆不能冒充作品事实。
- 同文档:194-259 提出 Director 只输出结构化 Decision，Goal/Production/Batch 由统一状态机写入，状态变化与 Artifact、Domain Event、Outbox 在同一事务。
- 同文档:261-294 提出 Workflow 是纯投影、旧 MissionPlan/AgentRun/AgentToolExecution 归档为 LegacyExecutionArchive，未完成旧项目生成 RecoveryGoalProposal。

### 目标设计

本任务只采纳这些文档中的领域边界：proposal/goal/context/candidate/review/acceptance/canon、来源/版本/hash、Application 写入权威、Workflow 纯读。固定 Proposal→Goal→Production 不是 Core loop，而是 Novel Agent/Application/Production 用例链。

## 4. 记忆、API、数据库旧设计的降级处理

### 历史参考

- Obsidian《天命AI写作 - Agent 决策系统设计》:7-45 曾将请求处理写成 Controller → Router → Runtime → session/memory/observation/LLM/tool/update memory；这对理解旧职责有用，但其 `AgentRuntime` 不是当前 Core 合同。
- 同文档:47-82 体现“自然对话优先”和按意图触发工具的产品期待；本任务将其收敛为 Proposal/command intent，不默认每条消息写业务状态。
- Obsidian《天命AI写作 - 记忆系统架构》:4-31 将 ChatHistory 压缩为最近消息 + Summary；:34-74 区分 SessionMemory 与 ProjectMemory；这些是未来 MemoryStore 的输入，不在本切片建立四层记忆物理表。
- Obsidian《天命AI写作 - 数据库设计》:22-63 的 `novel_projects`、`agent_sessions` 旧 SQLite schema 提供用户/项目/会话 scope 语义；本任务不直接迁移 SQLite 表，也不把它作为 PostgreSQL 设计完成证据。
- Obsidian《天命AI写作 - API 设计文档》:45-80 的 `/api/agent/chat` 旧响应包含 reply、session/run/phase、decision、RAG 字段；本任务只借鉴 conversation/decision 可观察性，重新以 GoalProposal/Workflow contract 为准。

### 目标设计与边界

- Conversation 原文归 ConversationStore；作者/项目偏好归 MemoryStore；人物、世界观、伏笔、正式章节归 CanonStore；候选/中间产物归 Artifact/CandidateStore。
- 本切片只读取一个最小 project snapshot 和少量 CharacterState/ForeshadowEntry，不实现完整 memory promotion/RAG。
- API DTO 可以兼容现有 `/api/agent` 前缀，但不能恢复旧 `phase=writing` 即代表生产完成的隐含语义。

## 5. EcomGen 借鉴边界

### 当前代码证据

- `EcomGen/` 是独立仓库副本，拥有自己的 `AGENTS.md`、README、package/runtime 和电商领域目录；当前 Git 状态将其整体视为未跟踪研究副本。
- 用户目标是小说垂直 Agent；EcomGen 的电商商品生成、商品素材和电商工作流不是小说领域模型。

### 目标设计

只借鉴可迁移的工程观察（例如领域 adapter、结构化生成、可审计候选/审核边界，若具体代码研究支持），不复制其业务命名、数据表、prompt 或依赖，不将 EcomGen 添加到 Tianming 的 package graph。本任务不得修改 EcomGen。

## 6. 当前明确决定

1. Core 先于 Novel Agent；Novel Agent 先于 Web 生产接线。
2. `tianming-ai` 是唯一 Pi AI import boundary，版本固定 0.57.1。
3. Core event 是 observation，不是 durable truth。
4. Agent 输出 Proposal/Candidate/Review/Command intent；Application/Domain/Worker 写业务事实。
5. 最小切片是一章、一个 batch、interactive acceptance；不是整书生产。
6. 旧 Runtime 只做证据和选择性迁移参考，不整体搬回或删除。
7. fake model/store 用于确定性验证；真实 PostgreSQL/Redis/Qdrant/provider、Outbox relay、lease/fence、RLS 和浏览器 E2E 另建门槛/任务。

## 7. 尚未验证事项

- Novel Agent 新 package 的最终 build/test 脚本、与 Core 的 workspace/file dependency 接法。
- 当前 Web 后端实际可编译的 solution/project 位置，以及 ASP.NET 与 Node adapter 的真实内部 API 契约。
- PostgreSQL 事务、Outbox、RLS、Worker lease/fence 的实现细节和现有 migration 兼容性。
- 真实模型对结构化 GoalProposal/Candidate 输出的稳定性、token/预算/超时策略。
- 现有前端 Workflow 页面是否能无破坏地消费新的 proposal/acceptance/canon projection DTO。
- 旧项目内容迁移、LegacyExecutionArchive、RecoveryGoalProposal 的字段和哈希校验。

这些项目不是本切片的已完成能力；它们必须在后续设计或接线任务中被研究和验收。
