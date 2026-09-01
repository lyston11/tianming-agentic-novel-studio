# Tianming Novel Agent Core Vertical Slice

## 1. 目标与用户价值

为天命 AI 写作建立第一个可运行、可测试、可审计的小说垂直切片：用户通过会话表达创作意图，Agent 先提出结构化目标，应用层在用户确认后创建生产批次，冻结小说上下文，调用通用 `AgentCore` 生成一章候选正文，经确定性连续性门禁后等待人工验收；用户接受后，应用层才把候选合并为 Canon，并更新可查询的角色状态与伏笔投影。

用户得到的不是“普通聊天回复”或“直接写库的黑盒生成”，而是一条能看见提案、确认边界、候选、审查、验收和正史落库边界的小说创作工作流。该切片是后续批量生产、整书生产和旧项目恢复的最小可验证内核。

## 2. 背景与现状证据

- `AGENT_CORE_ARCHITECTURE.md:8-33` 已确定四层方向：`tianming-web → tianming-novel-agent → tianming-agent-core → tianming-ai`，依赖只能向下。
- `AGENT_CORE_ARCHITECTURE.md:51-59` 已锁定通用 Core Loop：natural stop、串行工具、TypeBox 参数校验、错误 tool result、steering/follow-up、abort、maxTurns 和稳定事件；Core 状态不得出现小说字段。
- `tianming-agent-core/src/types.ts:34-121` 与 `src/agent-core.ts:41-237` 已提供通用工具、事件、状态、prompt 和注入 `streamFn` 的合同。
- `tianming-ai/src/index.ts:1-84` 已将 `@mariozechner/pi-ai@0.57.1` 收敛为唯一模型调用边界，并提供 `streamSimple` 与工具参数校验。
- `tianming-novel-agent/README.md:1-13` 当前仍是小说领域层骨架，已列出 Skill、Role、DomainTool、ContextProvider、Hook 的职责和禁止直接导入 Pi 系列包的约束。
- `tianming-web/README.md:1-9` 将 Web 后端定位为认证、授权、durable truth、事务、Outbox、Worker 和 Read Model 权威；前端已落地，但后端新接线仍属规划。
- `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts:1-95` 展示了旧的 Pi Agent 运行时、durable message 转换和实时事件桥接，是迁移参考而非新真源。
- `old/Web/NovelAgentWeb/Program.cs` 已注册旧的 `TargetArchitectureDirector`、`IAgentContextAssembler`、`IGoalCompiler`、Pi/Structured Runtime 和 Redis 事件设施；这说明现有能力分散在 legacy Web 服务中，不能在本任务中整体搬回。
- Obsidian 设计记录《天命小说 Agent 统一上下文与生产架构整合计划》:98-155、194-259、261-294 记录了 Application 入口、ContextPackage、授权、生产状态机、Outbox 与 legacy 迁移语义；其中与当前 Core 分层冲突的固定生产链以 `AGENT_CORE_ARCHITECTURE.md:1-5` 为准，仅提炼领域语义。

## 3. 本任务目标

### 3.1 必须实现的用户可观察链路

1. 创建或读取一个属于当前用户的小说项目上下文。
2. 在 Conversation 中接收自然语言创作意图。
3. Novel Agent 输出结构化 `GoalProposal`，而不是直接创建 Goal 或写入 Canon。
4. Application-facing port 接收该提案；用户通过确认命令确认目标边界。实现可用命令/测试驱动的模拟确认，但合同必须能被 HTTP/SSE 接线消费。
5. 确认后创建最小 `Goal`、`GoalRevision`、`Production`、`Batch`、`Task` 记录/内存适配物，并生成冻结的 `NovelContextPackage`。
6. 调用 `AgentCore` 与 `chapter-writer` role，使用 deterministic fake model 生成一个 `CandidateChapter`。
7. 运行确定性的 continuity gate；通过则状态为 `awaiting_acceptance`，失败则保留可审计 Review 结果，不得自动进入 Canon。
8. 接收 acceptance command；只有人工接受才执行 Canon merge。
9. Canon merge 更新最小结构化 `CharacterState` 和 `ForeshadowEntry`，并刷新一个可查询 projection。
10. 可通过查询接口/测试 fixture 验证 Goal、Production、Batch、Candidate、Review、Acceptance、Canon 和 projection 的状态与关联版本。

### 3.2 必须保持的架构边界

- `Proposal → Goal → Production` 不属于 `tianming-agent-core` 的固定阶段；它属于 Novel Agent/Application/Production 层。
- `AgentCore` 不知道小说、章节、Canon、PostgreSQL、ASP.NET、React 或授权。
- LLM、Core Runtime 和 DomainTool 只能输出/提交 `Proposal`、`Candidate`、`Review` 或 command intent；Application/Domain/Worker 才是业务状态的权威写入者。
- DomainTool 不直接访问 PostgreSQL、EF 或 Canon；必须调用 Application port。
- Core 事件是宿主观察面，不是 durable truth；宿主负责将事件映射成持久化消息、审计记录和 SSE 事件。
- 本切片使用最小结构化领域对象，不把可变的巨大 StoryBible JSON 设为新真源。

## 4. 非目标

以下内容明确不在本任务交付范围内：

- 整书自动生成、多章节/多批次并发生产、多 Agent 协作和完整 RAG。
- 真实 LLM/provider 生产接线、模型计费、OAuth、模型发现和 prompt 优化；只实现可替换的 deterministic fake model，并保留 `tianming-ai` port。
- 完整 PostgreSQL schema/migration、Redis/Qdrant 部署、真实 Outbox relay、lease/fence worker 集群和 RLS 全量落地。
- 删除或重写 `old/`、旧 `NovelPiRuntime`、MAF/Structured Runtime、`TargetArchitectureDirector`、旧 `MissionPlan/AgentRun/AgentToolExecution` 写路径。
- 把 EcomGen 代码、依赖、数据模型或电商领域能力并入 Tianming Agent；EcomGen 仅作为独立研究对象。
- 通过启动 Redis、Docker Compose、数据库或真实 Provider 来制造完成证据。
- 建立 Playwright 浏览器 E2E 基础设施；如现有前端能提供低成本证据，可补最小 UI/API/SSE 契约测试，但不以浏览器测试替代领域/运行时测试。

## 5. 最小领域合同

### 5.1 输入与提案

`ConversationTurn` 至少包含 `conversationId`、`projectId`、`userId`、`message`、`correlationId` 和可选 `idempotencyKey`。

`GoalProposal` 至少包含：

- `proposalId`、`projectId`、`conversationId`、`createdBy`；
- `intent`（本切片为写一章候选）；
- `chapterNumber`、`chapterBrief`、`acceptanceCriteria`；
- `executionMode`（本切片默认 `interactive_batch`）；
- `requiresConfirmation: true`；
- `sourceMessageIds`、`proposalVersion`、`contentHash`。

提案可以被拒绝或修订；未被确认的提案不得创建可执行 Production。

### 5.2 生产与候选

确认命令创建：

- `Goal`：合同生命周期；
- `GoalRevision`：确认时的不可变版本；
- `Production`：执行生命周期；
- `Batch`：本次交互批次；
- `Task`：一次候选章节生成任务；
- `NovelContextPackage`：章节执行前冻结的上下文包。

`NovelContextPackage` 至少包含 `goalRevisionId`、`canonSnapshot`、`characterStates`、`foreshadowEntries`、`sourceReferences`、`versionVector`、`contentHash`、`createdAt`。执行时只读取该冻结包，不回头拼装 mutable 全量上下文。

`CandidateChapter` 至少包含 `candidateId`、`taskId`、`chapterNumber`、`title`、`body`、`contextPackageHash`、`modelProfile`、`candidateVersion` 和 `status`。

### 5.3 Review、验收与 Canon

- `Review` 至少记录 `reviewId`、`candidateId`、`gateName`、`passed`、`findings`、`reviewVersion`、`createdAt`。
- 连续性门禁必须检查本切片明确定义的最小规则，例如章节号与 Goal 一致、候选引用的角色存在、伏笔更新引用存在、正文非空且字数在测试范围内；规则失败只产生 Review/阻塞状态。
- `Acceptance` 至少记录 `acceptanceId`、`candidateId`、`actorId`、`decision`、`expectedCandidateVersion`、`idempotencyKey`、`createdAt`。
- Canon merge 是唯一把候选变为作品事实的动作；必须使用候选版本和上下文哈希进行 optimistic check。
- merge 事务内更新最小 `CharacterState`、`ForeshadowEntry` 和 projection；重复 acceptance 不得重复写入。

## 6. 成功标准

本任务完成时，团队能在不依赖真实外部服务的测试环境中，演示并断言：

- 一个自然语言会话意图先得到结构化提案；提案包含来源、版本、哈希和必须确认标记。
- 未确认提案不会创建 Production/Task，也不会改变 Canon。
- 确认后，单个 Task 使用冻结 `NovelContextPackage` 调用 `AgentCore` + `chapter-writer` + fake model，产出可追溯 Candidate。
- Core tool/event 合同保持通用；Novel Agent 不直接导入 Pi 系列包，Core 不出现小说域字段。
- continuity gate 结果可查询；失败不会绕过人工验收。
- 接受候选后，Canonical 章节、一个 CharacterState、一个 ForeshadowEntry 和 projection 可查询，且携带候选版本/上下文哈希来源。
- 同一个 acceptance idempotency key、过期 candidate version、错误 project/user scope 都得到可观察且可测试的拒绝，不产生部分写入。
- 所有 Core 事件都可映射为宿主 durable message/SSE envelope；断线重放依赖宿主记录而非内存事件监听器。
- 现有 `tianming-agent-core` 与 `tianming-ai` 测试、type-check、build 不回归；新切片测试可在无 Redis/PostgreSQL/真实 Provider 的情况下运行。

## 7. 风险与后续门槛

| 风险/门槛 | 本任务处理 | 后续要求 |
|---|---|---|
| 并发确认或重复消费 | 合同中加入 idempotency key、expected version、scope 校验；fake store 测试拒绝冲突 | PostgreSQL 唯一键、事务隔离、RLS、Outbox 原子写入 |
| Worker 重试与双写 | 只定义 Task/command 结果边界，不宣称集群安全 | lease、attempt、fence token、可恢复 worker 和死信策略 |
| 事件丢失 | 显式定义 Core event → durable message 映射与重放合同 | 真实 Outbox relay、SSE cursor、断线恢复 |
| 上下文漂移 | 冻结 package/hash/version vector，测试执行读取快照 | Canon/Knowledge/Style/Model 版本注册与审计 receipt |
| 未授权写入 | DomainTool 只提交意图，Application 做 scope/授权 | 真实身份、RLS、审计、策略矩阵 |
| fake model 与生产模型差异 | fake 只验证合同和状态链路 | 真实 provider contract test、预算/计费/超时策略 |
| legacy 语义混淆 | notes 记录选择性迁移，禁止整体搬运 | 等价回归通过后再逐步切换/删除旧 callers |

## 8. 未决但不阻塞本任务的问题

- PostgreSQL 最终表名、迁移编号和跨服务事务实现留给后续持久化任务。
- 真实 HTTP/SSE endpoint 的最终 DTO 命名可在实现时适配现有 Web API，但不能突破本 PRD 的状态和所有权边界。
- continuity gate 的完整小说规则库、知识检索召回策略和作者偏好晋升规则不在本切片；本切片只锁定最小可重复规则。
- 是否在下一个任务引入完整 `ContextReadReceipt`、`LegacyExecutionArchive` 和 `RecoveryGoalProposal`，由迁移任务单独确定。

## 9. 验收清单

- [x] `GoalProposal` 是会话输出，不是直接写入命令。
- [x] 用户确认是创建 Goal/Production/Task 的必要条件。
- [x] `NovelContextPackage` 有来源、版本、哈希并在章节任务中冻结。
- [x] `AgentCore` 只负责通用 loop，Novel Agent 通过 role/tool/adapter 注入领域能力。
- [x] fake model 生成 CandidateChapter，continuity gate 生成 Review。
- [x] Candidate 只有在明确 acceptance 后才能 merge Canon。
- [x] Canon merge 更新 CharacterState、ForeshadowEntry 和 projection，并支持幂等/版本拒绝。
- [x] Core event 到 durable message/SSE 的映射有测试。
- [x] 现有 Core/AI 回归、目标包 type-check/build/test 和 `git diff --check` 通过。
- [x] 未启动外部依赖、未删除 legacy、未改动 EcomGen。

## 10. 任务状态

本任务已获用户明确批准并完成确定性 TypeScript vertical slice 实现。`task.json.status` 在归档前保持 `in_progress`；归档命令会将任务标记为 `completed`。生产 PostgreSQL/Web/Worker 接线、Outbox/SSE relay、lease/fence/RLS、真实 provider、完整 RAG、多 Agent、整书生产和 Playwright E2E 仍按本 PRD 第 4、7 节作为后续门槛，不应被当前 fake/in-memory 证据替代。
