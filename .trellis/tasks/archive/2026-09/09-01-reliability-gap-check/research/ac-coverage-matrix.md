# AC-P01～P12 覆盖矩阵

核查日期：2026-09-01 · 核查对象：`08-30-tianming-novel-agent-durable-control-plane` 的 12 条父级 AC
判定依据：实现路径（可按行号核对）+ **能实际跑通的测试**。仅有同名符号不算已满足。

实测基线（本次核查时）：`Unit` 837/837 · `AgentArchitecture` 27/27 · `NovelAgentRegression` 159/159（真 PostgreSQL Testcontainers）· `AgentKernelRegression` 6/6。

---

## AC-P01 durable proposal — 已满足

**要求**：conversation turn、assistant result/run provenance、GoalProposal 可按 user/project scope 查询；相同 idempotency key 重放同一结果，不同 payload 被拒绝。

- 实现：`Tianming.NovelAgent.Infrastructure/Persistence/Records.cs`（`ConversationTurnRecord`、`ConversationMessageRecord`、`GoalProposalRecord`、`ConversationRuntimeCheckpointRecord`）、`EfAgentControlStore.cs`、`AgentControlDbContext.cs`
- 测试：`PostgresVerticalSliceTests.Conversation_tool_confirmation_is_persisted_and_idempotent`、`.Concurrent_confirmation_with_same_idempotency_key_returns_one_durable_result`
- 命令：`dotnet test Tests/AgentArchitecture --filter PostgresVerticalSliceTests`
- **若未实现会怎样**：同一提案被重复确认两次，产生两个 Goal，用户看到重复生产批次。

## AC-P02 proposal lifecycle — 已满足

**要求**：proposed/confirmed/rejected/superseded/discarded 生命周期可审计；Goal 引用被确认的 proposal；同项目多历史 proposal 但同时最多一个 active confirmed Goal/Production。

- 实现：`Tianming.NovelAgent.Domain/Goals/GoalProposal.cs`（状态与显式决策）、`GoalContract.cs`、`CreativeGoal.cs`（append-only revisions、frozen references、proposal→Goal provenance）
- 测试：`PostgresVerticalSliceTests.Proposal_to_canon_merge_uses_one_persisted_control_plane`
- **若未实现会怎样**：被拒绝或已被取代的提案仍能被确认，用户按旧意图拿到正文。

## AC-P03 planned/start — 已满足

**要求**：ConfirmGoal 不启动模型、不把 Production 写成 running；StartProduction 幂等并按合法迁移推进。

- 实现：`Tianming.Web/Services/Goals/BookProductionTransitionService.cs`、`Tianming.NovelAgent.Domain/Production/Production.cs`（状态机）
- 测试：`AgentToCanonE2ETests.Conversation_to_canon_single_chapter_with_one_worker_and_two_user_actions`（确认与启动是两个独立用户动作）
- **若未实现会怎样**：用户点确认即开始烧 token，无法在启动前复核目标。

## AC-P04 durable transaction — 已满足

**要求**：Goal/Revision/Production/Batch/TaskGraph/Task 相关写入在 `AgentControlDbContext` 事务内原子；失败不留半成品；迁移用独立 history table。

- 实现：`Infrastructure/Persistence/EfAgentControlStore.cs`、`EfLegacyControlPlaneCommands.cs`；迁移 `Infrastructure/Migrations/202608170001_InitializeNovelAgentControlPlane.cs`（独立 history table）
- 测试：`PostgresVerticalSliceTests.Runtime_checkpoint_rolls_back_when_authoritative_turn_persistence_fails`、`GoalBudgetConcurrencyTests.FailKnownAsync_ReleasesReservationAndClosesExecutionAtomically`
- **若未实现会怎样**：Goal 落库但 TaskGraph 未落，项目卡在无任务可执行的确认态。

## AC-P05 tenant isolation — 已满足（覆盖强于要求）

**要求**：所有 command/query 同时执行显式 user/project predicate 和 RLS；跨用户/项目访问统一 not-found/forbidden 且无写入副作用。

- 实现：`Tianming.Web/Data/UserScopeConnectionInterceptor.cs`、RLS 迁移 `MigrationsPostgres/20260822000000_AddProjectContextActivationTenantRls`
- 测试（`Tests/NovelAgentRegression/Security/PostgresTenantIsolationTests.cs`，12 个）：`AllTenantOwnedBusinessTables_EnforceRowLevelSecurity`、`CreativeGoals_AreInvisibleAcrossDatabaseUserScopes`、`ConnectionInterceptor_SetsAuthenticatedDatabaseUserScope`、`CollaborationMemory_IsInvisibleAcrossDatabaseUserScopes`、`OutboxClaimFunction_ClaimsTenantWorkWithoutLeakingPayloadAcrossScopes`、`Registration_SetsNewTenantScopeBeforeCreatingDefaultSettings`、`BackgroundClaimFunctions_RejectConnectionsWithTenantScope`、`BackgroundClaimFunctions_DenyHttpApplicationRole`、`BackgroundClaimPreflight_VerifiesWorkerIdentityAndAllFunctionGrants` 等
- **若未实现会怎样**：A 用户能读到 B 用户的 Goal 与正文。注意 `AllTenantOwnedBusinessTables_EnforceRowLevelSecurity` 是全表扫描式断言，新增租户表若漏 RLS 会被它捕获——这比 AC 要求的更强。

## AC-P06 fenced execution — 已满足（实体名不同）

**要求**：同一 Task 并发 claim 只有一个成功；lease 过期、错误 fence token、取消后迟到结果被拒绝；**attempt、provider key 和 outcome 可审计**。

- fence/lease 实现：`Infrastructure/Persistence/Records.cs`、`EfAgentControlStore.cs`、`Domain/Production/Production.cs`（`FenceToken`）；`Tianming.Web/Data/Entities/KernelTask.cs:18-24`（`LeaseOwner`、`LeaseExpiresAt`、`NextAttemptAt`、`Attempt`、`MaxAttempts`）
- 测试（`KernelTaskClaimTests`，PostgreSQL 9/9）：`ConcurrentWorkers_AtomicallyClaimDifferentTasks`、`ClaimNextAsync_RecoversExpiredRunningLease`、`RenewAsync_ExtendsOnlyTheOwnedRunningLease`、`CompleteAsync_LostLeaseRollsBackWithoutUnblockingDependents`、`FailAsync_RetriesWithBackoffThenMovesGoalToAwaitingDecision`
- **审计部分的关键更正**：08-30 PRD 把这部分称作 `TaskAttempt`，该名称全仓零命中——**但其意图已由 `ModelExecution` 完整承担**（`Tianming.Web/Data/Entities/ModelExecution.cs`）：`TaskId` + `Attempt`（哪一次尝试）+ `Provider`/`Model`/`ProviderRequestId`/`ProviderLeaseOwner`（provider key）+ `Status`/`ResultJson`/`ResultContentHash`（outcome）+ `IdempotencyKey`/`OperationKey`，另含 token/成本核算。即每 (task, attempt) 一行可审计记录。
- 测试：`GoalModelExecutionEnvelopeTests.ExecuteAsync_UsesAmbientKernelScopeAndPersistsProviderUsage`
- **若未实现会怎样**：两个 Worker 同时 claim 同一 task，双写 candidate，Canon 出现重复正文；或重试后无法追溯哪一次调用产生了计费。

## AC-P07 unknown recovery — 已满足

**要求**：Provider 已发出但结果未知时进入 `outcome_unknown`；不自动重复调用或采纳迟到结果；reconcile 后可标 `unadopted` 或进入受控重试。

- 实现：`Tianming.Web/Services/Execution/ModelExecutionRecoveryService.cs`（`ChargeOutcomeUnknownAsync` at `:111`）、`GoalBudgetService.cs`、`Services/Production/DefaultWritingModelCompletionService.cs`；迁移 `MigrationsPostgres/20260728090000_AddModelExecutionProviderLease`、`20260728080000_HardenBackgroundClaimBoundaries`、`20260714060000_AddGoalControlAndExecutionRecoveryClaims`
- 测试：`ModelExecutionRecoveryServiceTests.OutcomeUnknown_ChargesReservationAndStoresProviderResultAsUnadopted`、`.UnknownOutcome_RetriesAtMostOnceAndLateOldResultNeverAdopts`、`GoalModelExecutionEnvelopeTests.ExecuteAsync_UnknownOutcomeLeavesExecutionForLeaseRecovery`、`.ExecuteAsync_KnownProviderFailureClosesExecutionAndReleasesReservation`
- **若未实现会怎样**：进程在 provider 返回前退出后自动重发同一请求，用户被重复计费且可能拿到两份正文。
- 备注：这与 EcomGen 独立收敛到的同一条不变量一致（外部请求前写标记、中断后转不可自动重试）。

## AC-P08 review and rework — 已满足

**要求**：hard gate 失败阻断 Acceptance；soft review 记 findings；Candidate 不可变，EditorialDraft/Rework 产生新 lineage/version，受预算与影响传播约束。

- 实现：`Services/ProductionKernel/ChapterGatekeeper.cs`、`ChapterRewriter.cs`（`tianming-web/backend/Services/Framework/AI/NovelAgent/Services/ProductionKernel/`）、`Tianming.Web/Services/Rework/`、`Services/Goals/ReworkGraphCompiler`
- 测试：`AgentToCanonE2ETests`（含定向返工链路）、`AgentKernelRegression` 的 `Tianming kernel preserves protected human chapters`、`Tianming kernel requires a frozen chapter context`
- **若未实现会怎样**：门禁未过的正文被合并进 Canon，或返工覆盖掉人工修改过的章节。

## AC-P09 acceptance and Canon — 已满足

**要求**：只有有权限的持久化 accepted Acceptance 才能合并 Canon；candidate version / context hash / Canon version vector 不匹配返回 conflict；Canon、merge record、domain event、Outbox 原子提交。

- 实现：`Tianming.Web/Services/Canon/PrefixMergeService.cs`、`CanonBranchService.cs`、`Infrastructure/Persistence/OutboxCanonMergePort.cs`
- 测试：`PostgresVerticalSliceTests.Proposal_to_canon_merge_uses_one_persisted_control_plane`、`AgentToCanonE2ETests`（人工 Acceptance → Merge → Canon 版本增加且正文可查询）；`TargetArchitecturePurityTests` 断言 `CanonBranchService` 只经 `CreateArtifactAsync` 而非 `new KernelArtifact`
- **若未实现会怎样**：未经用户验收的候选自动进入正史，或 Canon 版本与 merge 记录不一致导致无法回滚。

## AC-P10 projection and stream — 已满足

**要求**：WorkflowProjection 从 durable facts 派生；SSE 用稳定 envelope、stream identity、sequence、cursor replay；重复事件不重复推进状态。

- 实现：`Tianming.Web/Services/Goals/ProductionOutboxHostedService.cs`、`GoalProgressEventPublisher.cs`、`Services/Production/ProductionChainProjectionService.cs`；迁移 `Migrations/20260625033000_AddOutboxProcessingLease`
- 测试：`AgentToCanonApiSseE2ETests.Api_driven_agent_to_canon_flow_notifies_via_sse_and_keeps_read_model_authoritative`（托管 Outbox + SSE 仅通知）；`TargetArchitecturePurityTests.GoalProgressPublisher_WritesDurableOutboxWithoutDirectLiveDelivery`、`.Sse_UsesOwnedSessionAndAuthorizationHeaderWithoutQueryToken`
- **若未实现会怎样**：SSE 断线重连后重复事件把已完成的批次又推回运行态。

## AC-P11 frontend truth — 已满足

**要求**：前端只提交 REST command、读 projection/query、处理 SSE 刷新通知；不从 SSE payload 直接设置 Goal/Production/Canon 状态。

- 实现：`tianming-web/frontend/src/api/client.ts`（SSE 工厂含 cursor 续传）、`src/lib/runtime-events.ts`（894 行事件归一化）、`.trellis/spec/frontend/state-management.md`（成文契约：streamKind/streamId/sequence 校验、transient 不推进业务状态、统一 invalidate）
- 测试：前端 `client.test.ts`、`auth-store.test.ts`、`stream-retry.test.ts`（17/17）
- **若未实现会怎样**：SSE 里一个过期事件让 UI 显示错误的生产状态，用户据此做出错误的验收决定。
- 备注：此项 tianming 强于 EcomGen（后者 `packages/core/src/events.ts` 仅 19 行，无 cursor replay）。

## AC-P12 boundary regression — 已满足

**要求**：Core/AI/Novel Agent 现有测试不回归；Novel Agent 不直接导入 Pi/DB/Redis/Qdrant；`old/` 与 EcomGen 不被修改；每个 child 有 negative-path 与 integration evidence。

- 测试：`tianming-ai` 3/3、`tianming-agent-core` 10/10、`tianming-novel-agent` 11/11（type-check 干净，见 `09-01-ts-boundary-convergence`）；`ArchitectureDependencyTests`（Domain/Contracts/Application 无 EF/Agents/OpenAI/Web 依赖）；`ConversationRuntimeReplacementTests.Agent_control_projects_do_not_declare_the_agent_framework_package`
- **若未实现会怎样**：领域层偷偷依赖 EF，导致无法在无数据库环境下测试领域规则。
- 备注：`old/` 内容已于 `08-31-promote-control-plane` 整体提升到 `tianming-web/backend/`，该 AC 中"不修改 old/"的表述已随布局变化失效，等价约束是"不修改冻结区剩余内容"（旧前端、PiRuntime、Docs）。

---

## 结论

**12/12 已满足。零真缺口。**

08-30 那批五子任务计划建设的能力，在 `08-31-promote-control-plane` 提升后的 `tianming-web/backend/` 里全部已实现且有真库测试覆盖。

唯一需要更正的是我在本卡 PRD §2.2 写的判断——"`TaskAttempt` 是唯一确认的真缺口"。该名称确实零命中，但 AC-P06 要求的语义（每次尝试的 provider key 与 outcome 可审计）由 `ModelExecution` 完整承担，只是实体命名不同。**这是命名差异，不是能力缺失。**

因此 §3.3 的"只补真缺口"无需执行：没有满足"有明确失败场景 + 不新增表 + 有定向回归"三条件的缺口。

## 五个子任务的处置

| 子任务 | 结论 | 依据 |
|---|---|---|
| `08-30-novel-agent-conversation-goal-control` | 归档，已达标 | AC-P01/P02，`GoalProposal.cs` + `PostgresVerticalSliceTests` 幂等/并发确认测试 |
| `08-30-novel-agent-postgres-control-plane` | 归档，已达标 | AC-P04/P05，`AgentControlDbContext` + 独立 history table 迁移 + 12 个 RLS 测试 |
| `08-30-novel-agent-worker-reliability` | 归档，已达标 | AC-P06/P07，`KernelTaskClaimTests` 9/9 + `ModelExecutionRecoveryServiceTests` |
| `08-30-novel-agent-review-canon-outbox` | 归档，已达标 | AC-P08/P09/P10，`AgentToCanonE2ETests` + `PrefixMergeService` + Outbox 托管服务 |
| `08-30-novel-agent-web-sse-acceptance` | 归档，已达标 | AC-P10/P11，`AgentToCanonApiSseE2ETests` + 前端 SSE 三流契约 |
| `08-30-tianming-novel-agent-durable-control-plane`（父） | 归档 | 12/12 已满足 |
| `08-30-tianming-novel-agent-core-vertical-slice` | 归档 | 已完成的可行性研究；其 TS 越界部分已由 `09-01-ts-boundary-convergence` 收敛 |

保留这些 PRD 的正文——它们的领域语义（提案生命周期、fence token、冻结上下文、验收边界）是有价值的需求记录，且本矩阵逐条引用了它们。归档只改 `task.json` 状态，不删内容。

## 仍然待办（不在本卡范围，也不属 12 条 AC）

- Playwright 浏览器 E2E：`AGENT_CORE_ARCHITECTURE.md` 的 AC-15 阶段 3，仓库无该基础设施，08-18 审计已记为独立门槛。
- `09-01-retire-legacy-runtimes` 批次 B 记录的三项退役前置条件（`TargetArchitectureDirector` 归属裁决、`StructuredConversationAgentRuntime` 兜底决定、`wwwroot` 旧前端托管处置、PiRuntime adapter 接线）。
- `08-31-api-contract-codegen` 记录的 response DTO 标注前置条件（加 `[Required]` / 改 `ActionResult<T>`），完成后 `types.ts` 才可换成生成类型。
