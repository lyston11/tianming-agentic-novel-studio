# Quality Guidelines

> This document records conventions already implemented in this repository. It is not a planning draft.

---

## Overview

The novel-agent vertical slice uses a TypeScript domain adapter around the generic
`@tianming/agent-core` loop. It is intentionally deterministic and in-memory for
contract testing; production persistence and delivery guarantees belong to the Web
Application/Domain/Worker layer and must not be implied by the fake adapter.

## Scenario: Novel Agent vertical-slice adapter

### 1. Scope / Trigger

Apply this contract when adding or changing the novel-domain adapter, its roles,
skills, context package, domain tools, candidate/review/acceptance flow, or Core
event translation.

### 2. Signatures

- `NovelApplicationPorts`: the Application-facing port aggregate for project,
  context, proposal, candidate, production commands, workflow, and Canon operations.
- `NovelAgentApplication.handleConversationTurn(input: ConversationTurn)`:
  runs the proposal flow and returns a `GoalProposal` produced through the
  `propose_goal` tool.
- `NovelAgentApplication.confirmGoal(input: ConfirmGoalCommand)`:
  creates the confirmed Goal/Revision/Production/Batch/Task and freezes context.
- `NovelAgentApplication.runChapterTask(input)`:
  runs the `chapter-writer` role and returns CandidateChapter plus Review.
- `NovelAgentApplication.acceptCandidate(input: AcceptCandidateCommand)`:
  is the only entry point that can invoke Canon merge.
- `NovelContextPort.freezeForChapter(input)`:
  returns an immutable `NovelContextPackage` with a canonical SHA-256 hash.

### 3. Contracts

- Every command carries `ActorScope` (`projectId`, `userId`) and
  `correlationId`; every write command also carries an `idempotencyKey` and,
  where applicable, an expected candidate version and context hash.
- `NovelContextPackage` is an execution snapshot, not a source of truth. Canon,
  Goal, Production, Candidate, and Workflow Projection retain separate ownership.
- A CandidateChapter may enter `awaiting_acceptance` only after a passed
  continuity Review whose context hash matches the frozen package.
- Canon merge creates one `CanonicalChapter`, updates the referenced character
  and foreshadow versions, marks the candidate merged, and refreshes projection.
- Runtime events are mapped to durable-message-shaped records with stable
  `runId + sequence`; mapping does not advance domain state.
- `tianming-novel-agent` may import `@tianming/agent-core` only. It must not
  import `@mariozechner/pi-*`, Web/ASP.NET code, or database clients.

### 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Actor project/user scope does not match | `NovelCommandError("forbidden")`; no write |
| Proposal idempotency key repeats with the same content | Return the original proposal |
| Proposal idempotency key repeats with different content | `NovelCommandError("conflict")`; no replacement |
| Goal confirmation is repeated with the same key | Return the original commit result |
| Candidate context hash differs from the frozen package | Continuity failure or acceptance conflict; no Canon merge |
| Candidate version differs from `expectedCandidateVersion` | `NovelCommandError("conflict")`; no write |
| Review is missing or failed | Reject acceptance with `invalid_transition`; Canon remains unchanged |
| Acceptance decision is rejected | Persist rejection and block production/task; Canon remains unchanged |
| Runtime event is replayed with the same `runId + sequence` | Deduplicate; preserve one durable message |

### 5. Good / Base / Bad Cases

- Good: AgentCore emits `propose_goal`; the domain tool calls `ProposalPort`,
  then the application reads the persisted proposal by idempotency key.
- Good: a chapter is generated against a frozen package, passes the continuity
  gate, waits for human acceptance, and only then reaches the Canon port.
- Base: the deterministic fake model and `InMemoryNovelStore` prove state and
  hash contracts without claiming PostgreSQL transaction semantics.
- Bad: the model, role, or frontend changes Production/Task/Candidate status
  directly instead of submitting an application command or intent.
- Bad: `agent_end` is treated as successful production completion, or an in-memory
  Core listener is treated as durable SSE replay.
- Bad: a domain tool reaches into EF, a database client, Redis, Qdrant, or Canon
  storage instead of calling its narrow port.

### 6. Tests Required

- Vertical slice: assert Proposal → Confirm → frozen ContextPackage → Candidate
  → passed Review → awaiting acceptance → Acceptance → Canon and projection.
- Boundary tests: assert no Production before confirmation, failed continuity
  blocks acceptance, rejection blocks production, and stale versions are rejected.
- Authorization tests: assert cross-project and cross-user reads/writes return
  `forbidden` without partial state.
- Idempotency tests: assert same-key/same-content returns the same result and
  same-key/different-content returns `conflict`.
- Runtime tests: assert event sequence starts at one, ends with `agent_end`,
  replays after a cursor, and deduplicates repeated sequence numbers.
- Import/scope audit: assert Novel Agent has no direct Pi import, database write,
  Redis/Qdrant integration, or legacy-directory mutation.

### 7. Wrong vs Correct

#### Wrong

```ts
candidate.status = "merged";
await db.save(candidate);
```

This lets an AgentCore/runtime path bypass review, acceptance, authorization, and
any future transaction or outbox boundary.

#### Correct

```ts
await application.acceptCandidate({
  actor,
  actorId,
  correlationId,
  candidateId,
  decision: "accepted",
  expectedCandidateVersion,
  contextPackageHash,
  idempotencyKey,
});
```

The application command validates scope, version, review, and idempotency before
calling the Canon port. The in-memory implementation is only a deterministic test
adapter; a production adapter must preserve the same contract transactionally.

---

## Forbidden Patterns

- Direct database, Redis, Qdrant, or provider access from a Novel Agent role or
  domain tool.
- Direct Pi imports outside `tianming-ai`.
- Treating a fake model/store as evidence that production durability exists.
- Adding novel-domain objects to `tianming-agent-core` to shortcut an adapter.

## Required Patterns

- Keep dependencies directed Web/Application → Novel Agent → Agent Core → AI.
- Carry actor scope, correlation, idempotency, and expected versions across writes.
- Keep acceptance as the only Candidate-to-Canon transition.
- Keep WorkflowProjection derived and read-only.

## Testing Requirements

New domain contracts and state transitions require deterministic unit or vertical
slice coverage. Every bug fix requires a regression assertion for the failed
boundary. Package test, type-check, and build commands must pass before archiving a
Trellis task.

## Testing Value Boundary

Add a new test only when it protects at least one of these real contracts:

- a public HTTP, DTO, envelope, command, or stream contract;
- a domain invariant, state transition, idempotency rule, lease/fence rule, or
  authorization boundary;
- persistence, transaction, migration, outbox, or recovery behavior;
- a high-risk provider boundary such as model execution, Npgsql, Redis, Qdrant,
  or authentication logging;
- a reproduced defect whose absence would allow a known regression.

Before adding the test, state what real regression would be allowed through if
the test were absent. Test count, nominal coverage, and a green mock-only test do
not establish diagnostic value. Do not mechanically clone structurally identical
tests for every kernel, provider, model, or status. Use parameterized cases when
the same rule has several data variants. Removing existing tests requires a
separate change with an explicit replacement and coverage explanation.

The current examples are `old/Tests/Unit/Services/Goals/KernelTaskFailurePolicyTests.cs`,
which tests task-specific retry semantics, and
`old/Tests/Unit/Services/AgentSessions/AgentChatIdempotencyServiceTests.cs`,
which tests replay, payload conflict, and lease takeover. These tests protect
observable contracts rather than merely increasing the count.

## Compatibility Branch Rule

Do not retain or add runtime compatibility branches for historical states,
fields, paths, or behavior unless the requirement explicitly names the old
contract. When development data needs conversion, prefer a one-time migration or
an explicit versioned contract. Do not add a fallback chain for an uncertain
field owner or a planned directory.

The legacy isolation rule and its retirement conditions are authoritative in
`AGENT_CORE_ARCHITECTURE.md` section 6: production callers must be zero, the
replacement must have targeted regression coverage, historical data/migrations
must remain readable, and the full regression suite must pass. Until all four
conditions hold, keep the existing legacy path unchanged and document it as
legacy; do not create a second path just to make a future migration look complete.

## Code Review Checklist

- Does the change preserve the dependency direction and direct-import boundary?
- Are state transitions owned by the Application/Domain adapter rather than Core?
- Are scope, idempotency, version, content hash, and correlation checks explicit?
- Can a failed gate, abort, duplicate command, or replay leave partial state?
- Does the test prove the relevant negative path as well as the happy path?
- If a test is new, can the author name the real regression it prevents? If not,
  reject the test or require a narrower contract.
- Does the change avoid an unrequested compatibility branch and, if it touches a
  legacy path, preserve the four retirement conditions in `AGENT_CORE_ARCHITECTURE.md` section 6?
- Does the documentation distinguish deterministic test adapters from deferred
  PostgreSQL, Outbox/SSE, Worker lease/fence/RLS, provider, and E2E work?
