# 天命 NovelAgent 智能失败恢复与三层记忆架构重构设计

**日期**: 2026-06-08  
**版本**: 1.0  
**状态**: 设计已批准，待实现

---

## 一、背景与动机

### 问题陈述

当前 NovelAgent 系统存在两个架构级缺陷：

1. **缺乏智能失败恢复机制**
   - 工具执行失败时直接卡住，退出 agent 循环并等待用户干预
   - 虽有 `RepairableBlock` 机制能推荐前置工具（AgentKernel.cs:576-588），但仅覆盖 3-4 个特定场景
   - 没有通用的失败分析、前置工具链推理和自动重试能力
   - 与 Hermes/GenericAgent 等智能 Agent 框架的自适应执行能力差距明显

2. **记忆架构边界模糊**
   - 现有 4 层记忆（Session/Project/Author/Execution）职责不清
   - 每个新会话默认继承 ProjectMemory，导致无法实现"新对话=新小说"的语义
   - 用户期望：全局用户级偏好（跨所有小说）+ 独立的小说项目记忆 + 临时会话状态
   - 缺少项目路由逻辑，无法智能识别用户是要创建新书还是续写旧书

### 业务影响

- **失败恢复缺失** → Agent 遇到工具前置条件不满足时无法自主推进，用户体验差
- **记忆混乱** → 新对话无法干净启动，项目间状态互相污染

---

## 二、设计目标

1. **智能失败恢复**：工具失败时自动分析原因、推理前置工具链并重试，像 Hermes 一样智能
2. **三层记忆分离**：清晰划分 User（全局）/ Project（小说）/ Session（临时）三层边界
3. **项目智能路由**：新对话时根据用户意图自动创建新项目或加载已有项目
4. **架构清晰可维护**：模块化设计，失败恢复和记忆架构两个模块并行开发

---

## 三、架构设计

### 3.1 智能失败恢复引擎

#### 3.1.1 失败分类体系

新建 `AgentRecoveryEngine.cs`，实现失败分析与恢复：

```csharp
public enum ToolFailureType
{
    MissingPrerequisite,        // 缺少前置条件（如未选候选就构建上下文）
    InvalidParameters,          // 参数格式错误、ID 不存在
    LogicConstraintViolation,   // 业务逻辑约束（如已提交章节不能重建上下文）
    ResourceUnavailable,        // 资源不可用（如 Story Bible 未固化）
}

public sealed class ToolFailureAnalysis
{
    public ToolFailureType Type { get; set; }
    public string Reason { get; set; }
    public bool IsRecoverable { get; set; }
    public List<PrerequisiteToolChain> RecommendedChains { get; set; }
}
```

#### 3.1.2 前置工具链推理

核心方法：

```csharp
public async Task<RecoveryResult> RecoverFromFailureAsync(
    AgentToolCall failedCall,
    AgentToolExecutionResult failedResult,
    AgentSession session,
    StoryBibleDocument bible,
    CancellationToken ct)
{
    // 1. 分析失败类型
    var analysis = AnalyzeFailure(failedCall, failedResult, session, bible);
    
    if (!analysis.IsRecoverable)
        return RecoveryResult.Unrecoverable(analysis.Reason);
    
    // 2. 选择最佳前置工具链
    var chain = SelectBestChain(analysis.RecommendedChains, session, bible);
    
    // 3. 执行前置工具链
    foreach (var step in chain.Steps)
    {
        var result = await _toolRegistry.ExecuteAsync(step, session, bible, false, ct);
        if (!result.Success)
            return RecoveryResult.ChainFailed(step.Name, result.Message);
    }
    
    // 4. 重试原工具（最多 1 次完整链路）
    var retryResult = await _toolRegistry.ExecuteAsync(failedCall, session, bible, false, ct);
    return RecoveryResult.FromRetry(retryResult);
}
```

#### 3.1.3 工具依赖图

从现有 ToolPolicyEngine 提取依赖关系：

```
PlanChapter → SelectChapterCandidate → BuildChapterContextPackage → GenerateChapterWithChanges
                                                                    → ValidateChapterDraft
                                                                    → RepairChapterDraft
                                                                    → CommitValidatedChapter

PlanStoryFoundation → CommitStoryFoundation → PlanVolumeArc → CommitVolumeArc
```

失败时反向推理缺失的前置步骤。

#### 3.1.4 与现有机制集成

- **扩展 RepairableBlock**：将现有逻辑（PolicyBuildContext:576-588）迁移到 RecoveryEngine
- **保留 Guardrails 熔断**：RecoveryEngine 执行前检查 Guardrails，避免无限重试
- **记录到 ExecutionMemory**：每次恢复尝试记录到 `ToolFailurePatterns`、`SuccessfulRepairNotes`

---

### 3.2 三层记忆架构重构

#### 3.2.1 新记忆层次

```
┌─────────────────────────────────────┐
│   UserProfile (全局唯一)            │
│   - StylePreferences                │
│   - GenreHabits                     │
│   - ConfirmationTolerance           │
│   - GlobalConstraints               │
└─────────────────────────────────────┘
            ↓ 引用
┌─────────────────────────────────────┐
│   NovelProject (每本小说独立)       │
│   - ProjectId + Title + Genre       │
│   - StoryBible (故事数据)           │
│   - ProjectMemory (长期项目记忆)    │
│   - ExecutionMemory (工具失败模式)  │
│   - ChapterArchive (章节库)         │
└─────────────────────────────────────┘
            ↓ 绑定
┌─────────────────────────────────────┐
│   SessionContext (会话临时)         │
│   - SessionId + ActiveProjectId     │
│   - ChatHistory (当前对话)          │
│   - CurrentGoal + OpenQuestions     │
│   - PendingToolCall/Confirmation    │
│   - RecentObservations (最近 5 条)  │
└─────────────────────────────────────┘
```

#### 3.2.2 数据模型变更

**AgentCore.cs 重构**：

移除现有的 `AgentWorkingMemory`（lines 439-454），替换为：

```csharp
public sealed class UserProfile
{
    public string UserId { get; set; }
    public Dictionary<string, string> StylePreferences { get; set; }
    public Dictionary<string, int> GenreHabits { get; set; }
    public string ConfirmationTolerance { get; set; }  // "high" | "medium" | "low"
    public List<string> GlobalConstraints { get; set; }
}

public sealed class NovelProject
{
    public string ProjectId { get; set; }
    public string Title { get; set; }
    public string Genre { get; set; }
    public StoryBibleDocument StoryBible { get; set; }
    public ProjectMemory Memory { get; set; }
    public ExecutionMemory Execution { get; set; }
}

public sealed class SessionContext
{
    public string SessionId { get; set; }
    public string? ActiveProjectId { get; set; }  // null 直到项目确定
    public List<AgentConversationTurn> ChatHistory { get; set; }
    public string CurrentGoal { get; set; }
    public List<string> OpenQuestions { get; set; }
    public List<AgentRuntimeObservation> RecentObservations { get; set; }
    public AgentToolCall? PendingToolCall { get; set; }
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
}

public sealed class AgentRuntimeContext
{
    public UserProfile User { get; set; }
    public NovelProject? ActiveProject { get; set; }  // null 在路由前
    public SessionContext Session { get; set; }
    public AgentMissionState Mission { get; set; }
    public AgentMissionPlan MissionPlan { get; set; }
}
```

#### 3.2.3 存储路径调整

**AgentMemoryService.cs 改造**：

```
{StorageRoot}/
├── Users/
│   └── {userId}/
│       └── profile.json                  # UserProfile (全局唯一)
└── Projects/
    └── {workspace}/
        ├── NovelProjects/
        │   ├── projects.json              # 项目 catalog
        │   └── {projectId}/
        │       ├── story_bible.json       # StoryBible
        │       ├── project_memory.json    # ProjectMemory
        │       ├── execution_memory.json  # ExecutionMemory
        │       └── chapters/              # 章节库
        └── Agent/
            └── sessions.json              # SessionContext 缓存（可选）
```

**关键变更**：
- UserProfile 提升到 Users/ 目录，真正全局
- NovelProject 数据全部集中到 `NovelProjects/{projectId}/` 下
- SessionContext 不持久化（或仅缓存用于恢复上下文）

---

### 3.3 项目智能路由

#### 3.3.1 意图分类

新建 `ProjectRouter.cs`：

```csharp
public enum UserProjectIntent
{
    CreateNew,       // 明确说"新书"或未提到任何书名
    ContinueExisting, // 明确提到书名或说"续写"
    Unresolved,      // 意图模糊，需询问用户
}

public async Task<ProjectResolutionResult> ResolveProjectAsync(
    string userMessage,
    SessionContext session,
    CancellationToken ct)
{
    // 1. 分析意图
    var intent = await ClassifyIntentAsync(userMessage, session, ct);
    
    // 2. 路由逻辑
    return intent switch
    {
        CreateNew => await CreateNewProjectAsync(session, ct),
        ContinueExisting => await LoadExistingProjectAsync(userMessage, session, ct),
        Unresolved => ProjectResolutionResult.NeedsClarification("请问您是要创建新小说，还是续写已有的小说？"),
        _ => throw new InvalidOperationException()
    };
}
```

#### 3.3.2 意图识别策略

**ClassifyIntentAsync 实现**：

1. **关键词匹配**（快速路径）：
   - 包含"新书"/"新小说"/"创建" → CreateNew
   - 包含"续写"/"继续"/"上一本" → ContinueExisting
   - 提到具体书名（在 catalog 中存在） → ContinueExisting

2. **LLM 分类**（模糊意图）：
   - 调用 LLM 分析用户消息，返回分类 + 置信度
   - 置信度 < 0.7 → Unresolved

3. **会话历史判断**：
   - 如果 session 已有 ActiveProjectId，且用户未明确切换意图 → 默认 ContinueExisting

#### 3.3.3 集成到 AgentRuntime

**AgentRuntime.cs:66-73 改造**：

```csharp
public async Task<NovelAgentResponse> RunAsync(
    string userMessage,
    AgentSession session,
    int maxSteps,
    CancellationToken ct)
{
    // 1. 项目路由（首轮或切换时）
    if (session.ActiveProjectId == null || IsProjectSwitchIntent(userMessage))
    {
        var resolution = await _projectRouter.ResolveProjectAsync(userMessage, session, ct);
        if (resolution.NeedsClarification)
            return NovelAgentResponse.AskUser(resolution.ClarificationMessage);
        
        session.ActiveProjectId = resolution.Project.ProjectId;
    }
    
    // 2. 加载项目上下文
    var project = await _catalog.FindAsync(session.ActiveProjectId, ct);
    var runtimeContext = await _memoryService.LoadRuntimeContextAsync(session, project, ct);
    
    // 3. 进入 agent 循环...
}
```

---

## 四、实现计划

### 4.1 并行开发模块

| 模块 | 核心文件 | 负责功能 |
|------|---------|---------|
| **模块 A：失败恢复** | `AgentRecoveryEngine.cs`（新建）<br>`AgentKernel.cs`（扩展） | 失败分析、前置工具链推理、重试逻辑 |
| **模块 B：记忆架构** | `AgentCore.cs`（重构）<br>`AgentMemoryService.cs`（改造）<br>`ProjectRouter.cs`（新建） | 三层记忆模型、存储路径调整、项目路由 |
| **集成层** | `AgentRuntime.cs` | 集成 RecoveryEngine（line 312）<br>集成 ProjectRouter（line 66）<br>更新 Anchor Prompt 构建（line 348） |

### 4.2 文件清单

**新建文件**：
- `Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs`
- `Web/NovelAgentWeb/Support/ProjectRouter.cs`

**修改文件**：
- `Web/NovelAgentWeb/Support/AgentCore.cs`（重构数据模型）
- `Web/NovelAgentWeb/Support/AgentMemoryService.cs`（存储逻辑）
- `Web/NovelAgentWeb/Support/AgentRuntime.cs`（集成两个模块）
- `Web/NovelAgentWeb/Support/AgentKernel.cs`（扩展 RepairableBlock）

**测试覆盖**：
- `Tests/NovelAgentRegression/RecoveryEngineTests.cs`（新建）
- `Tests/NovelAgentRegression/MemoryArchitectureTests.cs`（新建）
- 更新现有 AgentRuntime 回归测试

### 4.3 开发里程碑

**Phase 1：模块独立开发**（并行）
- [ ] AgentRecoveryEngine 实现失败分析和前置链推理
- [ ] ProjectRouter 实现意图分类和项目路由
- [ ] AgentCore/AgentMemoryService 重构三层记忆模型

**Phase 2：集成与联调**
- [ ] AgentRuntime 集成 RecoveryEngine（失败时自动恢复）
- [ ] AgentRuntime 集成 ProjectRouter（session 初始化时路由）
- [ ] 更新 Anchor Prompt 构建逻辑读取新记忆结构

**Phase 3：测试与验证**
- [ ] 单元测试：RecoveryEngine 各失败类型的恢复策略
- [ ] 单元测试：ProjectRouter 意图分类准确性
- [ ] 集成测试：完整 agent 循环在失败场景下的恢复能力
- [ ] 回归测试：确保现有章节生成流程不受影响

---

## 五、验证标准

### 5.1 失败恢复验证

**场景 1：缺少章节候选**
- 用户："生成第一章的上下文包"
- 期望：Agent 自动检测到缺少候选，先执行 PlanChapter，再执行 BuildChapterContextPackage

**场景 2：未选定候选**
- 工具：BuildChapterContextPackage（但候选未选定）
- 期望：Agent 自动执行 SelectChapterCandidate，然后重试

**场景 3：不可恢复失败**
- 工具：CommitValidatedChapter（但 Story Bible 不存在）
- 期望：Agent 识别为 LogicConstraintViolation，停止并向用户说明原因

### 5.2 记忆架构验证

**场景 4：新对话创建新小说**
- 用户（新 session）："我要写一本科幻小说"
- 期望：创建新 NovelProject，SessionContext.ActiveProjectId 指向新项目

**场景 5：续写旧小说**
- 用户（新 session）："继续写《星际迷航》"
- 期望：加载已有项目《星际迷航》的 StoryBible 和 ProjectMemory

**场景 6：全局用户偏好生效**
- UserProfile 设置：StylePreferences["tone"] = "幽默轻松"
- 期望：新小说和旧小说的章节生成都遵循此风格偏好

---

## 六、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| RecoveryEngine 推理错误导致工具链陷入循环 | 高 | 保留 Guardrails 熔断机制，最多恢复 1 次完整链路 |
| 三层记忆重构破坏现有章节生成流程 | 高 | 完整回归测试覆盖，Phase 1 先完成模型设计不破坏存储 |
| 意图分类准确率低导致项目路由错误 | 中 | 低置信度时主动询问用户，提供项目列表供选择 |
| 存储路径变更导致旧数据迁移问题 | 中 | 编写数据迁移工具，保留向后兼容逻辑 |

---

## 七、后续优化方向

1. **恢复策略学习**：记录成功的恢复路径到 ExecutionMemory，下次类似失败时优先尝试
2. **项目推荐系统**：根据用户历史习惯（GenreHabits）推荐相关项目
3. **多模态意图识别**：支持上传大纲文档、角色卡，自动创建项目
4. **分布式项目存储**：支持云端同步 NovelProject，跨设备续写

---

**设计批准人**: 用户  
**批准日期**: 2026-06-08  
**下一步**: 调用 writing-plans 技能创建实现计划
