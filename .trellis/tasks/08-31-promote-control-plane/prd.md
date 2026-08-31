# 控制面提升：durable control plane 迁出 old/ 进 tianming-web/backend

## 1. 目标与用户价值

把已经通过测试的 durable 控制面从 `old/` 冻结区搬进 `tianming-web/backend/`，成为四层架构里名副其实的 Web 层，然后**停止重建它**。

用户价值是间接但关键的：当前 `:5002` 上服生产流量的后端被文档标为 "legacy 冻结"，而规划中的新控制面（08-30 那批五子任务）要在 Node 里重建一遍 PostgreSQL 事务、RLS、Outbox、Worker lease/fence——这些在 `old/` 里已经有真库实测通过的实现。本任务把叙事和事实对齐，让后续所有小说能力开发建立在能跑的地基上，而不是并行维护两套半成品。

本任务**不改任何业务逻辑**，只改路径。它是纯粹的结构搬迁 + 路径修复。

## 2. 背景与实测证据

### 2.1 为什么"只提升 old/Agent"不可行

`old/Agent` 四项目自身干净（`net10.0`，只互相引用，不依赖 `old/Web`、`old/Services`），但控制面测试不在其中：

| 测试项目 | ProjectReference | 含什么 |
|---|---|---|
| `Tests/AgentArchitecture` | 只引 Agent 四项目 | 29 个架构约束测试 |
| `Tests/NovelAgentRegression` | `Web/NovelAgentWeb.csproj` | `AgentToCanonE2ETests`、`AgentToCanonApiSseE2ETests`、`KernelTaskClaimTests` |
| `Tests/Unit` | `Web/NovelAgentWeb.csproj` | 单元 + 架构纯度 |
| `Tests/AgentKernelRegression` | `Web/NovelAgentWeb.csproj` | `OutputType=Exe` 控制台 runner |

`AgentToCanonE2ETests.cs` 直接 `using` 14 个 `TM.Web.NovelAgentWeb.*` 命名空间（`Data`、`Data.Entities`、`Services.{Canon,Content,Goals,Auth,Production,Rework,Execution,AgentApplication,AgentRuntime,Caching,Settings,VectorStore}`）。再追这些 Services 目录，它们互相引用 26 个 Web 命名空间，外加 `Data`、`Support`、`DTOs`。Worker 与 scheduler 本身就在 `Web/Services/Goals`（21 文件 / 2798 行），不在 `Agent/` 里。

**结论：durable 控制面不是可分离模块，它是 `old/Agent`（分层骨架）+ `old/Web/NovelAgentWeb`（Data / Services / Support / Migrations）的合体。要继承测试，必须整体搬。**

### 2.2 实测基线（2026-08-31，本机 Docker + SDK 10.0.400）

| 测试套 | 结果 |
|---|---|
| `AgentArchitecture` | **29/29 通过** |
| `NovelAgentRegression` | **159/159 通过**（真 Docker + PostgreSQL Testcontainers，1m41s） |
| `Unit` | **821/837，16 失败** |
| `AgentKernelRegression` | `dotnet test` 不执行（Exe 控制台 runner） |

那 16 个失败**全部**在 `Tests.Unit.Architecture.TargetArchitecturePurityTests`，根因单一：

```
System.IO.DirectoryNotFoundException : Repository root not found.
  at TargetArchitecturePurityTests.RepositoryRoot() in .../TargetArchitecturePurityTests.cs:line 258
```

`RepositoryRoot()` 向上查找同时满足 `README.md` 存在 **且** `Web/NovelAgentWeb/` 存在的目录。2026-08-22 那次 `git mv` 把 Web 移入 `old/` 后，`old/` 有 `Web/NovelAgentWeb` 但无 `README.md`，仓库根有 `README.md` 但无 `Web/NovelAgentWeb`，两条件再无法同时成立。**这 16 个失败是既有缺陷，不是本任务引入的**；journal 里记的 `Unit 837/837` 是 08-22 重构之前的数字。

### 2.3 规模

`old/Web/NovelAgentWeb`：566 个 `.cs` / 247,488 行。分区：`Migrations` 72 文件 / 33,803 行，`Services` 280 / 51,187，`Data` 89 / 5,516，`Support` 21 / 6,878，`Controllers` 18 / 4,348，`DTOs` 13 / 1,798。

### 2.4 已知跨界点

`old/Web` 有 4 个文件 `using TM.Services.Framework.AI.NovelAgent.Models`（指向 `old/Services`）：`Services/Canon/PrefixMergeService.cs`、`Services/Canon/ContinuitySummaryExtractor.cs`、`Services/Content/IProjectContentQueryService.cs`、`Services/Content/ProjectContentQueryService.cs`。

`Infrastructure.csproj` 含 `Microsoft.Agents.AI 1.17.0`（MAF adapter，在 `AGENT_CORE_ARCHITECTURE.md` §6 的冻结/待退役清单上）。本任务只搬不删。

## 3. 范围内

- `git mv` 迁移四组目录进 `tianming-web/backend/`，保留 100% rename 历史。
- 重写 `ProjectReference` 相对路径、solution 文件、`global.json` 位置、`Scripts/dotnet` wrapper 的 `repo_root` 推导。
- 修复 3 个含硬编码仓库路径的文件：`Tests/Unit/Architecture/TargetArchitecturePurityTests.cs`、`Tests/Unit/ProgramConfigurationTests.cs`、`Tests/AgentKernelRegression/Program.cs`。
- 处理 §2.4 的 4 个 `old/Services` 跨界引用（随引用方迁入，或在 backend 内落地所需模型）。
- 更新 `CLAUDE.md`、`AGENTS.md`、`README.md`、`AGENT_CORE_ARCHITECTURE.md` 里与新布局冲突的路径与叙事。
- 前端 `:3002 → :5002` 代理保持可用，后端启动方式文档化。

## 4. 明确不做

- **不改任何业务逻辑。** 除 §3 列出的路径/引用修复外，`.cs` 文件内容不动。
- 不删 legacy（MAF adapter、`TargetArchitectureDirector`、Structured Runtime、`/agent/chat` 兼容入口）——按 `AGENT_CORE_ARCHITECTURE.md` §6 条件另起任务。
- 不实现 08-30 那五个子任务的任何新功能；本任务完成后它们降级为缺口核查清单。
- 不动 `tianming-novel-agent` 的 TS 边界收敛（另起任务）。
- 不动 `old/Services`（除 §2.4 的 4 个引用）、`old/Docs`、`old/Web/NovelAgentWeb.Frontend`、题材提示词。
- 不新增 Playwright/E2E 平台，不改 PostgreSQL schema，不写迁移。
- 不碰 `EcomGen/`、`.zcode/`。

## 5. 验收标准

- [ ] **AC-1 路径迁移完成**：`tianming-web/backend/` 下含 Agent 四项目、`Tianming.Web`（原 `NovelAgentWeb`）、四个测试项目与 solution 文件；`git log --follow` 能追到迁移前历史（100% rename）。
- [ ] **AC-2 测试不回归**：`AgentArchitecture` 29/29、`NovelAgentRegression` 159/159 在新路径下通过。任一下降即回滚。
- [ ] **AC-3 顺手修复既有缺陷**：`Unit` 从 821/837 恢复为 **837/837**——`RepositoryRoot()` 的路径不变量在新布局下重新成立（仓库根同时有 `README.md` 与 `tianming-web/backend/`）。
- [ ] **AC-4 控制台 runner 可执行**：`AgentKernelRegression` 能通过 `dotnet run` 启动并跑完（记录实际结果作为新基线，不要求全绿）。
- [ ] **AC-5 零业务改动可证**：`git diff` 中 `.cs` 的改动仅限 §3 声明的 3 个路径解析文件 + §2.4 的 4 个跨界引用；其余全部是 rename 无内容变更。
- [ ] **AC-6 后端可启动**：`ASPNETCORE_URLS=http://+:5002` 下从新路径启动成功，前端 `:3002` 冒烟通过（登录 + 一个受保护读接口）。
- [ ] **AC-7 文档与事实一致**：`CLAUDE.md` 不再出现 `.NET 8` / SQLite 三层 / `Web/NovelAgentWeb` 旧路径 / 硬编码 `/Users/lyston/PycharmProjects/`；`old/` 的定位改述为"历史快照 + 待退役"，不再声称包含在跑的后端。
- [ ] **AC-8 收口**：`task.py validate` 与 `git diff --check` 通过。

## 6. 风险与对策

| 风险 | 对策 |
|---|---|
| `old/.dotnet/` (SDK 10.0.400) 是 gitignored，不在每个 worktree 里 | 迁移后 wrapper 的 `repo_root` 指向新位置；缺 SDK 时给出明确安装指引。系统 `dotnet` 只有 8.0.127，不满足 `global.json` |
| 566 文件搬迁中途构建断裂 | 分阶段 `git mv` + 每阶段构建；先搬 Agent 四项目（自洽）再搬 Web 再搬 Tests |
| `NovelAgentRegression` 依赖 Docker，CI/他机可能无 | 记录 Docker 为该套前置条件；无 Docker 时以 `AgentArchitecture` + `Unit` 为最小门 |
| "old/ 全部冻结"叙事失效引发文档不一致 | AC-7 强制同步；该叙事本就不成立（`:5002` 一直在服流量） |
| 搬迁与 codex worktree 未提交工作冲突 | 该批工作已先落 main（见 notes.md）；迁移在 main 单线进行 |

## 7. 后续任务（不在本任务）

1. **有序退役**：按 `AGENT_CORE_ARCHITECTURE.md` §6 的四条件删 MAF adapter、Structured Runtime、`TargetArchitectureDirector`、legacy Web turn path、`/agent/chat`。每删一项跑全回归。
2. **TS 边界收敛**：`tianming-novel-agent` 只保留 Skill / Role / DomainTool / ContextProvider / Hook；`store/in-memory-store.ts`、`application/`、`domain/continuity-gate.ts` 的 durable/确定性职责归 C#。含修复当前 type-check 失败（见 notes.md）。
3. **08-30 缺口核查**：逐条核对 idempotency key、fence token、expected-version/hash、`outcome_unknown` 等语义在现有实现中的真实缺口，只补缺口，不重建。
