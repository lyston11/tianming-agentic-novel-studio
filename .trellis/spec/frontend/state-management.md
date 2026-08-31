# State Management

## Scenario: Conversation and Workflow streams during migration

### 1. Scope / Trigger

Use this contract for Agent chat, Goal/Production progress, SSE replay, and
React Query invalidation. Conversation and production are separate runtimes;
their streams may coexist during migration but must not become two client-side
sources of business truth.

### 2. Signatures

- Workflow transport: `createNovelAgentWorkflowSseConnection(projectId,
  afterCursor, callbacks)`.
- Workflow hook: `useNovelAgentWorkflowStream(projectId, goalId,
  productionId, onBusinessEvent)`.
- Legacy compatibility stream: `createSseConnection(sessionId, afterEventId,
  callbacks)`.
- Query owner: `useGoalWorkflow(goalId)` and its single `invalidate()` path.

### 3. Contracts

- REST owns commands and snapshots. SSE only announces that persisted server
  state changed; the client refetches React Query data after a valid business
  event.
- Conversation and Workflow streams use the same envelope shape but distinct
  `streamKind`/`streamId` resources. A session stream cannot subscribe to a
  project workflow.
- Validate `streamKind`, `streamId`, positive monotonic `sequence`, and optional
  `goalId`/`productionId` before invalidating queries.
- Track the durable cursor per stream. Deduplicate by event identity and
  sequence; do not invent a second status cursor in a component.
- Legacy goal events and new Workflow events may both call the same
  `invalidate()` function during migration. Neither handler directly mutates a
  parallel Goal/Production object.
- Transient token deltas affect only the visible streaming message. They never
  advance Proposal, Goal, Production, Task, Candidate, or Canon state.

### 4. Validation & Error Matrix

| Input | Client behavior |
|---|---|
| Envelope has the wrong `streamKind` or `streamId` | Ignore it |
| Sequence is duplicate or older than the last accepted sequence | Ignore it |
| Workflow event targets another Goal/Production | Ignore it |
| Valid persisted business event | Update cursor, then invalidate the shared queries |
| Token delta | Append transient text only; do not invalidate business state |
| Connection closes | Reconnect with the durable cursor and bounded backoff |

### 5. Good / Base / Bad Cases

- Good: a Production event arrives, the hook accepts its sequence, and the
  existing Workflow query refetches the authoritative snapshot.
- Base: both legacy and new streams announce the same logical change; both only
  request an idempotent refetch.
- Bad: a hook sets `production.status` from an SSE payload and later overwrites
  fresher REST data.
- Bad: use `sourceSessionId` as the Workflow stream identity.

### 6. Tests Required

- Frontend contract test asserts separate Conversation/Workflow endpoint URLs,
  envelope fields, stream-kind checks, goal/production filters, and one shared
  invalidation path.
- Retry test asserts cursor reuse and bounded exponential backoff.
- Backend/API tests assert ownership before SSE headers are committed and that
  cross-user resources return 404.
- Browser E2E remains required before claiming the full chat-to-Canon user
  workflow is verified.

### 7. Wrong vs Correct

#### Wrong

```ts
onEvent(event => setProduction({ ...production, status: event.data.status }))
```

#### Correct

```ts
onBusinessEvent(() => invalidate())
```

React Query remains the server-state owner, while SSE supplies low-latency
change notification and replay position.

## State Categories

- Component-local: input drafts, selection, disclosure state.
- Server state: Conversation, Proposal, Goal, Production, Candidate, and
  Workflow snapshots in React Query.
- Transient stream state: token text, connection state, and per-stream cursor.
- URL state: selected project/session/goal identifiers when navigation requires
  a durable link.
