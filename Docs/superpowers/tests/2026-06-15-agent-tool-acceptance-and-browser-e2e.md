# Agent 工具逐项验收与浏览器 E2E 记录

日期：2026-06-15

分支：`main`

目标：补齐 `original-design-full-implementation` 收尾记录中缺失的 19 个 Agent 工具逐项验收表，并记录一次本地浏览器业务走查。

## 运行环境

- 前端：`http://localhost:3002`
- 后端：`http://localhost:5002`
- Redis：`novelagent-redis`，`6379`
- Qdrant：`novelagent-qdrant`，`http://localhost:6333/healthz` 返回 `healthz check passed`
- 后端健康：`/health` 返回 Redis/Qdrant Healthy；embedding 为本地 stub degraded

## 工具验收表

工具清单来自 `Web/NovelAgentWeb/Support/AgentToolRegistry.cs` 的 `BuildEntries()`。

| 工具 | 阶段 | 风险 | 必填参数 | 副作用声明 | 验收证据 | 结论 |
|---|---|---:|---|---|---|---|
| `tool_search` | meta | Low | `phase` | Redis/Memory 工具缓存、SQLite `agent_tool_search_snapshots` | `AgentKernelRegression` 覆盖未发现工具阻断、phase search、side effects；`ToolSearchCacheServiceTests` 覆盖 session/Redis/SQLite snapshot 恢复 | 通过 |
| `ResolveNovelProject` | project | Low | `mode`, `projectId`, `projectTitle`, `title`, `genre`, `seed` | memory `session/project`，SQLite `novel_projects/agent_sessions` | `AgentKernelRegression` 覆盖发现边界和 awaiting foundation 幂等；`AgentToolRegistryTests` 覆盖 `tool_search(All)` 不再暴露旧项目创建入口；浏览器无 LLM 配置时验证不会 500，并给出无项目提示 | 基础通过；完整 LLM 自动绑定/创建需配置真实 LLM 后复测 |
| `ProcessKnowledgeFile` | knowledge | Medium | `taskId` | memory `project`，SQLite `knowledge_base/knowledge_processing_tasks/content_documents/project_knowledge_usages`，Qdrant `knowledge` | `AgentKernelRegression` 验证 side effects；`KnowledgeProcessingServiceTests` 覆盖处理完成记忆语义；浏览器验证素材导入入口与粘贴素材创建 | 通过 |
| `QueryProjectStatus` | blackboard | Low | 无 | 无写入 | `AgentKernelRegression` 覆盖 provider tool parsing 与状态查询路由；浏览器 workflow 页面验证项目状态可读 | 通过 |
| `SearchCreativeKnowledge` | rag | Low | `query` | memory `execution`，SQLite `project_knowledge_usages`，Qdrant `knowledge` | `AgentKernelRegression` 覆盖 DB-created knowledge 检索和项目 usage 隔离 | 通过 |
| `PlanStoryFoundation` | planning | Low | `userSeed`, `genre` | memory `session/execution`，SQLite `agent_runs/content_documents` | `NovelAgentRegression` 覆盖 story foundation planning run；工具注册 schema 覆盖 provider exposure | 通过 |
| `CommitStoryFoundation` | commit | High | `runId`, `selectedMacroCandidateIndex`, `selectedMacroCandidateId`, `selectedMacroCandidateTitle` | memory `project/execution`，SQLite `story_constitutions/agent_runs/content_documents`，Qdrant `story_bible` | `AgentKernelRegression` 覆盖提交前置条件；`NovelAgentRegression` 覆盖按 index/id/title 提交流程 | 通过 |
| `PlanVolumeArc` | planning | Low | `creativeBrief`, `volumeId`, `volumeTitle`, `sourceTurnId` | memory `session/execution`，SQLite `agent_runs/content_documents` | 工具注册、phase search、provider schema 覆盖；卷提交回归覆盖下游 volume arc 数据写入 | 基础通过；建议后续加直接 planner flow 测试 |
| `CommitVolumeArc` | commit | High | `runId` | memory `project/execution`，SQLite `volume_arcs/agent_runs/content_documents`，Qdrant `story_bible` | `NovelAgentRegression` 覆盖确认保护和 foreshadow ledger 写入；phase search 覆盖发现 | 通过 |
| `PlanChapter` | planning | Medium | `creativeBrief`, `chapterId`, `sourceTurnId` | memory `session/execution`，SQLite `agent_runs/content_documents`，Qdrant `knowledge/chapter_context` | `AgentKernelRegression` 覆盖 raw userGoal 阻断和调度；`NovelAgentRegression` 覆盖章节候选生成 | 通过 |
| `SelectChapterCandidate` | planning | Medium | `runId`, `candidateTitles` | memory `session/execution`，SQLite `agent_runs` | `NovelAgentRegression` 覆盖未确认/确认选择；`RecoveryEngineTests` 覆盖恢复链推荐 | 通过 |
| `BuildChapterContextPackage` | writing | Low | `runId` | memory `execution`，SQLite `agent_runs/content_documents`，Qdrant `chapter_context` | `AgentKernelRegression` 覆盖缺上下文时自动推荐；`NovelAgentRegression` 覆盖章节上下文包构建 | 通过 |
| `GenerateChapterWithChanges` | writing | High | `runId` | memory `execution`，SQLite `agent_runs/content_documents` | `AgentKernelRegression` 覆盖缺上下文阻断；`NovelAgentRegression` 覆盖确认保护和正文生成 step 完成 | 通过 |
| `ValidateChapterDraft` | gate | Medium | `runId` | memory `execution`，SQLite `agent_runs` | `AgentKernelRegression` 覆盖缺草稿时推荐生成、需重校验时阻断提交；`NovelAgentRegression` 覆盖 gate failure 记录 | 通过 |
| `RepairChapterDraft` | writing | High | `runId` | memory `execution`，SQLite `agent_runs/content_documents` | `AgentKernelRegression` 覆盖 hard boundary 和修复推荐；工具 side effects 覆盖 | 通过 |
| `CommitValidatedChapter` | commit | High | `runId` | memory `project/execution`，SQLite `chapters/agent_runs/content_documents`，Qdrant `chapter` | `AgentKernelRegression` 覆盖提交前置条件和 side effects；`NovelAgentRegression` 覆盖 gate 未通过时拒绝提交 | 通过 |
| `RefreshProjectIndexes` | maintenance | Medium | `runId` | memory `project/execution`，SQLite `agent_runs/content_chunks/content_vector_points`，Qdrant `chapter/story_bible` | 工具注册、phase search、side effects 覆盖；内容/Qdrant 映射由内容层测试覆盖 | 基础通过 |
| `AnalyzeDependencyImpact` | maintenance | Low | `runId` | memory `execution`，SQLite `agent_runs` | `AgentKernelRegression` phase search 覆盖 review/maintenance 发现；dependency impact 当前以基础发现和 ledger 覆盖为主 | 基础通过；建议后续加直接业务样例 |
| `ReviewChapter` | review | Medium | `runId` | memory `project/author/execution`，SQLite `agent_runs/agent_memory_events` | 工具注册和 side effects 覆盖；记忆写入由 `AgentMemoryServiceTests` 覆盖相关 execution/project/author memory 通路 | 基础通过 |

## 浏览器 E2E 走查

### 步骤与结果

1. 打开 `http://localhost:3002/register`，注册测试用户 `ui_e2e_1781490447490`。
   - 结果：注册成功并进入 Agent 对话页。

2. 在 `http://127.0.0.1:3002/register` 复测时触发 `Failed to fetch`。
   - 结论：开发 CORS/API base 以 `localhost` 为准；正式走查使用 `http://localhost:3002`。

3. 新建 Agent 会话并发送：

   ```text
   我想创建一本玄幻小说，主角能听见世界规则的裂纹，书名叫《裂纹听命》。
   ```

   初始结果：500，前端显示 `Object reference not set to an instance of an object.`

   根因：无项目、无 LLM 配置时 Planner 进入 no-action fallback，`AgentRuntime.BuildStatusSummary(session, bible)` 收到 `bible == null` 并解引用。

   修复：`BuildStatusSummary` 支持无项目状态，返回“尚未绑定项目。可以先创建新小说，或切换到已有项目。”

   复验：同类 API 请求返回 200；浏览器 UI 不再显示请求失败，显示无项目提示。

4. 使用真实后端 API 为同一测试用户创建项目：

   ```json
   {
     "title": "裂纹听命",
     "genre": "玄幻",
     "subGenre": "规则怪谈",
     "coreHook": "主角能听见世界规则的裂纹"
   }
   ```

   结果：项目创建成功，浏览器刷新后侧栏显示当前项目 `裂纹听命`，Agent 会话显示 `已绑定项目`。

5. 打开 `创意知识库`。
   - 结果：知识库统计、分类、空态正常显示；项目上下文仍为 `裂纹听命`。

6. 打开 `创作工作流`。
   - 结果：工作台显示项目标题、core hook、会话数、空章节状态和 Action Dock。

7. 打开 `小说书城`。
   - 结果：显示 1 个项目，项目卡片为 `裂纹听命`，core hook 正常展示。

8. 在 `创意知识库 -> 导入素材` 中粘贴测试素材并创建：

   ```text
   规则裂纹会在黎明前发出轻响，只有主角能听见。每次听见裂纹，世界都会出现一条新的代价。
   ```

   结果：素材库显示 `裂纹规则素材`，`Research · 1 个向量块`。

### 限制

- 本次本地环境没有配置真实 LLM，所以没有完成从浏览器触发 LLM 自动 `tool_search -> ResolveNovelProject -> PlanStoryFoundation` 的完整链路。
- 完整工具执行链由 `AgentKernelRegression`、`NovelAgentRegression` 和 Unit 测试覆盖；浏览器走查重点验证登录、会话、项目上下文、页面状态、无项目 chat fallback、素材创建和向量化入口。
