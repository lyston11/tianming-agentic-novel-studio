# 实施计划：Novel Agent Web SSE and Acceptance Workbench

## 0. Discovery gate

- [ ] 定位新 backend 的真实 solution/project、route/controller、auth middleware、DTO、SSE producer 和 projection query。
- [ ] 读取前四个 child artifacts、backend/frontend specs、当前 `tianming-web/frontend/src/api`、hooks、workflow pages 和 legacy DTO。
- [ ] 盘点现有 `/api/novel-agent`、`/api/goals` endpoint 与 stream cursor，形成兼容表。

## 1. Contract and adapter

- [ ] 定义 Web DTO v1、error DTO、stream envelope、type guards/normalizers 和 explicit legacy mapper。
- [ ] 定义 REST command/query Application adapter，传递 auth/correlation/idempotency/expected version。
- [ ] 定义 conversation/workflow 分离 stream identity 和 cursor contract。

## 2. Backend transport

- [ ] 实现 turn/proposal/confirm/start/accept/rework commands（或在无 host 时只完成可编译 adapter contract）。
- [ ] 实现 workflow/candidate/runtime query，确保 projection 是 durable source。
- [ ] 实现 SSE headers 前 ownership 校验、Outbox/stream page、cursor replay、duplicate handling。

## 3. Frontend workbench

- [ ] 扩展现有 API client/types/query hooks，不创建第二套 client。
- [ ] 接入 Proposal/Review evidence、正文/Character/Foreshadow diff、Acceptance、Rework 和 conflict UI。
- [ ] 统一 shared invalidate path；transient token 不推进业务状态；新旧 stream 不共享 cursor。

## 4. Tests

- [ ] DTO round-trip/status/error/ownership tests。
- [ ] SSE stream separation/sequence/cursor/reconnect/duplicate tests。
- [ ] frontend decoder/hook/query invalidation tests。
- [ ] Acceptance/Rework stale version and no optimistic Canon mutation tests。
- [ ] Existing legacy route compatibility tests and affected package full checks。

## 5. Validation and rollback

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-novel-agent-web-sse-acceptance
# run discovered backend and frontend test/type-check/lint/build commands
```

回滚：关闭新 endpoint/stream/page feature flag，保留 legacy read/fallback；不要删除 durable events 或修改 Canon。SSE relay 故障只禁用通知并保留 cursor/retry。

## 6. Handoff

输出给 parent：Web DTO/SSE schema、HTTP mapping、frontend invalidation evidence 和遗留兼容限制。只有真实 Application/Outbox/projection 接线通过后，父任务才可声称端到端闭环。
