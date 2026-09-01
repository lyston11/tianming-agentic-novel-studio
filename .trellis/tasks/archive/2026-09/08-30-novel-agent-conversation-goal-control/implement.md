# 实施计划：Durable Conversation and Goal Proposal Control

## 0. Planning and discovery

- [ ] 确认当前可编译 Web/Application host、身份中间件和 persistence abstraction；若不存在，先记录 blocker，不创建假 backend。
- [ ] 读取 parent PRD/design、backend database/error/quality specs、现有 Novel Agent contracts 和 legacy GoalProposal/GoalContract 证据。
- [ ] 搜索所有 `GoalProposal`、conversation endpoint、checkpoint 和 `NovelAgentApplication` caller，建立兼容矩阵。

## 1. Contract first

- [ ] 定义 Conversation/Turn/Message/RuntimeRun/Checkpoint/Proposal/ProposalRevision/Confirmation DTO 和 typed errors。
- [ ] 定义 status reducer、append-only revision、source provenance、idempotency 和 expected version/hash 规则。
- [ ] 扩展 `NovelApplicationPorts` 的最窄接口，不把 EF/HTTP 类型泄漏到 Novel Agent。

## 2. Application use cases

- [ ] 实现 append turn + proposal run 的 Application 编排，保留 assistant/runtime failure evidence。
- [ ] 实现 propose/revise/reject/discard/confirm commands；确认只返回 GoalCommitIntent，交由 PostgreSQL child 的 transaction owner 落地。
- [ ] 实现多 proposal / 单 active Goal policy 和统一 scope/error mapping。

## 3. Persistence adapter contract

- [ ] 为 PostgreSQL child 提供 repository/UoW contract tests 和 expected transaction semantics。
- [ ] 若当前 host 已有 persistence，接入最小 adapter；不得在 legacy context 上增加旁路写入。
- [ ] 为旧 endpoint 准备 compatibility adapter，但保持 feature flag/off by default。

## 4. Tests and evidence

- [ ] Proposal lifecycle and revision tests。
- [ ] idempotency same/different payload tests。
- [ ] concurrent confirmation test with independent contexts or a deterministic repository harness。
- [ ] scope/forbidden/not-found/invalid-transition/no-partial-write tests。
- [ ] AgentCore → propose_goal → ProposalPort test；证明不创建 Production。

## 5. Validation and rollback

```bash
python3 ./.trellis/scripts/get_context.py --mode packages
python3 ./.trellis/scripts/task.py validate .trellis/tasks/08-30-novel-agent-conversation-goal-control
# run the discovered host's actual test/type-check/build commands
```

回滚点：先回滚新 Application adapter/endpoint；保留 append-only records，不删除已审计历史；旧读路径保持可用。不得通过回滚把 Proposal 直接变成执行状态。

## 6. Handoff

输出给 PostgreSQL child：稳定 schema semantics、transaction contract、unique active-goal policy、concurrency test fixtures 和错误矩阵。该 child 通过 quality gate 后才可启动 child 2。
