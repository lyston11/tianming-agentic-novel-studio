# 小说 Agent 新架构实施计划

> 状态标记：`[x]` 已有代码与测试证据；`[~]` 已有部分实现但仍有验收缺口；`[ ]` 尚未完成。

## 本轮交接状态（2026-08-18）

### 已完成

- Phase 0：`global.json`、solution、项目 `net10.0` 升级和依赖锁定已完成；上一轮 warnings-as-errors build 通过。当前交接环境缺少 `.NET SDK 10.0.400`，需在下一可用环境重验。
- Phase 1：Domain / Contracts / Application / Infrastructure 项目骨架、依赖护栏和架构测试已完成。
- Phase 2：核心合同、状态机、Typed DAG、三种 ProductionMode 合同和基础测试已完成。
- Phase 3：Proposal/Goal/Production 应用命令、唯一写入口、Conversation/Workflow SSE 合同、ownership adapter 和 recovery boundary 已建立。
- Phase 4 的 migration owner、双 DbContext 启动顺序、Outbox/StreamEvent 基础模型和 AcceptanceGate bridge 已建立。
- Phase 8 的 legacy snapshot/recovery、`LegacyControlPlaneWriteGuard` 类型和回归测试已建立，但默认启用门槛尚未满足。

### 部分完成或未完成

- Phase 4/6：真实 Worker 仍由旧 Web scheduler 通过 `NovelAgentDbContext` 写共享 `kernel_tasks/outbox_events`；新控制面尚未拥有完整 claim/complete/fail/renew 写路径。
- Phase 5：Provider-neutral gateway 和 deterministic runtime 已有；真实 OpenAI Responses/MAF 全链路兼容证据未齐。
- Phase 6：AcceptanceGate bridge 已闭环，但 Candidate、双审、人工验收、Canon adapter 的完整真实单批 E2E 尚未闭环。
- Phase 7：API/SSE/前端接线已完成主要路径；仓库没有 Playwright 配置，SSE 过期游标语义和浏览器 E2E 未完成。
- Phase 9：上一轮 build、架构测试、NovelAgentRegression 通过；本次前端 test/lint/type-check/build 与 `git diff --check` 通过。.NET 检查因 SDK 缺失未重跑；完整 AC-5/AC-8/故障注入证据仍缺，Trellis Check 保持阻塞发现。

### 准确的下一步

1. 先锁定 Worker ownership cutover 方案。推荐将 scheduler 的 task claim/complete/fail/renew、失败状态推进和 claim SQL/migration 迁入 `AgentControlDbContext`；若保留迁移期 adapter，必须另行批准其边界、审计证据和退出条件。
2. 在 ownership cutover 后默认启用 `EnforceLegacyControlPlaneReadOnly`，补齐旧 context 对控制面写入的全面回归测试。
3. 用 Testcontainers 串起真实 Worker → Candidate → 双审 → 人工验收 → Canon merge → Workflow projection 单批 E2E，并加入崩溃/重投/租约失效断言。
4. 补 SSE 过期游标稳定错误、Playwright 浏览器 E2E、AcceptPrefix 并发幂等和 Redis/Qdrant 清空重建验证。
5. 重新运行全量 Trellis Check；只有 AC-5/AC-8 和上述门槛通过后，才进入提交和归档。

## 实施原则

- 按纵切面提交可验证增量，但在用户要求前不创建 Git commit。
- 先建立依赖边界和失败测试，再迁移控制面；不先复制旧服务到新项目。
- 每一阶段都保持旧正式内容可读，禁止 Goal/Production 双写。
- 任何 Provider/MAF 兼容问题通过 Adapter 吸收，不修改已确认的 Domain/Application 合同。

## Phase 0：工具链与基线

- [~] `global.json` 已锁定 `.NET SDK 10.0.400`；当前交接环境只安装 8.0.127，需在具备 10.0.400 的环境重新验证。
- [x] 锁定 `.NET 10` 兼容的 ASP.NET Core、EF Core/Npgsql、OpenAI SDK、MAF、Testcontainers 和测试包版本。
- [x] Web、Agent 和现有测试项目已升级到 `net10.0`；上一轮基线构建与测试已有通过记录。
- [x] 已建立 `TianmingAgenticNovelStudio.slnx`，纳入现有与新增项目。
- [~] 已区分本次 SDK 环境阻塞与代码失败；仍需在 .NET 10 环境记录一次完整重验结果。

验证：

```bash
dotnet --info
dotnet restore <solution>
dotnet build <solution> --no-restore
dotnet test Tests/Unit/Unit.csproj --no-build
dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --no-build
dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj --no-build
```

## Phase 1：项目骨架与架构护栏

- [x] 创建 `Tianming.NovelAgent.Domain`、`Contracts`、`Application`、`Infrastructure` 项目并建立设计规定的引用方向。
- [x] 创建新架构测试项目，使用程序集/命名空间扫描锁定禁止依赖。
- [x] 添加 Composition Root 扩展，状态机和 Provider 类型未进入 Web Controller。
- [x] 以 deterministic runtime / provider adapter 测试证明替换边界不要求修改 Domain 或 Contracts。

验证：架构测试必须先失败，再在引用图正确后通过；搜索确认 Domain/Contracts 不含 `Microsoft.EntityFrameworkCore`、`OpenAI`、MAF、Web 命名空间。

## Phase 2：Domain 与 Contracts

- [x] 实现强类型 ID、`GoalContract`、不可变 `GoalRevision`、Proposal、Production、ProductionBatch、KernelTask、Artifact 和 DomainEvent。
- [x] 实现 Proposal、Goal、Production、KernelTask 的状态机和非法迁移错误。
- [x] 实现 `TaskKind` 白名单、DAG 定义、拓扑验证和三种 `ProductionMode` 编译参数。
- [x] 定义 Application 命令/查询 DTO、统一错误合同、`AgentEventEnvelope<T>` 和 SSE cursor。
- [x] 添加状态迁移、DAG 拒绝、模式复用、幂等和 protected 内容合同测试。

验证：Domain 测试不使用数据库、Web、MAF 或 Provider SDK；三个模式生成同一节点协议。

## Phase 3：Application 用例与唯一写入边界

- [x] 定义仓储、UnitOfWork、Model Gateway、Conversation Runtime、Canon/Knowledge/Chapter 和 legacy snapshot 端口。
- [x] 实现 append conversation turn、persist proposal、confirm/reject proposal 和 create Goal Revision 用例。
- [~] Production 控制与人工验收命令已建立；task claim/complete/fail/renew 仍由旧 Web scheduler 执行，尚未收敛唯一 owner。
- [~] `AcceptPrefix -> CanonMergeRequested -> CanonPrefixMerged/Rejected` 已有幂等握手；并发唯一键冲突尚未统一返回既有 request-result。
- [x] fake-port/contract tests 已覆盖未确认不得启动、Conversation 边界和重复命令。

## Phase 4：PostgreSQL、Outbox 与 StreamEvent

- [x] 创建 `AgentControlDbContext` 和独立 migration history 所有权配置，映射现有控制表而非创建重复表。
- [x] 添加 Conversation、Proposal、Context Checkpoint、StreamEvent 及必要兼容列和索引。
- [~] 聚合版本、命令幂等、项目级唯一 Canon lease 已建立；Task/Outbox claim 仍跨新旧 owner。
- [~] Application 命令事务已覆盖主要聚合/事件路径；完整故障注入证据未齐。
- [~] durable replay、Outbox dispatcher 和部分恢复路径已实现；过期游标、完整 lease/迟到 Artifact 恢复仍缺。
- [~] PostgreSQL 集成测试已覆盖主纵切面和重复投递的一部分；并发、事务故障、RLS/ownership 和恢复矩阵未完整。

高风险门禁：migration dry-run 必须证明没有删除或重建现有 Canon/Knowledge/Chapter 表；两个 DbContext 不能同时生成同一控制表迁移。

## Phase 5：Model Gateway 与 Conversation Runtime

- [x] 实现 Provider-neutral Model Gateway、provider registry、错误分类和调用审计合同。
- [~] 已使用官方 OpenAI .NET SDK/Responses API 实现 Adapter；真实凭据/网络全链路和完整错误矩阵未验证。
- [~] 已保留旧模型能力的 adapter 边界；各非 OpenAI provider 的兼容证据未齐。
- [~] 已实现 `MafConversationAgentRuntime` 和 deterministic test runtime；真实 MAF checkpoint/tool/handoff 全链路未齐。
- [x] 已实现 ConversationStore、Context Envelope 和显式 memory promotion 边界。
- [x] replacement tests 已证明测试 Runtime 不改变 Domain/Application 合同；真实 MAF 替换仍需集成证据。

## Phase 6：单批 Production 纵切面

- [x] 实现 `FreezeContext -> Analyze -> Plan -> CompileContext -> Write -> 双审 -> AwaitAcceptance -> Merge -> Finalize` 类型图编译。
- [~] PostgreSQL worker 已可运行主要任务和 AcceptanceGate bridge；owner cutover、完整 pause/retry/failure/budget/`OutcomeUnknown` 证据未齐。
- [~] Canon/Knowledge/章节 Adapter 和作用域边界已建立；冻结版本的全链路断言未齐。
- [~] Candidate/Review Artifact 合同和持久化已建立；真实 Worker 产出到人工验收的 E2E 未齐。
- [~] Legacy Canon merge Adapter 与幂等握手已建立；基线冲突、连续前缀、人工保护和崩溃恢复矩阵未齐。
- [ ] 完成一个章节的交互批次，同时用编译/状态机测试覆盖单章与整书模式。

## Phase 7：Web、SSE 与 Workflow 投影

- [x] 新增 Conversation、Proposal、Production、Workflow API，只通过 Application/adapter 边界调用。
- [~] Conversation/Workflow 双 SSE、统一 Envelope、持久游标和 transient token 已接线；过期游标稳定错误未完成。
- [~] 前端已接入新 Workflow 主要状态和操作；Conversation/Proposal 尚未完全替代旧主流程。
- [x] Workflow read model 明确区分新 Goal/Production 与 `legacy_read_only` 投影。
- [~] API、SSE 重连/去重和 ownership 测试已有；仓库无 Playwright 配置，浏览器纵切面未完成。

## Phase 8：遗留写入隔离与恢复

- [~] `LegacyControlPlaneWriteGuard` 类型与测试已建立；配置默认关闭，尚未形成最终拒写边界。
- [~] 新 API 已走 Application；旧 MissionPlan/Orchestrator/Runtime 路由与 Worker 写路径尚未全部退为只读。
- [x] legacy project snapshot reader 只提取正式内容、有效知识和已确认决定。
- [x] 已实现“恢复为新 Proposal/Goal”，拒绝续跑 MissionPlan、RuntimeRun 和 pending tool。
- [~] 已有旧路径回归测试；guard 默认启用和真实 Worker 下的全面控制面拒写尚未验证。

## Phase 9：全量验证与 Trellis Check

- [~] 上一轮 .NET build/单元/回归/架构测试有通过记录；本次前端 test/lint/type-check/build 通过。.NET 10 与 Playwright 未在本次重跑。
- [x] `prd.md` 已建立 AC-1 至 AC-16 证据/缺口表，未把缺证据项标记完成。
- [ ] Outbox、SSE replay、Task lease、Canon merge 和 Redis/Qdrant 清空恢复的完整故障注入矩阵。
- [~] Trellis Check 已识别 Worker ownership 为阻塞发现；只有完成 cutover 和全量重验后才能清零。
- [x] 本次交接在 `notes.md` 记录验证命令、通过/阻塞项和残余风险；未提交代码。

建议的最终命令（以实际 solution/项目名补齐）：

```bash
dotnet restore <solution>
dotnet build <solution> --no-restore -warnaserror
dotnet test <solution> --no-build
npm --prefix Web/NovelAgentWeb.Frontend run build
npm --prefix Web/NovelAgentWeb.Frontend run test --if-present
npm --prefix Web/NovelAgentWeb.Frontend run test:e2e --if-present
git diff --check
```

## 风险文件与回滚点

| 范围 | 风险 | 回滚点 |
|---|---|---|
| 所有 `.csproj` / `global.json` | `.NET 10` 和包升级破坏现有运行 | Phase 0 基线必须独立可还原 |
| `NovelAgentDbContext` / migrations | 双 migration owner 或破坏现有数据 | migration SQL 审查和数据库快照 |
| Goal/Production services | 新旧双写、状态漂移 | Web 路由切换前保留旧只读查询；禁止回放到旧执行 |
| Canon merge Adapter | 部分合并或重复合并 | request id + merge record；故障注入通过后启用 |
| `Program.cs` / DI | 旧写服务仍可解析 | DI 图测试和 Legacy write guard |
| SSE/Outbox | 重复、乱序、丢业务事件 | 持久 StreamEvent replay；Redis 仅实时加速 |
| Frontend API types | 新旧 payload 混用 | Contracts 生成/集中 decoder，禁止局部强转 |

## 任务激活记录（已完成）

- [x] `prd.md`、`design.md`、`implement.md` 已完成规划并进入实施；当前任务状态为 `in_progress`。
- [x] `implement.jsonl`、`check.jsonl` 包含真实 spec/research 条目；本次 `task.py validate` 再次通过。
- [~] 依赖版本已锁定且上一轮 `.NET 10` 验证通过；当前机器缺少 SDK 10.0.400，下一会话需重验。
- [x] 工作区既有未提交修改已记录，交接未回退用户改动。
- [x] 本次交接未创建 Git commit，未执行 finish-work 或归档。
