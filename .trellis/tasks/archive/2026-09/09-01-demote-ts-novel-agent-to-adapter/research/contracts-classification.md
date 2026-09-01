# contracts.ts 类型分类与真源判定

生成时间：2026-09-01
来源：`tianming-novel-agent/src/contracts.ts`（678 行，77 个导出）

## 结论先行：生成路线不成立

对 35 个跨边界类型逐一检查 `tianming-web/backend/openapi.json`（82 个 schema）：

**命中 0 个。35 个全部不在 OpenAPI 契约中。**

原因是结构性的，不是遗漏：`ports.ts` 定义的是**内部 Application port 契约**（`NovelApplicationPorts` 等 8 个 interface），不是 HTTP 契约。`NovelContextPackage`、`CandidateChapter`、`ChapterCandidateIntent` 这类概念从不经 REST 暴露，Swashbuckle 无从生成。

因此 R1 的"从 OpenAPI 生成"路径**不适用于本包**——与前端 `types.ts` 的情形不同（那里类型确实对应 HTTP DTO）。这不是缺陷，是边界性质不同。

### 对 R1 的修正

跨边界类型保持手写，但必须满足两个条件（替代"生成"）：

1. **注明对应的 C# 权威类型**（或明确"无对应物，属 TS adapter 内部概念"）；
2. **有 guard 防止字段级漂移**——见 R6，断言的对象是"TS 类型不得声明 C# 侧不存在的领域字段"，而非结构等价。

## 分类统计

| 类别 | 数量 | 处置 |
|---|---|---|
| 跨边界（`ports.ts` 传输） | 35 | 保留手写 + 标注 C# 对应物 |
| 嵌套字段类型 | 32 | 跟随父类型 |
| 纯 TS 内部 | 2 | 保留，无需对应 |
| 零引用 | 8 | 删除 |

## 1. 跨边界类型（35）

`ports.ts` 的 `import type {}` 块 + 内联 `import("./contracts.js").X` 引用。

AcceptCandidateCommand, Acceptance, AcceptanceResult, ActorScope, AppendMessage, AppendTurn, Batch, CandidateChapter, ChapterCandidateIntent, CompleteConversationTurn, ConfirmGoalCommand, ContextRequest, ConversationMessageRecord, ConversationQuery, ConversationRecord, ConversationTurn, ConversationTurnRecord, DurableRuntimeMessage, FreezeContextRequest, Goal, GoalCommitResult, GoalProposal, GoalRevision, Id, NovelContextPackage, NovelProjectSnapshot, Production, ProjectId, ProposeGoalInput, Review, RuntimeCheckpointRecord, RuntimeRunRecord, Task, UserId, WorkflowProjection

**C# 对应物精确定位待补**：初次 grep（`class|interface|record <Name>`）产生了假阳性——`Acceptance` 命中 `NovelAgentOutboxHandler.cs`、`Goal` 命中 `GoalWorkflowController.cs`、`Production` 命中 `ProductionEvent.cs`，均为子串误匹配。精确定位需按命名空间与字段集比对，不能靠名称 grep。

## 2. 嵌套字段类型（32）

跟随跨边界父类型，不独立维护真源。

AcceptanceDecision, BatchStatus, CandidateStatus, CanonMergeResult, CanonicalChapter, CharacterState, CharacterUpdateIntent, ConversationId, ConversationMessageRole, ConversationStatus, ConversationTurnStatus, CorrelationId, ExecutionMode, ForeshadowEntry, ForeshadowUpdateIntent, GoalIntent, GoalProposalContent, GoalProposalRevision, GoalProposalStatus, GoalStatus, IdempotencyKey, NovelCommandError, ProductionStatus, ReviewFinding, RuntimeEventType, RuntimeRunStatus, SourceReference, TaskStatus, VersionVector, canonicalJson, proposalContentHash, sha256

注：`NovelCommandError`（5 个文件使用）与 `ReviewFinding`、`RuntimeEventType`、`CharacterState`、`ForeshadowEntry`、`CanonMergeResult`、`proposalContentHash` 在 contracts.ts 外也有引用，删除父类型时需一并核查。

## 3. 纯 TS 内部（2）

- `clone<T>()` — `tools/domain-tools.ts`、`context/context-provider.ts`、`test/fixtures/in-memory-novel-test-store.ts`
- `contextPackageContentHash()` — `context/context-provider.ts`

adapter 实现细节，无 C# 对应概念。

## 4. 零引用（8）— 删除

ConfirmGoalProposalCommand, DecideProposalCommand, GoalCommitIntent, GoalProposalTransition, ProposalRevision, QueryWorkflowRequest, RequestChapterCommand, ReviseProposalCommand

在 `contracts.ts` 之外零引用（`src/`、`test/` 全查）。历史遗留未清理。

## 方法

```
跨边界    = ports.ts 的 import type 块 ∪ 内联 import("./contracts.js").X
嵌套      = 去掉自身声明后，名称仍在 contracts.ts 其余部分出现
纯内部    = contracts.ts 内无引用，但 src/ 或 test/ 有引用
零引用    = 两者皆无
```
