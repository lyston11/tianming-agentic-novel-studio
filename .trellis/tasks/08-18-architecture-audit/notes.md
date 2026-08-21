# 小说 Agent 架构审计决策记录

更新时间：2026-08-20

## 当前任务状态

任务仍处于 Planning。本文记录已经由用户确认的架构决策、代码证据和后续实施警戒线；它不是完成声明，也不授权立即修改产品代码。

现有 `audit-report.md` 和 `handoff-summary.md` 包含较早阶段对 Agent/Workflow 集成的判断，其中固定链式路径、自研 runtime、自动整书启动等内容已经过时。当前权威方向以 `prd.md` 与 `design.md` 为准。

## 已确认决策

### 1. Agent Loop 与进程边界

- 通用 Agent Loop 使用 `@earendil-works/pi-agent-core.Agent`，provider 使用 `@earendil-works/pi-ai`。
- 不整体引入 `pi-coding-agent`，只借鉴其宿主层 skill/resource/tool 模式。
- `AgentHarness` 仍有 `HarnessNotImplemented`，不能承担生产 durable session/recovery。
- Pi Runtime 是独立 Node Service，通过 HTTP/JSON 调 ASP.NET 内部 Application API。
- PostgreSQL 是 Conversation、Message 和项目业务状态唯一真源；Node 只持有单次 Run 内存。

### 2. Agent 驱动而非固定业务链

- Pi Agent 根据当前上下文和 ToolResult 自主选择观察和领域工具，不预设 Proposal→Goal→Production 的固定调用链。
- 长任务由 Worker、lease/fence、Outbox 和恢复机制推进。
- Acceptance 与 Canon Merge 不属于 Agent action vocabulary，也不注册为 Tool；它们是用户 Web 控制面动作。
- 项目事件只更新权威状态和通知 Web 刷新，不自动唤醒所有相关 Conversation。

### 3. Conversation 初始无项目

- 每个新 Conversation 都是 Unbound，不自动继承任何项目。
- Agent 可以自动发现候选项目，但只读取用户可访问项目的最小目录信息。
- 用户必须明确确认具体项目后，Conversation 才能激活项目上下文。
- Conversation 不复制其他 Conversation 的原始 transcript；handoff summary/压缩暂缓。

### 4. “必须确认”不是 Agent Loop 硬门槛

用户补充确认：项目绑定虽然必须由用户明确确认，但不应实现成阻断 Agent 执行的硬性检测门槛。

正确解释：

- 未确认时 Pi Loop 正常运行。
- Agent 可以继续聊天、澄清、发现项目、展示候选和使用通用能力。
- 项目发现由 skill/resource/只读 Tool 提供。
- 用户明确确认后，Agent 调用窄的项目上下文激活 Tool，或 Web 候选卡调用同一个 Application command。
- 缺少确认来源时，Tool 返回可恢复的 `confirmation_required`，Agent 继续询问；不 abort、不 terminate。
- 约束作用在“可见上下文和可用能力”上，不作用在“Agent 是否允许运行”上。
- 绑定只更新 Conversation context，不创建 Goal、不启动 Production、不执行其他领域副作用。

### 5. 两个不能写乱的实现边界

#### 5.1 新会话创建

目标不是“继续保留 projectId 参数但约定传 null”，而是从规范合同删除该输入：

```text
POST /agent/session
CreateUnboundSessionAsync(userId, idempotencyKey)
createAgentSession()
```

新数据库记录必须 `ProjectId = NULL`。旧 projectId 不能静默绑定或静默充当发现提示。

#### 5.2 Conversation API 与 Context Builder

Conversation 回合不能继续要求调用方传 projectId，也不能信任 Node/Web 传来的 projectId。目标签名按 `userId + sessionId` 加载服务端绑定：

```text
Unbound → 通用上下文 + 最小项目目录
Bound   → 通用上下文 + 最新版本化项目 Read Model + 项目工具
```

不能简单把现有 `AgentContextRequest.ProjectId` 全部改成 nullable。只对 Conversation 引入明确的 Unbound/Bound 上下文；Goal commit、Kernel execution、Canon 等执行路径继续要求强项目作用域。

## 当前代码证据

- `Web/NovelAgentWeb/Controllers/AgentController.cs`：`CreateSession` 仍接受 query `projectId` 并传给 Session service。
- `Web/NovelAgentWeb/Services/AgentSessions/AgentSessionService.cs`：创建实体时会直接写 `ProjectId = projectId`。
- `Web/NovelAgentWeb.Frontend/src/api/index.ts`：`createAgentSession(projectId?)` 仍暴露创建时项目参数。
- `Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx`：当前页面实际调用 `createAgentSession()`，说明删除 helper 参数不会改变当前主路径意图。
- `Agent/Tianming.NovelAgent.Application/Conversation/ConversationApplicationService.cs`：`AppendTurnAsync` 对 projectId 执行必填校验并传入 Runtime、Store、Event 和 Tool 路径。
- `Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs`：`ConversationTurnContext.ProjectId` 当前是非空字符串。
- `Web/NovelAgentWeb/Services/Context/AgentContextContracts.cs`：Conversation、GoalCommit 和 ChapterExecution 共用强制 projectId 的 `AgentContextRequest`。
- `Web/NovelAgentWeb/Services/Context/AgentContextAssembler.cs`：会直接加载 Goal、Memory、Knowledge 和 PendingIntents，因此不能在未绑定阶段调用现有项目组装路径。
- `Web/NovelAgentWeb/Support/AgentSession.cs` 与数据库映射已经允许空项目，具备 Unbound 数据基础。

## 风险警戒线

- 不要用一个全局 `beforeToolCall` gate 拦截所有未绑定 Conversation。
- 不要让唯一候选自动绑定。
- 不要提前加载项目正文后只在 UI 隐藏。
- 不要把确认绑定和 Goal/Production 副作用合并为一个工具。
- 不要因支持 Unbound Conversation 而放宽 Kernel/Canon 的项目作用域。
- 不要信任 Node 的 active project 作为最终授权依据。
- 不要为项目事实建立每 Conversation 的 Inbox、eventId 或 sequence 消费模型。
- 不要围绕 Pi 增加新的大型 NovelAgentOrchestrator。

## 延后决定

- 同一 Conversation 是否以及如何切换项目。
- Project handoff summary 的结构、生成和压缩策略。
- Node 模型凭据使用短期凭据还是 ASP.NET 模型代理。
- 是否以及何时从 HTTP/JSON 迁移到 gRPC。
- 是否为少数显式订阅场景增加 Agent 自动 wake-up。

## 下一次规划复核重点

1. 确认 `activate_project_context` 的用户确认来源采用对话消息、Web 候选卡，还是两者共享同一个 command；当前设计允许两者共存。
2. 明确旧 `POST /agent/session?projectId=` 的兼容返回形态，但无论选择何种状态码，都不得创建 Bound Session。
3. 决定实施工作是否拆成 Session/Context、Project Discovery/Binding、Node Pi Runtime、Agent→Canon E2E 四个子任务。
