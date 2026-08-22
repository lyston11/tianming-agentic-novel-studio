# 小说 Agent 目标架构设计

状态：Planning
更新时间：2026-08-20

## 1. 设计目标

以 `@earendil-works/pi-agent-core` 作为通用 Agent Loop，构建独立的 Node Agent Runtime Service；ASP.NET Application/Domain 继续拥有用户授权、项目状态、领域副作用、事务、Worker、Outbox 和 Canon。

Conversation 是通用 Agent 交互入口，不天然属于某个小说项目。新 Conversation 必须先保持 Unbound，由 Agent 发现候选项目，在用户明确确认后才激活项目上下文。

项目确认是 Agent 的交互/工具能力，不是包围整个 Agent Loop 的硬性运行门槛。未确认不会停止 Agent；它只限制当前 Run 能看到的项目数据和可调用的项目能力。

### 当前任务执行边界

- 用户已取消“只审计、不修改产品代码”的限制；当前任务直接实施本设计。
- 六个实施切片全部由 `08-18-architecture-audit` 顺序承担，不创建子任务。
- 切片顺序是硬依赖：Session 创建边界 → Conversation/Context → 项目发现与绑定 → Node Pi Runtime → Agent→Canon Application/Database E2E → API/SSE/浏览器 E2E。
- 每个切片必须在目标测试通过后才能进入下一切片；共享边界发生回归时回滚当前切片，不以兼容双写掩盖失败。
- 审计报告和历史 handoff 在实施结束时同步到最终代码事实。

## 2. 核心不变量

1. 新 Conversation 的 `ProjectId` 为 null；创建接口不得接收初始 `projectId`。
2. 项目发现可以自动进行，项目绑定必须来自用户明确确认。
3. 未确认不会 abort、terminate、暂停或拒绝整个 Pi Loop；Agent 仍可交流、澄清和使用通用能力。
4. 未绑定 Run 只能读取用户可访问项目的最小目录元数据，不能读取任何项目业务内容。
5. 项目写工具在绑定成功前不进入该 Run 的可用能力集合。
6. 绑定只更新 Conversation context，不创建 Goal、不启动 Production、不执行 Acceptance/Canon Merge，也不产生其他小说领域副作用。
7. Conversation 回合入口不接收或信任调用方提供的 `projectId`；ASP.NET 根据服务端 Session 解析绑定状态。
8. 允许未绑定的是 Conversation，不是 Goal/Kernel/Canon。项目领域执行路径继续要求明确项目作用域。
9. PostgreSQL Conversation/Message/Project Read Model 是权威状态；Node Pi Agent 只保留单次 Run 的进程内状态。
10. Conversation 不是项目事件消费者；Outbox 负责可靠投影，SSE 只通知 Web 页面刷新，项目事实不会复制成每个 Conversation 的 Inbox。

## 3. Conversation 状态模型

第一阶段不新增庞大的会话状态机，使用当前持久化指针与可审计消息边界表达状态：

```text
Unbound
  ├─ 通用对话
  ├─ list_accessible_projects（只读最小目录）
  ├─ 展示候选项目
  └─ 等待用户明确确认（Agent 继续运行）
         │
         └─ activate_project_context
                │
                ▼
Bound(projectId, bindingVersion)
  ├─ 加载版本化 ProjectContextSnapshot
  ├─ 启用项目读工具
  └─ 按授权策略启用项目写工具
```

`ProjectId == null` 表示 Unbound；非空表示当前 active project context。绑定时追加不可变的 `ProjectContextActivated` 自定义/领域消息或等价审计记录，包含：

- `conversationId`
- `projectId`
- `sourceUserMessageId` 或 Web 确认动作 ID
- `bindingVersion`
- `idempotencyKey`
- `confirmedAt`

当前 `AgentSession.ProjectId` 只保存最新指针，审计记录保存上下文切换边界。以后若允许切换项目，必须先设计旧项目上下文隔离和摘要边界；本阶段不实现自动切换。

## 4. Pi Skill、Resource 与 Tool 边界

### 4.1 项目发现

项目发现可以由薄宿主中的 skill/resource 引导，并通过原生只读 `AgentTool` 执行实际查询。建议首个工具为：

```text
list_accessible_projects
```

它通过 ASP.NET 内部 API 获取当前用户可访问项目，只返回：

- project ID
- 标题
- 项目状态
- 最近更新时间
- 可选的极短非正文描述

它不得返回正文、Canon、Story Bible、Goal、Proposal、Production、Candidate、Knowledge、Memory 或其他项目业务状态。

### 4.2 项目确认与绑定

建议使用窄工具：

```text
activate_project_context
```

输入至少包括 `conversationId`、`projectId`、`sourceUserMessageId`/确认动作 ID 和 `idempotencyKey`。它是 Conversation context command，不是小说领域 command。

用户明确确认由对话消息或 Web 候选卡动作表达。Agent 在确认后调用工具；ASP.NET 验证用户、Conversation、候选项目访问权、确认来源归属、幂等键和当前 binding version，但不增加新的 LLM 语义置信度评分器。

如果确认来源缺失、失效或不属于当前用户/Conversation，工具返回结构化结果：

```json
{
  "status": "confirmation_required",
  "recoverable": true
}
```

该结果不 abort Pi Run。Agent 可以继续解释候选、询问用户或完成其他通用工作。

绑定成功返回 `projectId`、`bindingVersion` 和 `contextVersion`。Node 宿主在下一模型回合前加载最新 `ProjectContextSnapshot` 并更新可用工具；不需要停止整个 Agent Loop。

### 4.3 项目领域工具

绑定成功后，项目工具直接实现 Pi 原生 `AgentTool`：TypeBox 参数、结构化 details、`onUpdate`、副作用工具 `executionMode: "sequential"`。Node 不直连业务数据库；所有领域工具调用 ASP.NET 内部 HTTP/JSON Application API。

Acceptance 和 Canon Merge 不注册为 Agent Tool。它们始终是用户 Web 控制面动作；Agent 只在后续用户触发的 Run 中读取其结果。

## 5. API 与 Application 目标合同

### 5.1 Session 创建

当前错误边界：

```text
POST /agent/session?projectId=...
GetOrCreateSessionAsync(..., projectId, ...)
createAgentSession(projectId?)
```

目标合同：

```text
POST /agent/session
CreateUnboundSessionAsync(userId, idempotencyKey)
createAgentSession()
```

创建记录必须满足 `AgentSession.ProjectId == null`。旧 `projectId` 不得被静默接受为绑定、发现提示或默认项目；规范合同应移除该参数。迁移期间若仍收到旧参数，应返回明确的兼容错误，而不是创建一个调用方误以为已绑定的 Session。

### 5.2 Conversation 回合

当前错误边界：

```csharp
AppendTurnAsync(userId, projectId, sessionId, request)
ConversationTurnContext(UserId, ProjectId, SessionId, ...)
```

目标合同：

```csharp
AppendTurnAsync(userId, sessionId, request)
```

Application 通过 `sessionId + userId` 加载服务端 Session，并构造以下二选一上下文：

```text
UnboundConversationContext
  userId, sessionId, durable messages, generic resources,
  minimal accessible-project catalog

BoundConversationContext
  userId, sessionId, projectId, bindingVersion,
  durable messages, ProjectContextSnapshot, project tools
```

Node 传入的项目 ID 不能成为权限或上下文选择依据；即使内部 HTTP 为路由方便携带 projectId，ASP.NET 也必须与 Session 当前绑定交叉验证。

### 5.3 Context Builder

不能把现有 `AgentContextRequest.ProjectId` 简单改成 nullable 并让所有调用方承担空值风险。目标是拆分职责：

- Conversation Context Builder：明确支持 Unbound/Bound 两种结果。
- Project Context Snapshot Builder：只接受已验证的 projectId，加载 Canon、Story Bible、Goal、Proposal/Production、待人工决策和版本。
- GoalCommit/KernelExecution Context Builder：继续要求 projectId，保持项目执行强约束。

现有 `IAgentContextAssembler.BuildKernelExecutionAsync` 不因无项目 Conversation 而放宽。

## 6. Run Context 与持久化

每个用户消息触发 Run 时：

1. ASP.NET/Node 加载该 Conversation 的 provider-neutral durable messages。
2. ASP.NET 读取 Session 当前 binding pointer/version。
3. Unbound：仅构造通用上下文和最小项目目录。
4. Bound：读取最新带版本号的 ProjectContextSnapshot。
5. Node 创建新的 Pi `Agent` 或继续当前 Run，使用 Pi 原生停止条件。
6. ToolResult 返回变化对象的 ID、状态和版本；并发冲突返回 `version_conflict`/`reread_required`。

Provider-neutral message envelope 需要保留 assistant content blocks、Tool Call、ToolResult、error、自定义 `ProjectContextActivated` 消息、Conversation sequence、trace ID、时间和 correlation ID。Checkpoint/summary 只是可重建缓存，不是第二真源。

## 7. 安全、并发与故障语义

- Session 和 project catalog 都按当前认证用户过滤。
- 项目绑定 command 必须幂等；重复确认返回同一绑定结果。
- 每个 Conversation 只有一个 active Run Lease；不同 Conversation 可以并行。
- 共享 Proposal、Production、Candidate、Canon 继续使用 Application 授权、乐观版本和实体 lease/fence。
- 项目在确认前被删除或权限被撤销：绑定工具返回可恢复的 `project_unavailable`，Agent 继续运行并重新发现。
- 已绑定项目后权限被撤销：下一次 hydration 清除/冻结绑定并回到 Unbound，不向模型泄露旧项目新数据。
- 绑定成功后项目快照加载失败：返回可恢复错误，保留审计事实；不得把部分项目工具暴露给当前 Run。
- 任何项目事件都不会自动唤醒所有关联 Conversation。

## 8. 迁移原则

1. 先封闭 Session 创建入口，确保所有新 Conversation 都是 Unbound。
2. 再让 Conversation Application 支持无项目回合；在此之前不能删除现有项目参数造成空值崩溃。
3. 拆分 Context Builder，建立“未绑定绝不加载项目数据”的可执行测试。
4. 接入项目发现和绑定工具，再启用动态项目工具集。
5. 最后迁移 Pi Node Runtime 和完整 Agent→Canon E2E。

迁移期间不保留创建时绑定和工具确认绑定两条长期路径；短期兼容入口必须显式失败或被测试证明不会写入 `ProjectId`。

## 9. 延后事项

- 跨项目 Conversation 切换策略。
- handoff summary 的生成、压缩和继承。
- Node 模型 API Key/代理最终方案。
- gRPC 替换 HTTP/JSON。
- 自动 wake-up/显式 Conversation 订阅。
- `AgentHarness` 未实现能力。
