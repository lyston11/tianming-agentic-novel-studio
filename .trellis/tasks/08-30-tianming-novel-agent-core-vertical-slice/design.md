# 技术设计：Novel Agent Core Vertical Slice

## 1. 设计原则

本任务采用“通用 Core + 小说领域 adapter + Web/Application 权威”的三段式切片。Core 只运行消息、模型和工具；Novel Agent 把小说角色/技能/上下文转成 Core 可消费的 system prompt、tool 和 role；Web/Application 负责身份、授权、领域状态、事务边界、持久化、Outbox 与查询投影。

依赖方向固定为：

```text
tianming-web
  ↓
tianming-novel-agent
  ↓
tianming-agent-core
  ↓
tianming-ai
```

实现可以先在 TypeScript 内用 in-memory adapter/fake store 验证业务合同，再由 Web 内部 API 或同等宿主端口接线。不得为了快速闭环把领域事实写入 Core 或让 Node 直接连接数据库。

## 2. 目标目录布局

目标新增/演进目录如下；实际命名以仓库现有 TypeScript/ASP.NET 约定为准，但职责不得跨越：

```text
tianming-novel-agent/
  src/
    contracts/          # GoalProposal、ContextPackage、Candidate、Review、commands
    roles/              # chapter-writer role 与工具白名单
    skills/             # 本切片的章节写作 skill 合同
    context/             # ContextProvider 与冻结/hash 编译
    tools/              # DomainTool；只调用 ports
    runtime/            # AgentCore adapter 与事件翻译
  test/
    contracts.test.ts
    vertical-slice.test.ts

tianming-web/           # 现有 ASP.NET 宿主，若本任务接线则新增对应 Application/Domain/Worker 层
  .../Application/
  .../Domain/
  .../Infrastructure/
  .../Api/

.trellis/tasks/08-30-tianming-novel-agent-core-vertical-slice/
  prd.md
  design.md
  implement.md
  notes.md
  implement.jsonl
  check.jsonl
```

如果当前仓库尚无 `src/` 或 C# 新层的实际承载位置，先沿现有目录约定建立最小模块；不得创建模糊的 `utils`/`common` 垃圾桶目录。

## 3. 模块边界与所有权

### 3.1 `tianming-agent-core`

权威能力：消息上下文、`AgentCore.prompt()`、natural stop、串行 tool execution、TypeBox 参数校验、错误 tool result、steering/follow-up、abort、maxTurns、稳定 `AgentCoreEvent`。

禁止：小说对象、项目/用户授权、Goal/Production/Task 状态、Canon/Review、PostgreSQL/EF/ASP.NET/React、领域状态机。

### 3.2 `tianming-ai`

权威能力：模型类型受控导出、`streamSimple` adapter、Pi AI provider 边界、TypeBox tool argument validation。Pi 版本固定 `@mariozechner/pi-ai@0.57.1`。

禁止：读取小说项目、持久化消息、决定授权或直接依赖 Novel Agent/Web。

### 3.3 `tianming-novel-agent`

权威能力：

- `chapter-writer` role；
- Skill/resource 合同；
- NovelContextPackage 的消费与最小编译适配；
- DomainTool 描述和工具白名单；
- 将模型输出解析为 GoalProposal/Candidate/Review/command intent；
- 将 Core event 翻译为宿主可持久化的 runtime message。

禁止：直接写 PostgreSQL/EF/Canon；直接导入 Pi 系列包；替 Web/Application 决定领域状态。

### 3.4 `tianming-web` Application/Domain/Worker

权威能力：当前用户 scope、授权、Goal/GoalRevision/Production/Batch/Task 状态、Candidate/Review/Acceptance/Canon 写入、事务、Outbox、Worker claim/lease/fence、projection 和 HTTP/SSE。

Application 是唯一用例编排入口：

```text
HandleConversationTurn
ConfirmGoal
RunChapterTask
AcceptCandidate
QueryWorkflow
SubscribeRuntimeEvents
```

Controller 只转换 DTO、传递身份/correlation/idempotency、映射状态码；Worker 只 claim/执行/提交命令，不直接拼接多个服务修改领域状态。

## 4. Port 草案

以下是跨层合同的方向性草案，具体语言可分别使用 TypeScript interface 与 C# interface，但字段语义必须一致：

```ts
interface ConversationPort {
  handleTurn(input: ConversationTurn): Promise<ConversationResult>;
}

interface NovelProposalPort {
  proposeGoal(input: NovelConversationContext): Promise<GoalProposal>;
}

interface ProductionCommandPort {
  confirmGoal(input: ConfirmGoalCommand): Promise<GoalCommitResult>;
  acceptCandidate(input: AcceptCandidateCommand): Promise<AcceptanceResult>;
}

interface NovelContextPort {
  loadForConversation(input: ContextRequest): Promise<NovelContextSnapshot>;
  freezeForChapter(input: FreezeContextRequest): Promise<NovelContextPackage>;
}

interface CandidatePort {
  saveCandidate(input: CandidateChapter): Promise<void>;
  saveReview(input: Review): Promise<void>;
}

interface CanonPort {
  mergeAcceptedCandidate(input: CanonMergeCommand): Promise<CanonMergeResult>;
}

interface WorkflowQueryPort {
  get(projectId: string, actor: ActorScope): Promise<WorkflowProjection>;
}

interface RuntimeEventSink {
  append(message: DurableRuntimeMessage): Promise<void>;
}
```

所有 port 都要携带 `projectId`、`actor/user scope`、`correlationId`；写命令携带 `idempotencyKey` 与 expected revision/version。DomainTool 只依赖上述 port 的最窄接口。

## 5. 数据合同与状态机

### 5.1 最小对象

```text
ConversationTurn
GoalProposal
Goal
GoalRevision
Production
ProductionBatch
KernelTask/Task
NovelContextPackage
CandidateChapter
Review
Acceptance
CanonicalChapter
CharacterState
ForeshadowEntry
WorkflowProjection
DurableRuntimeMessage
```

每个对象都有稳定 ID、project scope、创建时间；持久化实体额外拥有 revision/version。正文和上下文包保存 content hash；来源引用保存 source ID/type，不以复制全文替代出处。

### 5.2 状态转换

```text
Conversation
  └─> GoalProposal(proposed, requires_confirmation)
        ├─ reject/revise ─> proposal remains non-executable
        └─ ConfirmGoal
              └─ Goal(confirmed) + GoalRevision
                    └─ Production(running)
                          └─ Batch(running)
                                └─ Task(queued/running)
                                      └─ freeze ContextPackage
                                            └─ CandidateChapter(generated)
                                                  └─ continuity gate
                                                        ├─ Review(failed) → blocked/reworkable
                                                        └─ Review(passed)
                                                              └─ awaiting_acceptance
                                                                    ├─ reject/rework
                                                                    └─ AcceptCandidate
                                                                          └─ Canon merge
                                                                                ├─ CanonicalChapter
                                                                                ├─ CharacterState update
                                                                                ├─ ForeshadowEntry update
                                                                                └─ projection refresh
```

本切片只实现一个项目、一个 Goal、一个 Batch、一个章节候选和一次 Canon merge 的可测试路径；状态机仍必须拒绝跳过确认、跳过 gate 或从 candidate 直接 merge。

### 5.3 状态所有权

- `Goal.status`：`proposed | confirmed | superseded | cancelled`，由 Goal/Production domain command owner 修改。
- `Production.status`：`running | waiting | paused | blocked | completed | failed | cancelled`，由生产状态机修改。
- `Batch.status`：当前批次执行状态，由生产状态机修改。
- `Task.status`：调度技术状态，由 Worker/Scheduler 修改；结果转换为领域命令。
- `Candidate/Review/Acceptance`：Application/Domain 写入，不由 AgentCore 改写。
- `WorkflowProjection`：派生只读模型，不承担修复状态的副作用。

## 6. ContextPackage、版本与哈希

`ConversationContext` 可以读取项目的当前最小 snapshot；`GoalRevision` 确认时记录提案来源和版本；章节执行必须调用 `freezeForChapter`，把 Canon 中与本章相关的角色状态、伏笔、上一章游标、Goal 条件和必要资源变成不可变 `NovelContextPackage`。

哈希规则：

- `contentHash = hash(canonical-json(package contents))`；字段排序与时间字段策略要固定并测试。
- `versionVector` 至少包含 `goalRevision`、`canon`、`modelProfile` 和 `style/resource` 的版本。
- Candidate 必须回写 `contextPackageHash`；acceptance/merge 要求 candidate hash 与冻结包一致。
- 每次执行保留 `sourceReferences` 和可选 `ContextReadReceipt` 位置；正文不在 receipt 中重复存储。

上下文包是执行快照，不是真实源；Canon、Goal、Knowledge、Memory、Production 各自仍由所属 store/aggregate 负责。

## 7. Core event 到 durable message/SSE

Core event 仅通过 runtime adapter 进入宿主：

```text
AgentCoreEvent
  → RuntimeMessageEnvelope
      { messageId, runId, taskId, projectId, sequence,
        eventType, payload, correlationId, occurredAt }
  → Application/Outbox transaction
  → Workflow/runtime projection
  → SSE cursor stream
```

映射要求：

- 每个 run/event 有稳定 `runId + sequence`；宿主可去重。
- `message_start/update/end` 可合并为 durable message，但不得丢失最终 assistant/tool result；tool call 必须保留 toolCallId、toolName、error 标志。
- `agent_end` 映射为 run completion/abort/error observation，不自动等同于 Production completed。
- SSE 断线通过 cursor/replay 读取宿主记录，不依赖内存 listener。
- 本任务可以使用 fake `RuntimeEventSink` 验证映射；真实 Outbox relay、Redis fanout 和断线恢复是后续门槛。

## 8. DomainTool 权限与 scope

每个 DomainTool 声明 `name`、描述、TypeBox schema、所需 scope 和副作用级别：

```text
read_context        project:read       read-only
propose_goal        project:write      proposal-only
request_chapter     project:write      intent-only
submit_review       project:write      review-only
request_acceptance  project:write      command intent
```

Core 只执行已注入的 tool；Novel Agent role 只注入 `chapter-writer` 白名单。Application 在 port 层再次校验 actor、project ownership、Goal/Task revision 和 idempotency，不能把 role whitelist 当作授权替代品。任何 write/merge tool 都返回 intent 或业务结果，不直接取得数据库连接。

## 9. 错误、取消、幂等与恢复

- Schema/业务参数错误：转为可观察 error result 或结构化 command rejection；不抛出未记录的异常穿透。
- 未授权、跨 project、过期 revision：拒绝且不写入半成品；记录 correlation/idempotency 关联。
- Abort：Core 结束为 `aborted`；宿主保留已完成消息和 Task attempt，未执行工具不得伪造成功；Production 进入待恢复/失败策略由 Application 决定。
- Fake store 写操作使用 unique `idempotencyKey`；相同 key + 相同请求返回原结果，相同 key + 不同 payload 拒绝。
- Candidate acceptance 使用 `expectedCandidateVersion`；版本不匹配返回 conflict。
- Canon merge 要求 candidate 状态为 `awaiting_acceptance` 且 Review passed；重复 merge 返回同一 Canon result，不重复追加章节或状态变更。
- Worker lease、attempt、fence token、Outbox 原子写入、数据库隔离、重试和死信只定义接口/测试门槛，本切片不得声称已经提供分布式安全。

## 10. 测试架构

### 单元测试

- contract schema：提案、上下文包、candidate、review、commands、hash。
- role/tool whitelist：未知工具和越权 scope 被拒绝。
- continuity gate：通过/失败固定 fixture。
- state transition：非法跳转、重复确认、版本冲突。
- Canon reducer：接受后只产生预期 CharacterState/ForeshadowEntry/projection 变化。

### Runtime 集成测试

- 注入 deterministic fake model 的 `streamFn`，验证 AgentCore natural stop、串行 tool、错误 result 和事件映射不回归。
- 验证 `GoalProposal` 作为 tool/adapter 输出，不把领域阶段硬编码进 Core。
- 验证中止后不执行未开始的 tool，且 durable message 映射可重放。

### Vertical slice 测试

单个 in-memory project 从 conversation turn 跑到 acceptance/Canon query；同一测试包含 source/hash/version、scope、idempotency 和 projection 断言。

### Web/frontend 证据

如果本任务接入现有 Web：增加最小 API contract/SSE serialization 测试；前端只消费 DTO/事件，不在客户端推进领域状态。浏览器 E2E 作为后续任务，不以 mock UI 通过替代真实 API/worker 证据。

## 11. 旧代码选择性迁移映射

| 旧语义/路径 | 处理 |
|---|---|
| `old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts` | 仅借鉴 durable message 转换、事件观察和 tool adapter 形状；不复用其 `pi-agent-core.Agent` 运行时 |
| `old/Web/NovelAgentWeb/Program.cs` 中 `TargetArchitectureDirector`、Structured/Pi Runtime 注册 | 作为当前 callers/legacy 边界证据；新路径不得继续向其添加能力 |
| old 的 discovery/binding tools | 后续按 port 逐个迁移；本切片只实现最小 project/context 读取 |
| `MissionPlan/AgentRun/AgentToolExecution` | 不映射为新 Task 真源；未来写入 `LegacyExecutionArchive` |
| Goal/Production/TaskGraph/KernelArtifact 语义 | 保留为后续生产层合同；本切片只落一个最小 Goal/Production/Batch/Task/Candidate |
| 四层记忆/Story Bible mutable JSON | 提炼 scope/source/version 语义；不把大 JSON 搬为 Canon 或新上下文真源 |

## 12. 关键取舍

- **先 fake、后真实 provider**：让领域状态和 Core adapter 可在无外部服务下确定性验证；代价是真实模型差异需要后续 contract test。
- **先 in-memory port、后 PostgreSQL**：避免把数据库迁移和分布式可靠性混入第一条可验证链；代价是本任务不能证明生产部署可靠性。
- **单章节 interactive batch**：最小化人工验收和 Canon merge 边界；代价是不能证明整书调度。
- **统一上下文包而非 mutable StoryBible**：可冻结、可 hash、可审计；代价是需要后续补齐领域查询和版本注册。
- **Core event 观察面、宿主 durable**：保持 Core 通用且可嵌入；代价是 Web/Application 必须实现 durable mapping/replay。
