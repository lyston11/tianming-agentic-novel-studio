# 研究笔记：退役 wwwroot 旧前端托管

## R1 判定结论：**A —— 前端独立托管**（AC-1）

后端不需要任何静态文件中间件。删除 `UseDefaultFiles` + `UseStaticFiles` +
`MapFallbackToFile` 与整个 `wwwroot/`。

### 证据链

**证据 1 —— compose 里没有任何前端托管者。**
`old/docker-compose.yml` 的 `api` 服务是唯一暴露端口的服务（`5002:5002`）。
全文件只有 4 个服务：`postgres`、`postgres-role-bootstrap`、`qdrant`、`redis`、
`api`。**没有 nginx、没有静态托管容器、没有 frontend 服务。**

**证据 2 —— 该 Dockerfile 构建不出当前后端（决定性）。**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build   # ← .NET 8
WORKDIR "/src/Web/NovelAgentWeb"                 # ← 已不存在的路径
```

两处都对不上当前仓库：

| Dockerfile 假设 | 当前实际 |
|---|---|
| SDK 8.0 | `global.json` 锁 `10.0.400`，`net10.0` |
| `/src/Web/NovelAgentWeb` | `tianming-web/backend/Tianming.Web` |

后端已于 2026-09-01（任务 `08-31-promote-control-plane`）从 `old/` 提升，
Dockerfile 没有跟着更新。所以这份 compose 只能构建 `old/` 那个冻结后端。

**结论：当前后端不存在任何可工作的容器部署形态。** 因此不存在"生产 SPA
fallback 职责"需要保留——PRD 第 6 节列的 keyRisk 经实测不成立。

**证据 3 —— 没有任何构建链产出 wwwroot。**
`grep -rln "wwwroot" --include="*.sh" --include="*.yml" --include="*.ps1"` 零命中。
前端 `package.json` 的 `build` 是 `tsc -b && vite build`，无 outDir 指向后端，
无 copy 步骤。`wwwroot` 5 个文件全部手工提交，与构建链零关联。

**证据 4 —— dev 形态不依赖它。**
前端独立跑 `:3002`，经 Vite 代理到 `:5002`（`vite.config.ts:15`
`TIANMING_BACKEND_ORIGIN`）。删除静态托管对 dev 零影响。

### 为什么不选 B/C

选 B（后端同源托管）需要新建 `frontend build → wwwroot` 产出链，那是
PRD 第 5 节明确排除的"新前端部署基础设施"。当没有任何可工作的部署形态需要它时，
建这条链是为假想需求付成本。真要做同源部署，届时连 Dockerfile 一起修才对，
那是独立任务。

## 关于 `/agent/chat` 调用方的二次修正

PRD 第 2 节已修正 `CLAUDE.md` 的"唯一调用方是旧 bundle"。实测再补一条：
`tianming-web/frontend/src/api/chat.ts:14` 的 `sendChat` 不只在 UI 层零命中，
连 `src/api/index.ts:135` 的 re-export 也无人 import。两处都是纯死代码。

## 关于 DTO 删除范围的重要修正（缩小 R3）

PRD R3 写"`AgentChatRequest` / `AgentChatResponse` 类型若无其他消费点则一并清理"。
实测：**`AgentChatResponse` 有大量其他消费点，不能删。**

| 类型 | 其他消费点 | 处置 |
|---|---|---|
| `AgentChatRequest` | 仅 `/agent/chat` 端点 + `AgentControllerResumeTests` | **删** |
| `AgentChatResponse` | SSE 载荷（`AgentController.cs:228,246`）、`TargetArchitectureDirector.cs:146`、`AgentForegroundTurnRunner.cs:14-18`、`AgentTurnCoordinator.cs:29,52`、`AgentChatIdempotencyService`（+ `AgentChatRequestReceipt` 实体 + 4 份迁移 snapshot） | **保留** |

`AgentChatResponse` 是 SSE 事件的载荷类型，与 `/agent/chat` 无关；
`AgentChatResponsePublicProjection` 同理（它剥离内部调试字段，是安全边界）。
删它会连带砸掉 SSE 和幂等表。PRD 的措辞留了"若无其他消费点"的前提，
实测该前提不成立，故只删 `AgentChatRequest`。

`AgentChatIdempotencyService` 与 `AgentChatRequestReceipt` 表同样保留：
名字带 Chat 但服务对象不是这个端点，且有迁移历史，删除属独立的数据迁移决策。

## 未验证项（须记录而非默认通过）

`NovelAgentRegression` 需 Docker（Testcontainers 起真 PostgreSQL）。源码级已确认
该套件无 `MapFallbackToFile` / `agent/chat` / `index.html` 断言
（`git ls-files 'Tests/**/*.cs' | xargs grep`，零命中），故不预期受影响；
但按 AC-7 要求，若本机无 Docker 则须显式记为未验证。

## 实施记录（2026-09-01，本任务收口）

### 改动清单

- `Program.cs`：删除 `UseDefaultFiles()` + `UseStaticFiles()`（原 :587-588）与 `MapFallbackToFile("index.html")`（原 :598）。
- `wwwroot/`：`git rm -r` 全部 5 个文件（748K，历史可从 git 取回）。
- `AgentController.cs`：删除 `/agent/chat` 端点、构造注入的 `ConversationApplicationService _conversations`（仅该端点使用）与两个 using。
- `DTOs/Requests.cs`：删除 `AgentChatRequest` record（`AgentChatResponse` 保留，见上文 DTO 范围修正）。
- 前端：`api/chat.ts` 删 `sendChat`，`api/index.ts` 删 re-export，`api/types.ts` 删 `AgentChatRequest` 别名。
- 契约：`export-openapi.sh` 重导出 `openapi.json`（90 paths / 81 schemas，`/api/agent/chat` 路径与 `AgentChatRequest` schema 消失），`npm run gen:api` + `gen:check` 通过。
- guard（R4 双层）：`TargetArchitecturePurityTests` 新增 `LegacyFrontendBundle_IsNeitherHostedNorCommitted`（托管中间件 + wwwroot 目录）与 `AgentController_NoLongerExposesTheLegacyChatEntry`（路由 + DTO）。

### AC-2/AC-3 实际 HTTP 证据（非代码审查）

Development 环境真启动后端（连真实 PG/Qdrant/Redis 栈）：

- `GET /` → **404**
- `GET /index.html` → **404**
- `POST /api/agent/chat` → **404**

### guard 可证伪性（AC-6）

guard 在主体移除前就写入工作区（红状态设计），对 HEAD 内容可证伪：

- `git show HEAD:Program.cs` 含 3 处静态托管中间件 → `Assert.DoesNotContain` 必然失败；
- `git show HEAD:AgentController.cs` 含 2 处 `agent/chat|AgentChatRequest` → 必然失败；
- HEAD wwwroot 有 5 个 tracked 文件 → `Assert.False(Directory.Exists(...))` 必然失败。

移除后两 guard 转绿，随 Unit 套件通过。

### 测试数学（AC-7，减覆盖有据可查，非静默）

Unit 845 → **843/843 全绿**：−3（`AgentControllerResumeTests` 的三个 Chat 端点测试，被测主体已删）、−1（`AgentChatCompatEntry_PersistsOnlyThroughApplicationConversation`——它断言该端点"只经 Application Conversation 落库"，端点退役后断言主体消失，由新 guard 接管）、+2（新 guard）。

前端：typecheck ✅ / lint 0 错误（6 条存量 `set-state-in-effect` 警告，与本次改动文件无关）/ vitest 17/17 ✅ / build ✅。

`NovelAgentRegression`：源码级 grep 确认零 SPA fallback / `agent/chat` 断言（见上文），PRD 的"若含相关断言需同步并跑通"条件不触发，无需重跑。

### 实施中发现并处理的构建坑

删除 wwwroot 后首次启动 host 崩溃：`bin/` 里陈旧的 staticwebassets manifest 仍引用已删除的 wwwroot 目录（`StaticWebAssetsLoader` 在 Development 下无条件加载）。`dotnet clean` + 重建后消失。教训与 demote 任务的 `dist/` 教训同构：删除静态资源后必须清理构建产物，否则读回来的是幽灵。
