# 技术设计：PostgreSQL Novel Agent Control Plane

## 1. Ownership map

```text
AgentControlDbContext
  writes Goal / Revision / Production / Batch / Graph / Task /
  Conversation / Checkpoint / Stream / Lease / DomainEvent / Outbox

NovelAgentDbContext
  legacy-compatible reads only where explicitly allowed

Canon/Chapter write owner
  owns Canon records; cross-context changes use outbox handshake
```

兼容 Controller、legacy service 和 Worker 都依赖 Application command interface；任何 adapter 不能直接使用 DbContext 写共享控制表。

## 2. Schema groups

### Shared control tables

保留现有共享表的语义和列兼容性，使用明确 ownership：`creative_goals`、`goal_revisions`、`book_productions`、`production_batches`、`task_graph_versions`、`kernel_tasks`、`domain_events`、`outbox_events`。

### Agent-only tables

新增或确认：

- `conversations`、`conversation_turns`、`conversation_messages`；
- `runtime_runs`、`runtime_checkpoints`、`stream_events`；
- `task_attempts`、`task_leases`、`provider_requests`、`execution_outcomes`；
- idempotency/result and acceptance bridge records as required by child contracts。

每张租户表必须有 user/project scope、created/updated metadata、aggregate version 或 immutable sequence；大 payload 用 jsonb + content hash/source reference。

## 3. Transaction boundaries

### ConfirmGoal

```text
begin serializable
  set tenant context
  read proposal + expected version + existing confirmation result
  if result exists: return durable result
  validate active-goal policy and contract
  insert Goal, GoalRevision, Production(planned), Batch(planned), Graph, Task(planned)
  insert GoalConfirmed domain event / result
commit
on unique/serialization race:
  clear tracker
  re-read durable result
  return it if present
```

### StartProduction

```text
begin
  CAS Production/Batch planned and Goal active
  CAS root Task planned → ready
  insert unique dispatch outbox
commit
```

不在 HTTP transaction 内调用模型或等待 Worker。

## 4. RLS and predicates

- Connection/session 设置可信 tenant/user context，但 SQL 查询仍显式写 `user_id`/`project_id` predicate。
- RLS policy 覆盖 select/insert/update/delete；service role 与 migration role 分离。
- 返回 404/forbidden 语义由 Application 层统一，不能通过数据库异常泄漏资源。

## 5. State/CAS model

集中定义 transition table，禁止 Controller、Worker 和 projection 各自维护状态。每次状态迁移递增 aggregate version 并带 correlation/causation/idempotency。`planned` 与 `running` 必须以 snake_case 持久化多词状态。

## 6. Migration/compatibility rollout

1. 检查 solution 和 existing schema，再生成 Agent migration。
2. 在 staging 先迁移 legacy context，再迁移 AgentControlDbContext。
3. 开启 compatibility command adapter；旧 writer guard 保持观察模式直到所有合法 writer 被接管。
4. 验证双 context 的读/写 owner 和跨上下文 Canon handshake。
5. feature flag 切换新 ConfirmGoal/StartProduction；回滚只关闭新 command，不删除共享历史。

## 7. Test strategy

- Migration SQL snapshot/inspection tests。
- PostgreSQL integration with two DbContexts and a confirmation barrier。
- RLS tests for each CRUD path and missing application predicate。
- Transaction rollback tests for each insert group。
- Start idempotency and planned/running state tests。
- Legacy write guard tests proving no direct `SaveChangesAsync` control-plane mutation。
