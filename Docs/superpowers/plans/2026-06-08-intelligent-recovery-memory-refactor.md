# 智能失败恢复与三层记忆架构重构实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 NovelAgent 添加智能失败恢复能力（自动推理前置工具链并重试）和三层记忆架构（User/Project/Session 清晰分离）

**Architecture:** 并行开发两个独立模块（失败恢复引擎 + 记忆架构重构），最后在 AgentRuntime 中集成。失败恢复引擎负责工具执行失败后的自动恢复；记忆架构重构将现有 4 层记忆简化为 3 层并实现项目路由逻辑。

**Tech Stack:** .NET 8, C#, Microsoft Semantic Kernel, JSON 存储

---

## 文件结构

### 新建文件
- `Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs` - 失败分析、前置工具链推理、自动重试
- `Web/NovelAgentWeb/Support/ProjectRouter.cs` - 用户意图分类、项目路由
- `Tests/NovelAgentRegression/RecoveryEngineTests.cs` - 失败恢复测试
- `Tests/NovelAgentRegression/MemoryArchitectureTests.cs` - 记忆架构测试

### 修改文件
- `Web/NovelAgentWeb/Support/AgentCore.cs` - 重构数据模型（AgentWorkingMemory → AgentRuntimeContext）
- `Web/NovelAgentWeb/Support/AgentMemoryService.cs` - 三层记忆存储逻辑
- `Web/NovelAgentWeb/Support/AgentRuntime.cs` - 集成 RecoveryEngine 和 ProjectRouter
- `Web/NovelAgentWeb/Support/AgentKernel.cs` - 扩展 RepairableBlock 接口

---

## Task 1: 失败恢复引擎 - 数据模型

**Files:**
- Create: `Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs` (Part 1: Data models)

- [ ] **Step 1: Write failing test for ToolFailureType enum**

```csharp
// Tests/NovelAgentRegression/RecoveryEngineTests.cs
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class RecoveryEngineTests
{
    [Fact]
    public void ToolFailureType_HasExpectedValues()
    {
        var missingPrereq = ToolFailureType.MissingPrerequisite;
        var invalidParams = ToolFailureType.InvalidParameters;
        var logicViolation = ToolFailureType.LogicConstraintViolation;
        var resourceUnavail = ToolFailureType.ResourceUnavailable;
        
        Assert.Equal(0, (int)missingPrereq);
        Assert.Equal(1, (int)invalidParams);
        Assert.Equal(2, (int)logicViolation);
        Assert.Equal(3, (int)resourceUnavail);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests" -v n`
Expected: FAIL with "type or namespace 'ToolFailureType' could not be found"

- [ ] **Step 3: Create AgentRecoveryEngine.cs with data models**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public enum ToolFailureType
{
    MissingPrerequisite = 0,
    InvalidParameters = 1,
    LogicConstraintViolation = 2,
    ResourceUnavailable = 3,
}

public sealed class PrerequisiteToolStep
{
    public AgentToolCall ToolCall { get; set; } = new();
    public string MissingPrerequisiteTag { get; set; } = string.Empty;
}

public sealed class PrerequisiteToolChain
{
    public List<PrerequisiteToolStep> Steps { get; set; } = new();
    public int Priority { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ToolFailureAnalysis
{
    public ToolFailureType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRecoverable { get; set; }
    public List<PrerequisiteToolChain> RecommendedChains { get; set; } = new();
}

public sealed class RecoveryResult
{
    public bool Success { get; set; }
    public bool Recovered { get; set; }
    public string Message { get; set; } = string.Empty;
    public AgentToolExecutionResult? RetryResult { get; set; }
    
    public static RecoveryResult Unrecoverable(string reason) => new()
    {
        Success = false,
        Recovered = false,
        Message = $"不可恢复的失败：{reason}",
    };
    
    public static RecoveryResult ChainFailed(string toolName, string message) => new()
    {
        Success = false,
        Recovered = false,
        Message = $"前置工具链失败，{toolName} 执行出错：{message}",
    };
    
    public static RecoveryResult FromRetry(AgentToolExecutionResult retryResult) => new()
    {
        Success = retryResult.Success,
        Recovered = true,
        Message = retryResult.Success ? "自动恢复成功" : $"重试失败：{retryResult.Message}",
        RetryResult = retryResult,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.ToolFailureType_HasExpectedValues" -v n`
Expected: PASS

- [ ] **Step 5: Commit data models**

```bash
git add Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs Tests/NovelAgentRegression/RecoveryEngineTests.cs
git commit -m "feat(recovery): add failure analysis data models

- Add ToolFailureType enum (MissingPrerequisite, InvalidParameters, LogicConstraintViolation, ResourceUnavailable)
- Add PrerequisiteToolChain for representing prerequisite tool sequences
- Add ToolFailureAnalysis for failure classification results
- Add RecoveryResult for recovery attempt outcomes"
```

---

## Task 2: 失败恢复引擎 - 失败分析器

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs`
- Test: `Tests/NovelAgentRegression/RecoveryEngineTests.cs`

- [ ] **Step 1: Write failing test for AnalyzeFailure**

```csharp
// Add to RecoveryEngineTests.cs
[Fact]
public void AnalyzeFailure_MissingPrerequisite_ReturnsRecoverableWithChain()
{
    var engine = new AgentRecoveryEngine(null!, null!);
    var failedCall = new AgentToolCall { Name = "BuildChapterContextPackage", Arguments = new() { ["runId"] = "run123" } };
    var failedResult = new AgentToolExecutionResult { Success = false, Message = "构建上下文包前必须先选定章节候选。" };
    var session = new AgentSession { SessionId = "sess1", ActiveRunId = "run123", WorkingMemory = new AgentWorkingMemory() };
    var bible = new StoryBibleDocument { AgentRuns = new() };
    
    var analysis = engine.AnalyzeFailure(failedCall, failedResult, session, bible);
    
    Assert.Equal(ToolFailureType.MissingPrerequisite, analysis.Type);
    Assert.True(analysis.IsRecoverable);
    Assert.NotEmpty(analysis.RecommendedChains);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.AnalyzeFailure" -v n`
Expected: FAIL with "method 'AnalyzeFailure' not found"

- [ ] **Step 3: Implement AnalyzeFailure method**

```csharp
// Add to AgentRecoveryEngine.cs
public sealed class AgentRecoveryEngine
{
    private readonly AgentToolRegistry _toolRegistry;
    private readonly AgentToolGuardrails _guardrails;
    
    public AgentRecoveryEngine(AgentToolRegistry toolRegistry, AgentToolGuardrails guardrails)
    {
        _toolRegistry = toolRegistry;
        _guardrails = guardrails;
    }
    
    public ToolFailureAnalysis AnalyzeFailure(
        AgentToolCall failedCall,
        AgentToolExecutionResult failedResult,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var message = failedResult.Message?.ToLowerInvariant() ?? string.Empty;
        
        // Pattern 1: Missing prerequisite (RepairableBlock messages)
        if (message.Contains("必须先") || message.Contains("需要先") || message.Contains("前必须") || message.Contains("还没有"))
        {
            var chains = BuildPrerequisiteChains(failedCall, message, session, bible);
            return new ToolFailureAnalysis
            {
                Type = ToolFailureType.MissingPrerequisite,
                Reason = failedResult.Message,
                IsRecoverable = chains.Count > 0,
                RecommendedChains = chains,
            };
        }
        
        // Pattern 2: Invalid parameters (ID not found, format errors)
        if (message.Contains("不在当前") || message.Contains("不存在") || message.Contains("无效") || message.Contains("不是有效"))
        {
            return new ToolFailureAnalysis
            {
                Type = ToolFailureType.InvalidParameters,
                Reason = failedResult.Message,
                IsRecoverable = false,
                RecommendedChains = new(),
            };
        }
        
        // Pattern 3: Logic constraint violations (committed chapters, etc.)
        if (message.Contains("已提交") || message.Contains("不能自动") || message.Contains("只能"))
        {
            return new ToolFailureAnalysis
            {
                Type = ToolFailureType.LogicConstraintViolation,
                Reason = failedResult.Message,
                IsRecoverable = false,
                RecommendedChains = new(),
            };
        }
        
        // Pattern 4: Resource unavailable (Story Bible not initialized, etc.)
        if (message.Contains("尚未固化") || message.Contains("还未") || message.Contains("未知工具"))
        {
            var chains = BuildResourceInitChains(failedCall, message, session, bible);
            return new ToolFailureAnalysis
            {
                Type = ToolFailureType.ResourceUnavailable,
                Reason = failedResult.Message,
                IsRecoverable = chains.Count > 0,
                RecommendedChains = chains,
            };
        }
        
        // Default: unrecoverable
        return new ToolFailureAnalysis
        {
            Type = ToolFailureType.InvalidParameters,
            Reason = failedResult.Message,
            IsRecoverable = false,
            RecommendedChains = new(),
        };
    }
    
    private List<PrerequisiteToolChain> BuildPrerequisiteChains(
        AgentToolCall failedCall,
        string message,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();
        var runId = failedCall.Arguments.TryGetValue("runId", out var rid) ? rid : session.ActiveRunId ?? string.Empty;
        
        // Tool-specific prerequisite chains
        if (failedCall.Name == "BuildChapterContextPackage")
        {
            if (message.Contains("还没有候选") || message.Contains("章节候选"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "PlanChapter",
                                Arguments = new()
                                {
                                    ["creativeBrief"] = session.WorkingMemory?.CurrentGoal ?? "继续当前章节规划",
                                    ["sourceTurnId"] = session.ChatHistory?.LastOrDefault()?.TurnId ?? string.Empty,
                                },
                            },
                            MissingPrerequisiteTag = "chapter_candidates",
                        },
                    },
                    Priority = 1,
                    Description = "缺少章节候选，需要先规划章节",
                });
            }
            else if (message.Contains("还没选定") || message.Contains("候选还没选定"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "SelectChapterCandidate",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_candidate_selection",
                        },
                    },
                    Priority = 1,
                    Description = "章节候选未选定，需要先选择候选",
                });
            }
        }
        else if (failedCall.Name == "GenerateChapterWithChanges" || failedCall.Name == "ValidateChapterDraft")
        {
            if (message.Contains("上下文包") || message.Contains("context"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "BuildChapterContextPackage",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_context_package",
                        },
                    },
                    Priority = 1,
                    Description = "缺少上下文包，需要先构建",
                });
            }
            else if (message.Contains("草稿") || message.Contains("draft"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "GenerateChapterWithChanges",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_draft",
                        },
                    },
                    Priority = 1,
                    Description = "缺少章节草稿，需要先生成",
                });
            }
        }
        else if (failedCall.Name == "CommitValidatedChapter")
        {
            if (message.Contains("门禁") || message.Contains("gate") || message.Contains("校验"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "ValidateChapterDraft",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "generation_gate",
                        },
                    },
                    Priority = 1,
                    Description = "门禁未通过，需要先校验",
                });
            }
        }
        
        return chains;
    }
    
    private List<PrerequisiteToolChain> BuildResourceInitChains(
        AgentToolCall failedCall,
        string message,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();
        
        if (message.Contains("story bible 尚未固化") || message.Contains("故事地基"))
        {
            chains.Add(new PrerequisiteToolChain
            {
                Steps = new()
                {
                    new PrerequisiteToolStep
                    {
                        ToolCall = new AgentToolCall
                        {
                            Name = "PlanStoryFoundation",
                            Arguments = new()
                            {
                                ["userSeed"] = session.WorkingMemory?.CurrentGoal ?? "生成故事地基",
                                ["genre"] = "通用",
                            },
                        },
                        MissingPrerequisiteTag = "story_foundation",
                    },
                },
                Priority = 1,
                Description = "Story Bible 未初始化，需要先规划故事地基",
            });
        }
        
        return chains;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.AnalyzeFailure" -v n`
Expected: PASS

- [ ] **Step 5: Commit failure analyzer**

```bash
git add Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs Tests/NovelAgentRegression/RecoveryEngineTests.cs
git commit -m "feat(recovery): implement failure analyzer with prerequisite chain builder

- Add AnalyzeFailure method with pattern matching for 4 failure types
- Implement BuildPrerequisiteChains for chapter workflow tools
- Implement BuildResourceInitChains for Story Bible initialization
- Auto-populate arguments from session context (runId, creativeBrief)"
```

---

## Task 3: 失败恢复引擎 - 核心恢复逻辑

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs`
- Test: `Tests/NovelAgentRegression/RecoveryEngineTests.cs`

- [ ] **Step 1: Write failing test for RecoverFromFailureAsync**

```csharp
// Add to RecoveryEngineTests.cs
[Fact]
public async Task RecoverFromFailureAsync_ExecutesPrerequisiteChainAndRetries()
{
    var toolRegistry = new MockAgentToolRegistry();
    var guardrails = new AgentToolGuardrails();
    var engine = new AgentRecoveryEngine(toolRegistry, guardrails);
    
    var failedCall = new AgentToolCall { Name = "BuildChapterContextPackage", Arguments = new() { ["runId"] = "run1" } };
    var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节候选还没选定。" };
    var session = new AgentSession { SessionId = "s1", ActiveRunId = "run1", WorkingMemory = new AgentWorkingMemory() };
    var bible = new StoryBibleDocument();
    
    var result = await engine.RecoverFromFailureAsync(failedCall, failedResult, session, bible, CancellationToken.None);
    
    Assert.True(result.Recovered);
    Assert.Equal(1, toolRegistry.ExecutedCalls.Count);
    Assert.Equal("SelectChapterCandidate", toolRegistry.ExecutedCalls[0].Name);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.RecoverFromFailureAsync" -v n`
Expected: FAIL with "method 'RecoverFromFailureAsync' not found"

- [ ] **Step 3: Implement RecoverFromFailureAsync**

```csharp
// Add to AgentRecoveryEngine.cs
public async Task<RecoveryResult> RecoverFromFailureAsync(
    AgentToolCall failedCall,
    AgentToolExecutionResult failedResult,
    AgentSession session,
    StoryBibleDocument bible,
    CancellationToken ct)
{
    // 1. Check guardrails - avoid infinite retry loops
    if (!_guardrails.CanAttempt(failedCall.Name, failedCall.Arguments))
    {
        return RecoveryResult.Unrecoverable("工具执行失败次数过多，已被熔断器阻止");
    }
    
    // 2. Analyze failure type
    var analysis = AnalyzeFailure(failedCall, failedResult, session, bible);
    
    if (!analysis.IsRecoverable)
    {
        return RecoveryResult.Unrecoverable(analysis.Reason);
    }
    
    if (analysis.RecommendedChains.Count == 0)
    {
        return RecoveryResult.Unrecoverable("无法推理出有效的前置工具链");
    }
    
    // 3. Select best chain (highest priority)
    var chain = analysis.RecommendedChains.OrderByDescending(c => c.Priority).First();
    
    // 4. Execute prerequisite tool chain
    foreach (var step in chain.Steps)
    {
        var stepResult = await _toolRegistry.ExecuteAsync(step.ToolCall, session, bible, false, ct).ConfigureAwait(false);
        if (!stepResult.Success)
        {
            return RecoveryResult.ChainFailed(step.ToolCall.Name, stepResult.Message);
        }
    }
    
    // 5. Retry original tool (最多 1 次完整链路)
    var retryResult = await _toolRegistry.ExecuteAsync(failedCall, session, bible, false, ct).ConfigureAwait(false);
    return RecoveryResult.FromRetry(retryResult);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.RecoverFromFailureAsync" -v n`
Expected: PASS

- [ ] **Step 5: Commit recovery logic**

```bash
git add Web/NovelAgentWeb/Support/AgentRecoveryEngine.cs Tests/NovelAgentRegression/RecoveryEngineTests.cs
git commit -m "feat(recovery): implement core recovery logic with prerequisite chain execution

- Add RecoverFromFailureAsync with guardrails check
- Execute prerequisite tool chain sequentially
- Retry original tool after chain succeeds
- Return detailed RecoveryResult with success/failure status"
```

---

## Task 4: 三层记忆架构 - 数据模型重构

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentCore.cs` (lines 439-486)
- Test: `Tests/NovelAgentRegression/MemoryArchitectureTests.cs`

- [ ] **Step 1: Write failing test for new memory models**

```csharp
// Tests/NovelAgentRegression/MemoryArchitectureTests.cs
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class MemoryArchitectureTests
{
    [Fact]
    public void UserProfile_HasExpectedProperties()
    {
        var profile = new UserProfile
        {
            UserId = "user123",
            StylePreferences = new() { ["tone"] = "幽默轻松" },
            GenreHabits = new() { ["科幻"] = 5 },
            ConfirmationTolerance = "medium",
            GlobalConstraints = new() { "禁止血腥暴力描写" },
        };
        
        Assert.Equal("user123", profile.UserId);
        Assert.Equal("幽默轻松", profile.StylePreferences["tone"]);
        Assert.Equal(5, profile.GenreHabits["科幻"]);
        Assert.Single(profile.GlobalConstraints);
    }
    
    [Fact]
    public void SessionContext_CanHaveNullActiveProjectId()
    {
        var session = new SessionContext
        {
            SessionId = "sess1",
            ActiveProjectId = null,
        };
        
        Assert.Null(session.ActiveProjectId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests" -v n`
Expected: FAIL with "type 'UserProfile' could not be found"

- [ ] **Step 3: Add new memory models to AgentCore.cs**

```csharp
// Add to AgentCore.cs after line 438 (before AgentWorkingMemory)
public sealed class UserProfile
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "default";
    
    [JsonPropertyName("stylePreferences")]
    public Dictionary<string, string> StylePreferences { get; set; } = new();
    
    [JsonPropertyName("genreHabits")]
    public Dictionary<string, int> GenreHabits { get; set; } = new();
    
    [JsonPropertyName("confirmationTolerance")]
    public string ConfirmationTolerance { get; set; } = "medium";
    
    [JsonPropertyName("globalConstraints")]
    public List<string> GlobalConstraints { get; set; } = new();
}

public sealed class SessionContext
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    
    [JsonPropertyName("activeProjectId")]
    public string? ActiveProjectId { get; set; }
    
    [JsonPropertyName("chatHistory")]
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();
    
    [JsonPropertyName("currentGoal")]
    public string CurrentGoal { get; set; } = string.Empty;
    
    [JsonPropertyName("openQuestions")]
    public List<string> OpenQuestions { get; set; } = new();
    
    [JsonPropertyName("recentObservations")]
    public List<AgentRuntimeObservation> RecentObservations { get; set; } = new();
    
    [JsonPropertyName("pendingToolCall")]
    public AgentToolCall? PendingToolCall { get; set; }
    
    [JsonPropertyName("pendingConfirmation")]
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
}

public sealed class AgentRuntimeContext
{
    [JsonPropertyName("user")]
    public UserProfile User { get; set; } = new();
    
    [JsonPropertyName("activeProject")]
    public NovelProjectInfo? ActiveProject { get; set; }
    
    [JsonPropertyName("session")]
    public SessionContext Session { get; set; } = new();
    
    [JsonPropertyName("mission")]
    public AgentMissionState Mission { get; set; } = new();
    
    [JsonPropertyName("missionPlan")]
    public AgentMissionPlan MissionPlan { get; set; } = new();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests" -v n`
Expected: PASS

- [ ] **Step 5: Commit new memory models**

```bash
git add Web/NovelAgentWeb/Support/AgentCore.cs Tests/NovelAgentRegression/MemoryArchitectureTests.cs
git commit -m "feat(memory): add three-tier memory architecture models

- Add UserProfile for global user-level preferences
- Add SessionContext for ephemeral conversation state
- Add AgentRuntimeContext to replace AgentWorkingMemory
- Keep existing AgentWorkingMemory for backward compatibility (will deprecate later)"
```

---

## Task 5: 项目路由器 - 意图分类

**Files:**
- Create: `Web/NovelAgentWeb/Support/ProjectRouter.cs`
- Test: `Tests/NovelAgentRegression/MemoryArchitectureTests.cs`

- [ ] **Step 1: Write failing test for intent classification**

```csharp
// Add to MemoryArchitectureTests.cs
[Fact]
public async Task ClassifyIntentAsync_NewBookKeyword_ReturnsCreateNew()
{
    var router = new ProjectRouter(null!, null!);
    var session = new SessionContext { SessionId = "s1" };
    
    var intent = await router.ClassifyIntentAsync("我要写一本新书", session, CancellationToken.None);
    
    Assert.Equal(UserProjectIntent.CreateNew, intent);
}

[Fact]
public async Task ClassifyIntentAsync_ContinueKeyword_ReturnsContinueExisting()
{
    var router = new ProjectRouter(null!, null!);
    var session = new SessionContext { SessionId = "s1" };
    
    var intent = await router.ClassifyIntentAsync("续写之前的小说", session, CancellationToken.None);
    
    Assert.Equal(UserProjectIntent.ContinueExisting, intent);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.ClassifyIntentAsync" -v n`
Expected: FAIL with "type 'ProjectRouter' could not be found"

- [ ] **Step 3: Create ProjectRouter.cs with intent classification**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Web.NovelAgentWeb.Support;

public enum UserProjectIntent
{
    CreateNew = 0,
    ContinueExisting = 1,
    Unresolved = 2,
}

public sealed class ProjectResolutionResult
{
    public bool Success { get; set; }
    public bool NeedsClarification { get; set; }
    public string ClarificationMessage { get; set; } = string.Empty;
    public NovelProjectInfo? Project { get; set; }
}

public sealed class ProjectRouter
{
    private readonly NovelProjectCatalog _catalog;
    private readonly NovelAgentWorkspace _workspace;
    
    private static readonly HashSet<string> NewProjectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "新书", "新小说", "创建", "开始写", "写一本", "写一个", "新建",
    };
    
    private static readonly HashSet<string> ContinueKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "续写", "继续", "上一本", "之前的", "接着写", "继续写",
    };
    
    public ProjectRouter(NovelProjectCatalog catalog, NovelAgentWorkspace workspace)
    {
        _catalog = catalog;
        _workspace = workspace;
    }
    
    public async Task<UserProjectIntent> ClassifyIntentAsync(
        string userMessage,
        SessionContext session,
        CancellationToken ct)
    {
        var message = userMessage?.Trim().ToLowerInvariant() ?? string.Empty;
        
        // 1. Keyword matching (fast path)
        if (NewProjectKeywords.Any(kw => message.Contains(kw)))
            return UserProjectIntent.CreateNew;
        
        if (ContinueKeywords.Any(kw => message.Contains(kw)))
            return UserProjectIntent.ContinueExisting;
        
        // 2. Check if message mentions existing project title
        var catalog = await _catalog.LoadAsync(ct).ConfigureAwait(false);
        foreach (var project in catalog.Projects)
        {
            if (!string.IsNullOrWhiteSpace(project.Title) && message.Contains(project.Title.ToLowerInvariant()))
                return UserProjectIntent.ContinueExisting;
        }
        
        // 3. If session already has active project and no explicit switch intent, continue
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return UserProjectIntent.ContinueExisting;
        
        // 4. Default to unresolved for ambiguous cases
        return UserProjectIntent.Unresolved;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.ClassifyIntentAsync" -v n`
Expected: PASS

- [ ] **Step 5: Commit intent classification**

```bash
git add Web/NovelAgentWeb/Support/ProjectRouter.cs Tests/NovelAgentRegression/MemoryArchitectureTests.cs
git commit -m "feat(router): implement user intent classification for project routing

- Add UserProjectIntent enum (CreateNew, ContinueExisting, Unresolved)
- Add ProjectResolutionResult for routing outcomes
- Implement ClassifyIntentAsync with keyword matching and project title detection
- Default to ContinueExisting if session already has active project"
```

---

## __CONTINUE_HERE__

## Task 6: 项目路由器 - 项目解析逻辑

**Files:**
- Modify: `Web/NovelAgentWeb/Support/ProjectRouter.cs`
- Test: `Tests/NovelAgentRegression/MemoryArchitectureTests.cs`

- [ ] **Step 1: Write failing test for ResolveProjectAsync**

```csharp
// Add to MemoryArchitectureTests.cs
[Fact]
public async Task ResolveProjectAsync_CreateNewIntent_CreatesNewProject()
{
    var catalog = new MockNovelProjectCatalog();
    var workspace = new MockNovelAgentWorkspace();
    var router = new ProjectRouter(catalog, workspace);
    var session = new SessionContext { SessionId = "s1" };
    
    var result = await router.ResolveProjectAsync("我要写一本新书", session, CancellationToken.None);
    
    Assert.True(result.Success);
    Assert.NotNull(result.Project);
    Assert.Equal(result.Project.ProjectId, session.ActiveProjectId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.ResolveProjectAsync" -v n`
Expected: FAIL with "method 'ResolveProjectAsync' not found"

- [ ] **Step 3: Implement ResolveProjectAsync**

```csharp
// Add to ProjectRouter.cs
public async Task<ProjectResolutionResult> ResolveProjectAsync(
    string userMessage,
    SessionContext session,
    CancellationToken ct)
{
    var intent = await ClassifyIntentAsync(userMessage, session, ct).ConfigureAwait(false);
    
    return intent switch
    {
        UserProjectIntent.CreateNew => await CreateNewProjectAsync(session, ct).ConfigureAwait(false),
        UserProjectIntent.ContinueExisting => await LoadExistingProjectAsync(userMessage, session, ct).ConfigureAwait(false),
        UserProjectIntent.Unresolved => new ProjectResolutionResult
        {
            Success = false,
            NeedsClarification = true,
            ClarificationMessage = "请问您是要创建新小说，还是续写已有的小说？",
        },
        _ => throw new InvalidOperationException($"Unknown intent: {intent}"),
    };
}

private async Task<ProjectResolutionResult> CreateNewProjectAsync(
    SessionContext session,
    CancellationToken ct)
{
    var projectId = Guid.NewGuid().ToString("N");
    var project = new NovelProjectInfo
    {
        Id = projectId,
        Title = $"新小说 {DateTime.Now:yyyy-MM-dd HH:mm}",
        Genre = "未分类",
        StorageProjectName = projectId,
        CreatedAt = DateTime.Now,
    };
    
    await _catalog.AddAsync(project, ct).ConfigureAwait(false);
    await _catalog.ActivateAsync(project, ct).ConfigureAwait(false);
    
    session.ActiveProjectId = projectId;
    
    return new ProjectResolutionResult
    {
        Success = true,
        NeedsClarification = false,
        Project = project,
    };
}

private async Task<ProjectResolutionResult> LoadExistingProjectAsync(
    string userMessage,
    SessionContext session,
    CancellationToken ct)
{
    var catalog = await _catalog.LoadAsync(ct).ConfigureAwait(false);
    
    // Try to find project by title in message
    var message = userMessage.ToLowerInvariant();
    var matchedProject = catalog.Projects.FirstOrDefault(p =>
        !string.IsNullOrWhiteSpace(p.Title) && message.Contains(p.Title.ToLowerInvariant()));
    
    // Fallback to active project or most recent
    matchedProject ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
    
    if (matchedProject == null)
    {
        return new ProjectResolutionResult
        {
            Success = false,
            NeedsClarification = true,
            ClarificationMessage = "未找到已有小说项目。是否要创建新项目？",
        };
    }
    
    await _catalog.ActivateAsync(matchedProject, ct).ConfigureAwait(false);
    session.ActiveProjectId = matchedProject.Id;
    
    return new ProjectResolutionResult
    {
        Success = true,
        NeedsClarification = false,
        Project = matchedProject,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.ResolveProjectAsync" -v n`
Expected: PASS

- [ ] **Step 5: Commit project resolution logic**

```bash
git add Web/NovelAgentWeb/Support/ProjectRouter.cs Tests/NovelAgentRegression/MemoryArchitectureTests.cs
git commit -m "feat(router): implement project resolution with auto-creation and lookup

- Add ResolveProjectAsync orchestrating intent → project mapping
- Add CreateNewProjectAsync to generate new projects
- Add LoadExistingProjectAsync with title matching fallback to active project
- Update session.ActiveProjectId after resolution"
```

---

## Task 7: AgentRuntime 集成 - 失败恢复

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs` (line 312)

- [ ] **Step 1: Write failing integration test**

```csharp
// Add to RecoveryEngineTests.cs
[Fact]
public async Task AgentRuntime_ToolFailure_TriggersRecovery()
{
    var runtime = CreateTestRuntime();
    var session = new AgentSession { SessionId = "s1", ActiveRunId = "run1" };
    
    // Simulate tool failure that should trigger recovery
    var response = await runtime.RunAsync("构建第一章的上下文包", session, 5, CancellationToken.None);
    
    Assert.Contains("自动恢复", response.FinalMessage);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.AgentRuntime_ToolFailure" -v n`
Expected: FAIL (recovery not triggered)

- [ ] **Step 3: Integrate RecoveryEngine into AgentRuntime**

```csharp
// Modify AgentRuntime.cs constructor to inject RecoveryEngine
private readonly AgentRecoveryEngine _recoveryEngine;

public AgentRuntime(
    NovelAgentWorkspace workspace,
    AgentToolRegistry toolRegistry,
    AgentToolGuardrails guardrails,
    /* ... existing params ... */)
{
    // ... existing code ...
    _recoveryEngine = new AgentRecoveryEngine(toolRegistry, guardrails);
}

// Modify RunAsync around line 312 (after tool execution fails)
// Replace:
//   if (!result.Success)
//   {
//       lastObservation = new AgentRuntimeObservation { ... };
//       session.WorkingMemory.RecentObservations.Add(lastObservation);
//       break;
//   }
// With:
if (!result.Success)
{
    // Attempt automatic recovery
    var recoveryResult = await _recoveryEngine.RecoverFromFailureAsync(
        action.ToolCall,
        result,
        session,
        bible,
        ct).ConfigureAwait(false);
    
    if (recoveryResult.Recovered && recoveryResult.Success)
    {
        // Recovery succeeded, use retry result
        result = recoveryResult.RetryResult!;
        lastObservation = new AgentRuntimeObservation
        {
            Type = "tool_execution",
            Content = $"工具 {action.ToolCall.Name} 初次失败后自动恢复成功",
            Timestamp = DateTime.Now,
        };
    }
    else
    {
        // Recovery failed or not recoverable
        lastObservation = new AgentRuntimeObservation
        {
            Type = "tool_failure",
            Content = $"工具 {action.ToolCall.Name} 失败：{result.Message}。{recoveryResult.Message}",
            Timestamp = DateTime.Now,
        };
        session.WorkingMemory.RecentObservations.Add(lastObservation);
        break;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests.AgentRuntime_ToolFailure" -v n`
Expected: PASS

- [ ] **Step 5: Commit runtime integration**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs Tests/NovelAgentRegression/RecoveryEngineTests.cs
git commit -m "feat(runtime): integrate recovery engine into agent loop

- Inject AgentRecoveryEngine into AgentRuntime constructor
- Call RecoverFromFailureAsync after tool execution fails
- Use retry result if recovery succeeds
- Break agent loop only if recovery fails or not recoverable"
```

---

## Task 8: AgentRuntime 集成 - 项目路由

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs` (line 66)

- [ ] **Step 1: Write failing integration test**

```csharp
// Add to MemoryArchitectureTests.cs
[Fact]
public async Task AgentRuntime_NewSession_RoutesToNewProject()
{
    var runtime = CreateTestRuntime();
    var session = new SessionContext { SessionId = "s1", ActiveProjectId = null };
    
    var response = await runtime.RunAsync("我要写一本科幻小说", session, 5, CancellationToken.None);
    
    Assert.NotNull(session.ActiveProjectId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.AgentRuntime_NewSession" -v n`
Expected: FAIL (project routing not triggered)

- [ ] **Step 3: Integrate ProjectRouter into AgentRuntime**

```csharp
// Modify AgentRuntime.cs constructor to inject ProjectRouter
private readonly ProjectRouter _projectRouter;

public AgentRuntime(
    NovelAgentWorkspace workspace,
    AgentToolRegistry toolRegistry,
    NovelProjectCatalog catalog,
    /* ... existing params ... */)
{
    // ... existing code ...
    _projectRouter = new ProjectRouter(catalog, workspace);
}

// Modify RunAsync at line 66 (session initialization)
// Add before agent loop:
public async Task<NovelAgentResponse> RunAsync(
    string userMessage,
    AgentSession session,
    int maxSteps,
    CancellationToken ct)
{
    // 1. Project routing (first turn or explicit switch)
    if (string.IsNullOrWhiteSpace(session.ActiveProjectId) || IsProjectSwitchIntent(userMessage))
    {
        var resolution = await _projectRouter.ResolveProjectAsync(userMessage, session.ToSessionContext(), ct).ConfigureAwait(false);
        if (resolution.NeedsClarification)
        {
            return new NovelAgentResponse
            {
                Success = false,
                NeedsClarification = true,
                FinalMessage = resolution.ClarificationMessage,
            };
        }
        
        session.ActiveProjectId = resolution.Project!.Id;
    }
    
    // 2. Continue with existing agent loop...
    var project = await ResolveSessionProjectAsync(session, ct).ConfigureAwait(false);
    // ... rest of existing code ...
}

private bool IsProjectSwitchIntent(string message)
{
    var lower = message.ToLowerInvariant();
    return lower.Contains("切换") || lower.Contains("换个") || lower.Contains("换一本");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.AgentRuntime_NewSession" -v n`
Expected: PASS

- [ ] **Step 5: Commit project routing integration**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs Tests/NovelAgentRegression/MemoryArchitectureTests.cs
git commit -m "feat(runtime): integrate project router into session initialization

- Inject ProjectRouter into AgentRuntime constructor
- Add project resolution at start of RunAsync
- Return clarification response if intent is unresolved
- Add IsProjectSwitchIntent helper for explicit project switching"
```

---

## Task 9: AgentMemoryService 改造 - 三层存储

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentMemoryService.cs`

- [ ] **Step 1: Write failing test for LoadRuntimeContextAsync**

```csharp
// Add to MemoryArchitectureTests.cs
[Fact]
public async Task LoadRuntimeContextAsync_LoadsThreeTiers()
{
    var service = new AgentMemoryService(CreateTestWorkspace());
    var session = new SessionContext { SessionId = "s1", ActiveProjectId = "proj1" };
    var project = new NovelProjectInfo { Id = "proj1", Title = "测试小说" };
    
    var context = await service.LoadRuntimeContextAsync(session, project, CancellationToken.None);
    
    Assert.NotNull(context.User);
    Assert.NotNull(context.ActiveProject);
    Assert.NotNull(context.Session);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.LoadRuntimeContextAsync" -v n`
Expected: FAIL with "method 'LoadRuntimeContextAsync' not found"

- [ ] **Step 3: Implement LoadRuntimeContextAsync**

```csharp
// Add to AgentMemoryService.cs
public async Task<AgentRuntimeContext> LoadRuntimeContextAsync(
    SessionContext session,
    NovelProjectInfo project,
    CancellationToken ct)
{
    var userProfile = await LoadUserProfileAsync(ct).ConfigureAwait(false);
    var projectMemory = await LoadProjectMemoryAsync(project, ct).ConfigureAwait(false);
    var executionMemory = await LoadExecutionMemoryAsync(project, ct).ConfigureAwait(false);
    
    return new AgentRuntimeContext
    {
        User = userProfile,
        ActiveProject = project,
        Session = session,
        Mission = new AgentMissionState(),
        MissionPlan = new AgentMissionPlan(),
    };
}

private async Task<UserProfile> LoadUserProfileAsync(CancellationToken ct)
{
    var path = GetUserProfilePath();
    if (!File.Exists(path))
    {
        var defaultProfile = new UserProfile { UserId = "default" };
        await SaveUserProfileAsync(defaultProfile, ct).ConfigureAwait(false);
        return defaultProfile;
    }
    
    var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
    return JsonSerializer.Deserialize<UserProfile>(json) ?? new UserProfile();
}

private string GetUserProfilePath()
{
    var root = StoragePathHelper.GetStorageRoot();
    var userDir = Path.Combine(root, "Users", "default");
    Directory.CreateDirectory(userDir);
    return Path.Combine(userDir, "profile.json");
}

private async Task SaveUserProfileAsync(UserProfile profile, CancellationToken ct)
{
    var path = GetUserProfilePath();
    var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~MemoryArchitectureTests.LoadRuntimeContextAsync" -v n`
Expected: PASS

- [ ] **Step 5: Commit three-tier memory storage**

```bash
git add Web/NovelAgentWeb/Support/AgentMemoryService.cs Tests/NovelAgentRegression/MemoryArchitectureTests.cs
git commit -m "feat(memory): implement three-tier memory loading

- Add LoadRuntimeContextAsync to assemble User/Project/Session context
- Add LoadUserProfileAsync with default profile creation
- Add GetUserProfilePath using Users/{userId} directory
- Store UserProfile at global level (not per-workspace)"
```

---

## Task 10: 回归测试与验证

**Files:**
- Test: `Tests/NovelAgentRegression/NovelAgentRegression.csproj`

- [ ] **Step 1: Run all new tests**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --filter "FullyQualifiedName~RecoveryEngineTests|FullyQualifiedName~MemoryArchitectureTests" -v n`
Expected: All PASS

- [ ] **Step 2: Run existing regression tests**

Run: `/opt/homebrew/opt/dotnet@8/libexec/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj -v n`
Expected: All PASS (ensure no regressions)

- [ ] **Step 3: Manual verification - Scenario 1 (missing prerequisite)**

```bash
# Start web server
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project Web/NovelAgentWeb/NovelAgentWeb.csproj
# Test via API: POST /api/agent/run with message "构建第一章的上下文包"
# Expected: Agent auto-executes PlanChapter → BuildChapterContextPackage
```

- [ ] **Step 4: Manual verification - Scenario 2 (new project)**

```bash
# Test via API: POST /api/agent/run with new session and message "我要写一本科幻小说"
# Expected: Creates new project, session.ActiveProjectId populated
```

- [ ] **Step 5: Commit final integration**

```bash
git add -A
git commit -m "feat: complete intelligent recovery and three-tier memory refactoring

Integration complete:
- AgentRecoveryEngine with automatic prerequisite chain execution
- ProjectRouter with intent classification and project resolution
- Three-tier memory (User/Project/Session) with clean separation
- AgentRuntime integration with both recovery and routing
- Full test coverage for recovery scenarios and memory architecture

Verified scenarios:
- Automatic recovery from missing prerequisites
- New project creation from user intent
- Existing project continuation
- User profile persistence across projects"
```

---

## 验证清单

### 失败恢复验证

- [ ] **场景 1**: 用户说"生成第一章的上下文包"但无章节候选 → Agent 自动执行 PlanChapter 再重试
- [ ] **场景 2**: BuildChapterContextPackage 但候选未选定 → Agent 自动执行 SelectChapterCandidate 再重试
- [ ] **场景 3**: CommitValidatedChapter 但 Story Bible 不存在 → Agent 识别为不可恢复并说明原因

### 记忆架构验证

- [ ] **场景 4**: 新 session 说"我要写一本科幻小说" → 创建新 NovelProject
- [ ] **场景 5**: 新 session 说"继续写《星际迷航》" → 加载已有项目
- [ ] **场景 6**: UserProfile 设置 StylePreferences["tone"] = "幽默轻松" → 新旧项目都遵循

---

## 后续优化

1. **恢复策略学习**: 记录成功恢复路径到 ExecutionMemory
2. **LLM 意图分类**: 替换关键词匹配为 LLM 分类（置信度 < 0.7 时询问）
3. **多项目并行**: 支持同一会话切换多个项目
4. **数据迁移工具**: 旧 AgentWorkingMemory → 新三层架构的迁移脚本

