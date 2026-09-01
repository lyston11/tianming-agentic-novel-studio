# PostgreSQL Novel Agent Control Plane

## 1. Goal and user value

把 Novel Agent 的 Goal、Production、Batch、TaskGraph、Task 和相关事件从进程内状态提升为受事务、并发控制和租户隔离保护的控制面。用户看到的状态必须来自可恢复的数据库事实；确认、启动、任务状态变化和失败不能留下半写入对象。

## 2. Requirements

### R1 — AgentControlDbContext ownership

建立明确的 `AgentControlDbContext` 作为新控制面写 owner，覆盖：

- `creative_goals`、`goal_revisions`；
- `book_productions`、`production_batches`；
- `task_graph_versions`、`kernel_tasks`；
- Conversation/checkpoint/stream/lease 等 Agent-control-only 表；
- `domain_events`、`outbox_events`。

`NovelAgentDbContext` 只能按既定兼容边界读取或调用 command adapter；不得保留同一控制面实体的双写路径。

### R2 — migrations and schema compatibility

- 使用独立 `__AgentControlMigrationsHistory`。
- 迁移不得复制语义 `_v2` 控制表或破坏性 drop/recreate Canon、Knowledge、Chapter 和已有控制表。
- 新 tenant-scoped tables 必须配置 RLS；migration connection 与 HTTP application role 分离。
- JSON payload 使用 jsonb；表/列使用 snake_case，C# record 使用 PascalCase。

### R3 — atomic ConfirmGoal

`ConfirmGoal` 必须在一个 `IAgentUnitOfWork` transaction 中完成 proposal scope/version 校验、Goal/Revision/Production/Batch/TaskGraph/Task 创建、确认事件和 idempotency result 写入。并发请求只有一个提交结果；竞争失败者重读同一 durable result。

确认后状态固定为：

```text
Goal confirmed
Production planned
Batch planned
Task planned
```

确认不调用模型、不 dispatch Worker、不把 Production 写为 running。

### R4 — explicit StartProduction

`ProductionApplicationService.StartAsync` 是唯一启动入口。它以 CAS/状态机校验 planned 状态，原子地推进：

```text
Production planned → running
Batch planned → running
root Task planned → ready
```

同时写入 dispatch Outbox。重复 Start 是幂等 no-op；终态、错误 scope、缺失 graph 或已取消 Goal 必须拒绝。

### R5 — authorization and RLS

所有 query/command 同时带 user/project/resource predicate 和当前身份上下文。RLS 作为纵深防御，不替代应用层 predicate。跨租户请求必须不泄漏资源存在性，并且无部分写入。

### R6 — optimistic versions

Goal、Production、Batch、TaskGraph、Task 和 Outbox/Domain Event 持久化版本必须可用于 CAS。冲突返回结构化 conflict；不能以最后写入覆盖较新业务状态。

### R7 — compatibility command owner

为旧 Web entry points 提供 `ILegacyControlPlaneCommands`/等价 adapter，但旧 Controller/service 不得直接调用 `SaveChangesAsync` 写控制面。Canon/Chapter 等不同写 owner 的跨上下文协作必须通过明确 command/outbox handshake。

## 3. Acceptance criteria

- [ ] A1：在真实可编译的 .NET solution 中，AgentControlDbContext 可独立迁移，history table 与 legacy context 分离。
- [ ] A2：migration SQL 不创建重复 `_v2` 控制表，不 drop/recreate 既有 Canon/Knowledge/Chapter/控制表，新租户表带 RLS policy。
- [ ] A3：ConfirmGoal 事务成功时创建一套 Goal/Revision/Production/Batch/TaskGraph/Task 和确认事件；任一校验/写入失败时无半成品。
- [ ] A4：两个独立 DbContext 并发使用同一 idempotency key 时只产生一套结果，两个调用都能读到同一 durable result。
- [ ] A5：确认后的 Production、Batch、root Task 均为 planned；只有 StartProduction 将其推进到 running/ready 并产生 dispatch outbox。
- [ ] A6：重复 Start、重复确认、过期 version、错误 project/user、取消 Goal、缺 graph/dependency 都返回可识别错误且不推进状态。
- [ ] A7：显式 user/project predicate 与 RLS 同时阻止跨租户读写；测试验证无越权副作用。
- [ ] A8：legacy compatibility entry points 不再旁路控制面写 owner；缺少 Application command 时 fail closed。
- [ ] A9：transaction、CAS、migration、RLS、serialization 和 error mapping 测试在真实或项目认可的 PostgreSQL 集成环境中通过。
- [ ] A10：文档明确 Worker lease/fence、Canon cross-context merge、Outbox relay 的未完成边界，不用内存测试冒充生产证据。

## 4. Out of scope

- Worker claim/renew/complete 的完整业务执行（由 Worker child 负责），本任务只提供可被其消费的 Task/transaction 基础。
- Review、Acceptance、Canon merge 业务规则和 Outbox relay 消费者（由 Review/Canon child 负责）。
- 完整旧数据迁移、legacy 删除、Redis/Qdrant 部署和浏览器 E2E。
- 把 AgentControlDbContext 变成 Novel Agent 或 AgentCore 的直接依赖。

## 5. Dependencies and constraints

- 依赖 Conversation/Goal child 定义的 proposal、confirmation 和 GoalRevision semantics。
- 必须先定位真实 .NET solution、EF Core 版本、现有 migration assembly 和数据库连接配置。
- 遵守 `.trellis/spec/backend/database-guidelines.md` 的 AgentControlDbContext、planned/start、RLS、Outbox 和并发确认合同。
