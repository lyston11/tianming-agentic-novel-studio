# 天命小说 Agent 目标架构设计

**状态：** 历史整合计划（已降级为背景资料）
**日期：** 2026-07-12  
**降级说明：** 自 2026-08-22 起，本文描述的"统一上下文/固定生产链"方案不再是当前架构真源。Agent Loop 架构以 Core-first 重置为准，权威文档见 `Docs/AGENT_CORE_ARCHITECTURE.md`（Trellis 任务 `.trellis/tasks/08-22-tianming-agent-core-foundation`）。本文仅作历史背景与决策溯源使用。
**替代范围：** 本文替代 `Docs/superpowers/specs` 中此前所有以通用 ReAct、平级业务工具、SQLite 生产库和重叠多层事实记忆为基础的设计。

## 1. 目标

构建一个以小说创作过程为中心的 Agent 系统，而不是让通用 Agent 在几十个工具之间反复执行 `Observe -> Plan -> Act -> Reflect`。

目标系统必须做到：

1. 导演 Agent 负责理解对话、协作方式和创作目标，不微操专业生产步骤。
2. 用户意图冻结为不可变 `CreativeGoal`，由规则骨架和模型补全共同编译任务图。
3. 设定、规划、写作、连续性审校、审美审稿、知识和记忆分别由专业混合内核承担。
4. 正文是最高优先级的叙事证据，Story Bible 只保存重要共识，不把小说世界过度结构化。
5. 自主创作以 3–5 章候选批次推进，用户可逐章查看、编辑、返工和按连续前缀验收。
6. 用户知识默认全局可用，上传后自动归纳分类，写作时自动进行长文本检索和引用追踪。
7. PostgreSQL 是唯一权威数据源，Qdrant 是可重建长文本语义索引，Redis 是协调与热点缓存。
8. 新系统一次性替换旧 Agent，不长期保留新旧生产路径。

## 2. 明确非目标

以下方案已撤回或禁止进入目标实现：

- 不保留通用 ReAct loop 作为小说生产控制器。
- 不让导演 Agent 调用平级业务工具来拼装设定、规划和章节生产流程。
- 不让专业内核直接修改权威业务表。
- 不把 Project Memory、Working Memory 或向量结果当作作品事实来源。
- 不为地点、能力、物品、承诺等所有细微变化建立庞大事件模型。
- 不使用 SQLite 作为生产权威库。
- 不使用本地文件目录、MinIO 或 S3 保存业务正文或上传内容。
- 不允许用户附加提示词覆盖系统协议、状态权限或输出 Schema。
- 不通过“开始”“继续”“写一章”等关键词硬路由判断执行意图。
- 不直接模仿用户上传文本，只允许提取抽象风格特征。

## 3. 总体运行模型

```text
用户对话
  -> 导演 Agent
  -> CommitmentAssessment
  -> CreativeGoal
  -> Goal Compiler
  -> Versioned Task Graph
  -> Deterministic Scheduler
  -> Professional Kernels
  -> Immutable Artifacts + Domain Events
  -> Contract Validator
  -> Domain Reducer
  -> PostgreSQL Transaction + Outbox
  -> Redis invalidation/fan-out + Qdrant indexing
```

系统不在每个内核产物之后重新执行通用反思。导演只在以下事件发生时重新决策：

- 新用户意图或 Goal 修订。
- 跨专业内核争议。
- 需要新增权限或预算。
- 目标完成、终止或无法继续。

专业内核可在自身领域合同内执行有限生成、验证和修复循环，不把微步骤暴露给导演。

## 4. 协作模式

项目保存默认模式，单次 Goal 可以覆盖；Goal 结束后恢复项目默认值。

### 4.1 精确执行

- 严格执行用户合同。
- 内核可报告冲突，但不得改变目标。
- 任何扩大范围或改变核心方向的动作都需要用户授权。

### 4.2 共同作者

- Agent 可主动提出创意、异议和替代方案。
- 可逆的小分歧可自动处理。
- 人物走向、核心设定和剧情方向等关键分歧交给用户裁决。

### 4.3 自主作者

- 默认授权 3–5 章候选批次。
- 候选分支内可逆且不改变批次目标的分歧可自动裁决。
- 涉及正式正史、读者承诺、硬约束或批次目标时暂停并询问用户。

所有争议保存 `Claim / Evidence / Impact / Alternatives / Decision / DecidedBy`。

## 5. 对话与语义承诺点

普通讨论不能自动创建执行任务。导演维护独立 `DialogueState`：

- `Exploring`：发散、讨论、比较。
- `Proposed`：目标基本形成但仍可修改。
- `Committed`：用户接受当前合同并授权执行。
- `Revising`：正在修改已冻结目标。
- `Cancelled`：撤销执行意图。

导演根据完整对话、已接受决定、指代对象、项目状态和协作模式输出结构化 `CommitmentAssessment`。确定性策略负责判断是否允许创建 Goal。

语义接近承诺但仍有歧义时，系统展示可读目标摘要和关键合同，并进行一次确认；不能依赖关键词或模糊的“是否继续”。点击执行按钮是无歧义授权。

## 6. CreativeGoal 契约

`CreativeGoal` 提交后不可变。任何中途变化创建 `GoalRevision`，重新评估受影响任务子图。

核心字段：

```text
goal_id
user_id
project_id
source_session_id
goal_type
collaboration_mode
human_readable_objective
target_chapter_range
success_criteria
must_preserve
must_happen
must_not_change
acceptance_policy
rework_policy
total_cost_limit
canon_baseline_version
knowledge_snapshot_version
quality_contract_version
style_profile_version
model_config_versions
protocol_versions
status
created_at
```

授权前默认展示：目标、章节范围、协作模式、必须保留、必须发生、禁止项、成功条件、返工范围、总金额上限、验收方式和暂停条件。内部完整合同与任务图可展开查看。

`GoalRevision` 保存原目标、变化原因、新增或撤销约束、可复用产物、失效产物和新任务图版本。

## 7. Goal Compiler 与任务图

采用“确定性编译 + 持久需求分析 Kernel”：

- 确定性规则生成不可跳过的阶段、依赖、权限和门禁。
- `AnalyzeCreativeRequirements` Kernel 在持久任务中分析本书特有目标、人物变化和审美成功条件，统一进入预算、`ModelExecution`、lease、恢复与失败传播。
- 图校验器拒绝循环依赖、缺失产物、越权写入、缺少审稿或无法验收的任务图。
- 任务图按 Goal Revision 版本化，变化时只重跑受影响子图。

章节批次的必经骨架：

```text
Freeze Baselines
  -> Analyze Creative Requirements
  -> Compile Batch Plan
  -> For each chapter:
       Plan Chapter
       Compile Context and Knowledge
       Write Candidate
       Validate Protocol and Continuity
       Review Literary Quality
       Directed Rework when allowed
       Extract Lightweight Continuity Summary
  -> Batch Impact Analysis
  -> User Acceptance
  -> Prefix Merge
```

## 8. 专业混合内核

每个内核由受约束 LLM 能力和确定性工作流共同组成，只输出不可变 Artifact 和 Domain Event。

### 8.1 导演 Agent

理解对话、协作模式、承诺点、Goal 修订和跨内核争议。只提交 `CreativeGoal`，不调用专业内核或业务工具。

### 8.2 设定内核

负责 Story Bible 提案、核心设定一致性和高影响设定争议。不能直接修改 Story Bible，只能生成候选设定产物。

### 8.3 叙事规划内核

负责全书、卷弧、章节目标、伏笔、节奏、读者承诺和批次计划。

### 8.4 天命写作内核

负责上下文合同消费、正文创作和定向局部改写。它不负责自行检索整个知识库，不负责自证连续性，也不能直接提交正式章节。

### 8.5 连续性审校内核

独立于写作模型，基于正文证据判断人物、时间线、因果、设定和前后承接。结构化检查只产生疑点，语义审校负责解释或确认矛盾。

### 8.6 审美审稿内核

独立于连续性审校，负责人物可信度、剧情推进、节奏、悬念、情绪、文风、原创性和读者承诺。

### 8.7 知识加工与检索内核

上传后自动解析、归纳、分类、总结、提取 StyleProfile 和建立多尺度索引；创作时编译任务级 Knowledge Pack。

### 8.8 协作记忆内核

维护用户偏好、项目决定、会话状态和经验建议。经验只能先向用户建议，未经接受不能改变写作参数、检索排序或质量合同。

## 9. 内核写入协议

统一链路：

```text
Kernel Artifact
  -> Domain Event
  -> Contract Validator
  -> Domain Reducer
  -> PostgreSQL Transaction
  -> Outbox
```

事件至少包含：

```text
event_id, user_id, project_id, goal_id, task_id, branch_id,
aggregate_version, event_type, artifact_refs, evidence_refs,
causation_id, correlation_id, model_execution_id
```

Reducer 必须校验用户权限、Goal 版本、分支基线、任务状态、聚合版本和幂等键。内核不能通过自由文本隐式修改其他领域。

## 10. 候选正史分支与批次

自主批次创建独立 `CanonBranch`，冻结正式正史基线和其他合同版本。

- 后章可以依赖同批次前章的候选正文和候选连续性摘要。
- 候选事实不写入正式 Story Bible。
- 每章保留正文、版本、审稿、知识引用、模型配置和生产轨迹。
- 批次可逐章查看，也可整批查看。
- 只允许连续前缀合并；不能跳过未确认章节合并后章。
- 合并在一个 PostgreSQL 事务中提交章节版本、轻量正史、分支基线和 Outbox。

若 5 章中第 1–3 章通过、第 4 章返工、第 5 章依赖第 4 章，则只合并 1–3 章。第 4–5 章留在分支，以新正史为基线重新校验。

其他会话修改正式正史时，当前 Goal 继续使用冻结基线；合并前比较 `Goal Baseline / Current Canon / Candidate Branch`，无交集变化可自动重基线，有关键冲突则生成 RevisionPlan 或进入 `NeedsDecision`。

## 11. 人工编辑与定向返工

用户编辑产生新的候选版本，标记 `Authorship=Human`、`Protected=true`。专业内核可以审校和建议，但不能覆盖人工正文。

用户可通过选区交互或自然语言描述问题。导演结合章节、版本、选区和对话语义生成 `ReworkIntent`：

```text
target_scope
problem
desired_effect
preserve
may_change
must_not_change
acceptance_criteria
impact_assessment
```

写作内核只修改合同指定范围。连续性和审稿内核先验证用户指出的问题是否解决，再检查是否引入回归。

影响传播采用分级策略：

- 文案级变化不传播。
- 局部事实变化只重新校验受影响后章。
- 关键剧情变化生成 RevisionPlan，用户确认后才改写后章。
- 硬冲突阻止合并但不删除候选稿。

审稿内核可主动发现问题。候选分支中证据明确、范围明确且不改变核心方向的问题可自动修复；主观审美或重大剧情选择只提出建议。

自动返工预算：局部问题最多 2 次，整章问题最多 1 次；同一问题无改善或影响范围扩大时提前停止并进入 `NeedsDecision`。

## 12. 轻量正史

正文是最高优先级叙事证据。目标系统不尝试用关系模型完整描述小说世界。

权威内容只分为：

1. 章节正文与版本。
2. Story Bible 中用户确认的重要人物、世界规则和长期设定。
3. 每章少量连续性摘要：关键变化、未决事件、重要承诺、伏笔和下一章承接点。
4. 少量重大 `CanonChange`：死亡、身份公开、核心能力永久变化、重大承诺兑现等。

不为临时情绪、每次移动、能力冷却或无叙事意义的地点建立复杂状态事件。自动摘要只是导航，漏掉内容不代表正文事实不存在。

连续性判断流程：

```text
Lightweight summary raises suspicion
  -> Qdrant locates distant evidence
  -> PostgreSQL loads original chapters
  -> Continuity kernel decides from full-text evidence
```

Story Bible 修改默认只对未来 Goal 生效。已完成章节不自动返工；用户可主动请求追溯影响分析。

## 13. 双通道连续性校验

### 13.1 结构化通道

检查时间、地点、人物关键状态、任务必达项、禁止项、引用版本和正文状态变化，输出 `PotentialViolation`，不独立裁决复杂叙事语义。

### 13.2 语义通道

独立连续性模型读取完整正文、相关正史、候选分支、疑点和原文证据，并输出：

```text
Claim
SupportingEvidence
ContradictingEvidence
NarrativeExplanation
Confidence
Verdict
```

规则报警但语义证据能解释谎言、误解、伪装或合理变化时，记录章节版本级例外。语义通道发现规则未覆盖的矛盾仍可阻止通过。人物生死、核心设定、重大因果和读者承诺存在高影响争议时进入 `NeedsDecision`。

确定性规则只对版本不存在、分支错误、引用越权、事件缺失和事务状态非法等系统不变量拥有最终裁决权。

## 14. 质量合同

采用“系统基础维度 + 项目 QualityContract”。

硬门禁检查正史冲突、时间线错误、关键状态矛盾、硬知识约束、章节必达项和协议完整性。多维评价分别给出证据，不以总分相互抵消：

- 人物可信度。
- 剧情推进。
- 节奏与悬念。
- 情绪效果。
- 文风与语言。
- 原创性。
- 读者承诺。

审稿结论为 `Pass / PassWithSuggestions / ReworkRequired / NeedsDecision`。主观审美不足不能伪装成硬错误。

## 15. 知识自动加工

知识默认是用户级全局资产，所有用户自己的项目都可召回。

上传流程自动执行：

```text
Upload
  -> Parse
  -> Semantic sectioning
  -> Summarize
  -> Classify
  -> Extract entities, rules and reusable knowledge
  -> Extract abstract StyleProfile
  -> Persist in PostgreSQL
  -> Index in Qdrant
```

用户不需要要求导演调用 `ProcessKnowledgeFile`、`ClassifyProjectKnowledge` 或 `AttachKnowledgeToProject`。手工分类、禁用、修正摘要和“必须参考”只作为纠错与高级治理功能。

普通素材、写作方法和抽象风格可自动进入 Knowledge Pack。可能改变世界设定、人物身份、能力规则、重大剧情或读者承诺的知识生成 `KnowledgeProposal`，用户接受后成为项目级创作决定，但仍需在候选章节验收后才能成为具体正史。

项目实际采用知识时记录项目、章节、版本、用途、来源和次数。知识修改产生新版本：历史章节冻结原版本，未来 Goal 使用最新有效版本。每个 Goal 冻结 `KnowledgeSnapshot`，批次中途更新的知识默认从下一个 Goal 生效。

上传文本只提取抽象 StyleProfile，例如叙事距离、句式节奏、对白密度、描写比例、意象、情绪强度和信息释放方式；不得生成“模仿某作者”的指令，不把原文片段作为续写模板。

## 16. Qdrant 长文本召回

Qdrant 是长文本检索核心，但不是正文真源。

### 16.1 多尺度索引

- 文档级摘要向量。
- Section/章节级摘要向量。
- 按语义边界切分的 Chunk 向量，携带 parent、顺序和邻接信息。
- 知识条目、事实、事件摘要和 StyleProfile 向量。

### 16.2 第一阶段检索链

```text
Query Planning
  -> Qdrant Dense Retrieval
     + PostgreSQL Full-text Search
     + Entity/Dependency Queries
  -> RRF and deterministic fusion
  -> Lightweight reranking
  -> Parent/neighbor expansion from PostgreSQL
  -> Deduplication and conflict marking
  -> Evidence Bundle
  -> Knowledge Pack / Chapter Context Contract
```

查询按设定、人物、连续性、叙事承诺、知识素材和抽象文风拆分。Qdrant 每路召回候选 ID；系统必须回 PostgreSQL 校验用户、项目、分支、版本和状态，并读取完整片段及邻窗。

最近 2–3 章直接读取摘要、结尾状态和必要正文。远距离剧情通过章节摘要、实体/事件线索、剧情依赖和原文片段多路召回。

第二阶段可增加 Sparse Vector 和 Cross-Encoder Reranker，但不改变 Evidence Bundle 与上层内核合同。

## 17. 多层协作记忆

多层记忆保留，但不再复制作品事实。

### 17.1 Author Memory

跨项目保存用户写作偏好、协作习惯、禁忌和全局接受的抽象风格偏好。

### 17.2 Project Collaboration Memory

在同一项目内跨会话保存已接受的创作决定、默认协作模式、长期审美方向和未决创作问题。人物状态、世界规则和章节事实不属于此层。

### 17.3 Session Dialogue State

保存当前会话中的提案、备选、指代、承诺判断、未接受想法和当前 ReworkIntent。新会话不继承未接受提案。

### 17.4 Experience Memory

保存检索效果、修订效果、失败模式和模型表现观察，只能生成建议。用户可 `Accept / Reject / TryOnce`：

- Accept 默认转为当前项目决定，可手动提升为全局偏好。
- TryOnce 只覆盖下一个 Goal，结束后提供效果对比。
- Reject 保留审计且抑制重复建议。

### 17.5 Working Context

不是持久记忆。它是每次任务从正史、Goal、知识快照、项目决定、会话状态和相关经验临时编译的只读视图。

## 18. 模型与提示词配置

每个专业内核拥有独立模型配置，使用三级覆盖：

```text
System preset -> Project configuration -> Goal override
```

系统提供质量优先、均衡、成本优先预设。普通设置允许配置 Provider、Base URL、API Key 引用、模型、温度、最大输出、超时和显式 fallback。

高级设置只允许填写按内核隔离的 `CustomInstructions`，不能编辑完整模板。提示词装配顺序：

```text
System safety contract
  -> Kernel responsibility contract
  -> Input/output schema
  -> QualityContract and StyleProfile
  -> User CustomInstructions
  -> Current CreativeGoal
```

附加提示词与硬协议冲突时忽略冲突部分并向用户解释。每个候选版本保存实际模型、参数、配置版本、协议版本和 fallback 过程。

## 19. PostgreSQL 权威数据模型

生产环境只支持 PostgreSQL。所有业务事实、正文、上传二进制、知识、记忆和任务状态均保存在 PostgreSQL。

核心表组：

```text
Identity:
  users, roles, user_model_credentials

Projects and canon:
  projects, story_bible_versions, chapters, chapter_versions,
  continuity_summaries, canon_changes

Goals and execution:
  creative_goals, goal_revisions, task_graph_versions, kernel_tasks,
  kernel_artifacts, domain_events, model_executions, revision_plans

Candidate branches:
  canon_branches, candidate_chapters, candidate_acceptances,
  branch_merge_records

Knowledge:
  knowledge_documents, knowledge_document_blobs, knowledge_document_texts,
  knowledge_sections, knowledge_chunks, knowledge_entries,
  style_profiles, project_knowledge_usages, knowledge_citations,
  vector_index_records

Memory and dialogue:
  author_memories, project_collaboration_decisions,
  session_dialogue_states, experience_observations,
  experience_suggestions, goal_context_snapshots

Reliability:
  outbox_events, idempotency_records, leases_audit
```

关键身份、用户归属、状态、版本、章节关系和依赖必须关系化；可变审稿报告、模型扩展产物和诊断可使用 JSONB。上传 PDF/Word 等原始二进制保存在独立 `BYTEA` 表，避免普通查询加载大字段。

PostgreSQL 使用：

- `FOR UPDATE SKIP LOCKED` 原子领取任务与 Outbox。
- 聚合版本或 `xmin` 检测陈旧写入。
- 部分唯一索引约束 active Goal、active task 和幂等键。
- 状态变更与 Outbox 同事务提交。

### 19.1 用户隔离不变量

- 所有用户拥有的聚合和派生记录必须显式携带 `user_id`，项目级记录同时携带 `project_id`。
- API、SSE、后台任务、Reducer 和检索入口必须先从认证主体取得 `user_id`，再校验目标资源归属；不得把客户端提交的 `user_id` 当作授权依据。
- PostgreSQL 查询、唯一约束、外键或复合键必须包含用户作用域；生产环境启用并测试 Row-Level Security 作为纵深防御，应用层校验仍不可省略。
- Qdrant 每次查询和删除必须包含 `user_id` 过滤；项目知识额外按 Goal 的允许范围过滤。命中结果回 PostgreSQL 后再次验证归属、版本和状态。
- Redis key、lease、缓存和 SSE stream 必须包含用户作用域。SSE 订阅在 replay 和实时读取前都要校验 session、Goal 或 run 属于当前用户。
- 后台任务和 Outbox payload 必须携带权威 `user_id`，worker claim 后重新验证资源归属；任何作用域缺失都按协议错误拒绝执行。
- 自动化测试必须覆盖两个并发用户使用相同资源局部 ID 时，正文、知识、记忆、向量、缓存、SSE 和任务事件均不可互见。

## 20. Redis 职责

Redis 只负责可恢复的协调和热点数据：

- Goal、批次和任务分布式 lease。
- SSE fan-out 和短期 replay。
- 版本化项目快照、Chapter Context Contract、Knowledge Pack 和会话摘要。
- 热点查询和进度视图。

缓存键必须包含 `userId/projectId/branchId/version` 等完整作用域。数据库事务提交领域事件后主动失效缓存，TTL 仅作兜底。

Redis 清空后任务和作品不丢失；系统从 PostgreSQL 恢复队列和缓存。Redis 不可用时停止领取需要分布式协调的新增长任务，但权威读取仍保持正确。

## 21. Qdrant 职责

Qdrant 保存可重建 embedding 和定位元数据，不保存唯一正文：

- 用户全局知识与 StyleProfile。
- 正式章节、候选章节、摘要和重大 CanonChange。
- 文档、Section、Chunk 多尺度向量。

过滤字段至少包括 `user_id / project_id / branch_id / source_type / source_id / version / status / chapter_id / content_hash / embedding_version`。

Qdrant 返回候选 ID，PostgreSQL负责权限、版本、分支和完整内容。Qdrant 全量丢失时通过 `vector_index_records` 和内容哈希重建。

## 22. 工作流界面

采用混合控制台：

- 左侧显示批次、章节导航、候选状态和受影响章节。
- 中间默认显示正文，可切换审稿、知识引用和版本差异。
- 右侧显示当前内核任务、Goal 状态、总金额消耗和争议项。
- 完整任务图和领域事件默认折叠，只在高级查看或排错时展开。
- 选中文本可直接描述问题并创建定向 ReworkIntent。

每章可查看：正文、人工/Agent 版本、连续性证据、审美报告、使用知识及版本、模型配置、成本和生产轨迹。

旧书城视图、知识面板、工作流操作台、候选卡、执行图和事件展示属于产品能力，不属于旧 ReAct 生产控制器，必须保留并逐步接入新 Goal/DAG 查询与命令 API。不得仅以“当前没有 TSX import”或“CSS class 暂未引用”为依据删除这些界面、组件或样式；只有产品能力明确下线且完成数据与交互迁移后，才能独立评审删除。

Goal 实时事件使用 Goal 的权威 `SourceSessionId`，通过 `user_id + session_id` 作用域的本地 SSE bus 与 Redis fan-out/replay 发布。创建 Goal 前必须验证来源会话属于当前用户；返工请求只能沿用该 Goal 的来源会话，客户端不能改写审计会话。任务开始、完成、失败、安全点、候选变化、返工、验收、前缀合并、暂停、恢复和取消均发布 `goal_*` 事件。新事件不写入旧 `agent_runtime_events`；Goal Console 只消费 `data.goalId` 与当前 Goal 一致的事件，周期查询仅作降级恢复。

## 23. 预算、暂停、取消与恢复

### 23.1 总金额硬上限

用户为每个 Goal 设置总金额上限。每次模型调用前按模型单价和最大输出预留最坏成本；余额不足则不发起调用。达到上限后 Goal 进入 `BudgetExceeded` 终止态，保留所有候选产物。系统不能静默换便宜模型或降低质量门禁。

追加预算通过 GoalRevision 完成。

### 23.2 安全点暂停

暂停后停止派发新任务。当前模型请求可完成，结果保存为 `UnadoptedArtifact`，但不推进状态。到达安全点后释放 lease。恢复时重新验证 Goal、基线、知识快照和产物合同。

### 23.3 取消

用户取消时选择：

1. 保留候选分支。
2. 合并已通过的连续前缀，其余保留。
3. 放弃候选分支并按保留策略清理正文、Redis 和 Qdrant 派生数据。

### 23.4 崩溃中的模型调用

调用前持久化 `ModelExecution` 和预留金额。崩溃后状态为 `OutcomeUnknown`，按预留金额保守计费。Provider 支持幂等查询时先查询原结果；无法确认时自动重试一次。第二次仍失败则终止 Goal。延迟返回的旧结果只能保存为未采用产物。

## 24. 一次性迁移与上线

允许维护窗口，一次性替换旧 Agent：

1. 停止旧任务或等待完成，冻结写入。
2. 建立独立 PostgreSQL baseline migration，不重放含 SQLite `PRAGMA` 的旧迁移。
3. 将 SQLite 只读导入 PostgreSQL staging，完成类型转换和数据映射。
4. 核验用户、项目、章节版本、Story Bible、知识、记忆、引用、行数、外键和内容哈希。
5. 将知识改为用户级全局资产，保留已有项目使用记录。
6. Author Memory 迁移为全局偏好；已确认 Project Memory 迁移为项目决定；重复作品事实不迁移。
7. 不迁移旧 WorkingMemory、MissionPlan、未确认会话提案和未完成 ReAct 中间态。
8. 旧 Agent run、工具 ledger 和事件仅保留为只读审计。
9. 重建 Qdrant 多尺度索引。
10. 切换生产连接和路由，启动新系统完整性检查。
11. 检查失败时整体回滚 PostgreSQL、SQLite 和 Qdrant 快照。

新旧生产系统不长期并存，不提供项目级切换开关。

“旧生产系统退役”只涉及执行代码、写 API、队列、状态机和工具调用实现，不等于删除仍有产品价值的书城、知识、工作流和审计界面。保留界面必须改为读取 PostgreSQL 权威状态，并通过 Goal/Kernel API 发起受控写操作。

## 25. 验收标准

### 25.1 对话与 Goal

- 讨论不会因关键词误启动任务。
- 语义承诺点能生成可读合同并等待必要确认。
- GoalRevision 只重编译受影响任务子图。

### 25.2 小说生产

- 3–5 章候选批次可连续生产。
- 后章可依赖前章候选内容。
- 人工版本不会被 Agent 覆盖。
- 章节可定向返工并生成影响分析。
- 只允许连续前缀合并。

### 25.3 连续性与质量

- 结构化疑点与语义正文证据双通道工作。
- 连续性和审美使用独立模型配置。
- 主观审美不能伪装成硬错误。
- 自动返工遵守范围与次数预算。

### 25.4 知识与 RAG

- 上传内容自动完成解析、归纳、分类和 StyleProfile 提取。
- 用户全局知识可被任意自己的项目召回。
- 项目实际使用会记录章节、版本、用途和知识版本。
- Qdrant 命中后必须回 PostgreSQL 读取完整证据。
- 长文本检索可以定位远距离章节和资料，并展开父节点与邻窗。

### 25.5 数据与恢复

- PostgreSQL 是所有业务事实的唯一权威来源。
- Redis/Qdrant 清空后不丢作品，可从 PostgreSQL 恢复。
- 多 worker 原子 claim 不重复执行。
- Goal 金额上限不会因并发预留而超支。
- 暂停、取消、崩溃和 OutcomeUnknown 均可审计恢复。

### 25.6 迁移

- SQLite 与 PostgreSQL 的用户、项目、正文和章节版本逐项一致。
- 旧知识改为全局可用但用户隔离不变。
- 不把旧运行态和重叠事实记忆带入新系统。
- 一次性切换失败时可以整体回滚。

## 26. 实施拆分原则

本规范范围较大，实施计划必须拆为可独立验收的阶段，但最终上线仍为一次性切换：

1. PostgreSQL baseline、迁移器与数据所有权。
2. CreativeGoal、GoalRevision、任务图、Reducer 和调度器。
3. 专业内核合同与模型配置。
4. CanonBranch、批次、返工与连续前缀合并。
5. 知识自动加工、Qdrant 多尺度索引与 Context Compiler。
6. 多层协作记忆和 Experience Suggestion。
7. 混合工作流控制台。
8. 全量迁移演练、回归评测、维护窗口切换与回滚。

每一阶段都必须先建立契约测试和失败用例，再实现最小闭环；不得在新架构中重新引入通用工具 ReAct 作为捷径。
