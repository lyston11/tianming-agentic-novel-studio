# Type Safety

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

The frontend is TypeScript-first at compile time, but its HTTP and SSE inputs
are runtime JSON. The current API DTO mirror is hand-maintained in
`tianming-web/frontend/src/api/types.ts`; the API client and auth store are the
runtime boundary. The backend C# DTO/OpenAPI contract remains the eventual
source of generated API views.

## Type Organization

- Put shared HTTP DTOs, closed string unions, and response models in
  `src/api/types.ts`. Keep endpoint functions in domain modules such as
  `src/api/novel-agent.ts`, `src/api/goals.ts`, and `src/api/chat.ts`.
- Keep hook/component option types beside the hook/component when the type is
  private to that module. `ChapterWorkspaceProps` and
  `NovelAgentWorkflowStreamOptions` are current examples.
- Use string unions for known server enums and event kinds. Use interfaces for
  object-shaped DTOs and keep optional fields explicitly optional or nullable,
  matching the server payload rather than making every field optional.
- Use the `@/*` path alias and type-only imports where appropriate. Compiler
  settings in `tsconfig.app.json` include `noUnusedLocals`, `noUnusedParameters`,
  `noFallthroughCasesInSwitch`, and `erasableSyntaxOnly`; new code must pass
  `npm run typecheck`.

## Runtime Boundaries

- `src/api/client.ts` owns the unified `ApiEnvelope<T>` shape. Its
  `isApiEnvelope` check requires `success`, all four version fields, and either
  `data` or `error` before `unwrapEnvelope` returns the typed data.
- A failed envelope becomes `EnvelopeError` with the server error code. A
  missing envelope is a hard error; feature code must not silently treat a raw
  object as a successful DTO.
- `src/api/auth-store.ts` accepts `unknown`, checks records and non-empty strings,
  and returns an empty session when the persisted shape is invalid. Keep this
  validation centralized instead of casting localStorage data in components.
- SSE payloads are parsed at the stream boundary and filtered by stream kind,
  stream id, sequence, and target identifiers before they trigger invalidation.
  `src/api/types.ts` defines the envelope; stream hooks own the event-specific
  acceptance rules.

## Generated API Views

The backend is C#-first: backend DTOs produce OpenAPI, and frontend generated
types are a derived view. The API code-generation task is still planning; when
it lands, its generated view must follow these rules:

- Generated files are views and must not be hand-edited.
- A contract change goes through the backend DTO/OpenAPI source, then the
  generation command, then typecheck and the contract comparison gate.
- Types not represented by OpenAPI, such as SSE event payload details or local
  view models, remain in a clearly named hand-written file with the reason
  documented. Do not delete them merely because a generated schema exists.
- Do not make a local cast or compatibility alias to hide a generated/manual
  contract mismatch. Fix the source contract or the explicit boundary mapper.

## Assertions and Unknown Data

- Prefer narrowing functions and explicit checks for `unknown`. A type
  assertion is acceptable only at a small, documented boundary after the data
  has been validated or when a library's type is narrower than its runtime API.
- Do not use `any`, double assertions, or non-null assertions to make
  `tsc` pass. If a value can be absent, represent that absence and handle it.
- Keep normalization in one mapper. `toNovelProjectInfo` in
  `src/api/projects.ts` is the existing place for the project view conversion;
  do not repeat field renaming in every consumer.
- Do not spread raw JSON into a DTO and assume unknown fields are harmless when
  the operation changes server state. Validate the fields that drive a command.

## Real Examples and Tests

- `tianming-web/frontend/src/api/client.ts` demonstrates the envelope type,
  runtime check, error class, retry, and transport boundary.
- `tianming-web/frontend/src/api/auth-store.ts` demonstrates `unknown` parsing
  and storage sanitization.
- `tianming-web/frontend/src/api/types.ts` is the current shared DTO/type file.
- `tianming-web/frontend/src/api/client.test.ts` and
  `tianming-web/frontend/src/api/auth-store.test.ts` protect envelope and
  persisted-session contracts.

## Wrong vs Correct

Wrong: `(response as NovelAgentWorkflowResponse).current.goals[0]` in a page,
or edit a generated API type view to accommodate a backend mismatch.

Correct: validate/unwrap at `src/api/client.ts`, map at the API boundary when
needed, use the shared type in the feature hook, and change the backend DTO then
regenerate when the wire contract is actually changing.
