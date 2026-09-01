# 技术设计：Worker Execution Reliability and Recovery

## 1. Execution state model

```text
Task planned → ready → queued → claimed → running
Task running → awaiting_user → completed
Task running → retryable → queued
Task running → terminal_failed
Task running → outcome_unknown → reconcile
Task running → cancelled
```

`TaskAttempt` 是每次技术执行的 immutable record；Task 的当前状态是可 CAS 更新的聚合状态。`ExecutionOutcome` 不能只用 boolean 表示，至少区分 success、retryable_failure、terminal_failure、cancelled、outcome_unknown、unadopted。

## 2. Claim transaction

```text
begin transaction
  select Task where status in (ready, queued)
    and user_id/project_id match
    for update skip locked
  create TaskAttempt
  create Lease(owner, expires_at, fence_token)
  update Task → claimed/running, increment version
  append TaskClaimed event/outbox
commit
```

lease/fence 所有字段属于同一 control-plane 写 owner。`fence_token` 每次接管单调递增或不可猜测；所有 result command 带 task version、attemptId 和 token。

## 3. Run and result boundary

Worker 执行器只依赖：

```ts
interface TaskExecutionPort {
  claim(input: ClaimTask): Promise<ClaimedTask | null>;
  renew(input: RenewLease): Promise<LeaseResult>;
  submit(input: SubmitExecutionResult): Promise<ExecutionCommitResult>;
  cancel(input: CancelTask): Promise<TaskSnapshot>;
  reconcile(input: ReconcileOutcome): Promise<ExecutionCommitResult>;
}

interface ModelProvider {
  execute(input: ProviderRequest): Promise<ProviderResponse>;
  lookup(requestKey: string): Promise<ProviderLookupResult>;
}
```

模型调用参数由 Application/Novel Agent 编译，凭据由 Provider adapter 注入。Provider port 返回 receipt，不让 Worker 根据任意文本直接决定 Candidate/Canon 状态。

## 4. Failure matrix

| Failure | Persisted result | Retry |
|---|---|---|
| claim conflict/lost lease | no result or lease_rejected evidence | no blind retry |
| cancellation before request | cancelled attempt | no |
| bounded timeout before request sent | retryable if policy allows | yes, new attempt |
| request sent, response unknown | outcome_unknown + provider key | reconcile first |
| provider known terminal error | terminal_failed | no |
| malformed structured output | retryable/terminal per policy, evidence kept | bounded |
| late response after fence loss | unadopted result | no automatic adoption |
| accepted candidate conflict | result rejected, task unchanged or needs_decision | explicit rebase/retry |

## 5. Reconciliation

- Provider lookup by stable request key is the first recovery action.
- A found response is stored with response hash and `adoptionStatus=unadopted` until a fresh Application command validates Task/Attempt/context.
- If provider cannot look up, policy may permit exactly one new request only when the old request is proven not-started; otherwise stay outcome_unknown/needs_decision.
- Recovery never replays old tool calls or restores old runtime state.

## 6. Rollout and compatibility

1. Implement scheduler port/fake provider against child 2 contracts.
2. Run integration tests with two workers and a controllable clock.
3. Add real Worker host/queue adapter behind feature flag.
4. Keep old runtime read-only; no dual Task writes.
5. Expose execution receipts to Review child through typed query, not shared mutable object.

## 7. Test strategy

- deterministic clock and provider simulator for expiry, timeout and unknown response。
- PostgreSQL integration for `FOR UPDATE SKIP LOCKED`, CAS, lease expiry and rollback。
- crash simulation after provider-start and before result commit。
- duplicate result, stale fence, cancellation race and retry-budget tests。
- import audit proving Worker host owns infrastructure while Novel Agent remains provider-neutral。
