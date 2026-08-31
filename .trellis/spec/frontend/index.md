# Frontend Development Guidelines

> These documents describe conventions already implemented in this repository, not planning proposals.

---

## Overview

This directory contains source-backed guidance for the active Vite package at
`tianming-web/frontend`. The old frontend remains a frozen comparison surface.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Active source, feature, and test layout | Active |
| [Component Guidelines](./component-guidelines.md) | Component patterns, props, composition, accessibility | Active |
| [Hook Guidelines](./hook-guidelines.md) | Custom hooks, query ownership, and stream effects | Active |
| [State Management](./state-management.md) | React Query ownership and dual-SSE migration rules | Active |
| [Quality Guidelines](./quality-guidelines.md) | Checks, test value, accessibility, and forbidden patterns | Active |
| [Type Safety](./type-safety.md) | DTO organization, runtime boundaries, and generated views | Active |

---

## Ownership Map

- `src/api` owns HTTP/SSE transport, endpoint modules, and shared API types.
- `src/features` owns route pages and domain-specific composition.
- `src/components/ui` owns reusable Radix/shadcn primitives; `shared` and
  `layout` own cross-feature visual composition.
- `src/lib` owns module stores and pure client utilities. React Query remains the
  server-state owner for workflow snapshots.
- `src/test` owns shared Vitest/jsdom setup; focused tests stay beside the module
  under test.

## Before Editing

- Read the relevant guide and inspect at least two referenced files in
  `tianming-web/frontend/src`.
- Keep API DTOs, query state, transient stream state, and local UI state in their
  existing owners.
- Use a real runtime narrowing boundary for persisted or network data; do not
  silence type errors with an ad hoc cast.
- Add a test only when its absence would allow a named contract or reproduced
  regression to pass.

---

## Verification

From `tianming-web/frontend`, run `npm run typecheck`, `npm run lint`, `npm test`,
and `npm run build` for source changes. For documentation-only changes, at
minimum check the referenced paths and run the Trellis task validator.

All documentation in this directory is written in English, matching the
project's Trellis convention.
