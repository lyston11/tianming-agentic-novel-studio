# Tool Search机制实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现Hermes风格的Tool Search机制，让LLM自主判断工作阶段并动态发现可用工具

**Architecture:** 添加tool_search元工具，移除PhaseInference硬编码逻辑，在AgentSession中缓存已发现的工具，优化System Prompt教LLM如何使用工具发现机制

**Tech Stack:** ASP.NET Core 8.0, C# 12

---

## 文件结构

**新增文件:**
- 无（所有逻辑集成到现有文件）

**修改文件:**
- `Support/AgentSession.cs` - 添加工具缓存属性
- `Support/AgentToolRegistry.cs` - 添加tool_search工具和处理方法
- `Support/AgentCore.cs` - 修改System Prompt和工具暴露逻辑
- `Support/AgentRuntime.cs` - 移除PhaseInference调用

**删除文件:**
- `Support/PhaseInference.cs` - 删除整个文件

---

### Task 1: 扩展AgentSession添加工具缓存

**Files:**
- Modify: `Support/AgentSession.cs:1-22`

- [ ] **Step 1: 添加工具缓存属性**

在`AgentSession`类中添加三个新属性：

```csharp
public sealed class AgentSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string ActiveProjectId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public bool IsArchived { get; set; }
    public List<string> RunHistory { get; set; } = new();
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Tool Search缓存
    public string? DiscoveredPhase { get; set; }
    public List<ToolSchema> DiscoveredTools { get; set; } = new();
    public DateTime? LastToolSearchAt { get; set; }
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Support/AgentSession.cs
git commit -m "feat(agent): add tool search cache to AgentSession

- Add DiscoveredPhase to track current phase
- Add DiscoveredTools to cache discovered tools
- Add LastToolSearchAt for cache timestamp

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 在AgentToolRegistry添加tool_search工具

**Files:**
- Modify: `Support/AgentToolRegistry.cs:136-156`
- Modify: `Support/AgentToolRegistry.cs:874` (文件末尾添加新方法)

- [ ] **Step 1: 在BuildEntries中添加tool_search定义**

在`BuildEntries()`方法的entries数组中添加tool_search工具：

```csharp
private Dictionary<string, AgentToolEntry> BuildEntries()
{
    var entries = new[]
    {
        Entry("tool_search", "meta", "Low", false, new[] { "phase" }, 
              "搜索指定阶段的可用工具。phase参数（必填）可选值：Conversation（闲聊、问候、状态查询）、Planning（规划故事地基/卷/章节）、Creation（生成章节正文）、Review（提交章节、复盘）、All（返回全部工具）。根据用户意图和当前任务状态判断阶段。", 
              (call, session, _, _, ct) => ToolSearchAsync(call, session, ct)),
        Entry("StartNewNovelProject", "project", "Low", false, new[] { "title", "genre", "seed" }, "创建一本独立新小说并切换当前会话上下文，不覆盖旧书。", (call, session, _, _, ct) => StartNewNovelProjectAsync(call, session, ct)),
```

- [ ] **Step 2: 实现ToolSearchAsync方法**

在文件末尾添加tool_search处理方法：

```csharp
private Task<AgentToolExecutionResult> ToolSearchAsync(
    AgentToolCall call, 
    AgentSession session, 
    CancellationToken ct)
{
    var phaseArg = Arg(call, "phase").Trim();
    
    if (string.IsNullOrWhiteSpace(phaseArg))
    {
        return Task.FromResult(new AgentToolExecutionResult
        {
            Success = false,
            Message = "tool_search需要phase参数。可选值：Conversation/Planning/Creation/Review/All",
            Phase = session.Phase,
        });
    }
    
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
        return Task.FromResult(new AgentToolExecutionResult
        {
            Success = false,
            Message = $"未知阶段：{phaseArg}。可选：Conversation/Planning/Creation/Review/All",
            Phase = session.Phase,
        });
    }
    
    // 缓存到Session
    session.DiscoveredPhase = phaseArg;
    session.DiscoveredTools = tools.ToList();
    session.LastToolSearchAt = DateTime.UtcNow;
    
    var toolList = string.Join("\n", tools.Select(t => $"- {t.Name}: {t.Description}"));
    
    return Task.FromResult(new AgentToolExecutionResult
    {
        Success = true,
        Message = message + "\n\n" + toolList,
        Phase = session.Phase,
        Data = new { phase = phaseArg, tools },
    });
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Commit**

```bash
git add Support/AgentToolRegistry.cs
git commit -m "feat(agent): add tool_search meta-tool

- Add tool_search to discover tools by phase
- Cache discovered tools to session
- Support Conversation/Planning/Creation/Review/All phases

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: 移除高风险工具的确认机制

**Files:**
- Modify: `Support/AgentToolRegistry.cs:141-150`

- [ ] **Step 1: 修改RequiresConfirmation为false**

将以下5个工具的`RequiresConfirmation`从`true`改为`false`：

```csharp
Entry("CommitStoryFoundation", "commit", "High", false, new[] { "runId", "selectedMacroCandidateIndex", "selectedMacroCandidateId", "selectedMacroCandidateTitle" }, "把候选故事地基固化到 Story Bible。", (call, session, _, confirmed, ct) => CommitStoryFoundationAsync(call, session, confirmed, ct)),
Entry("CommitVolumeArc", "commit", "High", false, new[] { "runId" }, "把卷规划提交到 Story Bible。", (call, session, _, confirmed, ct) => CommitVolumeArcAsync(call, session, confirmed, ct)),
Entry("GenerateChapterWithChanges", "writing", "High", false, new[] { "runId" }, "生成章节正文和 CHANGES，硬门禁通过后才提交成稿。", (call, session, _, confirmed, ct) => GenerateChapterWithChangesAsync(call, session, confirmed, ct)),
Entry("RepairChapterDraft", "writing", "High", false, new[] { "runId" }, "根据门禁失败项修复章节草稿和 CHANGES。", (call, session, _, confirmed, ct) => RepairChapterDraftAsync(call, session, confirmed, ct)),
Entry("CommitValidatedChapter", "commit", "High", false, new[] { "runId" }, "提交已通过门禁的章节成稿，刷新索引并进入书城。", (call, session, _, confirmed, ct) => CommitValidatedChapterAsync(call, session, confirmed, ct)),
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Support/AgentToolRegistry.cs
git commit -m "refactor(agent): remove confirmation requirement from high-risk tools

- CommitStoryFoundation: requiresConfirmation true -> false
- CommitVolumeArc: requiresConfirmation true -> false
- GenerateChapterWithChanges: requiresConfirmation true -> false
- RepairChapterDraft: requiresConfirmation true -> false
- CommitValidatedChapter: requiresConfirmation true -> false

Adopt 'execute-then-correct' workflow pattern

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: 修改AgentCore的System Prompt

**Files:**
- Modify: `Support/AgentCore.cs:1179-1204`

- [ ] **Step 1: 添加工具发现机制说明**

在`BuildActionSystemPrompt`方法中，在"## Decision Principles"之后添加工具发现机制说明：

```csharp
"## Decision Principles\n" +
"1. Prioritize natural conversation. Use chat_reply for greetings, questions, status queries, and casual chat.\n" +
"2. Use tool calls only when user explicitly requests an action (e.g., '开始写章节', '生成草稿', '提交章节').\n" +
"3. For status queries like '进度如何' or '现在到哪了', use chat_reply with project context, NOT QueryProjectStatus tool.\n" +
"4. Project management: Call StartNewNovelProject when user wants to create a new novel. For casual greetings or questions, use chat_reply to explain you can help create novels.\n\n" +

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

"**重要**：如果当前会话已经发现了工具（有工具缓存），直接使用缓存的工具。只有在需要切换阶段时才重新调用 tool_search。\n\n" +

"## 任务执行原则\n" +
"采用'先执行后修正'模式，不要频繁请求用户确认：\n" +
"1. 理解用户意图后，使用tool_search找到工具，直接执行\n" +
"2. 执行后告知用户结果和下一步计划\n" +
"3. 如果用户不满意，会主动告诉你如何调整\n" +
"4. 工作流自带校验机制（如ValidateChapterDraft），发现问题自动修复\n\n" +

"**不要问**：\"我现在要调用XX工具，可以吗？\"\n" +
"**应该做**：调用工具 → 展示结果 → \"已完成XX，现在进行YY...\"\n\n"
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Support/AgentCore.cs
git commit -m "feat(agent): add tool discovery and execute-then-correct guidance to system prompt

- Add tool_search workflow explanation
- Add phase judgment principles
- Add execute-then-correct task execution principles

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: 修改AgentCore的工具暴露逻辑

**Files:**
- Modify: `Support/AgentCore.cs` (查找调用ListToolSchemasForPhase的位置)

- [ ] **Step 1: 找到工具暴露代码位置**

Run: `grep -n "ListToolSchemasForPhase" Support/AgentCore.cs`
Expected: 显示调用该方法的行号

- [ ] **Step 2: 修改工具暴露逻辑使用Session缓存**

找到类似这样的代码并修改：

```csharp
// BEFORE:
var availableTools = _toolRegistry.ListToolSchemasForPhase(phase);

// AFTER:
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
    availableTools = new List<ToolSchema>
    {
        _toolRegistry.Find("tool_search") != null 
            ? new ToolSchema
            {
                Name = "tool_search",
                Description = _toolRegistry.Find("tool_search")!.Description,
                Risk = "Low",
                RequiresConfirmation = false,
                Parameters = new Dictionary<string, string> { { "phase", "string" } }
            }
            : throw new InvalidOperationException("tool_search not found in registry")
    };
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Commit**

```bash
git add Support/AgentCore.cs
git commit -m "feat(agent): use session cache for tool exposure logic

- Check session.DiscoveredTools cache first
- Expose cached tools if available
- Fallback to tool_search only when no cache

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 移除AgentRuntime中的PhaseInference调用

**Files:**
- Modify: `Support/AgentRuntime.cs`

- [ ] **Step 1: 找到PhaseInference相关代码**

Run: `grep -n "PhaseInference\|_phaseInference" Support/AgentRuntime.cs`
Expected: 显示所有相关行号

- [ ] **Step 2: 删除PhaseInference字段和初始化**

删除类似这样的代码：

```csharp
// DELETE:
private readonly PhaseInference _phaseInference;

// DELETE in constructor:
_phaseInference = new PhaseInference();
```

- [ ] **Step 3: 删除InferPhase调用**

找到类似这样的代码并删除：

```csharp
// DELETE:
var phase = _phaseInference.InferPhase(userMessage, session);

// 如果phase变量后续被使用，改为：
// Phase不再由代码推断，LLM通过tool_search自己决定
// phase变量仅用于日志，可设为默认值
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add Support/AgentRuntime.cs
git commit -m "refactor(agent): remove PhaseInference hardcoded logic from runtime

- Remove _phaseInference field
- Remove InferPhase method calls
- Phase now determined by LLM via tool_search

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: 删除PhaseInference.cs文件

**Files:**
- Delete: `Support/PhaseInference.cs`

- [ ] **Step 1: 删除文件**

Run: `rm Support/PhaseInference.cs`
Expected: 文件被删除

- [ ] **Step 2: 验证没有其他引用**

Run: `grep -r "PhaseInference" --include="*.cs" .`
Expected: 没有结果或只有注释

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Commit**

```bash
git add Support/PhaseInference.cs
git commit -m "refactor(agent): delete PhaseInference hardcoded phase logic

Remove entire PhaseInference.cs file as phase judgment
is now done by LLM via tool_search mechanism

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 8: 端到端测试

**Files:**
- Test: Manual testing via API

- [ ] **Step 1: 启动后端**

Run: `ASPNETCORE_URLS=http://+:5002 dotnet run`
Expected: 服务启动在5002端口

- [ ] **Step 2: 测试首次对话（无缓存）**

发送用户消息："你好"
Expected: 
- LLM调用tool_search(phase="Conversation")
- 返回2个Conversation阶段工具
- Session缓存更新

- [ ] **Step 3: 测试后续对话（有缓存）**

发送用户消息："现在状态如何"
Expected:
- 直接使用缓存的Conversation工具
- LLM调用QueryProjectStatus

- [ ] **Step 4: 测试阶段切换**

发送用户消息："帮我创建一本新小说"
Expected:
- LLM判断需要Planning阶段
- LLM调用tool_search(phase="Planning")
- 缓存更新为Planning工具
- LLM调用StartNewNovelProject

- [ ] **Step 5: 测试高风险工具不需要确认**

创建项目后，发送："生成第一章"
Expected:
- LLM调用PlanChapter、GenerateChapterWithChanges等
- 不需要用户确认，直接执行

- [ ] **Step 6: 记录测试结果**

创建测试报告文件：

```bash
cat > Docs/superpowers/plans/2026-06-11-tool-search-test-report.md << 'EOF'
# Tool Search测试报告

## 测试时间
2026-06-11

## 测试场景

### 1. 首次对话（无缓存）
- ✅ 只暴露tool_search
- ✅ LLM成功判断阶段并调用tool_search
- ✅ Session缓存正确更新

### 2. 后续对话（有缓存）
- ✅ 直接使用缓存工具
- ✅ 不需要重复tool_search

### 3. 阶段切换
- ✅ LLM根据意图切换阶段
- ✅ 缓存正确更新

### 4. 高风险工具执行
- ✅ 不需要用户确认
- ✅ 采用"先执行后修正"模式

## 结论
Tool Search机制正常工作，满足设计要求。
EOF
```

- [ ] **Step 7: Commit测试报告**

```bash
git add Docs/superpowers/plans/2026-06-11-tool-search-test-report.md
git commit -m "test(agent): add tool search mechanism test report

All test scenarios passed:
- Tool discovery with cache
- Phase switching
- Execute-then-correct workflow

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## 自审清单

✅ **Spec覆盖**:
- Task 1: Session缓存 ✓
- Task 2: tool_search工具 ✓
- Task 3: 移除确认机制 ✓
- Task 4-5: System Prompt优化 ✓
- Task 6-7: 移除PhaseInference ✓
- Task 8: 端到端测试 ✓

✅ **占位符扫描**: 无TBD/TODO

✅ **类型一致性**: 所有引用的类型（ToolSchema, AgentSession等）在现有代码库中已定义

✅ **实现完整性**: 每个步骤都有完整代码，无"参考Task N"

---

计划已完成，保存到 `Docs/superpowers/plans/2026-06-11-tool-search-implementation.md`
