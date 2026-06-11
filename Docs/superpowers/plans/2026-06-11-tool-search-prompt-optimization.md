# Tool Search System Prompt 优化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 优化 AgentCore 中的工具发现机制指导文本，从"规则驱动"改为"需求驱动"，让 LLM 能够根据任务需求自主判断何时调用 tool_search

**Architecture:** 仅修改 System Prompt 文本（AgentCore.cs 的 BuildActionSystemPrompt 方法），不改代码逻辑、不改数据结构。改动范围小，风险低，可快速回滚。

**Tech Stack:** C# / ASP.NET Core 8.0

---

## File Structure

**Modify:**
- `Web/NovelAgentWeb/Support/AgentCore.cs:1224-1243` - BuildActionSystemPrompt 方法中的工具发现机制文本

**No new files created** - 这是纯文本优化任务

---

### Task 1: 更新工具发现机制文本

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs:1224-1243`

- [ ] **Step 1: 定位当前文本**

当前 BuildActionSystemPrompt 方法第 1224-1243 行包含：

```csharp
"## 工具发现机制\n" +
"你通过 tool_search 工具来发现当前可用的工具。\n\n" +
"**工作流程**：\n" +
"1. 分析用户意图和当前任务状态\n" +
"2. 判断处于哪个工作阶段：\n" +
"   - Conversation: 闲聊、问候、状态查询\n" +
"   - Planning: 规划故事地基/卷/章节\n" +
"   - Creation: 生成章节正文\n" +
"   - Review: 提交章节、复盘\n" +
"   - All: 不确定时查看全部工具\n" +
"3. 调用 tool_search(phase=\"Planning\") 获取该阶段可用工具\n" +
"4. 从返回的工具中选择合适的工具执行\n\n" +
"**阶段判断原则**：\n" +
"- 用户问候/提问/查询状态 → Conversation\n" +
"- 用户说\"创建新小说\"/\"规划故事\"/\"下一章\" → Planning\n" +
"- 已有章节规划，需要生成正文 → Creation\n" +
"- 草稿已生成，需要提交或复盘 → Review\n" +
"- 根据 MissionBlackboard 的任务状态判断阶段\n" +
"- 不确定时先用 tool_search(phase=\"All\") 查看全部工具\n\n" +
"**重要**：如果当前会话已经发现了工具（有工具缓存），直接使用缓存的工具。只有在需要切换阶段时才重新调用 tool_search。\n\n"
```

需要删除并替换为新文本。

- [ ] **Step 2: 替换为需求驱动的新文本**

使用 Edit 工具替换第 1224-1243 行的文本：

```csharp
"## 工具发现机制\n\n" +
"你通过 tool_search 工具来动态发现可用工具。\n\n" +
"**基本原则**：\n" +
"1. 根据用户意图和当前任务，判断需要什么工具\n" +
"2. 检查已缓存的工具是否满足需求\n" +
"3. 如果缓存不满足，调用 tool_search(phase=\"阶段名\") 获取该阶段的工具\n" +
"4. 工具缓存是上次 tool_search 的结果。如果缓存中的工具能满足需求，直接使用；如果缓存中没有你需要的工具，先调用 tool_search 发现新工具。\n\n" +
"**阶段说明**（供参考，不是硬性规则）：\n" +
"- Conversation: 闲聊、问候、状态查询\n" +
"- Planning: 规划故事地基、卷、章节\n" +
"- Creation: 生成章节正文、修复草稿\n" +
"- Review: 提交章节、复盘\n" +
"- All: 查看所有可用工具\n\n" +
"**示例**：\n" +
"- 用户说\"你好\" → 缓存里已有 tool_search，不需要其他工具 → 用 chat_reply\n" +
"- 用户说\"创建新小说\" → 需要 StartNewNovelProject → 如果缓存里没有，调用 tool_search(phase=\"Planning\")\n" +
"- 正在规划阶段，用户说\"开始写\" → 需要生成工具 → 调用 tool_search(phase=\"Creation\")\n\n"
```

- [ ] **Step 3: 验证修改正确性**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
dotnet build
```

预期：编译成功，无语法错误

- [ ] **Step 4: 提交修改**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs
git commit -m "refactor(agent): optimize tool search system prompt from rule-driven to need-driven

- Remove rigid instruction '直接使用缓存的工具'
- Add flexible guidance: judge if cached tools meet current needs
- Downgrade phase rules to suggestions (供参考，不是硬性规则)
- Add concrete examples for typical scenarios

Design doc: Docs/superpowers/specs/2026-06-11-tool-search-prompt-optimization.md

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 手动测试验证

**Files:**
- None (manual testing)

- [ ] **Step 1: 启动后端服务**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

预期：服务启动成功，监听 5002 端口

- [ ] **Step 2: 测试场景 1 - 新会话问候**

打开前端 http://localhost:3002，创建新会话，输入："你好"

预期行为：
- LLM 使用 chat_reply 回复，不调用 tool_search
- 回复自然友好

- [ ] **Step 3: 测试场景 2 - 创建小说**

在同一会话中，输入："创建一个玄幻小说"

预期行为：
- LLM 判断需要 StartNewNovelProject 工具
- 调用 tool_search(phase="Planning") 发现工具
- 调用 StartNewNovelProject 工具

- [ ] **Step 4: 测试场景 3 - 阶段切换**

规划完故事后，输入："开始写第一章"

预期行为：
- LLM 判断需要生成工具（GenerateChapterWithChanges）
- 检查缓存：只有 Planning 工具
- 调用 tool_search(phase="Creation") 发现生成工具
- 执行生成流程

- [ ] **Step 5: 测试场景 4 - 同阶段连续操作**

规划第一章后，输入："再规划第二章"

预期行为：
- LLM 判断需要 PlanChapter
- 检查缓存：有 PlanChapter
- 直接使用缓存工具，不再调用 tool_search

- [ ] **Step 6: 记录测试结果**

在设计文档 `Docs/superpowers/specs/2026-06-11-tool-search-prompt-optimization.md` 末尾追加测试结果：

```markdown
## 测试结果

**测试时间**: 2026-06-11

**场景 1 - 新会话问候**:
- 输入: "你好"
- 行为: [实际行为]
- 结果: [通过/失败]

**场景 2 - 创建小说**:
- 输入: "创建一个玄幻小说"
- 行为: [实际行为]
- 结果: [通过/失败]

**场景 3 - 阶段切换**:
- 输入: "开始写第一章"
- 行为: [实际行为]
- 结果: [通过/失败]

**场景 4 - 同阶段连续操作**:
- 输入: "再规划第二章"
- 行为: [实际行为]
- 结果: [通过/失败]

**总结**: [整体评价]
```

- [ ] **Step 7: 提交测试结果**

```bash
git add Docs/superpowers/specs/2026-06-11-tool-search-prompt-optimization.md
git commit -m "docs: add manual testing results for tool search prompt optimization

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## 自我审查

**Spec 覆盖检查**：
- ✅ 第 2 节设计方案 - Task 1 实现文本替换
- ✅ 第 6 节测试建议 - Task 2 覆盖所有手动测试场景
- ✅ 改动极小、风险可控 - 只修改一段字符串
- ✅ 回滚成本低 - git revert 即可

**占位符扫描**：无 TBD、TODO、"类似 Task N"

**类型一致性**：无类型、方法签名定义（仅文本修改）

---

## 实现计划完成

计划已保存到 `Docs/superpowers/plans/2026-06-11-tool-search-prompt-optimization.md`
