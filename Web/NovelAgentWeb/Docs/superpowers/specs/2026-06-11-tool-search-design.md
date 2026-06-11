# Tool Search机制设计文档

> **日期**: 2026-06-11  
> **目标**: 为Agent实现Hermes风格的Tool Search机制，让LLM自主判断工作阶段并动态发现可用工具

---

## 1. 设计目标

### 核心理念
**让LLM决定一切** - 移除硬编码的Phase推理逻辑，改为LLM根据对话上下文和任务状态自主判断当前阶段，通过`tool_search`工具动态发现可用工具。

### 关键变化
1. **移除**: `PhaseInference.cs`硬编码的阶段推理
2. **新增**: `tool_search`元工具用于工具发现
3. **优化**: System Prompt教LLM如何判断阶段和使用工具
4. **改进**: 采用"先执行后修正"流程，移除高风险工具的确认机制

---

## 2. 架构设计

### 2.1 核心组件

**新增组件**:
- `tool_search` 元工具 - LLM用来发现可用工具的工具
- `ToolSearchAsync` 处理方法 - 处理tool_search调用，返回工具列表
- Session工具缓存 - 缓存已发现的工具，减少重复search

**移除组件**:
- `PhaseInference.cs` - 删除整个文件
- `AgentRuntime.cs`中的Phase推理调用

**保留组件（用途改变）**:
- `AgentToolRegistry` - Phase分组逻辑保留，仅供tool_search内部使用
- `ConversationPhase`枚举 - 保留作为阶段标识

### 2.2 工作流程

```
用户消息进入Agent主循环
↓
检查Session是否有工具缓存
├─ 有缓存: 直接暴露缓存的工具
└─ 无缓存: 只暴露tool_search工具
↓
LLM分析上下文，判断当前阶段
↓
LLM调用: tool_search(phase="Planning")
↓
System执行并缓存到Session
↓
返回该阶段的工具列表
↓
后续对话直接使用缓存的工具
↓
用户可随时纠正，LLM重新search切换阶段
```

---

## 3. tool_search工具设计

### 3.1 工具定义

**名称**: `tool_search`  
**类型**: meta (元工具)  
**风险**: Low  
**需要确认**: false

**参数**:
- `phase` (必填, string) - 阶段名称

**可选phase值**:
- `Conversation` - 闲聊、问候、状态查询 (2个工具)
- `Planning` - 规划故事地基、卷、章节 (8个工具)
- `Creation` - 生成章节正文、修复草稿 (4个工具)
- `Review` - 提交章节、复盘、刷新索引 (4个工具)
- `All` - 返回全部17个工具

### 3.2 返回格式

```json
{
  "success": true,
  "message": "Planning阶段有 8 个可用工具。\n\n[工具列表...]",
  "phase": "Conversation",
  "data": {
    "phase": "Planning",
    "tools": [
      {
        "name": "QueryProjectStatus",
        "description": "读取 MissionBlackboard、Story Bible...",
        "risk": "Low",
        "requiresConfirmation": false,
        "parameters": {}
      }
    ]
  }
}
```

### 3.3 实现逻辑

```csharp
private Task<AgentToolExecutionResult> ToolSearchAsync(
    AgentToolCall call, 
    AgentSession session, 
    CancellationToken ct)
{
    var phaseArg = Arg(call, "phase").Trim();
    
    if (string.IsNullOrWhiteSpace(phaseArg))
        return ErrorResult("tool_search需要phase参数");
    
    IReadOnlyList<ToolSchema> tools;
    string message;
    
    if (phaseArg.Equals("All", StringComparison.OrdinalIgnoreCase))
    {
        tools = ListToolSchemas();
        message = $"返回全部 {tools.Count} 个工具。";
    }
    else if (Enum.TryParse<ConversationPhase>(phaseArg, true, out var phase))
    {
        tools = ListToolSchemasForPhase(phase);
        message = $"{phaseArg}阶段有 {tools.Count} 个可用工具。";
    }
    else
    {
        return ErrorResult($"未知阶段：{phaseArg}");
    }
    
    // 缓存到Session
    session.DiscoveredPhase = phaseArg;
    session.DiscoveredTools = tools.ToList();
    session.LastToolSearchAt = DateTime.UtcNow;
    
    return SuccessResult(message, tools);
}
```

---

## 4. Session缓存机制

### 4.1 Session模型扩展

```csharp
public class AgentSession
{
    // 当前发现的phase（最近一次tool_search的结果）
    public string? DiscoveredPhase { get; set; }
    
    // 该phase的工具缓存
    public List<ToolSchema> DiscoveredTools { get; set; } = new();
    
    // 上次tool_search的时间戳
    public DateTime? LastToolSearchAt { get; set; }
}
```

### 4.2 缓存使用逻辑

```csharp
// AgentCore中决定暴露哪些工具
IReadOnlyList<ToolSchema> availableTools;

if (!string.IsNullOrWhiteSpace(session.DiscoveredPhase) && 
    session.DiscoveredTools.Count > 0)
{
    // 有缓存，直接暴露上次search到的工具
    availableTools = session.DiscoveredTools;
}
else
{
    // 无缓存，只暴露tool_search
    availableTools = new List<ToolSchema> { GetToolSearchSchema() };
}
```

### 4.3 缓存清理策略

**触发时机**:
- 新会话创建时自动清空
- 用户明确说"重新开始"
- 关键状态变化（可选，如CommitValidatedChapter完成）

---

## 5. System Prompt优化

### 5.1 新增内容

```
## 工具发现机制
你通过 tool_search 工具来发现当前可用的工具。

**工作流程**：
1. 分析用户意图和当前任务状态
2. 判断处于哪个工作阶段：
   - Conversation: 闲聊、问候、状态查询
   - Planning: 规划故事地基/卷/章节
   - Creation: 生成章节正文
   - Review: 提交章节、复盘
   - All: 不确定时查看全部工具
3. 调用 tool_search(phase="Planning") 获取该阶段可用工具
4. 从返回的工具中选择合适的工具执行

**阶段判断原则**：
- 用户问候/提问/查询状态 → Conversation
- 用户说"创建新小说"/"规划故事"/"下一章" → Planning
- 已有章节规划，需要生成正文 → Creation
- 草稿已生成，需要提交或复盘 → Review
- 根据 MissionBlackboard 的任务状态判断阶段
- 不确定时先用 tool_search(phase="All") 查看全部工具

**重要**：每次需要调用工具前，先判断阶段并使用 tool_search。
```

### 5.2 任务执行原则

```
## 任务执行原则
采用'先执行后修正'模式，不要频繁请求用户确认：
1. 理解用户意图后，使用tool_search找到工具，直接执行
2. 执行后告知用户结果和下一步计划
3. 如果用户不满意，会主动告诉你如何调整
4. 工作流自带校验机制（如ValidateChapterDraft），发现问题自动修复

**不要问**："我现在要调用XX工具，可以吗？"
**应该做**：调用工具 → 展示结果 → "已完成XX，现在进行YY..."
```

---

## 6. 高风险工具确认机制移除

### 6.1 需要修改的工具

以下工具的`RequiresConfirmation`从`true`改为`false`:

```csharp
Entry("CommitStoryFoundation", ..., false, ...)  // High risk但不需确认
Entry("CommitVolumeArc", ..., false, ...)
Entry("GenerateChapterWithChanges", ..., false, ...)
Entry("RepairChapterDraft", ..., false, ...)
Entry("CommitValidatedChapter", ..., false, ...)
```

### 6.2 理由

1. 工作流自带校验机制（ValidateChapterDraft）
2. 所有操作都有对应的修正工具（RepairChapterDraft）
3. "先执行后修正"比"执行前确认"体验更好
4. 用户可以随时通过对话纠正LLM的决策

---

## 7. 完整交互流程示例

### 场景：用户要求生成第一章

```
=== 第1轮对话 ===
用户: "帮我写第一章"

Session检查: DiscoveredPhase=null (无缓存)
暴露工具: [tool_search]

LLM思考: 需要规划章节，应该在Planning阶段
LLM调用: tool_search(phase="Planning")

System执行:
- 返回Planning阶段8个工具
- 缓存: session.DiscoveredPhase = "Planning"
- 缓存: session.DiscoveredTools = [8个工具]

LLM调用: PlanChapter(creativeBrief="根据故事地基生成第一章候选", chapterId="chapter-001")
System返回: {3个候选}

LLM回复: "已生成3个第一章候选，推荐第1个，现在开始生成正文。"

=== 第2轮对话 ===
用户: "好的，继续"

Session检查: DiscoveredPhase="Planning" (有缓存)
暴露工具: [Planning阶段8个工具]

LLM调用: SelectChapterCandidate(...)
LLM调用: BuildChapterContextPackage(...)
LLM调用: GenerateChapterWithChanges(...) ✅ 直接执行，不需确认

System返回: {章节草稿已生成}
LLM回复: "第一章正文已生成，4200字。现在进行质量校验..."

=== 第3轮对话（用户干预）===
用户: "等等，这个开局太平淡了，换第3个候选"

Session检查: DiscoveredPhase="Planning" (有缓存)
暴露工具: [Planning阶段8个工具]

LLM调用: SelectChapterCandidate(candidateTitles="第3个候选标题")
LLM回复: "好的，已切换到第3个候选，重新生成..."
```

---

## 8. 实施步骤

### 8.1 文件修改清单

**删除**:
- `Web/NovelAgentWeb/Support/PhaseInference.cs`

**修改**:
- `Web/NovelAgentWeb/Support/AgentToolRegistry.cs`
  - 添加tool_search工具定义
  - 添加ToolSearchAsync方法
  - 修改高风险工具的RequiresConfirmation为false

- `Web/NovelAgentWeb/Support/AgentCore.cs`
  - 修改BuildActionSystemPrompt，添加工具发现机制说明
  - 添加任务执行原则说明
  - 修改工具暴露逻辑（优先使用缓存）

- `Web/NovelAgentWeb/Support/AgentRuntime.cs`
  - 删除PhaseInference实例化
  - 删除InferPhase调用

- `Web/NovelAgentWeb/Models/AgentSession.cs`
  - 添加DiscoveredPhase属性
  - 添加DiscoveredTools属性
  - 添加LastToolSearchAt属性

### 8.2 测试场景

1. **首次对话** - 验证只暴露tool_search
2. **阶段切换** - 验证tool_search正确返回各阶段工具
3. **缓存使用** - 验证后续对话使用缓存工具
4. **用户纠正** - 验证LLM能根据用户反馈重新search
5. **工作流连贯性** - 验证"先执行后修正"流程

---

## 9. 预期效果

### 9.1 优势

1. **LLM完全自主** - 不依赖硬编码关键词判断阶段
2. **上下文噪声最小** - 首次只看到1个工具
3. **可扩展性强** - 未来工具数量增加不影响context
4. **用户体验好** - "先执行后修正"比"执行前确认"流畅
5. **逻辑清晰** - 搜索→缓存→使用 流程易理解和调试

### 9.2 性能考虑

- **首次对话**: 需要1次额外的tool_search调用（增加1轮LLM）
- **后续对话**: 直接使用缓存，无额外开销
- **阶段切换**: 仅在用户明确要求时才重新search

### 9.3 风险与缓解

**风险1**: LLM可能判断错阶段
- **缓解**: 支持phase="All"兜底，system prompt提供判断原则

**风险2**: 缓存可能导致工具过时
- **缓解**: 关键状态变化时清空缓存（可选）

**风险3**: 移除确认机制可能误操作
- **缓解**: 工作流自带校验和修复机制，用户可随时纠正

---

## 10. 未来扩展

### 10.1 模糊搜索支持

当工具数量增加到50+时，可添加`intent`参数：

```csharp
tool_search(phase="All", intent="我想检查章节质量")
// 返回：ValidateChapterDraft, ReviewChapter, QueryProjectStatus
```

实现方式：
- 工具description做embedding
- Intent做语义搜索
- 返回topK相关工具

### 10.2 工具推荐系统

基于历史使用记录，推荐最可能需要的工具：

```json
{
  "recommendedTools": ["PlanChapter", "QueryProjectStatus"],
  "allTools": [...]
}
```

### 10.3 工具依赖链

自动检测工具调用的前置依赖：

```
PlanChapter → SelectChapterCandidate → BuildChapterContextPackage → GenerateChapterWithChanges
```

如果LLM跳过某步，system自动提示或补全。

---

**设计完成，等待审阅。**
