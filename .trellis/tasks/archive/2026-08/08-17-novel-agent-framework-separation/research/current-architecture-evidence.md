# Current Architecture Evidence

## Project layout

- The repository currently has no solution file and has four `.csproj` files.
- `Web/NovelAgentWeb/NovelAgentWeb.csproj:1-9` is an ASP.NET Core Web project targeting `net8.0`.
- `Web/NovelAgentWeb/NovelAgentWeb.csproj:22-60` links shared source files directly from `Services/Framework/AI` and `Services/Modules`; this is compilation coupling rather than a project boundary.
- `Tests/Unit/Unit.csproj:23-25`, `Tests/NovelAgentRegression/NovelAgentRegression.csproj:28-30`, and `Tests/AgentKernelRegression/AgentKernelRegression.csproj:11-14` reference the Web project.
- Local `dotnet --info` on 2026-08-17 reports only SDK `8.0.127`; `.NET 10` is not installed.

## Existing Goal and Production control plane

- `Web/NovelAgentWeb/Data/Entities/CreativeGoal.cs:3-34` already stores project/source session, objective and criteria JSON, strategy, budget, baseline/context versions, status, aggregate version, and idempotency key.
- `Web/NovelAgentWeb/Data/Entities/GoalRevision.cs:3-17` stores revision number and affected data but lacks explicit confirmation actor/time/source metadata required by the new contract.
- `Web/NovelAgentWeb/Data/Entities/BookProduction.cs:3-22` and `ProductionBatch.cs:3-20` already represent strategy, status, chapter/batch progress, task graph, Canon branch, and acceptance actor.
- `Web/NovelAgentWeb/Services/Production/BookProductionTransitionService.cs:96-182` uses a serializable transaction and PostgreSQL advisory lock for acceptance/merge progression, but `:301-367` directly mutates Goal, Production, and Batch statuses across write boundaries.
- `Web/NovelAgentWeb/Data/Entities/OutboxEvent.cs:3-24` already includes aggregate identity, attempts, processing lease, and idempotency fields.
- `Tests/Unit/Data/TargetArchitectureModelTests.cs:9-100` covers scoped uniqueness, optimistic concurrency, task lease/idempotency, event idempotency, and Candidate authorship.

## Legacy boundary

- `Web/NovelAgentWeb/Services/Workflow/WorkflowService.cs:57-154` returns `projectionKind=legacy` and `legacy_read_only` for projects without a Goal; `:162-230` returns the Goal projection without loading MissionPlan state.
- `Web/NovelAgentWeb/Services/Goals/DefaultCommitmentAssessmentModelClient.cs:50` explicitly forbids restoring or replaying old MissionPlan, RuntimeRun, or pending tool execution.
- `Web/NovelAgentWeb/Support/AgentCore.cs:267-304` still defines the complete `AgentMissionPlan`; it must be isolated rather than described as already deleted.
- `Services/Framework/AI/NovelAgent/Services/NovelAgentOrchestrator.cs:11-52` remains a complete legacy orchestrator and is still covered by regressions.

## Reusable reliable capabilities

- `Web/NovelAgentWeb/Services/Canon/ICanonBranchService.cs:5-35` provides branch/candidate/acceptance/prefix merge contracts and an explicit merge conflict exception.
- `Web/NovelAgentWeb/Services/Canon/PrefixMergeService.cs:444-462` writes authoritative ChapterVersions.
- `Web/NovelAgentWeb/Services/Chapters/IChapterService.cs:9-88` exposes chapter and version operations; `Data/NovelAgentDbContext.cs:38` owns `ChapterVersions`.
- `Web/NovelAgentWeb/Services/Knowledge/KnowledgeService.cs:13-99` combines PostgreSQL, semantic search, usage tracking, caches, and the production truth store behind a service boundary.
- `Web/NovelAgentWeb/Services/Production/IWritingModelCompletionService.cs:26` is the existing common model completion seam; `Program.cs:271` registers its default implementation.

## Data, transport, and provider evidence

- `Web/NovelAgentWeb/Program.cs:83-89` configures PostgreSQL as the authoritative production database, while `NovelAgentWeb.csproj:79-80` still references both Npgsql and SQLite EF providers.
- `Web/NovelAgentWeb/Controllers/AgentController.cs:61-119` implements authenticated SSE replay and live streaming; the frontend consumes fetch streams in `Web/NovelAgentWeb.Frontend/src/api/index.ts:428-486`.
- `Services/Models/KernelModelConfigurationService.cs:8-45` represents provider/base URL/model/fallback configuration.
- `Web/NovelAgentWeb/Services/Production/DefaultWritingModelCompletionService.cs:108-210` uses an Anthropic branch and an OpenAI-compatible `HttpClient` path. The repository currently has no official OpenAI SDK package reference.

## Planning conclusion

The code supports reuse and incremental isolation, but it does not yet enforce assembly boundaries or unique new-control-plane writers. The design must reuse existing PostgreSQL concepts and reliable Canon/Knowledge/Chapter services through ports, while moving state transitions and persistence ownership out of Web.

