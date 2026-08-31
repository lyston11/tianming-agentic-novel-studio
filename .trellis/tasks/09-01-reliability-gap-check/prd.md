# 可靠性语义缺口核查：只补真缺口，不重建控制面

## 1. 目标与用户价值

08-30 那批任务（父任务 + 五子任务，12 条父级 AC）计划在 Node 里建设一整套 durable 控制面。实测发现它们要建的能力**大部分已在 `old/` 里实现并通过真库测试**。本任务把「重建」换成「核查」：逐条核对每个声称需要的语义在现有实现中的真实状态，产出差异报告，**只补真缺口**。

用户价值是避免花几周重写已经能跑的东西，同时不放过真正缺失的部分。

## 2. 已实测的基线

### 2.1 现有实现覆盖情况（2026-08-31 / 09-01 实测）

| 08-30 要求的语义 | 现状 | 证据路径 |
|---|---|---|
| Idempotency key | ✅ 已实现 | `old/Agent/.../Persistence/`：`EfModelExecutionStore.cs`、`EfLegacyControlPlaneCommands.cs`、`OutboxCanonMergePort.cs`、`Records.cs`、`AgentControlDbContext.cs`；迁移 `202608170001_InitializeNovelAgentControlPlane.cs` |
| Fence token | ✅ 已实现 | `old/Agent/.../Persistence/Records.cs`、`EfAgentControlStore.cs`、`Domain/Production/Production.cs`；同上迁移 |
| `outcome_unknown` | ✅ 已实现 | `old/Web/.../Services/Execution/ModelExecutionRecoveryService.cs:111`（`ChargeOutcomeUnknownAsync`）、`GoalBudgetService.cs`、`Services/Production/DefaultWritingModelCompletionService.cs`；PostgreSQL 迁移 `20260714060000_AddGoalControlAndExecutionRecoveryClaims`、`20260728090000_AddModelExecutionProviderLease`、`20260728080000_HardenBackgroundClaimBoundaries` |
| Lease / claim / 心跳 | ✅ 已实现且真库验证 | `old/Web/.../Services/Goals/KernelTaskWorker.cs`；`old/Tests/NovelAgentRegression/Reliability/KernelTaskClaimTests.cs` **PostgreSQL 9/9 通过** |
| ProviderRequest | ✅ 已实现 | `old/Web/.../Data/Entities/ModelExecution.cs`、`Services/Execution/GoalModelExecutionEnvelope.cs`、`GoalBudgetService.cs`、`Services/Kernels/DefaultKernelStructuredModelClient.cs`、`Services/Production/IWritingModelCompletionService.cs`、`old/Agent/.../Persistence/Records.cs` |
| Outbox | ✅ 已实现 | `old/Web/.../Services/Goals/ProductionOutboxHostedService.cs`；`OutboxCanonMergePort.cs`；迁移 `20260625033000_AddOutboxProcessingLease` |
| 租户 RLS | ✅ 已实现 | Web PostgreSQL 迁移 `20260822000000_AddProjectContextActivationTenantRls`；`PostgresTenantIsolationTests` |
| 全链路 E2E | ✅ 已通过 | `AgentToCanonE2ETests`、`AgentToCanonApiSseE2ETests`（`NovelAgentRegression` 159/159，真 Docker + PostgreSQL Testcontainers） |
| **TaskAttempt** | ❌ **全仓零命中** | `grep -rln "TaskAttempt\|task_attempt" --include="*.cs" old/` 无结果 |

### 2.2 唯一确认的真缺口

`TaskAttempt` 在 08-30 父任务 PRD §2 和 AC-P06 中被列为 Worker 必须使用的机制（「Worker 使用 TaskAttempt、Lease、FenceToken、ProviderRequest 和 OutcomeUnknown」），但代码里不存在这个概念。

需要判断的是：现有的 lease/claim + `MaxAttempts` 重试预算（`FirstBatchTaskGraphCompiler` 的 `maxAttempts: 3`）是否已在功能上覆盖 `TaskAttempt` 想解决的问题（每次尝试独立可审计），还是确实缺少 per-attempt 记录。**这是本任务要回答的第一个问题，不是预设结论。**

### 2.3 为什么会出现这种落差

08-30 的规划把 `tianming-web` 当成空白后端来设计（父任务 PRD §7 自己写了「当前仓库的 `tianming-web` 没有新的 ASP.NET backend 实现」），但同时把 `old/` 整体视为 legacy 冻结区，因此没有清点其中已达标的能力。实际情况是 `old/Web/NovelAgentWeb` 一直在 `:5002` 上服生产流量。

08-18 架构审计的自评已经指出过同类问题：「60% 正确，40% 过度工程化」。

## 3. 范围内

### 3.1 逐条核查并产出差异报告

对 08-30 父任务的 **AC-P01 到 AC-P12** 逐条核验，每条给出四种判定之一：

- **已满足**：附证据路径 + 覆盖它的测试。
- **部分满足**：说明缺哪一部分、缺口的具体表现。
- **未满足**：说明完全缺失。
- **不适用**：说明为什么该条在当前架构下不成立（例如它假设了 Node 侧控制面）。

报告落地为 `research/ac-coverage-matrix.md`。

### 3.2 五个子任务的处置结论

对 `08-30-novel-agent-conversation-goal-control`、`-postgres-control-plane`、`-worker-reliability`、`-review-canon-outbox`、`-web-sse-acceptance` 各给出结论：整体已达标可归档 / 需保留但缩小到具体缺口 / 确实需要实施。

结论必须能直接转成对应任务的 `task.json` 状态变更或新的窄口任务。

### 3.3 只补真缺口

对判定为「未满足」或「部分满足」的条目，在本任务内补齐**当且仅当**满足全部三条：

- 缺口有明确的失败场景（能写出「输入什么 → 现在会错成什么」）；
- 修复不需要新增表或改变现有状态机语义；
- 有对应的定向回归测试。

不满足的，转为独立任务并写清依赖。

## 4. 明确不做

- 不在 Node/TypeScript 里重建任何控制面能力。
- 不新增数据库表、不改现有状态机语义、不写迁移（除 §3.3 三条件同时满足的例外，且需在 PRD 更新中显式记录）。
- 不改 08-30 那些 PRD 的正文——它们的领域语义有保留价值，本任务只改 `task.json` 状态并附核查结论。
- 不做 Playwright/浏览器 E2E（`old/` 仓库无该基础设施，08-18 审计 AC-15 已记为独立门槛）。
- 不动 `EcomGen/`、`.zcode/`。

## 5. 依赖与顺序

- **必须在 `08-31-promote-control-plane` 之后**：核查对象的路径会整体变更，提前做的报告会立即失效。
- **消费 `09-01-ts-boundary-convergence` 的裁决**：提案生命周期归 C# 还是 TS，直接决定 AC-P01/P02（durable proposal + lifecycle）该按哪一侧核查。若那张卡把 `proposal-lifecycle.ts` 删除，其中已想清楚的边界条件（expectedVersion/hash 校验、终态不可确认、superseded 不可再确认）应作为本任务的核查条目输入。
- 输出可能生成新的窄口实施任务。

## 6. 验收标准

- [ ] **AC-1 覆盖矩阵完整**：AC-P01～P12 全部 12 条有判定、证据路径和覆盖测试名。无「待确认」留白。
- [ ] **AC-2 证据可核对**：每条「已满足」判定引用的测试必须能实际运行并通过（附命令与结果）。不接受仅凭代码存在就判定已满足。
- [ ] **AC-3 TaskAttempt 有结论**：明确回答现有 lease/claim + `MaxAttempts` 是否覆盖其意图；若不覆盖，给出具体失败场景。
- [ ] **AC-4 五子任务有处置**：每个子任务有明确结论并已反映到 `task.json` 状态；被判定为已达标的必须归档而非留在 `planning` 制造假待办。
- [ ] **AC-5 真缺口已补或已转卡**：§3.3 三条件全满足的已修复且有定向回归；其余已开卡并写明依赖。
- [ ] **AC-6 回归不降**：`AgentArchitecture` 29/29、`NovelAgentRegression` 159/159、`Unit` 迁移后基线不下降。
- [ ] **AC-7 文档同步**：核查结论写入 `AGENT_CORE_ARCHITECTURE.md` 或独立报告并被 README/架构文档引用，使后续会话不再重复「以为要重建」的误判。
- [ ] **AC-8 收口**：`task.py validate` 与 `git diff --check` 通过。

## 7. 风险与对策

| 风险 | 对策 |
|---|---|
| 「代码里有这个名字」被当成「语义已实现」 | AC-2 要求每条已满足判定都附**能跑通的测试**，不接受仅有符号存在 |
| 核查变成又一轮大规模重构 | §3.3 三条件是硬门；不满足就转卡，本任务只出报告 + 小修 |
| 判定过于乐观导致真缺口被埋 | 对每条「已满足」额外要求写出「如果它其实没实现，会以什么方式出错」——写不出来说明证据不足 |
| 五子任务留在 `planning` 成为长期假待办 | AC-4 强制状态变更或归档 |
| 迁移后路径变化使报告失效 | §5 强制排在迁移之后 |

## 8. 预期产出形态

`research/ac-coverage-matrix.md` 的每行形如：

```
AC-P06 fenced execution | 已满足
  实现: old/Agent/.../Persistence/EfAgentControlStore.cs (fence token CAS)
        old/Web/.../Services/Goals/KernelTaskWorker.cs (claim + lease 心跳)
  测试: KernelTaskClaimTests (PostgreSQL 9/9) — 并发 claim 仅一个成功、lease 过期拒绝、
        错误 fence token 拒绝
  命令: dotnet test Tests/NovelAgentRegression --filter KernelTaskClaimTests
  若未实现会怎样: 两个 Worker 同时 claim 同一 task，双写 candidate，Canon 出现重复正文
```

这个形态的价值是：任何后续会话都能靠它在一分钟内判断某条语义要不要做，而不是重新读 12 条 AC 再猜。
