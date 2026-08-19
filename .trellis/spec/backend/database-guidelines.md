# Database Guidelines

> Database patterns and conventions for this project.

---

## Overview

<!--
Document your project's database conventions here.

Questions to answer:
- What ORM/query library do you use?
- How are migrations managed?
- What are the naming conventions for tables/columns?
- How do you handle transactions?
-->

(To be filled by the team)

---

## Query Patterns

<!-- How should queries be written? Batch operations? -->

(To be filled by the team)

---

## Migrations

<!-- How to create and run migrations -->

(To be filled by the team)

---

## Naming Conventions

<!-- Table names, column names, index names -->

(To be filled by the team)

---

## Common Mistakes

<!-- Database-related mistakes your team has made -->

(To be filled by the team)

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
