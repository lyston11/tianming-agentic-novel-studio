# Tianming Novel Agent Durable Control Plane

## 1. Goal and user value

将已验证的单章节 Novel Agent vertical slice 发展为可生产接线的 Web/Application durable control plane。用户的创作请求必须能够被持久化、确认、调度、审查、验收和合并为 Canon；流程状态、审计证据、失败恢复和实时通知必须可在进程重启与并发请求下解释和恢复。

本父任务只负责跨子任务的需求边界、依赖、统一合同和最终集成验收。实际实现由五个可独立验收的子任务完成。

## 2. Confirmed design decisions

- Web/Application/Domain 是 durable business truth、授权、事务、Outbox 和 projection 的所有者。
- Novel Agent 只拥有小说 Role、Skill、ContextPackage、DomainTool、Review 和结构化 intent 适配；不能直接写数据库或 Canon。
- AgentCore 只拥有通用消息/模型/tool loop；Core event 是 observation，不是业务真相。
- 外部产品动作可以是“确认并开始”，内部仍拆分 `ConfirmGoal` 与 `StartProduction`；确认后 Production/Batch/Task 为 `planned`，显式启动后才进入运行态。
- ContextPackage 冻结任务所需的最小相关事实、来源、版本向量、模型/策略版本和 hash；执行期间不回读 mutable snapshot。
- Candidate 不可变，返工通过 EditorialDraft/Rework 产生新版本；人工 Acceptance 是进入 Canon 的唯一业务入口。
- Worker 使用 TaskAttempt、Lease、FenceToken、ProviderRequest 和 OutcomeUnknown；不能盲目重试未知结果或自动采纳迟到结果。
- REST command/query 是状态真源，SSE 只通知刷新；前端以 projection 为真源，不自行推进状态机。
- `old/` 只提供小说领域语义，EcomGen 只提供工程模式样本；均不直接并入新 package graph。

## 3. In scope

### 3.1 Cross-task contracts

- Durable Conversation、ConversationTurn、assistant message、runtime run/checkpoint provenance。
- GoalProposal lifecycle、proposal version/hash、确认/拒绝/修订、确认来源和 actor。
- Goal、GoalRevision、Production、Batch、TaskGraph、Task 的状态与所有权边界。
- PostgreSQL AgentControlDbContext、事务、CAS、唯一约束和 tenant RLS。
- TaskAttempt、claim/renew/release、lease expiry、fence token、cancel、retry、provider request 和 outcome reconciliation。
- Review gate evidence：hard gate、soft review、literary findings、context/version provenance。
- EditorialDraft、ReworkRequest、Candidate lineage 和 bounded rework budget。
- Acceptance、Canon baseline compare-and-swap、Canon merge、Character/Foreshadow change application。
- Domain Event、Outbox、WorkflowProjection 和可游标 runtime/workflow stream。
- 稳定 Web DTO v1、REST commands/queries、SSE notification，以及现有前端 Acceptance/Rework 工作台适配。

### 3.2 Integration scope

父任务最终要证明一条用户作用域闭环：

```text
Conversation turn
→ durable GoalProposal
→ ConfirmGoal
→ StartProduction
→ queued Task
→ fenced Worker attempt
→ frozen ContextPackage
→ Candidate + Review
→ human Acceptance
→ Canon CAS merge
→ Domain Event + Outbox
→ WorkflowProjection + replayable SSE
```

## 4. Out of scope

- 整书自动生成、多章节并发、多 Agent 协作和无限制动态 DAG。
- 真实 Provider 的具体供应商接入、模型发现、计费结算和 OAuth；只定义可注入合同与受控 adapter。
- 完整 RAG、知识库重建、向量库部署、Memory promotion 和 Story Bible 大 JSON 迁移。
- 删除或重写 `old/`、旧 Pi/MAF runtime、Legacy Web 写路径，或让新系统与旧系统双写 Canon。
- EcomGen 代码、依赖、电商模型或 prompt 进入 Tianming。
- 以 Redis Pub/Sub、内存队列或 SSE listener 冒充 durable replay/worker lease。
- 在没有现有基础设施的情况下新增 Playwright E2E 平台；已有低成本测试只能作为补充证据。

## 5. Child task map and ordering

| Child | Deliverable | Depends on |
|---|---|---|
| `novel-agent-conversation-goal-control` | Conversation/Proposal contracts and lifecycle | None |
| `novel-agent-postgres-control-plane` | AgentControlDbContext, migrations, transactions, RLS, planned/start | Conversation/Goal contracts |
| `novel-agent-worker-reliability` | TaskAttempt, lease/fence, ProviderRequest, cancel/retry/recovery | Conversation contracts + PostgreSQL control plane |
| `novel-agent-review-canon-outbox` | Review/Rework/Acceptance/Canon CAS, events, Outbox, projections | Conversation contracts + PostgreSQL control plane; Worker result contract for execution linkage |
| `novel-agent-web-sse-acceptance` | Web DTO/REST/SSE and frontend Acceptance workbench | All preceding children |

Dependencies are ordering constraints, not permission to skip each child’s own planning and quality gate. Start one child at a time.

## 6. Acceptance criteria

- **AC-P01 — durable proposal:** conversation turn、assistant result/run provenance 和 GoalProposal 可按 user/project scope 查询；相同 idempotency key 重放同一结果，不同 payload 被拒绝。
- **AC-P02 — proposal lifecycle:** proposal 的 proposed/confirmed/rejected/superseded/discarded 生命周期可审计；Goal 必须引用被确认的 proposal；同一项目可以保留多个历史 proposal，但同一时刻最多一个 active confirmed Goal/Production。
- **AC-P03 — planned/start:** ConfirmGoal 不启动模型、不将 Production 写成 running；StartProduction 是幂等命令，并将 Production/Batch/Task 按合法迁移推进。
- **AC-P04 — durable transaction:** Goal/Revision/Production/Batch/TaskGraph/Task 的相关写入在 AgentControlDbContext 事务中保持原子性；失败不留下半成品；迁移使用独立 history table，不复制既有控制表。
- **AC-P05 — tenant isolation:** 所有 command/query 同时执行显式 user/project predicate 和 RLS；跨用户/项目访问返回统一 not-found/forbidden 语义，且无写入副作用。
- **AC-P06 — fenced execution:** 同一 Task 的并发 claim 只有一个成功；lease 过期、错误 fence token 或取消后的迟到结果被拒绝；attempt、provider key 和 outcome 可审计。
- **AC-P07 — unknown recovery:** Provider 已发出但结果未知时任务进入 `outcome_unknown`；不会自动重复调用或采纳迟到结果；reconcile 后结果可标记 `unadopted` 或进入受控重试。
- **AC-P08 — review and rework:** hard gate 失败阻断 Acceptance；soft review 记录 findings；Candidate 不可变，EditorialDraft/Rework 产生新 lineage/version，并受预算和影响传播规则约束。
- **AC-P09 — acceptance and Canon:** 只有有权限的持久化 accepted Acceptance 才能合并 Canon；candidate version、context hash 和当前 Canon version vector 不匹配时返回 conflict；Canon、merge record、domain event 和 Outbox 原子提交。
- **AC-P10 — projection and stream:** WorkflowProjection 从 durable facts 派生；SSE 使用稳定 envelope、stream identity、sequence 和 cursor replay；重复事件不重复推进状态。
- **AC-P11 — frontend truth:** 前端只提交 REST command、读取 projection/query、处理 SSE 刷新通知；不从 SSE payload 直接设置 Goal/Production/Canon 状态。
- **AC-P12 — boundary regression:** Core/AI/Novel Agent 现有测试不回归；Novel Agent 不直接导入 Pi/DB/Redis/Qdrant；old/ 和 EcomGen 不被修改；每个 child 有 negative-path 和 integration evidence。

## 7. Risks and deferred gates

- 当前仓库的 `tianming-web` 没有新的 ASP.NET backend 实现；子任务必须先确认真实 solution/project 和现有迁移，不得凭空制造 adapter。
- 旧 frontend DTO 与新 `WorkflowProjection` 不同；必须先定义 DTO v1 和兼容/切换策略，再改 UI。
- PostgreSQL、Outbox 和 Worker 的可靠性只能通过真实 integration tests 证明；in-memory adapter 仅用于合同测试。
- Canon 与旧 context/knowledge 版本可能发生冲突；exact-baseline merge 是第一阶段默认策略，rebase 另有明确命令和证据。
- 旧 runtime 的 checkpoint 不能恢复旧 ReAct loop；未知执行只能基于 Canon snapshot 创建新的受控 recovery proposal。

## 8. Planning status

父任务及五个子任务已生成规划目录，均保持 `planning`，未执行 `task.py start`，未修改业务代码，未提交 Git。实现前必须先审阅对应 child 的 `prd.md`、`design.md`、`implement.md` 和上下文清单，并单独批准该 child 的最终规划摘要。
