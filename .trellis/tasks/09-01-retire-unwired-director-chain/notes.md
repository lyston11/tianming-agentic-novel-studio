# 实施记录（2026-09-01）

## 残留物清理（同任务顺带，均为 gitignored 本地文件，无提交）

- `Tianming.Web/novelagent.db`（Jun 9 预迁移 SQLite 快照；`old/backups/pre-migration-20260609-193648/` 有同会话多份副本）、空 `novel_agent.db`、陈旧 `publish/` 输出。`.gitignore:54,64` 已覆盖，防止回流。

## 删除清单（7 个跟踪文件 + 3 处注册 + 2 个 catch）

- 生产：`TargetArchitectureDirector.cs`（197L）、`AgentForegroundTurnRunner.cs`（62L，含两组全仓零消费者的类型 `IAgentInterruptDecisionService`/`IAgentBackgroundRunReadinessGate`）、`AgentTurnCoordinator.cs`（59L）、`AgentChatIdempotencyService.cs`（258L，含两个异常类型）。
- `Program.cs`：`:206` Director、`:348` 幂等服务、`:397-398` Runner+Coordinator 注册。
- `GlobalExceptionMiddleware`：`AGENT_CHAT_IN_PROGRESS`/`AGENT_CHAT_IDEMPOTENCY_CONFLICT` 两个 catch（thrower 已删）。

## 保留

- `AgentChatRequestReceipt` 实体、`DbSet`、全部迁移与 snapshot（PRD AC-2；删表属独立数据迁移决策）。
- `ICommitmentAssessmentService`（GoalWorkflowController 在役）、`IConversationContextAssembler`（PiRuntimeContextProvider 在役）。

## 测试数学（AC-3，843 → 834，非静默减覆盖）

−8（三个专属测试文件：DirectorTests 3、TurnCoordinatorRuntimeQueueTests 2、IdempotencyServiceTests 3）、−1（`ProgramConfigurationTests` 读已删 Director 文件的测试）。1:1 翻转/替换不改变数量：`Program_UsesTargetArchitectureDirectorForForegroundTurns` → `Program_DoesNotRegisterTheUnwiredDirectorChain`；purity 的 `Director_ReadsModelContextOnlyThroughContextAssembler` → `UnwiredDirectorChain_IsAbsent`（文件级 guard）；`ConversationContext_IncludesPendingRecoveryIntents` 去掉 Director 断言。

## 验证（AC-4/AC-5）

- Unit **834/834**；AgentKernelRegression `dotnet run` **6/6**（断言已翻转为"不得注册"）。
- Guard 可证伪：对 HEAD 内容，4 个文件均存在（guard 必然失败）、`Program.cs` 含 Director 注册字符串（翻转断言必然失败）。

## 发现并转卡（不在本任务范围）

**传递孤儿 `ICollaborationMemoryService`**（`Services/Memory/CollaborationMemoryService.cs`，499L）：Director 是其唯一消费者，Director 删除后零消费；但 `CollaborationMemory` 有 Postgres 迁移（`20260714050000_AddCollaborationMemory`）与 `TargetArchitectureModelConfiguration` 实体配置纠缠，且当前仍注册在 `Program.cs`。删它需与 `AgentChatRequestReceipt` 表清理同批做数据迁移决策，转独立任务。
