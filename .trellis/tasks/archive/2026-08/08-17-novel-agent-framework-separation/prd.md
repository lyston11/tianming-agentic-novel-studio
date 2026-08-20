# 小说 Agent 新架构框架设计与旧代码分离

## 最终目标

在同一仓库内完成一套与旧运行时隔离的新小说 Agent 框架，并以可执行合同、独立程序集、单一写入所有权和端到端证据证明新旧边界可以安全切换。

新框架必须把创作意图讨论、知识研究和可靠生产执行组织成可协作但不越权的运行时：Conversation Agent 负责用户交互和声明式 handoff，Production Workflow 负责确定性 DAG 执行，Goal/Production/Task/Canon 各自只有一个权威写入入口。当前任务的关键完成证据是“对话提案 → Goal 确认 → 单批生产 → Candidate → 人工验收 → Canon 合并 → Workflow 投影”的真实闭环。

## 背景与代码库事实

- `MissionPlan`、`NovelAgentOrchestrator`、Runtime/ToolExecution 和旧 Workflow 投影仍存在；旧 Workflow 已有 `legacy_read_only` 语义，需保留作兼容、审计和恢复输入，但不能继续成为新执行模型。
- Goal 路径与旧 MissionPlan 投影已有初步隔离；旧执行状态不能原地续跑，恢复必须根据正式内容和已确认决策重新形成 Goal。
- Canon、Knowledge、章节版本和模型完成服务已有可靠接口，可通过 Adapter 复用，不要求重写。
- 仓库项目与 `global.json` 已迁到 C# / `.NET 10 LTS`，并锁定 EF Core 10、官方 OpenAI .NET SDK 和 MAF adapter；项目根目录 `.dotnet/` 提供 `10.0.400`，统一通过 `./Scripts/dotnet` 执行构建和测试。
- PostgreSQL 是生产业务真源；SQLite、Redis、Qdrant 和旧导入路径不能形成第二真源。Redis/Qdrant 只能保存可重建派生数据。
- 前端使用 React、TypeScript、Vite，已有 REST 与可重放 SSE 基础能力。

## 已确认决策

### 领域、运行时和交互

- 会话是唯一创作意图入口。Conversation Agent 只能形成结构化 Decision、Proposal、Goal Revision 建议或 WorkflowActionRequest。
- Conversation Runtime 与 Production Workflow 是两个独立运行时，共享合同、Context、Model Gateway、KnowledgeRetrievalPort 和 Event Envelope，但不共享执行控制权。
- Workflow 是合同确认、生产控制、人工验收和异常恢复界面；未经 Workflow 确认不得启动 Production。
- Goal 管理创作合同生命周期，Production 管理执行生命周期；Application 通过显式命令协调二者。
- Production 使用类型化状态机和持久 Typed DAG。LLM 不得决定事务提交、状态迁移、租约、重试、Task claim 或 Canon 写入。
- 同一项目可有多个 Proposal/Candidate，但同一时刻只能有一个 Production 持有有效 Canon 写入租约。
- Goal 修改形成不可变 Revision；人工正文修改形成受保护 Canon Revision，并触发影响分析。
- Production 执行上下文冻结 Goal、风格、模型、协议、Canon baseline 和 Knowledge snapshot；Canon 只在批次 Checkpoint 处演进。

### Conversation Agent Loop

- 会话分为 `ConversationSession -> AgentRun -> ModelTurn`：Session 是长期连续性边界，AgentRun 是一次运行，ModelTurn 是一次模型调用及其完整工具批次。
- 新用户消息使用 Pi 式 steering，在完整 ModelTurn 边界注入；显式停止才取消当前 AgentRun。
- Agent 保留开放式 ReAct loop，但 Production handoff 是硬边界；`request_start_production` 必须快速返回 `accepted + productionId`，不能同步等待 DAG。
- 每个 AgentRun 冻结权威 Context Checkpoint；崩溃恢复只从完整 ModelTurn 边界继续，不恢复半截 Token 流或半执行工具。
- 只有用户明确表达形成合同的意图时才创建 Proposal；普通聊天和 Agent 提示不能自动持久化 Proposal。
- MVP 不启用无新用户输入的通用 followUp，也不因 Workflow Event 自动唤醒隐藏 Agent。
- 长任务使用同一 ConversationSession 关联多个短 AgentRun 和一个或多个 Production；Workflow UI 是长任务主界面，持久 Workflow Event 通过 `sessionId + productionId + correlationId` 连接回会话。
- 用户查看关联卡片或发送消息时，才创建新的 AgentRun，并读取只读 `WorkflowEventContext`；不需要新开会话。

### 工具系统

- 原始工具协议稳定保留为 `read/write/search/sql/http`，供后续上下文、记忆、知识库、RAG 和领域工具组合使用；模型当前只看到 CapabilityPack 选出的必要工具。
- 原始工具必须通过虚拟资源、用户作用域、风险策略和 Operations 端口执行，不能等同于任意文件、任意 SQL 或任意网络调用。
- `sql` 原始工具永远只读，只能查询受控 PostgreSQL view/read model，并受 RLS、超时、行数和成本限制。
- `write` 可写临时会话记忆、RawSource、ResearchArtifact、DraftArtifact、KnowledgeCandidate 和 staging；不能直接修改 Goal、Production、KernelTask、Canon 或已确认权威知识。
- 记忆、知识和 RAG 的 CRUD 通过 Application 拥有的类型化命令/仓储完成，不能用 SQL DML 或直接 Qdrant 写入绕过所有权。
- 临时会话记忆和草案可以按策略自动保存；推广到已确认项目记忆或权威 Knowledge，以及权威内容的修改、归档和删除，必须用户确认并记录来源、版本和操作者。
- Workflow 只以异步、粗粒度 capability 暴露给 Conversation Agent：`preview/start/status/pause/resume/cancel/accept/revise`。DAG 节点、Worker 执行器、租约和 Canon merge 不注册为普通 Conversation 工具。
- 工具调用统一经过 `prepare -> validate -> before(policy) -> execute -> after(audit/redaction/render)` 五步管道；错误统一编码为结构化 ToolResult。

### 知识库、外部研究和 RAG

- 知识库支持用户上传、受控外部 HTTP 检索和 Agent 发起的外部研究。
- 所有来源先进入带 URL/文件引用、时间、内容哈希和抓取证据的 `RawSource/ResearchArtifact`。
- 原始资料经解析、清洗、总结和引用提取形成 `KnowledgeCandidate`；推广为权威 Knowledge 必须用户确认。
- 权威 Knowledge、来源、版本、推广状态和提炼结果保存在 PostgreSQL；embedding、全文索引、rerank 和 Qdrant 是可重建派生数据。
- Draft Knowledge 可以被 Conversation Agent 检索，但必须明确标记为 Draft；Production 默认只能使用已推广且符合适用范围的知识。
- Conversation Agent 与 Production Workflow 使用同一带版本、来源和引用的 `KnowledgeRetrievalPort`。
- 权威知识和项目记忆同时支持用户确认后的归档和物理删除；删除不能由原始工具、SQL 或向量索引直接完成。

### 技术与部署

- 后端使用 C# / `.NET 10 LTS` 模块化单体；新建独立 Domain、Contracts、Application、Infrastructure 项目。
- Domain 和 Contracts 不引用旧 Web、旧 Runtime、EF Core、具体数据库或 Provider SDK；Application 只依赖 Domain、Contracts 和抽象端口。
- Web 只能作为 Composition Root、REST/SSE Host 和 Legacy Adapter，不得直接修改新聚合或绕过 Application。
- PostgreSQL/EF Core 10 是 Goal、Production、Task、Artifact、Conversation、Knowledge 权威状态和业务事件的真源。
- MAF 可作为 `IConversationAgentRuntime` 的可替换 Adapter；LangGraph、Temporal、OpenAI Agents SDK 和其他框架只能借鉴设计，不拥有领域状态。
- Redis/Qdrant 丢失后必须可从 PostgreSQL 和持久 Artifact 重建。

## 功能需求

### FR-1 意图与合同

- 所有新创作意图必须记录为可追踪 Conversation 输入。
- 可执行结果必须显式提升为结构化 Proposal；Proposal 经 Workflow 确认后才能创建/修订 Goal。
- Goal 和 Revision 不可变、可比较，并记录来源 Session/Proposal、确认人、时间、范围和幂等键。

### FR-2 生产控制

- Production 引用确定的 Goal Revision、冻结 Context、ProductionMode 和 TaskGraphVersion。
- 每个 DAG 节点具备类型、输入、输出、依赖、状态、尝试次数、租约、预算和执行记录。
- 系统定义暂停、恢复、取消、超时、重试、人工介入、失败终止和 `OutcomeUnknown` 语义。
- `SingleChapter`、`InteractiveBatch`、`AutonomousBook` 复用同一状态机和节点协议。

### FR-3 Candidate、验收与 Canon

- 模型产物先进入 Candidate/Artifact，未经显式验收与合并命令不得进入 Canon。
- Canon merge 校验唯一租约、基线版本、连续前缀和人工保护内容；失败不得部分写入。
- 聚合、Artifact、Domain Event、StreamEvent 和 Outbox 具备可验证事务边界。

### FR-4 Conversation Runtime 与工具

- `IConversationAgentRuntime` 负责多轮讨论、原始工具调用、CapabilityPack、Proposal、handoff、steering 和 guardrail。
- Conversation Runtime 不得直接写 Production、执行 DAG、claim Task、合并 Canon 或推广权威 Knowledge。
- Chat Loop 具备 ModelTurn、工具批次、时延、预算和上下文安全阀；长篇规划、深度研究和章节生产必须异步 handoff。
- Workflow Event 在 Workflow UI 展示完整状态，Conversation 只显示摘要和关联入口；用户下一次交互创建新 AgentRun。

### FR-5 知识摄取与检索

- 支持用户上传、受控 HTTP 和 Agent 外部研究三类来源，并保留 provenance/hash。
- 支持 RawSource、ResearchArtifact、KnowledgeCandidate、推广状态、来源引用和版本历史。
- 推广、权威修改、归档和删除必须用户确认；Draft Knowledge 可被 Agent 检索但默认不得进入 Production。
- PostgreSQL 保存权威数据，RAG/向量/全文索引可重建；Conversation 和 Production 共用检索端口。

### FR-6 事件、查询与恢复

- Conversation SSE 与 Workflow SSE 分流，共享稳定 Event Envelope、事件序号、关联标识和游标重放。
- Token delta 是 transient，不参与业务恢复；业务事件必须经 StreamEvent + Outbox 持久化。
- Workflow 查询必须区分新 Goal/Production 与 `legacy_read_only` 投影。
- 旧项目恢复只读取正式内容和已确认决策，重新形成 Goal，不续跑旧 MissionPlan、RuntimeRun 或 pending tool execution。

## 验收标准

> 状态标记：`[x]` 已有满足当前标准的证据；`[~]` 仅部分满足；`[ ]` 尚无完成证据。

- [x] AC-1：架构依赖测试证明 Domain、Contracts、Application 不引用旧 Web、旧 Runtime 或 Provider SDK，Domain/Contracts 不引用 Infrastructure。
- [~] AC-2：遗留 MissionPlan、NovelAgentOrchestrator、Runtime/ToolExecution 和旧 Workflow 不能创建新 Goal、Production、KernelTask 或写 Canon。
- [~] AC-3：Conversation Agent 只能产出结构化 Decision/Proposal/WorkflowActionRequest；未经 Workflow 确认的 Proposal 无法启动 Production。
- [x] AC-4：同一 Production 状态机和 Typed DAG 通过单章、交互批次、整书三种模式合同测试。
- [ ] AC-5：完成真实“Conversation Proposal → Goal → 单批 DAG → Candidate → 人工验收 → Canon → Workflow 投影”单链路。
- [~] AC-6：每次模型调用可审计 Provider、模型、Prompt/Schema、Context hash、预算、用量、延迟、重试和 Trace，且无敏感凭据。
- [~] AC-7：未验收 Candidate 不得进入 Canon；基线冲突、租约失效和人工保护不会产生部分写入或静默覆盖。
- [~] AC-8：Goal、Production、Task、Canon 各自只有一个写入入口，Web、Conversation Runtime 和旧运行时不能绕过入口。
- [~] AC-9：聚合、Artifact、Domain Event、StreamEvent、Outbox 的事务边界经故障注入验证。
- [~] AC-10：Conversation/Workflow SSE 分流，业务事件可断线重放、去重、排序，Token delta 不参与恢复。
- [~] AC-11：迁移保留正式章节、人物、世界观、伏笔、有效知识和确认决策；旧执行状态只读归档，恢复创建新 Goal。
- [~] AC-12：替换 MAF 为测试 Runtime 时，Domain、Application、数据库模型和 Production 行为无需修改。
- [~] AC-13：清空 Redis/Qdrant 后，Goal、Production、Candidate、Canon、业务事件和 Knowledge 派生状态可从 PostgreSQL/Artifact 重建。
- [x] AC-14：架构图和分阶段计划覆盖 FR-1 至 FR-6，每阶段有输入、交付物、验证、兼容策略和进入门槛。
- [ ] AC-15：用户上传、受控 HTTP 和 Agent 外部研究均形成带 provenance/hash 的 RawSource/ResearchArtifact，经提炼与用户确认推广后进入 PostgreSQL 权威 Knowledge；Conversation 与 Production 使用同一带版本/引用的检索端口。
- [ ] AC-16：Draft Knowledge 在 Agent 检索中明确标记，默认不进入 Production；权威知识/记忆支持用户确认后的归档或物理删除，并保留审计结果。

## 当前验收证据与缺口

以下 .NET 通过数来自项目本地 `.dotnet/` SDK `10.0.400` 的重跑；前端和 diff 检查详见 `notes.md`。

- AC-1：通过。`AgentArchitecture` 依赖测试 `16/16`，warnings-as-errors build 无警告。
- AC-2：部分。Legacy archive/recovery 和写入守卫存在；本轮已迁移 Goal submission/compiler、Workflow batch transition、task-failure progression 和 manual Artifact 兼容入口，但其他遗留控制路径仍存在。
- AC-3：部分。Proposal 确认、未确认不得启动和幂等测试已覆盖；缺完整真实 Conversation runtime E2E。
- AC-4：通过合同层。同一 Domain 状态机/DAG 编译合同覆盖三种模式；缺真实 Worker 三模式证据。
- AC-5：未完成。已有 AcceptanceGate bridge 的 PostgreSQL 证据；缺真实 Worker → Candidate → 验收 → Canon → Projection 单链路。
- AC-6：部分。Provider-neutral gateway、审计字段和测试 Runtime 已有；缺真实 OpenAI/MAF 全链路证据。
- AC-7：部分。Candidate/lease/merge 合同已有；缺完整基线冲突、人工保护和故障注入证据。
- AC-8：部分。Worker 的 claim/renew/complete/fail、失败推进、dependent unblocking、AcceptanceGate bridge，以及本轮指定的 Goal/Workflow/Artifact compatibility commands 已迁至 `AgentControlDbContext` 并有聚焦测试证据；`GoalControlService` 等其他 legacy 控制路径仍直接写共享控制表，因此全局 guard 仍不能默认开启。
- AC-9：部分。Acceptance bridge 可幂等重投；缺跨服务崩溃恢复和完整 Outbox 故障注入。
- AC-10：部分。双 SSE、Envelope、去重和刷新已接线；缺过期游标稳定错误和浏览器 E2E。
- AC-11：通过合同层；缺真实数据迁移演练。
- AC-12：部分。Deterministic Runtime 替换边界已验证；缺真实 MAF 适配器全链路。
- AC-13：部分。PostgreSQL 控制面已建立；缺 Redis/Qdrant 清空重建演练。
- AC-14：通过。架构图、阶段计划和迁移门槛已同步到 `design.md`、`implement.md`、`notes.md`。
- AC-15/AC-16：设计已锁定，尚无实现证据。

任务继续保持 `in_progress`；不得用默认关闭 guard、新增双写或设计占位来宣称完成。

## 非目标

- 不重写可靠的 Canon、Knowledge、章节存储和模型配置，只通过 Adapter 复用或渐进迁移。
- 不恢复或续跑旧 ReAct 执行点、MissionPlan、RuntimeRun 或 pending tool execution。
- 第一阶段不拆微服务、不引入分布式事务、不采用全量事件溯源。
- 不让 Temporal、LangGraph、MAF、OpenAI Agents SDK 或其他第三方框架拥有 Goal、Production、Task、Knowledge 或 Canon 权威状态。
- 不把 Production DAG 节点注册为普通 Conversation 工具。
- 当前阶段不锁定全部类名、方法签名、表字段和 UI 视觉细节。
- 本次中途会话交接不扩展产品实现范围，不执行 `finish-work`、任务归档或 Git commit。

## 技术约束

- Domain/Contracts 不得依赖 Web、Runtime、EF、数据库实现或 Provider SDK。
- Application 只能依赖 Domain、Contracts 和抽象端口；Infrastructure/Web Adapter 实现外部系统。
- Web、Conversation Runtime、原始工具和旧运行时不得绕过 Application 修改 Goal、Production、Task、Knowledge 或 Canon。
- `sql` 仅查询受控只读 view/read model；记忆/知识 CRUD 只能走 Application 类型化命令。
- `write` 只能写 staging、Artifact、RawSource 或受控草案；权威推广、修改、归档、删除必须用户确认。
- `http` 必须走出站代理和资源策略，敏感凭据不进入 ToolContext 或日志。
- PostgreSQL 是唯一权威真源；Qdrant/Redis/全文索引可删除并重建。
- 所有命令和工具支持用户作用域、幂等、审计、取消和结构化错误。

## 风险

- `.NET 8` → `.NET 10`、EF/Npgsql 和 SDK/MAF 兼容性可能影响迁移、测试和部署。
- 原始 `sql/http/write` 能力过宽会绕过权限、SSRF 防护、数据所有权或权威知识推广门。
- Draft Knowledge、外部研究和 Agent 总结可能污染 RAG 或 Production 上下文，需要明确权威级别和推广审计。
- 长篇生成会增加 Context、Candidate、知识版本和索引存储成本。
- 持久 DAG、Outbox、SSE 重放、租约和多 AgentRun 之间存在竞态、重复投递、乱序和过期上下文风险。
- Worker ownership 已收敛，但旧 Workflow 写入口尚未迁完；在此之前不能默认启用 legacy write guard，AC-5/AC-8 仍受阻塞。
- MAF/OpenAI SDK 持续演进，接口泄漏会造成框架版本锁定。
- 用户于 2026-08-19 要求提交当前实现并归档任务；仍标记为 `[~]` / `[ ]` 的验收项保留为残余缺口，不因归档视为通过。

## 尚未解决的问题

以下问题不改变已锁定的产品边界，但仍需代码库证据、兼容性探针或后续设计确认：

1. `GoalControlService` pause/resume/cancel/safe-point 及其他遗留 production/recovery Artifact writer 的 Application-owned command 迁移，以及迁移完成后默认启用 legacy write guard。
2. 真实 Worker → Candidate → 双审 → 人工验收 → Canon merge → Workflow projection 的 Testcontainers/浏览器单链路证据。
3. Outbox、SSE replay、Canon merge、Task lease 和跨服务崩溃恢复的故障注入证据。
4. `AcceptPrefix` 并发幂等从 read-before-insert 转为统一 request-result 的实现证据。
5. 原始工具虚拟资源 URI、只读 SQL view 清单、write staging 资源类型、HTTP 出站策略和风险等级的具体合同。
6. Draft Knowledge 的检索范围、Production 可用的权威级别和归档/物理删除后的索引重建策略。
7. 首批 CapabilityPack 的精确工具清单，以及各 Workflow 命令的确认矩阵。
8. SSE 过期游标稳定错误、Playwright 浏览器 E2E、Redis/Qdrant 清空重建和真实数据迁移演练。
9. 最终程序集名称、MAF/SDK 版本、ConversationStore/MemoryPromotion 持久模型、SSE 保留策略和运维容量规划。
