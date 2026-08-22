# Tianming Agent Core 分层技术设计

## 1. 目标架构

```text
┌──────────────────────────────────────────────────────────────┐
│ tianming-web                                                 │
│ ASP.NET auth / application authority / PostgreSQL / HTTP/SSE │
│ Browser UI shell                                             │
└──────────────────────────────┬───────────────────────────────┘
                               │ internal runtime API + SSE
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-novel-agent runtime                                 │
│ skills / resources / roles / domain tools / context adapter  │
└──────────────────────────────┬───────────────────────────────┘
                               │ generic AgentCore contract
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-agent-core                                          │
│ message state / loop / tools / events / abort / steering      │
└──────────────────────────────┬───────────────────────────────┘
                               │ model port
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-ai                                                  │
│ project model boundary; first adapter is @mariozechner/pi-ai │
└──────────────────────────────────────────────────────────────┘
```

依赖只能向下：Web → Novel Agent Runtime → Core → AI。Core 不反向依赖 Novel Agent；AI 不知道任何小说或 Web 概念。

现有 `Agent/Tianming.NovelAgent.PiRuntime` 是迁移期宿主。第一阶段不重写它，只让新包成为未来替换点；旧 C# MAF、Structured Runtime、TargetArchitectureDirector 和 legacy Web path 作为 legacy，不再接受新 Agent 能力。

## 2. tianming-ai 边界

第一阶段建立独立 Node package `Agent/Tianming.Agent.Ai`，内部固定依赖 `@mariozechner/pi-ai@0.57.1`。它只提供项目自己的入口类型和适配函数：

- 模型描述和流函数类型；
- user/assistant/tool result 消息类型；
- assistant stream event 类型；
- tool schema/模型上下文类型；
- `streamSimple` 和工具参数校验等 Pi 能力的受控出口。

Core 只从 `@tianming/agent-ai` 导入这些类型，不直接导入 Pi 包。这样第一阶段可以复用 pi-ai 的 provider 实现，未来换模型实现时只修改 AI adapter，而不修改 Loop 或 Novel Agent。

本阶段不创造第二个 provider registry，不复制 token 计费、OAuth、HTTP provider 和模型发现逻辑。

## 3. tianming-agent-core Loop

Core 建立 `AgentCore`，其状态只包含通用运行所需信息：

```text
AgentState
├── systemPrompt
├── model
├── messages
├── tools
├── isRunning
├── streamMessage
└── error
```

一次 prompt 的控制流：

```text
prompt(user message)
  → append user message + agent_start
  → turn_start
  → transform context
  → convert to model context
  → stream assistant response
  → if tool calls: sequential tool execution
  → append tool results + turn_end
  → consume steering messages
  → if no tools/steering: consume follow-up messages
  → if no follow-up: agent_end / natural stop
```

### Natural stop

业务无关的停止条件固定为：当前 assistant response 没有 tool call，且 steering/follow-up 队列为空。`maxTurns` 只作为安全上限，触发时返回明确的 `max_turns_exceeded` 终止原因，不表示进入某个小说阶段。

### Tool contract

Core 工具只知道名称、描述、schema 和 `execute`。Core 不理解项目、章节、Goal 或 Canon。工具异常会生成 `isError=true` 的 tool result 并继续交给模型；工具是否重试、改变计划或停止由模型/宿主策略决定。工具按模型顺序串行执行，以保留副作用顺序和 steering 插入点。

### Steering/follow-up

- steering 在工具执行边界检查；存在 steering 时跳过尚未执行的工具并追加用户消息。
- follow-up 只在自然停止点检查，不抢占当前 assistant/tool turn。
- 两者都以 `AgentMessage` 进入上下文，事件顺序与普通消息一致。

### Abort

Core 接受外部 `AbortSignal`，也提供 `abort()`。信号传入 AI stream 和工具执行；已经完成的消息保留，未完成 run 以 `aborted` 结束，不再执行后续 tool call。

### Events

首版事件固定为：`agent_start`、`agent_end`、`turn_start`、`turn_end`、`message_start`、`message_update`、`message_end`、`tool_execution_start`、`tool_execution_update`、`tool_execution_end`。事件是宿主的观察面，不是 durable truth；Web/Novel Runtime 自行映射为持久化消息和 SSE。

## 4. Novel Agent 边界

后续 `tianming-novel-agent` 可以拥有：

- `Skill`：说明、资源引用、输入/输出合同、校验规则；
- `Role`：工具白名单、资源白名单、最大深度、完成策略；
- `DomainTool`：通过 Application port 提交意图，不直接操作数据库；
- `ContextProvider`：生成项目/会话上下文；
- `Hook`：保护前置条件和版本不变量。

借鉴 dsh-story 的 skill bridge、role whitelist/maxDepth、fail-fast 缺失主契约、单一权威状态与派生视图；不照搬其固定 13 阶段流程、文件系统 tracking state、单写者假设或把 hook 当成事务授权。

## 5. Web 边界

`tianming-web` 前端是 UI/API/SSE 壳，后端是认证和应用权威，不是单纯的 TUI：

- 前端不依赖 Node 包或模型 provider；
- ASP.NET 负责认证、授权、Session/Conversation durable truth、领域事务、Outbox、Worker 和 Read Model；
- Node Runtime 只通过内部 API 使用已授权的 Application 能力；
- Core event 不直接写 PostgreSQL，Web/Application 负责 durable mapping。

## 6. Legacy 隔离与迁移顺序

第一阶段采用“旁路新骨架、旧路径冻结”的策略：

1. 新 Core/AI package 先独立通过测试；
2. 新 Novel Agent Runtime adapter 只依赖新 Core；
3. 用一个最小 deterministic Novel Agent vertical slice 验证 adapter；
4. 再迁移现有 project discovery/binding tools；
5. 迁移完成且 API/SSE/browser evidence 齐全后，才删除旧 `NovelPiRuntime` 直接持有 Pi Agent 的路径、MAF adapter 和 legacy Web turn path。

任何删除必须满足：生产 callers 为零、替代路径有 targeted regression、历史数据/迁移仍可读取、完整回归通过。

## 7. 明确不做的事情

- 不在 Core 里实现 Proposal→Goal→Production 固定链；
- 不将小说 prompt、项目上下文、授权规则塞进 Core；
- 不把 ASP.NET transaction/persistence 推给 Node Agent；
- 不为了 Playwright 反向污染生产 health/DB routing；
- 不把 dsh-story 的文件 tracking state 直接当作 PostgreSQL 领域真源；
- 不把历史整合计划中“Director 自动生产”等表述当作当前合同。

## 8. 验收和后续依赖

本任务完成后，Playwright AC-15 才能基于稳定 Core event/tool/runtime contract 设计。AC-15 仍需另建任务，且必须验证真实 Web/Node/API/SSE；Core 单元测试不能替代浏览器 E2E。
