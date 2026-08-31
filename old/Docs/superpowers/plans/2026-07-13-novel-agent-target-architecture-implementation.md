# 天命小说 Agent 目标架构全量实施计划

**状态：** 2026-07-31 最终收口并通过复验

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 一次性用 PostgreSQL 权威状态、不可变 CreativeGoal、版本化任务图、专业内核、候选正史分支、长文本 RAG 和受控协作记忆替换旧通用 ReAct 小说生产路径。

**Architecture:** 导演 Agent 只生成语义承诺评估和不可变 Goal；确定性编译器、调度器、内核合同、Reducer 与 Outbox 驱动生产。PostgreSQL 保存全部权威内容，Qdrant 保存可重建向量索引，Redis 只承担 lease、SSE、短期 replay 和版本化缓存。

**Tech Stack:** .NET 8、ASP.NET Core、EF Core 8、PostgreSQL 16、Npgsql、Redis 7、Qdrant 1.18.1、React 19、TypeScript 6、Vite 8、xUnit、Testcontainers。

**Target specification:** `Docs/superpowers/specs/2026-07-12-novel-agent-target-architecture.md`

---

## 实施约束

- 每项行为变化遵循 RED -> GREEN -> REFACTOR；先看到目标测试按预期失败。
- 不修改或覆盖用户人工章节版本；所有 Agent 产物先进入候选分支。
- 不从客户端请求读取授权用 `user_id`；只使用认证主体和数据库归属关系。
- 不为兼容旧 Agent 引入双写或长期双运行；迁移窗口内一次性切换。
- 每个阶段结束都运行 `dotnet test Tests/Unit/Unit.csproj`、`dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj`、`npm test` 和 `npm run build`。

## Phase 1：PostgreSQL 权威底座

### Task 1：切换 EF Core Provider 与本地容器

**Files:**
- Modify: `Web/NovelAgentWeb/NovelAgentWeb.csproj`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Modify: `Web/NovelAgentWeb/appsettings.json`
- Modify: `Web/NovelAgentWeb/appsettings.Development.json`
- Modify: `docker-compose.yml`
- Modify: `Tests/Unit/ProgramConfigurationTests.cs`
- Modify: `Tests/NovelAgentRegression/E2E/TestWebApplicationFactory.cs`

- [x] **Step 1: 写失败测试**

断言生产项目引用 `Npgsql.EntityFrameworkCore.PostgreSQL`、Program 调用 `UseNpgsql`，且生产配置不再声明 SQLite 连接。

- [x] **Step 2: 验证 RED**

Run: `dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ProgramConfigurationTests`

Expected: FAIL，提示缺少 Npgsql provider 或仍调用 `UseSqlite`。

- [x] **Step 3: 最小实现**

使用：

```csharp
builder.Services.AddDbContext<NovelAgentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NovelAgentDb")));
```

Compose 新增 PostgreSQL 16 healthcheck、持久卷和 API 连接字符串；测试工厂显式覆盖为 SQLite 内存库，不让生产代码回退。

- [x] **Step 4: 验证 GREEN**

Run: `dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~ProgramConfigurationTests`

Expected: PASS。

### Task 2：建立目标 schema 实体与 PostgreSQL baseline

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/CreativeGoal.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/GoalRevision.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/TaskGraphVersion.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KernelTask.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KernelArtifact.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/DomainEvent.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ModelExecution.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/CanonBranch.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/CandidateChapter.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/CandidateAcceptance.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/BranchMergeRecord.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ContinuitySummary.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/CanonChange.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/GoalContextSnapshot.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ModelKernelConfiguration.cs`
- Create: `Web/NovelAgentWeb/MigrationsPostgres/20260713000000_TargetArchitectureBaseline.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`
- Test: `Tests/Unit/Data/TargetArchitectureModelTests.cs`

- [x] **Step 1: 写失败的 EF model 测试**，逐表验证复合用户作用域、并发 token、不可变版本和 active 状态唯一索引。
- [x] **Step 2: 验证 RED**：`dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~TargetArchitectureModelTests` 应因实体缺失失败。
- [x] **Step 3: 实现实体与 fluent mapping**，状态使用受控字符串或 enum converter，扩展报告使用 JSONB，正文与标识字段关系化。
- [x] **Step 4: 生成独立 PostgreSQL baseline**：`dotnet ef migrations add TargetArchitectureBaseline --project Web/NovelAgentWeb --output-dir MigrationsPostgres`。
- [x] **Step 5: 验证 GREEN**，并在空 PostgreSQL 上执行 `dotnet ef database update`。

### Task 3：用户隔离与 RLS

**Files:**
- Create: `Web/NovelAgentWeb/Data/Interceptors/UserScopeConnectionInterceptor.cs`
- Create: `Web/NovelAgentWeb/Data/UserScopeDbContextExtensions.cs`
- Create: `Web/NovelAgentWeb/MigrationsPostgres/20260713001000_EnableTenantRls.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/NovelAgentRegression/Security/PostgresTenantIsolationTests.cs`

- [x] **Step 1: 写双用户失败测试**，用户 A/B 使用相同局部资源标识时互不可见，后台 worker 未设置作用域时拒绝查询。
- [x] **Step 2: 验证 RED**，确认现有数据库路径可跨作用域读取。
- [x] **Step 3: 每个事务设置 `SET LOCAL app.current_user_id`，对用户拥有表建立并强制 RLS policy；后台系统操作使用显式审计角色和目标用户作用域。**
- [x] **Step 4: 验证 GREEN**，再运行全部授权测试。

## Phase 2：CreativeGoal、任务图与可靠调度

### Task 4：语义承诺评估与不可变 Goal

**Files:**
- Create: `Web/NovelAgentWeb/Services/Goals/CommitmentAssessment.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/CreativeGoalContracts.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/ICommitmentAssessmentService.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/CommitmentAssessmentService.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/ICreativeGoalService.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/CreativeGoalService.cs`
- Create: `Web/NovelAgentWeb/Controllers/GoalsController.cs`
- Test: `Tests/Unit/Services/Goals/CreativeGoalServiceTests.cs`

- [x] **Step 1: 写失败测试**，讨论态不创建 Goal；明确按钮授权或无歧义语义承诺才创建；客户端 userId 被忽略；Goal 提交后更新必须创建 Revision。
- [x] **Step 2: 验证 RED**。
- [x] **Step 3: 实现 `Exploring / Proposed / Committed / Revising / Cancelled` 状态和结构化 `CommitmentAssessment`，禁止关键词硬路由。**
- [x] **Step 4: Goal 创建事务冻结 canon、knowledge、quality、style、model 和 protocol versions。**
- [x] **Step 5: 验证 GREEN**。

### Task 5：确定性 Goal Compiler 与需求分析 Kernel

**Files:**
- Create: `Web/NovelAgentWeb/Services/Goals/IGoalCompiler.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/TaskGraphContracts.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/TaskGraphValidator.cs`
- Test: `Tests/Unit/Services/Goals/GoalCompilerTests.cs`

- [x] **Step 1: 写失败测试**，章节批次始终包含冻结基线、规划、上下文、写作、双审校、返工、摘要、影响分析和验收节点。
- [x] **Step 2: 写失败测试**，循环、缺失产物、越权写入、缺少审稿和不可验收图被拒绝。
- [x] **Step 3: 实现纯确定性白名单骨架，将书籍特有需求分析编译为 `AnalyzeCreativeRequirements` Kernel 任务。**
- [x] **Step 4: Revision 只使受影响子图失效并复用内容哈希相同的 Artifact。**
- [x] **Step 5: 验证 GREEN**。

### Task 6：持久任务 claim、lease 与启动恢复

**Files:**
- Create: `Web/NovelAgentWeb/Services/Goals/IKernelTaskScheduler.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/PostgresKernelTaskScheduler.cs`
- Create: `Web/NovelAgentWeb/Services/Goals/KernelTaskWorker.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Test: `Tests/NovelAgentRegression/Reliability/KernelTaskClaimTests.cs`

- [x] **Step 1: 写并发 worker 失败测试**，同一任务只能被一个 owner claim，lease 过期可恢复，启动扫描 queued/expired tasks。
- [x] **Step 2: 验证 RED**。
- [x] **Step 3: 使用 `FOR UPDATE SKIP LOCKED` + `lease_owner/locked_until/attempt` 原子领取，不再依赖进程内无界 Channel。**
- [x] **Step 4: Redis 只提供协调 lease；PostgreSQL 始终保存可恢复任务状态。**
- [x] **Step 5: 验证 GREEN**。

### Task 7：Artifact、Domain Event、Reducer 与 Outbox

**Files:**
- Create: `Web/NovelAgentWeb/Services/DomainEvents/IKernelArtifactStore.cs`
- Create: `Web/NovelAgentWeb/Services/DomainEvents/KernelArtifactStore.cs`
- Create: `Web/NovelAgentWeb/Services/DomainEvents/IDomainReducer.cs`
- Create: `Web/NovelAgentWeb/Services/DomainEvents/DomainReducer.cs`
- Create: `Web/NovelAgentWeb/Services/DomainEvents/DomainContractValidator.cs`
- Modify: `Web/NovelAgentWeb/Services/Production/ProductionOutboxDispatcher.cs`
- Test: `Tests/Unit/Services/DomainEvents/DomainReducerTests.cs`
- Test: `Tests/NovelAgentRegression/Reliability/OutboxClaimTests.cs`

- [x] **Step 1: 写失败测试**，错误 user/Goal/branch/version/idempotency 的事件不可落库；状态和 Outbox 必须同事务。
- [x] **Step 2: 写多实例 Outbox 失败测试**，同一事件不能被重复领取。
- [x] **Step 3: 实现合同校验、聚合版本 CAS、不可变 Artifact 和原子 Outbox claim。**
- [x] **Step 4: 验证 GREEN**。

## Phase 3：专业内核与天命写作核心

### Task 8：专业内核统一合同

**Files:**
- Create: `Web/NovelAgentWeb/Services/Kernels/IKernel.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/KernelContracts.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/KernelRegistry.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/SettingKernel.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/NarrativePlanningKernel.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/TianmingWritingKernel.cs`
- Test: `Tests/Unit/Services/Kernels/KernelContractTests.cs`

- [x] **Step 1: 写失败测试**，内核只能读取 `KernelExecutionContext` 并返回 Artifact/Event proposal，不能注入 DbContext 或直接修改业务表。
- [x] **Step 2: 验证 RED**。
- [x] **Step 3: 用适配器复用 `HardcoreWritingProductionKernel` 的写作能力，把检索、提交和自审从写作内核职责中移出。**
- [x] **Step 4: 验证 GREEN**。

### Task 9：独立连续性审校与审美审稿

**Files:**
- Create: `Web/NovelAgentWeb/Services/Kernels/ContinuityReviewKernel.cs`
- Create: `Web/NovelAgentWeb/Services/Kernels/LiteraryReviewKernel.cs`
- Create: `Web/NovelAgentWeb/Services/Quality/QualityContract.cs`
- Create: `Web/NovelAgentWeb/Services/Quality/PotentialViolationDetector.cs`
- Test: `Tests/Unit/Services/Quality/DualChannelReviewTests.cs`

- [x] **Step 1: 写失败测试**，规则只产生 `PotentialViolation`；语义模型基于正文证据裁决；高影响争议进入 `NeedsDecision`。
- [x] **Step 2: 写失败测试**，主观审美不能变成硬错误，维度不能用总分抵消。
- [x] **Step 3: 实现两个独立模型配置和 `Pass / PassWithSuggestions / ReworkRequired / NeedsDecision`。**
- [x] **Step 4: 验证 GREEN**。

## Phase 4：候选正史、批次与返工

### Task 10：CanonBranch 与连续前缀合并

**Files:**
- Create: `Web/NovelAgentWeb/Services/Canon/ICanonBranchService.cs`
- Create: `Web/NovelAgentWeb/Services/Canon/CanonBranchService.cs`
- Create: `Web/NovelAgentWeb/Services/Canon/PrefixMergeService.cs`
- Create: `Web/NovelAgentWeb/Controllers/CanonBranchesController.cs`
- Test: `Tests/NovelAgentRegression/Canon/CanonBranchMergeTests.cs`

- [x] **Step 1: 写失败测试**，3–5 章候选可依赖前章候选正文，候选事实不进入正式 Story Bible。
- [x] **Step 2: 写失败测试**，只能合并从第一章开始的连续已接受前缀，合并事务同时提交版本、轻量正史和 Outbox。
- [x] **Step 3: 实现分支基线、三方比较和冲突时 `NeedsDecision`。**
- [x] **Step 4: 验证 GREEN**。

### Task 11：人工版本保护与定向 ReworkIntent

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/ReworkIntent.cs`
- Create: `Web/NovelAgentWeb/Services/Rework/ReworkIntentService.cs`
- Create: `Web/NovelAgentWeb/Services/Rework/ReworkBudgetPolicy.cs`
- Test: `Tests/Unit/Services/Rework/ReworkIntentServiceTests.cs`

- [x] **Step 1: 写失败测试**，`Authorship=Human, Protected=true` 的候选版本永不被 Agent 覆盖。
- [x] **Step 2: 写失败测试**，选区或自然语言问题编译为明确 scope/preserve/mayChange/mustNotChange/acceptanceCriteria。
- [x] **Step 3: 实现局部最多 2 次、整章最多 1 次自动返工和影响传播分级。**
- [x] **Step 4: 验证 GREEN**。

### Task 12：轻量正史提取

**Files:**
- Create: `Web/NovelAgentWeb/Services/Canon/ContinuitySummaryExtractor.cs`
- Create: `Web/NovelAgentWeb/Services/Canon/CanonChangeExtractor.cs`
- Test: `Tests/Unit/Services/Canon/LightweightCanonTests.cs`

- [x] **Step 1: 写失败测试**，仅长期重要变化进入摘要/CanonChange，临时情绪、普通移动和能力冷却不强制结构化。
- [x] **Step 2: 实现带正文证据位置的少量摘要，正文始终保持最高证据优先级。**
- [x] **Step 3: 验证 GREEN**。

## Phase 5：知识自动加工与长文本 RAG

### Task 13：数据库二进制、知识版本与 StyleProfile

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeDocumentBlob.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeSection.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeChunk.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeEntry.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/StyleProfile.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeCitation.cs`
- Modify: `Web/NovelAgentWeb/Services/Knowledge/KnowledgeProcessingService.cs`
- Test: `Tests/Unit/Services/Knowledge/KnowledgeIngestionTests.cs`

- [x] **Step 1: 写失败测试**，上传二进制进入独立 BYTEA 表，自动解析、语义分段、摘要、分类、条目和抽象 StyleProfile。
- [x] **Step 2: 写失败测试**，不得生成模仿作者或原文续写模板；强影响知识生成待确认 Proposal。
- [x] **Step 3: 实现用户级全局版本与项目/章节实际使用记录；历史章节冻结旧版本。**
- [x] **Step 4: 验证 GREEN**。

### Task 14：Qdrant 多尺度索引与可重建记录

**Files:**
- Create: `Web/NovelAgentWeb/Services/Rag/MultiScaleVectorIndexer.cs`
- Create: `Web/NovelAgentWeb/Services/Rag/VectorIndexRebuilder.cs`
- Modify: `Web/NovelAgentWeb/Services/VectorStore/QdrantVectorStore.cs`
- Test: `Tests/NovelAgentRegression/VectorStore/MultiScaleIndexTests.cs`

- [x] **Step 1: 写失败测试**，document/section/chunk/entry/style 均携带完整用户、项目、分支、版本、哈希和 embedding version。
- [x] **Step 2: 写失败测试**，所有查询/删除必须带 user filter，Qdrant 清空后可从 PostgreSQL 重建。
- [x] **Step 3: 实现索引与重建。**
- [x] **Step 4: 验证 GREEN**。

### Task 15：Dense + FTS + 实体依赖融合检索

**Files:**
- Create: `Web/NovelAgentWeb/Services/Rag/QueryPlanner.cs`
- Create: `Web/NovelAgentWeb/Services/Rag/HybridRetriever.cs`
- Create: `Web/NovelAgentWeb/Services/Rag/ReciprocalRankFusion.cs`
- Create: `Web/NovelAgentWeb/Services/Rag/EvidenceBundleCompiler.cs`
- Test: `Tests/Unit/Services/Rag/HybridRetrieverTests.cs`
- Test: `Tests/NovelAgentRegression/Rag/LongRangeRecallTests.cs`

- [x] **Step 1: 写失败测试**，查询拆为设定、人物、连续性、承诺、知识、风格路由但不基于启动关键词。
- [x] **Step 2: 写失败测试**，融合 Qdrant Dense、PostgreSQL FTS、实体/依赖结果，经 RRF、轻量重排、父节点和邻窗展开生成 Evidence Bundle。
- [x] **Step 3: 实现检索；Qdrant 只返回 ID，正文从 PostgreSQL 回读并二次鉴权。**
- [x] **Step 4: 验证远距离章节召回和双用户隔离。**

## Phase 6：协作记忆、模型与运行控制

### Task 16：重新划界的四层记忆

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/AuthorMemory.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ProjectCollaborationDecision.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/SessionDialogueState.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ExperienceObservation.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ExperienceSuggestion.cs`
- Create: `Web/NovelAgentWeb/Services/Memory/CollaborationMemoryService.cs`
- Test: `Tests/Unit/Services/Memory/CollaborationMemoryServiceTests.cs`

- [x] **Step 1: 写失败测试**，作品事实不能写入协作记忆，未接受会话提案不跨会话。
- [x] **Step 2: 写失败测试**，Experience 只能建议，`Accept / Reject / TryOnce` 分别产生项目决定、抑制审计和单 Goal override。
- [x] **Step 3: 实现 Working Context 为临时只读编译结果。**
- [x] **Step 4: 验证 GREEN**。

### Task 17：逐内核模型配置与附加提示词协议

**Files:**
- Create: `Web/NovelAgentWeb/Services/Models/KernelModelConfigurationService.cs`
- Create: `Web/NovelAgentWeb/Services/Models/KernelPromptAssembler.cs`
- Modify: `Web/NovelAgentWeb/Controllers/SettingsController.cs`
- Test: `Tests/Unit/Services/Models/KernelPromptAssemblerTests.cs`

- [x] **Step 1: 写失败测试**，System preset -> Project -> Goal 覆盖顺序稳定，各内核配置独立。
- [x] **Step 2: 写失败测试**，CustomInstructions 只能追加，不能覆盖安全、职责、Schema、状态权限和质量合同。
- [x] **Step 3: 实现 Provider/Base URL/API key reference/model/temperature/output/timeout/fallback 版本化。**
- [x] **Step 4: 验证 GREEN**。

### Task 18：预算、暂停、取消与崩溃恢复

**Files:**
- Create: `Web/NovelAgentWeb/Services/Execution/GoalBudgetService.cs`
- Create: `Web/NovelAgentWeb/Services/Execution/GoalControlService.cs`
- Create: `Web/NovelAgentWeb/Services/Execution/ModelExecutionRecoveryService.cs`
- Test: `Tests/NovelAgentRegression/Reliability/GoalBudgetConcurrencyTests.cs`
- Test: `Tests/Unit/Services/Execution/GoalControlServiceTests.cs`

- [x] **Step 1: 写并发失败测试**，模型调用前原子预留最坏成本，总额永不超限。
- [x] **Step 2: 写失败测试**，安全点暂停保存 `UnadoptedArtifact`；取消提供保留、合并前缀、放弃三种策略。
- [x] **Step 3: 写失败测试**，`OutcomeUnknown` 保守计费，provider 查询后最多自动重试一次，迟到结果不得采用。
- [x] **Step 4: 实现并验证 GREEN**。

## Phase 7：API、SSE 与混合控制台

### Task 19：Goal/批次/章节工作流 API

**Files:**
- Create: `Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs`
- Modify: `Web/NovelAgentWeb/Controllers/AgentController.cs`
- Modify: `Web/NovelAgentWeb/Services/AgentRuntime/AgentRuntimeEventStreamConsumer.cs`
- Test: `Tests/NovelAgentRegression/E2E/GoalWorkflowApiTests.cs`
- Test: `Tests/Unit/Support/AgentSseEventBusTests.cs`

- [x] **Step 1: 写失败测试**，API 支持目标预览/确认/修订、批次状态、章节详情、返工、接受、前缀合并、暂停和取消。
- [x] **Step 2: 写失败测试**，SSE replay 和实时订阅在任何读取前校验当前用户归属，事件 key 含 user/goal/run。
- [x] **Step 3: 实现 API envelope、幂等键和稳定事件 ID。**
- [x] **Step 4: 验证 GREEN**。
- [x] **Step 5: Goal Worker 与工作流命令发布独立 `goal_*` 事件，按 Goal 的 `SourceSessionId` 进入用户/session 双维度 fan-out；不得写旧 runtime event 审计表。**

### Task 20：React 混合控制台

**Files:**
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/GoalConsolePage.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/hooks/useGoalWorkflow.ts`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/components/BatchChapterNav.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/components/ChapterWorkspace.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/components/GoalInspector.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/agent/components/ReworkComposer.tsx`
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts`
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx`
- Test: `Web/NovelAgentWeb.Frontend/tests/goalConsole.test.mjs`

- [x] **Step 1: 写失败测试**，三栏布局显示批次/章节、正文与审稿/引用/版本、当前内核/Goal/金额/争议。
- [x] **Step 2: 写失败测试**，选区返工、人工编辑保护、逐章验收、连续前缀合并和取消策略可操作。
- [x] **Step 3: 拆分现有 AgentPage 状态为稳定 hooks/components，事件 ID 来自服务端。**
- [x] **Step 4: 运行 `npm test`、`npm run lint`、`npm run build`，再用浏览器验证桌面和移动视口无重叠。**
- [x] **Step 5: Goal Console 使用 SSE 实时失效 React Query，30 秒轮询仅作降级；返工使用真实来源 session，不使用固定占位会话。**
- [x] **Step 6: 保留书城、知识面板、旧工作流操作台、候选卡、执行图和相关 CSS，后续按产品能力逐项接入新架构，不按静态 import 数量清理。**

## Phase 8：迁移、一次性切换与全量验收

### Task 21：SQLite staging 导入与 Qdrant 重建

**Files:**
- Create: `Web/NovelAgentWeb/Migration/SqliteToPostgresImporter.cs`
- Create: `Web/NovelAgentWeb/Migration/MigrationVerifier.cs`
- Create: `Web/NovelAgentWeb/Migration/TargetArchitectureCutoverService.cs`
- Create: `Tests/NovelAgentRegression/Migration/SqliteToPostgresMigrationTests.cs`
- Modify: `Web/NovelAgentWeb/Program.cs`
- Modify: `Docs/DEPLOYMENT.md`

- [x] **Step 1: 写失败测试**，用户、项目、章节版本、Story Bible、知识、记忆和内容哈希逐项一致。
- [x] **Step 2: 写失败测试**，WorkingMemory、MissionPlan、未确认提案和未完成 ReAct 状态不迁移；旧 run/ledger 只读保留。
- [x] **Step 3: 实现 staging 导入、核验报告、Qdrant 重建、切换前检查和整体回滚命令。**
- [x] **Step 4: 在数据副本上完成两次迁移演练并验证幂等。**

### Task 22：移除旧生产路径并完成验收

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`
- Delete or archive from compilation: `Web/NovelAgentWeb/Support/AgentPlanner.cs`
- Delete or archive from compilation: `Web/NovelAgentWeb/Support/ReflectionEngine.cs`
- Delete: `Web/NovelAgentWeb/Support/AgentRuntime.cs`
- Delete: `Web/NovelAgentWeb/Support/AgentToolRegistry*.cs`
- Modify: `Docs/ARCHITECTURE.md`
- Create: `Docs/superpowers/tests/2026-07-13-novel-agent-target-architecture-acceptance.md`

- [x] **Step 1: 写架构失败测试**，生产 DI 不得注册通用 Planner/Reflection loop，导演不得调用平级业务工具。
- [x] **Step 2: 删除旧生产路由和长期兼容开关，只保留只读审计查询。**
- [x] **Step 3: 运行后端 Unit、NovelAgentRegression、AgentKernelRegression、前端 test/lint/build。**
- [x] **Step 4: 在 OrbStack 启动 PostgreSQL、Redis、Qdrant、API 和前端，执行双用户隔离、3–5 章候选、返工、前缀合并、RAG、预算、暂停、取消和恢复 E2E。**
- [x] **Step 5: 记录实际命令、测试数量、失败注入和浏览器截图到验收报告；只有全部通过才标记全量计划完成。**

## 完成定义

- `Docs/superpowers/specs/2026-07-12-novel-agent-target-architecture.md` 的 26 节均能映射到上述任务和自动化验收。
- 生产环境不加载 SQLite provider，不运行 `SqliteSchemaNormalizer`，也不保留通用 ReAct 小说生产 loop。
- PostgreSQL 是唯一业务真源；Redis 和 Qdrant 清空可恢复。
- 双用户在数据库、向量、缓存、SSE、后台任务和文件上传内容上完全隔离。
- 用户能从对话形成可确认 Goal，查看候选章节，保护人工版本，定向返工并连续前缀合并。
- 天命写作核心通过专业内核合同消费 Context/Evidence，而不是作为平级工具被通用 Agent 随机调用。
- 迁移、回滚、金额硬上限和崩溃恢复均有可重复测试证据。

## 2026-07-31 最终收口

- [x] `GoalCompiler` 改为纯确定性编译，不再在确认请求中同步调用补图模型。
- [x] `AnalyzeCreativeRequirements` 成为持久 Kernel DAG 节点，统一进入预算、`ModelExecution`、lease、恢复与 Goal 失败传播。
- [x] Kernel 按 `TaskGraphVersion.GoalRevisionId` 投影有效 Goal 合同，冻结目标、成功标准、保留项、必发生项、禁止修改项、验收与返工策略。
- [x] 模型调用区分已知失败与结果未知；已知失败立即原子关闭执行并释放预留，结果未知保留 lease 交给恢复器。
- [x] 删除旧 `AgentRuntime`、`AgentKernel`、`AgentToolRegistry`、工具 ledger/cache/resolver 与对应源码形状测试，只保留目标架构生产路径和必要的旧审计读取。
- [x] OrbStack 双用户实测确认 Goal ownership：用户 B 查询用户 A Goal 返回 404。
- [x] 浏览器复验 `awaiting_decision`、预算显示、返工入口与桌面/移动布局；修复移动端返工区与 Inspector 的 Flex 高度循环，重叠从 195px 降为 0。
- [x] 完成代码级旧生产退役：删除旧 Runtime/Worker/Queue、工具执行 registry、重复 Goal/Canon API 和 runtime 写接口；会话历史、SSE 传输、旧审计读取与产品界面继续保留。
- [x] Goal DAG 实时链覆盖 task start/complete/fail、安全点、返工、候选变化、验收、合并及控制状态；事件按用户和真实来源会话隔离，且不回写旧 runtime 审计表。
- [x] Goal 创建验证 `SourceSessionId` 属于当前用户，返工只能复用 Goal 的权威来源会话，避免错误或伪造的会话审计关联。
- [x] 应用级健康检查覆盖 PostgreSQL、Redis、Qdrant 与 embedding；关键依赖降级统一返回 HTTP 503，匿名响应不泄露连接信息。
- [x] 最终复验：Web 0 warning/0 error，Unit 756/756，NovelAgentRegression 153/153，AgentKernelRegression 6/6，前端 7/7 与 production build 通过。
