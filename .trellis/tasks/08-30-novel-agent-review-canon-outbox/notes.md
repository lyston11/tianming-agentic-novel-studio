# 研究笔记：Review Canon Merge and Outbox

## Evidence

- old `PostGenerationReviewModels`/`ChapterPostGenerationReviewer` 展示 structured review、continuity、literary findings、proposed ledger changes 和 next-chapter implications。
- old `CanonBranchMergeTests` 展示 acceptance artifact、merge record、accepted prefix、branch、conflict、human protection、idempotency 和 event/outbox 语义。
- old `ChapterCommitTruthRecorderTests` 展示 committed truth 需要 runtime/package/gate/review metadata、fact snapshots、carryover 和 evidence。
- old `ReworkIntent`/`ReworkBudgetPolicy` 展示结构化返工、scope、impact propagation 和 bounded attempts。
- backend database guideline 要求 domain event/outbox 与事实同事务；跨 DbContext Canon merge 使用 handshake，不共享 fake transaction。
- 当前 TypeScript vertical slice 已有 continuity gate、Acceptance 和 Canon reducer，但没有 literary evidence、EditorialDraft、Canon current-version CAS 或真实 Outbox。

## Decisions

- Review passed 不是 Canon adoption；Acceptance 是显式人类事实。
- Candidate immutable，Rework 新版本；late/unknown execution result 不自动 merge。
- Projection 可重建，SSE/relay 不拥有业务真相。

## Deferred

- 完整文学质量自动化、多章 accepted-prefix orchestration、复杂语义 rebase。
- 真实多数据库部署和运维 relay 细节由基础设施任务落地。
