# TS 边界收敛：durable 职责归 C#，修复 type-check 与双真源

## 1. 目标与用户价值

`tianming-novel-agent` 现在越过了权威文档给它划的边界：它在 TypeScript 里实现了 Canon merge、角色/伏笔账本投影和提案生命周期状态机——这些按 `AGENT_CORE_ARCHITECTURE.md` §2 属于 ASP.NET 的 durable truth 职责。结果是**同一套提案生命周期在 C# 和 TS 各有一份实现**，而 `npm run type-check` 当前有 5 个错误。

本任务把它收敛回文档定义的五项职责，消除双真源，修复 type-check。用户价值是防止一类最难查的 bug：两份状态机语义漂移后，UI 显示的提案状态和数据库里的不一致。

## 2. 背景与实测证据

### 2.1 权威文档划的边界

`AGENT_CORE_ARCHITECTURE.md` §2：ASP.NET 是「认证、授权、Session/Conversation durable truth、领域事务、Outbox、Worker 和 Read Model 的唯一权威」。

`tianming-novel-agent/README.md` 列的五项职责：`Skill`、`Role`、`DomainTool`（**通过 Application port 提交意图，不直接操作数据库**）、`ContextProvider`、`Hook`。

### 2.2 实际越界的部分

`tianming-novel-agent/src/` 现状（2419 行，commit `e1f2fdab`）：

| 文件 | 行数 | 职责判定 |
|---|---|---|
| `contracts.ts` | 678 | ✅ 领域合同，保留 |
| `store/in-memory-store.ts` | 469 | ❌ Canon merge / CharacterState / ForeshadowEntry / projection = durable truth |
| `application/novel-agent-application.ts` | 216 | ❌ 编排 + 事务边界语义 = Application 职责 |
| `domain/proposal-lifecycle.ts` | 215 | ⚠️ 纯 reducer，但与 C# 重复（见 §2.3） |
| `runtime/fake-model.ts` | 185 | ✅ 测试替身，保留 |
| `ports.ts` | 160 | ✅ port 定义，保留（需收窄） |
| `tools/domain-tools.ts` | 154 | ✅ DomainTool，保留 |
| `context/context-provider.ts` | 104 | ✅ ContextProvider，保留 |
| `runtime/event-mapper.ts` | 62 | ✅ Core event → durable message 映射，保留 |
| `domain/continuity-gate.ts` | 61 | ⚠️ 确定性门禁规则，见 §4 |
| `roles/chapter-writer.ts` | 38 | ✅ Role，保留 |
| `skills/chapter-writing.ts` | 51 | ✅ Skill，保留 |

### 2.3 双真源

`tianming-novel-agent/src/domain/proposal-lifecycle.ts` 导出完整的纯状态转移 reducer：`createProposedProposal`、`reviseProposal`、`decideProposal`、`confirmProposal`、`transition`、`assertExpectedProposal`。

`old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` 已有同一套语义（Draft/Proposed/Confirmed/Rejected/Superseded/Discarded + 显式决策）。

**两份实现描述同一个状态机。** 这正是本轮架构工作要消除的问题。

### 2.4 当前 type-check 失败（5 个错误）

codex agent 在实现 08-30 child-1 时被中断，合同扩展了但实现端没跟上：

```
src/store/in-memory-store.ts(27,14)  TS2420  InMemoryNovelStore 未实现 NovelApplicationPorts
                                             的 propose/revise/reject/discard 等 9 个方法
src/store/in-memory-store.ts(441,43) TS2345  WorkflowProjection 缺 proposals
src/store/in-memory-store.ts(452,5)  TS2741  同上
src/tools/domain-tools.ts(111,53)    TS2345  GoalProposal 缺 status/revisionId/updatedAt
test/vertical-slice.test.ts(40,56)   TS2345  同 27,14
```

`npm test` 仍 **11/11 通过**——tsx 运行期擦除类型，只有 type-check 拦得住。两个 worktree 一致，**无绿版本可回退**。

### 2.5 已实测的基线

`tianming-ai` 3/3、`tianming-agent-core` 10/10、`tianming-novel-agent` 11/11（测试通过但 type-check 失败）。

链路依赖：`tianming-ai` install+build → `tianming-agent-core` install+build → `tianming-novel-agent` install（上游 `main` 指向 gitignored 的 `dist/`）。

## 3. 必须先做的裁决

**提案生命周期归哪一侧？** 本任务第一步是产出书面结论，不是直接改码。

倾向 **归 C#**，理由：`confirmProposal` 的输出必须与 Goal/GoalRevision/Production/Batch/Task 的创建在**同一个数据库事务边界**内落地，而那个事务在 C#（`AgentControlDbContext`）。纯函数的质量高低不影响这个约束——跨语言无法共享事务。

若裁决归 C#，则：
- 删除 `domain/proposal-lifecycle.ts`；
- `tools/domain-tools.ts` 的 `propose_goal` 退回纯 adapter，经 internal API 提交意图并接收结果；
- `ports.ts` 中 9 个生命周期方法收窄为「提交意图 + 读取结果」两类。

若裁决归 TS，必须同时说明它如何与 C# 的 Goal 确认事务保持单一权威，并**先修改 `AGENT_CORE_ARCHITECTURE.md` §2**——不允许代码与权威文档不一致地并存。

## 4. 范围内

- 产出 §3 的裁决并记入 `notes.md` 与 `AGENT_CORE_ARCHITECTURE.md`（如需）。
- 按裁决处置 `domain/proposal-lifecycle.ts`。
- `store/in-memory-store.ts`：Canon merge / 账本 / projection 的 durable 职责移除。保留一个**仅供测试**的最小 store 替身，且必须重命名并加注释说明它不是生产路径（例如 `test/fixtures/` 下）。
- `application/novel-agent-application.ts`：收敛为「组装 Core + Role + Tools + ContextProvider 并运行一次 run」的薄编排，不持有状态机与事务语义。
- `domain/continuity-gate.ts`：确定性门禁属于**领域规则**而非 durable truth。本任务只需明确它的最终归属并写入文档；若与 C# 的 `ChapterGatekeeper`（`old/Services/.../ProductionKernel/`）重复，按 §3 同一原则裁决。
- 修复 5 个 type-check 错误，`npm run type-check` 必须干净通过。
- 保持 `npm test` 通过；测试需要调整时，**保留原有 11 个测试覆盖的行为语义**（幂等重用、stale version、跨项目拒绝、并发确认序列化、abort/模型错误记为失败任务、冻结包不受可变快照影响、终态候选不可重开、伪造 Canon merge 拒绝）。

## 5. 明确不做

- 不在本任务实现 C# 侧的新能力。若裁决归 C# 且 C# 缺某个语义，记录为 `09-01-reliability-gap-check` 的输入。
- 不改 `tianming-agent-core` 与 `tianming-ai`（Core 不得引入小说字段）。
- 不改 `old/` 下任何 C# 代码。
- 不引入真实 model provider、PostgreSQL、Redis、Qdrant 或 HTTP 服务端。
- 不删 `contracts.ts` 里的领域合同——它是本层的正当职责，也是后续 internal API DTO 的参照。
- 不碰 `EcomGen/`、`.zcode/`。

## 6. 依赖与顺序

- **与 `08-31-promote-control-plane` 无强依赖**，可并行。但若裁决归 C#，internal API 的落点在迁移后的 `tianming-web/backend/`，实际接线应排在迁移之后；本任务可先完成裁决 + 删除 + type-check 修复，把接线留给后续。
- `09-01-reliability-gap-check` 消费本任务的裁决结论。

## 7. 验收标准

- [ ] **AC-1 裁决成文**：提案生命周期与 continuity gate 的归属有书面结论、理由和影响面，记入 `notes.md`；若归 TS 则 `AGENT_CORE_ARCHITECTURE.md` §2 同步修改。
- [ ] **AC-2 无双真源**：`proposal-lifecycle.ts` 与 `old/Agent/.../GoalProposal.cs` 不再同时描述同一状态机。可通过「删除其一」或「明确一方为唯一真源、另一方为其派生视图且有生成/校验机制」满足。
- [ ] **AC-3 durable 职责移除**：`tianming-novel-agent/src/` 下不再有 Canon merge、CharacterState/ForeshadowEntry 持久化更新或 WorkflowProjection 生成的生产路径代码。测试替身必须位于明确的测试目录并注明。
- [ ] **AC-4 type-check 干净**：`npm run type-check` 零错误。
- [ ] **AC-5 行为不回归**：`npm test` 通过，且 §4 列出的 11 项行为语义仍有测试覆盖。若某项因职责搬迁不再适用，须逐条说明它在哪一侧被覆盖。
- [ ] **AC-6 边界不变式**：`tianming-novel-agent` 不直接 import pi 系列包、不引入数据库/Redis/Qdrant 客户端；`tianming-agent-core` 与 `tianming-ai` 测试不回归（3/3、10/10）。
- [ ] **AC-7 依赖链可重建**：从干净 checkout 按 `tianming-ai` → `tianming-agent-core` → `tianming-novel-agent` 顺序 install+build 后全部测试通过；步骤写入 README。
- [ ] **AC-8 收口**：`task.py validate` 与 `git diff --check` 通过。

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 裁决归 C# 后 TS 侧测试大量失效，看似"降低了覆盖" | AC-5 要求逐条说明每项语义在哪一侧被覆盖，不允许静默丢弃 |
| 删掉 `proposal-lifecycle.ts` 丢失其中已想清楚的边界条件（expectedVersion/hash 校验、终态不可确认） | 删除前把这些条件作为需求条目写入 `notes.md`，交给 `09-01-reliability-gap-check` 核对 C# 侧是否已覆盖 |
| `npm install` 镜像源产出空的 `@types/node` 空壳，导致 `TS2688` 误判 | 已知陷阱：需 `--force` 显式重装该包；见 `08-31-promote-control-plane/notes.md` §6.2 |
| 上游 `dist/` 是 gitignored，下游解析失败被误判为代码错误 | 严格按 AC-7 的顺序 install+build |

## 9. 为什么现在做

这是唯一一张**修复已知坏状态**的卡：`main` 上 `tianming-novel-agent` 的 type-check 是红的（commit `e1f2fdab` 的 message 已标 `KNOWN BROKEN`）。而且它拦住了 08-30 child-1 的任何续做——不裁决双真源就继续补 TS 实现，是注定要丢弃的工作。
