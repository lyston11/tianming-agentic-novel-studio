# Database Guidelines

## Scenario: Novel Agent control plane on shared PostgreSQL tables

### 1. Scope / Trigger

Use this contract whenever code changes Goal, Production, KernelTask, Outbox,
StreamEvent, Conversation, or Canon coordination. `AgentControlDbContext` owns
the new control-plane writes; `NovelAgentDbContext` remains the legacy read and
reliable-capability adapter. Both map selected existing PostgreSQL tables, so
schema ownership and write ownership must stay explicit.

### 2. Signatures

- Runtime registration: `AddNovelAgentInfrastructure(Action<DbContextOptionsBuilder>)`.
- Migration context: `AgentControlDbContext` with history table
  `__AgentControlMigrationsHistory`.
- Command transaction: `IAgentUnitOfWork.ExecuteAsync<T>(Func<CancellationToken, Task<T>>, CancellationToken)`.
- Worker completion: `PostgresKernelTaskScheduler.CompleteAsync(KernelTaskClaim, IReadOnlyList<string>, CancellationToken)`.
- Compatibility command owner: `ILegacyControlPlaneCommands`, including
  `SubmitGoalAsync`, `PersistCompiledGraphAsync`, `CreateArtifactAsync`,
  `CreateGoalRevisionAsync`, batch transition, and task-failure operations.
- Bridge event type: `novel_agent_acceptance_gate_reached`.
- Bridge payload: `AcceptanceGateReachedPayload(UserId, ProjectId, GoalId,
  ProductionId, TaskGraphVersionId, TaskId, BranchId)`.

### 3. Contracts

- Production startup migrates `PostgresNovelAgentDbContext` first and
  `AgentControlDbContext` second. The contexts use separate migration history
  tables; neither context may independently create a semantic `_v2` copy of an
  existing control table.
- `AgentControlDbContext` is the write owner for `creative_goals`,
  `goal_revisions`, `book_productions`, `production_batches`,
  `task_graph_versions`, `kernel_tasks`, `domain_events`, and `outbox_events`.
  The legacy context may read them and is protected by
  `LegacyControlPlaneWriteGuard`.
- Conversation/checkpoint/stream/lease tables are Agent-control-only tables and
  must be created by the Agent migration assembly with tenant RLS.
- Web compatibility controllers and services may preserve their public entry
  points and legacy DTOs, but they only adapt inputs and call
  `ILegacyControlPlaneCommands`. `EfLegacyControlPlaneCommands` owns EF mapping,
  validation, and the `AgentControlDbContext` transaction. Do not retain a
  second Web/legacy-context write beside the command.
- A confirmed production is persisted as `planned`; its root task is also
  `planned`. Only `ProductionApplicationService.StartAsync` moves the
  production and batch to `running` and the root task to `ready`.
- When the real worker makes an `AcceptanceGate` task `awaiting_user`, the task
  state and `novel_agent_acceptance_gate_reached` outbox row are committed in
  the same `AgentControlDbContext` transaction. The outbox consumer verifies
  task, dependencies, graph, batch, branch, project, and user before calling
  `ReachAcceptanceGateAsync`.
- Duplicate bridge delivery is a no-op once Production is
  `awaiting_acceptance`; it must not append a second domain or stream event.
- Persisted multiword Production states use snake_case:
  `awaiting_acceptance` and `merging_canon`. Domain enum names never leak as
  `awaitingacceptance` or `mergingcanon`.
- Canon merge remains a cross-DbContext outbox handshake. Never share a fake
  transaction across the control and Canon write owners.

### 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Compatibility adapter is constructed without an Application command | Fail with `InvalidOperationException`; never fall back to direct legacy-context writes |
| Legacy graph has no `GoalRevisionId` | Preserve legacy behavior; do not create the new acceptance bridge event |
| New graph reaches acceptance without a matching Production/Batch/Branch | Roll back task completion and outbox insertion |
| Bridge payload scope differs from the outbox row | Reject delivery; do not advance Production |
| Gate is not `awaiting_user` or dependencies are incomplete | Reject delivery as stale/invalid |
| Same bridge row is delivered twice | Keep one `ProductionAwaitingAcceptance` event |
| Canon branch is already `needs_decision` | Route to idempotent Canon rejection; do not call merge again |
| Migration connection is missing | Fail startup; never migrate with the HTTP application role |

### 5. Good / Base / Bad Cases

- Good: a legacy Goal confirmation, graph compile, batch transition, or manual
  Artifact request enters through its existing Web API and commits through one
  Infrastructure command transaction.
- Good: worker completes both reviews, scheduler atomically persists the human
  gate and bridge outbox, dispatcher advances the new Production once.
- Base: old archived graph reaches its old human gate and no new bridge is
  emitted because it has no new Goal Revision identity.
- Bad: a controller, LLM runtime, or frontend writes Production status or marks
  a task ready directly.
- Bad: only one migration context is run in tests, hiding missing compatibility
  columns on shared tables.

### 6. Tests Required

- Application/unit: compatibility services pass tenant-scoped command records
  and do not call `SaveChangesAsync` for control-plane state.
- Infrastructure/integration: each compatibility command validates user/project
  ownership and commits all related Goal/Revision/Production/Graph/Task/Artifact
  changes atomically through `AgentControlDbContext`.
- PostgreSQL integration: migrate both contexts, run task claim/completion,
  assert the bridge outbox and final `awaiting_acceptance` Production state,
  redeliver the same event, and assert one domain event.
- Application/unit: Proposal confirmation remains `planned` until explicit
  Start; repeated acceptance-gate delivery is idempotent.
- Migration: generated SQL must not drop/recreate Canon, Knowledge, Chapter, or
  existing control tables; new tenant tables must have RLS.
- Guard: legacy writes to Goal/Production/Task/Artifact are rejected while
  permitted Canon/Chapter adapters continue to work.

### 7. Wrong vs Correct

#### Wrong

```csharp
gate.Status = "awaiting_user";
await legacyDb.SaveChangesAsync(ct);
await productions.ReachAcceptanceGateAsync(userId, productionId, ct);
```

The process can crash between databases and permanently split task and
Production state.

#### Correct

```csharp
gate.Status = "awaiting_user";
legacyDb.OutboxEvents.Add(AcceptanceGateBridge(gate, production));
await legacyDb.SaveChangesAsync(ct);
```

The dispatcher validates the persisted gate fact and idempotently advances the
new Production in its own transaction.

### Worker ownership cutover and remaining legacy gate

`AgentControlDbContext` owns Worker task claim, renew, complete, fail, dependent
unblocking, failure transitions, and the atomic AcceptanceGate bridge. The
`claim_kernel_task` function is delivered by the Agent migration history and is
marked `AgentControlDbContext worker ownership`; the request application role
cannot execute it.

Required evidence for this boundary:

- `PostgresKernelTaskScheduler` depends on `AgentControlDbContext`, not
  `NovelAgentDbContext` or a legacy production transition service.
- Completion and bridge insertion share one Agent-control transaction; lease
  loss rolls back completion and dependent unblocking.
- The bridge consumer re-checks user/project/goal/graph/task/dependencies, batch,
  and branch before calling the new Production application service.
- Duplicate bridge delivery is a no-op after `ProductionAwaitingAcceptance`.
- PostgreSQL integration tests cover concurrent claim, expired-lease recovery,
  renew, complete, fail, bridge delivery, duplicate delivery, and rollback.

### Legacy command ownership cutover and remaining guard gate

The compatibility entry points for Goal submission, Goal compilation, Workflow
batch transition, task-failure progression, Canon-branch Artifact creation, and
controller-created manual Artifacts now delegate to `ILegacyControlPlaneCommands`.
The production implementation is `EfLegacyControlPlaneCommands`; Web adapters
must not call `SaveChangesAsync` for these mutations or keep a dual-write path.

`TargetArchitecture:EnforceLegacyControlPlaneReadOnly=false` still means the
repository-wide cutover is incomplete. Other legacy control paths, including
`GoalControlService` pause/resume/cancel/safe-point handling and remaining
legacy production/recovery Artifact writers, still mutate shared control tables
through `NovelAgentDbContext`. Keep the guard disabled until every legitimate
writer has an Application-owned command; do not narrow the guard or add raw SQL
or scoped bypasses merely to enable it.

## Naming And Query Conventions

- PostgreSQL tables and columns use snake_case; C# records use PascalCase.
- Every user-scoped query includes `UserId` and the resource key. RLS is defense
  in depth, not a replacement for ownership predicates.
- JSON payload columns use `jsonb`; deserialize at the owning Adapter boundary.
- Outbox idempotency keys include the stable aggregate/task identity and are
  unique within user scope.

## Scenario: Concurrent Proposal confirmation

### 1. Scope / Trigger

Use this contract for every automatic or manual confirmation that maps a
Proposal to a Goal, Revision, Production, and `GoalConfirmed` event. A unique
confirmation index is necessary but is not sufficient: two requests can both
observe no existing result before either transaction writes.

### 2. Signatures

- `WorkflowApplicationService.ConfirmProposalAsync(userId, proposalId,
  actorId, ConfirmGoalProposalRequest, CancellationToken)`
- `IAgentUnitOfWork.ExecuteAsync<T>(Func<CancellationToken, Task<T>>,
  CancellationToken)` runs the confirmation lookup and all confirmation writes
  in one Serializable transaction.
- `IGoalRepository.FindConfirmationResultAsync(userId, projectId,
  idempotencyKey, CancellationToken)` reads the durable `GoalConfirmed` result.

### 3. Contracts

- The idempotency key is stable for the command (`conversation:<turn-key>` for
  automatic confirmation).
- The first committed request creates exactly one Goal Revision, Goal,
  Production, and `GoalConfirmed` event.
- A duplicate request returns the persisted
  `ConfirmGoalProposalResult(GoalId, GoalRevisionId, ProductionId,
  CorrelationId)` byte-for-byte equivalent in value.
- If a concurrent transaction loses a unique-key or serialization race, its
  transaction is rolled back, its change tracker is cleared, and it re-reads
  the durable result before surfacing an error.

### 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Existing result is visible inside the command transaction | Return it; create no new rows |
| Two requests both initially observe no result | One commits; the other re-reads and returns the same result |
| Transaction fails and no durable result exists | Preserve the original exception; do not fabricate a result |
| Caller cancellation is requested | Propagate cancellation; do not convert it to an idempotent success |
| Proposal is missing or outside user scope | Return not-found; do not write Goal/Production state |

### 5. Good/Base/Bad Cases

- Good: two independent DbContexts confirm with the same key and the database
  contains one Revision, one Goal, one Production, and one GoalConfirmed event.
- Base: a sequential duplicate reads the existing Revision and returns its
  original Goal/Production/CorrelationId.
- Bad: checking `FindConfirmationResultAsync` before opening the transaction
  and relying only on the unique index; the loser throws instead of returning
  the durable result.

### 6. Tests Required

- PostgreSQL vertical slice with two independent DbContexts and a barrier after
  both initial idempotency reads; assert both calls return equal results and
  counts of Revision, Goal, Production, and `GoalConfirmed` are all one.
- Sequential duplicate confirmation test; assert GoalId, ProductionId, and
  CorrelationId remain identical.
- Cancellation test; assert `OperationCanceledException` is not converted into
  a confirmation result.

### 7. Wrong vs Correct

#### Wrong

```csharp
var existing = await goals.FindConfirmationResultAsync(..., ct);
if (existing is not null) return existing;
return await unitOfWork.ExecuteAsync(ConfirmAndWriteAsync, ct);
```

#### Correct

```csharp
return await unitOfWork.ExecuteAsync(async ct =>
{
    var existing = await goals.FindConfirmationResultAsync(..., ct);
    if (existing is not null) return existing;
    return await ConfirmAndWriteAsync(ct);
}, cancellationToken);
```
