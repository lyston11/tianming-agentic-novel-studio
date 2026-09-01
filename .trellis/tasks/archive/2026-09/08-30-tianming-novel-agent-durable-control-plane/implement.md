# 实施计划：Tianming Novel Agent Durable Control Plane

> 父任务只负责跨子任务规划与最终集成验收；实现必须在 child task 中逐个执行。父任务保持 planning，不执行 `task.py start`。

## 0. Planning gate

- [x] 已确认 Q8–Q14 及其后续状态机、版本、Worker、Review、API 决策。
- [x] 已创建父任务和五个 child task。
- [x] 已读取 backend/frontend/shared thinking specs、现有 vertical slice、old 语义和 EcomGen 审计。
- [ ] 每个 child 的 PRD、design、implement、notes、research、implement/check manifest 完成并通过 `task.py validate`。
- [ ] 实现前由用户单独批准选定 child 的最新 planning summary，再运行 `task.py start`。

## 1. Child execution order

### 1.1 Conversation and Goal control

先实现 Conversation/Turn/assistant/run provenance、Proposal lifecycle、confirmation/rejection/revision contract 和 repository-independent tests。输出稳定 Application ports，明确多 proposal / 单 active Goal 约束。

### 1.2 PostgreSQL control plane

基于 1.1 的 contracts 实现 AgentControlDbContext、migration history、控制表、tenant RLS、unique/CAS 约束和 ConfirmGoal/StartProduction transactions。先确认真实 .NET solution、EF 版本和现有 migration，再写代码。

### 1.3 Worker reliability

基于 1.2 的 Task/transaction 实现 claim/lease/renew/release、TaskAttempt、FenceToken、cancel、retry、ProviderRequest 和 OutcomeUnknown/reconcile；使用 fake provider 做确定性故障测试。

### 1.4 Review, Canon and Outbox

实现硬门禁/软审查/evidence、EditorialDraft/Rework、Acceptance、Canon baseline CAS、Character/Foreshadow changes、merge record、Domain Event/Outbox 和 projection reducer。验证跨写上下文边界，不共享假 transaction。

### 1.5 Web/SSE/frontend

确认 Web backend 承载位置后实现 REST DTO/commands/projections、两种 SSE streams/cursor replay 和 frontend Acceptance/Rework workbench。前端只 invalidates React Query，不直接改业务状态。

## 2. Cross-task integration gates

- 依赖图和 public contract 不出现 Web → Core 反向依赖或 Novel Agent → DB/Pi 直连。
- `planned → start → running` 与 backend database spec、前端显示和 Worker claim 完全一致。
- 每条 durable mutation 都有 scope、correlation/causation、idempotency、expected version 和失败语义。
- 每个跨表写入都有 transaction/outbox 方案和 crash/retry 证据；in-memory 测试必须标记为 adapter evidence。
- Candidate/Review/Acceptance/Canon lineage 能通过 source references、content hashes、version vectors 追溯。
- SSE 断线只依赖 durable cursor；projection 查询仍是前端状态真源。
- old/EcomGen/`.zcode/` 等非本任务文件不被纳入实现提交。

## 3. Validation commands

每个 child 按自身实际包运行 lint/type-check/test/build；父任务最终至少执行：

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate <child-dir>
git diff --check
```

若触及 .NET：先找到真实 solution/project，再运行对应 `dotnet build`、目标测试和 migration/integration tests；不得凭空假设命令。若触及 frontend：按 `.trellis/spec/frontend/index.md` Quality Check 运行其现有 `test`、`typecheck`、`lint`、`build` 脚本。

最终全量回归：

```bash
(cd tianming-ai && npm test && npm run type-check && npm run build)
(cd tianming-agent-core && npm test && npm run type-check && npm run build)
(cd tianming-novel-agent && npm test && npm run type-check && npm run build)
```

## 4. Quality and review gates

- 最后一次 quality check 必须覆盖所有受影响 package/layer，而非只检查最新 child。
- 检查数据库 migration 不删除/复制 Canon、Knowledge、Chapter 或既有 control table；新 tenant table 必须有 RLS。
- 检查 legacy compatibility adapter 没有旁路 `SaveChangesAsync` 或 dual write。
- 检查 unknown provider outcome、late result、lost lease、duplicate outbox、stale version 和 cross-scope 均有可观察结果。
- 完成后调用 `trellis-update-spec` 判断是否需要把新合同提升到 `.trellis/spec/`，再按 workflow Phase 3.4 单独审阅提交计划。

## 5. Explicit non-actions

- 不在父任务下直接实现全部功能。
- 不启动 Redis、Docker、PostgreSQL 或真实 Provider 作为规划证据。
- 不删除/迁移 `old/`，不修改 EcomGen，不恢复旧 Pi/MAF loop。
- 不把 task completion、natural stop、SSE event 或 projection refresh 当作 Canon acceptance。
