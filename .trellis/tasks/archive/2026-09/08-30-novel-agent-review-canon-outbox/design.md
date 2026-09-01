# 技术设计：Review Canon Merge and Outbox

## 1. Domain boundaries

```text
Candidate (immutable artifact)
  → HardGateReview
  → SoftLiteraryReview
  → AcceptanceDecision (human, durable)
  → CanonMergeCommand (Application)
  → CanonWriteOwner transaction
  → DomainEvent + Outbox
  → projections/SSE
```

Review、Acceptance、Canon merge 是三个不同的 aggregate/command boundary。AgentCore/Novel Agent 只能产出候选、审查结果或 intent；不能直接设置 Candidate merged 或 Canon。

## 2. Review contract

```ts
interface ReviewArtifact {
  reviewId: Id;
  candidateId: Id;
  candidateVersion: number;
  contextPackageId: Id;
  contextPackageHash: string;
  goalRevisionId: Id;
  gateKind: "hard" | "soft";
  gateVersion: string;
  passed: boolean;
  findings: readonly ReviewFinding[];
  evidenceSpans: readonly EvidenceSpan[];
  inputHash: string;
  reviewerProfile: string;
}
```

Hard gate reducer 要求 fail closed；required source/evidence 缺失不能只给 warning。Soft findings 可建议 rework，但只有 explicit policy 才阻断。

## 3. Rework lineage

```text
Candidate v1 ── EditorialDraft ── ReworkRequest ── Candidate v2
       \_________________________________________ lineageId
```

Draft 可变、Candidate immutable。新 Candidate 记录 parent、lineage、sourceType、context hash 和 execution receipt；旧候选保留 historical status，不原地覆盖。

## 4. Acceptance and merge transaction

```text
begin Canon owner transaction
  authorize actor/project
  load persisted acceptance and candidate
  validate review/hard gate/version/context hash
  compare current canon version vector to frozen baseline
  validate branch/prefix/protected human versions
  reserve idempotency key
  write canonical chapter + character/foreshadow changes
  write merge record + domain events + outbox rows
  update projection checkpoint
commit
```

如果 Canon 与 control plane 是不同 DbContext，Acceptance gate/merge request 先由 control-plane outbox 发出；Canon owner 消费并重新验证 scope、baseline、branch 和 acceptance，成功后再发结果 event。绝不模拟跨数据库共享 transaction。

## 5. Outbox/reducer

Outbox key 至少包含 aggregate identity、event type、version/idempotency；consumer 用 inbox/delivery record 去重。Projection reducer 按 aggregate version/sequence 重放，旧事件不回退新状态。SSE 只读取 durable stream/projection，不在 callback 内写状态。

## 6. Conflict/recovery

- baseline mismatch → `merge_conflict` + evidence，不写 Canon。
- protected human version overlap → `needs_decision`。
- duplicate acceptance/merge → 原结果 no-op。
- Outbox publish failure → retry delivery，不回滚 Canon。
- crash before commit → transaction rollback，无 partial ledger。
- late Worker result → 由 Worker receipt 标记 unadopted，不能自动 merge。

## 7. Test strategy

- hard/soft gate fixture and evidence version tests。
- draft/rework lineage, budget and impact propagation tests。
- acceptance auth/version/idempotency/no-partial-write tests。
- Canon baseline CAS, branch/prefix, protected human conflict tests。
- transaction rollback and cross-context outbox handshake tests。
- duplicate event/consumer retry/projection replay tests。
