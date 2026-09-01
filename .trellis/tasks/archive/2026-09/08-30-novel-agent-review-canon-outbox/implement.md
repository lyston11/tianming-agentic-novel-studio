# 实施计划：Review Canon Merge and Outbox

## 0. Discovery

- [ ] 读取 parent、Conversation/Goal、PostgreSQL、Worker child artifacts 和 backend database/error/quality specs。
- [ ] 定位当前 Canon/Chapter/Character/Foreshadow 写 owner、Review 模型、Outbox/DomainEvent 表和 projection/reducer 实现。
- [ ] 研究 old CanonBranchMerge、PostGenerationReview、ChapterCommitTruthRecorder、DomainReducer 和 Rework contracts；记录哪些是语义、哪些是 legacy mechanism。

## 1. Review and rework contracts

- [ ] 定义 HardGateReview、SoftReview、ReviewFinding、EvidenceSpan、EditorialDraft、ReworkRequest 和 impact propagation。
- [ ] 将现有 continuity gate 映射为可版本化 hard gate；保留 evidence/hash/provenance。
- [ ] 实现 Candidate immutable lineage 与 new-version submission。

## 2. Acceptance and Canon command

- [ ] 定义 Acceptance actor role、idempotency、candidate/context/Goal version checks。
- [ ] 实现正文 + ledger updates 的原子 merge command。
- [ ] 实现 current Canon version-vector CAS、branch/prefix、protected human conflict 和 needs_decision。

## 3. Events, Outbox, projection

- [ ] 定义 Domain Event taxonomy、aggregate version、causation/correlation 和 outbox idempotency。
- [ ] 在正确 write owner transaction 内写 Canon/merge/event/outbox；跨 DbContext 使用 bridge handshake。
- [ ] 实现 inbox/consumer dedupe、projection reducer replay 和 failure retry。

## 4. Verification

- [ ] hard gate fail/soft finding/acceptance readiness tests。
- [ ] rework lineage/budget/impact tests。
- [ ] acceptance auth/version/hash/idempotency and no-partial-write tests。
- [ ] Canon CAS/conflict/protected human/prefix tests。
- [ ] outbox atomicity, duplicate delivery, replay and crash recovery tests。

## 5. Validation and rollback

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-novel-agent-review-canon-outbox
# run discovered backend tests, migration/integration tests, and affected package checks
```

回滚：关闭新 merge/rework endpoint，保留 immutable Review/Acceptance/Candidate/Canon records；不得删除已提交 Canon 或用 projection 反向修复 source of truth。冲突通过 corrective command 处理。

## 6. Handoff

输出给 Web child：Candidate/Review/Acceptance/Workflow query DTO、状态/错误矩阵和 SSE business event taxonomy。输出给 parent：Canon CAS、Outbox atomicity、replay 和 conflict evidence。
