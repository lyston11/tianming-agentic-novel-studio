# `old/` 目录全景拆解：历史、代码、设计思想、目标与重要任务

> 审计日期：2026-08-25  
> 审计性质：只读仓库考古与架构导航  
> 结论先行：`old/` 不是一个单一版本的“旧代码”，而是 2026-06 至 2026-08-22 之间多个架构阶段叠加后的完整历史快照。它保留了从“单体 Web + 文件/SQLite + 通用 Agent Loop”逐步演进到“PostgreSQL 控制面 + 任务图 + 专业内核 + Node Pi Runtime + Agent Core-first”的几乎全部证据。

## 1. 一页结论

### 1.1 它是什么

`old/` 是 2026-08-22 `4e379a24` 重构提交中通过 `git mv` 整体隔离的 legacy 区。该提交明确说明：Web、Services、Tests、Agent、Scripts、Docs、solution/config/env/tool cache 均移入 `old/`；根目录只保留四部分新架构、Pi 参考快照、`old/` 和工作流基础设施。因而 `old/` 的首要价值是**历史实现与迁移参考**，不是当前生产代码入口。

### 1.2 最重要的判断

1. **文档“已完成”不等于当前仍是权威设计。** `old/Docs/ARCHITECTURE.md` 和 `old/Docs/superpowers/tests/...acceptance.md` 记录的是 2026-07-31 的目标架构验收；`old/Docs/superpowers/specs/...` 已明确降级为历史整合计划。当前权威是根目录 `AGENT_CORE_ARCHITECTURE.md`。
2. **`old/Agent` 是后期收敛出的新控制面骨架。** 它已经把 Domain、Application、Contracts、Infrastructure 与 Pi Runtime 分开，并且有 PostgreSQL vertical-slice 测试；但其生产 caller 已被冻结，Pi Runtime 仍直接依赖 Pi Agent。
3. **`old/Services` 是最早且最丰富的小说业务内核。** 它包含 Story Bible、卷弧、创意知识库、伏笔/角色账本、写作内核、复盘和改写等大量领域能力，也包含较多历史模型与接口层次。
4. **`old/Web` 是集成壳和主要历史生产实现。** 它承担 ASP.NET API、EF schema/migrations、认证、多租户、RAG、SSE、Worker、Goal/Production 控制面、旧运行时兼容与前端静态发布。
5. **核心产品目标一直稳定：** 不是一次性生成一段文字，而是让长篇小说创作成为可追踪、可恢复、可审计、可人工验收的生产过程；变化主要在 Agent 如何驱动该过程。
6. **最大的历史教训是边界收敛。** 早期将许多能力集中到 `AgentRuntime`/`NovelAgentOrchestrator` 和 Web 层；后期才拆出 Application ports、不可变 Goal、持久任务、Artifact/Event/Reducer、候选分支、RLS 和独立 Node runtime；最终又将通用 Loop 下沉为 root `tianming-agent-core`。

## 2. 目录地图

| 路径 | 内容 | 架构意义 | 证据状态 |
|---|---|---|---|
| `old/Agent/` | `Tianming.NovelAgent.Domain`、`Application`、`Contracts`、`Infrastructure`、`PiRuntime` | 后期 Agent 控制面与独立 Node runtime | 后期实现/迁移目标，已冻结 legacy |
| `old/Services/Framework/AI/NovelAgent/` | Novel Agent 模型、Orchestrator、Story Bible、写作与专业内核 | 小说领域能力与历史通用编排 | 大量实际实现，部分不再是当前生产路径 |
| `old/Services/Modules/ProjectData/` | 项目数据、章节/摘要/事实/向量/Guide context 接口与实现 | 传统数据与 RAG/上下文支撑 | 历史业务支撑层 |
| `old/Services/Modules/VersionTracking/` | 版本追踪相关模型 | 内容版本与追踪 | 历史支撑 |
| `old/Web/NovelAgentWeb/` | ASP.NET API、数据、迁移、服务、Worker、控制器 | 集成边界、持久化与运行平台 | 后期完整 Web 实现，但现已 legacy |
| `old/Web/NovelAgentWeb.Frontend/` | React/TypeScript 工作台、Agent/Workflow/Materials 等页面 | 用户交互与状态展示 | 历史前端，已随 Web 一并隔离 |
| `old/Tests/` | 单元、回归、架构、RLS、Postgres vertical slice、Agent kernel 测试 | 历史行为和边界的最重要证据 | 测试代码；不应自动推断全部现行 |
| `old/Docs/` | 架构、部署、任务、研究、验收、质量材料 | 决策与演进记录 | 必须按日期/状态解读 |
| `old/Scripts/`、`old/deploy/` | dotnet wrapper、导入脚本、部署和 PostgreSQL 角色初始化 | 运维/迁移辅助 | 历史工具 |
| `old/【角色定义+创作规范】组合提示词/` | 多题材 `BizPrompt-*.json` 与 `Spec-*.json` | 产品题材知识和提示词资源 | 可复用资源，但不等同于 Agent runtime 合同 |
| `old/天命PPT/` | 产品介绍和差异说明 PPT | 产品定位/宣传材料 | 背景材料，不是代码事实 |
| `old/.claude/` | Trellis/Agent 辅助开发配置、hook、skill | 历史研发流程 | 工具配置，不是业务架构 |
| `old/.tmp/`、`old/backups/`、`old/.dotnet/`、`old/Web/.../publish`、DB 文件 | 探针、补丁、备份、SDK/构建/运行产物 | 不应作为架构依据 | 噪声/私有或生成内容 |

### 2.1 规模与整洁度观察

审计到的目录包含约 30,879 个文件、6,718 个目录；但该数量被 `node_modules`、本地 .NET SDK、发布目录、数据库、备份和测试构建产物显著放大。按顶层磁盘占用，`old/Tests` 约 3.8G、`old/Web` 约 2.5G，不能用磁盘规模判断业务复杂度。版本库顶层追踪量约为：Docs 742、Web 660、Services 222、Tests 210、Agent 55、题材提示词 50。

**结构性结论：** 业务代码本身已经出现 `Web/Services`、`Services/Framework`、`Services/Modules`、`Agent/*` 多套边界，正是后来进行 core-first 重构的背景；不过这些目录现在作为历史快照集中保存，继续原地整理会破坏考古价值，不建议在本任务中清理。

## 3. 历史时间线

### 阶段 A：通用 Agent、恢复、记忆与项目路由（2026-06-08）

Git 最早一组提交集中在 failure recovery、三层 memory、用户 intent/project router、Agent loop phase inference 和 tool filtering。例如 `feat(recovery)`、`feat(memory)`、`feat(router)`、`feat(runtime)`、`feat: complete intelligent recovery and three-tier memory refactoring`。这一阶段的核心假设是：Agent Runtime 维护较多状态，通过阶段推断、上下文构建、工具筛选和恢复链推进任务。

对应历史材料：`old/Docs/功能清单/小说Agent工程化TODO.md`、`old/Docs/tasks/multi-user-workspace-isolation.md`。

### 阶段 B：Story Bible、WorkspaceFactory 与多用户基础（2026-06-09）

随后建立 Story Constitution、Volume Arc、Character、Foreshadow、World Setting、Agent Run 等 Story Bible 实体，并实现 WorkspaceFactory 的引用计数/LRU/并发访问与 StoryBibleRepository。`old/PHASE1_COMPLETION_REPORT.md` 宣称 Phase 1 的核心代码和 39 个测试完成，但同时诚实记录旧测试文件编译问题，说明“报告完成”与“全仓库干净”并不等价。

### 阶段 C：SQLite 内容真源、知识库、Memory、RAG 与 Web 工作台（2026-06-09—06-15）

提交线快速扩张到 Qdrant user-level collections、知识库 integration、Redis、unified memory、chat history summary、内容文档真源、章节/素材/知识从文件系统迁入 SQLite ContentDocument。此阶段的重要推动力是可靠性和多用户隔离：避免用户之间共享 workspace/session/project；将文件式状态逐步变成数据库记录。

关键证据：`old/Docs/agent/multi-tenant-isolation-implementation.md`、`old/Docs/tasks/multi-user-workspace-isolation.md`、`old/Scripts/migrate_json_to_sqlite.py`、Web migrations 中 `AddContentDocumentTables`、`MigrateChapterToContentDocument`、`MigrateMaterialToContentDocument`。

### 阶段 D：知识库与生产内核融合、账本与 Agent-first UI（2026-06-18—06-25）

`old/IMPLEMENTATION_SUMMARY.md` 与 TODO 文档记录了密集的产品化工作：知识自动分类、ProjectDesignRules、ChapterBlueprint 版本链、生产包注入和门禁；并将卷弧、伏笔 ledger、角色 ledger、创意候选、商业节奏、RAG 相似正文、复盘和 Rewrite Loop 串起来。前端切成 Agent 对话、素材参考、创作工作流等连续界面。

这一阶段的设计思想是“创作链路而非工具菜单”，但历史代码数量和能力编排也开始显著变大。

### 阶段 E：目标架构整合与 PostgreSQL 控制面（2026-07-12—07-31）

`old/Docs/superpowers/specs/2026-07-12-novel-agent-target-architecture.md` 和对应实施计划把目标重置为：PostgreSQL 唯一业务真源、Qdrant 可重建索引、Redis 协调、不可变 CreativeGoal、版本化 Task Graph、专业 Kernels、Candidate/Canon 分离、RLS、Outbox、预算和恢复。`old/Docs/superpowers/plans/...` 将其拆成 8 个 phase、22 个 task；验收报告（2026-07-31）称目标架构通过 756 Unit + 153 NovelAgentRegression 级别的覆盖，并明确旧通用 ReAct/AgentRuntime 路径已经删除，而不是 Skip 掉测试。

这里要特别注意：这些“通过”是历史验收记录，不能替代当前根目录新架构的验收。

### 阶段 F：Goal/Production、Conversation、Pi Runtime 与控制面迁移（2026-08-01—08-22）

Git 提交显示后续继续完成 book production state machine、unified Agent production architecture、canonical goal workflow projection、unbound conversation/Pi runtime/Agent-to-Canon closure、legacy writers migration 和 read-only guard groundwork。此时 `old/Agent` 中出现清晰的 Domain/Application/Contracts/Infrastructure 分层，以及 `old/Agent/Tianming.NovelAgent.PiRuntime` 的 HTTP/JSON runtime。

### 阶段 G：Core-first 隔离（2026-08-22）

`4e379a24` 完成仓库重构：将旧系统整体移到 `old/`，把新 `tianming-ai` 与 `tianming-agent-core` 放到根目录，并把 `AGENT_CORE_ARCHITECTURE.md` 提升为当前权威。根目录架构明确：Web → Novel Agent Runtime → Agent Core → AI，Core 只负责通用 loop，不承载小说业务；旧 PiRuntime 是第一个未来迁移目标。

## 4. 产品目标与设计思想

### 4.1 产品目标

`old/开源说明.md` 将产品定义为面向长篇小说创作的 AI Agent 工作台，重点是组织并追踪：创作目标、章节计划、人物/世界设定、知识资料、连续性事实、审稿结果和返工任务。长篇一致性和可恢复生产比“一次生成一段文字”更重要。

目标能力包括：

- 从自然语言意图产生可确认的 Creative Goal；
- Story Bible、卷弧、人物状态、伏笔和连续性维护；
- 知识上传、结构化解析、混合 RAG 和引用追踪；
- Candidate 分支、人工 Acceptance、连续前缀 Merge 到 Canon；
- 失败恢复、预算、暂停/恢复/取消、幂等、审计和实时进度；
- 多用户 ownership + 数据库 RLS。

### 4.2 真源与派生物

后期目标架构形成清晰的所有权边界：

- PostgreSQL：用户、项目、正文、知识、记忆、Goal、Task、Artifact、Event、模型执行和审稿的业务真源；
- Qdrant：向量、哈希和定位 payload，可删除后重建；
- Redis：cache、lease、SSE replay、fan-out 等短期运行状态；
- `App_Data`/本地文件：仅密钥、配置、导入导出或模型资源，不保存隐式业务真源。

这是对早期文件路径、单一 JSON sessions、SQLite/文件混用的收敛。

### 4.3 Agent 设计思想的演进

| 旧阶段 | Agent 角色 | 主要问题/教训 | 后期修正 |
|---|---|---|---|
| 早期 | AgentRuntime + 阶段推断 + 多工具 | 运行时持有过多领域编排和状态 | 按 Application/Domain/Runtime 拆分 |
| 中期 | `NovelAgentOrchestrator` 统一协调故事地基、卷弧、章节、审稿、账本、改写 | 业务能力集中、文件约 2064 行、职责跨度大 | 专业内核/Goal/TaskGraph/Reducer 拆解 |
| 目标架构 | Director + fixed production DAG + 专业内核 | 可审计、可恢复、成本可控，但不是通用 Loop | 后续权威 Core-first 去掉固定业务阶段链 |
| 当前目标 | Generic Core natural stop + domain tools/context/hooks | 通用 loop 与小说业务解耦 | Novel Agent Runtime 通过 HTTP/JSON 接入 ASP.NET |

根目录 `AGENT_CORE_ARCHITECTURE.md` 明确：当前 Agent Core 在 assistant 无 tool call 且无 steering/follow-up 时 natural stop；工具串行执行；支持 steering/follow-up/abort/maxTurns；不把 Proposal→Goal→Production 固定链塞进 Core。

## 5. 代码分层与关键模块

### 5.1 `old/Agent`：后期控制面

#### Domain

`old/Agent/Tianming.NovelAgent.Domain/` 只有少量但关键的纯领域对象：

- `Goals/GoalContract.cs`：冻结目标合同；
- `Goals/GoalProposal.cs`：Proposal 生命周期；
- `Goals/CreativeGoal.cs`：Goal 状态；
- `Production/TaskGraph.cs`、`KernelTask.cs`、`Production.cs`：生产图、任务与生产聚合；
- `Events/AgentDomainEvent.cs`：领域事件。

这体现了后期“领域对象表达状态约束，基础设施负责持久化”的方向。

#### Contracts

`ConversationContracts.cs`、`WorkflowContracts.cs`、`ModelContracts.cs`、`AgentEventEnvelope.cs` 定义对话、工作流、模型和流事件边界，避免 Web DTO 直接成为 Domain 合同。

#### Application

`ConversationApplicationService.cs` 是对话入口。它先校验 user/session/idempotency/content，读取服务端 Session binding，幂等命中时返回既有结果；运行时只接收服务端构造的 binding；保存用户消息、assistant 消息、proposal 和事件；只有精确的 `confirm_creative_goal` tool call 才进入确认副作用路径。Unbound 对话即便模型提出 Proposal，也会降级为 DiscussOnly，防止绑定前产生项目副作用。

`WorkflowApplicationService.cs` 负责 Proposal 确认和 Goal/Production/Graph/Task 初始化；`ProductionApplicationService.cs` 负责启动、验收、AcceptPrefix、Canon merge；`ProjectContextApplicationService.cs` 负责项目上下文激活；`AgentToolRegistry.cs` 负责窄工具执行边界。`Ports/AgentPorts.cs` 把 runtime、store、event writer、UoW、Goal/Production repository 等隔离成接口。

#### Infrastructure

`AgentControlDbContext.cs` 以 PostgreSQL 为目标，包含 Conversation、Proposal、Goal、Revision、Production、Batch、CanonBranch、TaskGraph、KernelTask、Artifact、ModelExecution、DomainEvent、StreamEvent、Outbox、Canon lease 等表映射；配置并发 token、唯一幂等索引、JSONB 与 12 位历史 migration ID 兼容处理。

`EfAgentControlStore.cs`、`EfLegacyControlPlaneCommands.cs`、`OutboxCanonMergePort.cs` 和 `Conversation/EfMafSessionCheckpointStore.cs` 是 Application ports 的数据库适配器。

#### Pi Runtime

`old/Agent/Tianming.NovelAgent.PiRuntime/` 是 Node >=22 的 HTTP/JSON runtime，依赖 `@mariozechner/pi-agent-core@0.57.1`、`pi-ai@0.57.1` 和 TypeBox。`src/runtime.ts`：读取持久消息、构建系统 prompt、创建工具、订阅 token/tool 事件、调用 `agent.prompt`、序列化消息/checkpoint。它设计为“模型 loop 在 Node，ASP.NET 仍掌握权限、事务与持久化”。但它仍是 legacy caller，当前新 Core 尚未接入它。

### 5.2 `old/Services`：小说业务内核

#### Story Bible 与创作规划

`StoryBibleService.cs`、`GenreDirectionPlanner.cs`、`BookConceptDesigner.cs`、`VolumeArcPlanner.cs`、`ChapterNoveltyPlanner.cs` 把灵感变成故事宪法、整书概念、卷弧、章节候选和简报。

#### 账本与一致性

`CanonMaintenanceService.cs`、`ForeshadowLedgerService.cs`、`CharacterLedgerService.cs` 追踪 Canon、伏笔、角色目标/秘密/关系/能力代价/心理状态。`StoryStateSnapshotService.cs` 为章节规划聚合事实快照、摘要、活跃冲突、未回收伏笔、角色压力、长距离召回和相似正文。

#### 写作、门禁与改写

`HardcoreWritingEngine.cs` 与 `Services/ProductionKernel/` 下的 `ChapterPackageBuilder`、`ChapterPromptBuilder`、`HardcoreWritingProductionKernel`、`ChapterGatekeeper`、`ChapterRewriter` 形成“上下文包 → 生成 → 校验/门禁 → 返工/记录”的写作能力。根据 `IMPLEMENTATION_SUMMARY.md`，ProjectDesignRules/ChapterBlueprint 还会被注入章节 context package，并由 gatekeeper 检查 MustSatisfy/Forbidden/MustMention。

#### 传统 ProjectData 模块

`old/Services/Modules/ProjectData/` 提供章节 catalog、summary、fact archive、GuideContext、vector index、content chunk search、validation 等接口和实现。它是早期/中期历史能力的主要聚集地，也是 Web 服务和 NovelAgent 内核之间的支撑层。

### 5.3 `old/Web/NovelAgentWeb`：应用壳、数据与运行平台

#### 控制器/接口

控制器包含 Auth、Project、Workspace、Chapters、Materials、Knowledge、StoryBible、Creative、Workflow、Goal/Runtime、ProjectContext、PiRuntime internal API 等。`old/Docs/API-架构分析.md` 记录过 `/api/workspace` 与 `/api/workflow/workspace` 命名重叠，后续决定由 `/api/workspace` 提供概览、`/api/workflow/project/{id}` 提供项目工作流详情；这是 API 边界治理的一个具体案例。

#### 数据层

Web 同时保留 SQLite `NovelAgentDbContext`、PostgreSQL `PostgresNovelAgentDbContext`、`AgentControlDbContext` 相关迁移/导入路径。后期部署文档明确 SQLite 只允许作为一次性 staging 来源，PostgreSQL 才是生产真源；因此看到两套 migrations 时必须结合用途判断，不能简单认为是双生产库。

#### Worker 与可靠性

- `KernelTaskWorker.cs`：PostgreSQL task claim + lease heartbeat + user scope + executor + progress SSE；
- `BookProductionWorker.cs`：full-auto 模式下按 acceptance gate 推进批次；
- `KnowledgeProcessingWorker.cs`：知识处理异步任务；
- `ModelExecutionRecoveryWorker.cs`：模型执行恢复；
- `ProductionOutboxHostedService.cs`：Outbox 投影/投递。

`Program.cs` 注册 PostgreSQL context、JWT、Redis、内存/分布式 cache、Agent runtime、Goal compiler/scheduler/budget/control、专业 Kernels、知识/RAG、多个 hosted worker 与 SSE/事件组件，显示它是历史系统的组合根。

### 5.4 前端

`old/Web/NovelAgentWeb.Frontend/` 有 AgentPage、MaterialsPage、WorkflowPage、LibraryPage、Settings 等页面及 Goal Inspector、Candidate Cards、Execution Graph、Rework Composer、事件流/重连 hooks。历史设计强调“Agent-first 三页”：对话、素材参考、创作工作流，而不是把能力拆成后台菜单。`IMPLEMENTATION_SUMMARY.md` 记录 workflow tab 与 runtime tool log 分离。

## 6. 关键业务链路

### 6.1 历史中期链路：固定 Agent 创作闭环

```text
对话/AgentRuntime
  -> NovelAgentOrchestrator
  -> Story foundation / Volume arc / Chapter brief
  -> Candidate selection
  -> Hardcore writing engine
  -> ChapterGatekeeper / UnifiedValidation
  -> RewriteLoop
  -> Chapter fact snapshot / vector index
  -> Story Bible / ledger maintenance
```

这条链路适合解释 `old/Services` 的大量业务代码，但不是当前 Core 的合同。

### 6.2 后期 Goal/Production 链路

```text
用户对话
  -> CommitmentAssessment
  -> Exploring / Proposed / Committed / Revising / Cancelled
  -> GoalProposal
  -> 用户确认或精确 confirm_creative_goal tool
  -> CreativeGoal + immutable contract/baselines
  -> GoalCompiler
  -> Versioned TaskGraph
  -> KernelTask claim/lease
  -> Professional Kernel
  -> KernelArtifact + DomainEvent
  -> ContractValidator + DomainReducer
  -> PostgreSQL transaction + Outbox
  -> Candidate/Acceptance
  -> Prefix Merge
  -> Canon
```

根据 `old/Tests/AgentArchitecture/PostgresVerticalSliceTests.cs`，历史垂直切片真实验证了：对话 Proposal 幂等、确认幂等、10 个初始 task、Goal/Batch/Branch/Context snapshot 绑定、Start、Acceptance、AcceptPrefix 幂等、Canon merge 重复处理、ModelExecution 幂等/unknown outcome 和预算账本。

### 6.3 当前新架构链路

当前 root `AGENT_CORE_ARCHITECTURE.md` 把上述业务链拆成：

```text
Web（认证/授权/PG durable truth/HTTP+SSE）
  -> Novel Agent Runtime（skills/resources/domain tools/context）
  -> Tianming Agent Core（通用 loop）
  -> Tianming AI（model port）
```

Core 只知道消息、工具、事件、abort、steering、follow-up、natural stop 和 maxTurns；Proposal、Goal、Production、Canon 都由上层 Novel Agent/Web 实现。该差异是阅读 old 文档时最容易误判的地方。

## 7. 数据、并发、授权与可靠性思想

### 7.1 多租户隔离

历史问题文档明确记载过：Singleton `AgentRuntime`/workspace/session JSON 会让用户 A 看到用户 B 数据。修正路线包括 WorkspaceFactory 按 `(user, project)` 缓存、数据库 sessions、所有查询按 authenticated user ownership 过滤，以及后期 PostgreSQL RLS 强制隔离。

`old/Docs/agent/multi-tenant-isolation-implementation.md` 是“问题与方案”文档，不应单独当作最终实现；应结合 `Program.cs`、`UserScopeConnectionInterceptor.cs`、RLS migrations 与测试判断状态。

### 7.2 幂等、并发与 lease

幂等键贯穿 Conversation turn、Goal/Proposal confirmation、Task、ModelExecution、Outbox、Canon merge。Worker 以数据库原子 claim/lease 解决多实例竞争；lease heartbeat 丢失时停止执行，避免失去所有权的 worker 继续提交结果。`AGENT_TOOL_DEDUPLICATION.md` 还记录了 strict/per_turn/none 工具调用去重策略，但该文档反映的是 AgentRuntime 阶段的运行治理，不能替代当前 Core 工具合同。

### 7.3 预算与未知结果

目标架构区分已知失败与 `OutcomeUnknown`：已知 provider/config 失败立即释放预留；网络中断等不确定结果保留 lease/保守计费，避免请求实际成功但系统错误释放预算。Acceptance 报告还记录了“模型请求发送前预算失败”的验证。

### 7.4 正文与 Canon 保护

正文优先于摘要、向量和 memory；候选先进入 CanonBranch，不直接污染正式 Canon；人工版本 `Authorship=Human, Protected=true` 不可被 Agent 覆盖；只允许连续已接受前缀 merge；章节版本 rollback 通过重指向已有持久版本，不能删除历史。

## 8. 重要任务盘点

### 8.1 已完成或有明确验收证据的历史任务

- Web 化、移除 WPF/WindowsDesktop：`old/Docs/功能清单/小说Agent工程化TODO.md` P0；
- Story Bible 六表、WorkspaceFactory、Repository 与并发/隔离测试：`old/PHASE1_COMPLETION_REPORT.md`；
- 内容从文件路径迁入 ContentDocument、知识版本化和 Memory 管道：Git 2026-06-13/14 提交；
- 知识分类、ProjectDesignRules、ChapterBlueprint、生产包注入、设计规则门禁：`old/IMPLEMENTATION_SUMMARY.md`；
- 长篇 Agent 目标架构 22 tasks：`old/Docs/superpowers/plans/2026-07-13...md`；
- PostgreSQL/RLS/Goal/TaskGraph/Kernel/Candidate/RAG/预算/恢复/API/SSE/浏览器验收：`old/Docs/superpowers/tests/...acceptance.md`；
- Agent Application control-plane vertical slice：`old/Tests/AgentArchitecture/PostgresVerticalSliceTests.cs`；
- Unbound Conversation、项目发现/绑定、独立 Pi runtime、Agent-to-Canon closure：Git 2026-08-20—22 提交，相关实现仍在 `old/Agent`/`old/Web`。

### 8.2 明确未完成、未闭合或仅列为后续的事项

TODO 文档中仍有 10 项未勾选，尤其值得注意：

- 候选评分条目的总项仍显示未完成；
- 写作中检测世界观缺口；
- 已确认设定触发索引更新；
- 短线文章写作保持非主线；
- 若干 P0/真实 Web 适配和增强项（需结合文档上下文判断）。

`old/IMPLEMENTATION_SUMMARY.md` 还记录了两个架构纯度测试失败（Action-scoped idempotency key、legacy system key compatibility），以及 EF ModelSnapshot 历史不一致导致手写迁移/SQLite 直接应用的技术债。这些是“报告中写完成”与“仍有维护成本”的重要反证。

### 8.3 当前真正重要的后续任务

根目录架构已把后续顺序写清楚：

1. 让新的 `tianming-agent-core` 替代 `old/Agent/Tianming.NovelAgent.PiRuntime` 中直接持有的 pi-agent-core Agent；
2. 用新 Core 做最小 Novel Agent Runtime adapter vertical slice；
3. 迁移 project discovery/binding tools；
4. 完成 API/SSE/browser 真实证据；
5. 只有在 callers 为零、回归测试、数据/迁移可读、全量回归都通过后，才删除 legacy runtime/MAF/Web turn path。

## 9. 测试、部署与可运行性

### 9.1 测试分层

- `old/Tests/Unit/`：服务/模型/架构纯度和配置级测试；
- `old/Tests/NovelAgentRegression/`：Story Bible、WorkspaceFactory、RAG、Qdrant、Web/数据隔离和生产回归；
- `old/Tests/AgentArchitecture/`：后期 Domain/Application/Infrastructure、Postgres、迁移、对话和工具架构测试；
- `old/Tests/AgentKernelRegression/`：Kernel/全栈回归入口。

测试名称本身体现了演进：从 `WorkspaceFactoryTests`、`MemoryArchitectureTests`、`VectorizationIntegrationTests`，发展到 `ConversationRuntimeReplacementTests`、`DomainStateMachineTests`、`PostgresVerticalSliceTests`。

### 9.2 部署模型

`old/Docs/DEPLOYMENT.md` 给出的后期栈：API .NET 8/历史代码实际 csproj 已切到 net10.0、PostgreSQL 16、Redis 7、Qdrant 1.18.1、React/Vite 前端。生产配置要求：JWT、独立 PostgreSQL admin/app/worker 角色、RLS、DataProtection keys、内核 provider/model/pricing。SQLite 只能做一次性 staging import；Qdrant 可重建；Redis 丢失不应丢作品。

文档和 csproj 的版本不完全一致（部署文档写 .NET 8，而 `old/Web/NovelAgentWeb/NovelAgentWeb.csproj` 和 Agent projects 当前内容写 `net10.0`），这说明阅读 old 时应优先看代码/配置当前快照，文档版本只作历史说明。

### 9.3 不应直接运行的内容

- `old/.env`：可能包含本地敏感配置；
- `old/.dotnet/`：本地 SDK 复制品；
- `old/Web/.../publish/`、`bin/obj`、`node_modules`：生成物/依赖；
- `old/Web/.../novel_agent.db`、`novelagent.db`：运行数据库，不是 schema 设计唯一证据；
- `old/backups/`、`.tmp/`：备份、补丁、探针。

## 10. 迁移决策表

| old 内容 | 建议 | 原因 |
|---|---|---|
| Domain 中 Goal/Proposal/Task/Production 不变量 | 保留概念，按需迁移 | 已验证的业务语义和状态约束 |
| Application ports、Conversation binding、幂等规则 | 保留概念/适配 | 是 Web 与 runtime 解耦的边界证据 |
| Pi Runtime HTTP/JSON 合同 | 迁移适配器，不原样复制 | API 边界有价值，直接依赖旧 pi-agent-core 必须去掉 |
| `NovelAgentOrchestrator` 全体 | 拆分后选择性迁移 | 约 2064 行且职责覆盖规划、生产、审稿、账本、改写，整体迁移会复刻上帝服务 |
| `HardcoreWritingProductionKernel` | 作为能力适配参考 | 写作/门禁经验有价值，但不能让内核直接写权威表 |
| PostgreSQL/RLS/Outbox/lease 设计 | 优先继承并重新验证 | 可靠性和隔离边界是当前产品硬约束 |
| SQLite importer/migrations | 仅保留一次性迁移知识 | 生产真源已转 PostgreSQL，禁止长期双写 |
| `Services/Modules/ProjectData` | 按领域迁移接口和测试 | 内容、章节、RAG 能力有价值；避免整体搬运历史目录层次 |
| 前端 Goal Console/Agent UI | 选择性复用交互语义 | 状态/事件展示思路有价值，当前 Web 应重新绑定新 runtime contract |
| 题材 `BizPrompt`/`Spec` | 作为资源候选 | 题材知识可复用，但不能绕过 Core/Domain tool 合同 |
| `.claude`、PPT、备份、SDK、publish、DB | 不迁移 | 非产品运行时，且可能含敏感/生成内容 |

## 11. 风险清单

### 高风险

1. **把历史目标架构误当现行架构。** 解决办法：当前 loop 以 root `AGENT_CORE_ARCHITECTURE.md` 为准，old 目标 spec 只作决策溯源。
2. **恢复 legacy 双写/双运行。** 这会重新引入一致性和控制面所有权问题；迁移必须是单向、短窗口、可验证切换。
3. **跨用户数据泄露。** old 中存在 Singleton workspace/session 的历史问题；任何迁移必须保留 authenticated user ownership、Session binding revalidation 和 RLS。
4. **让 Node runtime 或 domain tool 直接写 Web 权威表。** ASP.NET Application/Infrastructure 仍应拥有授权、事务、幂等与 Outbox。
5. **跳过 Candidate/Acceptance/Canon 边界。** 自动生成不能直接污染正式正史。

### 中风险

1. **EF migrations 双历史混用。** old 同时包含 SQLite 与 PostgreSQL migrations，且 Agent control-plane 有自定义 migration ID 兼容；必须指定 context/provider 并走 wrapper。
2. **过度迁移 Orchestrator。** 大文件并非单一可复用服务；应按业务能力拆成 tools、context、adapters 和 Application commands。
3. **RAG 事实优先级错误。** Qdrant/摘要/memory 只能定位或辅助，正文/PostgreSQL 才是证据真源。
4. **文档与代码版本漂移。** 以源码、配置、测试和 commit 为主，文档注明日期和状态。

### 低风险/维护噪声

- `.DS_Store`、备份、临时 patch、publish、node_modules、SDK 和本地 DB 会污染搜索结果；分析工具应显式排除。

## 12. 给后续开发者的阅读顺序

1. 先读根目录 `AGENT_CORE_ARCHITECTURE.md` 和 `README.md`，确定当前架构。
2. 再读本报告第 3、4、6、10 节，理解 old 的历史与迁移边界。
3. 读 `old/Docs/ARCHITECTURE.md`、`old/Docs/DEPLOYMENT.md`，了解后期 PostgreSQL/TaskGraph/Kernel 体系。
4. 读 `old/Docs/superpowers/specs/...` 与 `plans/...`，理解为什么曾经选择固定生产 DAG，以及哪些内容已被降级。
5. 读 `old/Agent/Application` 和 `old/Agent/Infrastructure`，理解控制面合同与持久化；配合 `old/Tests/AgentArchitecture/PostgresVerticalSliceTests.cs` 看真实行为。
6. 读 `old/Agent/PiRuntime/src/runtime.ts`、`contracts.ts`、`tools.ts`，理解待迁移的 Node boundary。
7. 需要小说业务时，再读 `old/Services/Framework/AI/NovelAgent` 与 `old/Services/Modules/ProjectData`。
8. 最后读 Web `Program.cs`、目标 Worker、控制器和前端；不要从 `publish`/DB/备份反推设计。

## 13. 审计边界与未做事项

本报告完成了目录级、文档级、架构入口级、关键 Application/Infrastructure/Pi/Web Worker/测试级审计；没有逐一阅读 30,879 个文件，也没有把生成物、第三方依赖和所有迁移 Designer 展开。目录规模统计来自 `find old -type f`，包含依赖、SDK 和生成物，因此仅用于说明噪声规模，不代表业务源码规模。若后续需要实现迁移，应另开任务分别做：

- old PiRuntime → 新 Core adapter；
- old Domain/Application contracts → 新 Novel Agent vertical slice；
- Web production read/write caller 清点；
- 浏览器 E2E 与真实栈验证；
- 历史文档归档/索引。

## 14. 主要证据路径索引

- 当前权威：`AGENT_CORE_ARCHITECTURE.md`
- 历史架构：`old/Docs/ARCHITECTURE.md`
- 历史部署：`old/Docs/DEPLOYMENT.md`
- 产品定位：`old/开源说明.md`
- 历史 TODO：`old/Docs/功能清单/小说Agent工程化TODO.md`
- 目标架构（已降级）：`old/Docs/superpowers/specs/2026-07-12-novel-agent-target-architecture.md`
- 实施计划：`old/Docs/superpowers/plans/2026-07-13-novel-agent-target-architecture-implementation.md`
- 历史验收：`old/Docs/superpowers/tests/2026-07-13-novel-agent-target-architecture-acceptance.md`
- Phase 1 报告：`old/PHASE1_COMPLETION_REPORT.md`
- 知识/生产融合总结：`old/IMPLEMENTATION_SUMMARY.md`
- 多用户隔离：`old/Docs/tasks/multi-user-workspace-isolation.md`、`old/Docs/agent/multi-tenant-isolation-implementation.md`
- 应用对话边界：`old/Agent/Tianming.NovelAgent.Application/Conversation/ConversationApplicationService.cs`
- 应用端口：`old/Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs`
- 控制面数据库：`old/Agent/Tianming.NovelAgent.Infrastructure/Persistence/AgentControlDbContext.cs`
- Node Runtime：`old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts`
- 任务 Worker：`old/Web/NovelAgentWeb/Services/Goals/KernelTaskWorker.cs`
- 全自动生产 Worker：`old/Web/NovelAgentWeb/Services/Goals/BookProductionWorker.cs`
- Web 组合根：`old/Web/NovelAgentWeb/Program.cs`
- 后期垂直切片：`old/Tests/AgentArchitecture/PostgresVerticalSliceTests.cs`
