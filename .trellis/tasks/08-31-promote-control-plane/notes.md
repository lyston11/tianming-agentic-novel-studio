# 研究笔记：控制面提升

标记约定：**实测**＝本任务在本机运行验证；**代码证据**＝可按路径核对；**历史记录**＝文档/journal 声明，可能已失真。

## 1. 为什么开这个任务

08-30 规划了 `tianming-novel-agent-durable-control-plane` 父任务 + 5 个子任务（durable conversation、PostgreSQL control plane、worker lease/fence、review/canon/outbox、web SSE acceptance），12 条父级 AC。

**代码证据**：这 5 项要建的能力在 `old/` 里已有实现且实测通过（见 §3）。父任务 PRD §7 自己写了 "当前仓库的 `tianming-web` 没有新的 ASP.NET backend 实现"，但仍把 5 个子任务排成依赖链。

**实测**：2026-08-31 一个 codex agent 以 "根据父任务执行按顺序所有子任务" 启动，34 分钟内跑了 29 条命令、全是 `rg` 检索、零代码产出，最后一条是跨 `tianming-novel-agent tianming-web old/Agent old/Tests` 搜 `GoalProposal|ConversationTurn|ConfirmGoalCommand|...`。它在找一个不存在的可移植 host。已由用户中断。

08-18 架构审计的自评（`old-directory-architecture-audit` 之前的 `architecture-audit/research/simplified-architecture-proposal.md`）判断当前架构 "60% 正确，40% 过度工程化"，点名 5 层抽象、双 DbContext、16 个 AC、9 个 Phase。08-30 的 12 AC + 5 子任务是同一模式复发。

**本任务的立场**：要砍的是"重建控制面"这件事，不是砍领域模型。账本（伏笔/角色）+ ContextPackage 冻结 + Candidate 不可变 + 人工 Acceptance 是这个产品的护城河，必须保留。

## 2. 依赖闭包（代码证据）

`old/Agent` 四项目 `.csproj`：全 `net10.0`，只互相引用。`Infrastructure` 另有 `Microsoft.Agents.AI 1.17.0`（MAF）、`EF Core 10.0.11`、`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`、`OpenAI 2.13.0`。**不依赖 `old/Web` 或 `old/Services`**。

引用 Agent 的只有两处：`old/Web/NovelAgentWeb.csproj`（引 Application/Contracts/Infrastructure）、`old/Tests/AgentArchitecture`（引全四个）。

`AgentToCanonE2ETests.cs` 的 `using TM.Web.*`：`Data`、`Data.Entities`、`Services.AgentApplication`、`Services.AgentRuntime`、`Services.Auth`、`Services.Caching`、`Services.Canon`、`Services.Content`、`Services.Execution`、`Services.Goals`、`Services.Production`、`Services.Rework`、`Services.Settings`、`Services.VectorStore`（14 个）。

`Web/Services/{Canon,Content,Goals,AgentApplication,Production,Rework,Execution}` 合计 `using` 26 个 Web 命名空间 + `Data` + `Data.Entities` + `DTOs` + `Support`。

Worker/scheduler 在 `Web/Services/Goals`（21 文件 / 2798 行）：`KernelTaskWorker.cs`、`BookProductionWorker.cs` 等。**不在 `old/Agent` 里**。

→ 控制面 = `old/Agent` + `old/Web/NovelAgentWeb`，不可分割。

## 3. 实测基线（2026-08-31，Docker running，SDK 10.0.400 via `old/Scripts/dotnet`）

| 套件 | 命令 | 结果 |
|---|---|---|
| `AgentArchitecture` | `dotnet test` | **29/29 通过**，20s |
| `NovelAgentRegression` | `dotnet test` | **159/159 通过**，1m41s（真 PostgreSQL Testcontainers） |
| `Unit` | `dotnet test` | **821/837，16 失败** |
| `AgentKernelRegression` | `dotnet test` | 无测试执行 —— `OutputType=Exe` 控制台 runner，需 `dotnet run` |

`NovelAgentRegression` 159/159 包含 `AgentToCanonE2ETests`、`AgentToCanonApiSseE2ETests`、`KernelTaskClaimTests`——即 Conversation → Proposal → Goal → Production → Task → Candidate → Acceptance → Canon 全链路、API/SSE 层、以及 Worker lease/claim 在真库上的并发语义。**这就是 08-30 五子任务想重建的东西。**

### 3.1 Unit 的 16 个失败：既有缺陷，非本任务引入

全部在 `Tests.Unit.Architecture.TargetArchitecturePurityTests`。根因单一：

```
System.IO.DirectoryNotFoundException : Repository root not found.
  at TargetArchitecturePurityTests.RepositoryRoot() in .../TargetArchitecturePurityTests.cs:line 258
```

`RepositoryRoot()`（`:251-259`）向上找同时含 `README.md` **且** `Web/NovelAgentWeb/` 的目录。2026-08-22 `4e379a24` 把 Web `git mv` 进 `old/` 后：`old/` 有 `Web/NovelAgentWeb` 无 `README.md`，仓库根有 `README.md` 无 `Web/NovelAgentWeb`。两条件永不同时成立。

**历史记录失真**：journal Session 2（2026-08-22）记 "Unit 837/837 全绿"，那是重构**之前**的数字；重构本身打破了它，未被发现。

含硬编码仓库路径的文件（3 个）：`Tests/Unit/Architecture/TargetArchitecturePurityTests.cs`、`Tests/Unit/ProgramConfigurationTests.cs`、`Tests/AgentKernelRegression/Program.cs`。

**这条是本任务的顺手收益**：新布局下 `backend/` 同时有 `global.json` 与 `Tianming.Web/`，路径不变量重新成立，16 个测试应恢复（AC-3）。

## 4. 规模（代码证据）

`old/Web/NovelAgentWeb`：566 `.cs` / 247,488 行。`Migrations` 72/33,803 · `Services` 280/51,187 · `Data` 89/5,516 · `Support` 21/6,878 · `Controllers` 18/4,348 · `DTOs` 13/1,798。

`old/Services` 跨界被引 4 处（`using TM.Services.Framework.AI.NovelAgent.Models`）：`Services/Canon/PrefixMergeService.cs`、`Services/Canon/ContinuitySummaryExtractor.cs`、`Services/Content/IProjectContentQueryService.cs`、`Services/Content/ProjectContentQueryService.cs`。

## 5. "old/ 全部冻结" 不成立

`tianming-web/frontend/vite.config.ts:15` 默认 `TIANMING_BACKEND_ORIGIN=http://127.0.0.1:5002`；`.env.example` 注明 "默认 http://127.0.0.1:5002，即 old/ 冻结后端"。08-29 前端重建 PRD 也写 "对接唯一现存后端：old/ ASP.NET NovelAgentWeb"。

→ 被标为 legacy 的 `old/Web` 是唯一在服生产流量的后端。AC-7 要求文档改述。

## 6. 两个待处理的活问题

### 6.1 `tianming-novel-agent` type-check 失败（不在本任务范围）

**实测**：codex agent 被中断在半途，但走得比表面看起来远。已完成的部分：

- `src/contracts.ts`：`GoalProposal` 增 `status`/`revisionId`/`updatedAt`，新增 `GoalProposalStatus`、`GoalProposalRevision`、`GoalProposalTransition`、`proposalContentHash`。
- `src/ports.ts`：`NovelApplicationPorts` 增 `propose`/`revise`/`reject`/`discard`/`confirmProposal` 等 9 个方法；`WorkflowProjection` 增 `proposals`。
- `src/domain/proposal-lifecycle.ts`（215 行，新文件）：**完整的纯状态转移 reducer** —— `createProposedProposal`、`reviseProposal`、`decideProposal`、`confirmProposal`、`transition`、`assertExpectedProposal`。正是 child-1 `design.md` §6 要求的"集中 reducer，避免在 Controller/Adapter 各自维护 if/else 状态表"。

未完成的只有接线：`src/store/in-memory-store.ts` 未暴露那 9 个 port 方法、projection 未带 `proposals`；`src/tools/domain-tools.ts:111` 未改为经新 lifecycle 构造 proposal。

`npm run type-check` 5 个错误（两个 worktree 一致，**无绿版本可回退**）：

- `InMemoryNovelStore` 未实现 `NovelApplicationPorts` 的 9 个新方法
- `WorkflowProjection` 缺 `proposals`（2 处）
- `domain-tools.ts:111` 构造的 `GoalProposal` 缺 `status`/`revisionId`/`updatedAt`

`npm test` 仍 **11/11 通过**——tsx 运行期擦除类型，只有 type-check 拦得住。

**处置**：留给后续任务 2（TS 边界收敛）。接线本身很小（约 30–60 行），但补它之前必须先回答一个真问题：

`src/domain/proposal-lifecycle.ts` 与 `old/Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` 现在是**同一套提案生命周期的两份实现**。这正是本任务要消除的双真源。两种出路：

- 提案生命周期归 C#（与 Goal/Production/Canon 事务同侧）→ TS 侧的 reducer 删除，`propose_goal` 工具退回纯 adapter，经 internal API 提交意图。
- 提案生命周期归 TS → 需说明它如何与 C# 的 Goal 确认事务保持单一权威，且要改 `AGENT_CORE_ARCHITECTURE.md` §2。

倾向第一条，因为 `confirmProposal` 的输出必须与 Goal/Revision/Production 的创建在同一事务边界内落地，而那个事务在 C#。`proposal-lifecycle.ts` 作为纯函数 reducer 质量不错，但纯度不能解决跨语言事务归属问题。

已按现状提交，commit message 中标注 type-check 状态。

### 6.2 环境：SDK 与包安装

- 系统 `dotnet` = 8.0.127，**不满足** `global.json` 的 `10.0.400`。必须走 `Scripts/dotnet` wrapper + 项目本地 `.dotnet/`（gitignored，非每个 worktree 都有）。
- **实测**：`npm install --registry=https://registry.npmmirror.com` 会静默产出空的 `node_modules/@types/node/` 空壳目录，导致 `TS2688: Cannot find type definition file for 'node'`。需 `--force` 显式重装该包。
- `tianming-agent-core` 现在 `main` 指向 `dist/`（gitignored），下游 `tianming-novel-agent` 解析前必须先 `npm run build` 上游。链路：`tianming-ai` install+build → `tianming-agent-core` install+build → `tianming-novel-agent` install。
- **实测基线**（补齐后）：`tianming-ai` 3/3、`tianming-agent-core` 10/10、`tianming-novel-agent` 11/11。

## 7. 命名决定

`NovelAgentWeb` 目录改名 `Tianming.Web`，但**程序集名、RootNamespace、`TM.Web.NovelAgentWeb.*` 命名空间不动**。改命名空间会波及 566 个文件，等于业务代码改动，违反 AC-5。目录名与命名空间暂不一致，留给退役任务统一。

## 8. 不做的事（避免任务漂移）

不删 legacy、不改 schema、不写迁移、不动 `old/Services`（除 §4 四处引用）、不动 TS 包边界、不新增 E2E 平台、不碰 `EcomGen/` 与 `.zcode/`（后两者是误入仓库的第三方项目与编辑器配置，未提交）。
