# 研究证据：Worker Reliability

## Anchors

- `.trellis/spec/backend/database-guidelines.md:128-146`：AgentControlDbContext worker ownership、lease/fence、completion/bridge transaction 和 PostgreSQL tests。
- `old/Agent/Tianming.NovelAgent.Domain/Production/Production.cs`：Production lease/attempt/Canon write lease 语义。
- `old/Agent/Tianming.NovelAgent.Domain/Production/KernelTask.cs`：Task claim、attempt、retry、waiting human gate 和 terminal state。
- `old/Services/.../ModelExecutionRecoveryService.cs`：unknown/late result conservative recovery。
- `old/Agent/.../Persistence/Records.cs`：task/artifact/event receipt 参考。
- `EcomGen/` task/provider/worker 代码：可迁移 fingerprint、timeout、cancel 和 structured output 模式；不迁移其弱一致性边界。
- `tianming-novel-agent/src/application/novel-agent-application.ts`：当前无 durable claim/attempt，作为待补缺口证据。

## Boundary conclusion

Task execution reliability belongs to Web/Application/Worker/AgentControlDbContext. AgentCore remains a generic loop and Novel Agent remains a domain adapter. Unknown external outcomes must be modeled explicitly and never silently promoted to Canon.
