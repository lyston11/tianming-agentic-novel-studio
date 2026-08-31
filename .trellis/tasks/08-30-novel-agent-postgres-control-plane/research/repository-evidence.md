# 研究证据：PostgreSQL Control Plane

## Anchors

- `.trellis/spec/backend/database-guidelines.md:1-59`：AgentControlDbContext ownership、migration history、planned/start、bridge outbox。
- `.trellis/spec/backend/database-guidelines.md:87-103`：required PostgreSQL/migration/RLS tests。
- `.trellis/spec/backend/database-guidelines.md:173-233`：concurrent confirmation contract。
- `.trellis/spec/backend/database-guidelines.md:128-162`：worker ownership cutover 与 legacy guard boundary。
- `old/Agent/Tianming.NovelAgent.Infrastructure/Persistence/Records.cs`：历史 records/event/artifact contract 参考。
- `old/Web/NovelAgentWeb/Program.cs`：旧 Web registrations，证明现有能力不能不经审计整体搬运。

## Boundary conclusion

Database context、事务、RLS 和 control-plane event 是 Web/Application 基础设施职责；Novel Agent 与 AgentCore 只能通过 typed ports 接入。所有生产可靠性声明必须由 PostgreSQL integration evidence 支撑。
