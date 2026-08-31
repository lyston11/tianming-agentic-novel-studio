# Quality Guidelines

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

Apply this guide to frontend source, API/SSE consumers, component behavior, and
tests. The active package is `tianming-web/frontend`, built with Vite, React,
TypeScript, React Query, Tailwind, Radix/shadcn wrappers, Vitest, Testing Library,
and oxlint.

## Required Verification

Run the package checks from `tianming-web/frontend` for a frontend change:

```bash
npm run typecheck
npm run lint
npm test
npm run build
```

`package.json` defines these commands. `npm run build` includes `tsc -b` before
the Vite build, so a locally passing dev server is not enough evidence.

## Testing Value Boundary

Add a new test only when it protects at least one of these real contracts:

- a public API, JSON envelope, request header, URL, or SSE event contract;
- a domain invariant, user-visible state transition, or command guard;
- persistence, localStorage/sessionStorage, query invalidation, or stream cursor
  behavior;
- a high-risk provider, authentication, authorization, or browser protocol
  boundary;
- a defect that has been reproduced or previously regressed.

Before adding the test, state what real regression would be allowed through if
the test were absent. Test count and line coverage are not substitutes for
diagnostic value. Do not mechanically clone the same test for every endpoint,
provider, status, or component variant; use a parameterized case table when the
rule is the same. Removing existing tests requires a separate change with an
explicit replacement/coverage explanation.

## Frontend Boundaries

- Keep server snapshots in React Query and use one feature query owner. SSE is a
  notification/replay channel, not a second business-state store.
- Keep auth, current-project selection, and chat state in their existing
  module-level stores. Do not add zustand or a second global state library; the
  new frontend intentionally has no zustand dependency.
- Keep API calls and runtime envelope handling in `src/api`. Do not fetch from a
  presentational component or cast raw payloads in a page to bypass the API
  boundary.
- Preserve the explicit legacy compatibility paths: the auth store reads the
  documented persisted `{state, version}` shape, and legacy runtime SSE remains
  separate from the Workflow stream. Do not add guessed compatibility branches
  for undocumented fields or statuses.

## Accessibility and UI Quality

- Use semantic landmarks, labelled controls, `role="alert"` for actionable
  failures, keyboard-capable Radix primitives, and visible focus styles.
- Cover loading, empty, error, disabled, and success/refresh states when they
  change the user's available action. Avoid a test that only snapshots static
  class strings.
- Keep stable dimensions and readable text in dense workflow panels. Reuse the
  local UI primitives instead of introducing a parallel modal, select, or button
  implementation.

## Real Examples

- `tianming-web/frontend/package.json` is the source for the required commands.
- `tianming-web/frontend/src/api/client.test.ts` tests envelope, retry, and
  authorization behavior at the transport boundary.
- `tianming-web/frontend/src/api/auth-store.test.ts` tests invalid persisted
  state and auth subscription behavior.
- `tianming-web/frontend/src/lib/stream-retry.test.ts` tests the bounded retry
  contract rather than each consumer duplicating it.
- `tianming-web/frontend/src/features/workflow/use-goal-workflow.ts` and
  `use-novel-agent-workflow-stream.ts` show shared invalidation and stream
  ownership that should be preserved in tests.

## Code Review Checklist

- Does the change keep API, server state, transient stream state, and local UI
  state in their existing owners?
- Can a malformed, stale, unauthorized, or cancelled response be mistaken for
  a successful snapshot?
- Does every new test protect a real regression, and is the negative path tested
  where the failure would otherwise be silent?
- Were `typecheck`, `lint`, `test`, and `build` run for the changed package?
- Did the change avoid an undocumented compatibility branch or a duplicate
  component/query/store abstraction?
