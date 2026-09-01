# Backend Development Guidelines

> These documents describe conventions already implemented in this repository, not planning proposals.

---

## Overview

This directory contains source-backed guidance for the current ASP.NET, Agent,
and backend-facing TypeScript boundaries. Check the referenced paths before
assuming a migration or new host directory exists.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Current project, host, and test layout | Active |
| [Database Guidelines](./database-guidelines.md) | Shared control-plane tables, migrations, transactions, and outbox bridges | Active |
| [Error Handling](./error-handling.md) | Error types, handling strategies, and observable failure results | Active |
| [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns | Active |
| [Logging Guidelines](./logging-guidelines.md) | Structured logging, log levels, and secret boundaries | Active |

---

## Ownership Map

- `old/Web/NovelAgentWeb/Controllers`, `DTOs`, `Middleware`, and `Filters` own
  the current HTTP boundary.
- `old/Agent/Tianming.NovelAgent.Application`, `Domain`, and `Infrastructure`
  own the separated Agent application/domain/persistence packages.
- `old/Tests` owns unit, architecture, and regression evidence.
- `tianming-agent-core`, `tianming-ai`, and `tianming-novel-agent` are separate
  TypeScript packages and must preserve the dependency direction documented in
  `AGENT_CORE_ARCHITECTURE.md`.

## Before Editing

- Read the relevant guide and inspect at least two referenced real files.
- Confirm the owner of the value, state transition, error, or log field before
  introducing a new abstraction.
- Treat `old/` as an active legacy boundary for paths that still have callers;
  do not infer that a future promotion task has already moved them.
- Add a focused negative-path test only when the new behavior protects a real
  contract described by Quality Guidelines.

---

## Verification

Run the package-specific checks appropriate to the changed boundary and run
`python3 ./.trellis/scripts/task.py validate 08-31-fill-spec-and-test-policy`
before archiving documentation work. Keep documentation changes separate from
business-code changes so a review can verify the claimed paths directly.

All documentation in this directory is written in English, matching the
project's Trellis convention.
