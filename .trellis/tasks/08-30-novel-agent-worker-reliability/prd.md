# Worker Execution Reliability and Recovery

## 1. Goal and user value

让章节生产在异步 Worker、进程崩溃、并发消费者、取消和 Provider 超时下仍然可解释、可恢复、不可重复错误写入。用户应能区分“正在执行”“可重试失败”“结果未知”和“已完成”，而不是看到一个由 HTTP 请求或模型 natural stop 推断出来的假成功。

## 2. Requirements

### R1 — TaskAttempt

每次 Task 执行必须创建不可变 Attempt 记录，至少包含：

- attemptId、taskId、goal/production/project/user scope；
- attempt number、worker identity、lease/fence reference；
- contextPackageId/hash、model profile/policy version；
- start/finish time、cancel state、error classification；
- provider request IDs、input/output hashes、cost/usage receipt（若 Provider 提供）；
- final ExecutionOutcome 和 correlation/causation。

### R2 — atomic claim and lease

Worker 只能通过 scheduler/application command claim `ready/queued` Task。claim 必须原子地产生 owner、lease expiry、fence token 和 TaskAttempt；并发 Worker 至多一个成功。lease renew/release 必须校验 owner、未过期状态和当前 fence token。

### R3 — fenced writes

Task result、Candidate、Review、failure 或 dependent unblocking 的写入必须携带当前 fence token。lease 过期、token 不匹配、Task 已取消或新 Attempt 已接管时，迟到 Worker 写入必须被拒绝且保留 rejection evidence。

### R4 — cancellation

取消必须是持久化命令，并在 claim、Provider 调用前、工具边界、长流读取、结果提交前检查。取消不能伪造成功；已完成的 artifact/message 保留，未采用结果必须进入明确的 cancelled/unadopted 状态。

### R5 — ProviderRequest boundary

所有外部模型调用必须通过可注入 Provider port，记录 canonical request fingerprint、providerRequestKey、attempt、timeout、model profile、开始/完成/错误状态和响应 hash。Novel Agent 不直接持有凭据或 provider SDK。

### R6 — retry policy

按错误类型和 Task policy 区分 retryable、terminal_failed、cancelled 和 outcome_unknown；每个 Task 有最大 attempts 和退避策略。重试必须生成新 Attempt，但关联同一 Task/lineage；不能覆盖旧 attempt 记录。

### R7 — outcome_unknown and reconciliation

当请求已经发出但本地无法确认结果时：

```text
running → outcome_unknown → reconcile
```

reconcile 优先用 providerRequestKey 查询；若找到结果，保存为 `unadopted` 或由显式 Application command 采用；若无法确认，不得自动重复调用或把任务标为成功。达到策略上限后进入 terminal_failed 或 needs_decision。

### R8 — worker/application ownership

Worker 只 claim、执行和提交 typed result command；Production/Batch/Canon 状态由 Application/Domain 更新。Worker 不直接调用 DbContext 修改多个 aggregate，不直接接受 Candidate 或写 Canon。

### R9 — bounded runtime

模型/工具执行必须有超时、AbortSignal、最大 turn/token/budget 保护和 heartbeat/lease renew 边界。Provider timeout、tool failure、malformed output 和 process crash 都必须形成可查询的 ExecutionOutcome。

### R10 — recovery safety

旧 Pi/MAF `MissionPlan`、`NovelAgentRun`、pending tool execution 和 checkpoint 不可盲目恢复为当前 Task。恢复基于 retained Canon/context snapshot 创建新的受控 Attempt 或 RecoveryGoalProposal，迟到结果只能作为 evidence。

## 3. Acceptance criteria

- [ ] A1：两个独立 Worker 并发 claim 同一 Task 时只一个获得 lease/attempt，另一个得到可识别 conflict/no-op。
- [ ] A2：lease renew、release、expiry recovery 和 fence token 校验有 integration tests；失效 Worker 迟到结果不改变 Task/Candidate/Canon。
- [ ] A3：TaskAttempt 保存 context/model/request/error/provenance，并能查询完整执行链。
- [ ] A4：取消在 claim、provider 前、工具边界和提交前均可阻断；不会把 cancelled run 伪造成 success。
- [ ] A5：retryable 错误按最大 attempts 重新排队；terminal error 不重试；每次 retry 有新的 Attempt 和关联 lineage。
- [ ] A6：ProviderRequest 使用 fingerprint/providerRequestKey；已发出但结果未知的请求进入 outcome_unknown，不会盲目二次调用。
- [ ] A7：reconcile 能记录 provider 查到的结果、unadopted late result 或 terminal decision；不能自动进入 Canon。
- [ ] A8：Worker 只调用 typed Application/Domain result command；没有 direct multi-aggregate DbContext mutation。
- [ ] A9：timeout、malformed output、tool error、abort、process restart simulation 和 lost lease 均有负向测试及结构化错误结果。
- [ ] A10：文档明确 fake provider/in-memory scheduler 只能证明合同；真实分布式安全需 PostgreSQL integration evidence。

## 4. Out of scope

- 具体 Provider SDK、凭据服务、计费结算和多供应商路由策略。
- Review/Acceptance/Canon 业务规则（由 Review/Canon child 负责），本任务只提交可供其消费的 execution result。
- Redis 队列、Kubernetes autoscaling、完整监控平台和浏览器 E2E。
- 恢复旧 Pi/MAF loop 或删除 legacy runtime。

## 5. Dependencies and constraints

- 依赖 Conversation/Goal child 的 GoalRevision/Task provenance。
- 依赖 PostgreSQL child 的 AgentControlDbContext、Task 状态、CAS 和 transaction/UoW。
- 遵守 EcomGen 可迁移的 fingerprint、attempt、cancel、timeout 模式，但不得复制其无 lease/fence/Outbox 的弱保证。
- 生产代码不得让 `tianming-agent-core` 知道 lease、Task 或 ProviderRequest。
