# Agent-Workflow 集成交接摘要

> **⚠️ 历史材料（2026-08-21 标记）**：本文档写于本任务早期，其中“固定链式触发、自动提交 Goal”等方案已被权威 PRD/design 废弃。当前实现以 `.trellis/tasks/08-18-architecture-audit/prd.md`、`design.md`、`implement.md` 和 `audit-report.md` 为准；最终交接以 audit-report.md §7 交付物清单为准。本文仅保留作决策过程记录。

**任务背景**：完成 Agent Conversation 层与 Production Workflow 的集成接口，解除 AC-5/AC-8 阻塞。

**当前状态**：两个子系统已独立完成，但集成接口断裂，用户无法通过自然对话启动生产工作流。

---

## 1. 最终目标

### 核心目标

**实现用户通过自然对话启动整书生产工作流**，无需手动点击确认按钮。

**用户体验**：
```
用户："我要写一部玄幻小说，主角叫张三，100 章"
  ↓
Agent 对话收集需求（题材、大纲、风格等）
  ↓
Agent 判断用户承诺状态（LLM 评估）
  ↓
Agent 自动提交 Creative Goal 并启动 Production Workflow
  ↓
用户收到反馈："已启动整书生产任务，目标章节 100 章"
  ↓
用户可查看实时进度、暂停生产、审核章节草稿
```

### 技术目标

1. **持久化 GoalProposal 实体**：从 Agent 对话生成的提案保存到数据库，可审计、可追溯
2. **Agent 工具集成**：注册 `confirm_creative_goal` 工具，Agent 可自主调用
3. **Domain 层启用**：Application 层使用 Domain 聚合根（`GoalProposal.Confirm()`），不再绕过状态机
4. **端到端路径打通**：Agent 对话 → Proposal 持久化 → Agent 调用工具 → Goal 创建 → Production 启动 → 任务图编译 → Worker 执行

---

## 2. 已确认决策

### 决策 1：选择方案 C（Agent 工具 + 持久化 Proposal）

**理由**：
- 方案 A（前端自动触发）是临时方案，Agent 无法自主决策
- 方案 B（Director 集成提交）违反单一职责原则
- **方案 C 是长期正确的架构**：完整审计日志、Agent 工具系统集成、符合 DDD 最佳实践

### 决策 2：保持现有架构设计

**确认以下设计不需要简化**：
- ✅ Agent + Workflow 双引擎模型
- ✅ Proposal → Goal → Production 三层抽象
- ✅ DAG 任务图编排
- ✅ Domain 驱动设计（GoalProposal → CreativeGoal → Production 状态机）
- ✅ 双 DbContext（AgentDbContext 和 ProductionDbContext）

### 决策 3：Agent 工具调用时机

**Agent 自主调用工具的条件**：
```csharp
if (assessment.State == DialogueCommitmentState.Committed &&
    !assessment.RequiresConfirmation &&
    assessment.Authorization == GoalAuthorizationKind.ImplicitConsent &&
    assessment.ProposalId != null)
{
    // 调用 confirm_creative_goal 工具
}
```

**不自动调用的情况**：
- `RequiresConfirmation == true`：用户需要显式确认（涉及高成本、长时间任务）
- `Authorization == ExplicitAction`：用户需要主动说"开始生产"

### 决策 4：数据库设计

**新增 `goal_proposals` 表**：
```sql
CREATE TABLE goal_proposals (
    id VARCHAR(32) PRIMARY KEY,
    user_id VARCHAR(32) NOT NULL,
    project_id VARCHAR(32) NOT NULL,
    source_session_id VARCHAR(32) NOT NULL,
    status VARCHAR(20) NOT NULL,  -- Draft/Proposed/Confirmed/Rejected
    contract_json TEXT NOT NULL,
    decision_reason TEXT,
    created_at TIMESTAMP NOT NULL,
    confirmed_at TIMESTAMP,
    CONSTRAINT fk_user FOREIGN KEY (user_id) REFERENCES users(id),
    CONSTRAINT fk_project FOREIGN KEY (project_id) REFERENCES projects(id),
    CONSTRAINT fk_session FOREIGN KEY (source_session_id) REFERENCES chat_sessions(id)
);

CREATE INDEX idx_goal_proposals_user_project ON goal_proposals(user_id, project_id);
CREATE INDEX idx_goal_proposals_session ON goal_proposals(source_session_id);
```

### 决策 5：不修改现有 Production Workflow

**Production Workflow 已完整且稳定**：
- `CreativeGoalService.SubmitAsync()` 逻辑保持不变
- `GoalCompiler.CompileAsync()` 逻辑保持不变
- `KernelTaskScheduler` 和 `KernelTaskExecutionRouter` 保持不变

**只修改接口层**：
- 增加 Proposal 持久化逻辑
- 增加 Agent 工具注册
- 修改 `TargetArchitectureDirector` 调用工具

---

## 3. 验收标准

### AC-1: Proposal 持久化成功

**测试场景**：
```
Given: 用户在 Agent 对话中提出创作意图
When: CommitmentAssessmentService 判断 State == Proposed
Then: GoalProposal 记录保存到 goal_proposals 表
  And: 返回的 assessment.ProposalId 不为空
  And: Proposal.Status == "Proposed"
```

**验证命令**：
```sql
SELECT * FROM goal_proposals 
WHERE source_session_id = '<session_id>' 
  AND status = 'Proposed';
```

### AC-2: Agent 工具注册成功

**测试场景**：
```
Given: Agent Runtime 启动
When: 查询可用工具列表
Then: 工具列表包含 "confirm_creative_goal"
  And: 工具定义包含必需参数（proposal_id, total_cost_limit）
  And: 工具声明副作用（side_effects: ["database_write", "workflow_start"]）
```

**验证命令**：
```csharp
var tools = await _toolRegistry.ListAvailableToolsAsync();
Assert.Contains(tools, t => t.Name == "confirm_creative_goal");
```

### AC-3: Agent 自主调用工具成功

**测试场景**：
```
Given: Proposal 已持久化（ProposalId = "abc123"）
  And: assessment.State == Committed
  And: assessment.RequiresConfirmation == false
When: TargetArchitectureDirector.TryHandleAsync() 执行
Then: Agent 自主调用 confirm_creative_goal 工具
  And: 工具参数包含 proposal_id = "abc123"
  And: 工具执行成功返回 goalId
```

**验证日志**：
```
[INFO] AgentToolExecutor: Executing tool 'confirm_creative_goal' with args { proposal_id: "abc123", total_cost_limit: 100.0 }
[INFO] ConfirmCreativeGoalTool: Goal abc123-goal created successfully
[INFO] TargetArchitectureDirector: Production workflow started, goalId=abc123-goal
```

### AC-4: Proposal 状态迁移正确

**测试场景**：
```
Given: Proposal 状态为 "Proposed"
When: Agent 工具 confirm_creative_goal 执行成功
Then: Proposal 状态更新为 "Confirmed"
  And: Proposal.confirmed_at 记录当前时间
  And: 数据库事务提交成功
```

**验证命令**：
```sql
SELECT status, confirmed_at FROM goal_proposals WHERE id = 'abc123';
-- 预期: status='Confirmed', confirmed_at IS NOT NULL
```

### AC-5: CreativeGoal 创建成功（关联 Proposal）

**测试场景**：
```
Given: Agent 工具调用成功
When: CreativeGoalService.SubmitAsync() 执行
Then: CreativeGoal 记录创建（status="confirmed"）
  And: Goal.source_proposal_id == ProposalId
  And: GoalContextSnapshot 冻结基线
  And: BookProduction 初始化（status="running"）
```

**验证命令**：
```sql
SELECT g.id, g.status, g.source_proposal_id, p.status as production_status
FROM creative_goals g
LEFT JOIN book_productions p ON p.goal_id = g.id
WHERE g.source_proposal_id = 'abc123';
-- 预期: goal.status='confirmed', production.status='running'
```

### AC-6: Production Workflow 启动成功

**测试场景**：
```
Given: CreativeGoal 创建成功
When: GoalCompiler.CompileAsync() 执行
Then: TaskGraphDefinition 生成成功
  And: KernelTasks 写入数据库（状态: Pending）
  And: 至少包含核心任务节点（freeze-baselines, analyze-creative-requirements, chapter-N-write）
```

**验证命令**：
```sql
SELECT task_name, status FROM kernel_tasks 
WHERE production_id = '<production_id>' 
ORDER BY sequence_number;
-- 预期: 包含 freeze-baselines, analyze-creative-requirements, chapter-1-write, ...
```

### AC-7: 端到端路径打通

**测试场景**：
```
Given: 新用户对话会话
When: 用户输入 "我要写一部玄幻小说，主角叫张三，100 章"
  And: Agent 对话收集需求（3-5 轮）
  And: Agent 判断用户承诺（Committed）
Then: Agent 自动启动 Production Workflow
  And: 返回 "已启动整书生产任务，目标章节 100 章"
  And: 用户可查看实时进度
  And: 无需手动点击确认按钮
```

**验证步骤**：
1. 前端发送对话消息 → `POST /api/agent/chat`
2. 检查返回的 `assessment.State == "Committed"`
3. 检查返回的 `metadata.goalId` 不为空
4. 查询 `GET /api/goals/workflow/status/{goalId}` 返回 `status == "running"`

### AC-8: Domain 层状态机启用

**测试场景**：
```
Given: Application 层代码
When: 检查 CreativeGoalService.SubmitAsync() 实现
Then: 必须调用 Domain 方法 GoalProposal.Confirm()
  And: 必须调用 Domain 工厂方法 CreativeGoal.CreateFromProposal()
  And: 不允许直接 new CreativeGoal(...) 绕过状态机
```

**代码审查检查点**：
```csharp
// ❌ 错误：绕过 Domain 层
var goal = new CreativeGoal { Status = "confirmed", ... };

// ✅ 正确：使用 Domain 方法
var proposal = await _proposalRepo.GetByIdAsync(proposalId);
proposal.Confirm();
var goal = CreativeGoal.CreateFromProposal(proposal, revision);
```

---

## 4. 非目标

### 不在本次任务范围内

1. **不修改 Production Workflow 逻辑**
   - KernelTask 编排、调度、执行逻辑保持不变
   - GoalCompiler 任务图生成逻辑保持不变
   - Kernel 执行框架（tianming_writing, continuity_review）保持不变

2. **不修改 CommitmentAssessment LLM 逻辑**
   - 承诺状态判断（Exploring/Proposed/Committed）逻辑保持不变
   - RequiresConfirmation 判断逻辑保持不变
   - 只增加 Proposal 持久化，不改变评估流程

3. **不引入 Event Sourcing**
   - 不增加事件总线、Outbox 模式
   - 不记录 Domain 事件到事件流
   - 简单的状态迁移（Proposed → Confirmed）就够了

4. **不重构整个 Application 层**
   - 只修改 TargetArchitectureDirector 和 CreativeGoalService
   - 其他 Service（BookProductionService, GoalCompiler）保持不变

5. **不修改前端 UI**
   - 前端仍然支持手动确认按钮（作为备用路径）
   - Agent 对话界面不需要新增 UI 元素

6. **不处理异常边缘场景**
   - 不处理"用户中途改变主意"
   - 不处理"LLM 误判承诺状态"
   - 不处理"Proposal 过期失效"
   - 这些留给后续迭代

---

## 5. 技术约束

### 约束 1：保持 Clean Architecture 边界

**要求**：
- Domain 层不依赖 Application 层
- Application 层不依赖 Infrastructure 层（只依赖接口）
- Agent 工具实现放在 Application 层（`Web/NovelAgentWeb/Services/Goals/Tools/`）

### 约束 2：使用现有依赖注入框架

**要求**：
- 使用 ASP.NET Core DI 容器
- 工具注册在 `Program.cs` 的 `ConfigureServices()` 中
- 不引入新的 IoC 容器（如 Autofac）

### 约束 3：数据库迁移向后兼容

**要求**：
- 新增 `goal_proposals` 表，不删除任何现有表
- 新增 `CreativeGoal.source_proposal_id` 字段（nullable），旧记录为 NULL
- Migration 必须可回滚（`Down()` 方法完整）

### 约束 4：不影响现有 API 兼容性

**要求**：
- `POST /api/goals/workflow/confirm` 仍然可用（前端手动确认路径）
- `POST /api/agent/chat` 返回格式保持不变（增加 `metadata.goalId` 字段）
- 不破坏前端现有调用

### 约束 5：使用现有测试框架

**要求**：
- 单元测试使用 xUnit + NSubstitute
- 集成测试使用 WebApplicationFactory
- 不引入新的测试框架（如 SpecFlow）

### 约束 6：代码风格一致性

**要求**：
- 遵循项目现有命名规范（PascalCase for public, camelCase for private）
- 使用 `sealed record` 定义 DTO
- 使用 `sealed class` 定义 Entity
- 异步方法命名为 `XxxAsync()`

---

## 6. 风险

### 风险 1：CommitmentAssessment 误判承诺状态

**描述**：LLM 可能误判用户意图，将 "我在考虑" 判断为 "Committed"，导致提前启动生产。

**影响**：用户未准备好就启动了耗时长、成本高的生产任务。

**缓解措施**：
- 保持 `RequiresConfirmation` 机制：高成本任务仍需用户显式确认
- 增加审计日志：记录每次承诺判断的 LLM 推理过程（`assessment.Rationale`）
- 允许用户撤销：增加 "取消生产" 功能（后续迭代）

**优先级**：中（通过 `RequiresConfirmation` 已部分缓解）

### 风险 2：Proposal 持久化与 Goal 创建的事务一致性

**描述**：如果 Proposal 持久化成功，但 Goal 创建失败，会导致孤儿 Proposal 记录。

**影响**：数据不一致，用户看到 "提案已确认" 但实际未启动生产。

**缓解措施**：
- 使用数据库事务：Proposal 确认和 Goal 创建在同一个事务中
- 幂等性保证：使用 `idempotencyKey` 防止重复创建
- 状态补偿：定时任务清理 "Confirmed 但没有关联 Goal" 的 Proposal

**优先级**：高（必须在实现时处理）

### 风险 3：Agent 工具调用死循环

**描述**：如果工具执行失败，Agent 可能重试调用，导致死循环或重复创建 Goal。

**影响**：系统资源耗尽，数据库写入大量重复记录。

**缓解措施**：
- 工具调用最大重试次数：3 次
- 幂等性保证：基于 ProposalId 创建 Goal，重复调用返回相同结果
- 工具执行超时：30 秒超时，防止长时间阻塞

**优先级**：中（通过幂等性已部分缓解）

### 风险 4：旧代码路径与新代码路径冲突

**描述**：如果旧的 "手动确认" 路径和新的 "自动确认" 路径同时触发，可能创建重复的 Goal。

**影响**：同一个 Proposal 启动了两次生产，浪费资源。

**缓解措施**：
- Proposal 状态检查：`SubmitAsync()` 执行前检查 Proposal 是否已 Confirmed
- 数据库唯一约束：`CREATE UNIQUE INDEX idx_goal_source_proposal ON creative_goals(source_proposal_id)`
- 前端禁用按钮：如果检测到 `assessment.ProposalId` 不为空且状态为 Confirmed，禁用确认按钮

**优先级**：高（必须在实现时处理）

### 风险 5：Domain 层重构影响现有功能

**描述**：启用 Domain 层状态机后，如果 Application 层依赖了旧的 EF 实体行为，可能导致回归问题。

**影响**：现有功能（如手动创建 Goal）失效。

**缓解措施**：
- 渐进式重构：先增加新路径（Proposal → Goal），保留旧路径（直接创建 Goal）
- Feature Flag 控制：通过配置开关新路径，默认关闭
- 回归测试：运行所有现有集成测试，确保无破坏性变更

**优先级**：高（必须在实现时处理）

---

## 7. 尚未解决的问题

### 问题 1：Proposal 审计日志的保留时长

**问题描述**：
- `goal_proposals` 表会持续增长
- 需要定义数据保留策略（保留多久？如何归档？）

**当前决策**：
- 暂不处理，等待产品决策
- 建议：保留 90 天，之后归档到冷存储

### 问题 2：用户如何查看历史 Proposal

**问题描述**：
- 用户可能想查看 "我之前提过哪些创作想法"
- 前端没有 "历史提案" 页面

**当前决策**：
- 本次任务不实现前端页面
- 数据已持久化，后续可增加 `GET /api/goals/proposals` API

### 问题 3：Proposal 修改后如何处理

**问题描述**：
- 如果 Agent 对话中用户修改了需求（"章节数改成 200"）
- 是创建新 Proposal 还是更新旧 Proposal？

**当前决策**：
- 创建新 Proposal（保持不可变性）
- 旧 Proposal 状态保持 "Proposed"，新 Proposal 为 "Proposed"
- 只有用户确认的 Proposal 才变为 "Confirmed"

### 问题 4：Agent 工具失败时如何反馈用户

**问题描述**：
- 如果 `confirm_creative_goal` 执行失败（如数据库连接失败）
- Agent 应该如何告知用户？

**当前决策**：
- 工具返回 `AgentToolExecutionResult.Success = false`
- Agent 根据 `result.Message` 生成自然语言回复："抱歉，启动生产失败，原因：..."
- 用户可以重试或手动点击确认按钮

### 问题 5：totalCostLimit 参数如何确定

**问题描述**：
- `confirm_creative_goal` 工具需要 `total_cost_limit` 参数
- Agent 应该如何决定这个值？（默认值？从用户设置读取？LLM 推断？）

**当前决策**：
- 短期：使用默认值 `100.0m`（从 `appsettings.json` 读取 `DefaultProductionCostLimit`）
- 长期：Agent 根据用户历史消费记录和项目规模推断

### 问题 6：如何处理 "用户在生产中途改变主意"

**问题描述**：
- Production 启动后，用户说 "等等，我想改章节数"
- 当前系统没有 "暂停 → 修改 Goal → 继续" 的流程

**当前决策**：
- 本次任务不处理
- 当前只支持 "取消当前生产 → 重新创建 Goal"
- 未来增加 `revise_goal` 工具（支持 Goal Revision）

### 问题 7：Agent 工具调用的权限控制

**问题描述**：
- `confirm_creative_goal` 会启动耗时长、成本高的任务
- 是否需要权限验证？（用户余额、配额、访问控制）

**当前决策**：
- 本次任务不增加新的权限检查
- 复用 `CreativeGoalService.SubmitAsync()` 现有的验证逻辑
- 未来增加 "用户配额检查" 和 "成本预估"

---

## 8. 下一步行动

### 立即行动（审计完成后）

1. **创建 Trellis 任务**：
   - 任务名称：`agent-workflow-integration`
   - 父任务：`novel-agent-framework-separation`（可选）
   - 状态：`planning`

2. **编写 PRD**：
   - 基于本摘要生成 `prd.md`
   - 包含用户故事、验收标准、非功能需求

3. **编写 design.md**：
   - 数据库设计（goal_proposals 表结构）
   - 类设计（ConfirmCreativeGoalTool、GoalProposalEntity）
   - 调用链设计（TargetArchitectureDirector → Tool → CreativeGoalService）

4. **编写 implement.md**：
   - 分阶段实施计划（Phase 1: Migration, Phase 2: Tool, Phase 3: Integration）
   - 验证命令和测试用例

5. **获取用户审批**：
   - 向用户展示交接摘要和 PRD
   - 确认技术方案无异议
   - 确认验收标准清晰

### 实施阶段（用户批准后）

6. **Phase 1: 数据库 Migration**（1 天）
   - 创建 `goal_proposals` 表
   - 增加 `creative_goals.source_proposal_id` 字段
   - 运行 Migration，验证向后兼容

7. **Phase 2: Proposal 持久化**（1 天）
   - 修改 `CommitmentAssessmentService.AssessAsync()`
   - 增加 `GoalProposalRepository`
   - 单元测试 + 集成测试

8. **Phase 3: Agent 工具注册**（1 天）
   - 实现 `ConfirmCreativeGoalTool`
   - 注册到 Agent Runtime
   - 单元测试

9. **Phase 4: Director 集成**（1 天）
   - 修改 `TargetArchitectureDirector.TryHandleAsync()`
   - 增加工具调用逻辑
   - 集成测试

10. **Phase 5: Domain 层启用**（1 天）
    - 修改 `CreativeGoalService.SubmitAsync()`
    - 使用 Domain 方法（`GoalProposal.Confirm()`）
    - 回归测试

11. **Phase 6: 端到端测试**（1 天）
    - 手动测试完整对话流程
    - 验证所有 8 个 AC
    - 性能测试（承诺判断延迟、工具调用延迟）

---

## 9. 成功指标

### 定量指标

- **AC 通过率**：8/8 个验收标准全部通过
- **集成测试覆盖率**：新增代码测试覆盖率 > 80%
- **对话流程完成时间**：从用户开始对话到 Production 启动 < 60 秒（含 LLM 推理）
- **工具调用成功率**：> 99%（幂等性保证，避免重复创建）

### 定性指标

- **用户体验流畅**：无需手动点击确认按钮，Agent 自动启动生产
- **代码质量**：符合 Clean Architecture 原则，Domain 层状态机启用
- **可维护性**：新增代码清晰，后续可扩展（如增加 `revise_goal` 工具）
- **向后兼容**：手动确认路径仍可用，前端无破坏性变更

---

**交接摘要完成**。下一步：创建 Trellis 任务和 PRD。
