# 实施计划：PostgreSQL Novel Agent Control Plane

## 0. Discovery gate

- [ ] 定位实际 `.sln`、目标 `.csproj`、EF Core 版本、migration assembly、连接配置和测试基础设施。
- [ ] 读取 Conversation/Goal child artifacts、backend database/error/quality specs、现有 `Program.cs`/DbContext/records/migrations。
- [ ] 记录共享控制表当前写入者和需要由 command owner 接管的 legacy caller；不先修改 guard。

## 1. Contracts and schema map

- [ ] 将 proposal confirmation、GoalRevision、planned/start、scope、version、idempotency 映射到 C# Application/Domain records。
- [ ] 绘制每张表的 owner、tenant columns、unique indexes、concurrency token 和 JSON/hash 字段。
- [ ] 定义 `IAgentUnitOfWork`、repositories、migration context registration 和 compatibility command boundary。

## 2. DbContext and migrations

- [ ] 建立 AgentControlDbContext 与独立 migration history。
- [ ] 配置共享表映射而非创建语义重复表；新增 Conversation/checkpoint/stream/lease 基础表及 RLS。
- [ ] 生成并审查 SQL；确认无破坏性 drop/recreate 和无 HTTP-role migration。

## 3. Application transactions

- [ ] 实现 Serializable ConfirmGoal：查重、校验、写全链、事件和结果同事务。
- [ ] 实现 StartProduction：planned→running/ready、dispatch Outbox、CAS 和重复 no-op。
- [ ] 实现 scope/error mapping、cancelled/terminal guards、legacy compatibility command adapter。

## 4. Verification

- [ ] migration/schema tests。
- [ ] two-context concurrent confirmation with barrier。
- [ ] rollback/no-partial-write tests。
- [ ] RLS + explicit predicate cross-tenant tests。
- [ ] state/CAS/idempotency/duplicate start tests。
- [ ] compatibility guard tests。

## 5. Validation and rollback

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-novel-agent-postgres-control-plane
# use the discovered solution's actual dotnet build/test/ef migration checks
```

不得启动生产数据库来“制造”通过证据；测试若需要 PostgreSQL，使用项目既有 disposable/integration harness。回滚优先关闭 feature flag 或回退未启用 migration，保留已提交的 append-only facts。

## 6. Handoff

输出给 Worker child：可 claim 的 Task 状态、CAS/transaction API、task attempt 外键位置和 dispatch outbox contract。输出给 Canon child：domain event/outbox owner、跨 DbContext handshake 和 baseline version 字段。
