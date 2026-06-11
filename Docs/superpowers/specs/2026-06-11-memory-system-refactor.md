# 记忆系统架构重构设计文档

**日期:** 2026-06-11  
**目标:** 修复四层记忆隔离问题，迁移到 SQLite + IMemoryCache + Qdrant 三层存储架构

---

## 1. 问题诊断

### 1.1 当前架构缺陷

**四层记忆隔离问题:**
- ChatHistory 无自动提取机制，记忆层不会从对话中学习
- SessionMemory/ProjectMemory/AuthorMemory/ExecutionMemory 互相独立，无关联

**存储架构问题:**
- ProjectMemory/AuthorMemory/ExecutionMemory 存储在文件系统 JSON
- AuthorMemory 使用全局路径，多用户会冲突
- 无缓存，每次会话都读取 4 个 JSON 文件
- ChatHistory 无限增长，导致 SessionData 字段膨胀

**性能问题:**
- 每次会话开始：1 次数据库查询 + 4 次文件 I/O
- 无分布式缓存支持（仅进程内 IMemoryCache）

---

## 2. 设计目标

### 2.1 核心目标

1. **自动记忆提取** - Reflection 阶段从 ChatHistory 提取信息更新四层记忆
2. **三层存储架构** - SQLite（持久化） + IMemoryCache（热缓存） + Qdrant（语义检索）
3. **多用户隔离** - AuthorMemory 按 UserId 隔离，ProjectMemory 按 ProjectId 隔离
4. **ChatHistory 压缩** - 分层摘要（10轮→Summary, 30轮→MetaSummary）+ 关键信息提取
5. **原子更新** - 细粒度字段独立行，避免并发冲突

### 2.2 非目标

- ❌ 引入 Redis 分布式缓存（保持 IMemoryCache 简化部署）
- ❌ 修改前端 API（仅后端架构重构）
- ❌ 向量化所有记忆字段（仅 LongTermGoal/ReaderPromise 需要语义检索）

---

## 3. 存储架构设计

### 3.1 三层存储职责

#### **Layer 1: SQLite（持久化存储）**

**AgentSession 表（保持不变）:**
```sql
CREATE TABLE agent_sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT,
    title TEXT DEFAULT '新会话',
    is_archived INTEGER DEFAULT 0,
    session_data TEXT,  -- JSON: ChatHistory + WorkingMemory
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
);
```

**AgentMemory 表（细粒度字段）:**
```sql
CREATE TABLE agent_memories (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT,  -- NULL = 跨项目（AuthorMemory）
    memory_type TEXT NOT NULL,  -- "{scope}.{field}" 格式
    content TEXT NOT NULL,  -- JSON 序列化的值
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

CREATE INDEX idx_memories_user_project ON agent_memories(user_id, project_id);
CREATE INDEX idx_memories_type ON agent_memories(user_id, memory_type);
```

**MemoryType 命名规范:**
- `"project.long_term_goal"` - ProjectMemory.LongTermGoal
- `"project.reader_promise"` - ProjectMemory.ReaderPromise
- `"project.constraints"` - ProjectMemory.Constraints（JSON 数组）
- `"project.unresolved_threads"` - ProjectMemory.UnresolvedThreads（JSON 数组）
- `"author.style_likes"` - AuthorMemory.StyleLikes（JSON 数组）
- `"author.style_dislikes"` - AuthorMemory.StyleDislikes（JSON 数组）
- `"author.confirmation_tolerance"` - AuthorMemory.ConfirmationTolerance
- `"author.genre_habits"` - AuthorMemory.GenreHabits（JSON 数组）
- `"execution.tool_failures"` - ExecutionMemory.ToolFailurePatterns（JSON 数组）
- `"execution.repeated_blockers"` - ExecutionMemory.RepeatedBlockers（JSON 数组）
- `"execution.successful_repairs"` - ExecutionMemory.SuccessfulRepairNotes（JSON 数组）

#### **Layer 2: IMemoryCache（热数据缓存）**

**缓存策略:**
```
Key 格式: "memory:{scope}:{userId}:{projectId?}"
示例:
  - "memory:author:user123"
  - "memory:project:user123:proj456"
  - "memory:execution:user123:proj456"
  - "memory:session:sessionId"

TTL: 5 分钟
驱逐策略: LRU
```

**缓存失效时机:**
- 记忆更新后立即失效对应 Key
- SessionMemory 在请求结束后失效（避免跨请求污染）

#### **Layer 3: Qdrant（语义检索）**

**仅向量化字段:**
- `ProjectMemory.LongTermGoal` (50-200字) - 跨项目找相似目标
- `ProjectMemory.ReaderPromise` (100-300字) - 找相似类型作品

**Qdrant Payload:**
```json
{
  "id": "memory_proj123_long_term_goal",
  "vector": [0.123, ...],
  "user_id": "user123",
  "project_id": "proj123",
  "source_type": "memory",
  "memory_type": "project.long_term_goal",
  "content": "构建一个修仙世界，主角从凡人逆袭成仙帝"
}
```

**检索场景:**
- 用户创建新项目时，推荐相似的历史项目
- Agent 学习用户过往作品风格

---

### 3.2 数据流设计

#### **写入流程（每次 Agent 响应）**

```
┌─────────────────┐
│ User Message    │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ 1. Load SessionMemory (Cache优先)  │
│    Cache Hit → 返回                 │
│    Cache Miss → SQLite查询 → 缓存  │
└────────┬────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ 2. Agent Processing & Response      │
└────────┬────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────┐
│ 3. Memory Update (根据触发器类型)                   │
│                                                     │
│ Lightweight (每次响应):                             │
│   ✓ 更新 SessionMemory.ChatSummary                 │
│   ✓ ChatHistory 分层摘要（5轮→完整，10轮→Summary） │
│                                                     │
│ Standard (3-5轮对话):                               │
│   ✓ 提取 ShortTermPreferences                      │
│   ✓ 生成 10轮 Summary                              │
│                                                     │
│ ToolCall (工具调用):                                │
│   ✓ 更新 ExecutionMemory.ToolFailures/Repairs     │
│                                                     │
│ ChapterWrite (章节生成):                            │
│   ✓ 更新 ProjectMemory.UnresolvedThreads          │
│                                                     │
│ SessionEnd (会话结束):                              │
│   ✓ 完整提取所有四层记忆                           │
│   ✓ 生成 MetaSummary（30轮以上）                   │
└────────┬────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────┐
│ 4. Persist & Cache Invalidation          │
│   • SessionMemory → AgentSession.SessionData │
│   • Others → AgentMemory 表 (细粒度行)      │
│   • LongTermGoal/ReaderPromise → Qdrant    │
│   • 缓存失效                               │
└────────────────────────────────────────────┘
```

#### **读取流程（会话开始/恢复）**

```
┌──────────────────┐
│ Load Memory      │
└────────┬─────────┘
         │
         ▼
┌──────────────────────────────┐
│ 1. Check IMemoryCache        │
│    Key: "memory:author:uid"  │
└────────┬─────────────────────┘
         │
    Cache Hit? ──Yes──> Return
         │
        No
         │
         ▼
┌────────────────────────────────────────────┐
│ 2. Query SQLite                            │
│    SELECT * FROM agent_memories            │
│    WHERE user_id = @uid                    │
│      AND project_id IS NULL                │
│      AND memory_type LIKE 'author.%'       │
└────────┬───────────────────────────────────┘
         │
         ▼
┌────────────────────────────────┐
│ 3. Assemble Memory Object      │
│    AuthorMemory {              │
│      StyleLikes = GetField(),  │
│      StyleDislikes = GetField()│
│    }                           │
└────────┬───────────────────────┘
         │
         ▼
┌────────────────────────────────┐
│ 4. Set Cache (TTL 5 min)       │
└────────────────────────────────┘
```

---

## 4. 核心组件设计

### 4.1 IAgentMemoryRepository 接口

**职责:** 封装 AgentMemory 表的细粒度字段访问

```csharp
public interface IAgentMemoryRepository
{
    // ===== 读取完整记忆对象 =====
    Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default);
    Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default);
    Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default);
    
    // ===== 原子更新单个字段 =====
    Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default);
    
    // ===== 批量更新（事务） =====
    Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default);
    
    // ===== 内部辅助方法 =====
    // GetField<T>(rows, memoryType) - 从行集合中提取字段
    // UpsertField(userId, projectId, memoryType, value) - 插入或更新单行
}
```

**实现要点:**
- 使用 `IMemoryCache` 缓存完整对象（5分钟 TTL）
- 读取时一次性查询所有相关行：`WHERE memory_type LIKE 'author.%'`
- 更新时只操作单行，避免全对象读取-修改-写回

### 4.2 记忆提取触发器

**枚举定义:**
```csharp
public enum MemoryUpdateTrigger
{
    Lightweight,    // 每次响应 - 更新 ChatSummary
    Standard,       // 3-5轮对话 - 提取 Preferences
    ToolCall,       // 工具调用 - 更新 ExecutionMemory
    ChapterWrite,   // 章节生成 - 更新 ProjectMemory
    SessionEnd      // 会话结束 - 完整提取
}
```

**触发检测逻辑（AgentRuntime）:**
```csharp
private MemoryUpdateTrigger DetermineUpdateTrigger(AgentSession session)
{
    var turnCount = session.ChatHistory.Count / 2;  // 用户消息数
    var hasToolCall = session.LastDecision?.Mode == "tool_calling";
    var isChapterWrite = hasToolCall && session.PendingToolCall?.Name == "WriteChapter";
    
    if (isChapterWrite) return MemoryUpdateTrigger.ChapterWrite;
    if (hasToolCall) return MemoryUpdateTrigger.ToolCall;
    if (turnCount % 5 == 0) return MemoryUpdateTrigger.Standard;
    return MemoryUpdateTrigger.Lightweight;
}
```

### 4.3 ChatHistory 分层摘要

**数据结构:**
```csharp
public class LayeredChatHistory
{
    public string? MetaSummary { get; set; }  // 前 30+ 轮总结
    public List<ChatSummary> Summaries { get; set; } = new();  // 每 10 轮一个
    public List<ChatMessage> RecentMessages { get; set; } = new();  // 最近 5 轮
}

public class ChatSummary
{
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public string Content { get; set; } = string.Empty;  // LLM 生成的摘要
    public List<string> KeyDecisions { get; set; } = new();  // 关键决策点
    public DateTime CreatedAt { get; set; }
}
```

**压缩策略:**
```
Turn 1-5:   完整保留
Turn 6-10:  完整保留
Turn 11-15: 生成 Summary1(1-10轮) + 保留 11-15 轮
Turn 16-20: Summary1 + 保留 16-20 轮
Turn 21-30: Summary1 + Summary2(11-20轮) + 保留 21-30 轮
Turn 31+:   生成 MetaSummary(1-30轮) + Summary3(21-30) + 保留 31-35 轮
```

**LLM Prompt（生成 Summary）:**
```
你需要总结以下 10 轮对话的核心内容：
[对话内容...]

输出格式：
{
  "content": "用户在这 10 轮中确认了角色设定，讨论了情节大纲...",
  "keyDecisions": [
    "确认主角为第三人称视角",
    "决定第二章从主角失忆开始"
  ]
}
```

---

## 5. Reflection Prompt 扩展

### 5.1 当前 Reflection 输出（保持）

```json
{
  "summary": "本轮对话完成了章节大纲构思",
  "qualityGate": {
    "status": "pass",
    "issues": []
  },
  "missionPatch": {
    "chapterPatches": []
  }
}
```

### 5.2 新增 memoryUpdate 字段

**完整输出示例:**
```json
{
  "summary": "...",
  "qualityGate": {...},
  "missionPatch": {...},
  "memoryUpdate": {
    "sessionMemory": {
      "chatSummary": "用户确认第三人称视角，正在构思第二章大纲，要求避免过度修饰",
      "extractedPreferences": [
        "保持节奏紧凑",
        "避免冗长环境描写"
      ]
    },
    "projectMemory": {
      "newConstraints": ["不要出现现代科技元素"],
      "unresolvedThreads": ["主角身世之谜（第15章揭示）"]
    },
    "authorMemory": {
      "styleLikes": [],
      "styleDislikes": ["过于啰嗦的描写", "重复的情绪渲染"]
    },
    "executionMemory": {
      "toolSuccess": "WriteChapter 成功，质量门禁通过",
      "toolFailure": null
    }
  }
}
```

**Prompt 指令（追加到 BuildReflectSystemPrompt）:**
```
# 记忆提取指令

在每次 Reflection 时，你需要从对话历史中提取关键信息更新四层记忆：

## memoryUpdate.sessionMemory（会话短期记忆）
- chatSummary: 本轮对话的核心内容（50-100字）
- extractedPreferences: 用户表达的偏好（如"避免..."、"更喜欢..."）

## memoryUpdate.projectMemory（项目约束记忆）
- newConstraints: 新识别的写作约束（如"不要出现XXX"）
- unresolvedThreads: 新增的伏笔线索（格式："线索名（计划揭示章节）"）

## memoryUpdate.authorMemory（作者风格记忆）
- styleLikes: 用户明确喜欢的写作风格
- styleDislikes: 用户明确反感的风格（如"过于XX"）

## memoryUpdate.executionMemory（工具执行记忆）
- toolSuccess: 工具成功经验（仅在有工具调用时填写）
- toolFailure: 工具失败原因（仅在失败时填写）

注意：
1. 如果某个字段本轮无更新，返回空数组 [] 或 null
2. extractedPreferences 只提取明确的偏好，不要臆测
3. 风格类的信息放到 authorMemory，约束类的放到 projectMemory
```

---

## 6. 实施计划

### 6.1 实施顺序（大爆炸重构）

**Day 1-2: Repository 层 + 缓存**
1. 实现 `IAgentMemoryRepository` 接口
2. 实现 `AgentMemoryRepository` + IMemoryCache 集成
3. 单元测试：读取/更新/批量操作

**Day 2-3: AgentMemoryService 重构**
1. 删除文件系统读写逻辑
2. 替换为 `IAgentMemoryRepository` 调用
3. 保持接口签名不变（对上层透明）

**Day 3-4: Reflection 记忆提取**
1. 扩展 `BuildReflectSystemPrompt` 添加记忆提取指令
2. 实现 `ApplyMemoryUpdate` 方法
3. 触发器检测逻辑（Lightweight/Standard/ToolCall/ChapterWrite/SessionEnd）

**Day 4-5: ChatHistory 分层摘要**
1. 实现 `LayeredChatHistory` 数据结构
2. 实现压缩逻辑（10轮→Summary, 30轮→MetaSummary）
3. 集成到 `AgentRuntime`

**Day 5: Qdrant 集成**
1. LongTermGoal/ReaderPromise 双写 Qdrant
2. 实现跨项目语义检索接口

**Day 5: 数据迁移**
1. 实现迁移脚本：文件系统 JSON → SQLite
2. 备份原始文件
3. 执行迁移（测试环境验证）

### 6.2 回滚方案

**迁移失败处理:**
1. 保留原始 JSON 文件 7 天
2. 数据库插入失败时记录日志
3. 提供回退脚本：清空 AgentMemory 表 + 恢复文件读取

**线上灰度:**
1. 功能开关：`MemorySystem:UseNewArchitecture`（默认 true）
2. 检测到数据异常时自动降级到文件系统
3. 监控关键指标：记忆加载耗时、缓存命中率

---

## 7. 验收标准

### 7.1 功能验收

- ✅ 多用户隔离：AuthorMemory 按 UserId 独立
- ✅ 记忆自动提取：每轮对话更新 ChatSummary
- ✅ 分层摘要：30 轮对话后生成 MetaSummary
- ✅ 工具调用记忆：WriteChapter 成功后更新 ExecutionMemory
- ✅ 跨项目检索：根据 LongTermGoal 推荐相似项目

### 7.2 性能验收

- ✅ 会话启动耗时：< 200ms（缓存命中）
- ✅ 缓存命中率：> 80%（5 分钟 TTL）
- ✅ SessionData 大小：< 500KB（30 轮对话后）
- ✅ 记忆更新延迟：< 100ms（原子字段更新）

### 7.3 数据迁移验证

- ✅ 所有历史 AuthorMemory 文件成功导入
- ✅ ProjectMemory 字段完整性检查（无数据丢失）
- ✅ 迁移后用户可正常加载历史会话

---

## 8. 风险评估

### 8.1 技术风险

| 风险 | 概率 | 影响 | 缓解措施 |
|-----|------|------|---------|
| 数据迁移失败 | 中 | 高 | 保留备份 7 天，提供回滚脚本 |
| 并发更新冲突 | 低 | 中 | 细粒度字段行隔离，降低冲突面 |
| Reflection 提取不准确 | 中 | 低 | 人工审核前 100 条，优化 prompt |
| 缓存一致性问题 | 低 | 中 | 更新后立即失效缓存 |

### 8.2 业务风险

| 风险 | 概率 | 影响 | 缓解措施 |
|-----|------|------|---------|
| 历史会话加载失败 | 低 | 高 | 迁移脚本充分测试，灰度发布 |
| 用户感知性能下降 | 低 | 中 | 缓存预热，监控响应时间 |
| 记忆提取误导 Agent | 中 | 低 | 提供用户手动编辑记忆接口 |

---

## 9. 后续优化

### 9.1 Phase 2（未来迭代）

- 用户手动编辑记忆界面
- 记忆版本历史追踪
- 跨用户记忆聚合（团队协作场景）
- MetaSummary 的语义向量化（超长会话检索）

### 9.2 技术债清理

- 删除旧的文件系统代码路径
- 统一缓存 Key 命名规范
- 补充集成测试覆盖率

---

**文档版本:** v1.0  
**最后更新:** 2026-06-11  
**审核状态:** 待审核
