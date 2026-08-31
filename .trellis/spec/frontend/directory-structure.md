# Directory Structure

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

Use this guide for new React pages, feature components, API modules, stores,
hooks, shared UI, and frontend tests. The active frontend is the Vite package at
`tianming-web/frontend`; `old/Web/NovelAgentWeb.Frontend` is a frozen comparison
implementation and is not an active destination for new work.

## Current Layout

```text
tianming-web/frontend/
├── src/
│   ├── api/          # client, endpoint modules, DTO types, auth store, tests
│   ├── components/
│   │   ├── layout/   # AppRail and route guards
│   │   ├── shared/   # BrandMark, PageHeader, EmptyState, MarkdownContent
│   │   └── ui/       # shadcn/radix primitives
│   ├── features/
│   │   ├── agent/ auth/ library/ materials/ settings/ workflow/
│   ├── lib/          # stores, stream retry, runtime normalization, cn
│   ├── test/         # Vitest/jsdom setup
│   ├── App.tsx
│   ├── main.tsx
│   └── index.css
├── public/
├── package.json
├── vite.config.ts
└── tsconfig*.json
```

## Ownership Rules

- Put transport and DTO mapping in `src/api`. Keep one endpoint family per
  module, for example `src/api/novel-agent.ts`, `src/api/goals.ts`, and
  `src/api/chat.ts`. The barrel `src/api/index.ts` preserves the existing flat
  import surface.
- Put reusable visual primitives in `src/components/ui` and application-wide
  visual pieces in `src/components/shared` or `src/components/layout`. Feature
  UI and feature-specific composition belong under the matching `src/features`
  directory.
- Put cross-feature state machinery and pure client utilities in `src/lib`.
  The project, chat, and auth stores are module-level stores there or in
  `src/api/auth-store.ts`; do not create a second store inside a page.
- Put route-level lazy pages and their local subcomponents in the owning feature.
  `src/App.tsx` is the route and provider composition boundary, not a feature
  implementation area.
- Keep frontend tests beside the module under test with a `.test.ts` or
  `.test.tsx` suffix. Shared browser test setup belongs in `src/test/setup.ts`.

## Naming

- Use lowercase kebab-case filenames, including React components and hooks:
  `goal-workbench.tsx`, `use-goal-workflow.ts`, and `auth-store.ts` are the
  existing pattern.
- Use named exports for shared components and hooks. Route pages loaded by the
  lazy imports in `src/App.tsx` use the default export where the current page
  module already does so.
- Keep a feature's component, hook, and feature utility together. Import shared
  code through the `@/*` alias configured in `tsconfig.app.json` and
  `vite.config.ts`.
- Do not add a generic `utils` export for a feature-specific function. A utility
  belongs in `src/lib` only when it has a cross-feature responsibility.

## Real Examples

- `tianming-web/frontend/src/App.tsx` owns providers and lazy route pages.
- `tianming-web/frontend/src/api/client.ts` owns the JSON envelope, auth header,
  retry, idempotency, and SSE transport primitives.
- `tianming-web/frontend/src/features/workflow/use-goal-workflow.ts` owns the
  workflow query/mutation composition for its feature.
- `tianming-web/frontend/src/components/shared/page-header.tsx` is a shared
  layout component; `tianming-web/frontend/src/components/ui/button.tsx` is a
  reusable Radix/shadcn-style primitive.
- `tianming-web/frontend/src/lib/project-store.ts` owns current-project client
  state and exposes the React subscription hook.

## Wrong vs Correct

Wrong: fetch a workflow directly from `workflow-page.tsx`, define a second copy
of the DTO beside it, and put the resulting status in a component-local object.

Correct: keep the endpoint in `src/api`, keep the server state in the feature's
React Query hook, and render it through feature components. A stream hook may
announce a change, but the query remains the snapshot owner.

The current promotion task has not created `tianming-web/backend`. Do not move
or document frontend files as if that backend directory already exists.
