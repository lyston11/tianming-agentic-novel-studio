# Phase-Based Tool and Memory Layering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement phase-based tool exposure and memory loading to reduce token consumption and response time while maintaining agent autonomy.

**Architecture:** Introduce conversation phase inference that classifies each turn into one of four phases (Conversation, Planning, Creation, Review) based on user intent and mission state. Each phase exposes a different tool set and loads appropriate memory depth. Agent remains autonomous but operates within appropriate context for each phase.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core, Microsoft.SemanticKernel

---

## File Structure

**New Files:**
- `Web/NovelAgentWeb/Support/PhaseInference.cs` - Phase classification logic
- `Web/NovelAgentWeb/Support/PhaseContextBuilder.cs` - Phase-specific context construction

**Modified Files:**
- `Web/NovelAgentWeb/Support/AgentCore.cs` - Add phase enum and related types
- `Web/NovelAgentWeb/Support/AgentToolRegistry.cs` - Add phase-based tool filtering
- `Web/NovelAgentWeb/Support/AgentRuntime.cs` - Integrate phase inference and context building

**Test Files:**
- `Tests/NovelAgentRegression/PhaseInferenceTests.cs` - Phase classification tests
- `Tests/NovelAgentRegression/PhaseContextBuilderTests.cs` - Context building tests

---

### Task 10: Register new services in dependency injection

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs` or service registration file

- [ ] **Step 1: Add PhaseInference and PhaseContextBuilder to DI container**

Find the service registration section and add:

```csharp
builder.Services.AddSingleton<PhaseInference>();
builder.Services.AddSingleton<PhaseContextBuilder>();
```

- [ ] **Step 2: Verify services are registered before AgentRuntime**

Ensure registration order:
1. PhaseInference (no dependencies)
2. PhaseContextBuilder (depends on AgentMemoryService, NovelAgentWorkspace)
3. AgentRuntime (depends on PhaseInference, PhaseContextBuilder)

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Program.cs
git commit -m "feat: register PhaseInference and PhaseContextBuilder in DI"
```

---

### Task 11: Create phase inference tests

**Files:**
- Create: `Tests/NovelAgentRegression/PhaseInferenceTests.cs`

- [ ] **Step 1: Create test class with status query test**

```csharp
using Xunit;
using TM.Web.NovelAgentWeb.Support;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace Tests.NovelAgentRegression;

public class PhaseInferenceTests
{
    [Fact]
    public void ClassifyIntent_StatusQuery_ReturnsStatusQuery()
    {
        // Arrange
        var inference = new PhaseInference();
        var session = new SessionContext();
        
        // Act
        var intent = inference.ClassifyIntent("章节写完了吗", session);
        
        // Assert
        Assert.Equal(TurnIntent.StatusQuery, intent);
    }
    
    [Fact]
    public void ClassifyIntent_ShortGreeting_ReturnsFreeChat()
    {
        // Arrange
        var inference = new PhaseInference();
        var session = new SessionContext();
        
        // Act
        var intent = inference.ClassifyIntent("好的", session);
        
        // Assert
        Assert.Equal(TurnIntent.Confirmation, intent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (no implementation yet)**

```bash
dotnet test Tests/NovelAgentRegression/PhaseInferenceTests.cs --filter "FullyQualifiedName~PhaseInferenceTests"
```

Expected: Tests should pass since implementation exists

- [ ] **Step 3: Commit**

```bash
git add Tests/NovelAgentRegression/PhaseInferenceTests.cs
git commit -m "test: add phase inference classification tests"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs` (RunAsync method, around line 100-150)

- [ ] **Step 1: Add phase inference before agent loop**

Find the location after project routing (around line 120) and add:

```csharp
// Infer conversation phase
var phase = _phaseInference.InferPhase(
    userMessage,
    session.WorkingMemory.Mission,
    session);

// Log phase for debugging
_logger?.LogInformation("Conversation phase inferred: {Phase}", phase);

// Get phase-appropriate tools
var allowedTools = _toolRegistry.ListToolSchemasForPhase(phase);

_logger?.LogInformation("Phase {Phase} allows {ToolCount} tools", phase, allowedTools.Count);
```

- [ ] **Step 2: Pass phase-filtered tools to BuildAnchorPrompt**

Find the BuildAnchorPrompt call (around line 348) and modify to use allowedTools:

```csharp
var anchorPrompt = BuildAnchorPrompt(
    session,
    bible,
    allowedTools,  // Changed from _toolRegistry.ListToolSchemas()
    bible.AgentRuns.Where(r => session.RunHistory.Contains(r.RunId)).ToList());
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs
git commit -m "feat: integrate phase inference into agent loop"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentRuntime.cs` (constructor and RunAsync method)

- [ ] **Step 1: Add PhaseInference field to AgentRuntime**

```csharp
private readonly PhaseInference _phaseInference;
private readonly PhaseContextBuilder _contextBuilder;
```

- [ ] **Step 2: Update constructor to inject new services**

Find the constructor (around line 40-60) and add parameters:

```csharp
public AgentRuntime(
    NovelAgentWorkspace workspace,
    AgentKernel kernel,
    AgentMemoryService memoryService,
    AgentToolRegistry toolRegistry,
    NovelProjectCatalog catalog,
    AgentRecoveryEngine recoveryEngine,
    ProjectRouter projectRouter,
    PhaseInference phaseInference,
    PhaseContextBuilder contextBuilder)
{
    _workspace = workspace;
    _kernel = kernel;
    _memoryService = memoryService;
    _toolRegistry = toolRegistry;
    _catalog = catalog;
    _recoveryEngine = recoveryEngine;
    _projectRouter = projectRouter;
    _phaseInference = phaseInference;
    _contextBuilder = contextBuilder;
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Support/AgentRuntime.cs
git commit -m "feat: add PhaseInference and PhaseContextBuilder to AgentRuntime"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/PhaseContextBuilder.cs`

- [ ] **Step 1: Add PrepareCreationContext method**

```csharp
public async Task<Dictionary<string, object>> PrepareCreationContextAsync(
    SessionContext session,
    AgentMissionState mission,
    StoryBibleDocument bible,
    string? runId,
    CancellationToken ct)
{
    var context = new Dictionary<string, object>();
    
    // Find current run and context package
    NovelAgentRun? run = null;
    if (!string.IsNullOrWhiteSpace(runId))
    {
        run = bible.AgentRuns?.FirstOrDefault(r => 
            string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }
    
    // Full context package if available
    if (run?.ContextPackage != null)
    {
        context["context_package"] = run.ContextPackage;
    }
    
    // Story Bible (full)
    context["story_bible"] = bible;
    
    // Mission plan
    if (mission?.MissionPlan != null)
    {
        context["mission_plan"] = mission.MissionPlan;
    }
    
    // Recent observations (5 items for creation phase)
    if (session.RecentObservations != null && session.RecentObservations.Count > 0)
    {
        context["recent_observations"] = session.RecentObservations.TakeLast(5).ToList();
    }
    
    return context;
}
```

- [ ] **Step 2: Add PrepareReviewContext method**

```csharp
public async Task<Dictionary<string, object>> PrepareReviewContextAsync(
    SessionContext session,
    AgentMissionState mission,
    StoryBibleDocument bible,
    string? runId,
    CancellationToken ct)
{
    var context = new Dictionary<string, object>();
    
    // Find current run
    NovelAgentRun? run = null;
    if (!string.IsNullOrWhiteSpace(runId))
    {
        run = bible.AgentRuns?.FirstOrDefault(r => 
            string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }
    
    // Draft artifact
    if (run?.DraftArtifact != null)
    {
        context["draft_artifact"] = run.DraftArtifact;
    }
    
    // Gate report
    if (run?.GateReport != null)
    {
        context["gate_report"] = run.GateReport;
    }
    
    // Mission plan
    if (mission?.MissionPlan != null)
    {
        context["mission_plan"] = mission.MissionPlan;
    }
    
    // Recent observations (3 items)
    if (session.RecentObservations != null && session.RecentObservations.Count > 0)
    {
        context["recent_observations"] = session.RecentObservations.TakeLast(3).ToList();
    }
    
    return context;
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Support/PhaseContextBuilder.cs
git commit -m "feat: add creation and review context preparation"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/PhaseContextBuilder.cs`

- [ ] **Step 1: Add PrepareConversationContext method**

```csharp
public async Task<Dictionary<string, object>> PrepareConversationContextAsync(
    SessionContext session,
    AgentMissionState mission,
    CancellationToken ct)
{
    var context = new Dictionary<string, object>();
    
    // Minimal context: only recent observations
    if (session.RecentObservations != null && session.RecentObservations.Count > 0)
    {
        context["recent_observations"] = session.RecentObservations.TakeLast(1).ToList();
    }
    
    // Mission summary (very brief)
    if (mission?.MissionPlan != null)
    {
        context["mission_summary"] = new
        {
            stage = mission.MissionPlan.Stage,
            status = mission.MissionPlan.Status,
            project_title = mission.MissionPlan.ProjectTitle,
        };
    }
    
    return context;
}
```

- [ ] **Step 2: Add PreparePlanningContext method**

```csharp
public async Task<Dictionary<string, object>> PreparePlanningContextAsync(
    SessionContext session,
    AgentMissionState mission,
    StoryBibleDocument bible,
    CancellationToken ct)
{
    var context = new Dictionary<string, object>();
    
    // Recent observations (3 items)
    if (session.RecentObservations != null && session.RecentObservations.Count > 0)
    {
        context["recent_observations"] = session.RecentObservations.TakeLast(3).ToList();
    }
    
    // Mission plan summary
    if (mission?.MissionPlan != null)
    {
        context["mission_plan"] = mission.MissionPlan;
    }
    
    // Project memory summary
    var projectMemory = await _memoryService.LoadProjectMemoryAsync(
        new NovelProjectInfo { Id = session.ActiveProjectId ?? string.Empty },
        ct).ConfigureAwait(false);
    
    if (projectMemory != null)
    {
        context["project_memory_summary"] = new
        {
            total_chapters = projectMemory.TotalChapters,
            key_patterns = projectMemory.SuccessPatterns.Take(3).ToList(),
        };
    }
    
    // Story Bible basics
    if (bible?.Constitution != null)
    {
        context["story_foundation"] = new
        {
            genre = bible.Constitution.Genre,
            core_hook = bible.Constitution.CoreHook,
            volume_count = bible.VolumeArcs?.Count ?? 0,
        };
    }
    
    return context;
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Support/PhaseContextBuilder.cs
git commit -m "feat: add conversation and planning context preparation"
```

---

**Files:**
- Create: `Web/NovelAgentWeb/Support/PhaseContextBuilder.cs`

- [ ] **Step 1: Create PhaseContextBuilder class with dependencies**

```csharp
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class PhaseContextBuilder
{
    private readonly AgentMemoryService _memoryService;
    private readonly NovelAgentWorkspace _workspace;
    
    public PhaseContextBuilder(AgentMemoryService memoryService, NovelAgentWorkspace workspace)
    {
        _memoryService = memoryService;
        _workspace = workspace;
    }
    
    public async Task<int> EstimateTokensForPhaseAsync(
        ConversationPhase phase,
        SessionContext session,
        CancellationToken ct)
    {
        return phase switch
        {
            ConversationPhase.Conversation => 500,   // Checkpoint only
            ConversationPhase.Planning => 2500,      // Checkpoint + summary + RAG(5)
            ConversationPhase.Creation => 12000,     // Full context package
            ConversationPhase.Review => 7000,        // Checkpoint + draft + reports
            _ => 500,
        };
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Support/PhaseContextBuilder.cs
git commit -m "feat: create PhaseContextBuilder with token estimation"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/AgentToolRegistry.cs` (after line 30)

- [ ] **Step 1: Define tool tier arrays**

```csharp
private static readonly string[] ConversationTools = new[]
{
    "QueryProjectStatus",
    "StartNewNovelProject",
};

private static readonly string[] PlanningTools = new[]
{
    "QueryProjectStatus",
    "StartNewNovelProject",
    "SearchCreativeKnowledge",
    "PlanStoryFoundation",
    "PlanVolumeArc",
    "PlanChapter",
    "SelectChapterCandidate",
    "BuildChapterContextPackage",
};

private static readonly string[] CreationTools = new[]
{
    "QueryProjectStatus",
    "GenerateChapterWithChanges",
    "RepairChapterDraft",
    "ValidateChapterDraft",
};

private static readonly string[] ReviewTools = new[]
{
    "QueryProjectStatus",
    "CommitValidatedChapter",
    "ReviewChapter",
    "RefreshProjectIndexes",
};
```

- [ ] **Step 2: Add GetToolsForPhase method**

```csharp
public IReadOnlyList<string> GetToolNamesForPhase(ConversationPhase phase)
{
    return phase switch
    {
        ConversationPhase.Conversation => ConversationTools,
        ConversationPhase.Planning => PlanningTools,
        ConversationPhase.Creation => CreationTools,
        ConversationPhase.Review => ReviewTools,
        _ => ConversationTools,
    };
}

public IReadOnlyList<ToolSchema> ListToolSchemasForPhase(ConversationPhase phase)
{
    var allowedNames = GetToolNamesForPhase(phase);
    return _entries
        .Where(e => allowedNames.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
        .Select(e => new ToolSchema
        {
            Name = e.Value.Definition.Name,
            Description = e.Value.Definition.Description,
            Risk = e.Value.Definition.Risk,
            RequiresConfirmation = false,
            Parameters = e.Value.Definition.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
        })
        .ToList();
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Support/AgentToolRegistry.cs
git commit -m "feat: add phase-based tool filtering to AgentToolRegistry"
```

---

**Files:**
- Modify: `Web/NovelAgentWeb/Support/PhaseInference.cs`

- [ ] **Step 1: Add InferPhase method**

```csharp
public ConversationPhase InferPhase(
    string userMessage,
    AgentMissionState mission,
    SessionContext session)
{
    var intent = ClassifyIntent(userMessage, session);
    
    // 1. Status query always returns Conversation
    if (intent == TurnIntent.StatusQuery)
        return ConversationPhase.Conversation;
    
    // 2. Confirmation also returns Conversation
    if (intent == TurnIntent.Confirmation && session.PendingToolCall != null)
        return ConversationPhase.Conversation;
    
    // 3. Free chat returns Conversation
    if (intent == TurnIntent.FreeChat)
        return ConversationPhase.Conversation;
    
    // 4. Creative intents depend on mission state
    if (intent == TurnIntent.CreativeBrief || intent == TurnIntent.ContinueMission)
    {
        var currentTask = GetCurrentTask(mission);
        
        if (currentTask == null || string.IsNullOrWhiteSpace(currentTask.Status) || currentTask.Status == "unstarted")
            return ConversationPhase.Planning;
        
        if (currentTask.Status == "context_ready")
            return ConversationPhase.Creation;
        
        if (currentTask.Status == "draft_generated" || currentTask.Status == "validated")
            return ConversationPhase.Review;
        
        return ConversationPhase.Planning;
    }
    
    // 5. Revision request needs full context
    if (intent == TurnIntent.RevisionRequest)
        return ConversationPhase.Creation;
    
    // 6. Default
    return ConversationPhase.Conversation;
}

private static AgentMissionTask? GetCurrentTask(AgentMissionState mission)
{
    if (mission?.MissionPlan?.SchedulerState?.Tasks == null)
        return null;
    
    return mission.MissionPlan.SchedulerState.Tasks
        .FirstOrDefault(t => t.Status != "completed" && t.Status != "cancelled");
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Support/PhaseInference.cs
git commit -m "feat: add phase inference logic based on intent and mission state"
```

---

**Files:**
- Create: `Web/NovelAgentWeb/Support/PhaseInference.cs`

- [ ] **Step 1: Create PhaseInference class with intent classification**

```csharp
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class PhaseInference
{
    private static readonly string[] StatusQueryKeywords = new[] { "状态", "进度", "完成", "写了", "做了", "怎么样" };
    private static readonly string[] ConfirmationKeywords = new[] { "好的", "确认", "可以", "继续", "行" };
    private static readonly string[] NewProjectKeywords = new[] { "新书", "新小说", "创建" };
    
    public TurnIntent ClassifyIntent(string userMessage, SessionContext session)
    {
        var message = userMessage?.Trim().ToLowerInvariant() ?? string.Empty;
        
        // 1. Status query
        if (StatusQueryKeywords.Any(kw => message.Contains(kw)))
            return TurnIntent.StatusQuery;
        
        // 2. Confirmation
        if (session.PendingToolCall != null && ConfirmationKeywords.Any(kw => message.Contains(kw)))
            return TurnIntent.Confirmation;
        
        // 3. Free chat (short message without creative keywords)
        if (message.Length < 10 && !message.Contains("写") && !message.Contains("章"))
            return TurnIntent.FreeChat;
        
        // 4. New project
        if (NewProjectKeywords.Any(kw => message.Contains(kw)))
            return TurnIntent.NewProjectSeed;
        
        // 5. Continue mission
        if (message.Contains("继续") || message.Contains("下一章") || message.Contains("下一个"))
            return TurnIntent.ContinueMission;
        
        // 6. Revision request
        if (message.Contains("修改") || message.Contains("修复") || message.Contains("改"))
            return TurnIntent.RevisionRequest;
        
        // 7. Default to creative brief
        return TurnIntent.CreativeBrief;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Support/PhaseInference.cs
git commit -m "feat: add PhaseInference with intent classification"
```

---

### Task 13: Manual verification and performance testing

**Files:**
- N/A (manual testing)

- [ ] **Step 1: Build and start the application**

```bash
cd Web/NovelAgentWeb
dotnet build
dotnet run
```

Expected: Application starts without DI errors

- [ ] **Step 2: Test conversation phase (status query)**

Send message: "当前项目状态怎么样"

Expected:
- Phase inferred as Conversation
- Only 2 tools available (QueryProjectStatus, StartNewNovelProject)
- Response time: ~1-2s
- Token usage: ~500-1000

- [ ] **Step 3: Test planning phase (new chapter)**

Send message: "规划下一章"

Expected:
- Phase inferred as Planning
- 8 planning tools available
- Response time: ~3-5s
- Token usage: ~2000-3000

- [ ] **Step 4: Document results**

Create notes on actual token usage and response times compared to expectations.

---

## Implementation Summary

**Expected Improvements:**
- Average token consumption: -62% (8000 → 3000)
- Status query latency: -80% (5s → 1s)  
- Planning latency: -62% (8s → 3s)
- Creation latency: -33% (15s → 10s)

**Key Design Principles:**
1. Agent remains autonomous - phase only determines available context
2. Phase inference based on observable state, not hardcoded rules
3. Tools filtered per phase but agent chooses freely within that set
4. Memory loading scales to phase needs (minimal → full)
5. This is context engineering, NOT workflow

**Files Created:**
- PhaseInference.cs - Intent classification and phase inference
- PhaseContextBuilder.cs - Phase-specific context preparation

**Files Modified:**
- AgentCore.cs - Added phase enums
- AgentToolRegistry.cs - Added phase-based tool filtering
- AgentRuntime.cs - Integrated phase inference
- Program.cs - Registered new services

---
