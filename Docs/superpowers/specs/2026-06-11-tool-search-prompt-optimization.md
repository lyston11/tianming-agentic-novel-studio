# Tool Search System Prompt 优化设计

> **日期**: 2026-06-11
> **目标**: 优化 Agent System Prompt 中的工具发现机制指导，让 LLM 能够根据任务需求自主判断何时调用 tool_search

---

## 1. 背景

### 现有实现

天命 AI 写作 Agent 已经实现了 Hermes 风格的工具发现机制：

1. **tool_search 元工具**（AgentToolRegistry.cs 第875-931行）
   - 输入：phase 参数（Conversation/Planning/Creation/Review/All）
   - 输出：该阶段可用的工具列表
   - 结果缓存到 Session（DiscoveredPhase, DiscoveredTools, LastToolSearchAt）

2. **动态工具暴露**（AgentObservationBuilder.cs 第827-855行）
   - 如果 Session 有缓存：暴露缓存的工具列表
   - 如果没有缓存：只暴露 tool_search

3. **System Prompt 指导**（AgentCore.cs 第1224-1243行）
   - 告诉 LLM 如何使用 tool_search
   - 说明各阶段的含义
   - 指导何时使用缓存

### 问题

当前 System Prompt（第1243行）的指导存在逻辑错误：

```
**重要**：如果当前会话已经发现了工具（有工具缓存），直接使用缓存的工具。只有在需要切换阶段时才重新调用 tool_search。
```

**问题分析**：
- ❌ "直接使用缓存的工具" - 过于僵化，不让 LLM 思考
- ❌ "只有在需要切换阶段时" - LLM 可能不知道何时算"切换阶段"
- ❌ 导致 LLM 看到有缓存就不思考，可能用错工具

**正确的设计意图**：
- ✅ LLM 应该根据当前任务判断需要什么工具
- ✅ 如果缓存中有需要的工具 → 直接用
- ✅ 如果缓存中没有需要的工具 → 调用 tool_search
- ✅ LLM 自己决策，不是硬性规则

---

## 2. 设计方案

### 核心原则

**从"规则驱动"改为"需求驱动"**：
- 不告诉 LLM "必须在什么时候调用 tool_search"
- 让 LLM 根据任务需求，自然地判断是否需要新工具
- 工具缓存是辅助，不是限制

### 修改内容

#### 修改 1: 工具发现机制整体重写

**位置**: AgentCore.cs `BuildActionSystemPrompt` 方法，第1224-1243行

**当前内容**：
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

**重要**：如果当前会话已经发现了工具（有工具缓存），直接使用缓存的工具。只有在需要切换阶段时才重新调用 tool_search。
```

**修改为**：
```
## 工具发现机制

你通过 tool_search 工具来动态发现可用工具。

**基本原则**：
1. 根据用户意图和当前任务，判断需要什么工具
2. 检查已缓存的工具是否满足需求
3. 如果缓存不满足，调用 tool_search(phase="阶段名") 获取该阶段的工具
4. 工具缓存是上次 tool_search 的结果。如果缓存中的工具能满足需求，直接使用；如果缓存中没有你需要的工具，先调用 tool_search 发现新工具。

**阶段说明**（供参考，不是硬性规则）：
- Conversation: 闲聊、问候、状态查询
- Planning: 规划故事地基、卷、章节
- Creation: 生成章节正文、修复草稿
- Review: 提交章节、复盘
- All: 查看所有可用工具

**示例**：
- 用户说"你好" → 缓存里已有 tool_search，不需要其他工具 → 用 chat_reply
- 用户说"创建新小说" → 需要 StartNewNovelProject → 如果缓存里没有，调用 tool_search(phase="Planning")
- 正在规划阶段，用户说"开始写" → 需要生成工具 → 调用 tool_search(phase="Creation")
```

### 关键改进点

1. **移除僵化指令**
   - 删除"直接使用缓存的工具"
   - 删除"只有在需要切换阶段时才重新调用"

2. **强调需求判断**
   - "根据用户意图和当前任务，判断需要什么工具"
   - "如果缓存中没有你需要的工具，先调用 tool_search"

3. **阶段说明降级**
   - 从"阶段判断原则"改为"阶段说明（供参考）"
   - 不强制 LLM 必须按阶段分类

4. **增加具体示例**
   - 三个典型场景的决策示例
   - 帮助 LLM 理解"何时需要新工具"

---

## 3. 实现细节

### 文件修改

**文件**: `Web/NovelAgentWeb/Support/AgentCore.cs`  
**方法**: `BuildActionSystemPrompt`  
**行数**: 1224-1243

### 不需要修改的部分

以下部分工作正常，无需改动：

1. **AgentToolRegistry.cs**
   - tool_search 实现（第875-931行）
   - 阶段到工具的映射（第34-66行）

2. **AgentObservationBuilder.cs**
   - 工具暴露逻辑（第827-855行）
   - Session 缓存机制

3. **AgentSession.cs**
   - DiscoveredPhase, DiscoveredTools, LastToolSearchAt 字段

---

## 4. 预期效果

### 改进前

```
LLM 思考：有缓存 → 直接用缓存工具 → 可能不符合当前任务需求
```

### 改进后

```
LLM 思考：当前任务需要什么工具？
         → 检查缓存
         → 缓存有 → 直接用
         → 缓存没有 → 调用 tool_search
```

### 典型场景

**场景 1: 阶段切换**
- 用户刚规划完故事，说"开始写第一章"
- LLM 判断：需要生成工具（GenerateChapterWithChanges）
- 检查缓存：只有 Planning 工具
- 决策：调用 tool_search(phase="Creation")

**场景 2: 同阶段多次交互**
- 用户规划了第一章，又说"再规划第二章"
- LLM 判断：需要 PlanChapter
- 检查缓存：有 PlanChapter
- 决策：直接使用缓存工具

**场景 3: 新会话启动**
- 用户打开新会话，说"你好"
- LLM 判断：不需要操作工具，只是打招呼
- 检查缓存：只有 tool_search
- 决策：用 chat_reply，不调用任何工具

---

## 5. 风险评估

### 低风险

**改动范围**：
- 只修改 System Prompt 文本
- 不改代码逻辑
- 不改数据结构

**回滚成本**：
- 改一行字符串即可回滚

### 可能的副作用

**过度调用 tool_search**：
- 如果 LLM 判断失误，可能多调用几次 tool_search
- 影响：每次多 1 次 LLM 调用，延迟增加 ~500ms
- 缓解：Session 缓存仍然存在，多数情况下能命中

**LLM 困惑**：
- 部分 LLM 可能不理解"需求驱动"的指导
- 影响：可能频繁调用 tool_search(phase="All")
- 缓解：示例足够清晰，主流 LLM 能理解

---

## 6. 测试建议

### 手动测试场景

1. **新会话问候**
   - 输入："你好"
   - 预期：chat_reply，不调用 tool_search

2. **创建小说**
   - 输入："创建一个玄幻小说"
   - 预期：调用 tool_search(phase="Planning")，然后 StartNewNovelProject

3. **阶段切换**
   - 先规划故事，再输入："开始写第一章"
   - 预期：调用 tool_search(phase="Creation")，然后生成工具

4. **同阶段连续操作**
   - 规划第一章后，输入："规划第二章"
   - 预期：直接用缓存的 PlanChapter

### 回归测试

- 确保现有的工具调用流程不受影响
- 验证 Session 缓存仍然生效
- 检查 tool_search 返回的工具列表正确

---

## 7. 后续优化方向

### 可选改进（本次不实现）

1. **缓存过期机制**
   - 当前缓存永久有效（除非调用新的 tool_search）
   - 未来可以加入"缓存过期时间"（如 5 分钟）

2. **工具使用统计**
   - 记录 LLM 调用 tool_search 的频率
   - 分析是否过度调用或调用不足

3. **动态阶段推断**
   - 根据 MissionBlackboard 自动推断当前阶段
   - 在 AnchorPrompt 中提示 LLM "建议阶段"

---

## 8. 总结

本设计通过优化 System Prompt，将工具发现机制从"规则驱动"改为"需求驱动"，让 LLM 能够：

- ✅ 根据任务自主判断需要什么工具
- ✅ 灵活使用工具缓存
- ✅ 在阶段切换时自然地调用 tool_search

改动极小（仅修改一段 System Prompt），风险可控，预期能显著改善工具发现的灵活性。
