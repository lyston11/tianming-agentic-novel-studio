# 实施记录：08-30 可靠性缺口核查

## 执行

本任务无代码变更，只产出证据文档。

### 1. 构建 AC-P01～P12 覆盖矩阵

逐条核对 `08-30-tianming-novel-agent-durable-control-plane` 的 12 个父级 AC，每条判定必须附：

- 实现路径（可按文件名行号核对）
- **能实际跑通的测试**（名称 + filter 命令）
- "若未实现会怎样"的失败场景

文档：`research/ac-coverage-matrix.md`

验证命令示例：
```bash
./tianming-web/backend/Scripts/dotnet test \
  tianming-web/backend/Tests/Unit/Unit.csproj \
  --filter "FullyQualifiedName~ModelExecutionRecoveryServiceTests"
# Passed 5/5

./tianming-web/backend/Scripts/dotnet test \
  tianming-web/backend/Tests/NovelAgentRegression/NovelAgentRegression.csproj \
  --filter "FullyQualifiedName~KernelTaskClaimTests"
# Passed 9/9 (PostgreSQL Testcontainers)
```

所有引用的测试均实际运行并通过。

### 2. 关键发现与更正

**AC-P06 的命名差异**：PRD 称实体为 `TaskAttempt`，该名称全仓零命中——我在 PRD §2.2 判定这是"唯一确认的真缺口"。**该判定是错的。**

AC-P06 要求的语义（每次尝试的 provider key 与 outcome 可审计）由 `ModelExecution` 完整承担（`Tianming.Web/Data/Entities/ModelExecution.cs`）：

- `TaskId` + `Attempt`（哪一次尝试）
- `Provider` / `Model` / `ProviderRequestId` / `ProviderLeaseOwner`（provider key）
- `Status` / `ResultJson` / `ResultContentHash`（outcome）
- `IdempotencyKey` / `OperationKey`、token / 成本核算

即每 (task, attempt) 一行可审计记录，有测试 `GoalModelExecutionEnvelopeTests.ExecuteAsync_UsesAmbientKernelScopeAndPersistsProviderUsage`。这是**命名差异，不是能力缺失**。

### 3. 结论

**12/12 已满足。零真缺口。**

08-30 那批五子任务计划建设的能力，在 `08-31-promote-control-plane` 提升后的 `tianming-web/backend/` 里全部已实现且有真库测试覆盖：

| AC | 核心机制 | 关键测试（均通过） |
|---|---|---|
| P01 | `GoalProposalRecord`、幂等 key | `PostgresVerticalSliceTests.Concurrent_confirmation_with_same_idempotency_key_returns_one_durable_result` |
| P02 | `GoalProposal.cs` 状态机、append-only revisions | `PostgresVerticalSliceTests.Proposal_to_canon_merge_uses_one_persisted_control_plane` |
| P03 | `BookProductionTransitionService`、独立确认与启动 | `AgentToCanonE2ETests.Conversation_to_canon_single_chapter_with_one_worker_and_two_user_actions` |
| P04 | `AgentControlDbContext` 原子事务、独立 history table | `PostgresVerticalSliceTests.Runtime_checkpoint_rolls_back_when_authoritative_turn_persistence_fails` |
| P05 | `UserScopeConnectionInterceptor`、RLS | `PostgresTenantIsolationTests`（12/12，全表扫描式断言） |
| P06 | `FenceToken`、`LeaseExpiresAt`、`ModelExecution` 审计 | `KernelTaskClaimTests`（9/9 真库）、`GoalModelExecutionEnvelopeTests` |
| P07 | `ModelExecutionRecoveryService`、`outcome_unknown` | `ModelExecutionRecoveryServiceTests.OutcomeUnknown_ChargesReservationAndStoresProviderResultAsUnadopted` |
| P08 | `ChapterGatekeeper`、不可变 Candidate、返工 lineage | `AgentToCanonE2ETests`、`AgentKernelRegression` 冻结上下文测试 |
| P09 | `PrefixMergeService`、Canon 原子提交 | `PostgresVerticalSliceTests.Proposal_to_canon_merge_uses_one_persisted_control_plane` |
| P10 | `ProductionOutboxHostedService`、SSE cursor replay | `AgentToCanonApiSseE2ETests`、`TargetArchitecturePurityTests` Outbox 守卫 |
| P11 | 前端 SSE 三流契约、不从 payload 直接设状态 | `client.test.ts`、`stream-retry.test.ts`（17/17） |
| P12 | 架构依赖守卫、TS 边界收敛 | `ArchitectureDependencyTests`、`ConversationRuntimeReplacementTests` package guard、`tianming-novel-agent` 11/11 |

因此 PRD §3.3 的"只补真缺口"无需执行——没有满足"有明确失败场景 + 不新增表 + 有定向回归"三条件的缺口。

### 4. 七个任务的处置建议

| 任务 | 处置 | 理由 |
|---|---|---|
| `08-30-novel-agent-conversation-goal-control` | 归档，保留 PRD | AC-P01/P02 已达标，但其领域语义（提案生命周期、显式决策）是有价值的需求记录 |
| `08-30-novel-agent-postgres-control-plane` | 归档，保留 PRD | AC-P04/P05 已达标，12 个 RLS 测试覆盖强于 AC 要求 |
| `08-30-novel-agent-worker-reliability` | 归档，保留 PRD | AC-P06/P07 已达标，其 fence/lease/unknown 语义可作并发设计参考 |
| `08-30-novel-agent-review-canon-outbox` | 归档，保留 PRD | AC-P08/P09/P10 已达标 |
| `08-30-novel-agent-web-sse-acceptance` | 归档，保留 PRD | AC-P10/P11 已达标 |
| `08-30-tianming-novel-agent-durable-control-plane`（父） | 归档，保留 PRD | 12/12 已满足 |
| `08-30-tianming-novel-agent-core-vertical-slice` | 归档，保留 PRD | 已完成的可行性研究，TS 越界部分已由 `09-01-ts-boundary-convergence` 收敛 |

**归档只改 `task.json` 状态为 `completed`，不删除 PRD/design/implement 内容**——它们记录的领域概念（冻结上下文、验收边界、Outbox 契约）仍是有价值的架构知识。

### 5. 仍然待办（不在本卡范围，也不属 12 条 AC）

以下事项不属于可靠性缺口，因此不在本卡覆盖范围：

- **Playwright 浏览器 E2E**：`AGENT_CORE_ARCHITECTURE.md` AC-15 阶段 3，仓库无该基础设施，08-18 审计已记为独立门槛。
- **批次 B 退役前置条件**（`09-01-retire-legacy-runtimes` 记录）：`TargetArchitectureDirector` 归属裁决、`StructuredConversationAgentRuntime` 兜底决定、`wwwroot` 旧前端托管处置、PiRuntime adapter 接线。
- **API contract codegen 前置条件**（`08-31-api-contract-codegen` 记录）：response DTO 标注（加 `[Required]` / 改 `ActionResult<T>`），完成后 `types.ts` 才可换成生成类型。

## AC 达成证据

- **AC-1**：research/ac-coverage-matrix.md 逐条记录实现路径与测试
- **AC-2**：每条判定引用的测试均实际运行通过（示例见 §1 验证命令）
- **AC-3**：矩阵结论 12/12 + 七个任务归档建议（§4）
- **AC-4**：§2 明确纠正 PRD §2.2 的判定（`TaskAttempt` 非缺口）
- **AC-5**：§5 列出三项不属本卡范围的待办
