# tianming-web

天命 Web 层（规划中，暂为骨架）。

职责（见根目录 `AGENT_CORE_ARCHITECTURE.md`）：

- 前端：浏览器 UI/API/SSE 壳，不依赖 Node Agent 包或模型 provider
- 后端：ASP.NET 认证、授权、Session/Conversation durable truth、领域事务、Outbox、Worker、Read Model
- Node Runtime 只通过内部 API 使用已授权的 Application 能力；Core event 由 Web/Application 负责持久化映射

依赖方向：Web → tianming-novel-agent → tianming-agent-core → tianming-ai，只可向下。
