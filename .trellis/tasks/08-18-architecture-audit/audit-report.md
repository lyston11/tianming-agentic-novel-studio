# 天命小说 Agent 架构审计报告（最终版）

**任务**: 08-18-architecture-audit
**更新时间**: 2026-08-21
**性质**: 审计 + 直接实施。本报告与当前代码事实一致；早期版本中"固定链式 Agent、自动整书启动、两套系统已完整打通"等结论已按权威 PRD/design 废弃。

---

## 1. 执行摘要

本任务在同一个任务内完成了六个顺序切片的实施与验证：

| 切片 | 内容 | 状态 |
|---|---|---|
| Slice 1 | 封闭新 Session 创建边界（全部 Unbound 创建） | ✅ 通过 |
| Slice 2 | 服务端解析的 Unbound Conversation 回合 | ✅ 通过 |
| Slice 3 | Conversation 与项目执行上下文拆分（Unbound/Bound 判别联合） | ✅ 通过 |
| Slice 4 | 项目发现与用户显式确认绑定（ASP.NET 合同） | ✅ 通过 |
| Slice 5 | 独立 Node Pi Runtime + 聊天持久化收口 | ✅ 通过 |
| Slice 6 | Agent→Canon Application/Database E2E | ✅ 通过 |

**核心成果**：在真实迁移历史的 PostgreSQL 上，单 Worker + 确定性 Runtime 跑通
Conversation → Proposal/Goal → Production → Task → Candidate → 用户 Acceptance →
用户 Merge → Canon 版本增加且正文可查询的完整闭环（`AgentToCanonE2ETests`）。
Acceptance 与 Canon Merge 不注册为 Agent Tool；Agent Loop 采用 `pi-agent-core`
原生停止语义；PostgreSQL 是唯一会话真源。

**质量门**：Unit 840/840、AgentArchitecture 29/29、NovelAgentRegression 157/158
（唯一失败为先于本任务的历史缺陷，见 §6）、前端 11/11 + lint + build、Node
type-check/test/build/冒烟、双提供方迁移前向+回滚脚本、`task.py validate`、`git diff --check`。

---

## 2. 实施中发现并修复的真实缺陷

以下缺陷均由真实迁移/端到端测试暴露，`EnsureCreated` 式测试无法发现：

### P0-1 共享表物理 FK 插入顺序缺失（已修复）
- **位置**: `Agent/Tianming.NovelAgent.Infrastructure/Persistence/EfAgentControlStore.cs`
- **证据**: 真实迁移下 `book_productions→creative_goals`、`production_batches→book_productions` 存在物理外键；EF 模型无导航属性时插入顺序不定，确认提案即触发 FK 违例。
- **修复**: 写入所有者内按依赖顺序落库（goal 先刷、production 先于 batch 刷），事务原子性不变。

### P0-2 bridge 事件重投递破坏终态不可逆（已修复）
- **位置**: `ProductionApplicationService.ReachAcceptanceGateAsync`
- **证据**: production 经用户 Merge 进入 Completed 后，acceptance-gate bridge 事件重投递会抛 `Completed→AwaitingAcceptance` 状态机异常，违反"状态不可逆、重复投递 no-op"合同。
- **修复**: 对 Completed/Failed/Cancelled 终态幂等返回。

### P1-1 Domain 任务图缺重试预算（已修复）
- **位置**: `FirstBatchTaskGraphCompiler`（Domain）
- **证据**: freeze 任务 MaxAttempts=1，瞬态失败直接进入 awaiting_decision，Worker 无法按指数退避重试；legacy 路径同类任务预算为 3。
- **修复**: freeze 对齐 `maxAttempts: 3`；E2E 验证 Transient 失败→退避→重试→完成。

### P1-2 project_context_activations 缺租户 RLS（已修复）
- **位置**: Web PostgreSQL 迁移 `20260822000000_AddProjectContextActivationTenantRls`
- **证据**: Slice 4 新表晚于 2026-07 的 RLS 全表扫描迁移落地，未带 `tenant_isolation` 策略；`PostgresTenantIsolationTests` 捕获。
- **修复**: ENABLE/FORCE RLS + tenant_isolation 策略；前向与定向回滚脚本均已生成验证。（RLS 为 PostgreSQL 特性，SQLite 开发库不适用。）

### P2-1 Node 包命名空间漂移（已修复）
- npm 上 `@earendil-works/pi-*` 自 0.74 起才存在且 API 已演进；PRD 研究依据的 0.57.1 对应改名前的 `@mariozechner/pi-*`。Runtime 锁定 `@mariozechner/pi-agent-core`/`pi-ai` 0.57.1，Agent loop/steering/事件流合同与 PRD 一致。

---

## 3. 分维度审计结论（R1–R6）

### R1 架构分层与依赖
- Domain/Contracts 不依赖 Infrastructure（架构纯度测试持续约束）；Application 通过端口隔离外部依赖；Web 作为 Composition Root 注册 Pi Runtime 适配器（`PiRuntime:Enabled` 切换）。
- 新增 `Agent/Tianming.NovelAgent.PiRuntime` 独立 Node 服务：不直连业务数据库、不持久化模型密钥，仅回调 ASP.NET 内部 API（`x-pi-runtime-key` 校验）。

### R2 模块职责
- 历史结论维持：`AgentCore.cs` 是模型聚合而非上帝类；大文件多为领域内聚服务。本次新增代码遵循既有分层，未引入新的职责混杂目录。

### R3 新旧代码隔离与迁移风险
- 聊天持久化已收口：浏览器 `AgentPage` 与 legacy `/agent/chat` 兼容入口均只写 `ConversationMessages`；会话恢复从 durable messages 重放；纯度测试证明聊天路径不再依赖 `AgentTurnCoordinator`/`IChatHistoryRepository`，零 `AgentChatTurns` 双写。
- `TargetArchitectureDirector`/`ChatHistoryRepository` 类保留为迁移期代码（浏览器 WorkflowPage 等仍引用兼容入口），其退役是独立清理门。
- `EnforceLegacyControlPlaneReadOnly` 保持关闭：`GoalControlService` pause/resume/cancel/safe-point 及部分 recovery writer 仍是合法直写方（规则 #14/#21）。

### R4 多用户隔离与安全
- Session 创建合同无 projectId 入参；回合合同 `userId+sessionId`，绑定由服务端解析；Bound 回合每次重新校验项目访问权（规则 #70）。
- 项目激活需可审计确认来源 + 幂等键 + binding version；缺确认返回可恢复 `confirmation_required`，Loop 不中断。
- 未绑定上下文不加载任何项目业务数据、不暴露项目写工具（回归测试覆盖）。
- 本任务范围内未发现凭据进入 ToolContext/日志/Prompt 的泄漏路径。

### R5 数据一致性与事务
- PostgreSQL 单一真源贯穿：Conversation/Message envelope（`pi-envelope:` + `pi.assistant.v1`/`pi.tool-result.v1`）、checkpoint 仅派生缓存。
- Canon merge 保持跨 DbContext outbox 握手；bridge/canon_merge_requested 重投递幂等；CandidateVersion/AcceptanceDecision artifact/BranchMergeRecord 证据链完整；CanonWriteLease fence 在合并后释放。
- 并发确认幂等（同键字节级等价结果）由既有 Serializable vertical-slice 测试覆盖。

### R6 测试与可维护性
- 新增 E2E 使用真实双迁移历史 + worker 角色连接，替代 EnsureCreated 盲区；这是本轮最有价值的测试资产。
- Node Runtime 单测以 fake stream 驱动 pi loop，验证自然停止、confirmation_required 可恢复、激活后工具集切换、envelope 往返。

---

## 4. 建议（P0/P1/P2，区分已实施/待实施/延后）

### 已实施（本任务）
- P0：FK 插入顺序、bridge 终态幂等；E2E 工厂未替换 AgentControlDbContext 导致 legacy 章节验收在无本地 PostgreSQL 的环境必然失败（连接拒绝被 Npgsql 归类为瞬态错误）——已将控制面上下文接入同一 SQLite 测试库并按生产语义补齐共享表列漂移与 agent 专有表，`GoalWorkflowApiTests` 恢复通过。
- P1：freeze 重试预算、project_context_activations RLS（§2）。
- P2：Pi 包版本锚定；聊天持久化单一真源收口。

### 待实施（cutover 前，建议下一任务）
- **P1**：API/SSE E2E 与 Playwright 浏览器 E2E（Slice 6 顺序验证的第二、三阶段）。
- **P1**：剩余合法 legacy writer（GoalControlService 控制面、recovery Artifact writers）迁移后启用 `EnforceLegacyControlPlaneReadOnly` preflight。
- **P2**：退役 `TargetArchitectureDirector`/`ChatHistoryRepository`/`AgentTurnCoordinator` 等迁移期代码；WorkflowPage 切换到新链路。

### 明确延后（PRD Out of Scope）
- 跨项目 Conversation 切换与 handoff summary；Node 模型凭据最终方案；gRPC；默认 Agent 自动 wake-up；`AgentHarness` 未实现能力。

---

## 5. AC-1～AC-16 证据索引

| AC | 结论 | 主要证据 |
|---|---|---|
| AC-1 | ✅ | 本报告；旧结论已废弃 |
| AC-2 | ✅ | god-classes research + §3 R2；未误报聚合文件 |
| AC-3 | ✅ | §3 R3；guard 关闭前提与遗留 writer 清单 |
| AC-4 | ✅ | §3 R4 + P1-2 RLS 修复；Unbound 泄漏回归测试 |
| AC-5 | ✅ | §3 R5 + P0-1/P0-2 修复；E2E 幂等/lease/fence 断言 |
| AC-6 | ✅ | §4 已实施/待实施/延后三分清单 |
| AC-7 | ✅ | 本报告为权威；handoff-summary.md 已标记历史材料 |
| AC-8 | ✅ | Slice 1：无参创建、DB ProjectId=null、旧 query 显式拒绝 |
| AC-9 | ✅ | Slice 2：回合合同无调用方 projectId；Unbound 自然停止 |
| AC-10 | ✅ | Slice 3：Unbound/Bound 判别联合；Kernel/Goal 强项目作用域保留 |
| AC-11 | ✅ | Slice 4：最小目录元数据；确认来源+幂等键+binding version；confirmation_required 可恢复 |
| AC-12 | ✅ | Slice 5：Node 服务用 pi-agent-core.Agent + pi-ai；HTTP/JSON；durable envelope 重建 |
| AC-13 | ✅ | Node 工具集断言；Acceptance/Merge 非 Agent Tool；待人工决策投影 |
| AC-14 | ✅ | `AgentToCanonE2ETests`：全链路 + Canon 版本增加 + 正文可查询 |
| AC-15 | ◐ | Application/DB 层通过；API/SSE 与 Playwright 按序待扩展（§4 待实施） |
| AC-16 | ✅ | 每 Conversation Run Lease；共享对象授权/幂等/版本/fence；guard 保持关闭 |

---

## 6. 已知问题与风险

1. ~~历史缺陷：legacy 章节验收 API 返回 transient-failure 400~~ **已修复（2026-08-21）**：根因是 E2E 工厂从未替换 `AddNovelAgentPostgresInfrastructure` 注册的 `AgentControlDbContext`，控制面写入在无本地 PostgreSQL 的环境必然失败（连接拒绝被 Npgsql 归类为瞬态错误）；且两模型在共享表上存在列漂移（生产由 Agent 迁移 `ADD COLUMN IF NOT EXISTS` 补齐）。修复：测试宿主将控制面上下文接入同一 SQLite 库，启动时按生产语义物化 agent 专有表、补齐共享表缺失列后再建索引（`TestWebApplicationFactory` + `Program.cs` Testing 分支）。
2. **活跃 Run 跨请求 steering**：HTTP 协议每请求新建 Run；跨请求 steering/follow-up 需要活跃 Run 注册表，留待 API/SSE 层验证阶段一并设计。
3. **迁移期双入口**：`/agent/chat` 兼容入口与 WorkflowPage 仍在服务旧前端面；已证明只写权威存储，但退役前仍是维护面。

---

## 7. 交付物清单

- 产品代码：Session/Conversation/Context/项目激活（Slices 1–4）、Node Pi Runtime + C# 适配器 + 聊天收口（Slice 5）、一致性修复（Slice 6）。
- 测试：Node 4 例、C# PiRuntime/控制器/会话重放/纯度测试、`AgentToCanonE2ETests` 全链路 E2E、RLS 迁移回归。
- 迁移：Web PostgreSQL `20260822000000_AddProjectContextActivationTenantRls`（含 Designer，前向/回滚脚本已验证）。
- 文档：本报告、implement.md 六切片实施记录、handoff-summary.md 历史标记。
