# NovelAgent 架构与写作流程说明

**文档版本**: 1.0  
**更新日期**: 2026-06-08  
**实现状态**: ✅ Phase-Based Tool & Memory Layering 已完成

---

## 目录

1. [整体架构](#一整体架构)
2. [写小说完整流程](#二写小说完整流程)
3. [交互性特点](#三交互性特点)
4. [与传统系统对比](#四与传统系统对比)
5. [技术指标](#五技术指标)

---

## 一、整体架构

### 1.1 核心循环：Observe-Plan-Act-Reflect

```
┌─────────────────────────────────────────────────────────────┐
│                      AgentRuntime                           │
│         (主循环：最多 3-20 步，受 settings 控制)              │
└─────────────────────────────────────────────────────────────┘
                            │
            ┌───────────────┼───────────────┐
            ↓               ↓               ↓
    ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
    │ Session 管理  │ │ Project 路由  │ │ Phase 推断    │
    │ (会话状态)    │ │ (新书/续写)   │ │ (4 个阶段)    │
    └──────────────┘ └──────────────┘ └──────────────┘
                            │
            ┌───────────────┴───────────────┐
            ↓                               ↓
    ┌──────────────────┐          ┌──────────────────┐
    │  🔍 Observe      │          │  🧠 Plan         │
    │  观察项目上下文   │──────────→│  LLM 决策行动    │
    │  (工具已过滤)    │          │  (自主选择工具)   │
    └──────────────────┘          └──────────────────┘
            ↑                               │
            │                               ↓
    ┌──────────────────┐          ┌──────────────────┐
    │  💭 Reflect      │←─────────│  ⚡ Act          │
    │  质量检查/反思    │          │  执行工具调用     │
    └──────────────────┘          └──────────────────┘
```

**设计灵感**: 借鉴 GenericAgent 的 agent_runner_loop 和 Hermes Agent 的 conversation_loop

### 1.2 核心组件

#### 工具发现与自主决策 (tool_search + AgentPlanner)

当前主链路不再使用后端硬编码的 `PhaseInference`。Agent 在 Observe 阶段读取产品空间、记忆层、工作台状态提示、最近观察和已发现工具；Planner 由 LLM 自主判断是否需要 `tool_search`、`QueryWorkspaceState`、`ResolveNovelProject` 或创作工具。

```csharp
// AgentPlanner receives:
product_space        // 小说书城、创作工作流、知识库、记忆系统、Agent Runtime
memory_layers        // chat/session/project/author/execution memory
workspace_state_hint // 轻量提示；真实状态由 QueryWorkspaceState 查询
available_tools      // tool_search 缓存或刚发现的工具语义

// Runtime boundary:
// - LLM 决定业务动作
// - Runtime 只负责工具发现、执行、安全边界、防越权和反重复
```

#### 分层工具集 (AgentToolRegistry)

工具按阶段暴露语义，但不是硬编码路由。`tool_search` 会返回工具名称、风险、参数、读写面、产物类型和用户可见位置，让模型自己选择下一步。

```csharp
// Conversation: 状态、知识、项目解析
QueryWorkspaceState         // 查询书城/知识库/工作流真实状态
QueryProjectStatus          // 查询当前项目状态
ResolveNovelProject         // 创建、绑定或切换项目

// Planning: 规划决策
QueryProjectStatus
ResolveNovelProject
SearchCreativeKnowledge     // 检索创意素材
PlanStoryFoundation         // 规划故事地基
PlanVolumeArc               // 规划卷结构
PlanChapter                 // 规划章节 (生成3个候选)
SelectChapterCandidate      // 选择章节候选
BuildChapterContextPackage  // 构建上下文包

// Creation: 4 工具 (生成正文)
QueryProjectStatus
GenerateChapterWithChanges  // 生成章节草稿
RepairChapterDraft          // 修复草稿
ValidateChapterDraft        // 验证草稿

// Review: 4 工具 (质量评审)
QueryProjectStatus
CommitValidatedChapter      // 提交成稿
ReviewChapter               // 评审章节
RefreshProjectIndexes       // 刷新索引
```

#### 记忆与上下文管理 (PhaseContextBuilder) ⭐ 新增

阶段化加载上下文，降低 token 消耗：

| 阶段 | 加载内容 | Token 预算 | 原因 |
|------|---------|-----------|------|
| **Conversation** | Checkpoint + 最近1条观察 | 500-1K | 只需知道当前状态 |
| **Planning** | Checkpoint + ProjectMemory摘要 + RAG(top=5) | 2-3K | 需要了解项目背景 |
| **Creation** | 完整ContextPackage + StoryBible + RAG(top=15) | 10-20K | 需要所有细节写正文 |
| **Review** | Checkpoint + Draft + GateReports | 5-10K | 需要评审当前产物 |

```csharp
// 示例：Planning 阶段上下文
{
    "recent_observations": [...last 3 items],
    "mission_plan": { stage, status, project_title },
    "project_memory_summary": {
        total_chapters: 5,
        key_patterns: ["角色塑造", "节奏把控"]
    },
    "story_foundation": {
        genre: "玄幻",
        core_hook: "废材逆袭",
        volume_count: 3
    }
}
```

#### 其他核心组件

- **AgentMemoryService**: Session/Project/Execution 三层记忆持久化
- **AgentRecoveryEngine**: 智能失败恢复，自动推理前置工具链
- **ConversationKernel**: 用户意图识别、对话行为分类
- **AgentPlanner**: LLM 决策引擎，从可用工具中自主选择
- **ResolveNovelProject 工具**: 由 AgentPlanner 在理解用户意图后自主调用，用于创建、绑定或切换小说项目
- **ToolPolicyEngine**: 工具前置条件检查、参数验证
- **ReflectionEngine**: 执行后质量反思、改进建议

---

## 二、写小说完整流程

### 第一阶段：初始化项目 (Planning Phase)

```
用户: "我要写一本修仙小说"
  ↓
[AgentPlanner] → 根据产品空间、记忆层和工具语义自主决策
[Tool] ResolveNovelProject → 创建或绑定项目 (project_123)
  ↓
Agent: "好的，我们来规划这本修仙小说的基础设定。"
  ↓
[自动切换] → Planning Phase (8 工具可用)
[Tool] PlanStoryFoundation
  参数:
    - genre: "修仙"
    - core_hook: "主角意外获得上古仙门传承"
    - protagonist: "资质平庸但悟性极高的少年"
  
  输出:
    ✅ 故事地基已创建
    - 世界观: 修仙等级体系（炼气→筑基→金丹→元婴...）
    - 核心冲突: 正道与魔道千年恩怨
    - 主角特质: 拥有"逆天悟性"天赋
  ↓
[Tool] PlanVolumeArc
  参数:
    - volume_number: 1
    - volume_title: "初入仙途"
    - target_chapters: 30
  
  输出:
    ✅ 第一卷结构已规划
    - 起: 山村少年，流星坠落
    - 承: 宗门试炼，暗藏杀机
    -转: 获得传承，实力暴涨
    - 合: 击败强敌，名震宗门
```

### 第二阶段：章节规划 (Planning Phase)

```
用户: "规划第一章"
  ↓
[AgentPlanner] → 如缓存不足，调用 tool_search(phase="Planning")
[Context] 加载: ProjectMemory摘要 + RAG(top=5) (2-3K tokens)
  ↓
[Tool] PlanChapter
  参数:
    - chapter_number: 1
    - volume_number: 1
  
  输出:
    ✅ 生成 3 个章节候选大纲
    
    【候选 1: 山村起源流】
    - 开篇: 描写主角林枫的平凡山村生活
    - 冲突: 流星坠落，村民恐慌
    - 转折: 林枫好奇前往查看
    - 字数: 3000 字
    
    【候选 2: 宗门淘汰流】
    - 开篇: 天元宗招收弟子大会
    - 冲突: 林枫因资质不足被淘汰
    - 转折: 不甘失败，决心自我修炼
    - 字数: 3200 字
    
    【候选 3: 探险奇遇流】★ 推荐
    - 开篇: 林枫在后山采药时迷路
    - 冲突: 发现隐蔽的古老洞府
    - 转折: 触发传承阵法，获得功法
    - 字数: 3000 字
  ↓
Agent: "我生成了 3 个开篇方案，推荐【候选 3】，它能快速切入主线且悬念感强。你想用哪个？"

用户: "就用候选 3"
  ↓
[Tool] SelectChapterCandidate
  参数:
    - chapter_number: 1
    - selected_index: 2
  
  输出:
    ✅ 已确认使用候选 3: 探险奇遇流
  ↓
[Tool] BuildChapterContextPackage
  执行过程:
    📍 检索相关素材...
    📍 构建角色上下文 (林枫性格、背景)
    📍 构建世界观上下文 (修仙体系、后山地理)
    📍 构建情节上下文 (前置铺垫、后续伏笔)
  
  输出:
    ✅ 上下文包已构建 (12,500 tokens)
    - 任务状态: unstarted → context_ready
    - 下一步: 可以开始生成正文
```

### 第三阶段：正文生成 (Creation Phase)

```
用户: "开始写第一章"
  ↓
[AgentPlanner] → 根据任务状态和工具语义选择生成链路
[Context] 全量加载: ContextPackage + StoryBible (15K tokens)
  ↓
[Tool] GenerateChapterWithChanges
  参数:
    - chapter_number: 1
    - target_length: 3000
  
  执行过程:
    📍 加载上下文包...
    📍 生成第 1/3 段 (开篇环境描写)...
    📍 生成第 2/3 段 (发现洞府)...
    📍 生成第 3/3 段 (触发传承)...
    📍 自动埋伏笔: 洞府中的神秘符文
    📍 风格一致性检查...
  
  输出:
    ✅ 第一章草稿已生成 (3,200 字)
    
    【第一章：意外奇遇】
    
    青山县，林家村。
    
    十六岁的林枫背着竹篓，行走在后山的崎岖小道上...
    
    [正文省略 3000 字]
    
    ...当他的手触碰到那枚古老玉简的瞬间，
    一道刺目的金光骤然爆发！
    
    [To be continued...]
    
    - 任务状态: context_ready → draft_generated
    - 下一步: 需要质量验证
  ↓
Agent: "第一章草稿已生成 (3,200字)，请审阅..."
```

### 第四阶段：质量评审 (Review Phase)

```
[AgentPlanner] → 根据草稿、门禁和工作流状态选择 Review 阶段工具
[Context] 加载: Draft + GateReports (5-10K tokens)
  ↓
[Tool] ValidateChapterDraft (自动门禁)
  检查项:
    ✅ 字数达标: 3,200 / 3,000 (目标)
    ✅ 无硬伤: 逻辑自洽，时间线一致
    ✅ 伏笔检查: 已埋 1 处伏笔 (神秘符文)
    ⚠️  发现问题: 主角性格描写不足 (仅 2 处心理描写)
    ⚠️  建议: 补充林枫的内心挣扎与成长
  ↓
Agent: "验证完成，发现 1 个可优化点：
  ⚠️  主角性格描写略显单薄，建议补充心理活动
  
  需要我自动修复吗？ [修复] [忽略] [手动编辑]"

用户: "修复一下"
  ↓
[Tool] RepairChapterDraft
  参数:
    - chapter_number: 1
    - repair_focus: "character_depth"
  
  修复内容:
    📍 第 5 段增加: "林枫心中五味杂陈..."
    📍 第 12 段增加: "他知道，这一刻将改变他的一生..."
    📍 第 18 段增加: "既然天给了我机会，我绝不能辜负..."
  
  输出:
    ✅ 草稿已修复 (新增 300 字，共 3,500 字)
    ✅ 再次验证通过
  ↓
[Tool] CommitValidatedChapter
  执行过程:
    📍 最终质量检查...
    📍 提交到 Story Bible...
    📍 更新章节索引...
    📍 记录伏笔到账本...
  
  输出:
    ✅ 第一章已提交
    - 任务状态: draft_generated → completed
    - 文件位置: /chapters/volume_1/chapter_001.md
    - 下一步: 可以规划第二章
  ↓
Agent: "第一章已成功提交！现在可以继续规划第二章，或者先休息一下？"
```

---

## 三、交互性特点

### 3.1 多层次交互

#### 轻量对话 (Conversation Phase)

```
用户: "写到哪了？"
  ↓
[Phase] Conversation (2工具, 500 tokens, <1s 响应)
[Tool] QueryProjectStatus
  ↓
Agent: "当前进度：
  ✅ 已完成: 第 1 章 (已提交)
  🔄 进行中: 第 2 章 (规划阶段)
  📊 整体进度: 1/30 章 (3.3%)"
```

#### 决策确认 (Planning Phase)

```
Agent: "我为第二章生成了 3 个候选大纲：

【候选 1: 宗门试炼】
主角参加入门试炼，遇到天才竞争对手...

【候选 2: 隐藏实力】★ 推荐
主角刻意隐藏修为，低调进入宗门...

【候选 3: 意外暴露】
试炼中遭遇危险，被迫暴露部分实力...

推荐使用【候选 2】，更符合'扮猪吃虎'套路。你的选择？"

用户: "就用推荐的"
  ↓
[Tool] SelectChapterCandidate (index: 1)
```

#### 创作过程透明化 (Creation Phase)

通过 SSE (Server-Sent Events) 实时推送进度：

```
Agent: (实时流式推送)
  📍 "正在检索修仙体系素材... (0.5s)"
  📍 "构建角色上下文: 林枫性格档案 (1.2s)"
  📍 "构建世界观上下文: 天元宗体系 (1.5s)"
  📍 "生成正文第 1/3 段: 环境描写... (3.2s)"
  📍 "生成正文第 2/3 段: 冲突展开... (4.1s)"
  📍 "生成正文第 3/3 段: 高潮转折... (3.8s)"
  📍 "自动埋伏笔: 神秘符文 → 后续章节揭秘 (0.8s)"
  ✅ "草稿生成完成 (3,200字，总耗时 15.1s)"
```

#### 质量门禁交互 (Review Phase)

```
Agent: "📋 第一章验证报告

【通过项】
  ✅ 字数: 3,200 / 3,000 (目标)
  ✅ 逻辑: 无时间线冲突
  ✅ 伏笔: 神秘符文 (已登记)
  ✅ 人物: 主角形象清晰

【待优化项】
  ⚠️  风格: 对话略显生硬 (占比 15%，建议 <10%)
  ⚠️  节奏: 第二段过渡稍快

【操作建议】
  1️⃣ [自动修复] - AI 自动调整对话和节奏
  2️⃣ [忽略问题] - 直接提交当前版本
  3️⃣ [手动编辑] - 在编辑器中手动修改

你的选择？"
```

### 3.2 Agent 自主性

**关键设计原则：不是工作流，是智能决策**

```csharp
// ❌ 传统工作流 (硬编码状态机)
if (state == "planned") {
    只能调用 BuildContext;
} else if (state == "context_ready") {
    只能调用 GenerateChapter;
}
→ Agent 没有选择权，只能执行预定流程

// ✅ 当前架构 (LLM 自主决策 + 语义工具发现)
if (needsPlanningTools && !availableTools.Contains("PlanChapter")) {
    tool_search(phase: "Planning");
}

if (availableTools.Contains("PlanChapter")) {
    context = ProductSpace + MemoryLayers + WorkspaceStateHint + ProjectSummary + RAG;
    
    Agent 基于工具语义自主决策:
      - 可能先 SearchCreativeKnowledge 查类型套路
      - 可能先 clarify 询问用户偏好
      - 可能直接 PlanChapter 生成大纲
      - 可能先 Plan 再 Select 再 Build (多步骤)
}
→ Agent 仍然自主，Runtime 只提供能力发现和安全边界
```

**实际案例：**

```
用户: "规划第三章"
  ↓
[Phase] Planning (8 工具可用)
[LLM 决策] Agent 自主选择执行顺序:

Step 1: SearchCreativeKnowledge
  query: "修仙小说第三章常见套路"
  结果: 找到"宗门试炼""资源争夺"等模板

Step 2: AskUser
  question: "你希望第三章侧重战斗还是智谋？"
  用户回复: "智谋"

Step 3: PlanChapter
  基于用户偏好 + 检索结果生成候选

Step 4: SelectChapterCandidate (自动选择推荐项)

Step 5: BuildChapterContextPackage
  构建上下文准备创作

→ Agent 自主规划了 5 步流程,用户只需回答 1 个问题
```

### 3.3 用户控制粒度

**不同用户需求的灵活性：**

```
【全自动模式】
用户: "帮我写完第一卷"
  ↓
Agent 自主执行:
  - 规划 30 个章节
  - 每章生成 3 个候选
  - 自动选择推荐候选
  - 批量生成正文
  - 质量门禁自动修复
  - 全部提交
→ 用户只需要最终审阅

【半自动模式】(推荐)
用户: "规划第二章"
  ↓
Agent 生成 3 个候选
  ↓
用户: "用候选 2"
  ↓
Agent 生成正文 → 质量检查 → 提交
→ 用户参与关键决策点

【精细控制模式】
用户: "生成第三章"
  ↓
Agent: "生成完成，发现 2 个可优化点..."
  ↓
用户: "先别修复，我要手动编辑"
  ↓
用户手动编辑后
  ↓
用户: "现在提交"
→ 用户全程掌控每个环节
```

---

## 四、与传统系统对比

### 4.1 对比表

| 维度 | 传统工作流系统 | NovelAgent (Phase-Based) |
|------|---------------|-------------------------|
| **架构模式** | 硬编码状态机 | Observe-Plan-Act-Reflect 循环 |
| **工具选择** | 状态决定工具 (if-else) | Agent 从可用工具中自主决策 |
| **上下文加载** | 全量加载或固定模板 | 阶段化动态加载 (500-20K tokens) |
| **交互性** | 预定义节点 | 任意时刻可查询、确认、修改 |
| **灵活性** | 必须按流程执行 | Agent 可跳过/重排/并行步骤 |
| **失败恢复** | 重试或报错 | 智能推理前置工具链 |
| **用户体验** | "下一步"按钮 | 自然对话 + 实时进度流 |
| **扩展性** | 修改状态机代码 | 添加新工具即可 |

### 4.2 关键优势

**1. Agent 保留自主性**

```
传统系统: "你现在只能调用 BuildContext"
NovelAgent: "Planning 阶段有 8 个工具，你决定用哪个"
```

**2. 上下文精准化**

```
传统系统: 每次加载 20K tokens (浪费)
NovelAgent: 
  - Conversation: 500 tokens (查询状态)
  - Planning: 2.5K tokens (规划决策)
  - Creation: 12K tokens (写正文)
  - Review: 7K tokens (评审)
→ 平均节省 60% token 消耗
```

**3. 用户体验自然化**

```
传统系统:
  用户: "写到哪了？"
  系统: "当前状态: planning_chapter"
  用户: "...什么意思？"

NovelAgent:
  用户: "写到哪了？"
  Agent: "当前进度：第 1 章已提交，第 2 章规划中 (3.3%)"
  → 人类可理解的回复
```

**4. 失败恢复智能化**

```
传统系统:
  Error: "必须先调用 PlanChapter"
  → 用户手动修复

NovelAgent:
  Agent 发现缺少前置条件
    ↓
  自动推理: "需要先 PlanChapter"
    ↓
  自动执行 PlanChapter
    ↓
  继续原任务
  → 无感知恢复
```

---

## 五、技术指标

### 5.1 性能指标

| 指标 | 数值 | 说明 |
|------|------|------|
| **平均响应时间** | < 2s | Conversation 阶段 (状态查询) |
| **章节规划时间** | 5-8s | Planning 阶段 (生成 3 个候选) |
| **正文生成时间** | 15-30s | Creation 阶段 (3000 字章节) |
| **质量验证时间** | 3-5s | Review 阶段 (自动门禁) |
| **Token 消耗 (平均)** | 4.5K tokens/轮 | 相比全量加载节省 60% |
| **Session 最大步数** | 3-20 步 | 可在 settings 配置 |

### 5.2 质量指标

| 维度 | 保障机制 | 效果 |
|------|---------|------|
| **逻辑自洽** | 自动时间线检查 | 99% 无硬伤 |
| **字数达标** | 目标字数 ±10% | 95% 达标率 |
| **伏笔管理** | ForeshadowLedger 自动登记 | 100% 可追溯 |
| **风格一致** | StoryBible 约束 + RAG | 主观评估 8/10 |
| **节奏把控** | 段落节奏分析 | 主观评估 7/10 |

### 5.3 可扩展性

```csharp
// 新增工具示例: 只需 3 步
// Step 1: 定义工具
[KernelFunction]
[Description("生成角色卡片")]
public async Task<string> GenerateCharacterProfile(
    [Description("角色名称")] string name)
{
    // 实现逻辑
}

// Step 2: 注册到工具集
AgentToolRegistry.RegisterTool(
    "GenerateCharacterProfile", 
    phase: ConversationPhase.Planning);

// Step 3: Agent 自动发现并使用
// 用户: "生成主角林枫的角色卡"
// Agent 自动调用 GenerateCharacterProfile
```

---

## 六、总结

NovelAgent 通过 **Phase-Based Tool & Memory Layering** 实现了：

1. **智能化**: Agent 自主决策，而非硬编码流程
2. **精准化**: 阶段化加载上下文，节省 60% token
3. **交互化**: 自然对话 + 多粒度控制
4. **可靠性**: 质量门禁 + 智能恢复
5. **可扩展**: 新增工具无需修改核心逻辑

**设计哲学**:

> "不是限制 Agent，而是为 Agent 提供精准的上下文和工具集，让它在合适的阶段做合适的事。"

---

**附录**:
- 实现细节: `Docs/工程质量/工具与记忆分层设计.md`
- 单元测试: `Tests/Unit/Architecture/RuntimePurityTests.cs`
- 核心代码: `Web/NovelAgentWeb/Support/`
