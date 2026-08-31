# 研究证据：Review Canon Outbox

## Anchors

- `old/Services/Framework/AI/NovelAgent/Models/PostGenerationReviewModels.cs`：review artifact 语义。
- `old/Services/Framework/AI/NovelAgent/Services/ChapterPostGenerationReviewer.cs`：post-generation continuity/literary checks。
- `old/Tests/Unit/Services/Canon/CanonBranchMergeTests.cs`：accepted prefix、branch conflict、human protection、merge evidence、idempotency。
- `old/Tests/Unit/Services/Production/ChapterCommitTruthRecorderTests.cs`：commit truth metadata/evidence。
- `old/Web/NovelAgentWeb/Data/Entities/ReworkIntent.cs` 与 `ReworkBudgetPolicy.cs`：rework/impact/budget。
- `.trellis/spec/backend/database-guidelines.md:57-58`：Canon cross-DbContext outbox handshake。
- `.trellis/spec/backend/database-guidelines.md:87-103`：Outbox/migration/integration test requirements。
- `tianming-novel-agent/src/domain/continuity-gate.ts`、`src/store/in-memory-store.ts`：当前最小 gate/acceptance/reducer evidence。

## Boundary conclusion

Review、Acceptance、Canon 和 Outbox 必须是可审计的独立事实；AgentCore event、Review boolean、projection callback 和 SSE notification 均不能替代 Canon write transaction。
