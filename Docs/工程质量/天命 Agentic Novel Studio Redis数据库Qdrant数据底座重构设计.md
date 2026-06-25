# 天命 Agentic Novel Studio Redis、数据库、Qdrant 数据底座重构设计

状态：待合并到 Obsidian 主重构文档

主文档：

- `/Users/lyston/Obsidian/lyston/Codex/天命AI写作/天命 Agentic Novel Studio 知识库多项目与工作流生产内核融合重构设计.md`

说明：

本轮由于当前执行环境只能写入项目工作区，且不能申请写入 Obsidian 目录的授权，因此先将本节作为独立文档落在仓库内。后续权限恢复后，应将本文内容合并到主重构文档，作为 `Redis / 数据库 / Qdrant 数据底座重构设计` 章节。

## 1. 核心职责边界

Redis、数据库、Qdrant 不能只是系统里的三个存储组件。它们必须重新定义职责边界，否则 Agent Loop、工作流进度、知识检索、章节连续性、多项目隔离都会继续混乱。

核心原则：

```text
数据库 = 唯一真相源
Redis = 运行时协调和实时事件
Qdrant = 语义检索索引
```

三者不能互相替代。

```text
数据库管真相。
Redis 管运行中。
Qdrant 管召回。
Agent 管决策。
天命内核管生产。
工作流管展示。
书城管最终作品。
```

如果某个信息会影响最终作品、连续性、章节版本、项目状态，它必须在数据库里。

如果某个信息只是为了实时反馈、并发协调、短期缓存，它可以在 Redis。

如果某个信息只是为了语义相似召回，它可以进入 Qdrant，但不能成为硬事实。

## 2. 数据库职责

数据库必须承载所有可回放、可审计、可恢复的状态。

数据库是硬事实来源，Agent、天命生产内核、工作流、书城都应该以数据库为准。

数据库应保存：

```text
产品层：
- 用户
- 项目
- 卷
- 章节
- 正文版本
- 书城展示状态
- 工作流 run
- 工作流 step
- Agent 会话
- chat turns

天命生产结构层：
- Story Bible
- Design Rules
- Outline
- Volume Plan
- Chapter Plan
- Chapter Blueprint
- Tianming Package
- ContextIds

创意层：
- CreativeIntent
- CreativeDecision
- RevisionPlan
- ImpactAnalysis
- Chapter Version

知识层：
- Knowledge Entry
- Knowledge Directory
- Project Knowledge Binding
- Knowledge Classification
- Knowledge Snapshot

连续性事实层：
- FactSnapshot
- CharacterState
- LocationState
- ItemState
- TimelineEvent
- ForeshadowingState
- ConflictProgress

生成与质量层：
- ChapterDraft
- CHANGES
- GenerationGateReport
- RewriteAttempt
- AgentReview
- QualityReview

运行审计层：
- RuntimeRun
- ToolExecutionLedger
- ProductionEvent
- OutboxEvent
- IndexStatus
```

凡是会影响小说最终结果、项目状态、章节连续性、用户可追溯历史的内容，都必须进入数据库。

## 3. Redis 职责

Redis 不应该保存小说事实，也不应该保存最终状态。

Redis 适合保存短期、运行中、可丢失后从数据库恢复的状态。

Redis 应用于：

```text
- active run 状态
- 长任务心跳
- SSE / WebSocket 实时事件
- interrupt queue
- 工具调用锁
- 幂等 key
- 短期 Observe Pack 缓存
- tool_search 工具目录缓存
- 后台任务队列
- worker lease
- 防重复提交锁
```

示例：

```text
用户问：现在执行到哪了？

Agent 可以先查 Redis：
- 当前 active run
- 最近心跳
- 最近 runtime events
- 是否有 pending interrupt

但最终可追溯记录必须在数据库 production_events / runtime_runs 中。
```

Redis 可以快，但不能当真相源。

## 4. Qdrant 职责

Qdrant 只负责语义召回，不负责事实判断。

Qdrant 适合索引：

```text
- 知识库 chunks
- 章节正文 chunks
- 章节摘要
- Story Bible 摘要
- FactSnapshot 摘要
- 项目记忆摘要
- 用户偏好摘要
- 执行经验摘要
```

Qdrant 返回的内容只能是候选上下文，不能覆盖数据库硬事实。

例如：

```text
Qdrant 召回到某段文本：邮徽像武器。
数据库 FactSnapshot / Knowledge Binding：邮徽不能攻击。

最终必须以数据库硬事实为准。
```

## 5. 三层数据流

章节提交后的正确数据流：

```text
CommitChapter
-> 数据库事务写入：
   - chapter version
   - content document
   - CHANGES
   - FactSnapshot
   - production_events
   - outbox_events
-> Redis 推送实时事件给前端
-> 后台 worker 消费 outbox
-> 写入 Qdrant 向量索引
-> 数据库更新 index_status
```

这样即使 Redis 事件丢失、Qdrant 索引失败，数据库仍然完整。

恢复时：

```text
数据库 production_events 可重建工作流历史。
数据库 outbox_events 可重试索引。
数据库 index_status 可判断哪些内容未完成向量化。
Redis active run 可从 runtime_runs 恢复。
```

## 6. Agent Observe Pack 的读取顺序

Agent Observe Pack 应按硬度读取上下文：

```text
1. 数据库读取硬状态：
项目、卷、章节、正文版本、FactSnapshot、知识绑定、工作流持久事件、创意状态。

2. Redis 读取运行时状态：
active run、心跳、interrupt、实时进度、锁、短期缓存。

3. Qdrant 召回软上下文：
相关知识、历史章节片段、记忆摘要、执行经验、长距离召回。

4. 大模型判断：
哪些是硬事实，哪些只是参考，是否需要工具继续查询。
```

原则：

```text
硬事实来自数据库。
运行状态来自 Redis。
相似内容来自 Qdrant。
最终决策由 Agent 大模型完成。
```

## 7. 多项目隔离

三层都必须严格隔离项目和用户。

数据库：

```text
所有项目数据必须带：
- owner_user_id
- project_id
- visibility
- status
```

Redis key：

```text
agent:{user_id}:{session_id}:active_run
agent:{user_id}:{project_id}:{run_id}:heartbeat
agent:{user_id}:{project_id}:{run_id}:interrupts
agent:{user_id}:{project_id}:{tool_name}:{idempotency_key}
```

Qdrant payload：

```json
{
  "userId": "...",
  "projectId": "...",
  "sourceType": "knowledge_entry | chapter | fact_snapshot | memory | execution_memory",
  "sourceId": "...",
  "version": 3,
  "visibility": "global | user | project",
  "status": "active"
}
```

检索时必须强制 filter：

```text
userId = 当前用户
projectId = 当前项目或允许为空的全局 / 用户级资料
status = active
visibility 符合权限
```

不能再出现 A 项目的主角、章节、知识被 B 项目召回进去。

## 8. Qdrant Collection 设计

不建议所有向量都塞进一个 collection。

建议按语义和访问模式拆分：

```text
knowledge_chunks：
知识库条目、上传文档、规则素材。

project_content_chunks：
章节正文、章节摘要、已提交内容。

project_state_summaries：
Story Bible、FactSnapshot、角色状态、世界状态摘要。

agent_memory_chunks：
用户记忆、项目记忆、会话摘要。

execution_memory_chunks：
工具失败经验、门禁失败模式、修复经验。
```

每个 point 至少保存：

```json
{
  "userId": "...",
  "projectId": "...",
  "sourceType": "chapter",
  "sourceId": "chapter-002",
  "sourceVersion": 2,
  "chunkIndex": 4,
  "textHash": "...",
  "status": "active",
  "visibility": "project",
  "createdAt": "..."
}
```

## 9. Outbox 与索引一致性

数据库写入和 Qdrant 写入不能放在同一个强事务里。

建议采用 Outbox：

```text
数据库事务：
- 写章节正文
- 写 FactSnapshot
- 写 production_event
- 写 outbox_event: index_chapter_content

后台 worker：
- 读取 outbox_event
- 切 chunk
- 生成 embedding
- 写 Qdrant
- 更新 index_status
- 标记 outbox_event completed
```

如果 Qdrant 失败：

```text
- 不回滚章节提交。
- outbox_event 标记 failed / retryable。
- 工作流展示“章节已提交，索引稍后重试”。
- Agent 查询硬事实仍可读数据库。
```

## 10. Redis 事件与数据库事件的关系

Redis event 用于实时体验。

数据库 event 用于审计和恢复。

同一生产事件应先持久化或最终持久化到数据库，再推送到 Redis。

```text
production_events：长期保存。
runtime_events：可长期保存，也可按策略归档。
redis pub/sub / stream：实时推送。
```

前端掉线后：

```text
重新进入页面
-> 从数据库读取历史 production_events
-> 从 Redis 读取 active run 最新状态
-> 继续订阅实时事件
```

## 11. Redis Key 建议

```text
agent:run:{run_id}:state
agent:run:{run_id}:heartbeat
agent:run:{run_id}:events
agent:run:{run_id}:interrupts
agent:session:{session_id}:active_run
agent:project:{project_id}:active_runs
agent:tool:{tool_name}:{idempotency_key}:result
agent:tool_catalog:{schema_hash}
agent:observe_pack:{session_id}:{message_id}
lock:project:{project_id}:produce_chapter:{chapter_id}
lock:chapter:{chapter_id}:commit
```

Redis TTL 策略：

```text
active run：任务结束后保留短时间。
heartbeat：短 TTL。
tool result 幂等缓存：中等 TTL。
tool catalog：schema hash 变化失效。
observe pack：很短 TTL。
locks：必须有 TTL，避免死锁。
```

## 12. 数据库关键约束

必须加项目作用域唯一约束，避免旧问题复发。

```text
chapters：
unique(project_id, chapter_number)
unique(project_id, chapter_slug)

volumes：
unique(project_id, volume_number)

chapter_versions：
unique(chapter_id, version_number)

project_knowledge_bindings：
unique(project_id, knowledge_entry_id, scope, status_active)

runtime_runs：
index(session_id, status)
index(project_id, status)

production_events：
index(run_id, created_at)
index(project_id, chapter_id, created_at)

outbox_events：
index(status, next_retry_at)
```

章节 ID 不应该再用全局 `chapter-001` 这种容易跨项目冲突的设计。显示名可以是“第 1 章”，内部 ID 必须全局唯一或项目作用域唯一。

## 13. 数据库迁移原则

不能再因为清理运行产物把数据库还原成旧 schema。

需要：

```text
- 所有 schema 变化必须有 migration。
- 禁止依赖手工旧数据库文件。
- 启动时检查 schema version。
- schema 不匹配时明确报错，不静默运行。
- 测试数据库和开发数据库分离。
- 运行产物清理不能删除或回滚 schema。
```

## 14. 数据底座验收标准

1. 数据库能完整恢复工作流历史，不依赖 Redis。
2. Redis 丢失后，active run 能从数据库恢复或明确标记为 interrupted。
3. Qdrant 索引失败不影响章节提交。
4. Agent 查询硬事实不依赖 Qdrant。
5. Qdrant 检索必须带 user/project filter。
6. 同一知识可被多个项目引用，但绑定、分类、使用记录按项目隔离。
7. 同一章节重写后产生新版本，旧版本可追溯。
8. 工作流能显示索引状态、后台任务状态和失败重试。
9. tool_search 工具缓存 schema 变化后自动失效。
10. 任何最终产物都能从数据库定位来源：知识、创意、生产包、门禁、AgentReview、FactSnapshot。

## 15. 最终判断

这套边界成立后，系统才不会因为缓存丢失、向量召回漂移、运行事件中断，导致小说事实、工作流状态或 Agent 决策失真。

最终数据底座应服务于这个闭环：

```text
Agent 自主决策
-> 天命内核生产
-> 数据库事务保存硬事实
-> Redis 推送运行进度
-> Qdrant 异步索引召回内容
-> 工作流展示真实生产事件
-> 书城展示最终作品
-> Agent 后续 Observe 时按数据库、Redis、Qdrant 的硬度顺序读取上下文
```
