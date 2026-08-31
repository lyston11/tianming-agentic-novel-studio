# 天命小说 Agent 目标架构全量验收报告

**验收日期：** 2026-07-31（最终复验）
**分支：** `codex/novel-agent-target-architecture`
**环境：** macOS + OrbStack + PostgreSQL 16 + Redis 7 + Qdrant 1.18.1 + .NET 8
**结论：** 通过

## 1. 验收范围

本报告验证 `2026-07-12-novel-agent-target-architecture.md` 与 `2026-07-13-novel-agent-target-architecture-implementation.md` 的全量闭环：

- 对话承诺、CreativeGoal、Revision 和版本化任务图。
- PostgreSQL 原子 task claim、lease、恢复、预算与 Outbox。
- 天命写作、连续性、审美、RAG、知识、记忆和模型配置内核。
- 候选分支、逐章验收、定向返工、连续前缀合并与三种取消策略。
- PostgreSQL/RLS、Qdrant、Redis、SSE 和后台任务的用户隔离。
- SQLite staging 导入、幂等核验和 Qdrant 可重建边界。
- React Goal Console 桌面/移动视口。

## 2. 自动化验证

| 命令 | 结果 |
|---|---|
| `dotnet test Tests/Unit/Unit.csproj --no-restore` | 756/756 passed，0 skipped |
| `dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --no-restore` | 153/153 passed，0 skipped |
| `dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj` | 6/6 target architecture checks passed |
| `npm test` | 7/7 passed |
| `npm run lint` | passed |
| `npm run build` | passed；保留 508.20 kB chunk 非阻断警告 |
| `dotnet ef migrations has-pending-model-changes ...` | no pending model changes |
| `docker compose build api` | Release build 0 warnings / 0 errors |
| `git diff --check` | passed |

测试数量相较旧报告下降，是因为旧 `AgentRuntime`、通用 ReAct loop、`AgentToolRegistry`、工具 ledger/cache/resolver 及其源码形状测试已经从生产代码和测试工程中删除，不是通过 Skip 隐藏失败。NovelAgentRegression 新增 PostgreSQL、RLS、Goal、Kernel、RAG、预算、恢复和迁移覆盖后为 153 项；Unit 新增 Goal SSE 用户/session 隔离、来源会话归属与返工会话固定、“不写旧 runtime event”、内容读取边界及 PostgreSQL 健康探针验证后为 756 项。

## 2.1 确定性编译与 Goal 合同复验

- `/api/goals/workflow/confirm` 只持久化 Goal 并执行确定性 DAG 编译，不同步调用模型，不再产生“Goal 已提交但 Graph 缺失”的半成功状态。
- DAG 首段为 `FreezeBaselines -> AnalyzeCreativeRequirements -> CompileBatchPlan`；单章验收图共 13 个节点。
- `AnalyzeCreativeRequirements` 作为正式 Kernel 节点进入持久任务、统一预算、`ModelExecution`、lease、恢复和失败传播。
- 所有正式 Kernel 按 `TaskGraphVersion.GoalRevisionId` 读取有效 Revision，冻结 Artifact 与提示词均包含完整 Goal 合同。
- 同一幂等键可恢复已提交但缺图的历史 Goal，返回现有 Goal 和新建的 v1 Graph，不重复提交 Goal。

已知模型配置失败复验 Goal：`700f0b93d34a450496e2b60418111f4b`。最终状态为 `awaiting_decision`；对应 `ModelExecution=failed`、`reserved_cost=0`、`actual_cost=0`、`lease_expires_at=null`，证明已知失败会即时闭合预算账本。网络或连接结果未知仍保留 lease，不能误释放可能已计费的预留。

## 3. 真实生产 Goal

主验收 Goal：

```text
goalId:        bcba7ff054544c6981ff0bc833cfaa31
graphId:       065a311cd45a413fbe2eae685a4efff8
branchId:      44939514abb147c0aea4b40d318cbc6d
mergeRecordId: 8f36f389e00f4bcd873373a683f7871e
```

结果：

- Goal、UserAcceptance、PrefixMerge 均 completed。
- 第 4 章定向返工后生成 Candidate v2；第 5、6 章 Candidate v1。
- 三章连续前缀合并为正式 ChapterVersion/ContentDocument。
- 第 4 章正文、连续性审校、审美审稿和 2 条知识引用可从工作台查看。
- 重复接受第 4 章返回同一 CandidateAcceptance，幂等成立。
- 三个章节索引 outbox 全部 completed。

## 4. 返工合同

定向返工生成 5 个持久子任务：

1. DirectedReworkDraft。
2. ReviewContinuity。
3. ReviewLiteraryQuality。
4. DirectedRework reducer。
5. ExtractContinuitySummary。

返工同时消费原 Candidate、冻结 `ChapterContextContract` 和 `ReworkIntent`。第 4 章结果为 Candidate v2，Intent 进入 resolved，原版本未被覆盖。

## 5. 取消、暂停与预算失败注入

### 三种取消策略

- `PreserveCandidateBranch`：Goal canceled，候选分支 preserved，候选保留。
- `DiscardCandidateBranch`：Goal canceled，分支 discarded，候选清理。
- `MergeAcceptedPrefix`：只提交已接受的第 8 章前缀，索引完成后 Goal canceled。

### 暂停恢复

```text
goalId: 3f929539bb7345ccb98fcc6cc0930ae3
```

- 遗留 `pause_requested` 经公开 Pause API 重入收敛为 `paused`。
- Resume 成功进入 `resumed`。
- 无 running task 时 Pause 直接冻结 ready/queued task 并返回真实持久状态 `paused`。
- 验收后使用 PreserveCandidateBranch 取消，未留下 active Goal。

### 金额硬上限

```text
goalId: 8a7eb91d1b5640f3b79ee1d0e2910f0c
limit:  0.01
```

将 narrative planning 临时配置为高单价后，Goal 在模型请求发送前进入 `budget_exceeded`；`actual_cost=0`、`reserved_cost=0`，证明没有超额调用。验收后单价恢复为 0；18080 模型配置只保留在“验收灯城 A”测试项目中，没有 active Goal 使用它。

## 6. 用户隔离与 SSE

验收用户：

```text
user A: 81e31d15-dc42-4b45-972d-ef6b2c8df9b3
user B: c4906dc4-9921-447d-b53a-a80029c33188
```

数据库与自动化验证覆盖：

- PostgreSQL RLS 下 CreativeGoal 和 CollaborationMemory 跨用户不可见。
- 后台无用户作用域时不能读取用户表。
- Qdrant 命中需要用户过滤并回 PostgreSQL 二次验证。
- Redis/SSE key 使用用户与 session/goal/run 维度。

SSE 实测 session：

```text
688772b958f842e7927dcf554621866f
```

- 用户 A 以 `afterEventId=sse-accept-1` replay 到稳定事件 `sse-accept-2`。
- 用户 B 读取 A session 返回 404。
- ownership guard 在 Redis replay 和内存订阅之前执行。
- replay 事件 TTL 为 10 分钟。
- Goal DAG 现在发布 `goal_committed / goal_revised / goal_task_started / goal_task_completed / goal_task_failed / goal_state_changed / goal_candidate_changed / goal_candidate_accepted / goal_prefix_merged`。
- Goal 事件从 PostgreSQL 权威 Goal 读取 `SourceSessionId`，同时进入本机 event bus 与 Redis fan-out；测试确认作用域为 `userId + sessionId`，且 `agent_runtime_events` 不新增记录。
- Goal 提交拒绝不属于当前用户的来源会话；返工服务拒绝与 Goal 来源会话不一致的请求，客户端不能伪造审计会话。
- Goal Console 只响应 `data.goalId` 等于当前 Goal 的事件并实时失效 workflow/chapter 查询；30 秒 polling 保留为断线降级，返工沿用真实来源会话。

## 7. 迁移与长文本 RAG

- SQLite importer 只导入权威业务内容，跳过 WorkingMemory、MissionPlan、未确认提案和未完成 ReAct 状态。
- 同一 staging 副本导入两次：第二次 `ImportedCount=0`、`ReusedCount>0`。
- verifier 比对用户、项目、章节、Story Bible、知识、记忆和内容哈希。
- PostgreSQL migration 包含全文与 trigram 索引。
- 长距离章节通过 Qdrant + PostgreSQL 混合召回，跨用户向量 payload 被拒绝。
- Qdrant 返回定位 ID，正文和版本从 PostgreSQL 回读。

## 8. Outbox 与运行终态

修复 `project_domain_event` 后，dispatcher 回读权威 DomainEvent，并在处理期间进入事件所属用户作用域。最终数据库：

```text
creative_goals:
  awaiting_decision 2
  budget_exceeded  1
  canceled         6
  completed        1
dispatchable goals (committed/running/resumed): 0

kernel_tasks:
  awaiting_decision 1
  awaiting_user     4
  blocked          40
  canceled         51
  completed       109
  failed            6
  ready             1

model_executions:
  completed        23
  failed            1
  retry_scheduled   1

outbox_events:
  completed        102
```

其中 `project_domain_event` 真实验收事件 75 条全部 completed。Outbox 采用原子 claim、owner 和 lease，多实例重复领取风险已消除。

当前 2 个 `awaiting_decision` Goal 是模型配置失败验收记录，不属于可派发状态。修复前 Goal `60ae3c12d7df4db4a63f60e09997fc03` 保留 1 条零成本 `retry_scheduled` ModelExecution 和对应 `ready` task 作为恢复语义证据；recovery claim 只领取 stale `reserved/running` 执行，Kernel scheduler 也以 Goal 状态为门禁，因此二者不会后台运行。修复后 Goal `700f0b93d34a450496e2b60418111f4b` 已验证即时 `failed` 结算。40 个 `blocked` 和 51 个 `canceled` task 均为终态/条件分支审计轨迹；queued/running runtime run 为 0。

## 9. 浏览器验收

页面：

```text
http://localhost:5002/goal?goalId=bcba7ff054544c6981ff0bc833cfaa31
```

桌面 1280x720：

- 三栏完整，页面 `scrollWidth=clientWidth=1280`。
- 章节、正文、双审稿、知识引用和 Inspector 无重叠。
- completed Goal 的接受、返工、暂停、恢复、合并和取消按钮均禁用。
- 完整任务图默认折叠。

移动 390x844：

- shell/desk 不再被 rail min-content 撑宽。
- 页面无横向滚动；Goal 主区右边界小于视口。
- 顶部导航在受控容器内横向滚动，条目不逐字换行。
- Topbar 目标进入自然文档流，不覆盖正文。
- 页面自然纵向滚动，章节、正文和 Inspector 不被 `100vh` 裁切。
- 最终复验发现并修复返工区百分比最小高度导致的 Flex 高度循环；修复前返工区与 Inspector 覆盖 195px，修复后边界连续且 overlap=0。
- 浏览器 console 无 warning/error。

截图：

- `Docs/superpowers/tests/assets/2026-07-19-goal-console-desktop.png`
- `Docs/superpowers/tests/assets/2026-07-19-goal-console-mobile.png`

## 10. 最终运行状态

OrbStack 服务：

```text
api        running
postgres   running / healthy
qdrant     running
redis      running
```

`GET http://localhost:5002/health` 返回 Healthy；PostgreSQL、Redis、Qdrant 和 embedding 均 Healthy，embedding 为 `bge-small-zh-v1.5`、512 维、`degraded=false`。PostgreSQL 探针使用应用连接执行 `SELECT 1`；任一关键依赖降级时端点返回 HTTP 503，匿名响应不泄露连接信息。

2026-07-31 最终执行 `docker compose build api` 与 `docker compose up -d api`：PostgreSQL role bootstrap 正常退出，API 完成替换，PostgreSQL healthy，Redis/Qdrant/API 持续 running，健康端点再次返回全局 `Healthy`，API 确认以 `Production` 环境运行。

临时 18080 验收模型不属于正式栈，已在最终收口时关闭。正式应用继续运行在：

```text
http://localhost:5002/
```

## 11. 结论

目标架构的代码、迁移、真实工作流、用户隔离、可靠性、前后端和 OrbStack 运行态均完成。PostgreSQL 是唯一业务真源；Qdrant 与 Redis 保持派生/协调边界；旧通用 ReAct 小说生产实现及其生产测试已删除，只保留 Conversation Agent 的理解/查询职责和必要的旧审计读取。书城、知识面板、旧工作流操作台、候选卡、执行图及其样式是待继续接入新架构的产品能力，明确保留，不纳入旧生产代码清理。全量计划可标记完成。
