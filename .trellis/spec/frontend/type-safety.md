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

The backend is C#-first: backend DTOs produce OpenAPI, and the frontend
generated types are a derived view. The chain is in place:

```text
C# DTOs  →  tianming-web/backend/openapi.json      (Scripts/export-openapi.sh)
         →  tianming-web/frontend/src/api/schema.d.ts   (npm run gen:api)
```

Two gates guard it, and they cover different halves:

- `./tianming-web/backend/Scripts/export-openapi.sh --check` catches the backend
  half — `openapi.json` falling behind the C# DTOs. It needs the database stack
  up, because the host fails fast on connection strings, JWT secret, worker role
  identity, and a worker-role login precheck before Swagger is reachable.
- `npm run gen:check` catches the frontend half — `schema.d.ts` being hand-edited
  or left stale after `openapi.json` changed. It needs no services and never
  writes into the working tree.

Rules:

- Generated files are views and must not be hand-edited.
- A contract change goes through the backend DTO source, then
  `export-openapi.sh`, then `npm run gen:api`, then typecheck and both gates.
- Types not represented by OpenAPI, such as SSE event payload details or local
  view models, remain in a clearly named hand-written file with the reason
  documented. Do not delete them merely because a generated schema exists.
- Do not make a local cast or compatibility alias to hide a generated/manual
  contract mismatch. Fix the source contract or the explicit boundary mapper.

### Why `types.ts` is still hand-written

`src/api/types.ts` was NOT replaced by `schema.d.ts`, deliberately. Measured on
2026-09-01: the spec has 82 schemas, `types.ts` exports 230 types, and only 29
names overlap. Of the 28 overlapping types that have properties, **all 28** would
become looser if swapped — every one of them generates with `required: none` and
`nullable: true` fields, so `id: string` would become `id?: string | null`.

The cause is on the backend: response DTOs carry no `[Required]` attributes and
the controllers return `IActionResult` rather than `ActionResult<T>`, so
Swashbuckle can infer neither requiredness nor most response schemas. Request
DTOs that do use `[Required]` generate correctly — `CreateKnowledgeRequest`
comes out with `required: [content, entryType, projectId, title]`.

So swapping today would trade real compile-time guarantees for a drift check we
already get from the two gates above. Preconditions for revisiting it:

1. Annotate response DTOs so requiredness survives into the schema.
2. Return typed results so response schemas appear at all.
3. Re-measure the overlap and confirm the generated types are no looser.

Until then `schema.d.ts` is a contract-drift detector, not a type source.

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
