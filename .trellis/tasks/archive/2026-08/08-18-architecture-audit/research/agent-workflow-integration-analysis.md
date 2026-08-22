# Agent 与 Workflow 集成架构深度审计

**审计日期**: 2026-08-18  
**审计范围**: Agent 层与 Production Workflow 的接口设计与集成路径  
**审计方法**: 读取实际代码，追踪调用链，分析数据流

---

## 执行摘要

天命小说系统存在**两套并行但未完全打通的架构**：

1. **新目标工作流架构（Target Architecture）**：`Agent/Tianming.NovelAgent.*` + `Web/NovelAgentWeb/Services/Goals/`
2. **遗留对话代理架构（Legacy Agent）**：`Web/NovelAgentWeb/Support/AgentCore.cs` + 工具调用系统

**核心问题**：两套架构在概念层面存在 **Proposal → Goal → Production** 的设计意图，但在代码实现中**接口不完整**，导致：
- Agent 对话层无法直接触发 Goal 确认
- Goal Workflow 缺少从 Agent 自然对话到 CommitmentAssessment 的标准路径
- Production 启动依赖手动 API 调用，Agent 工具系统未集成

---

## 1. 天命小说内核（Production Workflow）实际架构

### 1.1 核心入口

**主控制器**: `Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs`

```
POST /api/goals/workflow/confirm
  ↓
CreativeGoalService.SubmitAsync()
  ↓
BookProductionService.InitializeAsync()
  ↓
GoalCompiler.CompileAsync()
  ↓
KernelTaskScheduler (PostgreSQL-based)
  ↓
KernelTaskExecutionRouter.ExecuteAsync()
```

**关键发现**：整书生产的启动流程**完整且可运行**，但**只能通过 HTTP API 触发**。

### 1.2 数据模型

**Domain 层（DDD 风格）**: `Agent/Tianming.NovelAgent.Domain/`

```
GoalProposal (提案)
  ├─ Status: Draft → Proposed → Confirmed
  ├─ Contract: GoalContract
  └─ Confirm() → 状态迁移到 Confirmed

CreativeGoal (目标)
  ├─ Status: Confirmed → Active → Completed/Cancelled
  ├─ Revisions: List<GoalRevision>
  └─ Activate() → 状态迁移到 Active

Production (生产实例)
  ├─ Status: Planned → Running → AwaitingAcceptance → Completed
  ├─ Graph: TaskGraph
  └─ Start() → 状态迁移到 Running

KernelTask (执行任务)
  ├─ Status: Pending → Ready → Leased → Running → Succeeded
  ├─ Definition: TaskNodeDefinition
  └─ Claim() → 租约管理
```

**关键发现**：Domain 层设计**清晰且完整**，但 `GoalProposal` 实体在 Application 层**几乎未使用**。

### 1.3 工作流编排逻辑

**GoalCompiler** (`Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs:36-185`)

```csharp
public async Task<TaskGraphDefinition> CompileAsync(string goalId, ...)
{
    var goal = await _db.CreativeGoals.SingleOrDefaultAsync(...);
    var range = ParseBookRange(goal.TargetChapterRangeJson);
    var batch = await _bookProductions.GetCurrentBatchAsync(goal.Id, ...);
    
    // 构建 DAG 任务图
    var nodes = BuildSkeleton(range.Start, range.End);
    
    // 生成 KernelTask 实例
    _db.KernelTasks.AddRange(nodes.Select(node => new KernelTask { ... }));
    
    return new TaskGraphDefinition(goal.Id, nextVersion, nodes);
}
```

**Task Graph 结构** (每章的任务依赖链):

```
freeze-baselines
  ↓
analyze-creative-requirements
  ↓
compile-batch-plan
  ↓
[对每章] chapter-N-plan
  ↓
chapter-N-context
  ↓
chapter-N-write (tianming_writing kernel)
  ↓
chapter-N-continuity-review
  ↓
chapter-N-literary-review
  ↓
chapter-N-directed-rework
  ↓
chapter-N-continuity-summary
  ↓
[全批次] batch-impact-analysis
  ↓
acceptance-gate (HumanGate 或 AgentPolicy)
  ↓
prefix-merge
```

**关键发现**：任务图生成逻辑**完整**，支持依赖管理、并行执行、人工门禁和自动验收。

### 1.4 Worker 调度逻辑

**PostgresKernelTaskScheduler** (`Web/NovelAgentWeb/Services/Goals/PostgresKernelTaskScheduler.cs`)

- 使用 PostgreSQL advisory locks 实现分布式任务租约
- 每个 Worker 调用 `ClaimNextAsync()` 获取 Ready 状态的任务
- 执行完毕后调用 `CompleteAsync()` 或 `FailAsync()`

**KernelTaskExecutionRouter** (`Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs:33-77`)

```csharp
public async Task<KernelTaskExecutionResult> ExecuteAsync(KernelTaskClaim claim, ...)
{
    var context = await _contexts.BuildKernelExecutionAsync(claim, ...);
    var output = await _registry.GetRequired(claim.KernelName).ExecuteAsync(context, ...);
    
    var safePoint = await _goalControl.ReachSafePointAsync(claim, output.Artifacts, ...);
    if (safePoint.Disposition != Continue) return Paused/Canceled/BudgetExceeded;
    
    var adoption = await _reducer.ApplyAsync(claim, output.Artifacts, ...);
    return new KernelTaskExecutionResult(adoption.ArtifactIds);
}
```

**关键发现**：Kernel 执行框架**健壮**，支持暂停、预算控制、失败重试和 Artifact 管理。

---

## 2. 新 Agent 层（Conversation Agent）实际架构

### 2.1 核心入口

**主控制器**: `Web/NovelAgentWeb/Controllers/AgentController.cs`

```
POST /api/agent/chat
  ↓
AgentTurnCoordinator.HandleAsync()
  ↓
TargetArchitectureDirector.TryHandleAsync()
  ↓
CommitmentAssessmentService.AssessAsync()
  ↓
LLM 判断 DialogueCommitmentState
```

**关键发现**：Agent 对话系统**只到达 CommitmentAssessment 阶段**，未实现"确认后自动提交 Goal"的逻辑。

### 2.2 Agent 决策流程

**TargetArchitectureDirector** (`Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs:37-117`)

```csharp
public async Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, ...)
{
    var context = await _contexts.BuildAsync(new AgentContextRequest(...));
    
    // 调用 LLM 评估对话是否构成明确承诺
    var assessment = await _commitments.AssessAsync(new CommitmentAssessmentRequest(...));
    
    // 返回评估结果给前端
    return ReplyAsync(session, BuildReply(assessment), BuildSuggestions(assessment), ...);
}
```

**CommitmentAssessment 状态机**:

```
Exploring (探讨中)
  ↓
Proposed (已提议)
  ↓
Committed (已承诺) → RequiresConfirmation = true/false
  ↓
[缺失环节] → 应该调用 CreativeGoalService.SubmitAsync()
```

**关键发现**：`TargetArchitectureDirector` **只生成 ProposedContract，但不提交**。用户需要手动调用 `POST /api/goals/workflow/confirm` 才能真正启动 Production。

### 2.3 Agent Proposal 数据结构

**CommitmentAssessment** (`Web/NovelAgentWeb/Services/Goals/CommitmentAssessment.cs`)

```csharp
public sealed record CommitmentAssessment(
    DialogueCommitmentState State,        // Exploring/Proposed/Committed
    GoalAuthorizationKind Authorization,  // None/ImplicitConsent/ExplicitAction
    double Confidence,
    bool RequiresConfirmation,
    string Rationale,
    CreativeGoalContract? ProposedContract  // ← 这是 DTO，不是 Domain GoalProposal
);
```

**CreativeGoalContract** (DTO):

```csharp
public sealed record CreativeGoalContract(
    string GoalType,
    string CollaborationMode,
    string HumanReadableObjective,
    string TargetChapterRangeJson,
    List<string> SuccessCriteria,
    List<string> MustPreserve,
    List<string> MustHappen,
    List<string> MustNotChange,
    string AcceptancePolicyJson,
    string ReworkPolicyJson,
    string ExecutionStrategy,
    string BookPlanJson
);
```

**关键发现**：`CreativeGoalContract` 是一个**扁平 DTO**，未映射到 Domain 层的 `GoalProposal` 实体。

---

## 3. 接口设计当前状态分析

### 3.1 已实现的接口

#### ✅ Goal → Production 转换（完整）

**入口**: `CreativeGoalService.SubmitAsync()` (`Web/NovelAgentWeb/Services/Goals/CreativeGoalService.cs:29-131`)

```csharp
public async Task<GoalSubmissionResult> SubmitAsync(
    CreateCreativeGoalCommand command,
    CommitmentAssessment assessment,
    ...)
{
    // 1. 验证 assessment.State == Committed
    // 2. 验证 assessment.RequiresConfirmation == false
    // 3. 创建 CreativeGoal 实体（状态: committed）
    // 4. 创建 GoalContextSnapshot（冻结基线）
    // 5. 调用 BookProductionService.InitializeAsync()
    //    → 创建 BookProduction 实体（状态: running）
    //    → 创建第一个 ProductionBatch
    // 6. 返回 GoalId
}
```

**调用路径**: `POST /api/goals/workflow/confirm` → `SubmitAsync()` → `InitializeAsync()`

**关键发现**：这条路径**完整且可用**，但只能通过**前端显式调用 API** 触发。

#### ✅ Production → KernelTask 编译（完整）

**入口**: `GoalCompiler.CompileAsync()` (`Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs:36-185`)

```csharp
public async Task<TaskGraphDefinition> CompileAsync(string goalId, ...)
{
    var goal = await _db.CreativeGoals.SingleOrDefaultAsync(...);
    var batch = await _bookProductions.GetCurrentBatchAsync(goal.Id, ...);
    var nodes = BuildSkeleton(batch.StartChapterNumber, batch.EndChapterNumber);
    
    // 持久化任务图
    _db.TaskGraphVersions.Add(graphVersion);
    _db.KernelTasks.AddRange(...);
    
    await _bookProductions.BindCompiledBatchAsync(goal.Id, graphVersion.Id, branch.Id, ...);
    return definition;
}
```

**调用时机**: `SubmitAsync()` 后自动调用 → `CompileAsync()` 生成初始任务图

**关键发现**：编译逻辑**健壮**，支持增量重编译（Revision）和任务重用。

### 3.2 缺失的接口

#### ❌ Agent Dialogue → Goal Submission（断裂）

**现状**:

```
TargetArchitectureDirector.TryHandleAsync()
  ↓ 返回 assessment (含 ProposedContract)
  ↓
[断点] 用户必须手动调用前端"确认"按钮
  ↓
POST /api/goals/workflow/confirm
  ↓
CreativeGoalService.SubmitAsync()
```

**根因**:

1. `TargetArchitectureDirector` 只负责**对话评估**，不负责**执行提交**
2. `CommitmentAssessmentService` 返回的 `assessment` 是**临时对象**，未持久化到 `GoalProposal` 实体
3. 前端需要**再次构造** `CommitCreativeGoalRequest`，重复传递 Contract 数据

**影响**:

- AC-5 阻塞：Agent 无法"自然对话后自动启动 Production"
- AC-8 阻塞：需要手动点击确认按钮，体验割裂

#### ❌ GoalProposal Domain 实体未使用

**Domain 层定义** (`Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs`):

```csharp
public sealed class GoalProposal
{
    public GoalProposalStatus Status { get; private set; }  // Draft/Proposed/Confirmed
    public GoalContract Contract { get; }
    
    public void Propose() { ... }
    public void Confirm() { ... }
    public void Reject(string reason) { ... }
}
```

**Application 层使用情况**: `grep -r "GoalProposal" Web/NovelAgentWeb/` → **仅在 Domain 层，Application 层未引用**

**根因**:

- `CreativeGoalService` 直接创建 `CreativeGoal` 实体（数据库表），跳过了 `GoalProposal` 状态机
- `CommitmentAssessment` 中的 `ProposedContract` 是 DTO，未映射到 `GoalProposal`

**影响**:

- Domain 层的提案审批流程（Draft → Proposed → Confirmed）**形同虚设**
- 无法追溯"用户何时同意了哪个版本的提案"
- 缺少 Proposal 审计日志

#### ❌ Agent 工具系统未集成 Goal Workflow

**现有 Agent 工具** (`Web/NovelAgentWeb/Support/AgentCore.cs`):

- 定义了 `AgentToolDefinition`、`AgentToolCall`、`AgentToolExecutionResult`
- 支持工具发现、参数验证、副作用声明
- 但工具注册表中**没有**"提交 CreativeGoal"或"启动 Production"的工具

**根因**:

- Agent 工具系统面向**遗留 StoryBible 工作流**（`NovelAgentRun`、`MacroCandidate`）
- 新 Goal Workflow 独立演进，未暴露工具接口给 Agent

**影响**:

- Agent 无法通过工具调用启动 Production
- 需要依赖 HTTP API 外部调用，破坏了 Agent 的自治性

---

## 4. 接口不清晰的根因分析

### 4.1 架构演进导致双轨并行

**时间线推断**（从 Migration 文件和命名规范分析）:

1. **Phase 1 (2026-06)**：遗留 Agent 系统（`AgentCore.cs`、`NovelAgentRun`）
2. **Phase 2 (2026-07-08)**：引入 Target Architecture（`TargetArchitectureDirector`、`CommitmentAssessment`）
3. **Phase 3 (2026-08-03)**：统一架构迁移（`20260803040345_UnifyAgentProductionArchitecture`）

**关键 Migration**: `Web/NovelAgentWeb/Migrations/20260802070000_UnifyAgentProductionArchitecture.cs`

- 说明：代码库正在进行**架构统一**，但**尚未完成**

### 4.2 Domain 层与 Application 层脱节

**Domain 层**（DDD 风格）:

- 定义了完整的 `GoalProposal` → `CreativeGoal` → `Production` 生命周期
- 状态迁移通过 Domain 方法封装（`Propose()`、`Confirm()`、`Activate()`）

**Application 层**（CQRS 风格）:

- 直接操作 EF Core 实体（`CreativeGoal`、`BookProduction`）
- 未调用 Domain 方法，绕过了状态机

**根因**:

- Domain 层设计完成，但 Application 层**未重构**以使用 Domain 聚合根
- 存在"新瓶装旧酒"现象：Domain 接口定义了，但 Application 仍用老方式

### 4.3 两套 DbContext 导致跨边界查询困难

**代码证据**:

- `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs` (主 DbContext)
- `Agent/Tianming.NovelAgent.Infrastructure/Persistence/` (独立 Repository)

**影响**:

- Agent 层查询 Goal/Production 状态时，需要**跨 DbContext**
- 无法在单个事务中完成"Agent 对话 → Goal 提交 → Production 启动"

**根因**:

- Clean Architecture 边界过严：Domain 层完全隔离，Application 层未提供统一查询接口

---

## 5. 接口设计建议方案

### 5.1 短期方案：打通 Agent → Goal 提交路径

**目标**：让 Agent 对话系统能够直接触发 Goal 确认，无需用户点击"确认"按钮。

**实现步骤**:

#### Step 1: 在 `TargetArchitectureDirector` 中集成提交逻辑

修改 `TryHandleAsync()`:

```csharp
public async Task<AgentForegroundTurnResult> TryHandleAsync(...)
{
    var assessment = await _commitments.AssessAsync(...);
    
    // 新增：如果用户明确授权且不需要二次确认，直接提交
    if (assessment.State == DialogueCommitmentState.Committed &&
        !assessment.RequiresConfirmation &&
        assessment.Authorization == GoalAuthorizationKind.ImplicitConsent &&
        assessment.ProposedContract != null)
    {
        var command = MapToCreateGoalCommand(assessment.ProposedContract, session);
        var submission = await _goals.SubmitAsync(command, assessment, ct);
        
        if (submission.Status == GoalSubmissionStatus.Created)
        {
            var graph = await _compiler.CompileAsync(submission.GoalId, ct);
            return AgentForegroundTurnResult.Reply(..., goalId: submission.GoalId, graph: graph);
        }
    }
    
    return ReplyAsync(session, BuildReply(assessment), ...);
}
```

**优点**:
- 最小改动，复用现有 `SubmitAsync()` 逻辑
- 解除 AC-5/AC-8 阻塞

**缺点**:
- `TargetArchitectureDirector` 承担了提交职责，违反单一职责原则
- 仍未使用 Domain 层的 `GoalProposal` 实体

#### Step 2: 添加 Agent 工具 `ConfirmCreativeGoal`

注册工具到 Agent Runtime:

```csharp
public sealed class ConfirmCreativeGoalTool : IAgentTool
{
    public string Name => "confirm_creative_goal";
    
    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolCall call, ...)
    {
        var proposalId = call.Arguments["proposal_id"];
        var totalCostLimit = decimal.Parse(call.Arguments["total_cost_limit"]);
        
        var assessment = await LoadAssessmentAsync(proposalId, ...);
        var command = new CreateCreativeGoalCommand(assessment.ProposedContract, totalCostLimit, ...);
        var submission = await _goals.SubmitAsync(command, assessment, ...);
        
        return new AgentToolExecutionResult
        {
            Success = submission.Status == GoalSubmissionStatus.Created,
            Message = $"Creative Goal {submission.GoalId} 已确认并启动。",
            Artifact = new AgentToolArtifact { ArtifactId = submission.GoalId, ... }
        };
    }
}
```

**优点**:
- Agent 可以通过工具调用主动提交 Goal
- 符合 Agent 工具系统的现有架构

**缺点**:
- 需要持久化 `CommitmentAssessment`（当前是临时对象）

### 5.2 中期方案：启用 Domain 层 `GoalProposal` 实体

**目标**：让 Application 层使用 Domain 聚合根，而不是直接操作 EF 实体。

#### Step 1: 持久化 `GoalProposal`

添加 `GoalProposals` 表到 `NovelAgentDbContext`:

```csharp
public DbSet<GoalProposalEntity> GoalProposals { get; set; }

public sealed class GoalProposalEntity
{
    public string Id { get; set; }
    public string UserId { get; set; }
    public string ProjectId { get; set; }
    public string SourceSessionId { get; set; }
    public string Status { get; set; }  // Draft/Proposed/Confirmed
    public string ContractJson { get; set; }
    public string DecisionReason { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

#### Step 2: 修改 `CommitmentAssessmentService` 持久化提案

```csharp
public async Task<CommitmentAssessment> AssessAsync(...)
{
    var assessment = await _model.AssessAsync(...);
    
    if (assessment.State == DialogueCommitmentState.Proposed &&
        assessment.ProposedContract != null)
    {
        var proposal = new GoalProposalEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = _currentUser.GetUserId(),
            ProjectId = request.ProjectId,
            SourceSessionId = request.SessionId,
            Status = "Proposed",
            ContractJson = JsonSerializer.Serialize(assessment.ProposedContract),
            CreatedAt = DateTime.UtcNow
        };
        _db.GoalProposals.Add(proposal);
        await _db.SaveChangesAsync(...);
        
        return assessment with { ProposalId = proposal.Id };
    }
    
    return assessment;
}
```

#### Step 3: `CreativeGoalService` 基于 Proposal 创建 Goal

```csharp
public async Task<GoalSubmissionResult> SubmitAsync(string proposalId, decimal totalCostLimit, ...)
{
    var proposalEntity = await _db.GoalProposals.SingleAsync(p => p.Id == proposalId, ...);
    var proposal = MapToDomain(proposalEntity);
    
    // 使用 Domain 方法
    proposal.Confirm();
    
    var goal = CreativeGoal.Confirm(Guid.NewGuid().ToString("N"), proposal, revision);
    
    // 持久化 Domain 事件
    await _goalRepository.SaveAsync(goal, ...);
    await _bookProductions.InitializeAsync(goal, ...);
    
    return new GoalSubmissionResult(GoalSubmissionStatus.Created, goal.Id);
}
```

**优点**:
- 完整使用 Domain 层状态机
- 可审计用户确认了哪个版本的 Proposal
- 支持"修改提案后重新确认"流程

**缺点**:
- 需要数据库 Migration
- Application 层需要较大重构

### 5.3 长期方案：统一 Agent 与 Workflow 的执行模型

**目标**：Agent 对话层和 Production Workflow 共享同一个执行引擎。

#### 架构愿景

```
用户对话
  ↓
Agent Conversation Runtime (LLM + 工具调用)
  ↓
[工具] discover_creative_goal → 生成 GoalProposal
  ↓
[工具] confirm_creative_goal → 创建 CreativeGoal
  ↓
Goal Orchestrator (统一调度器)
  ↓
Production Workflow (KernelTask DAG)
  ↓
Kernel Execution (tianming_writing, continuity_review, ...)
  ↓
[反馈] Agent SSE Event → 用户界面实时更新
```

#### 关键改动

1. **Agent 工具注册表**暴露 Goal/Production 能力:
   - `discover_creative_goal`
   - `confirm_creative_goal`
   - `query_production_status`
   - `pause_production`
   - `revise_goal`

2. **Goal Orchestrator** 统一管理 Agent 和 Workflow 生命周期:
   - Agent 对话阶段：使用 LLM 生成 Proposal
   - Production 阶段：切换到 KernelTask 调度器
   - 反馈阶段：通过 SSE 实时推送进度

3. **Event Sourcing** 记录所有状态变更:
   - `GoalProposalCreated`
   - `GoalConfirmed`
   - `ProductionStarted`
   - `BatchCompleted`

**优点**:
- Agent 和 Workflow 无缝集成
- 完整的审计日志
- 支持 CQRS 和 Event Sourcing

**缺点**:
- 架构变更巨大
- 需要数月重构周期

---

## 6. 解除 AC-5/AC-8 阻塞的具体路径

### AC-5: Agent 无法自然对话后自动启动 Production

**根因**：`TargetArchitectureDirector` 只评估承诺状态，未实现提交逻辑。

**解决方案**（优先级排序）:

#### 方案 A（最快）：前端自动触发确认 API

- 前端检测到 `assessment.State == Committed && !assessment.RequiresConfirmation`
- 自动调用 `POST /api/goals/workflow/confirm`
- 工作量：1-2 小时（纯前端改动）

#### 方案 B（推荐）：Director 集成提交逻辑

- 修改 `TargetArchitectureDirector.TryHandleAsync()`
- 条件满足时调用 `CreativeGoalService.SubmitAsync()`
- 工作量：4-6 小时（后端改动 + 测试）

#### 方案 C（最佳）：Agent 工具 `ConfirmCreativeGoal`

- 添加工具到 Agent Runtime
- Agent 决策时自主调用工具
- 工作量：8-12 小时（工具注册 + 持久化 Proposal）

**推荐路径**：**方案 A + 方案 B 并行**

- 方案 A 立即解除阻塞（今天完成）
- 方案 B 提供正式后端支持（本周完成）
- 方案 C 作为下一迭代改进（下周规划）

### AC-8: Proposal → Goal 转换路径未定义

**根因**：`GoalProposal` Domain 实体未被 Application 层使用。

**解决方案**:

#### 阶段 1（本周）：建立 DTO → Command 映射

```csharp
// 在 CreativeGoalService 中添加
public CreateCreativeGoalCommand MapFromAssessment(
    CommitmentAssessment assessment,
    string sessionId,
    decimal totalCostLimit,
    string idempotencyKey)
{
    return new CreateCreativeGoalCommand(
        assessment.ProposedContract!,
        sessionId,
        totalCostLimit,
        idempotencyKey
    );
}
```

- 工作量：2 小时

#### 阶段 2（下周）：持久化 `GoalProposal` 实体

- 添加 `GoalProposals` 表
- `CommitmentAssessmentService` 保存 Proposed 状态
- `CreativeGoalService.SubmitAsync()` 基于 ProposalId 创建 Goal
- 工作量：1 天（Migration + 重构）

#### 阶段 3（下下周）：启用 Domain 层状态机

- 使用 `GoalProposal.Confirm()` 方法
- 使用 `CreativeGoal.Confirm(proposal)` 工厂方法
- 移除 Application 层直接操作 EF 实体
- 工作量：2-3 天（Application 层重构）

---

## 7. 总结

### 现状

- **Production Workflow** (Goal → Production → KernelTask)：**架构完整，可运行**
- **Agent Conversation**：**只到 Proposal 阶段，未集成提交**
- **Domain 层**：**设计优雅，但未被 Application 层使用**

### 根因

1. **架构演进中断**：正在进行统一架构迁移（`UnifyAgentProductionArchitecture`），但未完成
2. **双轨并行**：遗留 Agent 系统和新 Goal Workflow 独立演进
3. **Domain 层形同虚设**：Application 层绕过 Domain 聚合根，直接操作数据库

### 最快解除阻塞路径（1-2 天）

1. **今天**：前端检测 `Committed` 状态时自动调用确认 API（方案 A）
2. **明天**：后端 `TargetArchitectureDirector` 集成提交逻辑（方案 B）
3. **本周五**：添加 `CreateGoalCommand` 映射工具（AC-8 阶段 1）

### 长期架构目标（1-2 月）

1. 持久化 `GoalProposal` 实体并启用 Domain 状态机
2. 添加 Agent 工具系统集成 Goal Workflow
3. 统一 Agent Runtime 和 Production Orchestrator
4. 引入 Event Sourcing 记录完整审计日志

---

## 附录：关键文件清单

### Production Workflow 核心

- `Agent/Tianming.NovelAgent.Domain/Goals/GoalContract.cs` - Domain 契约定义
- `Agent/Tianming.NovelAgent.Domain/Goals/GoalProposal.cs` - Domain 提案实体
- `Agent/Tianming.NovelAgent.Domain/Goals/CreativeGoal.cs` - Domain 目标聚合根
- `Agent/Tianming.NovelAgent.Domain/Production/Production.cs` - Domain 生产实例
- `Agent/Tianming.NovelAgent.Domain/Production/KernelTask.cs` - Domain 任务实体
- `Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs` - HTTP API 控制器
- `Web/NovelAgentWeb/Services/Goals/CreativeGoalService.cs` - Goal 提交服务
- `Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs` - 任务图编译器
- `Web/NovelAgentWeb/Services/Goals/BookProductionService.cs` - Production 管理服务
- `Web/NovelAgentWeb/Services/Goals/PostgresKernelTaskScheduler.cs` - 任务调度器
- `Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs` - 任务执行路由

### Agent Conversation 核心

- `Web/NovelAgentWeb/Controllers/AgentController.cs` - Agent HTTP API
- `Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs` - 对话管理器
- `Web/NovelAgentWeb/Services/Goals/CommitmentAssessmentService.cs` - 承诺评估服务
- `Web/NovelAgentWeb/Services/Goals/CommitmentAssessment.cs` - 评估结果 DTO
- `Web/NovelAgentWeb/Support/AgentCore.cs` - Agent 工具系统定义

### 数据模型

- `Web/NovelAgentWeb/Data/Entities/CreativeGoal.cs` - Goal EF 实体
- `Web/NovelAgentWeb/Data/Entities/BookProduction.cs` - Production EF 实体
- `Web/NovelAgentWeb/Data/Entities/KernelTask.cs` - Task EF 实体
- `Web/NovelAgentWeb/Data/Entities/CandidateChapter.cs` - 候选章节实体
