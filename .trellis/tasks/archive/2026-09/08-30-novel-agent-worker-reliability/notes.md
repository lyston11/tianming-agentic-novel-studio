# 研究笔记：Worker Execution Reliability and Recovery

## Evidence

- backend database guideline 要求 `AgentControlDbContext` 负责 Worker claim/renew/complete/fail/dependent unblocking 和 acceptance bridge，且 lease loss 回滚 completion。
- old `Production.cs`、`KernelTask.cs` 和 persistence records 展示 planned/running/leased/waiting/succeeded/failed/cancelled、attempt、lease expiry、claim owner 和 fence 语义。
- old recovery services 明确：旧 MissionPlan、RuntimeRun、pending tool execution 不能直接恢复；应基于 retained canonical data 创建新 Goal/Proposal。
- EcomGen 的 task ID、attempt、provider request key、timeout、cancel boundary 和 output dedupe 可迁移；其缺少 lease/fence/Outbox/强 scope 的部分不能复制。
- 当前 TypeScript vertical slice 没有 TaskAttempt/lease/provider boundary，`runChapterTask` 在 store command queue 外同步运行，不能证明并发安全。

## Decisions

- Worker 只提交 typed result command；状态 owner 在 Application/Domain。
- outcome_unknown 是一等状态；late result 默认 unadopted。
- fence 校验是持久化写入前的硬门禁，不是日志字段。
- fake provider 只用于合同测试，真实 provider contract 由后续阶段接入。

## Deferred

- 具体 queue/broker、provider、secret manager、metrics/tracing 平台。
- 多区域 Worker、autoscaling、死信运维界面。
