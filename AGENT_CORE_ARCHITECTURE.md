# Tianming Agent Core 分层架构基线

**状态：** 当前 Agent Loop 架构权威（2026-08-22 起）
**来源任务：** `.trellis/tasks/08-22-tianming-agent-core-foundation`
**历史计划降级：** `Docs/superpowers/specs/2026-07-12-novel-agent-target-architecture.md` 及其"统一上下文/固定生产链"方案已降级为背景资料，不再是当前架构真源。

---

## 1. 分层与依赖方向

```text
┌──────────────────────────────────────────────────────────────┐
│ tianming-web（ASP.NET + 浏览器 UI）                            │
│ 认证 / Application 权威 / PostgreSQL durable truth / HTTP+SSE │
└──────────────────────────────┬───────────────────────────────┘
                               │ internal runtime API + SSE
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-novel-agent（后续）                                   │
│ skills / roles / resources / domain tools / context adapter   │
└──────────────────────────────┬───────────────────────────────┘
                               │ generic AgentCore contract
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-agent-core（仓库根目录同名文件夹）                     │
│ message state / loop / serial tools / events / abort /        │
│ steering / follow-up / natural stop / maxTurns 安全上限        │
└──────────────────────────────┬───────────────────────────────┘
                               │ model port（streamFn 注入）
┌──────────────────────────────▼───────────────────────────────┐
│ tianming-ai（仓库根目录同名文件夹）                            │
│ 项目模型调用边界；第一阶段 adapter = @mariozechner/pi-ai@0.57.1 │
└──────────────────────────────────────────────────────────────┘
```

依赖只能向下：Web → Novel Agent Runtime → Core → AI。Core 不反向依赖 Novel Agent；AI 不知道任何小说或 Web 概念。

当前实际 package：

| 层 | 包名 | 路径 | 依赖 |
|---|---|---|---|
| AI 边界 | `@tianming/agent-ai` | `tianming-ai/` | `@mariozechner/pi-ai@0.57.1`（精确固定）、`@sinclair/typebox` |
| 通用 Core | `@tianming/agent-core` | `tianming-agent-core/` | 仅 `@tianming/agent-ai`（file:）+ `@sinclair/typebox` |

两个包都是独立 Node ESM 包（Node >=22、TypeScript 5.9、各自 lockfile），未被任何生产 caller 引用前不改变线上行为。

## 2. 各层运行时边界

- **tianming-web**：前端（`tianming-web/frontend`）是 UI/API/SSE 壳，不依赖 Node 包或模型 provider；后端（`tianming-web/backend`）的 ASP.NET 是认证、授权、Session/Conversation durable truth、领域事务、Outbox、Worker 和 Read Model 的唯一权威。控制面已于 2026-09-01 由任务 `08-31-promote-control-plane` 从 `old/` 提升至此，包含 Agent 四项目、`Tianming.Web` 宿主、小说领域内核 `Services/` 和四个测试项目。
- **Novel Agent Runtime**（迁移期宿主为 `old/Agent/Tianming.NovelAgent.PiRuntime`（legacy 隔离区））：只通过内部 API 使用已授权的 Application 能力；未来切换为新 Core 的宿主 adapter。
- **Core event 不是 durable truth**：事件只是宿主观察面；Web/Application 负责把消息映射为 PostgreSQL 持久化和 SSE。
- **模型调用只经 `@tianming/agent-ai`**：provider registry、token 计费、OAuth、HTTP provider、模型发现全部留在 pi-ai 内，项目内其他层禁止直接 import pi-ai。

## 3. Core Loop 合同（测试锁定）

- **Natural stop**：assistant 响应无 tool call 且 steering/follow-up 队列为空即结束一次 run；不存在固定 Proposal → Goal → Production 阶段链。`maxTurns` 只是安全上限，触发返回 `max_turns_exceeded`，不代表进入某业务阶段。
- **工具串行执行**：按模型返回顺序执行；异常转为 `isError=true` 的 tool result 继续交给模型；是否重试/停止由模型与宿主策略决定。参数先过 TypeBox schema 校验，失败同样成为可观察 error result。
- **Steering**：在工具执行边界检查；存在则跳过尚未执行的工具并追加用户消息进入下一轮。被跳过的 tool call 会合成 `isError=true` 的 "Skipped due to queued user message." 结果（沿用 Pi 参考实现），保证每个 toolCallId 都有匹配 toolResult、模型上下文对 provider 保持 well-formed。
- **Follow-up**：只在自然停止点消费并继续 loop，不抢占当前 assistant/tool turn。
- **Abort**：外部 AbortSignal 与主动 `abort()` 等价；信号传入模型流和工具执行；已完成消息保留，未完成 run 以 `aborted` 结束且不再执行后续 tool call（未执行的 call 同样补合成结果）。
- **事件集合（稳定顺序）**：`agent_start`、`agent_end`、`turn_start`、`turn_end`、`message_start`、`message_update`、`message_end`、`tool_execution_start`、`tool_execution_update`、`tool_execution_end`。完整顺序由 `test/agent-core.test.ts` 精确断言锁定。
- **状态仅含**：systemPrompt、model、messages、tools、isRunning、streamMessage、error。无小说域字段。

## 4. 对 Pi 的借鉴与不照搬

借鉴（设计层面）：

1. natural stop condition（无 tool call 且无 steering/follow-up 即停）；
2. 工具按模型顺序串行执行 + 失败转 error tool result；
3. steering 在工具边界抢占、follow-up 只在自然停止点生效；
4. 事件作为观察面而非持久化协议；
5. maxTurns 作为纯安全护栏。

不照搬（第一阶段约束）：

1. **不直接依赖 `@mariozechner/pi-agent-core`**——避免本阶段沦为换名包装；Tianming Core 的公共契约由本项目测试锁定（`tianming-agent-core/test/`）。
2. 不引入 AgentHarness/durable session/resume 协议——pi-agent-core 0.57.1 中这些 API 明确抛 `HarnessNotImplemented`，不能支撑集成。
3. 不导入 pi-coding-agent 宿主生态（skills/permissions/themes 等）；novel-domain 行为后续通过 tools/hooks/context 围绕 Core 实现。
4. pi-ai 版本固定 0.57.1（npm `@earendil-works/pi-*` 命名空间自 0.74 才存在且 API 已演化），不得升级到未验证版本。

## 5. dsh-story 可借鉴模式与边界

可借鉴：skill bridge 组织方式、role 工具白名单/maxDepth、缺主契约 fail-fast、单一权威状态 + 派生视图。

不照搬：固定 13 阶段生产流程、文件系统 tracking state 当真源、单写者假设、把 hook 当事务授权。（证据：`Docs/research/oh-story-dsh/ANALYSIS.md`）

## 6. Legacy 隔离规则

以下路径冻结为 legacy，不再接受新 Agent 能力；本阶段不要求删除：

- `old/Agent/Tianming.NovelAgent.PiRuntime` 直接持有 pi-agent-core `Agent` 的 turn path；
- C# MAF adapter（`tianming-web/backend/Tianming.NovelAgent.Infrastructure/Conversation/MafConversationAgentRuntime.cs`）、Structured Runtime（`StructuredConversationAgentRuntime`）、`TargetArchitectureDirector`；
- legacy Web turn path 及其控制面写入者，含 `AgentController` 的 `/agent/chat` 兼容入口。

删除条件（必须同时满足）：生产 callers 为零、替代路径有 targeted regression、历史数据/迁移仍可读取、完整回归通过。

各项实测达标情况（2026-09-01，见任务 `09-01-retire-legacy-runtimes`）：

| 冻结项 | 条件 1 生产 callers 为零 | 可删 |
|---|---|---|
| MAF adapter | ✅ `Program.cs` 从未调用 `AddMafConversationRuntime` | 是（守卫断言需先扩展到 Infrastructure 程序集） |
| `TargetArchitectureDirector` | ❌ `Program.cs:194`、`:385` 现役 `IAgentForegroundTurnRunner` | 否 |
| `StructuredConversationAgentRuntime` | ❌ `Program.cs:306`，`PiRuntime:Enabled` 为假时的默认兜底 | 否（属产品可用性决定） |
| PiRuntime 持有 pi-agent-core | ❌ `package.json:16`、`src/runtime.ts:1` 直接依赖 | 否 |
| `/agent/chat` | 待核验新前端调用情况 | 待定 |

当前未迁移 legacy caller：`old/Agent/Tianming.NovelAgent.PiRuntime/src/runtime.ts`（仍使用 pi-agent-core Agent）。迁移顺序见第 7 节。

## 7. 后续迁移顺序

1. ✅ 新 Core/AI package 独立通过测试（本任务）；
2. 新 Novel Agent Runtime adapter 只依赖新 Core；
3. 用最小 deterministic Novel Agent vertical slice 验证 adapter；
4. 迁移现有 project discovery/binding tools；
5. API/SSE/browser evidence 齐全后，才删除旧 NovelPiRuntime 直接持有 Pi Agent 的路径、MAF adapter 和 legacy Web turn path。

**Playwright AC-15 前置条件**：浏览器 E2E 必须以稳定后的 Core/Novel Agent Runtime event/tool/runtime 契约为前提，另建任务实施；Core 单元测试不能替代真实 Web/Node/API/SSE 验证。

## 8. 本任务明确不做的事

不在 Core 里实现 Proposal→Goal→Production 固定链；不将小说 prompt、项目上下文、授权规则塞进 Core；不把 ASP.NET transaction/persistence 推给 Node Agent；不为 Playwright 反向污染生产 health/DB routing；不把历史整合计划的 Director 自动生产表述当作合同；不删除任何 legacy 代码。
