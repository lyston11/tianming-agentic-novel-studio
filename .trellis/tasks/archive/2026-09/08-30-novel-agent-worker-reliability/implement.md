# 实施计划：Worker Execution Reliability and Recovery

## 0. Discovery

- [ ] 读取 parent、Conversation/Goal、PostgreSQL child artifacts 和 backend database/error/quality specs。
- [ ] 定位实际 Worker host、scheduler、queue abstraction、DbContext 和可用 integration test harness；仓库没有对应实现时记录 blocker。
- [ ] 研究 legacy `KernelTask`/lease/attempt/fence 语义和 EcomGen task/provider/cancel 模式，写入差异矩阵。

## 1. Contract and state

- [ ] 定义 TaskAttempt、Lease、FenceToken、ProviderRequest、ExecutionOutcome、ReconcileCommand 和 typed errors。
- [ ] 定义 Task transition reducer、retry classification、attempt budget 和 cancellation checkpoints。
- [ ] 定义 worker result command，确保 worker 不直接改 Production/Batch/Canon。

## 2. Scheduler and persistence

- [ ] 实现 atomic claim/renew/release/expiry recovery；沿 AgentControlDbContext transaction/UoW。
- [ ] 实现 TaskAttempt/provider request receipt 的持久化和唯一 key/fingerprint。
- [ ] 实现 retry enqueue/outbox contract；避免内存 queue 被误当 distributed lease。

## 3. Provider and recovery

- [ ] 注入 fake provider，覆盖 known success/failure、timeout、malformed、abort 和 response unknown。
- [ ] 实现 outcome_unknown/reconcile/unadopted late result；禁止旧 loop/checkpoint replay。
- [ ] 实现执行前后的 bounded timeout、heartbeat、cancel 和 fence checks。

## 4. Tests and handoff

- [ ] Concurrent claim、lost lease、stale fence、renew/expiry、cancel race。
- [ ] retry budget、provider fingerprint/idempotency、unknown outcome、late result。
- [ ] crash/restart simulation 和 no-Canon-side-effect tests。
- [ ] 输出给 Review child：TaskAttempt/ExecutionOutcome 查询 contract、candidate submit provenance 和 unknown/error mapping。

## 5. Validation and rollback

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-novel-agent-worker-reliability
# run the discovered host's actual dotnet/node tests, type-check, build, and PostgreSQL integration suite
```

回滚：停止新 Worker claim 和 dispatch feature flag；保留 Attempts、provider receipts 和 outcome_unknown records；通过 reconcile 处理未决执行，不直接删除或重置为 success。
