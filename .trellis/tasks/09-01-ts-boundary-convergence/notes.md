# 实施记录：TS 边界收敛

## 裁决

### 提案生命周期：归 C#

`old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` 与 C# Workflow Application 是生产唯一权威。原因是提案确认不能只改变一个 proposal 状态：它必须和 Goal、GoalRevision、Production、Batch、Task 的创建共享 `AgentControlDbContext` 事务。TypeScript 通过 internal Application port 提交 `ProposeGoalInput` 或确认意图并读取结果；它不维护 reducer、proposal revision/transition durable 记录，也不直接创建执行生产状态。

### Continuity gate：C# 负责生产权威，TS 只做纯预检

C# `old/Services/Framework/AI/NovelAgent/Services/ProductionKernel/ChapterGatekeeper.cs` 已覆盖生产硬门禁，并由生产流程决定是否可进入后续阶段。TS `runContinuityGate` 保留为本层对候选意图的确定性、非持久化预检：章节号、冻结 context hash、角色/伏笔引用完整性。它的 Review 是提交给宿主的结果，不是第二个生产状态机；C# gate 的结果和生产状态才是接受与 Canon 流程的权威。

## 从 TS reducer 转交给 09-01-reliability-gap-check 的约束

删除 `domain/proposal-lifecycle.ts` 不得丢失这些生产约束，后续由 C# 控制面逐条核对实际测试覆盖：

- proposal content hash 必须覆盖稳定的内容字段，不覆盖状态/时间等生命周期元数据。
- expected proposal version 与 expected content hash 必须同时校验，过期命令不得写入。
- 只允许合法状态转移；已确认、已拒绝、已废弃或其他终态不可重新打开。
- `reject` 必须保存非空决策原因，`supersede/discard` 的允许来源状态必须保持。
- proposal、revision、transition 和 confirmation intent 的 actor/project/correlation/idempotency scope 必须一致。
- 同一幂等键同一内容返回原结果；同一幂等键不同内容必须冲突。
- confirm 的结果与 Goal/Revision/Production/Batch/Task 创建必须原子提交，失败不得留下部分状态。

## 11 项行为覆盖迁移说明

现有 `vertical-slice.test.ts` 仍通过测试专用 `InMemoryNovelTestStore` 模拟 Application-facing port；这些测试不把内存实现当作生产 durability 证据。

| 语义 | 当前测试覆盖 | 生产权威 |
|---|---|---|
| 幂等键重用与不同内容冲突 | `rejects proposal idempotency reuse for different content` | C# Application/数据库幂等约束 |
| 确认前不创建 Production | `does not create production before proposal confirmation` | C# Workflow/Application |
| continuity failure 在 acceptance 前阻断 | `blocks a continuity failure before acceptance` | C# ChapterGatekeeper + task/production 状态 |
| rejection 不 merge Canon | `records a rejection and blocks the production without merging Canon` | C# Acceptance/Canon command |
| stale version 与跨项目拒绝 | `rejects stale versions and cross-project access without writes` | C# actor scope/version 校验 |
| 冻结包不受可变 snapshot 影响 | `uses the frozen package...`、`reads context through...` | C# ContextPackage/read model |
| 终态候选不可重开与伪造 merge 拒绝 | `does not reopen terminal candidates...` | C# candidate/Canon application |
| malformed/model-error/abort 记失败任务 | `records malformed, model-error, and abort runs...` | C# Worker/task failure transition |
| 并发确认序列化 | `serializes concurrent confirmations...` | C# transaction/idempotency/fence |
| acceptance 幂等 | 同一测试的并发 acceptance 断言 | C# acceptance command |
| runtime event 顺序与 replay | `runs the proposal-to-canon...` 中 sink 断言 | Web/Application event/outbox relay |

## 依赖重建记录

执行时按以下顺序验证，避免下游 `file:` 依赖读取不到上游 gitignored `dist/`：

```bash
cd tianming-ai && npm install && npm run build && npm test
cd ../tianming-agent-core && npm install && npm run build && npm test
cd ../tianming-novel-agent && npm install && npm run type-check && npm test
```

## 最终验证（2026-09-01）

- `tianming-ai`: `npm run build` 通过；`npm test` 通过，3/3。
- `tianming-agent-core`: `npm run build` 通过；`npm test` 通过，10/10。
- `tianming-novel-agent`: `npm run type-check` 通过且零错误；`npm run build` 通过；`npm test` 通过，11/11。
- `python .trellis/scripts/task.py validate .trellis/tasks/09-01-ts-boundary-convergence` 通过，`implement.jsonl` 6 项、`check.jsonl` 5 项全部有效。
- `git diff --check` 通过。
- `tianming-novel-agent/src` 未发现 `proposal-lifecycle`、`mergeAcceptedCandidate` 或旧的生产 store 实现；依赖扫描未发现 Pi、Redis、Qdrant、PostgreSQL/ORM 直接依赖。
- `CharacterState`、`ForeshadowEntry`、`WorkflowProjection` 的剩余命中属于 contracts、技能输入或读取接口，不是 TS durable 写入实现。
