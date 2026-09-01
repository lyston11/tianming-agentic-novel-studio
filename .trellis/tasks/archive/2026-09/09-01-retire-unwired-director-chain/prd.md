# 退役已注册未接线的 TargetArchitectureDirector 链

## 1. 背景与裁决

`AGENT_CORE_ARCHITECTURE.md` §6 冻结表确认：`TargetArchitectureDirector → IAgentForegroundTurnRunner → AgentTurnCoordinator` 整条链**生产不可达**（末端无消费者），仅被测试双向锁定（`ProgramConfigurationTests` 要求注册存在、`TargetArchitecturePurityTests` 要求 `AgentController` 不用它）。它是项目里"已注册未接线"组件的典型，被 demote 裁决（§9 与 `09-01-demote-ts-novel-agent-to-adapter` 的 rejectedAlternatives）明确点名为反复困扰的来源。

用户已裁决：执行冻结区退役（2026-09-01）。本任务是 §6 冻结表 4 项中第一个可解阻塞项。

## 2. 立项前实测（2026-09-01）

| 事实 | 证据 |
|---|---|
| 链注册存在但无生产消费者 | `Program.cs:206`（Director）、`:397`（IAgentForegroundTurnRunner→Director）、`:398`（AgentTurnCoordinator）；全仓 `.cs` 消费者仅测试与实现文件自身 |
| AgentChatIdempotencyService 唯一生产消费者是 AgentTurnCoordinator | grep `IAgentChatIdempotencyService`：仅 `Program.cs`（注册）、`AgentTurnCoordinator.cs`、自身；其异常 `AgentChatRequestInProgressException` 由 `GlobalExceptionMiddleware.cs:35` 捕获 |
| 链删除后幂等服务孤儿化 | 同上；保留它=再造一个"已注册未接线"组件 |
| AgentChatRequestReceipt 表有迁移历史 | 4+ 个迁移 snapshot 引用；删除表属独立数据迁移决策，本任务不做 |
| `StructuredConversationAgentRuntime` 不可删 | `Program.cs:318`，`PiRuntime:Enabled=false`（appsettings.json:53）时的现役默认会话实现，删除=产品可用性破坏 |
| PiRuntime 不可删 | 替代路径（TS adapter internal API 接线）未建，独立任务 |
| 残留物（同任务顺带清理，均 gitignored 无需提交） | `Tianming.Web/novelagent.db`（Jun 9 预迁移快照，`old/backups/pre-migration-20260609-193648/` 有同会话多份副本）、空 `novel_agent.db`、陈旧 `publish/`；`.gitignore:54,64` 已覆盖 |

## 3. 需求

- **R1** 删除生产代码：`TargetArchitectureDirector.cs`、`AgentForegroundTurnRunner.cs`、`AgentTurnCoordinator.cs`（Support/ 下两文件先读源码确认无第三方依赖）及其 `Program.cs` 三处注册。
- **R2** 删除孤儿化幂等服务：`AgentChatIdempotencyService.cs`（含 `AgentChatRequestInProgressException`）、`Program.cs` 注册、`GlobalExceptionMiddleware` 对应 catch。**保留** `AgentChatRequestReceipt` 实体、`DbSet` 与全部迁移历史。
- **R3** 测试同步：删除 `TargetArchitectureDirectorTests.cs`、`AgentTurnCoordinatorRuntimeQueueTests.cs`、`AgentChatIdempotencyServiceTests.cs`；`ProgramConfigurationTests` 的注册断言翻转为注册不存在断言；`TargetArchitecturePurityTests` 中读取 Director 文件的两处断言改为文件不存在 guard（可证伪：对 HEAD 必然失败）。
- **R4** `AgentKernelRegression/Program.cs:69-70` 的"必须使用 Director"断言翻转为"不得出现"。
- **R5** 文档：`AGENT_CORE_ARCHITECTURE.md` §6 Director 行标记已删除（含日期与任务）；`CLAUDE.md` 关键文件清单移除 `AgentForegroundTurnRunner.cs`、修正 `TargetArchitectureDirector.cs` 条目（原文"现役"与冻结表自相矛盾）。

## 4. 验收标准

- [ ] AC-1 链上 3 文件 + 幂等服务删除，`Program.cs` 相关注册（206/397/398 + 幂等注册）清零，grep 全仓无生产引用。
- [ ] AC-2 `AgentChatRequestReceipt` 实体、`DbSet`、迁移文件与 snapshot 原样保留。
- [ ] AC-3 后端 Unit 全绿，测试数学逐条记录（删 3 个专属测试文件 + 翻转的断言），非静默减覆盖。
- [ ] AC-4 `AgentKernelRegression` `dotnet run` 全绿（断言翻转后）。
- [ ] AC-5 文件不存在 guard 就位且可证伪（HEAD 上必然失败）。
- [ ] AC-6 文档两处同步完成；`task.py validate` + `git diff --check` 通过。

## 5. 范围外

- `StructuredConversationAgentRuntime`（现役默认实现）、PiRuntime（待 TS adapter 接线）、legacy Web turn path 其余写入者——各按 §6 既有阻塞保留。
- 删除 `AgentChatRequestReceipt` 表/迁移（数据迁移独立决策）。
- EcomGen、pi-agent（用户裁决不动）。
