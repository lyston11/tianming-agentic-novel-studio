# 退役 wwwroot 旧前端托管，解除 /agent/chat 的最后阻塞

## 1. 目标与用户价值

`AGENT_CORE_ARCHITECTURE.md` 第 6 节把 `/agent/chat` 列为冻结项，删除条件是"生产 callers 为零"。当前不满足，**唯一实质原因是后端仍在托管一份 2026-08-22 构建的旧前端**：`Program.cs:587-598` 的 `UseDefaultFiles` + `UseStaticFiles` + `MapFallbackToFile("index.html")` 把 `wwwroot/assets/index-BaN8_1u2.js` 当作可访问 SPA 提供出去，而该 bundle 内含 `/agent/chat` 调用。

解掉它带来两件事：`/agent/chat` 可以真正退役；`legacy` 这个标签从"贴上但仍在服流量"变成名副其实。这是把冻结区从 5 项收敛到 4 项的第一块砖。

## 2. 立项前实测（先纠正一个既有认知）

`CLAUDE.md` 与 `AGENT_CORE_ARCHITECTURE.md` 都写「`/agent/chat` 唯一调用方是 `wwwroot/` 里的旧前端」。**这句话不准确。**

```text
/agent/chat 的全部引用点：
  AgentController.cs:49                                  端点定义
  wwwroot/assets/index-BaN8_1u2.js                       旧 bundle（后端仍在托管）
  old/Web/NovelAgentWeb.Frontend/src/api/index.ts:457    历史快照，不参与构建
  tianming-web/frontend/src/api/chat.ts:14               ← 当前前端也在调用
  tianming-web/frontend/src/api/index.ts:135             ← 且被 re-export
```

但再往上一层追，`sendChat` 在 UI 层**零命中**——当前会话实际走 `/novel-agent/conversations/{sessionId}/turns`（`src/api/novel-agent.ts:19`）。所以新前端侧是**死导出，不是真依赖**，可与端点一并移除，不需要先做前端迁移。

这条修正让本任务从"需前置前端改造"降级为"可独立完成"，也是先立项核对而非照抄文档的收益。

## 3. 需求

### R1 — 判定生产部署拓扑（前置，不可跳过）

`MapFallbackToFile("index.html")` 不只服务旧 bundle，它同时是 SPA fallback 机制本身。dev 环境前端独立跑 `:3002` 并经 Vite 代理访问 `:5002`，不依赖它；但**非 dev 部署形态未确认**。必须先判定三者之一：

- **A 前端独立托管**（nginx / 静态托管 / 容器分离）：后端完全不需要静态文件中间件 → 删除 `UseDefaultFiles` + `UseStaticFiles` + `MapFallbackToFile` 与整个 `wwwroot`。
- **B 后端同源托管**：需保留 fallback，但必须指向**新前端**构建产物，并建立 `tianming-web/frontend` build → `wwwroot` 的产出链（当前 `wwwroot` 是手工提交的陈旧产物，5 个文件全部 git tracked，无任何构建链关联）。
- **C 两种都要支持**：静态托管改为配置开关，默认关闭。

证据来源：`old/docker-compose.yml` 的 api 服务定义、部署脚本、以及 `wwwroot` 是否出现在 publish 流程。判定结论必须写入 notes 并成为后续步骤依据。

### R2 — 处置 wwwroot 旧 bundle

按 R1 结论执行。若为 A：删除 `wwwroot/` 全部 5 个文件（`index.html`、`assets/index-BaN8_1u2.js`、`assets/index-BR5WX5NH.css`、`icons.svg`、`favicon.svg`，748K）。若为 B/C：`wwwroot` 内容改由前端构建产出，旧 bundle 删除且不再手工提交，并在 `.gitignore` 处置产物目录。

无论哪条路径，结束状态必须满足：**后端不再向任何客户端提供含 `/agent/chat` 调用的 JS。**

### R3 — 退役 /agent/chat 端点

R2 完成后，`AgentController.cs:49` 的 `[HttpPost("agent/chat")]` 及其专属请求/响应 DTO、以及仅服务于它的内部路径一并移除。同步移除前端死代码：`src/api/chat.ts:14` 的 `sendChat` 与 `src/api/index.ts:135` 的 re-export。`AgentChatRequest` / `AgentChatResponse` 类型若无其他消费点则一并清理（注意二者现为 OpenAPI 生成物，需重新导出契约）。

### R4 — 回流 guard

`Tests/Unit/Architecture/TargetArchitecturePurityTests.cs` 新增断言，覆盖两个层面：

- `Program.cs` 不含 `MapFallbackToFile`（或按 R1-B/C 结论，断言其指向新产物而非旧 bundle）；
- `AgentController.cs` 不含 `agent/chat` 路由。

参照该文件既有 `LegacyLoopProductionComponents_AreAbsent` 的文件存在性断言写法。**注意教训**：`09-01-retire-legacy-runtimes` 的经验表明单一层面 guard 会漏——程序集级断言看不见未被代码使用的 `PackageReference`，同理"文件已删"看不见"托管配置仍在"。两个断言都要有。

### R5 — 文档同步

- `AGENT_CORE_ARCHITECTURE.md` 第 6 节表格：`/agent/chat` 行标记为已删除并注明日期与任务，同时**修正**"唯一调用方是旧前端"的失准表述。
- `CLAUDE.md` 关键文件清单：移除 `AgentController.cs` 的 `/agent/chat` 兼容入口描述与 `UseStaticFiles` + `MapFallbackToFile` 托管说明。

## 4. 验收标准

- [ ] **AC-1 拓扑判定成文**：R1 的三选一结论有证据支撑并写入任务 notes；后续改动与结论一致。
- [ ] **AC-2 旧 bundle 不再可达**：后端启动后请求 `/` 与 `/index.html` 不再返回含 `/agent/chat` 的 JS（A 路径下返回 404；B/C 路径下返回新前端产物）。以实际 HTTP 响应为证，非仅代码审查。
- [ ] **AC-3 端点已删**：`/agent/chat` 返回 404；`AgentController.cs` 无该路由。
- [ ] **AC-4 前端死代码已清**：`sendChat` 及其 re-export 移除；`grep -rn "agent/chat" tianming-web/frontend/src` 仅可能命中 `schema.d.ts`（生成物，须随契约重新导出而消失）。
- [ ] **AC-5 契约同步**：`export-openapi.sh` 重新导出后 `openapi.json` 不含 `/api/agent/chat`；`npm run gen:check` 通过且不修改工作区。
- [ ] **AC-6 guard 双层**：R4 的两个断言均存在且在移除前会失败、移除后通过（可证伪）。
- [ ] **AC-7 不回归**：后端 Unit 845/845（+ 新增 guard）、AgentArchitecture 27/27；前端 `typecheck` / `lint` / `test` 17 个 / `build` 全通过。`NovelAgentRegression` 若含 SPA fallback 或 `/agent/chat` 相关断言需同步并跑通（需 Docker）。
- [ ] **AC-8 文档准确**：R5 两处更新完成，含对旧表述的显式修正。
- [ ] **AC-9 收口**：`task.py validate` 与 `git diff --check` 通过。

## 5. 不做的事

- 不动 `/novel-agent/conversations/*` 现役会话路径。
- 不退役其余 4 项冻结物（`TargetArchitectureDirector`、`StructuredConversationAgentRuntime`、PiRuntime 持有 pi-agent-core、legacy Web turn path）——各有独立阻塞，另建任务。
- 不删除 `old/Web/NovelAgentWeb.Frontend/`（历史快照，不参与构建，无生产影响）。
- 不借机重构 `AgentController` 其余端点。
- 不引入新的前端部署基础设施；R1 判定为 B/C 时只建立最小构建产出链。

## 6. 风险与对策

| 风险 | 对策 |
|---|---|
| `MapFallbackToFile` 同时是新前端生产部署的 SPA fallback，直接删除破坏非 dev 形态 | R1 设为不可跳过的前置判定；证据取自 compose/部署脚本/publish 流程，而非假设 |
| `AgentChatRequest/Response` 可能被 `/agent/chat` 之外的端点复用 | 删除前 grep 全部消费点；OpenAPI 重新导出后由 `gen:check` 兜底 |
| 旧 bundle 删除后历史部署无法回滚到该版本 | 5 个文件均 git tracked，历史可从 git 取回；无需保留工作区副本 |
| `NovelAgentRegression` 可能有依赖静态托管的 E2E（需 Docker 才能发现） | AC-7 显式要求跑该套件；本机无 Docker 时须记录为未验证项而非默认通过 |

## 7. 为什么值得做

这是把"legacy"从标签变成事实的最小一步，且已被实测降级为可独立完成——不需要等语言边界裁决、不需要前端迁移。收益是冻结区少一项、后端不再对外提供一份两周前的陈旧 SPA、`/agent/chat` 这条绕过 `ConversationApplicationService` 的旁路彻底关闭。

成本主要落在 R1 判定，不在代码改动本身。
