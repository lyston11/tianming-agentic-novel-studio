# 补齐 spec 与测试价值规则：修复子代理被注入空规范

## 1. 目标与用户价值

`.trellis/spec/` 是每个 AI 会话的行为源头——Trellis 的 hook 会把任务 jsonl 清单里列出的 spec 自动注入 `trellis-implement` 和 `trellis-check` 子代理的提示。**当前 11 个 spec 里有 8 个是空模板，而其中一个正被全部 7 个 08-30 任务引用。** 子代理实际收到的是 `(To be filled by the team)`。

用户价值直接：子代理不再按通用习惯写码/评审，而是按本仓库真实约定。这也是 `00-bootstrap-guidelines` 那个自 2026-08-17 起 `in_progress` 的任务本该完成的事。

## 2. 实测证据

### 2.1 spec 填充现状

按"去掉空行、注释、标题、引用后的真实内容行数"统计：

| spec | 真实行数 | 状态 |
|---|---|---|
| `backend/database-guidelines.md` | 184 | ✅ 已填 |
| `guides/cross-layer-thinking-guide.md` | 194 | ✅ 预置 |
| `guides/code-reuse-thinking-guide.md` | 125 | ✅ 预置 |
| `backend/quality-guidelines.md` | 111 | ✅ 已填 |
| `frontend/state-management.md` | 63 | ✅ 已填 |
| `guides/index.md` | 50 | ✅ |
| `backend/index.md` / `frontend/index.md` | 19 / 20 | 索引，3 项标 To fill |
| `backend/directory-structure.md` | 20 | ❌ 模板 |
| `frontend/directory-structure.md` | 20 | ❌ 模板 |
| `frontend/component-guidelines.md` | 18 | ❌ 模板 |
| `backend/error-handling.md` | **16** | ❌ 模板，**被 7 个任务引用** |
| `backend/logging-guidelines.md` | 16 | ❌ 模板 |
| `frontend/hook-guidelines.md` | 16 | ❌ 模板 |
| `frontend/quality-guidelines.md` | 16 | ❌ 模板 |
| `frontend/type-safety.md` | 16 | ❌ 模板，被 3 个任务引用 |

### 2.2 空 spec 正在被注入

`backend/error-handling.md` 出现在**全部 7 个** 08-30 任务的 `implement.jsonl` **和** `check.jsonl` 里，注入理由写的是"command rejection、scope/版本冲突、abort 和 durable error result 需要遵守后端错误处理约定"。文件实际内容：

```
## Error Types
(To be filled by the team)
## Error Handling Patterns
(To be filled by the team)
## API Error Responses
(To be filled by the team)
```

`frontend/index.md` 与 `frontend/type-safety.md` 同样被 3 个任务引用，同样是模板。

`00-bootstrap-guidelines/prd.md` 自己警告过这个后果："Empty spec = sub-agents write generic code."

### 2.3 已发生的代价

- `Tests/Unit/Architecture/TargetArchitecturePurityTests` 的 16 个失败源于一次路径变更打破 `RepositoryRoot()` 不变量，**9 天无人发现**（journal 仍记 `Unit 837/837`）。
- 仓库累积了 `/agent/chat` 兼容入口、双 DbContext、MAF adapter、`TargetArchitectureDirector` 等"没人明确要求但也没人删"的兼容面。
- 08-18 架构审计自评"60% 正确，40% 过度工程化"。

## 3. 范围内

### 3.1 补齐 6 个真正被用到的 spec

优先级按被引用次数与实际风险：

1. **`backend/error-handling.md`**（P0，7 个任务引用）——按 `old/Web/NovelAgentWeb` 与 `old/Agent/Tianming.NovelAgent.Application` 的真实做法写：typed result vs 异常的选择、not-found / forbidden / conflict / invalid-transition / cancelled 的区分、幂等键冲突语义、Npgsql 瞬态错误分类、`isError` tool result 的可观察性、禁止吞错与伪造成功。
2. **`frontend/type-safety.md`**（P0，3 个任务引用；且 `08-31-api-contract-codegen` 的 AC-7 要写入此处）
3. **`backend/logging-guidelines.md`**——含"不得序列化 token/凭据"（已有 `AuthenticationLogging_DoesNotSerializeTokens` 测试在守）
4. **`backend/directory-structure.md`** / **`frontend/directory-structure.md`**——必须反映 `08-31-promote-control-plane` 之后的布局
5. **`frontend/quality-guidelines.md`**、**`frontend/component-guidelines.md`**、**`frontend/hook-guidelines.md`**——按 `tianming-web/frontend/src/` 真实模式（module store + `useSyncExternalStore`，无 zustand；shadcn/radix；react-query）

### 3.2 引入两条 EcomGen 规则

**测试价值边界**（加入 `backend/quality-guidelines.md` 与 `frontend/quality-guidelines.md`）。参照 `EcomGen/AGENTS.md` 的「测试价值与 TDD 边界」：新增测试必须至少保护其中一项——公开 API 契约、领域不变量、持久化或状态转换、高风险 Provider 协议边界、已复现缺陷；否则不新增。禁止按模型/Provider/组件机械复制同构测试；同一规则的多个变体用参数化。定期删除被更高层充分覆盖且无独立诊断价值的测试。

直接针对 tianming 现状：837 个单测里 16 个静默失效 9 天，说明测试数量不等于诊断能力。

**禁止未经要求的兼容分支**（加入 `backend/quality-guidelines.md`）。参照 `EcomGen/AGENTS.md:56`："未经明确需求，不保留历史状态、字段或行为的运行时兼容分支。需要处理开发数据时，优先采用一次性迁移或显式版本化方案。"

直接针对 tianming 现状：兼容面已成为 `AGENT_CORE_ARCHITECTURE.md` §6 需要专门列退役条件的负债。

### 3.3 索引与文档纪律

- `backend/index.md` / `frontend/index.md` 的 To fill 状态改为 Active。
- 在 `spec/guides/index.md` 或各 index 顶部加一句 EcomGen 式声明（参照 `EcomGen/ARCHITECTURE.md:3`）：**本文档描述已经落地到代码中的约定，不是规划稿**；写 spec 前先在代码里找 2-3 个真实例子，禁止写入未实现的理想模式。
- 修正 `CLAUDE.md` 的失真内容（.NET 8 / SQLite 三层 / `Web/NovelAgentWeb` 旧路径 / 硬编码 `/Users/lyston/PycharmProjects/`）——若 `08-31-promote-control-plane` 已做，此处只做校验。

## 4. 明确不做

- 不改任何业务代码、不改测试、不删现有测试（删测试按新规则另起任务）。
- 不改 08-30 那 7 个任务的 jsonl 清单内容——补齐 spec 后它们引用的文件自然有内容了。
- 不新增 spec 文件，只填现有的。
- 不做 API 契约代码生成（归 `08-31-api-contract-codegen`）。
- 不把 EcomGen 的 TypeBox、pnpm workspace、BullMQ、SQLite 或任何依赖引入 tianming。
- 不动 `EcomGen/` 与 `learngraph/`（独立仓库，只读参考）。

## 5. 依赖与顺序

- **`backend/directory-structure.md` 与 `frontend/directory-structure.md` 必须在 `08-31-promote-control-plane` 之后写**，否则记录的是即将失效的布局。
- 其余 spec（error-handling、logging、type-safety、frontend 三件）**不依赖迁移，可以先做**。
- 建议顺序：先做不依赖布局的部分（3.1 的 1-3 + 3.2 + 3.3），迁移完成后补 directory-structure 两件。
- 完成后应能收口 `00-bootstrap-guidelines`（它的三个 checkbox 正是此事）。

## 6. 验收标准

- [ ] **AC-1 无空模板被引用**：全部 7 个 08-30 任务 + 08-31 系列任务的 jsonl 清单所引用的每个 spec，真实内容行数 > 40，且不含 `(To be filled by the team)`。
- [ ] **AC-2 写实不写理想**：每份补齐的 spec 至少引用 **2 个**仓库真实文件路径作为例证；禁止出现代码库中不存在的模式。抽查可核对。
- [ ] **AC-3 error-handling 覆盖真实语义**：明确写出 not-found / forbidden / conflict / invalid-transition / cancelled 的区分、幂等键相同 key 不同 payload 的处理、Npgsql 瞬态错误分类、禁止吞错与伪造成功。
- [ ] **AC-4 测试价值规则落地**：前后端 quality-guidelines 均含该规则及"新增测试前须能说明其缺失会放过什么真实回归"的要求。
- [ ] **AC-5 兼容分支规则落地**：`backend/quality-guidelines.md` 含该规则，并指向 `AGENT_CORE_ARCHITECTURE.md` §6 的退役条件。
- [ ] **AC-6 索引一致**：两个 index.md 无 To fill 残留，状态与实际填充情况一致。
- [ ] **AC-7 文档纪律声明**：spec 顶部有"描述已落地约定、非规划稿"的声明。
- [ ] **AC-8 bootstrap 可收口**：`00-bootstrap-guidelines` 的三个 checkbox 可勾选，任务可 finish + archive。
- [ ] **AC-9 收口**：`task.py validate` 与 `git diff --check` 通过；不含业务代码改动。

## 7. 风险与对策

| 风险 | 对策 |
|---|---|
| 把理想模式写进 spec，导致子代理写出与仓库不符的代码 | AC-2 强制每份 spec 引用 2 个真实路径；bootstrap PRD 原话："write what the code actually does, not what it should do" |
| directory-structure 写完即被迁移作废 | §5 明确该两件排在迁移之后 |
| 一次补 8 个文件质量摊薄 | 按 §3.1 优先级分批，error-handling 与 type-safety 先行并单独评审 |
| 测试价值规则被误用为"删测试许可" | 规则只约束**新增**；删除既有测试须单独任务并逐条说明覆盖来源 |
