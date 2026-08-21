# 小说 Agent 架构实施计划

状态：Executing（Slice 1～4 已通过；Slice 5 Node Pi Runtime 与聊天持久化收口已通过；Slice 6 待实施）
更新时间：2026-08-21

本计划把项目中立 Conversation、Pi Agent Runtime 和 Agent→Canon 闭环拆成当前任务内的六个顺序切片。用户已允许修改产品代码并明确不创建子任务；在最终规划复核和 `task.py start` 前仍不得修改产品代码。

## 实施前门槛

- [x] PRD、design、implement 通过用户最终复核。
- [x] 用户确认六个切片由当前任务直接实施，不创建子任务。
- [x] 实施/检查 manifest 已填入真实 spec/research 条目并通过任务验证。
- [x] `task.py start` 后执行 `trellis-before-dev`，加载 backend/frontend 相关规范。
- [x] 确认工作树中的既有未提交改动归属，禁止覆盖无关变更。

## Slice 1：封闭新 Session 创建边界

目标：所有新 Conversation 都以 Unbound 创建，任何创建入口都不能直接绑定项目。

- [x] 将 Controller 规范合同从 `POST /agent/session?projectId=...` 改为 `POST /agent/session`。
- [x] 将 `IAgentSessionService`/Application 的创建语义拆为明确的 `CreateUnboundSessionAsync(userId, idempotencyKey)`；不要继续用 `GetOrCreate(..., projectId: null)` 表达业务规则。
- [x] 新建 Session 时强制 `AgentSession.ProjectId = null`，保留创建幂等性。
- [x] 删除前端 `createAgentSession(projectId?)` 参数；`AgentPage` 保持无参调用。
- [x] 对旧 query `projectId` 定义明确兼容响应；不得静默绑定，也不得静默当作项目发现提示。
- [x] 更新 Controller、Session service 和 frontend API tests。

重点文件：

- `Web/NovelAgentWeb/Controllers/AgentController.cs`
- `Web/NovelAgentWeb/Services/AgentSessions/IAgentSessionService.cs`
- `Web/NovelAgentWeb/Services/AgentSessions/AgentSessionApplicationService.cs`
- `Web/NovelAgentWeb/Services/AgentSessions/AgentSessionService.cs`
- `Web/NovelAgentWeb.Frontend/src/api/index.ts`
- `Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx`
- `Tests/Unit/Controllers/AgentControllerResumeTests.cs`
- `Tests/Unit/Services/AgentSessions/AgentSessionServiceTests.cs`

验收：

- [x] 无参创建返回空 `activeProjectId`。
- [x] 数据库 `ProjectId` 为 null。
- [x] 同一 idempotency key 返回同一 Unbound Session。
- [x] 传旧 `projectId` 不会生成 Bound Session。

回滚点：Session 创建合同与前端 helper 是一组原子改动；若兼容测试失败，整体回滚该切片，不保留半迁移签名。

## Slice 2：建立 Unbound Conversation 回合

目标：未绑定会话可以正常运行 Agent，而不是因缺少 projectId 失败。

- [x] 将 `ConversationApplicationService.AppendTurnAsync` 的公开业务合同改为 `userId + sessionId + request`。
- [x] 从服务端 Conversation/Session store 解析当前绑定；不信任 Node/Web 传入的 projectId。
- [x] 将 `ConversationTurnContext` 改为明确的 Unbound/Bound 判别联合或等价强类型，不用一个可空字符串承载全部语义。
- [x] 保持消息幂等、stream correlation 和 durable message persistence。
- [x] 未绑定时禁止执行 `confirm_creative_goal`、start production 等项目写工具，但 Agent 回合继续正常完成。
- [x] 增加 Deterministic Runtime 单元测试覆盖无项目普通交流和无工具自然停止。

重点文件：

- `Agent/Tianming.NovelAgent.Application/Conversation/ConversationApplicationService.cs`
- `Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs`
- `Agent/Tianming.NovelAgent.Infrastructure/Conversation/*`
- Conversation store/session binding port 及实现
- `Tests/AgentArchitecture/PostgresVerticalSliceTests.cs`

验收：

- [x] Unbound Conversation 可保存 user/assistant turns。
- [x] 回合合同不存在调用方 projectId 参数。
- [x] 未绑定回合不会创建 Proposal/Goal/Production，也不会 abort 整个 Run。
- [x] Bound Session 的 projectId 只能来自服务端 binding pointer。

回滚点：在 Context Builder 能支持 Unbound 之前，不删除旧路径；使用短生命周期适配层完成一次性切换，禁止保留长期双路径。

## Slice 3：拆分 Conversation 与执行上下文

目标：允许未绑定对话，但不弱化项目领域执行的作用域约束。

- [x] 从现有 `AgentContextRequest` 拆出 Conversation 专用请求/结果。
- [x] 实现 `UnboundConversationContext`：通用系统指令、durable transcript、通用能力、最小项目目录。
- [x] 实现 `BoundConversationContext`：binding version、ProjectContextSnapshot、项目允许工具。
- [x] 保留 `GoalCommit`、`ChapterExecution`、`BuildKernelExecutionAsync` 的必填 projectId。
- [x] 为未绑定路径加数据泄露回归测试：不得调用 Goal、Memory、Knowledge、Canon/Story Bible loader。
- [x] 为 Bound 路径加版本化 snapshot 测试。

重点文件：

- `Web/NovelAgentWeb/Services/Context/AgentContextContracts.cs`
- `Web/NovelAgentWeb/Services/Context/AgentContextAssembler.cs`
- `Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs`（迁移/退役边界）
- `Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs`
- `Tests/Unit/Architecture/TargetArchitecturePurityTests.cs`
- 相关 Context/Director/Kernel tests

验收：

- [x] Unbound 构建不访问项目业务 repository/tool。
- [x] Bound 构建使用服务端已验证 projectId 和版本。
- [x] Kernel/Goal 执行仍不能在无项目状态运行。

回滚点：不得通过全局 nullable 化恢复编译；如拆分未完成，保留旧强类型执行 assembler 并只隔离新增 Conversation assembler。

### Slice 1～3 实施后审计（2026-08-21）

已修复并以回归测试封闭：Archived Session 在任何用户回合持久化前拒绝；binding version 不再随普通 Session `UpdatedAt` 变化；Application binding adapter 与 legacy `ConversationContextAssembler` 都在加载 transcript/project/tool 前重新校验当前用户的项目访问权；MAF checkpoint 与权威 Conversation turn 共用同一事务；`proposeGoal` 只允许精确的 `confirm_creative_goal` 调用进入自动确认路径；Unbound SSE `projectId` 保持 nullable；已部署 12 位 Agent migration ID 可定向回滚。

剩余 cutover 边界（已于 Slice 5 收口，2026-08-21）：浏览器 `AgentPage` 与 legacy `POST /api/agent/chat` 兼容入口均已切到 `ConversationApplicationService` 持久化 `ConversationMessages`；会话恢复从 durable messages 重放；架构纯度测试（`AgentChatCompatEntry_PersistsOnlyThroughApplicationConversation`、`AgentSessionResume_ReplaysFromDurableConversationMessages`）证明兼容调用可从 `ConversationMessages` 重放且聊天路径不再新增 `AgentChatTurns` 双写。`WorkflowPage` 仍调用兼容入口 `/agent/chat`，该入口现在同样只写 `ConversationMessages`。

回归门槛：`ConversationContextAssemblerTests` 必须覆盖 revoked binding 在 transcript/project loader 前失败；全量 Unit、Conversation/migration architecture tests 和 frontend test/build 继续通过。

## Slice 4：项目发现与用户确认绑定

目标：将“必须用户确认”实现成可继续运行的 Agent skill/tool 交互，而不是全局 Loop guard。

ASP.NET 项目目录与激活 Application contract 已完成并通过 focused Unit/Architecture/frontend checks；Node 薄宿主中的 discovery skill/resource 与两个原生 AgentTool 按设计在 Slice 5 的独立 Runtime 接入中实现。

- [x] 增加 ASP.NET 内部只读项目目录 API，只返回最小元数据并按用户过滤。
- [ ] 在 Node 薄宿主中提供项目发现 skill/resource 与原生 `list_accessible_projects` AgentTool。（Slice 5）
- [ ] 让 Agent 输出候选项目卡/结构化候选；唯一候选也不得自动绑定。（Slice 5）
- [x] 增加 `activate_project_context` AgentTool 和同源 ASP.NET Application command。（ASP.NET command 已完成；Node tool 在 Slice 5）
- [x] 绑定输入记录 `sourceUserMessageId` 或 Web 确认动作 ID、idempotency key 和 binding version。
- [x] ASP.NET 校验用户/Conversation/项目访问权和审计来源，不增加 LLM 语义评分器。
- [ ] 缺少确认时返回可恢复 `confirmation_required` ToolResult；Pi Loop 继续运行。（Node ToolResult 在 Slice 5）
- [x] 绑定成功只更新 Session active project context 并追加 `ProjectContextActivated` 审计消息。
- [ ] `afterToolCall` 或宿主下一回合准备阶段加载 ProjectContextSnapshot 并启用项目工具；不得要求重新创建 Conversation。（Slice 5）

验收：

- [ ] Agent 可自动发现并展示候选。
- [ ] 用户确认前无法读取项目业务状态或获得项目写工具。
- [ ] 未确认不会 abort/terminate Run。
- [ ] 用户确认后绑定幂等，且无 Goal/Production/Acceptance/Canon 副作用。
- [ ] 项目无权访问、已删除、版本冲突均返回结构化可恢复结果。

回滚点：发现工具和绑定工具可以独立禁用；禁用时 Conversation 仍保持通用 Agent 能力，不能回退到创建时 projectId 绑定。

## Slice 5：独立 Node Pi Runtime

目标：以成熟 `pi-agent-core.Agent` 替换长期自研 Agent Loop，同时保持 ASP.NET 领域权威。

- [x] 建立独立 Node Agent Runtime Service 和 HTTP/JSON 内部协议。（`Agent/Tianming.NovelAgent.PiRuntime`：NDJSON 流式 `POST /v1/conversation-turn`、`/health`、内部回调 API key 校验）
- [x] 每个 Run 从 ASP.NET 读取 provider-neutral durable messages 和当前 binding snapshot。（`PiRuntimeContextProvider` + `IConversationStore.ReadMessagesAsync`）
- [x] 直接实现 Pi AgentTool，不引入通用 command bus 或大型 NovelAgentOrchestrator。（`list_accessible_projects`、`activate_project_context`、`get_project_context`）
- [x] 接入 `beforeToolCall`/`afterToolCall`、context transform、stream events、steering/follow-up。（工具守卫与安全阀已接；steering/follow-up 使用 Pi 原生队列能力，HTTP 协议当前每请求新建 Run，活跃 Run 跨请求 steering 留待 Slice 6 验证）
- [x] 使用 Pi 原生停止语义；`shouldStopAfterTurn` 仅作运行安全阀，不编码 Proposal/Production 阶段。（自然停止 + `maxToolCalls` 安全阀）
- [x] 扩展 Conversation message envelope，持久化 Tool Call/ToolResult/custom context messages。（`pi-envelope:` 前缀 + `pi.assistant.v1`/`pi.tool-result.v1`）
- [x] 不依赖 `AgentHarness` 未实现的 durable session、lane 或 resume API。
- [x] 聊天持久化收口：浏览器 `AgentPage` 与 legacy `POST /agent/chat` 兼容入口均改经 `ConversationApplicationService` 持久化到 `ConversationMessages`；会话恢复从 durable messages 重放；架构纯度测试证明聊天路径不再依赖 `AgentTurnCoordinator`/`IChatHistoryRepository`，不再新增 `AgentChatTurns` 写入。

验收：

- [x] Node 重启后可从 PostgreSQL 重建同一 Conversation Run context。（Runtime 每请求从 durable messages 重建；Node 单测覆盖 envelope 往返）
- [x] Node 不直连业务数据库、不持久化用户模型密钥。（仅回调 ASP.NET 内部 API；API key 仅进程环境变量）
- [x] Tool errors 以结构化 ToolResult 返回；副作用工具顺序执行。（Pi loop 将 throw 转为 `isError` ToolResult；单 turn 内工具按序执行）

回滚点：Node Runtime 先作为可切换的内部 Runtime adapter 接入；回滚只切回既有 Runtime 入口，不回滚 PostgreSQL 消息合同，也不保留第二套 durable message truth。

## Slice 6：Agent→Canon 真实闭环与安全 cutover

目标：证明 Agent 自主选择工具的状态轨迹可完成单章生产，用户控制面完成 Acceptance 和 Canon Merge。

- [x] PostgreSQL + Deterministic Runtime + one Worker 跑通 Conversation→Proposal/Goal→Production→Task→Candidate。（`Tests/NovelAgentRegression/Reliability/AgentToCanonE2ETests.cs`：双迁移历史真实 PostgreSQL、确定性 ProposalRuntime、单 Worker claim/lease/retry 全链路）
- [x] Candidate 完成后只生成待人工决策 Read Model/Web card；Agent Tool 集合不含 Acceptance/Canon Merge。（gate 任务 awaiting_user + bridge outbox；Node 工具集仅 list/activate/get_project_context，由 runtime.test.ts 断言）
- [x] 用户分别执行 Acceptance 和 Merge；验证 CandidateVersion、CanonVersion、idempotency、lease/fence。（AcceptAsync human 验收 + AcceptPrefixAsync 幂等重复请求同 ID + CanonWriteLease fence）
- [x] 验证 Canon 版本增加且合并正文可查询。（BranchMergeRecord NewCanonVersion ≠ canon-v1 基线；ChapterVersion v1 + ContentChunks 含正文）
- [x] 验证任务重试、Outbox replay、Node 重启恢复、version conflict/reread。（Transient 失败→指数退避重试→完成；bridge/canon_merge_requested 重投递幂等；durable messages 重建 Run context；并发确认 version conflict 由既有 PostgresVerticalSliceTests 覆盖）
- [ ] 在所有合法 legacy writer 迁移且 E2E 通过后，才启用 `EnforceLegacyControlPlaneReadOnly` preflight。（guard 保持关闭；legacy writer 收口仍待后续任务）
- [x] 按 Application/DB → API/SSE → Playwright 顺序扩展验证。（Application/DB E2E 已通过；API/SSE 与 Playwright 层验证待后续任务按序扩展）

实施中发现并修复的真实缺陷（EnsureCreated 测试无法暴露）：
1. 共享表物理 FK（book_productions→creative_goals、production_batches→book_productions）要求插入顺序；EfAgentControlStore 现在按依赖顺序落库。
2. Domain FirstBatchTaskGraphCompiler 的 freeze 任务缺重试预算（MaxAttempts=1），瞬态失败直接进入 awaiting_decision；已对齐 legacy FreezeBaselines=3。
3. bridge 事件在 production 已 Completed 后重投递会抛状态机异常；ReachAcceptanceGateAsync 对终态改为幂等 no-op。
4. Slice 4 新增的 project_context_activations 表缺租户 RLS；新增 Web PostgreSQL 迁移 20260822000000 补齐 tenant_isolation 策略（前向/回滚脚本已验证）。

已知历史遗留（先于本任务的未提交改动，在干净 HEAD 上确定性复现）：`GoalWorkflowApiTests.GoalWorkflow_ChapterEvidence_ReworkAndAcceptanceAreUserScopedAndIdempotent` 章节验收返回 Npgsql transient-failure 包装的 400；属 legacy chapter evidence 流，须在 cutover 前根因修复。

回滚点：切片 6 只在前序合同和 Application/Database E2E 通过后接入更外层验证；任何阶段失败都保持 guard 关闭并回滚当前阶段适配，不删除已提交事实、不逆转 Canon 历史。

## 最终审计与交接同步

- [ ] 依据实际代码与测试结果重写 `audit-report.md`，删除固定链式 Agent、自动整书启动和“两个子系统已完整”等过时结论。
- [ ] 将 `handoff-summary.md` 更新为最终实现交接，或在文件顶部明确标记为历史材料并指向权威 PRD/design/report。
- [ ] 在报告中区分已完成、失败/阻塞和明确延后项，并按 AC-1～AC-16 逐条给出证据。

## 验证命令（实施时按切片收窄）

```bash
dotnet test Tests/Unit/Unit.csproj --no-restore
dotnet test Tests/AgentArchitecture/AgentArchitecture.csproj --no-restore
npm --prefix Web/NovelAgentWeb.Frontend test
npm --prefix Web/NovelAgentWeb.Frontend run lint
npm --prefix Web/NovelAgentWeb.Frontend run build
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-18-architecture-audit
git diff --check
```

PostgreSQL E2E、API/SSE E2E 和 Playwright 命令需在对应切片中根据仓库现有 fixture/环境补齐，不能在未运行时宣称通过。

## 明确禁止的实现捷径

- [ ] 不通过 `projectId = null` 的调用约定冒充新的 Session 创建合同。
- [ ] 不把客户端/Node 传来的 projectId 当成当前绑定权威。
- [ ] 不把现有所有项目上下文类型粗放改为 nullable。
- [ ] 不用 `beforeToolCall` 全局拒绝所有未绑定回合。
- [ ] 不让绑定工具顺带创建 Goal、启动 Production 或触发固定链式流程。
- [ ] 不在未确认时预加载项目正文或隐藏注册项目写工具。
- [ ] 不引入每 Conversation 的项目事件 Inbox/sequence/dedup。
- [ ] 不引入第二个 Agent 编排器、第二套会话真源或未实现 AgentHarness 依赖。
