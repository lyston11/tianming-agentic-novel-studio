# API 契约单一真源：OpenAPI 生成前端类型 + 对账门禁

## 1. 目标与用户价值

把前端 API 类型从"人肉跟着后端 DTO 改"变成"从后端 OpenAPI 生成 + CI 对账"。

用户价值是防止一类特定 bug：后端改了 DTO、前端类型没跟上，`tsc` 依然通过（因为手写类型自成一体），错误在运行时才暴露成字段 undefined 或解析失败。当前 `tianming-web/frontend/src/api/types.ts` 有 **2817 行手写类型**镜像 C# DTO，没有任何机制保证它和后端一致。

本任务与语言选择无关，不触碰控制面归属，因此和 `08-31-promote-control-plane` 的 C# 决定完全兼容。

## 2. 背景与实测证据

### 2.1 生产端已经存在

- `old/Web/NovelAgentWeb/NovelAgentWeb.csproj:78` 已引入 `Swashbuckle.AspNetCore 6.5.0`。
- `old/Web/NovelAgentWeb/Program.cs:86` 已 `builder.Services.AddSwaggerGen()`。
- `old/Web/NovelAgentWeb/Program.cs:566-567` 已 `app.UseSwagger()` + `app.UseSwaggerUI()`。

**OpenAPI 文档已经在运行时对外提供。** 本任务不需要新增后端能力，只需要消费它。

### 2.2 消费端完全缺失

`tianming-web/frontend/package.json` 的 scripts 只有 `dev`/`build`/`typecheck`/`lint`/`test`/`test:watch`/`preview`——**零 codegen**。

前端 api 层实际构成（`tianming-web/frontend/src/api/`，共 4345 行）：

| 文件 | 行数 | 性质 |
|---|---|---|
| `types.ts` | **2817** | 手写，镜像 C# DTO，无生成机制 |
| `client.ts` | 346 | 信封合同 / 幂等键 / 重试 / SSE 工厂 |
| 其余 14 个按域端点模块 + auth-store + 测试 | 1182 | 手写调用 |

08-29 前端重建 PRD 明确记录 `types.ts` 是从旧前端"整体移植"的（原样搬迁 2817 行），不是重新生成的。

### 2.3 EcomGen 的参照实现

EcomGen 的链路（`EcomGen/package.json` scripts + `EcomGen/scripts/generate-openapi.mjs`）：

```text
packages/contracts/src/*.ts   (TypeBox，唯一手写真源)
  → pnpm gen:openapi         → openapi.yaml
  → pnpm --filter web gen:api → apps/web/src/api/schema.d.ts
  → pnpm gen:check            → 不修改工作区，只对账，失败即报错
  → pnpm lint:openapi         → @redocly/cli lint
  → pnpm verify-contracts     → gen:check + lint:openapi + contracts 测试
```

`EcomGen/AGENTS.md:17` 声明："接口契约以 `packages/contracts/src` 中的 TypeBox schema 为唯一手写真源；`openapi.yaml` 与 `apps/web/src/api/schema.d.ts` 是生成视图。"

**方向差异（重要）**：EcomGen 是 TS-first，contracts 包是真源、OpenAPI 是派生物。tianming 是 C#-first，**C# DTO 是真源、OpenAPI 是派生物、TS 类型是二次派生物**。本任务照搬的是"单一真源 + 生成视图 + 对账门禁"这个结构，不是照搬 TypeBox。

## 3. 范围内

- 后端导出 OpenAPI 文档到版本控制的文件（Swashbuckle CLI 或启动时导出），产出 `tianming-web/backend/openapi.json`（或 yaml）。
- 前端加 `gen:api` 脚本，从该文档生成类型（`openapi-typescript` 生成 `schema.d.ts`）。
- 加 `gen:check`：重新生成到临时位置并与已提交产物比对，**不修改工作区**，不一致即失败。
- 迁移 `types.ts`：能由 OpenAPI 覆盖的类型改为从生成视图 re-export；覆盖不到的（SSE 事件负载、前端本地视图模型）保留手写但**集中到独立文件并注明原因**。
- 把 `gen:check` 接入前端 `test` 或单独的 `verify-contracts` 脚本，并在 `.trellis/spec/frontend/type-safety.md` 记录该约定。

## 4. 明确不做

- 不改后端 DTO 形状、不改 API 路由、不改任何 C# 业务逻辑。
- 不引入 TypeBox，不把契约真源搬到 TS（tianming 是 C#-first，与 EcomGen 方向相反）。
- 不重写 `client.ts` 的信封合同 / 幂等键 / 重试 / SSE 工厂——那些是行为不是类型。
- 不动 SSE 事件的运行时归一化（`src/lib/runtime-events.ts`，894 行）；SSE 负载不在 OpenAPI 覆盖范围内。
- 不新增 CI 平台。若仓库尚无 CI，门禁以本地脚本形式交付并在文档中声明。
- 不在本任务里补其他 spec（归 `08-31-fill-spec-and-test-policy`）。

## 5. 依赖与顺序

**必须在 `08-31-promote-control-plane` 之后**。原因：那个任务会把后端从 `old/Web/NovelAgentWeb` 搬到 `tianming-web/backend/Tianming.Web`，本任务要写的导出脚本路径、`openapi.json` 落盘位置和前端相对路径都依赖迁移后的布局。提前做会产生一次无谓的路径返工。

## 6. 验收标准

- [ ] **AC-1 契约落盘**：后端能以可重复命令导出 OpenAPI 到版本控制文件；同一份代码两次导出结果字节一致（无时间戳/随机序）。
- [ ] **AC-2 类型生成**：`npm run gen:api` 从该文件生成前端类型，生成物进版本控制。
- [ ] **AC-3 对账门禁**：`npm run gen:check` 在契约与生成物一致时通过、不一致时失败，且**不修改工作区任何文件**。
- [ ] **AC-4 门禁可证伪**：故意改一个后端 DTO 字段后 `gen:check` 必须失败；恢复后必须通过。这条是本任务的核心证据。
- [ ] **AC-5 手写类型收缩**：`types.ts` 中能被 OpenAPI 覆盖的部分改为 re-export 生成类型；剩余手写类型集中且每块注明保留原因（SSE 负载 / 前端本地视图模型）。记录迁移前后行数。
- [ ] **AC-6 不回归**：前端 `typecheck`、`lint`、`test`（现有 17 个）全部通过；后端测试不受影响。
- [ ] **AC-7 约定成文**：`.trellis/spec/frontend/type-safety.md` 写明"生成视图不得手改，契约变更走后端 DTO + 重新生成"。
- [ ] **AC-8 收口**：`task.py validate` 与 `git diff --check` 通过。

## 7. 风险与对策

| 风险 | 对策 |
|---|---|
| Swashbuckle 6.5.0 对 .NET 10 的 OpenAPI 输出可能需要升级或换 `Microsoft.AspNetCore.OpenApi` | 先实测导出；需要换生成器时记录为独立决定，不顺手升级依赖 |
| C# DTO 的 nullable / 多态 / 联合类型生成出的 TS 可能比手写类型更宽松 | 逐域迁移，每域跑 `typecheck`；无法安全生成的类型保留手写并注明 |
| 生成物 diff 噪声大，掩盖真实契约变更 | 固定生成器版本与排序；AC-1 要求两次导出字节一致 |
| SSE 事件不在 OpenAPI 内，可能被误判为"未覆盖即可删" | §4 明确排除；`runtime-events.ts` 不动 |

## 8. 为什么值得做（与 EcomGen 的关系）

这是本次 EcomGen 对标里**唯一通过验证、且 tianming 确实缺失、且与已定 C# 方案不冲突**的高价值项。其余多数维度上 tianming 并不落后：

- SSE 纪律 tianming 更严：EcomGen `packages/core/src/events.ts` 只有 19 行；tianming 有三流 + cursor 续传 + 894 行事件归一化 + `.trellis/spec/frontend/state-management.md` 成文契约。
- 可靠性不变量两边独立收敛到同一答案（外部请求前写标记、进程中断转不可自动重试、幂等键、REST 为真源 SSE 只通知）——tianming 已有 `KernelTaskClaimTests` 在真 PostgreSQL 上 9/9 通过。
- 持久化底座 tianming 更强：EcomGen 是 SQLite + 单用户桌面（`packages/core/src/` 内 grep 不到 tenant/RLS）；tianming 是多租户 PostgreSQL + RLS + 72 迁移。
