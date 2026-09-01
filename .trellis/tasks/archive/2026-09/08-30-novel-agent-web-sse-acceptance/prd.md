# Novel Agent Web SSE and Acceptance Workbench

## 1. Goal and user value

让用户可以通过稳定的 Web API 完成 Proposal、Confirm/Start、Candidate Review、Acceptance 和 Rework，并在断线重连后从 durable projection/SSE cursor 恢复视图。前端只消费服务器真源，不通过 SSE 或本地状态猜测 Production、Task 或 Canon。

## 2. Requirements

### R1 — stable DTO v1

定义独立于 EF、legacy DTO 和 AgentCore event 的 Web DTO v1，覆盖 Conversation turn/result、GoalProposal、WorkflowProjection、Candidate、Review、Acceptance、Rework 和 Canon status。每个 DTO 明确 `schemaVersion`、stable IDs、scope、version/hash、correlation 和 error shape。

### R2 — REST commands and projections

实现或适配以下 command/query（最终路由可兼容现有前缀，但语义不可退化）：

```text
POST /projects/{projectId}/conversations/{conversationId}/turns
POST /projects/{projectId}/goal-proposals/{proposalId}/confirm
POST /projects/{projectId}/productions/{productionId}/start
POST /projects/{projectId}/candidates/{candidateId}/accept
POST /projects/{projectId}/candidates/{candidateId}/rework
GET  /projects/{projectId}/workflow
GET  /projects/{projectId}/runtime-events?after={cursor}
GET  /projects/{projectId}/candidates/{candidateId}
```

Controller 只负责 DTO/身份/header 转换；Application command owner 负责授权、幂等、状态机和事务；query 返回 projection，不返回可被前端直接写回的实体引用。

### R3 — authorization and HTTP semantics

所有 endpoint 在读取资源和写入前校验 user/project/session/goal/production scope。统一映射：

```text
202 Accepted      async command accepted
200 OK            query/idempotent result
403 Forbidden     authenticated but unauthorized
404 Not Found    resource outside visible scope
409 Conflict     stale version/hash/state/idempotency conflict
422 Unprocessable Entity invalid contract/input
```

错误体需包含稳定 code、message、correlationId 和可选 expected/current version；不得泄漏跨租户资源存在性。

### R4 — SSE envelope and streams

Conversation 与 Workflow 使用相同 envelope shape、不同 `streamKind`/`streamId`。Envelope 至少包含：

```text
schemaVersion, eventId, streamKind, streamId, sequence,
eventType, projectId, sessionId, goalId, productionId,
taskId, candidateId, correlationId, causationId, transient, data
```

SSE 只通知 durable state changed；cursor/replay 从 durable stream/outbox/projection 读取。token delta 可 transient 展示，但不能推进业务状态。

### R5 — frontend state ownership

前端 React Query 持有 Conversation/Proposal/Workflow/Candidate/Review server state；SSE hook 只验证 stream identity/sequence/cursor，并触发共享 invalidate。任何组件不得从 SSE payload 直接 set Production/Task/Canon status。

### R6 — Acceptance workbench

提供可复用的 proposal review、hard/soft Review findings、正文 diff、Character/Foreshadow diff、Acceptance decision、冲突和 Rework entry UI/queries。Accept/Rework 都提交 typed REST command；过期版本显示 conflict 并刷新 authoritative query。

### R7 — migration compatibility

当前 frontend 仍含 legacy `CreativeGoal`/task graph/artifact DTO 和旧 endpoint wrappers。定义 explicit adapter/feature flag：新 DTO 与 legacy DTO 不互相隐式 cast；legacy fallback 保留至等价回归通过；新 SSE stream 与 legacy stream 不能共享 cursor/reducer。

### R8 — observability and replay

API/SSE 日志和客户端错误包含 request/correlation/event cursor，不记录正文 secrets 或 credentials。断线重连使用每条 stream 自己的 cursor 和 bounded backoff；重复/旧 sequence 被丢弃，不能导致状态回退。

## 3. Acceptance criteria

- [ ] A1：API contract tests 验证 turn/proposal/confirm/start/accept/rework/query 的 DTO round-trip、schemaVersion、ID、version/hash 和 error shape。
- [ ] A2：command endpoint 返回 202/200/403/404/409/422 的稳定语义，跨 scope 请求不泄漏资源且无副作用。
- [ ] A3：Workflow query 返回与 durable source 一致的 projection；前端无需本地状态机即可渲染 planned/running/awaiting_acceptance/completed/conflict。
- [ ] A4：Conversation 和 Workflow SSE 使用不同 stream identity；服务端校验 ownership 后才发送 headers；cursor replay、duplicate/old sequence 和 reconnect 通过测试。
- [ ] A5：transient token event 只更新可见流文本，不 invalidate 或改变 Goal/Production/Task/Candidate/Canon；business event 只触发共享 query invalidation。
- [ ] A6：Acceptance workbench 能展示 Review evidence、正文/账本 diff、冲突和 rework，并以 typed command 提交；stale candidate 显示 authoritative refresh。
- [ ] A7：legacy fallback 和新 adapter 并存时不发生 DTO shape 混用、双 cursor 或双状态 owner；frontend typecheck/lint/test/build 通过。
- [ ] A8：API/SSE 与 backend Outbox/projection/Worker result 的 correlation/causation/event IDs 可追踪；SSE 发送失败不回滚业务事实。
- [ ] A9：已有 Core/AI/Novel Agent 测试不回归；浏览器 E2E 仅在已有基础设施可复用时补充，不以 mock API 作为完成证据。

## 4. Out of scope

- 新建 ASP.NET host（若仓库没有可接入 backend，必须先记录 blocker）。
- 真实 Redis fanout、WebSocket、完整 SSE relay、生产级 auth provider 和部署运维。
- 完整 full-book UI、任意 DAG 编辑器、多 Agent 控制台和全部 RAG 结果展示。
- 删除 legacy frontend/backend/runtime 或修改 EcomGen/old。

## 5. Dependencies and constraints

- 依赖前四个 child 提供 durable Conversation/Goal、PostgreSQL transactions、Worker receipts、Review/Canon/Outbox/projection contracts。
- 必须遵守 `.trellis/spec/frontend/state-management.md`：REST 真源、SSE 通知、shared invalidation、独立 cursor。
- 必须先搜索现有 API/client/hooks/types，扩展现有边界而不是重复建立第二个 client 或 reducer。
