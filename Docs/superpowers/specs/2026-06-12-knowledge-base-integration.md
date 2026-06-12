# 知识库集成修复设计文档

## 概述

**目标**：修复知识库系统的三大问题
1. 实现文件自动处理管道（上传后由 Agent 读取、拆解、归纳、总结）
2. 将知识库检索从文本匹配改为 Qdrant 向量检索
3. 建立记忆与知识库的关联关系

**设计原则**：
- 结构化数据 + RAG 语义检索相结合
- 支持多种文件类型（结构化知识、创意素材、混合内容）
- 短文件和长文件采用不同处理策略
- 复用现有架构（SQLite、Qdrant、Agent 工具系统）

---

## 整体架构

### 数据流

```
用户上传文件
  ↓
文件存储 + 创建 ProcessingTask (SQLite)
  ↓
前端/Agent 触发处理
  ↓
文件长度判断
  ├─ 短文件(<6K tokens): LLM 单次分析
  └─ 长文件(≥6K tokens): 分块分析 → 聚合总结
  ↓
提取知识条目 (标题、分类、内容、标签)
  ↓
双存储写入
  ├─ SQLite: Knowledge 表 (元数据 + 结构化字段)
  └─ Qdrant: 向量库 (分块语义检索)
  ↓
更新 ProcessingTask 状态 (progress: 0-100)
  ↓
Reflection 阶段: 自动关联到 ProjectMemory/AuthorMemory
```

### 核心组件

1. **KnowledgeProcessingService** - 文件处理编排器
   - 文件分块逻辑
   - LLM 调用编排
   - 进度追踪

2. **Agent 工具: ProcessKnowledgeFile**
   - 工具名称: `ProcessKnowledgeFile`
   - 可用阶段: Idle, Planning, Reflection
   - 需要确认: true
   - 参数: `taskId` (处理任务ID)

3. **LLM Analyzer**
   - 复用 `AgentToolCallingClient`
   - 三种分析 Prompt（短文件/分块/聚合）

4. **存储层改进**
   - 扩展 `KnowledgeService`
   - `CreativeKnowledgeBaseService.RetrieveAsync` 改用 Qdrant 向量检索

5. **记忆关联**
   - 扩展 `ProjectMemory` 和 `AuthorMemory` 模型
   - Reflection 阶段自动记录使用的知识条目

---

## 数据模型设计

### SQLite 新增表

**knowledge_processing_tasks（处理任务表）**
```sql
CREATE TABLE knowledge_processing_tasks (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT NULL,
    file_name TEXT NOT NULL,
    file_path TEXT NOT NULL,
    file_size INTEGER NOT NULL,
    status TEXT NOT NULL,  -- pending/processing/completed/failed
    strategy TEXT NOT NULL,  -- single_pass/chunked
    progress INTEGER DEFAULT 0,  -- 0-100
    total_chunks INTEGER NULL,
    processed_chunks INTEGER DEFAULT 0,
    extracted_entries_count INTEGER DEFAULT 0,
    error_message TEXT NULL,
    started_at TEXT NULL,
    completed_at TEXT NULL,
    created_at TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IX_knowledge_processing_tasks_user_id ON knowledge_processing_tasks(user_id);
CREATE INDEX IX_knowledge_processing_tasks_status ON knowledge_processing_tasks(status);
```

### 扩展现有 Knowledge 表

```sql
ALTER TABLE knowledge ADD COLUMN source_type TEXT DEFAULT 'manual';  -- manual/extracted
ALTER TABLE knowledge ADD COLUMN source_file_id TEXT NULL;  -- 关联 processing_task.id
ALTER TABLE knowledge ADD COLUMN chunk_index INTEGER NULL;  -- 从第几块提取
ALTER TABLE knowledge ADD COLUMN extraction_context TEXT NULL;  -- 提取时的上下文摘要
```

### 扩展 AgentMemory 表

**ProjectMemory 新增字段**（存储为 JSON）：
```json
{
  "long_term_goal": "...",
  "reader_promise": "...",
  "constraints": [...],
  "unresolved_threads": [...],
  "referenced_knowledge_ids": ["knowledge-id-1", "knowledge-id-2"],  // 新增
  "used_trope_patterns": ["套路1", "套路2"]  // 新增：去重的已用套路
}
```

**AuthorMemory 新增字段**：
```json
{
  "style_likes": [...],
  "style_dislikes": [...],
  "confirmation_tolerance": "...",
  "genre_habits": [...],
  "favorite_knowledge_ids": ["knowledge-id-3", "knowledge-id-4"]  // 新增：常用知识条目
}
```

### Qdrant 向量存储 Payload

**Knowledge 向量 Payload**：
```json
{
  "id": "vec-uuid",
  "vector": [0.1, 0.2, ..., 0.512],
  "payload": {
    "userId": "user-123",
    "projectId": "proj-456",
    "sourceType": "knowledge",
    "sourceId": "knowledge-id",
    "sourceFileId": "task-id",
    "chunkIndex": 0,
    "category": "GenrePrinciple",
    "content": "原文片段...",
    "metadata": {
      "title": "知识条目标题",
      "tags": ["标签1", "标签2"]
    }
  }
}
```

**Memory 向量 Payload**（现有，不改变）：
```json
{
  "id": "memory_user-123_proj-456_project.long_term_goal",
  "vector": [...],
  "payload": {
    "userId": "user-123",
    "projectId": "proj-456",
    "sourceType": "memory",
    "sourceId": "project.long_term_goal",
    "content": "讲述一个少年从普通人成长为...",
    "metadata": {
      "memory_type": "project.long_term_goal"
    }
  }
}
```

**向量化策略**：
- **Memory**: 仅向量化长文本字段（`project.long_term_goal`, `project.reader_promise`）
- **Knowledge**: 全部条目向量化（因为都是长文本创意知识）
- **隔离**: 通过 `sourceType` 字段区分，检索时过滤

---

## 文件处理策略

### 分块阈值
- **短文件**: < 6000 tokens（约 8000 中文字符）
- **长文件**: ≥ 6000 tokens

### 分块参数
- **块大小**: 4000 tokens
- **重叠**: 200 tokens（避免知识点被截断）

### 短文件处理流程

```
1. 读取文件全文
   ↓
2. Token 计数检查
   ↓
3. 单次 LLM 调用（短文件分析 Prompt）
   ↓
4. 解析返回的 JSON 数组
   ↓
5. 批量写入 Knowledge 表
   ↓
6. 批量向量化到 Qdrant
   ↓
7. 更新 Task 状态: completed, progress: 100
```

### 长文件处理流程

```
1. 读取文件全文 → 分块
   ↓
2. 初始化 Task: total_chunks, processed_chunks: 0
   ↓
3. For each chunk:
   ├─ LLM 调用（分块分析 Prompt + 上一块摘要）
   ├─ 解析返回的 entries + summary
   ├─ 暂存 entries 到内存
   ├─ 更新 Task: processed_chunks++, progress 计算
   └─ 保存 summary 供下一块使用
   ↓
4. 聚合阶段:
   ├─ LLM 调用（聚合总结 Prompt + 所有 entries）
   ├─ 去重合并
   └─ 生成全文级知识条目
   ↓
5. 批量写入 Knowledge 表
   ↓
6. 批量向量化到 Qdrant
   ↓
7. 更新 Task 状态: completed, progress: 100
```

---

## LLM 分析 Prompt

### 短文件分析 Prompt

```
你是一个创意写作知识提取专家。分析以下文本，提取可用于小说写作的知识条目。

**文本内容**：
{content}

**提取要求**：
1. **类型原则**(GenrePrinciple): 特定题材的写作规则、读者期待、禁忌
2. **套路警告**(TropePattern): 常见俗套桥段、老梗、容易引起反感的模式
3. **反套路策略**(AntiTropeStrategy): 避免套路的技巧、创新手法
4. **风格示例**(StyleExample): 值得学习的叙述风格、对话技巧、节奏控制

**输出格式**（JSON数组）：
[
  {
    "title": "简短标题（10字内）",
    "category": "GenrePrinciple|TropePattern|AntiTropeStrategy|StyleExample",
    "content": "详细说明（50-200字）",
    "tags": ["标签1", "标签2"],
    "weight": 5,
    "originalText": "原文引用片段（可选）"
  }
]

提取3-10条最有价值的知识条目，确保每条都实用、具体、可操作。
```

### 长文件分块分析 Prompt

```
你是创意写作知识提取专家。这是一个长文档的第 {chunkIndex}/{totalChunks} 块。

**当前块内容**：
{chunkContent}

**上一块摘要**（仅第2块及以后提供）：
{previousSummary}

提取本块中的写作知识条目（格式同上），并生成本块摘要（100字内）用于下一块上下文。

**输出格式**：
{
  "entries": [
    {
      "title": "...",
      "category": "...",
      "content": "...",
      "tags": [...],
      "weight": 5,
      "originalText": "..."
    }
  ],
  "summary": "本块内容摘要，包含关键主题、人物、技巧"
}
```

### 长文件聚合总结 Prompt

```
你是创意写作知识提取专家。已完成分块分析，现在需要跨块聚合。

**各块提取的条目**（JSON数组）：
{allEntries}

**任务**：
1. 识别重复或相似条目，合并去重
2. 提取跨块的共性主题、技巧模式
3. 生成全文级别的高阶知识条目（如整体风格特征、叙事结构规律）

**输出格式**：
{
  "deduplicated": [
    {
      "title": "...",
      "category": "...",
      "content": "...",
      "tags": [...],
      "weight": 5
    }
  ],
  "aggregated": [
    {
      "title": "全文级知识标题",
      "category": "...",
      "content": "跨块归纳的高阶规律",
      "tags": [...],
      "weight": 8
    }
  ]
}
```

---

## Agent 工具设计

### ProcessKnowledgeFile 工具

**注册定义**：
```csharp
Entry("ProcessKnowledgeFile", "knowledge", "Medium", true, 
    new[] { "taskId" }, 
    "处理已上传的知识文件，自动提取创意写作知识条目。支持结构化文档和创意素材。", 
    (call, _, _, _, ct) => ProcessKnowledgeFileAsync(call, ct))
```

**参数**：
- `taskId` (string, required): 处理任务ID（由上传接口返回）

**工具行为**：
1. 验证 Task 存在且属于当前用户
2. 检查 Task 状态（pending → processing）
3. 调用 `KnowledgeProcessingService.ProcessFileAsync(taskId)`
4. 返回处理结果摘要

**可用阶段**: Idle, Planning, Reflection  
**风险等级**: Medium  
**需要确认**: true（提示："将分析文件并提取知识，可能需要1-5分钟，是否继续？"）

---

## 知识库检索改进

### 现有问题
`CreativeKnowledgeBaseService.RetrieveAsync` 使用关键词分词文本匹配：
```csharp
var tokens = Tokenize(query);
var overlap = entryTokens.Intersect(tokens).Count();
var score = overlap * 2.0 + entry.Weight * 0.4;
```

### 改进方案：Qdrant 向量检索

```csharp
public async Task<CreativeKnowledgeRetrievalResult> RetrieveAsync(
    string query,
    StoryCreativeConstitution? constitution = null,
    IEnumerable<string>? usedPlotPatterns = null,
    int topK = 8,
    CancellationToken ct = default)
{
    // 1. 生成查询向量
    var queryVector = await _embedding.EncodeAsync(query, EmbeddingMode.Query, ct);
    
    // 2. Qdrant 向量检索（召回更多候选）
    var vectorResults = await _vectorStore.SearchAsync(
        userId: _currentUser.UserId,
        queryVector: queryVector,
        filter: new { SourceType = "knowledge" },  // 只检索知识库
        topK: topK * 2,
        ct: ct
    );
    
    // 3. 加载记忆进行增强
    var projectMemory = await _memoryRepo.GetProjectMemoryAsync(
        _currentUser.UserId, _currentUser.ProjectId, ct);
    var authorMemory = await _memoryRepo.GetAuthorMemoryAsync(
        _currentUser.UserId, ct);
    
    // 4. 重排序（Boost + 过滤）
    var reranked = vectorResults.Select(r => new
    {
        Result = r,
        Score = CalculateScore(r, constitution, usedPlotPatterns, 
                               projectMemory, authorMemory)
    }).OrderByDescending(x => x.Score).ToList();
    
    // 5. 过滤已用套路
    if (category == "TropePattern")
    {
        reranked = reranked.Where(x => 
            !projectMemory.UsedTropePatterns.Any(p => 
                x.Result.Content.Contains(p, StringComparison.OrdinalIgnoreCase)
            )
        ).ToList();
    }
    
    // 6. 分类返回
    var topResults = reranked.Take(topK).Select(x => x.Result).ToList();
    return BuildResult(topResults, constitution);
}

private double CalculateScore(
    VectorSearchResult result,
    StoryCreativeConstitution? constitution,
    IEnumerable<string>? usedPlotPatterns,
    ProjectMemory projectMemory,
    AuthorMemory authorMemory)
{
    var score = result.Score;  // 向量相似度基础分
    
    // Boost: 项目引用过的知识
    if (projectMemory.ReferencedKnowledgeIds.Contains(result.SourceId))
        score += 2.0;
    
    // Boost: 作者收藏的知识
    if (authorMemory.FavoriteKnowledgeIds.Contains(result.SourceId))
        score += 1.5;
    
    // Boost: 题材匹配
    if (constitution != null && result.Metadata.TryGetValue("category", out var category))
    {
        if (category.ToString() == "GenrePrinciple" && 
            result.Content.Contains(constitution.Genre, StringComparison.OrdinalIgnoreCase))
            score += 3.0;
    }
    
    return score;
}
```

---

## 记忆与知识库关联

### Reflection 阶段自动关联

**触发条件**：
- Agent 在 Planning/Writing 阶段调用 `SearchCreativeKnowledge` 工具
- AgentMemoryUpdate 包含 `UsedKnowledgeIds` 字段

**实现位置**：`AgentCore.ApplyMemoryUpdateAsync`

```csharp
if (memoryUpdate.UsedKnowledgeIds?.Any() == true)
{
    var projectMemory = await _memoryRepo.GetProjectMemoryAsync(userId, projectId, ct);
    
    // 去重添加到引用列表
    var newRefs = memoryUpdate.UsedKnowledgeIds
        .Except(projectMemory.ReferencedKnowledgeIds)
        .ToList();
    
    if (newRefs.Any())
    {
        projectMemory.ReferencedKnowledgeIds.AddRange(newRefs);
        await _memoryRepo.UpdateFieldAsync(
            userId, projectId, 
            "project.referenced_knowledge_ids", 
            projectMemory.ReferencedKnowledgeIds, ct);
    }
}

// 提取使用的套路模式
if (memoryUpdate.UsedTropePatterns?.Any() == true)
{
    var projectMemory = await _memoryRepo.GetProjectMemoryAsync(userId, projectId, ct);
    
    var newTropes = memoryUpdate.UsedTropePatterns
        .Except(projectMemory.UsedTropePatterns)
        .ToList();
    
    if (newTropes.Any())
    {
        projectMemory.UsedTropePatterns.AddRange(newTropes);
        await _memoryRepo.UpdateFieldAsync(
            userId, projectId, 
            "project.used_trope_patterns", 
            projectMemory.UsedTropePatterns, ct);
    }
}
```

### AgentMemoryUpdate 扩展

```csharp
public class AgentMemoryUpdate
{
    // 现有字段
    public List<string> NewConstraints { get; set; } = new();
    public List<string> NewUnresolvedThreads { get; set; } = new();
    public List<string> ResolvedThreads { get; set; } = new();
    
    // 新增字段
    public List<string> UsedKnowledgeIds { get; set; } = new();  // 使用的知识条目ID
    public List<string> UsedTropePatterns { get; set; } = new();  // 使用的套路模式
}
```

### 检索时融合记忆

在 `SearchCreativeKnowledge` 工具调用时，自动融合 ProjectMemory 和 AuthorMemory：
- 已引用知识 → Score +2.0
- 作者收藏知识 → Score +1.5
- 已用套路 → 过滤掉（避免重复）

---

## API 设计

### POST /api/knowledge/upload
**上传文件并创建处理任务**

**请求**：multipart/form-data
- `file`: 文件
- `projectId`: 项目ID（可选）
- `title`: 标题（可选，默认文件名）

**响应**：
```json
{
  "taskId": "task-123",
  "fileName": "创意写作技巧.md",
  "fileSize": 45678,
  "status": "pending",
  "message": "文件已上传，等待处理"
}
```

### POST /api/agent/tool/ProcessKnowledgeFile
**触发 Agent 处理（由 Agent 工具调用）**

**请求**：
```json
{
  "taskId": "task-123"
}
```

**响应**：
```json
{
  "success": true,
  "taskId": "task-123",
  "status": "completed",
  "extractedCount": 12,
  "message": "已提取 12 条知识条目"
}
```

### GET /api/knowledge/tasks/{taskId}
**查询处理进度**

**响应**：
```json
{
  "id": "task-123",
  "fileName": "创意写作技巧.md",
  "status": "processing",
  "strategy": "chunked",
  "progress": 60,
  "totalChunks": 5,
  "processedChunks": 3,
  "extractedEntriesCount": 8,
  "startedAt": "2026-06-12T10:30:00Z"
}
```

### GET /api/knowledge/search
**向量检索知识库（前端直接调用）**

**请求参数**：
- `q`: 查询文本
- `projectId`: 项目ID（可选）
- `category`: 知识类型过滤（可选）
- `topK`: 返回数量（默认8）

**响应**：
```json
{
  "query": "如何避免套路",
  "hits": [
    {
      "id": "knowledge-001",
      "title": "用代价替代巧合",
      "category": "AntiTropeStrategy",
      "content": "遇到机械反转或突然救场时...",
      "tags": ["反套路", "代价"],
      "score": 8.5
    }
  ],
  "genrePrinciples": [...],
  "tropeWarnings": [...],
  "antiTropeStrategies": [...]
}
```

---

## 实现优先级

### P0 - 核心功能（MVP）
1. **数据库迁移**：新增 `knowledge_processing_tasks` 表，扩展 `knowledge` 表
2. **KnowledgeProcessingService**：短文件处理 + 单次 LLM 分析
3. **Agent 工具**：`ProcessKnowledgeFile` 注册
4. **向量检索改进**：`CreativeKnowledgeBaseService.RetrieveAsync` 改用 Qdrant
5. **API 端点**：`/api/knowledge/upload`, `/api/knowledge/tasks/{taskId}`

### P1 - 增强功能
6. **长文件处理**：分块 + 聚合总结
7. **记忆关联**：扩展 ProjectMemory/AuthorMemory，Reflection 自动关联
8. **检索增强**：融合记忆进行 Boost 和过滤

### P2 - 优化功能
9. **前端进度条**：实时显示处理进度
10. **批量处理**：支持一次上传多个文件
11. **知识编辑**：前端界面编辑提取的知识条目

---

## 测试策略

### 单元测试
- `KnowledgeProcessingService.ProcessFileAsync` - 短文件/长文件分支
- `CreativeKnowledgeBaseService.RetrieveAsync` - 向量检索 + 记忆融合
- LLM Prompt 解析逻辑

### 集成测试
- 端到端文件处理流程（上传 → 处理 → 检索）
- Agent 工具调用测试
- 记忆关联测试（Reflection 阶段自动记录）

### 性能测试
- 长文件处理时间（目标：10K 字文档 < 2 分钟）
- 向量检索响应时间（目标：< 500ms）
- 并发处理能力（多用户同时上传）

---

## 风险与缓解

### 风险1：LLM 提取质量不稳定
**缓解措施**：
- Prompt 工程优化（提供示例输出）
- 后处理验证（检查 JSON 格式、必填字段）
- 允许用户手动编辑提取结果

### 风险2：长文件处理时间过长
**缓解措施**：
- 异步后台处理
- 前端进度条反馈
- 设置超时限制（单文件最多 10 分钟）

### 风险3：向量检索召回率不足
**缓解措施**：
- 调整 topK 召回倍数（2-3倍）
- 多策略融合（向量 + 关键词）
- 记忆 Boost 提升相关性

---

## 实施时间表

**总工期：5-7 个工作日**

- Day 1: 数据库迁移 + KnowledgeProcessingService 基础框架
- Day 2: 短文件处理 + LLM 集成
- Day 3: Agent 工具 + API 端点
- Day 4: 向量检索改进 + 长文件处理
- Day 5: 记忆关联 + Reflection 集成
- Day 6-7: 测试 + Bug 修复 + 文档

---

## 成功标准

1. ✅ 用户上传文件后，Agent 能自动提取 3-10 条知识条目
2. ✅ 知识库检索使用 Qdrant 向量检索，召回准确率 > 80%
3. ✅ Reflection 阶段自动记录使用的知识条目到 ProjectMemory
4. ✅ 长文件（>10K 字）处理成功率 > 95%
5. ✅ 端到端处理时间：短文件 < 30s，长文件 < 2min

---

## 相关文档

- [[01 项目架构设计]]
- [[03 记忆系统架构]]
- [[04 数据库设计]]
- [[05 API 设计文档]]
