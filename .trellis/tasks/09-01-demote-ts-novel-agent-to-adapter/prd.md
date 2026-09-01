# Demote tianming-novel-agent to a pure adapter layer

## 1. 背景与裁决

`tianming-novel-agent`（1,676 行 TS）与 C# 后端并行持有同一套小说领域实现，零接线、零门禁。本任务把它降级为纯 adapter，消除影子领域实现。

**领域归属不是待决问题，它已经落地。** `08-30` 五个子任务于 2026-09-01 全部 `completed`，控制面、租户 RLS、fenced worker、Canon merge、SSE 均在 C# 侧实现，并有真 PostgreSQL E2E 覆盖：

| 证据 | 实测 |
|---|---|
| `AgentControlDbContext` / `KernelTask` / `CandidateChapter` / `CanonBranch` / `ProductionBatch` / `GoalProposal` | 23 / 84 / 62 / 59 / 37 / 20 个 C# 文件 |
| EF 迁移 | 61 个 |
| RLS 策略 | 10 个 `MigrationsPostgres` 文件（含 `EnableTenantRls`、`EnforceTenantRlsAndActiveTaskGraph`、`EnableBookProductionTenantRls`） |
| 真库 E2E | `AgentToCanonE2ETests.cs` 522 行、`AgentToCanonApiSseE2ETests.cs` 585 行、`KernelTaskClaimTests.cs` 473 行 |

TS 侧的对应状态：

- **零接线**：全仓无 importer；C# 无 `Process.Start`、无 Node 宿主包、无队列桥接。唯一 HTTP→Node 通路（`Program.cs:309` `PiConversationAgentRuntime` → `:4317`）指向 legacy Pi runtime，且 `appsettings.json:53` 为 `"Enabled": false`。
- **影子实现**：`src/contracts.ts` 678 行（占全包 40%）手工镜像 C# 领域类型；`src/domain/continuity-gate.ts` 62 行，对应 C# 权威的 901 行 6-gate `ChapterGatekeeper`。
- **零门禁**：`package.json` 有 `build`/`test`/`type-check`，但无 CI 执行（仓库根无 `.github/`）。要求仅以散文存在于 `.trellis/spec/backend/database-guidelines.md:234-240`。

### 已否决的替代方案

- **删除整个包**：会连带丢掉真实的 loop 与 adapter 资产；`AGENT_CORE_ARCHITECTURE.md` §7 的迁移顺序仍然成立。
- **保留为"已建未接线的目标态"**：等于再造一个 `TargetArchitectureDirector`——项目里已有的那个"已注册未接线"组件正是反复困扰的来源。重复已知错误。

## 2. 需求

### R1 — 契约真源单一化

**实测结论（2026-09-01，见 `research/contracts-classification.md`）：生成路线不成立。**

77 个导出分为四类：跨边界 35、嵌套字段 32、纯 TS 内部 2、零引用 8。对 35 个跨边界类型逐一检查 `openapi.json`（82 schema）：**命中 0 个**。

原因是结构性的：`ports.ts` 定义的是内部 Application port 契约（8 个 interface），不是 HTTP 契约。`NovelContextPackage`、`ChapterCandidateIntent` 这类概念从不经 REST 暴露，Swashbuckle 无从生成。与前端 `types.ts` 的情形不同——那里的类型确实对应 HTTP DTO。

因此改为：

- **跨边界 35 个**：保留手写，但每个须标注对应的 C# 权威类型，或明确"无对应物，属 adapter 内部概念"。精确定位须按命名空间与字段集比对——名称 grep 会产生假阳性（实测 `Acceptance` 误命中 `NovelAgentOutboxHandler.cs`、`Goal` 误命中 `GoalWorkflowController.cs`、`Production` 误命中 `ProductionEvent.cs`）。
- **嵌套 32 个**：跟随父类型，不独立维护真源。注意 `NovelCommandError`（5 文件使用）、`ReviewFinding`、`RuntimeEventType`、`CharacterState`、`ForeshadowEntry`、`CanonMergeResult`、`proposalContentHash` 在 contracts.ts 外亦有引用。
- **纯内部 2 个**（`clone`、`contextPackageContentHash`）：保留，无需 C# 对应。
- **零引用 8 个**：删除（`ConfirmGoalProposalCommand`、`DecideProposalCommand`、`GoalCommitIntent`、`GoalProposalTransition`、`ProposalRevision`、`QueryWorkflowRequest`、`RequestChapterCommand`、`ReviseProposalCommand`）。

漂移防护改由 R6 承担：断言 TS 不得声明 C# 侧不存在的领域字段，而非结构等价。

### R2 — continuity gate 权威归属

C# `ChapterGatekeeper` 的六个 gate（`ApplyHardGates` 入口 + `ApplyDesignRulesGate`、`ApplyCoreContinuityGate`、`ApplyKnowledgeBoundaryGate`、`ApplyAcceptedCreativeIntentGate`、`ApplySourceRevisionPlanGate`）是唯一权威。

`continuity-gate.ts` 不是孤立叶子：`src/application/novel-agent-application.ts:25` 调用 `runContinuityGate`，`src/index.ts:7` 对外导出。因此不能直接删除，必须先给出替代路径。二选一并说明理由：

- **降级为 preflight**：保留结构性预检（章号、空正文、context hash），但错误码词表与 C# 共享单一真源，且代码与导出注明"非权威"；
- **移除**：application 流程改为直接提交候选章，由 C# 判定。

无论哪条，都不得保留"两处各自判定、错误码互不相识"的现状——当前 TS 的 `chapter_number_mismatch` 等 code 在 C# 侧无对应物。

### R3 — 公共导出面收敛

`src/index.ts` 只导出 adapter 层真实需要的东西：loop 接线、tool 定义、context provider、event mapper、port interface。领域类型不再作为本包的公共 API 对外暴露。

### R4 — 构建产物新鲜度

`dist/` 已被 `.gitignore:39` 忽略，但比 `src/` 旧 2–3.5 小时，且仍携带 `dist/src/store/in-memory-store.{js,d.ts}`——其源码已在 commit `6564aaa9` 删除。

**把陈旧 `dist/` 当作现状读取，已在本项目造成过一次错误的架构判断。** 需要机制化保障：`build` 前清理输出目录，或在校验入口断言 `dist/` 不比 `src/` 旧。仅靠 gitignore 不够。

### R5 — 可执行校验入口

把散文要求变成单一可执行入口（如 `Scripts/verify-node-packages.sh`），跑三个包的 `type-check` + `test`。必须遵守 `file:` 依赖的构建顺序：`tianming-ai` → `tianming-agent-core` → `tianming-novel-agent`。

本任务**不**引入 CI。C# 侧同样无 CI，那是独立的基础设施决定，不应被本任务顺带决定。脚本需保留将来接入 CI 或 trellis 门禁的接口。

### R6 — 回流防护

加 guard 断言，防止领域实现回流到 TS 包：`src/` 不得出现 Canon merge、账本投影、状态迁移判定或持久化。参照现有边界规则（`.trellis/spec/backend/quality-guidelines.md:51` 限制 import 仅 `@tianming/agent-core`；`database-guidelines.md:207-208` 禁止 database/Redis/Qdrant/Pi 直连）。

### R7 — 文档修正

`AGENT_CORE_ARCHITECTURE.md` 增补一节，明确记录领域归属裁决及其依据，使后续 session 不再重复追问或误判。`CLAUDE.md` 已严重失真需同步修正：它写 .NET 8、SQLite 三层、`Web/NovelAgentWeb` 路径，而 `global.json` 是 `10.0.400`、PostgreSQL 才是真源、目录早已四层化，且含 `/Users/lyston/PycharmProjects/` 硬编码绝对路径。

## 3. 验收标准

- [ ] **AC-1** 产出 `src/contracts.ts` 逐类型分类清单（跨边界 / 纯内部），每条附 C# 对应物 `file:line` 或"无对应物"判定。
- [ ] **AC-2** 跨边界类型有单一真源与漂移检查；若判定生成产物更宽松而保留手写，须记录测量数据与理由。
- [ ] **AC-3** R2 二选一已实施；`novel-agent-application.ts` 与 `index.ts` 的引用相应更新，无悬空导入。
- [ ] **AC-4** 若保留 preflight：错误码与 C# 共享真源，且 TS 侧任一 code 在 C# 侧可被定位。若移除：application 流程有替代路径且测试覆盖。
- [ ] **AC-5** `src/index.ts` 不再导出与 C# 重复的领域类型。
- [ ] **AC-6** 陈旧 `dist/` 已清理，且有机制防止再次把陈旧产物当现状——非仅 gitignore。
- [ ] **AC-7** 单一校验入口存在、可执行、遵守构建顺序；三个包 type-check + test 全绿，并附实际输出。
- [ ] **AC-8** guard 断言存在并在领域代码回流时失败（须以刻意的反例验证 guard 真会失败，不接受仅代码审查）。
- [ ] **AC-9** 11 个 vertical-slice 测试相应调整后仍全绿；若有删减，逐条说明删除原因，不得静默减少覆盖。
- [ ] **AC-10** `AGENT_CORE_ARCHITECTURE.md` 记录裁决；`CLAUDE.md` 的 .NET 版本、目录结构、数据库真源、硬编码路径均已修正。

## 4. 范围外

- 把 TS 包接线到 C# 后端（internal API / subprocess / 队列）——独立任务，需先有部署形态决定。
- 引入 CI（含 C# 侧）。
- 删除 `TargetArchitectureDirector`、`StructuredConversationAgentRuntime`、PiRuntime、`/agent/chat`——见 `09-01-retire-wwwroot-legacy-frontend` 及 `AGENT_CORE_ARCHITECTURE.md` §6 的删除条件。
- 改动 C# `ChapterGatekeeper` 的判定逻辑。
- 重新评估 `08-30` 五个已完成子任务的产出质量。

## 5. 依赖与约束

- 不得为迎合 TS 而修改 `tianming-agent-core`（不加小说字段）。
- 不得让 TS 包直连 database/Redis/Qdrant 或 Pi 包。
- C# `ChapterGatekeeper` 六个 gate 的权威地位是本任务前提，不在此重新论证。
- `file:` 依赖要求上游包先构建出 `dist/`，任何校验脚本都必须遵守该顺序。
