# 小说 Agent 项目架构审计

## Goal

对天命 AI 小说 Agent 项目完成架构审计，并在同一任务内按审计结论直接实施目标架构：建立项目中立的 Unbound Conversation、用户确认后的项目上下文激活、独立 Node Pi Agent Runtime，以及可验证的 Agent → Canon 单章闭环。

本任务既保留审计证据和最终报告，也允许修改后端、前端、数据库、Node Runtime 与测试代码。六个实施切片在当前任务中按顺序执行，不拆分子任务。

工作聚焦：架构分层、模块职责边界、新旧代码隔离、多用户隔离安全性、数据一致性、可测试性和可维护性，并以可运行测试证明设计边界。

## Background

### 项目概况

- **定位**: 基于 AI Agent 的长篇小说创作工作台,支持多用户、向量检索和智能内容生成
- **技术栈**: ASP.NET Core 8.0/10.0 + React/TypeScript + PostgreSQL + Qdrant + Redis
- **核心机制**: Conversation Agent + Production Workflow + Goal/Canon 账本管理
- **代码规模**: Web 项目约 19.7 万行 C# 代码(不含 Migrations),37 个 Service 子目录

### 当前架构状态

从代码库检查发现:

1. **已完成但尚未完全收口的架构演进**（历史 `08-17-novel-agent-framework-separation` 已于 2026-08-19 归档，残余验收缺口仍需本任务处理）：
   - 已建立独立 `Agent/` 目录和 `Tianming.NovelAgent.{Domain,Contracts,Application,Infrastructure}` 四个项目
   - 目标是将 Conversation Agent 和 Production Workflow 从旧的 `NovelAgentOrchestrator` 中分离
   - 已引入 DDD 分层：Domain/Contracts 不依赖 Infrastructure，Application 通过端口隔离外部依赖
   - 新旧代码仍并存，旧运行时、合法 legacy writers 和全局 guard 的收口尚未完成

2. **现有 Web 项目结构**:
   - 37 个 Service 子目录:AgentRuntime, AgentTools, AgentMemory, Memory, Knowledge, Production, Execution, Goals, Kernels, Workflow, Creative, Quality, Content, Chapters, Projects, Materials, StoryBible, Canon 等
   - 核心决策逻辑在 `Support/AgentCore.cs` (937 行)
   - 16 个 Controllers
   - Workspace 通过 `AsyncLocal` 实现多用户隔离,并有 Phase 1 监控系统(`WorkspaceUsageAuditMiddleware`)

3. **遗留代码**:
   - `MissionPlan`、`NovelAgentOrchestrator`、旧 Runtime/ToolExecution 仍存在
   - 旧 Workflow 已标记 `legacy_read_only`,但默认写入守卫关闭
   - 旧 Worker 仍通过 `NovelAgentDbContext` 写共享表

4. **已完成基础设施**:
   - 知识库自动处理流程(上传 → ProcessKnowledgeFile → 向量化 → Reflection 关联)
   - 向量检索融合 ProjectMemory/AuthorMemory 的 Boost 和题材匹配
   - Workspace 违规追踪服务(24小时内存保留)

## Requirements

### R1: 架构分层与依赖关系审计

检查以下维度:

- Domain/Contracts 是否真正独立于 Infrastructure(EF Core, Npgsql, Provider SDK)
- Application 是否只依赖抽象端口,而非具体实现
- Web 项目是否作为纯 Composition Root,还是包含过多业务逻辑
- Services 目录下 37 个子目录的职责边界是否清晰
- 新旧代码的物理隔离是否足够(Agent/ vs Web/NovelAgentWeb/)

### R2: 模块职责与上帝类/组件审计

识别以下问题:

- 单个文件超过 600 行且职责混杂的"上帝类"(如 `AgentCore.cs` 937 行)
- 单个函数超过 80 行或圈复杂度 > 12 的过长函数
- 37 个 Service 子目录中是否有职责模糊或功能重叠的模块
- `Support/` 目录是否成为"垃圾桶"
- 是否存在 `utils`、`common`、`misc` 等堆积杂项的目录

### R3: 新旧代码隔离与迁移风险审计

评估新旧架构的边界和风险:

- 旧运行时(`NovelAgentOrchestrator`, `MissionPlan`)的写入权限是否已收敛
- 新 Agent 架构的核心接口(`IConversationAgentRuntime`, `IWorkspaceFactory`)是否稳定
- 新旧代码的数据库表共享情况(如 `kernel_tasks`, `outbox_events`)
- 迁移路径是否清晰,是否有回滚机制
- 历史 `08-17-novel-agent-framework-separation` 验收项的阻塞点：旧 Worker ownership 何时收敛

### R4: 多用户隔离与安全性审计

验证以下安全边界:

- `AsyncLocal<NovelAgentWorkspace>` 的线程安全性和泄漏风险
- 是否存在全局静态变量或单例存储用户数据
- LLM 调用是否尊重用户设置的 `llmTemperature` 和 `llmMaxTokens`(避免硬编码)
- 工具权限:原始 `sql/write/http` 工具的虚拟资源边界
- 敏感数据:凭据是否进入 ToolContext/日志/Prompt

### R5: 数据一致性与事务边界审计

检查以下维度:

- PostgreSQL 作为唯一真源的原则是否贯彻(Redis/Qdrant 可重建)
- Goal、Production、Task、Canon 是否各有单一写入入口
- Candidate → 验收 → Canon merge 的事务边界和冲突检测
- Outbox、StreamEvent、SSE replay 的可靠性
- 聚合、Artifact、Domain Event 的事务边界和崩溃恢复

### R6: 测试覆盖与可维护性审计

评估以下质量指标:

- 架构纯度测试是否覆盖依赖规则(AC-1 已通过 16/16)
- 关键业务流程是否有集成测试(如 Conversation → Goal → Production → Canon)
- 是否有合同测试验证 Provider-neutral gateway
- 测试是否依赖真实 MAF/OpenAI SDK,还是可替换为 Deterministic Runtime
- 代码是否易于测试,还是充斥全局状态和隐式依赖

## 已确认的目标方向（架构审计建议必须遵守）

### 产品主旨

- 产品是运行在浏览器中的**小说创作 Agent**；Production Workflow 是保证长流程可靠性、恢复性和审计性的内部机制，不是面向用户的产品主角，也不应演化成新的总编排器。
- 保留 Conversation Agent + Production Workflow 双引擎，以及 Proposal → Goal → Production → Task → Candidate → Acceptance → Canon 领域链路。
- LLM/Agent 负责理解意图、提出 Proposal 和请求工具；确定性 Application/Domain 代码负责授权、副作用、状态迁移、事务和 Canon 写入保护。

### 第一条真实闭环

- 第一阶段范围固定为：Web 对话 → 结构化 Proposal 卡片 → 单章、单候选版本生产 → 用户人工 Acceptance → 用户单独确认 Merge → Canon 版本增加且能查询合并后的正文。
- 测试顺序固定为：Application/Database E2E → API/SSE E2E → 浏览器 E2E。
- 第一条 E2E 使用 PostgreSQL、真实事务/Outbox/Worker Claim、Deterministic Runtime 和固定最小创作合同；不以真实 LLM 的非确定输出作为成功路径前提。
- SSE 仅通知变更，数据库 Read Model/API 是页面权威来源；前端收到事件后重新查询。

### 用户决策与安全边界

- Proposal 是可审计的用户级对象，支持修改、拒绝、确认、过期和 Supersede；修改创建新 Revision，旧版本保留并标记 Superseded。
- 默认允许 SingleChapter 隐式启动；InteractiveBatch 和 AutonomousBook 需要显式确认；超出成本、章节数或时间阈值必须显式确认。
- 授权合并优先级为：服务端硬上限 > 项目安全策略 > 用户偏好 > 系统默认值；LLM 不得绕过任何上限。
- Acceptance 和 Canon Merge 是两个明确的用户动作。
- Acceptance/Merge 使用 CandidateVersion、CanonVersion、IdempotencyKey 和既有 Canon lease/fence 做并发与幂等保护。
- 失败采用任务级重试、状态不可逆、Canon 不回滚；已合并内容通过新 Candidate/Rework 修正。

### 收口顺序

1. 跑通 Agent → Canon 单章 E2E。
2. 盘点并迁移遗留控制面写入者。
3. 完成 pause/resume/cancel/safe-point 等合法状态控制路径的 owner 收口。
4. 增加 Legacy guard preflight，并在测试环境开启。
5. 回归旧 API、Agent API、Worker 和 Recovery 后再考虑生产环境 cutover。

### Pi Agent Core 采用决策

- 自研 `StructuredConversationAgentRuntime`/自研 Agent Loop 不再作为长期 Agent Loop 核心；Agent Loop 采用 `@earendil-works/pi-agent-core`，模型/provider 适配采用 `@earendil-works/pi-ai`。
- 不整体引入 `@earendil-works/pi-coding-agent`：coding-agent 的终端 UI、文件工具和代码工作流不属于小说产品；小说领域能力通过薄宿主层的工具、hooks、context transform、resources、事件流和 Web/SSE 适配提供。
- Pi 的核心职责保持最小：模型请求、消息上下文、工具调用、流式事件、steering/follow-up 和可插拔 provider。小说知识、章节生产、Proposal/Goal/Production/Candidate/Canon 状态及持久化仍由本项目的领域/Application/Infrastructure 所有。
- Pi 的 `Agent`/loop 是进程内运行时，不是业务数据库的权威状态机；会话、操作恢复、Outbox、Worker lease 和 Canon 事务不能假设由 Pi 内存自动保证。
- Pi 的 `beforeToolCall`/`afterToolCall`、`transformContext`、`prepareNextTurn`、事件订阅和自定义消息是领域接入点；工具本身调用现有 Application ports，不能绕过现有写入所有者直接操作 EF/Canon。
- 小说领域工具直接实现 `pi-agent-core` 的原生 `AgentTool` 合同，不复制 `pi-coding-agent` 的文件/终端工具，也不先增加万能工具抽象。每个工具提供 `name`、`label`、面向模型的 `description`、TypeBox `parameters` 和 `execute(toolCallId, params, signal, onUpdate)`；失败通过 throw 交给 Loop 生成 `isError` ToolResult，有副作用的工具采用 `executionMode: "sequential"`，授权与审计分别落在 `beforeToolCall`/`afterToolCall` 和 ASP.NET Application 边界。
- 研究边界：当前仓库中的 `pi-agent-core` 基础 `Agent` loop 可用；`AgentHarness` 文档描述了更完整的 durable session/hook/runtime 方向，但当前源码仍有部分 `HarnessNotImplemented`，不能作为现成功能或本阶段依赖。
- 原先以成本、章节数或时间门限直接决定 Agent 是否启动的方案不再作为 Pi loop 的门控假设；这部分需要按新的 Agent 驱动方向重审。Acceptance/Merge 等不可逆业务动作仍由领域工具和确定性业务规则保护，不把通用门限塞进 Agent Loop。

### 用户对 Pi 承载与 Agent 驱动方式的确认

- Pi Runtime 采用独立 Node Agent Runtime Service，通过内部 HTTP 或 gRPC 与 ASP.NET Application 通信；不在 ASP.NET 进程内复刻或嵌入 Node Loop。
- PostgreSQL 中的 Conversation/Message/Proposal 是唯一权威；Conversation 创建时不要求绑定项目，Pi 只保留一次运行所需的进程内上下文，恢复时从数据库 Read Model 重建，不建立第二套持久化会话真源。会话初始处于无项目上下文状态，项目识别和绑定完成后才加载项目相关的 Read Model 与摘要。
- 小说领域 Tool 只提交经过 Application 授权的业务意图/命令并返回 `productionId`、状态或其他可查询结果；长流程由 Worker 推进，Agent 默认不因 Worker 或用户控制面事件自动 wake-up，而是在用户下一次消息或创建新 Conversation 时读取最新项目状态。
- 不采用固定的 Proposal → Goal → Production → Candidate → Acceptance → Canon 链式触发器。Pi Agent Loop 自主决定下一次模型请求和工具调用；工具集合提供观察、决策和动作能力，Agent 根据当前上下文与工具结果逐步推进。
- “Agent 自主决定”不等于“Agent 拥有业务写权限”：每个有副作用的工具仍必须经过 Application 的状态校验、用户/项目策略、服务端安全上限、幂等键和租约/fence。Acceptance 与 Canon Merge 不注册为 Agent Tool，也不属于 Agent 的动作模型；Agent 完成被授权的工作后，领域状态变化负责生成待人工决策的 Read Model 和 Web 卡片。用户完成 Acceptance、再完成 Merge 后，系统发布事实事件；这些事实事件由 Outbox 可靠投递并由 Web/SSE 通知页面，但不进入每个 Conversation 的 Agent 事件 Inbox，也不自动触发 Agent 推理。Agent 在用户下一次交互时读取最新项目 Read Model 并刷新认知状态，不能调用对应控制面动作。
- Agent Loop 保持 Pi 原生停止语义：Assistant 本轮没有 Tool Call，且没有 steering/follow-up 时自然结束；Provider 返回 error/aborted 时停止。通用 Loop 不预设 Proposal/Production 阶段顺序。`shouldStopAfterTurn`、AbortSignal 和 ToolResult `terminate` 只保留为取消、服务关停或明确异步交接等运行时机制，不作为领域编排器。
- 第一条验收不再断言固定工具调用序列，而断言在确定性 Agent Runtime 下，Agent 可以通过允许的工具集合推进 Conversation → Proposal/Goal → Production → Candidate；Candidate 完成后由领域投影生成待人工决策状态，用户随后独立完成 Acceptance 与 Canon Merge，并使 Canon 版本增加且正文可查询。中间查询和 Agent Tool 顺序可由 Agent 决定；`accept_candidate`、`merge_canon`、`request_acceptance` 等控制面动作不出现在 Agent Tool 集合中。
- 长任务提交后当前 Agent Run 可以自然结束或以 ToolResult `terminate` 明确交接；ASP.NET/Worker 通过 Outbox 持久化状态变化并通知 Web，但默认不自动唤醒任何 Agent Run，以避免多个 Conversation 指向同一任务时发生 fan-out 和重复推理。Outbox 只负责事实变化的可靠投递，SSE/Redis fanout 只负责向具体 Web 页面发送变更通知；Agent 不作为项目事件消费者，不维护按 Conversation 分裂的事件 Inbox、`eventId` 消费表或项目事件序列。用户在某个已绑定项目的 Conversation 发送新消息或创建新 Conversation 并完成项目绑定时，Node 从 PostgreSQL 重建该 Conversation 消息上下文并读取最新项目状态；如果当前 Conversation 有 active Run，用户消息按 Pi 的 steering/follow-up 运行语义进入该 Run，否则创建新 Run。未绑定 Conversation 只读取通用上下文和用户可访问项目的最小目录元数据，不读取任何项目正文或业务状态。若未来需要自动恢复，只能由显式 Conversation 订阅和独立幂等 wake-up 策略启用。
- continuation run 继承同一 Conversation 的持久化消息、可重建摘要/检查点和当前项目 Read Model，不继承旧 Node 进程的闭包、队列或其他隐式内存。每个新 Run/trace 在第一次模型调用前读取一次带版本号的权威状态快照；领域 ToolResult 和外部变化事件携带最新对象版本，Agent 按需再调用观察工具，不在 `transformContext` 中每次模型调用都无条件远程读取。
- 当前 `agent_conversation_messages` 只保存 `Role + Content`，不足以无损恢复 Pi 的 assistant content blocks、Tool Call、ToolResult 和自定义状态事件；实施设计必须扩展为 provider-neutral 的持久化消息 envelope。`ConversationRuntimeCheckpointRecord` 可以保存带源消息序号的摘要/压缩派生物，但不得成为第二会话真源。
### Conversation 项目发现、确认与上下文激活

- 新 Conversation 始终以 `Unbound` 创建，创建合同不接收 `projectId`，数据库 `AgentSession.ProjectId` 必须为 `NULL`。项目 ID 不能作为“创建会话时已确认”的兼容捷径，也不能静默解释为发现提示。
- 未绑定 Conversation 仍运行完整的 Pi Agent Loop：它可以继续普通交流、澄清意图、调用通用能力，并通过项目发现 skill/resource 或只读 `AgentTool` 获取当前用户可访问项目的最小目录元数据（项目 ID、标题、状态、更新时间）和展示候选。未确认不会触发全局 guard、abort、`terminate` 或暂停整个 Agent。
- 项目发现可以自动执行；项目绑定必须来自用户明确确认。即使只有一个高置信度候选，Agent 也只能展示候选并继续对话，不能自行绑定。
- 用户确认后，Agent 可调用窄的 `activate_project_context` 上下文工具，或者由等价的 Web 候选卡确认动作调用同一个 ASP.NET Application command。确认是交互/工具合同，不是独立的 LLM 承诺评分器或包围每个回合的硬性检测门槛。调用携带候选项目、Conversation 和确认来源消息/动作的审计引用；ASP.NET 校验用户所有权、项目访问权、Conversation、幂等键和版本，但不运行新的语义置信度分类器。
- 若 Agent 在缺少确认来源时尝试激活，工具返回可恢复的结构化 `confirmation_required` ToolResult；Pi Loop 继续运行并可继续询问用户，不把该结果升级为运行级错误。
- 绑定成功只改变 Conversation 的 active project context，并记录上下文切换边界；它不创建 Goal、不启动 Production、不接受 Candidate、不合并 Canon，也不产生其他小说领域副作用。
- 确认前不得加载项目正文、Canon、Story Bible、Goal、Proposal/Production、Knowledge 等业务状态，也不得向该 Run 提供项目写工具。绑定成功后，Node 宿主才读取带版本号的最新项目 Read Model，并在下一模型回合启用该项目允许的读写工具；项目工具的每次调用仍由 ASP.NET Application 重新授权。
- `POST /agent/session`、前端 `createAgentSession` 和 Session Application 接口必须删除创建时 `projectId` 输入，使用语义明确的无项目创建合同；不得仅靠调用方约定传 `null`。
- Conversation 回合入口不得要求或信任客户端/Node 传入 `projectId`。目标合同以 `userId + sessionId` 读取服务端会话绑定；未绑定时构造通用 `UnboundConversationContext`，绑定后构造 `BoundConversationContext` 并加载项目快照。
- Context Builder 不采用把全部现有 `ProjectId` 粗放改为 nullable 的方案。Conversation 上下文必须明确区分 Unbound/Bound；Goal commit、Kernel execution 等本来要求项目作用域的路径继续使用强类型、必填的项目上下文，避免把“允许未绑定对话”扩散成“领域执行可以无项目”。
- Conversation 不复制其他 Conversation 的原始消息。跨项目 handoff summary 的生成、压缩和继承机制暂缓；同一 Conversation 未来若允许切换项目，必须追加可审计的上下文边界，不能混合两个项目的摘要或写工具权限。
- 同一项目允许多个 Conversation 并行运行 Agent；每个 Conversation 独立持有 Run Lease。项目共享对象继续由 ASP.NET Application 的用户/项目隔离、幂等键、乐观版本和实体级 Lease/Fence 保护；不引入项目级单一 Agent 队列。多个 Conversation 在下一次用户交互时读取同一份项目 Read Model，而不是各自消费和去重项目事件。

### Pi 研究依据

- 第一阶段内部协议暂定 HTTP/JSON：ASP.NET 调用 Node run API，Node 调用 ASP.NET 内部 Tool API，流式输出复用 NDJSON/SSE；长任务通知继续通过 Outbox。后续可在不改变领域 Tool 合同的前提下评估 gRPC。
- Agent Tool 的副作用权限采用分级策略：Proposal 创建、查询和创建新 Revision 可由 Agent 调用；Goal 确认和低成本 SingleChapter 启动在默认安全阈值内可由 Agent 调用；超出项目/用户配置的成本、章节数或时间预算时返回待用户授权的状态；InteractiveBatch、AutonomousBook、Acceptance 和 Canon Merge 始终由用户控制面触发。待用户授权不是隐藏的 Agent Tool，而是领域状态和 Web 交互共同产生的事实；用户操作完成后，事实通过 Outbox 投影到 Web，Agent 在用户下一次交互时读取最新项目状态，不由该事件自动唤醒。
- Node 的模型配置/API Key 传递方案暂缓决定；无论最终采用短期凭据下发还是 ASP.NET 模型代理，Node 不得直连业务数据库或持久化用户密钥。
- Pi 官方理念是保持核心最小并通过扩展适配工作流，而不是把权限、计划模式、子 Agent、领域状态硬编码进核心：<https://github.com/earendil-works/pi>。
- `pi-agent-core` 已提供 `Agent` 状态、`AgentLoopConfig`、工具执行模式、`beforeToolCall`/`afterToolCall`、`transformContext`、steering/follow-up 和事件流；这些能力足以承载小说 Agent 的薄宿主层。
- `pi-coding-agent` 的 Extension/Skill/Resource 机制是宿主层范式，可借鉴边界和生命周期，但不应把 coding-agent 的终端产品能力搬入 Web 小说系统。

## Out of Scope

- 不审查代码格式、命名风格、注释密度等纯审美问题。
- 不全面重构与六个实施切片无关的现有功能；只修改 Session/Conversation/Context、项目发现与绑定、Node Pi Runtime、Agent→Canon 闭环及其必要测试和适配代码。
- 不全面审查前端 React/TypeScript 组件；只修改和验证 Session/Conversation/项目上下文、权威 Read Model、SSE 刷新及必要 Web 控制面。
- 不重新挑战用户已经确认的产品需求；实现必须保持这些边界。
- 延后跨项目 Conversation 切换、handoff summary、Node 模型凭据最终方案、gRPC 迁移、默认 Agent 自动 wake-up 和未实现的 `AgentHarness` 能力。

## Acceptance Criteria

### 审计与架构交付

- [ ] **AC-1**: 更新 `audit-report.md`，使其与当前 Pi Agent、Unbound Conversation、用户控制面和 Agent→Canon 决策一致；每个问题包含描述、影响、风险等级和改进方向。
- [ ] **AC-2**: 识别有证据支持的上帝类和职责不清模块，并避免把生成文件、模型聚合文件或仅体积较大的内聚服务误报为上帝类。
- [ ] **AC-3**: 评估新旧代码隔离、共享表写入所有权、迁移阻塞点、回滚点和 legacy guard 启用前提。
- [ ] **AC-4**: 验证多用户、Conversation、项目绑定、工具权限与凭据边界，指出并修复本任务范围内的数据泄漏风险。
- [ ] **AC-5**: 验证 PostgreSQL 单一真源、事务、Outbox、幂等、租约/fence、版本冲突和 SSE/Read Model 权威边界。
- [ ] **AC-6**: 最终报告提供 P0/P1/P2 建议，并区分本任务已实施、仍待实施和明确延后的事项。
- [ ] **AC-7**: 最终 Markdown 审计报告保存于当前任务目录的 `audit-report.md`，过时的 `handoff-summary.md` 明确标记为历史材料或同步更新。

### 当前任务的产品实施

- [ ] **AC-8（原 IC-1）**: 新 Conversation 的 API、Application 合同和数据库行为均不接受或持久化初始 `projectId`；创建结果明确为 Unbound，旧 query 不得静默绑定。
- [ ] **AC-9（原 IC-2）**: Conversation 回合合同不接收调用方 `projectId`；Unbound Conversation 可持久化 user/assistant turns、执行通用 Pi Loop、自然停止和项目发现，且不会创建 Proposal/Goal/Production 或被全局 abort/terminate。
- [ ] **AC-10（原 IC-3/IC-5）**: Conversation Context 明确区分 Unbound/Bound；确认前不加载正文、Canon、Story Bible、Goal、Proposal/Production、Knowledge 或项目写工具，同时 Goal/Kernel/Canon 路径继续要求强项目作用域。
- [ ] **AC-11（原 IC-4）**: `list_accessible_projects` 只返回按用户过滤的最小目录元数据；`activate_project_context` 只在可审计的用户明确确认后幂等绑定，缺少确认返回可恢复 `confirmation_required`，绑定本身不产生小说领域副作用。
- [ ] **AC-12**: 独立 Node Agent Runtime Service 使用 `@earendil-works/pi-agent-core.Agent` 与 `@earendil-works/pi-ai`，通过 HTTP/JSON 调 ASP.NET 内部 Application API，不直连业务数据库、不依赖未实现 `AgentHarness`，并从 PostgreSQL 的 provider-neutral message envelope 重建 Run context。
- [ ] **AC-13**: Agent 自主选择允许的观察和领域工具，不实现固定 Proposal→Goal→Production 链；Acceptance 与 Canon Merge 不注册为 Agent Tool，Candidate 完成后只投影待人工决策状态，由用户分别执行 Acceptance 和 Merge。
- [ ] **AC-14**: PostgreSQL + 真实事务/Outbox/Worker Claim + Deterministic Runtime + one Worker 的 Application/Database E2E 跑通 Conversation→Proposal/Goal→Production→Task→Candidate→用户 Acceptance→用户 Merge，并断言 Canon 版本增加且正文可查询。
- [ ] **AC-15**: 在 AC-14 通过后，依次增加并通过 API/SSE E2E 与浏览器 Playwright E2E；SSE 只通知刷新，API Read Model 保持页面权威。
- [ ] **AC-16**: 并发 Conversation 使用独立 Run Lease，共享项目对象通过租户授权、幂等键、乐观版本和实体 lease/fence 保护；在合法 legacy writers 未全部迁移且完整 E2E 未通过前，不启用全局 `EnforceLegacyControlPlaneReadOnly`。

## Evidence Collected

### 代码规模与结构
- **后端代码量**: 73,619 行 C# (不含 Migrations)
- **测试文件**: 215 个测试文件,分布在 6 个目录(AgentArchitecture, AgentKernelRegression, NovelAgentRegression, Regression, Unit, Fixtures)
- **大文件**: 19 个非 Migration 文件超过 600 行
- **上帝类候选**: `AgentCore.cs` (937 行, 53 个类型定义) - 已确认为模型聚合而非上帝类

### 新架构进展
- **Agent/ 目录**: 4 个新项目(Domain, Contracts, Application, Infrastructure), 47 个 .cs 文件
- **架构纯度测试**: `TargetArchitecturePurityTests` 包含 16 个测试用例,验证:
  - 旧 Runtime 组件已移除
  - 新 DI 注册正确
  - KernelTaskScheduler 只通过 AgentControlDbContext 写入
  - GoalCompiler 不依赖 LLM Service
  - Director 只通过 IAgentContextAssembler 读取
  - SSE 使用 Authorization header 而非 query token
  - 认证日志不序列化 token
  - 项目文件不编译仓库外源码

### 37 个 Service 子目录
从 `Web/NovelAgentWeb/Services/` 下找到的子目录(需要进一步检查职责边界):
- AgentRuntime, AgentTools, AgentMemory, Memory, Knowledge, Production, Execution, Goals, Kernels, Workflow, Creative, Quality, Content, Chapters, Projects, Materials, StoryBible, Canon, Auth, Workspace, Context, Repositories, Collaboration, VectorSearch, Export 等

### Conversation 项目上下文现状
- `AgentSession.ProjectId` 已允许为 null，运行时 `AgentSession.ActiveProjectId` 也已有空值语义；数据模型具备 Unbound 基础。
- `POST /agent/session` 仍接受可选 query `projectId`，`IAgentSessionService.GetOrCreateSessionAsync` 和持久化服务仍可在创建时写入它；这是必须移除的旧绑定入口。
- 前端 `AgentPage` 当前调用 `createAgentSession()` 时未传项目，但 API helper 仍暴露可选 `projectId`；目标合同需要同时在 TypeScript 和 ASP.NET 删除该能力。
- `ConversationApplicationService.AppendTurnAsync` 和 `ConversationTurnContext` 仍要求 `string projectId`，会把项目身份作为每轮调用参数；目标实现必须改为按 Session 服务端解析，而不是简单放宽为客户端可传 null。
- `AgentContextRequest`/`AgentContextAssembler` 当前把 Conversation、GoalCommit、ChapterExecution 放在同一个强制项目模型中并直接加载 Goal、Memory、Knowledge、PendingIntents；实现时必须拆出 Unbound/Bound Conversation 上下文，保留 Goal/Kernel 的项目强约束。

### 架构不变量(从 design.md)
1. PostgreSQL 是 Goal、Production、Task、Artifact、Conversation、业务事件和 Canon 的唯一真源
2. LLM 只产出 Proposal、Artifact 或结构化判断；状态迁移由确定性代码决定
3. Conversation Runtime 不得写 Production、Task 或 Canon
4. 旧 MissionPlan、NovelAgentOrchestrator、Runtime/ToolExecution 只能读取和投影
5. Goal、Production、Task 和 Canon 各有一个写入所有者

## Implementation Verification Backlog（非阻塞产品决策）

以下问题通过实施期代码检查和测试回答，不再阻塞范围收敛：
1. **37 个 Service 子目录的实际职责**: 需要检查每个目录的文件数和核心接口,判断是否有职责重叠
2. **新旧代码的数据库表共享**: 需要检查 `AgentControlDbContext` vs `NovelAgentDbContext` 的 Entity 映射
3. **LLM 参数硬编码检查**: 需要 grep 所有 LLM 调用点,验证是否尊重 `user_settings.json`
4. **AsyncLocal 泄漏风险**: 需要检查 `WorkspaceFactory` 的实现和 `AsyncLocal` 清理逻辑
5. **Outbox 可靠性**: 需要检查 `ProductionOutboxDispatcher` 的重试和死信处理
6. **测试覆盖的实际深度**: 215 个测试文件覆盖了哪些核心流程?

### 已确认决策

**执行模式**：深度代码审计 + 目标架构产品实施

**审计重点**：
1. 产品构思的合理性（小说 Agent 的核心流程是否符合创作场景）
2. 技术方向的可行性（Agent + Workflow 双引擎模型、Proposal → Goal → Production 抽象层级）
3. 开发计划的现实性（历史方案的 16 个 AC、9 个 Phase 及其阻塞根因）
4. 集成接口分析（Agent 和 Workflow 的实际集成状态）

**核心发现**：
- 用户的系统是**运行在 Web 端、由 Agent 驱动的小说创作系统**；工作流是内部可靠执行机制，不是产品主旨，也不应新增大型总 Orchestrator
- Production Workflow（天命小说内核）已具备大量独立能力，但从 Agent 对话到 Canon 的单条真实闭环仍缺少验收证据
- Agent Conversation 层已具备 Proposal 持久化和 `confirm_creative_goal` Tool Call 路径
- 当前主要问题已经从“两个子系统完全断裂”转变为：副作用授权策略尚未收口，Candidate/Acceptance/Canon 后半段仍主要由 Web/legacy owner 承载，完整链路、恢复和 cutover 尚未验证

**输出交付物**：
- `agent-workflow-integration-analysis.md`：深度代码审计报告（112K tokens）
- `audit-report.md`：与最终实现一致的架构审计报告
- 后端、前端、数据库、Node Runtime 和分阶段 E2E 测试代码
- `simplified-architecture-proposal.md`：早期误判（仅保留历史记录，不作为实现依据）

## Notes

- 审计重点是“具有实际维护成本的结构性问题”，而非单纯的代码坏味道。
- 尊重已确认的架构演进方向和当前 PRD/design 合同。
- 区分历史遗留问题与本次新引入问题。
- 所有判断必须基于可观察证据（代码、目录结构、依赖关系和可运行测试）。
- 本任务允许修改产品代码，但仅限六个实施切片及其必要适配、迁移、测试和文档。
