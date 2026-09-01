# Directory Structure

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

Use this guide when adding or moving backend code, tests, migrations, controllers,
application ports, domain objects, or runtime adapters. The paths below describe
the checked-in layout on this branch. The control-plane promotion task is still
in `planning`, so a proposed `tianming-web/backend` directory is not a current
source location.

## Current Layout

```text
old/
├── Agent/
│   ├── Tianming.NovelAgent.Application/
│   │   ├── Conversation/ Models/ Ports/ Production/ Workflow/
│   ├── Tianming.NovelAgent.Contracts/
│   ├── Tianming.NovelAgent.Domain/
│   │   ├── Common/ Events/ Goals/ Production/
│   ├── Tianming.NovelAgent.Infrastructure/
│   │   ├── Conversation/ Migrations/ Models/ Persistence/
│   └── Tianming.NovelAgent.PiRuntime/
├── Web/NovelAgentWeb/
│   ├── Controllers/ DTOs/ Data/ Filters/ Middleware/ Models/
│   ├── Migration/ Migrations/ MigrationsPostgres/ Services/ Support/
└── Tests/
    ├── Unit/ AgentArchitecture/ AgentKernelRegression/ NovelAgentRegression/
└── TianmingAgenticNovelStudio.slnx

tianming-agent-core/   # generic TypeScript AgentCore package
tianming-ai/           # model/provider boundary
tianming-novel-agent/  # novel-domain TypeScript adapter
tianming-web/frontend/ # browser UI, API client, and SSE consumers
```

The ASP.NET host is currently `old/Web/NovelAgentWeb/NovelAgentWeb.csproj` and
the solution currently lists the projects from `old/Agent`, `old/Web`, and
`old/Tests` in `old/TianmingAgenticNovelStudio.slnx`.

## Backend Ownership

- HTTP routes belong in `old/Web/NovelAgentWeb/Controllers`. Controllers obtain
  the authenticated user, bind request DTOs, call an application/service boundary,
  and translate the local result into an HTTP result.
- JSON request and response shapes belong in `old/Web/NovelAgentWeb/DTOs` or the
  relevant `Models/<Area>` directory when the host already uses that model family.
  Do not hide route contracts in `Support` or a generic helper.
- Middleware, MVC result filters, and request context behavior belong in
  `Middleware`, `Filters`, and `Services/Auth` respectively. `Program.cs` owns
  composition and dependency registration, not feature logic.
- Domain invariants belong in `old/Agent/Tianming.NovelAgent.Domain`; application
  orchestration and ports belong in `old/Agent/Tianming.NovelAgent.Application`;
  EF, persistence records, and migrations belong in `Infrastructure` or the Web
  host's existing `Data`/`Migrations` boundary.
- Background work remains with its owning Web service area. For example,
  `Services/Goals/KernelTaskWorker.cs` owns the current kernel-task worker and
  `Services/Production/ProductionOutboxHostedService.cs` owns outbox dispatch.
- Tests stay under `old/Tests` and follow the existing split: focused behavior in
  `Unit`, cross-project architecture checks in `AgentArchitecture`, and durable
  or end-to-end scenarios in `NovelAgentRegression`.

## Naming and Placement

- C# files use PascalCase names matching their public type, such as
  `KernelTaskFailurePolicy.cs`, `ApiEnvelope.cs`, and `ProjectService.cs`.
- Keep feature-specific helpers beside the feature. `Common/Helpers` is for the
  existing stateless cross-feature helpers, not a new dumping ground for services.
- Keep a new application port in `Application/Ports` and its implementation in
  the owning adapter. Do not add a database reference to a Domain project just
  to reach an existing repository.
- Add a migration in the migration set owned by the DbContext it changes. Do not
  put schema changes in controller or service files.
- When a change is only a path move, update project references and solution paths
  explicitly and leave namespaces and business behavior unchanged.

## Real Examples

The following paths are current examples of the boundaries described above:

- `old/Web/NovelAgentWeb/Controllers/NovelAgentApplicationController.cs`
- `old/Web/NovelAgentWeb/DTOs/ApiEnvelope.cs`
- `old/Web/NovelAgentWeb/Services/Goals/KernelTaskWorker.cs`
- `old/Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs`
- `old/Agent/Tianming.NovelAgent.Domain/Production/KernelTask.cs`
- `old/Agent/Tianming.NovelAgent.Infrastructure/Persistence/EfAgentControlStore.cs`
- `old/Tests/Unit/Middleware/GlobalExceptionMiddlewareTests.cs`

## Wrong vs Correct

Wrong: create `old/Web/NovelAgentWeb/Services/Utils/WorkflowService.cs` and let
the controller update EF entities directly. That mixes routing, persistence, and
domain ownership and bypasses the existing application ports.

Correct: place the route in `Controllers`, keep the request/response contract in
`DTOs`, call the existing application/service boundary, and put a targeted test
under `old/Tests/Unit` or the matching regression project.

Before using a future migration path, check the promotion task and the actual
filesystem. Do not make a path rule from a PRD alone.
