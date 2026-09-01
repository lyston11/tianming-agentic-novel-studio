# 技术设计：Novel Agent Web SSE and Acceptance Workbench

## 1. Boundary

```text
HTTP Controller / Route
  DTO + auth + correlation/idempotency headers only
        ↓
Application command/query service
  authorization + transaction + durable owner
        ↓
Domain/ports
  Proposal / Production / Review / Canon / Projection
        ↓
Outbox/stream adapter
  typed SSE envelope and cursor replay
        ↓
React Query + SSE hooks
  query invalidate; no business state reducer in UI
```

如果仓库当前没有新 Web backend，先完成 host discovery 和 adapter contract，不能凭空在错误目录创建 Controller。

## 2. API contract

### Commands

- append conversation turn：接受 `projectId/conversationId`、body、idempotency key、correlation ID，返回 turn/run/proposal summary。
- confirm proposal：接受 expected proposal version/hash，返回 Goal/Revision/Production IDs 和 planned status。
- start production：接受 expected production version，返回 running/ready projection。
- accept candidate：接受 candidate/version/context hash、decision、acceptance idempotency key，返回 Acceptance/Canon outcome。
- rework candidate：接受 structured ReworkRequest/Draft reference，返回 new lineage/version。

### Queries

- workflow projection：返回 proposal、Goal、Production、Batch、Task、Candidate、Review、Acceptance、Canon summary、versions。
- candidate detail：返回正文、source references、Review evidence、Character/Foreshadow diff。
- runtime events：按 workflow/conversation stream cursor 返回 typed event page。

## 3. SSE envelope

```ts
interface WebStreamEnvelope<T> {
  schemaVersion: "v1";
  eventId: string;
  streamKind: "conversation" | "workflow";
  streamId: string;
  sequence: number;
  eventType: string;
  projectId: string;
  sessionId?: string;
  goalId?: string;
  productionId?: string;
  taskId?: string;
  candidateId?: string;
  correlationId: string;
  causationId?: string;
  transient: boolean;
  data: T;
}
```

`eventId + streamId + sequence` 用于去重和 cursor；`transient=true` 只用于 token/connection observations。业务事件只表示 durable change，客户端收到后 invalidate 对应 query。

## 4. DTO ownership and decoding

- DTO schema/type guard/normalizer 由 `tianming-web/frontend/src/api` 或 backend adapter 单一 owner 定义。
- Components/hooks 不直接 cast raw JSON；统一 client decoder 处理 date、nullable、enum 和 error。
- Legacy DTO 与 new DTO 通过显式 mapper 转换；禁止 `as CreativeGoal` 或把 WorkflowProjection 当 legacy TaskGraph。

## 5. Frontend state flow

```text
REST query → React Query cache (server state)
SSE event → validate stream/sequence/cursor
          → business event: shared invalidate()
          → transient token: local stream text only
```

Proposal/Acceptance/Rework mutations 先更新 query invalidation strategy，再展示 optimistic UI；不能 optimistic set Canon/Production status。冲突时保留用户 Draft，刷新 candidate/workflow authoritative state。

## 6. Compatibility and rollout

1. 新 endpoint/DTO 先以 feature flag 暴露；旧 `/api/goals` 继续可读。
2. 新 conversation/workflow streams 使用独立 streamKind/streamId/cursor，与 legacy session stream 并行。
3. 逐页将 Acceptance workbench 改为新 projection；保留 legacy fallback 直到 contract/integration evidence 完成。
4. Outbox/Projection 作为 SSE 上游；SSE 连接失败只影响通知，不影响 command transaction。

## 7. Security and error mapping

- 认证身份由 host 注入，不能信任 body 的 userId。
- ownership 在 query 和 SSE headers 提交前校验；跨 scope 统一 404 或项目错误策略。
- 统一 error DTO：`code`、`message`、`correlationId`、`details`（可选 expected/current version）。不返回数据库异常、token、provider key 或其他租户信息。

## 8. Test strategy

- backend DTO/status/error/ownership contract tests。
- SSE serializer, stream separation, sequence/cursor/replay/duplicate tests。
- frontend API decoder and hook tests，验证 invalidate-only 与 transient isolation。
- Acceptance/Rework component tests，验证 stale conflict、diff display 和 typed commands。
- 若无真实 backend，必须把未实现集成标为 blocker/deferred，不用 mock fetch 宣称端到端完成。
