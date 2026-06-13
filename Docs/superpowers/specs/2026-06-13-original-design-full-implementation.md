# 原始设计全量实现修复设计

**日期:** 2026-06-13  
**实现口径:** 以原始 5 份设计文档、后续 Docs/superpowers 执行记录和用户本轮确认的 SQLite + Qdrant 内容边界为准，允许 API、数据库和前端调用发生破坏性调整
**端口契约:** 前端 `http://localhost:3002`，后端 `http://localhost:5002`

---

## 1. 目标

本轮修复不是继续追加临时功能，也不是只更新文档。目标是把当前项目收敛回最初设计的完整形态：

1. 多用户小说创作 SaaS 架构。
2. SQLite + Qdrant 的持久内容架构；SQLite 保存精确真源、版本、关系和分块文本，Qdrant 保存可重建的语义向量索引。Redis 如启用，只作为可失效缓存，不承载内容真源。
3. Agent 的 Observe / Plan / Act / Reflect 闭环。
4. ChatHistory、SessionMemory、ProjectMemory、AuthorMemory、ExecutionMemory 的分层记忆闭环。
5. 知识库从上传、处理、抽取、向量化、检索、Agent 引用到记忆沉淀的完整闭环。
6. 前端、后端、数据库和 API 与原始设计文档保持一致。

当前代码已有很多骨架和部分实现，但存在接口漂移、记忆不闭环、知识库半闭环、前端状态不统一、端口配置偏差等问题。本设计将这些偏差合并为一条可执行的修复路线。

---

## 2. 设计来源回顾

### 2.1 原始 5 份设计文档

已纳入本设计的原始文档：

- `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/01 项目架构设计.md`
- `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/02 Agent 决策系统设计.md`
- `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/03 记忆系统架构.md`
- `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/04 数据库设计.md`
- `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/05 API 设计文档.md`

这些文档确立了最终标准：

- 后端 ASP.NET Core 8，前端 React/Vite/Zustand。
- 后端 `5002`，前端 `3002`。
- Qdrant HTTP `6333`，gRPC `6334`。
- Redis `6379` 可作为缓存/运行态加速层，但不属于章节、素材、知识、StoryBible、AgentRun 产物的持久真源。
- SQLite 是结构化数据、精确内容、版本和关系真源。
- Qdrant 负责语义向量检索，所有向量点都必须能由 SQLite 重建。
- Agent 入口为 `AgentController -> AgentRouter -> AgentRuntime`。
- Agent 决策模式为 Chat、Tool、Reflection。
- API 以复数资源契约为主，如 `/api/projects`、`/api/agent/sessions`、`/api/chapters?projectId=...`、`/api/characters`、`/api/materials/{id}/vectorize`。

### 2.2 Docs/superpowers 历史设计记录

本次额外回顾了以下历史记录，并将其视为用户原本计划的一部分：

- `Docs/superpowers/specs/2026-06-08-multi-user-novel-agent-design.md`
- `Docs/superpowers/specs/2026-06-09-novel-agent-multi-user-architecture.md`
- `Docs/superpowers/plans/2026-06-09-migration-roadmap.md`
- `Docs/superpowers/plans/2026-06-09-phase1-database-workspace.md`
- `Docs/superpowers/plans/2026-06-09-phase2-vector-storage.md`
- `Docs/superpowers/plans/2026-06-09-phase3-api-refactoring.md`
- `Docs/superpowers/plans/2026-06-09-phase4-frontend-migration.md`
- `Docs/superpowers/plans/2026-06-08-intelligent-recovery-memory-refactor.md`
- `Docs/superpowers/plans/2026-06-08-phase-based-tool-memory-layering.md`
- `Docs/superpowers/specs/2026-06-11-memory-system-refactor.md`
- `Docs/superpowers/specs/2026-06-12-knowledge-base-integration.md`
- `Web/NovelAgentWeb/Docs/superpowers/specs/2026-06-11-tool-search-design.md`
- `Docs/superpowers/specs/2026-06-11-tool-search-prompt-optimization.md`
- `Docs/superpowers/specs/2026-06-11-frontend-technical-debt-cleanup.md`
- `Docs/superpowers/plans/2026-06-11-p0-project-context-unification.md`

这些记录补充了原始 5 份文档未完全展开的实现意图：

- 历史记录中曾出现 SQLite + 文件系统 + Qdrant 的过渡迁移方案；本次按用户确认修正为 SQLite + Qdrant 的持久内容架构，文件系统不再作为业务存储层，Redis 只作为可选缓存层。
- Workspace 生命周期采用 `(userId, projectId)` 项目级实例池、引用计数、LRU 淘汰。
- Qdrant collection 使用 `novel_agent_{userId}`，通过 `project_id` payload 做项目隔离与跨项目检索。
- 前端项目上下文必须使用单一 Zustand store，不能各页面维护独立项目状态。
- Agent 工具发现经历了从硬编码 PhaseInference 到 Hermes 风格 `tool_search` 的设计演进。
- 记忆系统最终目标是四层/五层分层记忆，而不是只保存一个 WorkingMemory JSON。
- 知识库目标是文件处理、知识抽取、向量检索、记忆关联和 Agent 使用统计闭环。

---

## 3. 当前偏差矩阵

### 3.1 基础设施偏差

当前后端 CORS 只放行 `http://localhost:3000`，与固定前端端口 `3002` 不一致。Qdrant 客户端配置偏向 gRPC `6334`，但健康检查与文档需要明确 HTTP `6333` 和 gRPC `6334` 分工。Redis 如果保留为缓存组件，需要让开发/生产配置、连接健康、缓存命中状态、降级状态可见；但内容持久化不能依赖 Redis。

### 3.2 API 契约偏差

当前控制器主要使用：

- `/api/project`
- `/api/agent/session`
- `/api/chapters/project/{projectId}`
- `/api/storybible/characters`

原始 API 文档要求：

- `/api/projects`
- `/api/agent/sessions`
- `/api/chapters?projectId=...`
- `/api/characters`
- `/api/materials/{materialId}/vectorize`

本轮按用户确认的 A 口径执行：最终 API 回到原始文档契约，前端同步迁移到原始契约。旧接口可以短期保留为兼容别名，但不再作为最终标准。

### 3.3 数据库偏差

当前数据库已有 users、projects、chapters、characters、materials、knowledge_base、agent_sessions、agent_memories 等表，但字段与原始设计不完全一致：

- `agent_memories` 当前以 `memory_type + content` 表达字段，原始设计强调 `memory_key/memory_value` 的语义和更明确的细粒度字段访问。
- `novel_projects` 当前有 `word_count`，原始设计还需要目标字数、状态枚举和项目创作元信息。
- `materials` 当前仍有 `file_path` 等遗留字段；目标设计要求素材原文进入统一内容文档/分块层，素材元数据、版本、向量状态和 Qdrant point 映射由 SQLite 管理，`file_path/content_path` 只能作为迁移来源或临时上传处理痕迹，不能作为运行时业务真源。
- StoryBible 相关表已部分存在，但 `/api/characters`、世界观设定、伏笔账本、AgentRun 与 Workflow 的契约需要统一。

### 3.4 记忆系统偏差

当前代码已经有 `AgentMemoryRepository` 和 `AgentMemoryService`，但还不是完整的分层记忆系统：

- 仓储只完整覆盖 ProjectMemory、AuthorMemory、ExecutionMemory。
- SessionMemory 主要存在于 session JSON 中，没有与缓存、压缩、沉淀规则形成独立闭环。
- Reflection 中没有 `MemoryUpdate` 时，部分质量门禁/执行经验变化可能只留在当前会话对象里。
- ChatHistory 压缩有实现，但缺少稳定的容错、触发测试和与 SessionMemory 的同步策略。
- 记忆冲突优先级、重复偏好沉淀、跨项目 AuthorMemory 泛化没有完整实现。

### 3.5 Agent 决策偏差

当前 `AgentRuntime` 有 Observe / Plan / Act / Reflect 主循环，`AgentPlanner` 提示词也包含自然对话优先原则。但仍有偏差：

- Chat、Tool、Reflection 三种模式缺少足够的规则级回归测试。
- `tool_search` 机制已实现一部分，但与原始 Agent 决策系统、阶段上下文、工具缓存 TTL、缓存失效策略没有完全收敛。
- 自动失败恢复和前置工具链推理有历史计划与部分实现，但需要统一进 AgentRuntime 的标准路径。
- 决策响应需要稳定输出 `decision/rag/memory/runtimeTrace/missionPlan`，供前端和调试使用。

### 3.6 知识库偏差

当前知识库已有文件上传任务、处理服务、SQLite 条目、Qdrant upsert、文本 fallback 和 Agent 工具接入。但还未完全达到历史设计：

- 知识处理任务缺少前端与 Agent 的完整状态闭环。
- 知识条目更新路径需要覆盖 `entryType/tags/weight/source` 等字段。
- Agent 检索命中后需要更新 usage count，并在 Reflection 中写入 `ProjectMemory.ReferencedKnowledgeIds`。
- 检索应融合 ProjectMemory、AuthorMemory、已用套路过滤和题材匹配 boost。
- 需要提供原始 API 文档要求的素材向量化 endpoint。

### 3.7 前端偏差

历史设计明确指出前端最大问题是项目上下文不统一。当前已有部分 Zustand store 改造痕迹，但工作区很脏，且前端 API 调用仍围绕当前临时接口：

- 前端需要统一迁移到原始 API 契约。
- Project context 必须使用单一 Zustand store。
- Materials、Knowledge、Workflow、Library、Agent 页面要统一 currentProjectId 来源。
- 登录注册、设置页、工作流、素材知识库页面需要通过真实 API 驱动，不保留空数组或本地假数据断点。

---

## 4. 目标架构

### 4.1 存储架构

最终采用 SQLite + Qdrant 的持久内容架构：

1. **SQLite:** 精确真源。保存用户、项目、业务元数据、内容文档、内容版本、顺序分块、hash、关系、状态、Agent 会话、Agent 记忆、处理任务、AgentRun 清单，以及 Qdrant point 的反向映射。SQLite 负责完整读取、版本回滚、审计和重建索引。
2. **Qdrant:** 语义索引。保存章节、素材、知识、StoryBible section、AgentRun 可复用产物、长期记忆等文本分块的 embedding 与检索 payload。Qdrant 只负责召回和相似度，不是唯一真源；任意 collection 损坏后必须能从 SQLite 重建。
3. **Redis / MemoryCache:** 可选缓存。可缓存 SessionMemory、ProjectMemory、AuthorMemory、ExecutionMemory 快照、用户设置、项目列表、知识检索热点结果、Agent 工具发现缓存和短期运行状态。缓存失效后必须能从 SQLite/Qdrant 重建，不能保存唯一业务内容。

统一内容层使用以下抽象，不把长内容散落在各业务表的大字段里：

```text
content_documents
  id, user_id, project_id, source_type, source_id, document_role,
  title, mime_type, content_hash, version, status, created_at, updated_at

content_chunks
  id, document_id, chunk_index, chunk_text, token_count,
  char_start, char_end, content_hash

content_vector_points
  id, document_id, chunk_id, qdrant_collection, qdrant_point_id,
  vector_model, indexed_at, index_status, error_message
```

业务表只保存当前内容文档引用和领域状态，例如：

```text
chapters.current_document_id
materials.raw_document_id
knowledge_processing_tasks.upload_document_id
story_bible_snapshots.document_id
agent_run_artifacts.document_id
```

五类长内容按领域拆分到 SQLite 与 Qdrant：

| 内容 | SQLite 真源 | Qdrant 索引 |
| --- | --- | --- |
| 章节正文 | `chapters` 元数据、章节版本、`content_documents`、有序 `content_chunks`、hash、当前版本指针 | 章节正文 chunks，用于上下文召回、连续性检查和相似章节搜索 |
| 素材原文 | `materials` 元数据、原始/解析内容文档、chunk 顺序、向量状态、point 映射 | 素材 chunks，用于素材语义搜索和相似素材检测 |
| 知识上传内容 | 上传任务、上传原文文档、处理策略、解析过程、抽取出的 `knowledge` 条目、source 关系 | 上传内容 chunks 与知识条目 chunks，用于知识检索和 memory-aware rerank |
| StoryBible 完整内容 | 结构化 StoryBible 表是主真源；需要完整文档时生成 `story_bible_snapshots` 版本文档 | 按 constitution、volume、character、world、foreshadow section 建索引 |
| AgentRun 产物 | `agent_runs` 保存运行状态、输入输出摘要和 artifact 清单；大产物进入 `agent_run_artifacts` + 内容文档 | 只索引有复用价值的草稿、评审、上下文包、失败修复经验 |

写入顺序：

1. 先写 SQLite 内容文档、分块、业务关系和待索引状态。
2. 再异步写 Qdrant point。
3. Qdrant 成功后回写 `content_vector_points.index_status=completed`。
4. Qdrant 失败时保留 SQLite 真源，检索降级为文本搜索，后台可重试索引。
5. 内容更新创建新版本，不直接覆盖旧正文、旧素材、旧 StoryBible snapshot 或旧 AgentRun artifact。

文件系统不属于目标业务存储层。允许的文件使用只有三类：

1. HTTP 上传过程中的短生命周期临时缓冲。
2. 数据库备份、导入、导出、迁移脚本输入。
3. 构建产物和应用静态资源。

运行时业务逻辑不得依赖 `content_path`、`file_path` 或 Markdown/JSON 文件作为真源。旧文件只能作为一次性迁移输入，迁移后由 SQLite 内容层和 Qdrant 索引共同承载。

Qdrant collection 使用 `novel_agent_{userId}`。所有点 payload 必须包含：

- `user_id`
- `project_id`
- `source_type`
- `source_id`
- `entity_type`
- `chunk_id`
- `content_preview`
- `content_hash`
- `metadata`

`content_preview` 只用于检索解释和排序辅助，不作为完整正文真源。完整内容必须回到 SQLite 内容层读取。

### 4.2 Workspace 架构

WorkspaceFactory 是标准入口：

- Key: `(userId, projectId)`
- 生命周期: request acquire/release
- 引用计数: 多 session 共享同一项目 workspace
- 淘汰策略: LRU，空闲超时后释放
- 审计: WorkspaceUsageAuditMiddleware 记录跨用户/跨项目使用异常

Agent、StoryBible、Material、Knowledge、Workflow 操作都必须在用户与项目上下文内执行。

### 4.3 API 架构

最终 API 以原始设计为准：

- Projects: `/api/projects`
- Agent sessions: `/api/agent/sessions`
- Agent chat: `/api/agent/chat`
- Chapters: `/api/chapters?projectId=...`
- Characters: `/api/characters`
- Materials: `/api/materials`
- Material vectorization: `/api/materials/{id}/vectorize`
- Knowledge: `/api/knowledge`
- Knowledge search: `/api/knowledge/search`
- Knowledge upload/tasks: `/api/knowledge/upload`, `/api/knowledge/tasks/{taskId}`
- StoryBible/Workflow: 与原始文档命名保持一致，必要时添加 `story-bible` 路由别名并迁移前端。

旧路由只作为短期兼容层，所有新前端调用和测试使用原始契约。

---

## 5. 分层记忆设计

### 5.1 记忆层

最终使用五个协同层：

1. **ChatHistory**
   - 最近 5 条完整消息保留。
   - 每 10 轮生成 Summary。
   - 30 轮后生成 MetaSummary。
   - 压缩失败时不能中断用户请求，必须降级保留最近消息。

2. **SessionMemory**
   - 当前目标、开放问题、短期偏好、最近观察、待执行工具、最后意图。
   - 随会话持久化到 `agent_sessions.session_data`，并在 repository/cache 侧有清晰读写接口。

3. **ProjectMemory**
   - 长期目标、读者承诺、项目约束、未解决伏笔、引用知识、已用套路。
   - 项目生命周期内跨会话共享。
   - `long_term_goal`、`reader_promise` 向量化到 Qdrant。

4. **AuthorMemory**
   - 风格喜好、风格反感、确认容忍度、类型习惯、常用知识。
   - 跨项目共享。

5. **ExecutionMemory**
   - 工具失败模式、重复阻塞、成功修复经验。
   - 工具调用后实时更新。

### 5.2 信息流

每次 Agent 回合必须走完整路径：

```text
UserMessage
  -> ChatHistory append
  -> Hydrate Session/Project/Author/Execution Memory
  -> Observe/Plan/Act/Reflect
  -> MemoryUpdate extraction
  -> SessionData + AgentMemory rows + Qdrant vectors
  -> SaveSession
  -> Response with decision/rag/memory evidence
```

### 5.3 沉淀与冲突

沉淀规则：

- 明确用户偏好进入 SessionMemory。
- 同一偏好重复 3 次进入 ProjectMemory constraints。
- 多项目重复模式进入 AuthorMemory。
- 工具失败/修复实时进入 ExecutionMemory。
- 知识库命中进入 ProjectMemory referenced knowledge。

冲突优先级：

1. SessionMemory 当前明确指令。
2. ProjectMemory 项目硬约束。
3. AuthorMemory 风格反感。
4. ExecutionMemory 失败经验。
5. AuthorMemory 风格喜好。

---

## 6. Agent 决策设计

### 6.1 主路径

标准入口：

```text
AgentController.Chat
  -> AgentRouter.HandleAsync
  -> AgentRuntime.RunAsync
  -> ConversationKernel classify
  -> AgentObservationBuilder
  -> AgentPlanner
  -> ToolPolicyEngine
  -> AgentToolRegistry
  -> ReflectionEngine
  -> AgentMemoryService
```

### 6.2 三种决策模式

1. **Chat**
   - 问候、身份、解释、状态摘要、轻量询问。
   - 不调用业务工具。

2. **Tool**
   - 用户明确要求创建、规划、生成、提交、处理知识文件、向量化素材。
   - 必须经过工具策略、guardrail、失败恢复。

3. **Reflection**
   - 工具执行后更新任务、质量门禁、记忆。
   - 对失败工具生成恢复策略或用户可理解的下一步。

### 6.3 工具发现

采用历史记录中确认的 Hermes 风格 `tool_search`：

- LLM 根据当前任务判断需要什么工具。
- 如果缓存工具满足需求，直接使用。
- 如果缓存不满足，调用 `tool_search(phase=...)`。
- 缓存需要 TTL 和关键状态变化失效。
- 工具阶段包括 Conversation、Planning、Creation、Review、All。

高风险写入型工具仍可采用“先执行后修正”的工作流，但必须有可回滚/可修复路径，并在测试中覆盖误调用保护。

---

## 7. 知识库设计

### 7.1 上传处理闭环

```text
POST /api/knowledge/upload
  -> upload stream parsed into content_documents/content_chunks + knowledge_processing_tasks pending
  -> Agent ProcessKnowledgeFile or frontend trigger
  -> short file single pass / long file chunked processing
  -> extracted knowledge entries
  -> SQLite knowledge rows
  -> Qdrant vectors
  -> task completed
```

短文件使用单次 LLM 分析。长文件使用 SQLite 内容层中的有序分块做分块分析、上一块摘要、最终聚合总结。所有 LLM 输出必须做 JSON 清洗、校验和失败降级。上传临时文件如果存在，处理成功或失败后都不能成为业务读取依赖。

### 7.2 检索闭环

搜索路径：

```text
SearchCreativeKnowledge
  -> Qdrant semantic search
  -> SQLite row hydration
  -> text fallback if vector unavailable
  -> memory-aware rerank
  -> usage count increment
  -> MemoryUpdate.usedKnowledgeIds
```

重排序规则：

- ProjectMemory 已引用知识加权。
- AuthorMemory 收藏知识加权。
- 题材匹配加权。
- 已用套路过滤或降权。
- 权重、创建时间和相似度共同排序。

---

## 8. 前端设计

### 8.1 API 迁移

前端 API 层最终只调用原始 API 契约。旧接口调用全部迁移。

核心文件：

- `Web/NovelAgentWeb.Frontend/src/api/index.ts`
- `Web/NovelAgentWeb.Frontend/src/api/client.ts`
- `Web/NovelAgentWeb.Frontend/src/api/types.ts`

### 8.2 项目上下文

项目上下文使用单一 Zustand store：

- `currentProjectId`
- `setCurrentProject`
- `initializeFromStorage`

Materials、Knowledge、Workflow、Library、Rail、Agent 全部只从 store 读取当前项目。禁止各页面私有 `selectedProjectId` 长期存在。

### 8.3 页面闭环

- Agent 页面展示 decision、rag、memory、runtimeTrace 的可调试状态。
- Materials 页面支持上传、文本创建、显式向量化、删除。
- Knowledge 页面支持上传任务、处理进度、检索、编辑分类/标签/权重。
- Workflow 页面使用真实项目/章节/AgentRun/任务黑板数据，不使用空数组占位。
- Library 页面按真实项目状态展示书城数据。

---

## 9. 数据迁移策略

本轮允许破坏当前临时接口，但不允许无故丢数据。迁移策略：

1. 先备份 SQLite 数据库；旧 `App_Data` 文件仅作为一次性迁移输入。
2. 新增缺失字段和表，不直接删除旧列。
3. 对旧 `memory_type/content` 数据生成新语义字段或兼容视图。
4. 将旧章节 Markdown、素材文件、StoryBible JSON、Agent 产物文件迁移为 `content_documents`、`content_chunks`、领域引用和 `content_vector_points` 待索引记录，不迁移成散落的大字段。
5. 对旧 materials/knowledge/chapter/storybible/agent_run 数据补齐内容文档引用和向量化状态。
6. 迁移后运行时禁止继续从旧文件路径读取业务内容；如 Qdrant 未完成索引，必须从 SQLite 内容层读取并文本降级。
7. 对旧 frontend API 调用一次性迁移，不保留双写逻辑。
8. 最终清理旧接口和过时字段前必须有测试覆盖和用户确认。

---

## 10. 测试策略

必须覆盖以下层级：

1. **后端单元测试**
   - Memory repository。
   - 可选缓存 key、TTL、失效和本地降级。
   - ChatHistory compressor。
   - Knowledge service。
   - Tool search。
   - Agent decision fallback。

2. **后端集成测试**
   - `/api/projects`。
   - `/api/agent/sessions`。
   - `/api/chapters?projectId=...`。
   - `/api/characters`。
   - `/api/materials/{id}/vectorize`。
   - `/api/knowledge/upload` + task processing。

3. **向量检索测试**
   - Qdrant collection per user。
   - project_id filter。
   - knowledge/material/memory payload。
   - vector fallback。

4. **前端测试**
   - TypeScript build。
   - API layer smoke tests。
   - Project store cross-page consistency。
   - Materials/Knowledge/Workflow/Agent 页面 smoke test。

5. **端到端验证**
   - 后端 `5002`。
   - 前端 `3002`。
   - Redis 如启用，`6379` 健康状态可见；未启用时 UI/API 明确显示缓存降级。
   - 登录 -> 创建项目 -> 上传知识 -> 处理知识 -> Agent 搜索知识 -> 生成/规划 -> 记忆沉淀。

---

## 11. 实施顺序

### P0: 契约与基础设施

- 修复端口/CORS 为 `3002 -> 5002`。
- 明确 Qdrant HTTP/gRPC 配置。
- 明确 Redis 仅为可选缓存，配置、健康检查、缓存 key 规范和本地降级显示不能影响内容真源。
- 恢复原始 API 路由骨架与测试。

### P0: 数据库与迁移

- 补齐原始 schema。
- 新增统一内容层：`content_documents`、`content_chunks`、`content_vector_points`，以及章节、素材、知识上传任务、StoryBible snapshot、AgentRun artifact 的内容文档引用。
- 写 EF migration。
- 保留现有数据并补迁移脚本。
- 建立必要索引和外键。

### P0: 分层记忆闭环

- 补 SessionMemory repository/cache/read/write。
- 修复 Reflection 无 MemoryUpdate 时执行经验不持久的问题。
- 完成 ChatHistory 压缩容错。
- 完成记忆沉淀和冲突优先级测试。

### P1: Agent 决策闭环

- 固化 Chat/Tool/Reflection 三模式。
- 完成 tool_search TTL 和状态失效。
- 接入失败恢复路径。
- 稳定 response contract。

### P1: 知识库闭环

- 完成知识条目字段更新。
- 完成 usage count 与 MemoryUpdate 引用。
- 完成显式 material vectorize API。
- 完成 memory-aware rerank。

### P1: 前端迁移

- API 层迁移到原始契约。
- 全局 project store。
- Materials/Knowledge/Workflow/Library/Agent 页面接真实数据。

### P2: 清理与文档

- 清理旧路由、重复服务、未使用文件。
- 更新 README/部署文档。
- 清理生成物和本地数据库污染。

---

## 12. 风险与处理

1. **API 破坏风险**
   - 用户已确认可接受严格原始契约。
   - 处理方式：集中迁移前端，不做长期双轨。

2. **数据库迁移风险**
   - 处理方式：先备份，再新增字段/表，再迁移数据，最后清理。

3. **LLM 不稳定风险**
   - 处理方式：关键路径必须有规则兜底和测试，不只依赖提示词。

4. **Qdrant/可选缓存本地依赖风险**
   - 处理方式：健康检查公开真实运行模式；Qdrant 不可用时保留 SQLite 真源并允许文本检索降级；Redis 未启用或不可用时 UI 和 API 显示缓存降级。

5. **当前工作树污染**
   - 当前仓库包含生成物、Qdrant 数据、SQLite wal/shm、前端未提交修改。
   - 处理方式：实施时只 stage 任务相关文件，不回滚用户或生成过程产生的无关改动。

---

## 13. 完成标准

本轮全量实现完成时，应满足：

1. `http://localhost:3002` 前端能稳定访问 `http://localhost:5002` 后端。
2. 原始 API 文档中的核心接口有实现和测试。
3. Agent 回合后，ChatHistory、SessionMemory、ProjectMemory、AuthorMemory、ExecutionMemory 可持久化并在下一回合恢复。
4. 知识库上传文件后能处理、抽取、向量化、检索，并被 Agent 引用到记忆。
5. 前端项目上下文跨页面一致。
6. 后端 build、核心单元测试、关键集成测试、前端 build 全部通过。
7. `Docs/superpowers` 新增 implementation plan 与测试报告，能追溯每个修复任务。
