# 小说 Agent 新架构交接笔记

更新时间：2026-08-18

## Grilling handoff

本任务保持 `in_progress`。本次是中途换会话交接，不执行 `finish-work`、归档或 Git commit。

### Locked decisions

- 会话是唯一创作意图入口；Conversation Agent 只能形成结构化 Decision、Proposal、Goal Revision 建议或 `WorkflowActionRequest`。
- Workflow 是合同确认、生产控制、人工验收和异常恢复界面；未经 Workflow 确认不得启动 Production。
- Conversation Runtime 与 Production Workflow 是两个独立运行时，共享合同、Context、Model Gateway、Knowledge Retrieval 和 Event Envelope，但不共享执行控制权。
- Goal 管理创作合同生命周期，Production 管理确定性执行生命周期；LLM 不得决定事务提交、状态迁移、租约、重试、Task claim 或 Canon 写入。
- Conversation loop 分为长期 `ConversationSession`、单次 `AgentRun` 和内部 `ModelTurn`。新消息只在完整 ModelTurn 边界 steering；显式停止才取消当前 Run；崩溃只从完整边界恢复。
- Chat Agent 保留有限的开放 ReAct loop，但 Production handoff 是硬边界。`request_start_production` 快速返回 `accepted + productionId`，不能同步等待 DAG。
- 只有明确用户意图才创建 Proposal；普通聊天和 Agent 提示不能自动持久化 Proposal。MVP 不启用无新输入的通用 followUp，也不因 Workflow Event 自动唤醒隐藏 Agent。
- 长任务沿用同一 ConversationSession，使用多个短 AgentRun 和持久 Workflow Event；Workflow UI 是完整进度与操作主界面，Conversation 只显示摘要和关联入口。
- Workflow 仅以异步、粗粒度 `preview/start/status/pause/resume/cancel/accept/revise` capability 暴露给 Conversation Agent；DAG 节点、Worker、租约和 Canon merge 不注册为普通 Conversation 工具。
- 保留 `read/write/search/sql/http` 稳定原始工具合同，但必须经过虚拟资源、用户作用域、风险策略、Operations 端口和五步执行管道；`sql` 永远只读，`write` 不得修改权威控制状态。
- 用户上传、受控 HTTP 和 Agent 研究统一进入 `RawSource/ResearchArtifact -> KnowledgeCandidate -> promotion -> RAG`；权威 Knowledge/Memory 的推广、修改、归档和删除必须用户确认。
- 使用 C# / `.NET 10 LTS` 模块化单体，拆分 Domain、Contracts、Application、Infrastructure；Web 只作 Composition Root、REST/SSE Host 和 Legacy Adapter。
- PostgreSQL 是唯一权威真源；Redis、Qdrant、全文和 embedding 索引只保存可重建派生数据。
- Provider-neutral Model Gateway 隔离官方 OpenAI SDK；MAF 隐藏在可替换的 `IConversationAgentRuntime` adapter 后。任何第三方框架均不拥有 Goal、Production、Task、Knowledge 或 Canon 状态。
- 单章、交互批次和整书模式复用同一 Typed DAG/状态机；首个真实验收纵切面是一个交互批次。
- 遗留恢复只读取正式章节、人物、世界观、伏笔、有效知识和已确认决定；旧 MissionPlan、RuntimeRun 和 pending tool 不续跑。
- Conversation SSE 与 Workflow SSE 分流并共享稳定 Envelope；业务事件持久化并可重放，Token delta 是 transient。
- 目标写入所有权已锁定：`AgentControlDbContext` 最终拥有新 Goal/Production/Task/Artifact/事件写入；旧 Worker 通过 `NovelAgentDbContext` 写 `kernel_tasks/outbox_events` 只是尚未退出的迁移期例外，不是长期双写决策。

### Evidence found

上一轮已记录、但本次未能重跑的 .NET 证据：

- `.NET 10` solution warnings-as-errors build：0 warning / 0 error。
- `AgentArchitecture`：16/16。
- `NovelAgentRegression`：156/156。
- AcceptanceGate bridge 已有 PostgreSQL 集成证据：旧 scheduler 在同一事务写 task 状态和 bridge Outbox；consumer 重新校验 user/project/goal/graph/task/dependency/batch/branch；重复投递不重复推进 Production。
- Production 多词状态持久化为 `awaiting_acceptance`、`merging_canon` snake_case。
- Legacy snapshot/recovery 和 API ownership 检查已有单元/回归测试。

本次交接重新运行的证据：

- `npm --prefix Web/NovelAgentWeb.Frontend test`：7/7 通过。
- `npm --prefix Web/NovelAgentWeb.Frontend run lint`：通过。
- `npx tsc -b --pretty false`（frontend 工作目录）：通过。
- `npx vite build`（frontend 工作目录）：通过；Vite 报告主 JS chunk 524.59 kB 的非阻塞体积警告。
- `git diff --check`：通过。
- `dotnet --version`：未进入构建，因 `global.json` 要求 `10.0.400`，本机仅安装 `8.0.127`。因此本次不能把旧 .NET 通过数当作已重验结果。

关键证据位置：

- `Agent/Tianming.NovelAgent.Infrastructure/Persistence/AgentControlDbContext.cs`
- `Web/NovelAgentWeb/Services/Goals/PostgresKernelTaskScheduler.cs`
- `Web/NovelAgentWeb/Services/AgentApplication/NovelAgentOutboxHandler.cs`
- `Web/NovelAgentWeb/Data/Interceptors/LegacyControlPlaneWriteGuard.cs`
- `Agent/Tianming.NovelAgent.Application/Production/ProductionApplicationService.cs`
- `Tests/AgentArchitecture/PostgresVerticalSliceTests.cs`
- `Tests/NovelAgentRegression/Reliability/KernelTaskClaimTests.cs`

### Unresolved frontier

1. Worker ownership cutover 的具体形态：直接将 claim/complete/fail/renew 与 claim SQL/migration 迁入 `AgentControlDbContext`，还是批准一个独立、可审计、有退出条件的迁移期 worker adapter。
2. 哪些粗粒度 Workflow 命令必须再次人工确认，以及首批 CapabilityPack 的精确工具清单。
3. Production 是否只能选择版本化模板和参数、由确定性编译器生成 DAG；当前推荐保持该边界，不允许 Agent 自由生成拓扑。
4. Chat Loop 的 ModelTurn、工具批次、时延、预算和上下文安全阀精确数值。
5. 原始工具虚拟 URI、只读 SQL view 清单、write staging 资源类型、HTTP 出站策略和风险等级。
6. Draft Knowledge 的可见范围、Production 可用的权威级别，以及归档/物理删除后的派生索引重建合同。
7. `AcceptPrefix` 并发唯一键冲突如何统一返回既有 request-result。
8. SSE 过期游标稳定错误、Playwright 浏览器 E2E、Redis/Qdrant 清空重建和真实数据迁移演练。
9. 真实 Worker -> Candidate -> 双审 -> 人工验收 -> Canon merge -> Workflow projection 的完整 Testcontainers/浏览器证据，以及 Outbox/lease/merge 崩溃恢复矩阵。

### Blocked dependencies

- 当前机器缺少 `.NET SDK 10.0.400`，阻塞本会话重新运行 solution build、Unit、AgentArchitecture、NovelAgentRegression 和 AgentKernelRegression。
- `EnforceLegacyControlPlaneReadOnly` 默认关闭。直接启用会阻断仍依赖旧 context 的 scheduler、failure transition 和验收路径。
- 旧 `PostgresKernelTaskScheduler` 仍通过 `NovelAgentDbContext` 写共享 `kernel_tasks/outbox_events`，因此 AC-8 的单一写入所有权尚未成立。
- 仓库没有 Playwright 配置，AC-5 的浏览器纵切面尚无可执行测试入口。
- 在 Worker ownership、guard cutover、真实单批 E2E 和关键故障注入完成前，任务不得宣称完成或归档。

### Recommended next question

是否批准按推荐方案直接完成 Worker ownership cutover：把 task claim/complete/fail/renew、失败状态推进和 claim SQL/migration 迁入 `AgentControlDbContext`，然后默认启用 legacy write guard？

若不批准直接迁移，需要明确批准迁移期 worker adapter 的允许写集合、审计证据、启用条件和退出日期；该方案仍不能作为 AC-8 的完成证据。

### Next implementation step

1. 在具备 `.NET SDK 10.0.400` 的环境先重跑 solution warnings-as-errors build、Unit、AgentArchitecture、NovelAgentRegression 和 AgentKernelRegression，确认交接基线。
2. 为 Worker ownership cutover 先添加失败测试：guard 默认启用时，真实 worker 能通过新 owner claim/renew/complete/fail，并原子产生 AcceptanceGate bridge；旧 context 对 Task/Artifact 写入被拒绝。
3. 将 scheduler 写路径和 PostgreSQL claim function/migration ownership 移至 `AgentControlDbContext`，删除迁移期 legacy task-write 例外，不使用 raw SQL bypass 绕过 guard。
4. 重跑上述 .NET 测试和 PostgreSQL Testcontainers，再补真实单批 Candidate/Canon/Projection E2E。

## Spec update judgment

- 应保留本次对 `.trellis/spec/backend/database-guidelines.md` 的更新：它用完整七段合同记录 shared-table migration owner、AcceptanceGate bridge、迁移期 Worker 例外和 guard cutover 门槛，属于可复用的跨层数据库约束。
- 应保留 `.trellis/spec/frontend/state-management.md` 的双 SSE/React Query 单一 server-state owner 合同；本次没有发现需要继续扩展的前端规范。
- 不应把尚未锁定的 Worker adapter 形态、CapabilityPack 列表或安全阀数值写入 spec；这些仍属于 frontier，待决策和实现证据形成后再更新。

## Suggested commit content

当前不创建 commit，也不建议以“任务完成”名义提交。等 `.NET 10` 基线重验和 Worker ownership 方案落地后，建议按可回滚边界组织提交：

1. `feat: establish modular novel agent architecture`：`Agent/` 四层程序集、solution/toolchain、Domain/Application/Infrastructure 合同与架构测试。
2. `refactor: cut over kernel task write ownership`：scheduler/claim function/migration owner、legacy guard 默认启用、AcceptanceGate bridge 与故障/租约回归测试。
3. `feat: expose novel agent workflow APIs and streams`：Application-only Web API、双 SSE、前端 Workflow 接线和前端合同测试。
4. `docs: sync Trellis handoff and executable specs`：本任务的 PRD/design/implementation/notes，以及 backend database 和 frontend state-management spec。

如果这些改动在编译上不可独立，至少保证每个 commit 都有对应验证命令和明确回滚点；不要把生成的前端 assets 与其源代码拆到不同提交。
