# 研究证据：Web、SSE 与 Acceptance Workbench

## Anchors

- `tianming-web/README.md`：新 Web backend 仍是规划边界，负责 auth/durable truth/transaction/Outbox/Worker/projection。
- `tianming-web/frontend/src/api/types.ts`：当前 legacy workflow DTO 和旧状态形状。
- `tianming-web/frontend/src/api/novel-agent.ts`：新意图命名的 API wrapper，但目标仍需与 durable control-plane adapter 对齐。
- `tianming-web/frontend/src/features/workflow/use-novel-agent-workflow-stream.ts`：workflow SSE cursor/reconnect/invalidation 参考。
- `tianming-web/frontend/src/features/workflow/use-goal-workflow.ts`：React Query workflow query ownership 参考。
- `.trellis/spec/frontend/state-management.md:21-69`：REST truth、SSE notification、stream separation、cursor、transient isolation。
- `.trellis/spec/backend/database-guidelines.md:47-58`：outbox bridge transaction、duplicate delivery 和 persisted state。

## Boundary conclusion

Web transport 必须是 Application command/query 的薄 adapter；SSE 不能推进业务状态。前端需要显式 DTO/schema decoder 和 legacy mapper，不能通过本地 cast 把新 projection 伪装成旧 task graph。
