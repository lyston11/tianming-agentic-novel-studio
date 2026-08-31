# 实施计划：Novel Agent Core Vertical Slice

> 本文件记录已执行的实施顺序、门禁与结果。任务已获用户明确批准并已进入 `in_progress`；以下 deferred gates 仍未在本切片中实现。

## 0. 开始前门禁（已完成）

- [x] 阅读 `.trellis/workflow.md` Phase 1.1–1.5；复杂任务确认 `prd.md`、`design.md`、`implement.md` 齐全。
- [x] 阅读 `.trellis/spec/backend/index.md` 及其 active database guide；同时检查相关目录、错误处理、质量和日志规范。未修改 ASP.NET/Web 持久化层。
- [x] 阅读根 `AGENT_CORE_ARCHITECTURE.md`、本任务 `prd.md`、`design.md`，确认未把固定业务链塞进 Core。
- [x] 任务 manifest 校验通过；用户已明确批准执行，任务状态已进入 `in_progress`。

## 1. Contracts/ports first

**输入：** PRD/设计合同、Core/AI 现有导出、Web 现有 DTO/调用者证据。

**工作：**

- 建立 Novel Agent 的 contracts：ConversationTurn、GoalProposal、ConfirmGoalCommand、Goal/Revision、Production/Batch/Task、NovelContextPackage、CandidateChapter、Review、Acceptance、CanonMergeResult、WorkflowProjection、RuntimeMessageEnvelope。
- 定义 sourceReferences、versionVector、contentHash、project/user scope、correlationId、idempotencyKey 和 expected version。
- 定义 port interface；先使用最窄接口，避免 domain tool 依赖 store 实现。
- 为状态机定义合法转换和 rejection codes。

**测试：** schema/contract 单测、canonical JSON/hash fixture、非法状态与 scope fixture。

**完成定义：** contracts 可被 adapter、fake store、测试引用；无业务对象进入 `tianming-agent-core/src/types.ts`；type-check 通过。

**依赖：** 无。

**回滚点：** 仅新增 contracts/ports；若命名与既有 Web DTO 冲突，回滚新增文件，不改 Core 公共合同。

## 2. Novel Agent adapter、role、skill、context

**输入：** 第 1 步合同、`tianming-agent-core` AgentCore API、`tianming-novel-agent/README.md`。

**工作：**

- 实现 `chapter-writer` role 与 tool whitelist/maxDepth/完成策略。
- 实现最小小说 skill/resource：章节 brief、角色状态、伏笔引用、输出格式。
- 实现 `ContextProvider`，将项目 snapshot 转成 conversation context；实现 freeze/hash 为 `NovelContextPackage`。
- 实现 DomainTool：读取项目上下文、提交 GoalProposal、请求章节任务、提交 review/acceptance intent；每个 tool 只调用 port。
- 用 `tianming-agent-core` 的 `AgentCoreTool` 适配 TypeBox schema；禁止导入任何 `@mariozechner/pi-*` 包。

**测试：** role whitelist、tool schema、缺上下文 fail-fast、跨 project scope rejection、snapshot/hash fixture。

**完成定义：** 小说层只依赖 `@tianming/agent-core`（及其受控 AI 类型），可以在 fake port 上运行；无数据库导入、无 Pi 直接导入。

**依赖：** 第 1 步。

**回滚点：** 删除 adapter/role/tool 新增代码即可恢复 README 骨架；不改 legacy Runtime。

## 3. Deterministic fake model vertical slice

**输入：** 第 1–2 步 contracts/adapter。

**工作：**

- 编写 deterministic fake `streamFn`：第一段根据 ConversationTurn 返回结构化 GoalProposal；确认后按固定 fixture 返回一个 chapter candidate 所需的 assistant/tool messages；可注入 malformed/continuity-failing/abort 场景。
- 用 fake store 实现项目、Goal、Production、Batch、Task、ContextPackage、Candidate、Review、Acceptance、Canon、projection 的最小内存持久化。
- 编排单项目单章节闭环：Conversation → Proposal → ConfirmGoal → freeze context → AgentCore → Candidate → continuity gate → awaiting acceptance。
- 将 Core event 变成 RuntimeMessageEnvelope，按 runId/sequence 进入 fake durable sink。

**测试：** 完整 happy path、未确认阻断、gate failure、模型错误、abort、Core event 映射和重放、重复 key、版本冲突。

**完成定义：** 在没有 Redis/PostgreSQL/真实 provider 时，单命令测试能跑完整 proposal-to-awaiting-acceptance 链；断言每个状态和 hash/source/version。

**依赖：** 第 2 步。

**回滚点：** fake model/store/vertical runner 独立于生产接线；失败时保留 contracts，回退 runner，不撤销 Core/AI。

## 4. Application/worker 接线与 Canon reducer

**输入：** 第 3 步通过的 vertical slice、现有 Web Application/Domain/Worker 目录和数据库规范。

**工作：**

- 在 Web/Application/Domain 边界实现 ConfirmGoal、RunChapterTask、AcceptCandidate、QueryWorkflow 的用例 port/adapter；Controller 不直接调多个服务。
- 实现 acceptance command 的 actor/project scope、candidate version、review passed、idempotency 检查。
- 实现 Canon reducer：接受后写 CanonicalChapter，并更新一个 CharacterState、一个 ForeshadowEntry 和 projection；写入/模拟同事务边界。
- 若本仓库现有后端无法在本任务中安全接线，则保留可编译 Application-facing adapter 和 contract tests，明确 PostgreSQL migration/真实 Worker deferred；不要伪造生产持久化。
- Worker 只 claim/执行 task 并提交结果命令；不直接改 Goal/Production/Batch 领域状态。

**测试：** application command tests、reducer tests、authorization/scope、idempotency、optimistic revision、非法 transition、worker result mapping。

**完成定义：** acceptance 是进入 Canon 的唯一入口；重复 acceptance 无重复副作用；所有写入拥有 correlation/idempotency/expected version。

**依赖：** 第 3 步；必须先读取 backend active specs。

**回滚点：** 使用 feature flag/新 adapter 隔离旧 runtime；不改旧写路径、不删除 old 文件。

## 5. API/SSE/frontend evidence（最小，不扩张为 E2E 基础设施）

**输入：** 第 4 步 application contracts、现有 `tianming-web/frontend/src/api`、agent/workflow 页面与 hooks。

**工作：**

- 如后端接线可编译，实现最小 HTTP DTO：conversation turn、proposal/confirm、acceptance、workflow query；沿现有 auth/scope 机制。
- 将 RuntimeMessageEnvelope/Outbox-like records 序列化为 SSE payload；客户端按 cursor/事件顺序消费，不在前端推进状态机。
- 只补足能展示 proposal、awaiting acceptance、accepted/canon projection 的现有页面或测试；不重建前端、不新增浏览器基础设施。

**测试：** API serialization/contract、SSE event order/cursor、frontend typecheck/lint/unit；浏览器 E2E 只有在已有基础设施可复用时才作为 evidence，否则写入后续任务。

**完成定义：** Web/前端只消费 application projection；刷新/重连不依赖内存 Core listener；未授权请求得到预期错误。

**依赖：** 第 4 步；前端修改还需读取 frontend specs。

**回滚点：** 由 endpoint/feature flag 隔离新路径，保留现有 UI/legacy fallback。

## 6. 全量质量检查与收敛

**输入：** 所有实现变更。

**必须运行的验证命令：**

```bash
# Core / AI（无外部服务）
(cd tianming-ai && npm test && npm run type-check && npm run build)
(cd tianming-agent-core && npm test && npm run type-check && npm run build)

# Novel Agent（若新增 package.json，使用其实际脚本）
(cd tianming-novel-agent && npm test && npm run type-check && npm run build)

# Frontend（若本任务触及前端）
(cd tianming-web/frontend && npm test && npm run typecheck && npm run lint && npm run build)

# 仓库级检查
git diff --check
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-tianming-novel-agent-core-vertical-slice
```

若实现了 ASP.NET 后端，还必须依据 backend spec 运行仓库真实的 `dotnet build`、目标测试项目和迁移/数据库 contract tests；不得在没有找到实际 solution/project 的情况下凭空写命令。

**质量门：**

- `trellis-check` 对照本任务所有受影响层的 spec、PRD、设计和实施清单。
- 检查 import graph，确保 Novel Agent 不导入 Pi、Core 不导入小说/Web。
- 检查状态写入 owner、事务/Outbox 说明、scope/idempotency/version、错误和取消语义。
- 检查 EcomGen、old/legacy 和未授权 dirty files 未被修改。
- 如发现 PRD/设计缺陷，回到 Phase 1 修改材料，不能靠代码绕过边界。

## 7. 明确不做的步骤

- 不启动 Redis、Docker Compose、PostgreSQL、Qdrant 或真实 Provider。
- 不删除 old、旧 Pi Loop、MAF/Structured Runtime、TargetArchitectureDirector 或历史迁移。
- 不修改 EcomGen。
- 不把整书生产、完整 RAG、批量调度、多 Agent、真实分布式 lease/fence/Outbox 当成本任务实现结果。
- 不提交或推送 Git；完成编码后仍需按 Trellis Phase 3.4 单独审阅提交计划。


## 8. 完成后的证据包

## 8. 完成后的证据包

本次任务已形成以下证据包：

1. `tianming-novel-agent/src/contracts.ts`、`ports.ts` 与 canonical JSON/SHA-256 hash/version 合同；
2. `roles/chapter-writer.ts`、`skills/chapter-writing.ts`、`tools/domain-tools.ts`、`context/context-provider.ts`；
3. `runtime/fake-model.ts`、`store/in-memory-store.ts` 与单章节 vertical slice 测试；
4. `Candidate → Review → Acceptance → Canon` reducer/projection 测试，包含接受、拒绝、gate failure、版本冲突和 scope 拒绝；
5. `runtime/event-mapper.ts` 的 Core event → durable message、sequence、去重和 replay 测试证据；
6. 相关 package 的 test/type-check/build 输出已记录在本任务完成记录；
7. 未实现门槛明确保留：PostgreSQL schema/transaction、Outbox relay、lease/fence、RLS、真实 provider、Playwright E2E、legacy migration/delete。

## 9. 实际执行结果（2026-08-30）

- `tianming-novel-agent`: 11 tests passed；`npm run type-check` passed；`npm run build` passed。
- `tianming-agent-core`: 10 tests passed；`npm run type-check` passed；`npm run build` passed。
- `tianming-ai`: 3 tests passed；`npm run type-check` passed；`npm run build` passed。
- `git diff --check` passed；`task.py validate` passed（`implement.jsonl` 6 entries、`check.jsonl` 9 entries）。
- 依赖边界审计通过：Novel Agent 无 `@mariozechner/pi-*`、数据库、Redis/Qdrant/Docker 直接实现；`old/` 与 `EcomGen/` 未修改。
- 未修改 `tianming-web` 前后端，因此未运行前端或 ASP.NET 构建；生产持久化、Outbox/SSE relay、Worker lease/fence/RLS、真实 provider 和浏览器 E2E 仍为后续任务。
- 全局 `.trellis/spec/` 当前为模板级规范，本任务没有发现应提升为全仓库通用规则的新约定；任务特有边界与经验已记录在 `notes.md`、`design.md` 和本完成记录中。
