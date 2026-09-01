# 研究笔记：PostgreSQL Novel Agent Control Plane

## Confirmed repository/spec evidence

- `.trellis/spec/backend/database-guidelines.md` 指定 `AgentControlDbContext` 为新控制面写 owner，legacy context 不能双写。
- 新控制表包括 Goal/Revision/Production/Batch/Graph/Task/DomainEvent/Outbox；Conversation/checkpoint/stream/lease 是 Agent-control-only tables，并需 tenant RLS。
- Confirmed production/root task 必须为 `planned`；显式 `ProductionApplicationService.StartAsync` 才进入 running/ready。
- Concurrent Proposal confirmation 必须在同一 Serializable transaction 内查重、写入；竞争失败后重读 durable result。
- 旧 `old/Web/NovelAgentWeb/Program.cs` 注册多个旧 runtime/service，但不能直接作为新写 owner；兼容入口应 adapter 到 Application command。
- 当前 `tianming-web` 无新 backend 实现，实施前必须确认真实 solution/project，不能凭空写 C# 文件。

## Design decisions

- 共享表不复制 `_v2` 语义表；用明确 DbContext ownership 和 migration history 隔离。
- RLS 是 defense in depth；每个 repository 仍显式带 user/project predicate。
- Canon 是不同写 owner 时，跨上下文提交采用 outbox handshake，不共享伪事务。

## Deferred

- 具体表名/迁移号要以实际 solution 和 schema inspection 为准。
- Worker attempt/lease 字段由 Worker child 在本基础上设计，但其事务边界必须复用 AgentControlDbContext。
- 旧控制面全面切换和 `EnforceLegacyControlPlaneReadOnly` 开启需要后续独立验收。
