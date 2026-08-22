# Agent 工具去重机制

## 问题背景

在 Agent Loop 中，LLM 有时会因为规划失误或遇到困难而反复尝试同一个工具，导致：
- 陷入死循环（同样的参数重复调用）
- 浪费计算资源（重型工具如 `ProduceChapter` 消耗大量 Token）
- 产生副作用重复（数据重复插入、事件重复发送）

## 去重策略

### 1. **strict（严格去重）** - 默认策略

**规则**: 同名 + 同参数禁止重复调用

**指纹计算**:
```csharp
// name::arg1=val1&arg2=val2
BuildToolCallFingerprint(toolCall)
```

**适用场景**: 
- 写操作工具（`ProduceChapter`, `CommitStoryFoundation`, `AttachKnowledgeToProject`）
- 有副作用的工具（插入数据库、生成文件、发送事件）

**示例**:
```
Step 1: ProduceChapter(chapterId=ch-001) → 成功
Step 2: ProduceChapter(chapterId=ch-001) → 🚫 被去重拦截
Step 3: ProduceChapter(chapterId=ch-002) → ✅ 允许（参数不同）
```

---

### 2. **per_turn（按轮次去重）**

**规则**: 只检查工具名，允许同名不同参数

**指纹计算**:
```csharp
// 只用工具名
toolCall.Name
```

**适用场景**:
- 检索类工具（`SearchCreativeKnowledge`）
- 批量操作工具（需要多次调用不同参数）
- 幂等只读工具

**示例**:
```
Step 1: SearchCreativeKnowledge(query="主角心理") → 成功
Step 2: SearchCreativeKnowledge(query="冲突设计") → ✅ 允许（per_turn 策略）
Step 3: SearchCreativeKnowledge(query="主角心理") → 🚫 仍然被拦截（per_turn 只允许不同参数）
```

**注意**: `per_turn` 不是"完全不去重"，而是只检查工具名：
- 首次调用 `SearchCreativeKnowledge("A")` → ✅
- 第二次调用 `SearchCreativeKnowledge("B")` → ✅
- 第三次调用 `SearchCreativeKnowledge("A")` → ✅（参数不同，指纹不同）
- 第三次调用 `SearchCreativeKnowledge` **任意参数** → 🚫（工具名已用过，per_turn 只记录名字）

**实际效果**: `per_turn` = 允许一次多参数调用，但不允许同工具名在同一轮内反复出现。

---

### 3. **none（不去重）** - 谨慎使用

**规则**: 完全不检查，允许任意重复调用

**适用场景**:
- 完全幂等的只读工具
- 工具本身有内部去重机制
- 测试/调试场景

**风险**: 可能导致死循环，仅在确认安全时使用。

---

## 实现细节

### 数据模型扩展

**`AgentToolSemanticSpec`** (AgentCore.cs:743):
```csharp
public string DeduplicationPolicy { get; set; } = "strict";
```

### 去重逻辑 (AgentRuntime.cs:548)

```csharp
var toolDefinition = _toolRegistry.Find(action.ToolCall.Name);
var dedupPolicy = toolDefinition?.Semantic.DeduplicationPolicy ?? "strict";

if (dedupPolicy != "none")
{
    var fingerprint = dedupPolicy == "strict"
        ? BuildToolCallFingerprint(action.ToolCall)  // name::arg1=val1&arg2=val2
        : action.ToolCall.Name;  // per_turn: only check name

    if (!executedCalls.Add(fingerprint))
    {
        var message = dedupPolicy == "strict"
            ? "本轮已有同名同参数工具结果，请基于已有观察继续。"
            : "本轮已调用过该工具，请尝试其他工具或反思当前状态。";
        // 返回治理拦截结果...
    }
}
```

### 工具注册配置 (AgentToolRegistry.cs)

**默认策略**:
```csharp
DeduplicationPolicy = dedupPolicy ?? (sideEffects.BusinessReadOnly ? "per_turn" : "strict")
```

**显式配置**:
```csharp
Entry("SearchCreativeKnowledge", "rag", "Low", false, 
    new[] { "query" }, 
    "检索创意知识库...", 
    Effects(...), 
    handler,
    dedupPolicy: "per_turn")  // 显式指定策略
```

---

## 已配置工具

### per_turn 策略工具

| 工具名 | 原因 |
|--------|------|
| `SearchCreativeKnowledge` | 需要多次搜索不同关键词 |
| `AttachKnowledgeToProject` | 需要批量绑定多个知识条目 |
| 所有 `BusinessReadOnly=true` 的工具 | 只读工具默认 per_turn |

### strict 策略工具（默认）

所有写操作工具，包括：
- `ProduceChapter`
- `CommitStoryFoundation`
- `CreateChapterBlueprint`
- `ClassifyProjectKnowledge`
- 等等...

---

## 最佳实践

### 1. 工具设计原则

**✅ 推荐：批量参数设计**
```csharp
AttachKnowledgeBatch(knowledgeIds: ["k1", "k2", "k3"])
```

**❌ 避免：依赖多次调用**
```csharp
AttachKnowledge(knowledgeId: "k1")
AttachKnowledge(knowledgeId: "k2")
AttachKnowledge(knowledgeId: "k3")
```

### 2. 选择去重策略

| 工具特征 | 推荐策略 |
|----------|----------|
| 有副作用（写DB/发事件） | `strict` |
| 幂等只读 | `per_turn` |
| 检索类/多参数调用 | `per_turn` |
| 完全内部去重 | `none`（谨慎） |

### 3. LLM 提示增强

在系统提示中告知 LLM 去重机制：
```
- 每个工具在同一轮次只能以相同参数调用一次
- 检索类工具（SearchCreativeKnowledge）可以多次调用不同参数
- 如果工具被拦截，请反思当前状态或尝试其他工具
```

---

## 性能影响

- **内存开销**: 每轮最多记录 `maxSteps` (3-20) 个工具指纹，可忽略
- **CPU开销**: 哈希计算和集合查找 O(1)，可忽略
- **Token 节省**: 避免重复调用重型工具，节省显著

---

## 未来优化方向

1. **时间窗口去重**: 跨轮次记录工具调用，防止短时间内反复尝试
2. **工具依赖分析**: 基于 `ReadsFrom`/`WritesTo` 自动推断去重策略
3. **LLM 反馈学习**: 记录被拦截的工具调用，训练 LLM 规避重复
4. **用户可配置**: 允许在 `user_settings.json` 中覆盖去重策略

---

**实施日期**: 2026-06-25  
**实施者**: Claude Opus 4.7  
**相关提交**: 待提交
