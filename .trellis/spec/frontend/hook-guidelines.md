# Hook Guidelines

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

Use this guide for custom hooks in `tianming-web/frontend/src`. Hooks currently
cover auth context, React Query workflow state, legacy/runtime SSE transport,
and subscriptions to module-level client stores.

## Naming and Shape

- Every custom hook starts with `use` and has a responsibility visible in its
  name, such as `useAuth`, `useGoalWorkflow`, `useGoalProgressStream`, or
  `useNovelAgentWorkflowStream`.
- Define an options interface next to a non-trivial hook. Keep identifiers,
  callbacks, and nullable inputs explicit rather than accepting an untyped bag.
- Keep hook return shapes stable. A query hook may expose query objects,
  selected data, mutation functions, status, and error; it should not return a
  different shape for loading versus ready states.
- Call hooks unconditionally at the top level. Conditions belong inside an
  effect/query option, as `enabled: Boolean(goalId)` and the stream early-return
  effects demonstrate.

## Server State and Commands

- Use `@tanstack/react-query` for server snapshots and mutations. Endpoint
  functions remain in `src/api`; a hook composes them and owns query keys,
  invalidation, and mutation status.
- `useGoalWorkflow` is the current owner of the Goal workflow snapshot. Its
  `invalidate()` function invalidates both the workflow and selected chapter
  queries, and both legacy and new stream hooks call that same function.
- Do not maintain a parallel component copy of Goal, Production, Candidate, or
  Canon state. After a successful command, invalidate/refetch the authoritative
  query rather than patching a status from the command response.
- Keep draft input, selected disclosure, and connection state local to the hook
  or component that owns it. Do not put transient token text into React Query.

## Streams and Effects

- A stream hook creates and closes its transport inside `useEffect`. Track a
  `disposed` flag, clear retry timers, and close the connection in cleanup.
- Store changing callbacks in a ref when the connection should not restart for
  every render. `useNovelAgentWorkflowStream` and `useGoalProgressStream` use a
  callback ref for this purpose.
- Keep the cursor/sequence ref with the stream instance. Validate stream identity,
  filter the target Goal/Production, ignore old or transient business events as
  appropriate, then notify the query owner.
- Use the shared `runtimeStreamRetryDelay` helper for reconnects. Do not create
  a second backoff formula in a feature hook.
- Treat malformed stream data as an ignored event with the existing query/poll
  fallback. Do not convert a malformed or cancelled stream into a successful
  business state.

## Module-Level Stores

The current client stores are plain module state plus listener sets. React-facing
subscriptions use `useSyncExternalStore`, as in:

- `tianming-web/frontend/src/lib/project-store.ts`
- `tianming-web/frontend/src/lib/chat-store.ts`
- `tianming-web/frontend/src/api/auth-store.ts`

Keep mutation functions and persistence in the store module. The hook should
subscribe to a stable snapshot and should not reimplement storage parsing in
each consumer.

## Real Examples and Tests

- `tianming-web/frontend/src/features/workflow/use-goal-workflow.ts` is the
  query/mutation owner and shared invalidation example.
- `tianming-web/frontend/src/features/workflow/use-novel-agent-workflow-stream.ts`
  is the Workflow cursor, filtering, cleanup, and reconnect example.
- `tianming-web/frontend/src/features/agent/use-agent-runtime-stream.ts` is the
  legacy replay plus live SSE example.
- `tianming-web/frontend/src/features/auth/auth-context.tsx` is the context
  subscription and mutation example.
- Test hooks only for a real query, stream, store, or user interaction contract;
  do not add a test that merely proves React called a hook.

## Wrong vs Correct

Wrong: set `production.status` from an SSE payload inside a component hook or
start a new connection whenever an inline callback identity changes.

Correct: validate the stream envelope, advance its cursor, call the shared
`invalidate()` callback, and let React Query fetch the current snapshot. Keep
the callback in a ref and clean up the connection on dependency changes.
