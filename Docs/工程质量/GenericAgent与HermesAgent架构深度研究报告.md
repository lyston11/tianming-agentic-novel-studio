# GenericAgent 与 Hermes Agent 架构深度研究报告

> 研究对象：  
> - https://github.com/lsdefine/GenericAgent  
> - https://github.com/NousResearch/hermes-agent  
>
> 研究目的：不是复制它们的代码，而是提炼优秀 Agent 系统的架构边界、运行时模型、工具治理、记忆系统、任务调度和自进化机制，用来反推当前“天命 Agentic Novel Studio”小说 Agent 的重构方向。

## 0. 总结结论

这两个项目代表了两类优秀 Agent 的不同路线：

- **GenericAgent** 是“小内核、自进化、经验外化”的路线。它的核心代码非常少，Agent Loop 非常清晰，工具数量很少，但通过 SOP、工作记忆、长期记忆、计划文件、反射任务和 Subagent 协议，形成了持续成长的 Agent。
- **Hermes Agent** 是“工业级运行时治理”的路线。它有完整的会话运行时、工具注册与执行器、工具护栏、上下文工程、记忆管理、技能系统、跨平台网关、子 Agent 委托、调度任务、预算控制和中断机制。

这两个项目表面差异很大，但底层共识高度一致：

1. **Agent 主循环必须小而稳定**，不能把业务流程和补丁塞进 Runtime。
2. **状态必须外化**，长期任务不能只靠模型上下文或聊天历史记住。
3. **工具必须是受控能力**，不能模型输出什么就执行什么。
4. **用户输入不能直通业务工具**，必须先经过行为/意图仲裁。
5. **长任务必须有检查点、预算、恢复、验证和阻断机制**。
6. **记忆必须分层**，不同记忆有不同更新权限和生命周期。
7. **自进化靠 SOP/Skill/Memory 结晶**，不是无限扩展 if/else。
8. **子任务隔离上下文**，主 Agent 对子 Agent 输出仍需验证。

对当前小说 Agent 来说，最关键的问题不是“某个 Planner 判断错了”，而是：

```text
用户原话 -> Planner -> 小说业务工具参数
```

这条链路本身是错误的。

当用户说“我叫你生成的小说章节准备好了吗”，这句话应该被识别为 `status_query`，进入任务黑板查询；但当前架构允许它被当成 `PlanChapter(userGoal = 用户原话)`，于是用户的任务管理话术被当作创作素材。

这是 Agent 设计问题，不是关键词补丁问题。

建议的目标架构是：

```text
UserTurn
  -> Conversation Kernel / TurnIntent 仲裁
  -> Mission Blackboard / 小说任务黑板
  -> Novel Domain Planner / 小说领域规划器
  -> Guarded Writing Executor / 受控写作工具执行器
  -> Observation
  -> Reflect & Quality Gate
  -> Mission Patch / Memory Patch
  -> Reply
```

其中最应该立刻废弃的模式是：

```text
PlanChapter(userGoal = userMessage)
```

章节规划工具不应该接收原始用户输入，而应该接收结构化的：

```text
ChapterPlanningRequest:
  projectId
  missionId
  volumeId
  chapterId
  creativeBrief
  constraints
  sourceTurnIds
  currentStoryState
```

只有 `Conversation Kernel` 把用户输入转换成明确的 `creativeBrief` 或 `revisionRequest` 后，才允许进入创作工具。

---

## 1. GenericAgent 架构研究

### 1.1 项目定位

GenericAgent 的 README 明确提出：

- 极简 Agent 框架。
- 约 3K 行核心代码。
- 9 个原子工具。
- 约 100 行 Agent Loop。
- 通过任务执行沉淀 Skill/SOP，形成个人技能树。

它的核心不是“预置一堆业务能力”，而是：

```text
最小可执行内核 + 原子工具 + 分层记忆 + 自进化 SOP
```

这种设计非常适合长期成长型 Agent。

### 1.2 目录结构拆解

关键目录：

```text
agent_loop.py              # 极简 Agent Loop
agentmain.py               # Agent 入口、任务队列、反射模式、后台任务
ga.py                      # 工具实现、Handler、工作记忆注入
llmcore.py                 # 模型客户端、历史压缩、工具调用封装
assets/tools_schema.json   # 9 个原子工具定义
assets/sys_prompt.txt      # 系统提示词
memory/*.md                # SOP、记忆层、验证规则、计划规则
reflect/*.py               # 自主模式、Goal Mode、Scheduler、Worker
frontends/*                # CLI/TUI/IM/桌面前端
```

GenericAgent 的结构很有特点：主循环很薄，复杂度放在 SOP 和 handler 中，而不是放在核心循环里。

### 1.3 Agent Loop

`agent_loop.py` 中的核心循环可以抽象为：

```text
messages = [system, user]
while turn < max_turns:
    response = client.chat(messages, tools)
    tool_calls = response.tool_calls or no_tool
    for tool_call in tool_calls:
        outcome = handler.dispatch(tool_name, args)
        collect tool_results
        collect next_prompt
    if no next_prompt or should_exit:
        break
    messages = [{"role": "user", "content": next_prompt, "tool_results": tool_results}]
```

它的重要设计点：

1. **工具执行后只返回 StepOutcome**  
   工具本身不直接决定整个 Agent 的业务流程，只提供观察结果和下一轮提示。

2. **主循环不理解业务**  
   它不知道什么是“写代码”“读网页”“发消息”，只知道 LLM、工具、结果、下一轮。

3. **工具结果进入下一轮上下文**  
   工具结果不是直接裸返回给用户，而是回到 LLM 的下一步判断。

4. **没有工具时也走 no_tool 分支**  
   这保证普通回复也在统一循环内，而不是另开一条 Chat 兜底路径。

这点对我们很关键：当前小说 Agent 虽然已经试图做统一 Runtime，但 Runtime 里仍然混入大量小说业务判断，导致主循环越来越胖。

### 1.4 StepOutcome 设计

GenericAgent 工具返回 `StepOutcome`：

```text
data: 工具结构化结果
next_prompt: 给下一轮模型的提示
should_exit: 是否退出
```

这个结构非常干净。

它区分了：

- 工具结果是什么。
- 下一步让模型观察什么。
- 是否停止。

我们的 `AgentToolExecutionResult` 类似，但问题是工具结果和业务阶段、MissionPlan、前端展示、确认机制耦合太多。GenericAgent 的启发是：工具结果应该尽量原子化，业务状态更新由任务黑板/反思层处理。

### 1.5 Handler Dispatch

GenericAgent 的 `BaseHandler.dispatch` 通过方法名映射工具：

```text
do_code_run
do_file_read
do_file_patch
...
```

如果工具不存在，返回“未知工具”并让模型重新选择。

它的设计重点不是复杂注册系统，而是：

- 工具名和实现一一对应。
- 工具执行前后有 hooks。
- 工具执行输出被统一包裹。

这适合小内核。

对我们来说，小说 Agent 不一定要用这种动态方法映射，但需要保持同样原则：

- 工具必须是稳定能力边界。
- 工具名不能兼容性膨胀。
- 不应该保留旧工具入口然后 Runtime 里继续 Normalize。

例如 `GenerateChapter` 这种旧入口如果业务上已经禁止，就不应该继续作为“兼容重写”存在太久，否则模型和历史会话会继续污染新链路。

### 1.6 9 个原子工具

GenericAgent 的工具定义集中在 `assets/tools_schema.json`。

核心工具：

```text
code_run
file_read
file_write
file_patch
web_scan
web_execute_js
ask_user
update_working_checkpoint
start_long_term_update
```

这些工具共同特点：

- 原子。
- 通用。
- 可组合。
- 不包含业务流程。

它没有 `BuildWebsite`、`FixBug`、`DeployProject` 这种业务按钮。

我们的小说 Agent 当前有大量“业务半流程工具”：

```text
PlanStoryFoundation
PlanVolumeArc
PlanChapter
GenerateChapterWithChanges
ValidateChapterDraft
CommitValidatedChapter
...
```

小说领域当然需要领域工具，但需要拆清楚：

- **领域原子能力**：检索设定、构建上下文包、生成草稿、校验门禁、提交章节。
- **业务工作流**：写一本书、推进下一章、修复草稿、提交成稿。

工具应该偏前者，工作流应该由 Mission Kernel 组织。

### 1.7 工作记忆 update_working_checkpoint

GenericAgent 的 `update_working_checkpoint` 很关键。它是短期工作便签，每轮自动注入上下文。

规则：

- 长任务早中期使用。
- 子任务切换前使用。
- 多次失败后使用。
- 新任务时清理旧进度，但保留仍有效约束。
- 简单任务不滥用。
- 任务完成后不写它，改用长期记忆。

这解决的是 Agent 长任务漂移问题。

我们当前有 `WorkingMemory`、`MissionPlan`、`RecentObservations`，但缺少一个强制写入/读取的短期 checkpoint 协议。

建议引入小说 Agent 的 `WorkingCheckpoint`：

```text
WorkingCheckpoint:
  currentUserNeed
  currentBook
  currentChapterTask
  currentBlockingQuestion
  nonNegotiableConstraints
  nextExpectedAction
  staleWarning
```

并且每次开始新用户 turn 时，Conversation Kernel 必须先判断 checkpoint 是否仍适用。

### 1.8 分层记忆

GenericAgent 的记忆 SOP 很明确：

```text
L1: global_mem_insight.txt  极简索引
L2: global_mem.txt          全局事实
L3: memory/*.md             SOP/脚本/专项记录
L4: raw sessions            历史会话归档
```

最重要的公理：

```text
No Execution, No Memory.
```

没有行动验证，不写长期记忆。

对小说 Agent 的映射：

```text
L1: 项目/作者/类型索引
L2: 作者长期偏好、项目长期事实
L3: 写作 SOP、类型 SOP、项目风格指南、角色说话规则
L4: 会话与写作过程归档
```

并且长期记忆写入必须满足：

- 用户确认过。
- Story Bible 已固化。
- 章节已提交。
- 门禁/质量评审通过。
- 或者工具实际验证过。

不能把模型在 Reflect 中的主观猜测直接写成事实。

### 1.9 Plan Mode

GenericAgent 的 `memory/plan_sop.md` 是非常强的设计。

核心流程：

```text
探索态 -> 规划态 -> 执行态 -> 验证态 -> 修复循环
```

关键约束：

- 复杂任务先探索。
- 主 Agent 不做大量探测，委托 subagent。
- 计划写入 `plan.md`。
- 每轮执行前必须重新读取 plan。
- 每一步必须有 mini 验证。
- 最后必须启动独立验证 subagent。
- 不允许凭记忆执行。

这其实是一个通用任务黑板系统。

我们当前的 `AgentMissionPlan` 还不够硬，它没有变成每轮必须读取的执行协议。它更像展示数据，而不是决策核心。

小说 Agent 应该有类似：

```text
book_mission.md 或 BookTaskTree.json
```

但应该是结构化 JSON + 可读摘要，而不只是 Markdown。

每轮必须从任务黑板读取：

- 当前作品。
- 当前卷。
- 当前章节。
- 章节状态。
- 当前阻塞点。
- 上次执行工具。
- 下一步允许动作。

### 1.10 Verify SOP

GenericAgent 的验证 SOP 强调：

- 读代码不是验证。
- 测试通过也不是全部验证。
- 必须有工具证据。
- 必须做对抗性探测。
- 最终输出 `VERDICT: PASS/FAIL/PARTIAL`。

这个理念可以直接迁移到小说 Agent 的“质量门禁”：

- 结构化门禁不是文学质量。
- LLM 自评不是质量证明。
- 必须用多维校验：
  - CHANGES 协议。
  - 事实一致性。
  - 角色动机。
  - 冲突推进。
  - 节奏。
  - 读者承诺。
  - 文风一致。
  - 伏笔状态。

最终质量评审也应该输出：

```text
VERDICT: PASS / REWRITE / NEEDS_USER_INPUT / BLOCKED
```

### 1.11 Subagent 协议

GenericAgent 的 `memory/subagent.md` 设计了一个文件 IO 协议：

```text
temp/{task_name}/
input.txt
output.txt
reply.txt
_stop
_keyinfo
_intervene
```

优点：

- 子 Agent 上下文独立。
- 主 Agent 不被海量中间信息污染。
- 主 Agent 可以监察、纠偏、提前停止。
- 支持 Map-Reduce 并行。

这对小说 Agent 非常有价值。

小说创作天然适合多 Agent：

- 设定审查 Agent。
- 伏笔审计 Agent。
- 角色动机 Agent。
- 文风一致性 Agent。
- 商业节奏 Agent。
- 章节摘要/索引 Agent。

但父 Agent 不能直接相信子 Agent 的结论，必须要求可验证证据。

### 1.12 Reflect 模式

GenericAgent 的 `reflect/*.py` 提供了后台自驱机制：

- `autonomous.py`：用户离开后自动触发自主任务。
- `goal_mode.py`：预算驱动，持续优化目标，直到预算耗尽。
- `scheduler.py`：定时任务调度。
- `agent_team_worker.py`：BBS 接单式多 Agent 协作。

这说明优秀 Agent 不只是“一问一答”，还要有外部唤醒机制。

小说 Agent 也需要：

- 后台章节任务队列。
- 用户离开后可继续低风险任务。
- 高风险提交等待确认。
- 定时复盘项目状态。
- 自动发现阻塞任务并汇报。

但这必须建立在 Mission Blackboard 上，而不是让模型凭聊天历史猜。

### 1.13 GenericAgent 对我们的核心启发

GenericAgent 不是告诉我们“少写代码”，而是告诉我们：

1. 主循环要薄。
2. 状态要外化。
3. 复杂任务要有计划文件/任务黑板。
4. 经验要沉淀为 SOP。
5. 工具要原子化。
6. 验证要独立。
7. 子任务要隔离。
8. 长任务要有预算和反射唤醒。

---

## 2. Hermes Agent 架构研究

### 2.1 项目定位

Hermes Agent 是生产级、自改进、多平台 Agent。

README 中强调：

- 内置学习循环。
- 技能创建和使用中改进。
- 持久记忆 nudging。
- 跨会话搜索和摘要。
- 用户建模。
- 支持多模型和多部署环境。
- 支持 Telegram、Discord、Slack、WhatsApp、Signal 等平台。
- 支持 Cron 调度。
- 支持 agentskills.io 标准。

它不是一个小 demo，而是完整运行时系统。

### 2.2 目录结构拆解

关键目录：

```text
agent/
  agent_init.py
  conversation_loop.py
  tool_executor.py
  tool_guardrails.py
  memory_manager.py
  context_engine.py
  skill_preprocessing.py
  prompt_builder.py
  context_compressor.py
  iteration_budget.py
  ...

tools/
  registry.py
  tool_search.py
  delegate_tool.py
  memory_tool.py
  session_search_tool.py
  todo_tool.py
  clarify_tool.py
  terminal_tool.py
  file_tools.py
  ...

gateway/
  session.py
  session_context.py
  stream_dispatch.py
  platforms/*

cron/
  scheduler.py
  jobs.py
```

Hermes 不是靠单个 Planner 类完成所有事情，而是分成：

- 初始化层。
- 对话循环层。
- 工具执行层。
- 工具治理层。
- 上下文工程层。
- 记忆层。
- 技能层。
- 平台层。
- 调度层。

这正是我们当前项目缺少的分层。

### 2.3 Agent 初始化层

`agent/agent_init.py` 非常庞大，但职责清晰：

- 模型/provider/base_url/api_mode 解析。
- Anthropic-compatible、OpenAI Responses、Bedrock、Codex 等 transport 自动选择。
- gateway/session/user/platform 信息绑定。
- tool callbacks 初始化。
- iteration budget 初始化。
- interrupt/steer 状态初始化。
- tool guardrails 初始化。
- checkpoint 初始化。
- context files / SOUL / AGENTS 等上下文配置。
- credential pool 和 provider fallback。

关键点：

```text
Agent Runtime 初始化的是“运行环境”，不是业务流程。
```

我们当前项目把很多小说业务判断放进 Runtime，本质上混淆了：

- Agent 运行时治理。
- 小说领域规划。
- 写作工具执行。

Hermes 的启发是：Runtime 应该关心“能不能、在哪、用什么权限、用多少预算、如何中断、如何记录”，而不是关心“现在是不是该规划章节”。

### 2.4 Conversation Loop

`agent/conversation_loop.py` 是 Hermes 的单轮对话驱动层。

它处理：

- user turn。
- 模型调用。
- tool dispatch。
- retry/fallback。
- context compression。
- memory/skill nudges。
- streaming。
- usage/cost。
- API error classification。

它比 GenericAgent 大很多，但仍然保持一个原则：

```text
所有回复都经过同一对话循环。
```

普通聊天、工具调用、记忆更新、技能加载都在统一循环内处理。

这点我们之前已经意识到，但还没有做干净。我们的设计曾经出现过：

- Planner 选工具。
- 没工具就走普通聊天。
- 工具结果直接格式化返回。

这正是需要避免的结构。

### 2.5 Tool Executor

Hermes 的 `agent/tool_executor.py` 是非常值得研究的部分。

它不仅执行工具，还做了大量前置和后置治理：

#### 2.5.1 工具作用域检查

Hermes 有 `tool_search` 桥接工具，模型可以先搜索工具，再调用底层工具。

但底层工具 unwrap 后仍然要检查：

```text
这个 session 是否真的有权调用该工具？
```

这避免了：

```text
模型绕过工具搜索桥，调用未授权底层工具
```

对我们来说，应该有类似“小说工具作用域”：

- 当前会话绑定哪个 project。
- 当前 project 是否允许写入。
- 当前 run 是否属于该 session/project。
- 当前工具是否允许在当前章节状态下执行。

### 2.5.2 Plugin Block

Hermes 工具执行前会询问 plugin 是否阻止工具：

```text
get_pre_tool_call_block_message(...)
```

这说明工具执行前有一个可插拔策略层。

我们可以设计：

```text
NovelToolPolicy.beforeCall(tool, args, missionState)
```

用来阻止：

- 状态询问误进创作工具。
- 未确认时固化/提交。
- 缺少上下文包时生成正文。
- gate 未通过时提交书城。
- raw user message 直通 PlanChapter。

### 2.5.3 Guardrails

Hermes 有 `ToolCallGuardrailController.before_call`。

它把工具安全治理放在模型之外。

这点非常关键：  
**不能指望模型自己永远遵守系统提示。**

我们的小说 Agent 也必须有模型外的硬规则：

```text
if action.Tool == PlanChapter && args.Source == RawUserMessage:
    block
```

不是提示词里说“不要这样”，而是运行时不允许。

### 2.5.4 Checkpoint

Hermes 对 file-mutating 和 destructive terminal command 做 checkpoint。

小说 Agent 的对应物是：

- 固化 Story Bible 前做快照。
- 提交卷规划前做快照。
- 提交章节成稿前做快照。
- 修改设定影响已写章节前做依赖影响报告。

当前项目已经有部分 rollback/project context，但还没有形成统一 checkpoint policy。

### 2.5.5 并发工具执行

Hermes 支持并发工具执行，但保持结果顺序。

对小说 Agent 来说，写入型工具不能并发乱跑，但读/评审型工具可以并行：

- 角色动机评审。
- 文风评审。
- 伏笔评审。
- 事实一致性检查。
- 类型节奏检查。

这些可以成为“并行质量评审任务”，最后由主 Agent 汇总裁决。

### 2.6 Tool Registry

Hermes 的 `tools/registry.py` 管理工具注册、schema、toolset、handler、check_fn、动态 schema override。

核心思想：

- 工具不是散落在 Runtime switch 中。
- 每个工具有：
  - name
  - toolset
  - schema
  - handler
  - check_fn
  - emoji/display
  - dynamic schema

我们当前 `AgentToolRegistry` 仍然是一个静态 list + switch，后续很容易继续膨胀。

建议小说 Agent 改成：

```text
NovelToolEntry:
  name
  category
  risk
  schema
  preconditions
  handler
  confirmationPolicy
  resultMapper
  artifactTypes
```

这样工具治理可以数据化。

### 2.7 Tool Search

Hermes 有工具搜索，而不是每次把所有工具都完整塞给模型。

这有两个好处：

1. 降低上下文噪声。
2. 限制模型只看到当前可用工具。

小说 Agent 也可以做阶段化工具暴露：

```text
foundation 阶段:
  QueryProjectStatus
  SearchCreativeKnowledge
  PlanStoryFoundation
  CommitStoryFoundation

chapter_planning 阶段:
  QueryProjectStatus
  PlanChapter
  SelectChapterCandidate

draft_generation 阶段:
  BuildChapterContextPackage
  GenerateChapterWithChanges
  ValidateChapterDraft
  RepairChapterDraft
  CommitValidatedChapter
```

不要在所有阶段都把所有工具暴露给 Planner。

### 2.8 Delegate Task

Hermes 的 `delegate_task` 非常成熟。

它强调：

- 子 Agent 有独立 conversation、terminal、toolset。
- 子 Agent 没有父会话记忆，必须显式传 context。
- 子 Agent 结果只是 self-report，父 Agent 必须验证。
- 子 Agent 可 leaf 或 orchestrator。
- 有并发数限制和 spawn depth 限制。
- 子 Agent 不能使用 clarify/memory/send_message 等工具，避免越权。
- 长期后台任务不要用同步 delegate，而应该用 cron/background。

这对小说 Agent 价值很大。

建议后续设计：

```text
delegate_novel_review:
  taskType: character_motivation | prose_style | foreshadowing | continuity | market_pacing
  contextPackageId
  draftArtifactId
  projectId
  output: structured verdict + evidence
```

主 Agent 汇总这些评审，但不能直接相信。

### 2.9 Memory Manager

Hermes 的记忆系统包括：

- memory context block。
- streaming scrubber。
- memory tool。
- session search。
- background review。
- curator。
- user modeling。

其核心不是简单 RAG，而是：

```text
长期记忆需要被合适地注入当前 turn，并且不能污染原始会话。
```

`build_memory_context_block` 会把预取记忆包成 fenced block 注入当前用户消息附近。

这说明：

- 记忆是运行时上下文的一部分。
- 但注入应该是临时的，不应直接改写持久会话。

我们的 `AgentObservationBuilder` 已经有类似想法，但需要进一步明确：

- 哪些是 session memory。
- 哪些是 project memory。
- 哪些是 author memory。
- 哪些是 execution memory。
- 每种记忆如何注入。
- 哪种记忆允许 Reflect 更新。

### 2.10 Skill System

Hermes 的 skill 系统做了：

- 扫描本地 skills。
- 读取 frontmatter。
- 按 category 生成技能索引。
- 根据平台和可用工具过滤。
- 缓存 skill prompt。
- 任务相关时强制模型加载 skill。
- 技能缺陷可通过 skill_manage 修补。

它在系统提示中强调：

```text
如果技能相关，必须加载。
困难任务后，建议保存为 skill。
如果技能缺步骤，更新技能。
```

小说 Agent 也需要类似的“写作技能系统”：

```text
skills/
  玄幻爽文节奏.md
  都市悬疑伏笔.md
  长篇角色弧线.md
  章节开篇钩子.md
  CHANGES协议写法.md
  作者个人风格.md
```

这些技能不是知识库素材，而是“程序性写作记忆”。

### 2.11 Context Engine

Hermes 会加载：

- SOUL.md
- .hermes.md / HERMES.md
- AGENTS.md
- CLAUDE.md
- .cursorrules

并且有优先级、截断策略、扫描策略。

它解决的是：

```text
当前工作区有哪些稳定规则？
```

小说 Agent 的对应物是：

- Project Story Bible。
- Author Style Guide。
- Book Mission Blackboard。
- Writing SOP。
- Genre Skill。
- Current Chapter Context Package。

这些应该由 `NovelContextEngine` 统一组织，而不是散在 Prompt 拼接逻辑里。

### 2.12 Iteration Budget

Hermes 有 `IterationBudget`：

- parent agent 有预算。
- subagent 有预算。
- 工具调用轮次不能无限增长。
- 中断和预算是运行时治理的一部分。

小说 Agent 的预算不应该只有 `AgentMaxAutoSteps`，还应该有：

- 每个 user turn 最大自动工具步数。
- 每个 chapter task 最大修复次数。
- 每个 mission 最大后台自动步数。
- 每个 quality gate 最大重写次数。
- 每个 session 最大连续无进展次数。

预算耗尽应该进入 `blocked` 或 `needs_user_input`，并写入任务黑板。

### 2.13 Gateway 与多平台会话

Hermes 的 gateway 支持多个平台，但关键不是平台数量，而是：

- session context。
- platform-specific formatting。
- stream dispatch。
- delivery。
- slash commands。
- status。

这说明 Agent 应该分清：

```text
Agent Kernel != UI Channel
```

我们当前 Web 前端和 Agent 后端已经分离，但还没有把“会话历史”和“任务状态”作为独立领域对待。

用户要的 GPT/Grok 左侧会话历史，本质是 UI 层；而 Agent 每个会话的任务记忆，是 Kernel 层。二者需要绑定，但不能混为一谈。

### 2.14 Hermes 对我们的核心启发

Hermes 不是告诉我们“做更多功能”，而是告诉我们：

1. Runtime 要治理执行，不做业务脑。
2. 工具执行要有 scope、guardrail、checkpoint、policy。
3. 上下文注入要有工程化层。
4. 记忆和技能是长期系统，不是 prompt 拼接。
5. 子 Agent 要隔离并受限。
6. 模型兼容要在 transport 层处理，不散落在业务逻辑里。
7. 中断、预算、恢复、调度都是 Agent 必备运行时能力。

---

## 3. 两者对照

| 维度 | GenericAgent | Hermes Agent | 对小说 Agent 的启发 |
|---|---|---|---|
| 主循环 | 极简约 100 行 | 工业级 conversation loop | 主循环不能堆业务流程 |
| 工具 | 9 个原子工具 | 大型注册系统 + toolsets | 小说工具要原子化、阶段化暴露 |
| 状态 | checkpoint、plan.md、history_info | session、context、trajectory、memory | Mission Blackboard 必须成为真相源 |
| 记忆 | L1/L2/L3/L4 | memory manager + session search + user model | 作者/项目/会话/执行记忆分层 |
| 技能 | SOP 自进化 | skills 标准、skill_view/manage | 写作 SOP/类型技能必须产品化 |
| 验证 | verify_sop 强制工具证据 | tool failure classification + guardrails | 质量门禁不能只靠 LLM 总结 |
| 子 Agent | 文件 IO 协议 | delegate_task 工具 | 小说评审/索引/审计可子 Agent 化 |
| 调度 | reflect/goal/scheduler | cron/gateway/background review | 多章节任务需要调度器 |
| 安全 | SOP 和工具约束 | guardrail/checkpoint/scope | 小说写入需要硬前置条件 |
| 自进化 | 任务完成后沉淀 SOP | 技能创建和改进 | 项目经验应形成写作技能 |

---

## 4. 当前小说 Agent 的结构性问题

### 4.1 用户输入直通生产工具

当前最危险的模式：

```text
context.UserMessage -> AgentPlanner -> ToolCall.Args["userGoal"] = context.UserMessage
```

这导致：

- 状态询问可能成为创作素材。
- 用户抱怨可能成为章节目标。
- 用户取消/催促可能触发推进。
- 用户反馈可能被误写入 Story Bible。

优秀 Agent 都不会允许 raw user message 直接进入高层生产工具。

### 4.2 Conversation Intent 与 Domain Intent 混在一起

用户的“会话行为”有很多种：

```text
status_query
confirmation
cancel
feedback
revision_request
new_creative_seed
continue_task
free_chat
project_switch
```

小说领域动作也有很多种：

```text
plan_foundation
plan_volume
plan_chapter
build_context
generate_draft
validate_gate
repair
commit
review
```

现在两者混在 Planner 里，导致用户一说“章节”，就可能进入 `PlanChapter`。

应该先做 Conversation Kernel，再做 Domain Planner。

### 4.3 MissionPlan 不是硬真相源

我们已经有 `AgentMissionPlan` 和 `BookTaskTree`，但它还不够硬：

- Runtime 不是每步都以它作为唯一决策依据。
- 工具前置条件不是从它强制推导。
- Planner 仍然可绕开它。
- 状态查询没有完全从它生成。

优秀 Agent 的状态是外化且强制读取的。  
我们需要把 MissionPlan 提升为 `Mission Blackboard`。

### 4.4 工具太像业务按钮

当前工具中仍有一些高层业务含义：

```text
PlanChapter
PlanVolumeArc
GenerateChapterWithChanges
```

领域工具可以存在，但必须拆出：

- 工具能力。
- 工作流策略。
- 前置条件。
- 风险确认。
- 状态更新。

否则 Runtime 会越来越像按钮状态机。

### 4.5 Runtime 继续变胖

我们之前把很多判断塞进 Runtime：

- pending confirmation。
- legacy action normalize。
- needs confirmation。
- mission sync。
- project isolation。
- reflection stop condition。
- tool fallback。

这些有些是必要治理，但业务判断应该下沉到：

- Conversation Kernel。
- Mission Blackboard。
- Tool Policy。
- Domain Planner。

Runtime 只负责 loop 和治理。

### 4.6 Reflect 还没有成为裁判

Reflect 应该决定：

- 工具结果是否满足目标。
- 是否要继续。
- 是否需要用户。
- 是否需要修复。
- 是否允许进入提交确认。
- 哪些任务状态改变。

当前 Reflect 更多像总结器 + 质量字段补丁，还没有成为小说生产裁判。

### 4.7 RAG 是上下文材料，不是任务意图

我们已经引入阶段化 RAG，但要注意：

RAG 不能决定用户这一轮是不是创作请求。  
RAG 只能在已经明确任务类型后提供上下文。

否则“状态询问 + 章节词汇”也会触发章节规划 RAG，进一步放大误判。

---

## 5. 推荐的新架构

### 5.1 总体分层

建议重构为：

```text
NovelAgentKernel
  ConversationKernel
  MissionBlackboard
  NovelContextEngine
  DomainPlanner
  ToolPolicyEngine
  ToolExecutor
  ReflectionEngine
  MemorySkillEngine
  TaskScheduler
```

### 5.2 ConversationKernel

职责：

```text
把用户输入转换成 TurnIntent，不调用小说业务工具。
```

输出：

```text
TurnIntent:
  type:
    free_chat
    status_query
    confirmation
    cancel
    new_project_seed
    creative_brief
    continue_mission
    revision_request
    user_feedback
    project_switch
  confidence
  referencedProjectId
  referencedRunId
  referencedChapterId
  extractedBrief
  extractedConstraints
  safetyNotes
```

硬规则：

- `status_query` 永远不能调用创作工具。
- `confirmation` 只能消费 pending confirmation。
- `cancel` 只能取消 pending 或暂停任务。
- `creative_brief` 才能创建/修改创作任务。
- `continue_mission` 必须从 Mission Blackboard 找下一步。

### 5.3 MissionBlackboard

这是全书任务真相源。

结构：

```text
MissionBlackboard:
  missionId
  sessionId
  projectId
  bookId
  status
  currentFocus
  userGoal
  authorConstraints
  bookTaskTree
  pendingConfirmations
  activeArtifacts
  schedulerQueue
  blockers
  lastVerifiedState
```

章节任务：

```text
ChapterTask:
  chapterId
  volumeId
  status:
    unstarted
    planned
    candidate_selected
    context_ready
    draft_generated
    gate_failed
    quality_failed
    repairing
    validated
    awaiting_commit_confirmation
    committed
    blocked
  allowedNextActions
  requiredArtifacts
  sourceBriefId
  contextPackageId
  draftArtifactId
  gateReportId
  qualityReportId
  commitId
```

注意：  
`allowedNextActions` 应该由状态机/策略引擎计算，Planner 不能随便越过。

### 5.4 NovelContextEngine

负责组装上下文，不负责决策。

输入：

- TurnIntent。
- MissionBlackboard。
- Story Bible。
- ProjectData。
- 会话摘要。
- 作者偏好。
- 当前章节上下文包。
- RAG 召回。

输出：

```text
NovelObservation:
  turnIntent
  missionState
  projectState
  allowedActions
  relevantMemory
  ragBuckets
  activeArtifacts
  risks
```

### 5.5 DomainPlanner

只在以下 intent 时工作：

- `creative_brief`
- `continue_mission`
- `revision_request`
- `user_feedback`

它不直接接收 raw user message，而接收结构化 observation。

输出：

```text
DomainPlan:
  nextActionType:
    ask_user
    call_tool
    wait_confirmation
    update_mission
    final_reply
  toolCall?
  missionPatch?
  reason
```

### 5.6 ToolPolicyEngine

所有工具调用前必须过策略。

规则示例：

```text
PlanChapter:
  requires creativeBrief or continueMission
  forbids raw status query as userGoal
  requires committed foundation
  requires volume plan or explicit chapter bootstrap

BuildChapterContextPackage:
  requires selected candidate
  requires runId belongs to session project

GenerateChapterWithChanges:
  requires context_ready
  requires confirmation

CommitValidatedChapter:
  requires gate_status = validated
  requires quality_status = pass or user_override
  requires confirmation
```

这层必须是代码硬规则，不是 prompt。

### 5.7 ToolExecutor

执行器只做：

- scope check。
- project context。
- checkpoint。
- execute。
- result classification。
- artifact save。
- event emit。

不要在执行器里做“用户是不是想写下一章”这种判断。

### 5.8 ReflectionEngine

Reflect 应该从总结器升级为裁判。

输入：

- tool result。
- mission state。
- gate report。
- quality report。
- user intent。

输出：

```text
ReflectionDecision:
  verdict:
    continue
    final
    ask_user
    needs_rewrite
    blocked
    await_confirmation
  missionPatch
  qualityGate
  memoryPatch
  nextActionRecommendation
```

硬规则：

- GenerationGate 通过不等于可提交。
- QualityGate 失败不能进入提交确认。
- Reflect 不能直接写长期事实，只能提出 memory patch。

### 5.9 MemorySkillEngine

负责：

- 作者偏好。
- 项目长期目标。
- 类型 SOP。
- 写作技巧 SOP。
- 执行失败经验。
- 成功修复策略。

写入规则：

- 未验证不写长期记忆。
- 用户偏好可低风险写入，但需要可撤销。
- 项目 canon 必须用户确认或 Story Bible 固化。
- 技能更新需要来源证据。

### 5.10 TaskScheduler

用于多任务：

```text
queued
running
waiting_confirmation
blocked
paused
done
```

调度器不替代 Agent 决策，它只是维护可执行队列。

后台可自动执行：

- 检索。
- 摘要。
- 上下文包构建。
- 非写入型质量检查。

必须等待用户确认：

- 固化 Story Bible。
- 提交卷规划。
- 生成正文草稿。
- 修复草稿。
- 提交成稿。
- 删除/覆盖。

---

## 6. 推荐落地路线

### 阶段 1：停止 raw user message 直通

目标：

- 引入 `TurnIntent`。
- 禁止 `PlanChapter(userGoal = userMessage)`。
- 所有创作工具参数改成结构化对象。

验收：

- “我叫你生成的章节准备好了吗”只进入状态查询。
- “刚才写完了吗”只读取 MissionBlackboard。
- “继续写下一章”从当前任务状态推导下一步。

### 阶段 2：MissionBlackboard 成为唯一状态源

目标：

- `AgentMissionPlan` 升级为 `MissionBlackboard`。
- 工具结果只产生 artifacts 和 mission patches。
- 前端工作流页从 blackboard 渲染。

验收：

- 每个章节任务状态可追踪。
- 任务切换不串。
- 会话恢复后能知道上次停在哪。

### 阶段 3：ToolPolicyEngine

目标：

- 工具调用前置条件全部代码化。
- 工具暴露按阶段过滤。
- 高风险工具统一确认。

验收：

- Planner 输出非法工具会被阻止并生成解释。
- 缺上下文不能生成正文。
- gate 未过不能提交。

### 阶段 4：Reflect 裁判化

目标：

- Reflect 输出 verdict。
- 质量失败进入 rewrite/blocked。
- 质量通过才允许提交确认。

验收：

- GenerationGate pass + QualityGate fail 时不能提交。
- Reflect 可要求用户补设定。
- 修复次数超限后阻塞。

### 阶段 5：写作 Skill/SOP 系统

目标：

- 将创作经验从 Prompt 中剥离成技能。
- 类型、风格、章节写法、CHANGES 协议都成为可加载技能。

验收：

- 不同类型小说加载不同写作 SOP。
- 作者偏好可沉淀、可回收。
- 项目经验能长期复用。

### 阶段 6：子 Agent 评审与多任务调度

目标：

- 引入评审子 Agent。
- 多章节任务可并行读/审，写入仍串行确认。
- 后台调度器只执行低风险任务。

验收：

- 多本小说任务可并列展示。
- 单章有多个评审报告。
- 主 Agent 对子 Agent 输出有验证动作。

---

## 7. 最终判断

当前小说 Agent 的方向没有错：  
用户和 Agent 对话，Agent 自主调用工具、RAG、写作引擎、门禁和工作流。

但实现方式正在偏离：

```text
从“Agent 主脑 + 工具身体”
偏成了
“Runtime 状态机 + Planner 补丁 + 业务工具按钮”
```

GenericAgent 和 Hermes Agent 共同说明：优秀 Agent 的关键不是工具数量，而是边界清楚。

我们应该把当前系统重构为：

```text
Conversation Kernel 负责理解用户话语
Mission Blackboard 负责长期任务真相
Domain Planner 负责小说领域决策
ToolPolicyEngine 负责硬约束
Writing Executor 负责执行旧项目硬核写作能力
ReflectionEngine 负责质量裁判和任务推进
MemorySkillEngine 负责经验沉淀
TaskScheduler 负责低风险后台推进
```

如果这个边界建立起来，旧项目的 ProjectData、RAG、CHANGES、事实快照、依赖追踪，才会真正成为“写作身体”；否则它们只会被当前 Runtime 当成更多工具按钮继续堆上去。

一句话总结：

> 我们不应该继续给现有 Agent 打补丁，而应该先把“用户话语 -> 任务事件 -> 小说任务黑板 -> 受控工具执行”的主干重建起来。只有这样，它才会像一个自主小说 Agent，而不是一个带聊天入口的写作流程机。

