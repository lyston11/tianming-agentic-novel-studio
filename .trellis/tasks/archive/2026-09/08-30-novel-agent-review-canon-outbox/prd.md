# Review Canon Merge and Outbox

## 1. Goal and user value

把“模型写出文本”“通过连续性检查”“作者愿意采用”“进入作品正史”拆成可审计、可重放、可冲突处理的不同事实。用户可以看到 Review 的硬门禁证据、软审查 finding、正文/角色/伏笔 diff，并明确接受后才改变 Canon；数据库故障或事件投递失败不会制造部分成功。

## 2. Requirements

### R1 — structured review layers

Review 必须区分：

- hard continuity/policy gate；
- soft literary review；
- human acceptance readiness。

Hard gate 至少检查章节号、GoalRevision、Candidate version、context hash、角色/伏笔引用、Story Constitution forbidden directions、结构化变更可应用、正文非空和必要 evidence。Soft review 记录节奏、重复、风格偏移、情绪曲线、悬念、对话比例等 findings，不默认为阻断。

### R2 — immutable evidence

Review 是 immutable artifact，记录 candidate/version/context/Goal revision、gateVersion、input/output hash、finding severity、evidence spans、recommended action、reviewer/model provenance、createdAt。后续 Review 不覆盖旧 Review，而是新版本/新 attempt。

### R3 — EditorialDraft and Rework

Candidate 不可变。作者修改通过 EditorialDraft 和结构化 ReworkRequest 表达，至少包括 target candidate/version、scope/selection、problem、desired effect、preserve/mayChange/mustNotChange、acceptance criteria、impact level、attempt budget。SubmitRework 产生新 Candidate lineage/version。

影响传播：

```text
copy → no propagation
local_fact → revalidate affected following chapters
key_plot → revision plan required
hard_conflict → block Canon merge
```

### R4 — atomic Acceptance

Acceptance 必须是有权限 actor 提交的持久化决策，校验 Candidate version、Review passed、context hash、project scope、idempotency key 和 actor role。MVP 原子接受正文、CharacterState changes、Foreshadow changes 及 provenance；前端可分区显示，但不能静默部分合并。

### R5 — Canon baseline CAS

Canon merge 只能由 Application/Domain command 调用，必须校验：

- persisted accepted Acceptance 和 acceptance ID；
- Candidate status/version/context hash；
- GoalRevision/branch；
- frozen Canon version vector 与当前 baseline；
- protected human-authored versions；
- conflict/accepted-prefix 规则。

baseline 不匹配进入 merge conflict/needs_decision，不静默覆盖；MVP 不自动 rebase 非证明安全的变更。

### R6 — transactional events/outbox

Canon、Character/Foreshadow changes、merge record、Domain Event、Outbox 和必要 projection checkpoint 必须在其权威写 owner 的事务边界内提交。Outbox relay/consumer 幂等；投递失败不回滚已提交业务事实；重复 bridge/merge 不重复副作用。

### R7 — projection

WorkflowProjection、Candidate detail、Review summary 和 Canon status 是从 durable records/events 派生的只读模型。Reducer 必须按事件/版本可重放，不在 stream callback 中直接修改 source of truth。

## 3. Acceptance criteria

- [ ] A1：hard gate、soft review 和 human acceptance readiness 可独立查询；hard failure 阻断 acceptance，soft finding 不自动改变状态。
- [ ] A2：Review evidence 包含候选/上下文/Goal revision/gate version/hash/evidence span，旧 Review 保留且可重放。
- [ ] A3：EditorialDraft 可编辑但不能写 Canon；SubmitRework 生成新的 candidateVersion/parent/lineage，并执行预算和 impact propagation。
- [ ] A4：Acceptance 校验 actor scope/role、candidate version、context hash、Review 和 idempotency；重复请求返回同一结果，冲突不写入。
- [ ] A5：正文、CharacterState、Foreshadow 和 provenance 作为一次原子接受；失败时没有部分 Canon/ledger 更新。
- [ ] A6：Canon merge 对 current Canon version vector 做 CAS；冻结后 Canon 变化返回 merge conflict/needs_decision，受保护人工作品不被覆盖。
- [ ] A7：Canon/merge record/domain event/outbox 同事务提交；Outbox 重复投递、relay 重试和 crash recovery 不产生重复 Canon/domain event。
- [ ] A8：accepted-prefix/branch/conflict 规则有 integration tests；review passed 或 natural stop 不能绕过 Acceptance。
- [ ] A9：projection 可从 durable event 重建，SSE/relay 失败不改变业务真相。
- [ ] A10：文档区分当前实现、生产 transaction/outbox 证据和仍 deferred 的 full literary review/RAG/multi-agent 能力。

## 4. Out of scope

- 完整文学 Reviewer Agent 集群、长篇质量模型和自动写作风格优化。
- Worker claim/lease/provider recovery（由 Worker child 负责），本任务消费其 execution receipt。
- Web endpoint/frontend 交互实现（由 Web child 负责），本任务提供 DTO/domain query contract。
- 删除 legacy、迁移所有 Story Bible 数据和完整 RAG。

## 5. Dependencies and constraints

- 依赖 Conversation/Goal 和 PostgreSQL control-plane contracts；Worker execution receipt 可在其完成后接入。
- Canon 是明确 write owner；跨 DbContext 不共享假事务，使用 outbox handshake。
- 不能把 `Review.passed`、AgentCore `natural_stop` 或 SSE event 当作 Canon truth。
