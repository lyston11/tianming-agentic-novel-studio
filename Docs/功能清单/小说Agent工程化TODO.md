# 小说 Agent 工程化 TODO

项目路径：

```text
/Users/lyston/PycharmProjects/tianming-agentic-novel-studio
```

当前目标：在保留小说写作主线的前提下，把工作流升级为 Agent 主导的长篇小说创作系统。短线文章写作暂不作为当前主线。

## P0 环境与编译

- [x] 安装 `.NET 8 SDK`。
- [x] 将主交互壳从 WPF 调整为 Web App，后续不再以 WindowsDesktop 作为主开发目标。
- [x] 删除旧 WPF 桌面壳、桌面 UI 框架、历史桌面模块和历史版本副本。
- [x] 新增不依赖 WPF / WindowsDesktop 的 Web 工作台工程：

```text
Web/NovelAgentWeb/NovelAgentWeb.csproj
```

- [x] 执行 Web 编译验证：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet build /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb/NovelAgentWeb.csproj
```

- [x] 启动 Web 工作台并完成浏览器/API 链路验证：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb/NovelAgentWeb.csproj
```

- [ ] 修复新增 `NovelAgent` 模块可能出现的编译错误。
- [ ] 检查 `NovelAgentPlugin` 的 Semantic Kernel 函数签名是否和当前项目依赖版本兼容。
- [ ] 确认 `NovelAgent` 插件能在聊天工具调用列表中出现。
- [x] 新增不依赖 WPF 的 NovelAgent 核心回归测试工程。
- [x] 构建 NovelAgent 核心回归测试工程：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet build /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

- [x] 执行 NovelAgent 核心回归测试：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

当前编译状态：

- macOS 上已安装 Homebrew `dotnet@8`，版本 `8.0.127`。
- NuGet restore 已成功。
- 当前主线 Web 工程 `NovelAgentWeb` 已构建通过，不依赖 `Microsoft.NET.Sdk.WindowsDesktop`。
- 旧 WPF 桌面壳已删除，当前仓库不再包含 `Core/App/天命.csproj`，不再需要 `Microsoft.NET.Sdk.WindowsDesktop`。
- NovelAgent 核心回归测试工程已构建通过，15 个回归检查全部通过。

## P0 当前已完成

- [x] 从原项目复制新项目。
- [x] 新项目命名为 `tianming-agentic-novel-studio`。
- [x] 创建 `Services/Framework/AI/NovelAgent` 模块。
- [x] 新增 `NovelAgentRun` 运行模型。
- [x] 新增 `StoryCreativeConstitution` 故事创意宪法模型。
- [x] 新增 `GenreDirectionPlanner` 类型风向规划器。
- [x] 新增 `BookConceptDesigner` 整书创意设计器。
- [x] 新增 `ChapterNoveltyPlanner` 章节创意简报规划器。
- [x] 新增 `NovelAgentOrchestrator` Agent 编排器。
- [x] 新增 `NovelAgentPlugin` Semantic Kernel 插件。
- [x] 新增 `StoryBibleService` 项目级持久化服务。
- [x] 新增 `StoryBibleDocument` 和 `CanonLedgerEntry` 数据模型。
- [x] 新增 `StoryStateSnapshotService` 章节故事状态快照服务。
- [x] 新增 `StoryStateSnapshot` RAG 上下文摘要模型。
- [x] 新增 `StoryStateSnapshot.SimilarContentFragments` 相似正文片段检索结果。
- [x] 新增 `ChapterPostGenerationReviewer` 章节生成后复盘服务。
- [x] 新增 `NovelAgentPostGenerationReview` 结构化质量报告模型。
- [x] 新增 `NovelAgentRewriteLoopService` 质量改写闭环服务。
- [x] 新增 `NovelAgentRewriteAttempt` 改写尝试记录模型。
- [x] 新增 `CanonMaintenanceService` 世界观增量维护服务。
- [x] 新增 `CreativeKnowledgeBaseService` 创意知识库服务。
- [x] 新增 `CreativeKnowledgeBaseDocument`、`CreativeKnowledgeEntry` 和检索结果模型。
- [x] 新增 `VolumeArcPlanner` 卷级大框架规划器。
- [x] 新增 `VolumeArcPlan`、`VolumeChapterBeat`、`VolumeForeshadowPlan` 和 `VolumeCharacterArc` 卷级规划模型。
- [x] 新增 `ForeshadowLedgerService` 伏笔账本维护服务。
- [x] 新增 `ForeshadowLedgerEntry`、`ForeshadowMaintenanceResult` 和伏笔状态机模型。
- [x] 新增 `CharacterLedgerService` 角色状态账本维护服务。
- [x] 新增 `CharacterLedgerEntry`、`CharacterMaintenanceResult`、角色秘密/关系/能力代价/心理状态模型。
- [x] 注册 NovelAgent 相关服务到依赖注入。
- [x] 注册 `NovelAgent` 插件到 Kernel。
- [x] 新增 `Web/NovelAgentWeb` 网站工作台。
- [x] 新增 Minimal API：Story Bible、Agent Runs、故事地基、卷规划、章节候选、章节生成、复盘、Canon/伏笔/角色账本维护。
- [x] 新增 Agent-first 多页面 Web 前端：首页是 Agent 对话框，第二页是素材参考库，第三页是故事地基 + 卷规划 + 章节工坊的一体化创作工作流。
- [x] 每个页面共享 Agent 状态、Story Bible 摘要、运行指标和统一侧边导航，避免把完整创作链路拆散成后台菜单。
- [x] 新增素材参考入库闭环：支持上传/粘贴素材，由 Agent 拆解为故事地基、卷规划、章节工坊可引用的工作流参考。
- [x] 接入 Claude Code 官方 `frontend-design` skill 作为 Web UI 设计约束，替代原 `Uncodixfy` skill。

## 大工程闭环清单

> 这里的 Todo 按“能单独交付、能跑通一条业务链路”的粒度维护，不再把每个字段或小函数拆成一个 Todo。

- [x] **故事地基闭环：从灵感到 Story Bible 可确认落盘**
  - 包含：整书创意宪法、类型风向、宏观候选、Canon Ledger、版本记录、高风险确认、Agent Run 持久化。
- [x] **章节规划闭环：从 Story Bible + RAG 状态到章节创意简报**
  - 包含：章节目标读取、事实快照摘要、历史章节摘要、长距离召回、已用桥段、活跃冲突、未回收伏笔、创意简报确认。
- [x] **章节生成执行闭环：从确认简报到 WriterPlugin 落盘生成**
  - 包含：人工确认、Agent Run 状态推进、调用 `WriterPlugin.GenerateChapter`、复用现有强一致生成/保存/索引更新链路。
- [x] **生成后质量复盘闭环：从已生成正文到结构化审计报告**
  - 包含：读取生成正文、复用 `UnifiedValidationService`、检查旧桥段重复、Story Bible 禁止方向、未确认新设定、代价/后果、故事变量变化、下一章建议、需要改写时进入 `Repairing` 状态。
- [x] **改写闭环：从质量复盘失败到自动/半自动 Rewrite Loop**
  - 包含：按失败项构造改写任务、调用现有 `AutoRewriteEngine`、复用 GenerationGate、保存修订、重跑复盘、限制最大轮次、保留改写原因。
- [x] **创意候选闭环：从单一规则简报升级为知识库增强多候选评审**
  - 包含：每章 6 个候选、候选评分、Agent 推荐、用户选择/混合、商业节奏规划备注、选择结果写回 Run、未确认不得生成正文。
- [x] **知识库闭环：类型知识、套路库、反套路策略和项目记忆可检索**
  - 包含：项目级创意知识库、内置知识种子、类型原则、套路桥段库、反套路策略库、项目已用桥段库、章节规划前检索、候选评分接入。
- [x] **世界观增量闭环：正文中新设定从 Proposed 到 Canon / Rejected**
  - 包含：检测新设定、提出 Proposed、冲突检查、人工确认、升级 Canon、拒绝项标记为 Rejected 并屏蔽为后续依据。
- [x] **Agent 自动推进闭环：低风险步骤自动执行，高风险步骤停在确认点**
  - 包含：ContinueAgentRun、风险上限、步骤自动刷新、生成后复盘自动执行、Proposed 自动导入、候选/生成/改写/Canon 变更确认前暂停。
- [x] **RAG 相似正文闭环：章节规划前检索真实正文片段并纳入创意评分**
  - 包含：ContentChunkSearchService 接入、ChapterEmbeddingIndex 章节向量粗召回、ChunkEmbeddingIndex chunk 向量细召回、RAG query 记录、相似正文片段摘要、候选重复风险扣分、简报相似内容风险提示。
- [x] **卷级大框架闭环：先定一卷的承诺、节拍、反转、高潮和伏笔，再写章节**
  - 包含：PlanVolumeArc、CommitVolumeArc、卷级高风险确认、同卷覆盖保护、卷级节拍写入 Story Bible、章节规划自动读取当前卷和章节 Beat、候选评分接入卷级节拍匹配。
- [x] **伏笔账本闭环：把卷级伏笔计划和章节复盘中的伏笔变化沉淀为可追踪 Ledger**
  - 包含：ForeshadowLedger 持久化、卷级伏笔自动导入 Planned、章节规划前召回活跃/临近到期伏笔、生成后复盘提出伏笔变化、低风险投放/强化自动导入、高风险回收/废弃等待确认、插件工具和自动推进接入。
- [x] **角色状态账本闭环：把人物目标、秘密、关系、能力代价和心理变化沉淀为可追踪 Ledger**
  - 包含：CharacterLedger 持久化、章节规划前召回角色状态压力、生成后复盘提出角色变化、低风险目标/关系/心理/能力变化自动导入、高风险死亡/身份改写/秘密揭露/关系反转/能力规则变化等待确认、插件工具和自动推进接入。
- [x] **Agent Web UI 闭环：把 Story Bible、Run、简报、复盘、确认流变成可操作网站**
  - 已完成：新增 ASP.NET Core `net8.0` Web 工作台，不再依赖 WPF / WindowsDesktop。
  - 已完成：拆成 Agent-first 三页产品结构：首页 `Agent 对话`，第二页 `素材参考`，第三页 `创作工作流`。
  - 已完成：首页打开即对话，Agent 根据 Story Bible、素材库和最近 Run 判断下一步应该补地基、收素材、推卷规划还是生成章节候选。
  - 已完成：素材页支持上传/粘贴文本素材，解析为摘要、标签和故事地基/卷规划/章节工坊三类引用。
  - 已完成：创作工作流页把故事地基、卷级规划、章节候选、确认、生成、账本导入、Run 步骤轨道和事件流放在同一个连续流程里。
  - 待增强：真实项目章节库接入、真实 WriterPlugin Web 适配、Proposed 设定逐条确认 UI、复盘报告详情页、混合候选编辑器、素材向量化召回与引用溯源。
- [x] **工程质量闭环：可构建、可测试、可回归**
  - 已完成：新增不依赖 WPF 的 `Tests/NovelAgentRegression` 核心回归测试工程，覆盖题材风向、整书创意、创意知识库、Orchestrator 半集成、章节 fake 生成执行、Story Bible、角色状态账本、卷级伏笔导入、Canon 高风险确认、向量 RAG、商业节奏、情绪/关系知识影响候选、示例小说回归数据加载。
  - 已完成：新增 `Tests/Fixtures/NovelAgent/rules-debt-academy.story_bible.json` 示例小说回归数据。
  - 已完成：新增 `Tests/Fixtures/NovelAgent/rules-debt-academy.chapters.json` 多章正文样本，用于 RAG 相似片段、旧桥段重复和章节规划风险提示回归。
  - 已完成：新增 `Docs/工程质量/小说Agent工程质量闭环.md` 质量说明、命令、已知限制和后续扩展。
  - 已完成：修复卷级伏笔章节 ID 推导丢失补零格式的问题，`chapter-001` + 第 24 个 Beat 现在推导为 `chapter-024`。
  - 已完成：修复 `NovelAgentOrchestrator.ApplySelectedMacroCandidate` 缺失 `FirstNonEmpty` 导致的潜在 C# 编译错误。
  - 已完成：修复 `CommitStoryFoundationAsync` / `CommitVolumeArcAsync` 成功后返回的 `StoryBibleCommitResult.Document` 不是最新 Run 状态快照的问题。
  - 当前主线验收：Web 工作台构建、浏览器点击链路、API 端到端、核心回归测试。
  - 历史桌面壳：已删除，不再作为验收对象。

## P1 Story Bible 与设定账本

- [x] 设计 `StoryBibleService`。
- [x] 确定 Story Bible 的项目内存储路径。
- [x] 将 `StoryCreativeConstitution` 保存为项目级设定。
- [x] 支持读取当前 Story Bible。
- [x] 支持更新 Story Bible。
- [x] 支持 Story Bible 版本记录。
- [x] 设计 Canon Ledger 数据结构。
- [x] 支持设定状态：
  - [x] `Draft`
  - [x] `Proposed`
  - [x] `Canon`
  - [x] `Deprecated`
  - [x] `Conflict`
- [x] 新增设定进入 Canon 前必须做冲突检查。
- [x] 高风险设定变更必须要求用户确认。

## P1 Agent 编排闭环

- [x] 将 `PlanStoryFoundation` 接入真实 Story Bible 创建流程。
- [x] 将 `PlanChapterCreativeBrief` 接入真实章节生成前置流程。
- [x] 建立 Agent Run 持久化。
- [x] 支持 Agent Run 恢复。
- [x] 支持 Agent Run 取消。
- [x] 支持 Agent Run 步骤级日志。
- [x] 支持 Agent Run 步骤风险等级展示。
- [x] 支持高风险步骤人工确认。
- [x] 支持低风险步骤自动执行。
- [x] 支持卷级大框架规划、确认和提交。
- [x] 支持伏笔账本自动导入和高风险伏笔状态确认。
- [x] 支持角色状态账本自动导入和高风险角色状态确认。

## P1 RAG 上下文接入

- [x] 接入 `GuideContextService` 读取项目设定。
- [x] 接入 `FactSnapshot` 读取事实快照。
- [x] 接入伏笔账本。
- [x] 接入角色状态账本。
- [x] 接入历史章节摘要。
- [x] 接入 `ChapterEmbeddingIndex`。
- [x] 接入 `ChunkEmbeddingIndex`。
- [x] 接入 `ContentChunkSearchService` 检索相似正文片段。
- [x] 接入 `LongDistanceRecall`。
- [x] 在章节规划前检索相似桥段。
- [x] 在章节规划前检索相似正文片段。
- [x] 在章节规划前执行章节向量粗召回。
- [x] 在章节规划前执行 chunk 向量细召回。
- [x] 在章节规划前检索相关设定。
- [x] 在章节规划前检索未回收伏笔。
- [x] 在章节规划前强制召回 Story Bible 伏笔账本。
- [x] 在章节规划前强制召回 Story Bible 角色状态账本。
- [x] 在章节规划前读取已确认卷级节拍。
- [x] 在章节规划前输出上下文摘要。

## P1 卷级大框架规划

- [x] 新增 `VolumeArcPlanningRequest`。
- [x] 新增 `VolumeArcPlan` 卷级规划模型。
- [x] 支持卷级读者承诺。
- [x] 支持卷级进入状态和退出状态。
- [x] 支持卷级核心问题。
- [x] 支持卷级冲突升级。
- [x] 支持中段反转设计。
- [x] 支持卷末高潮设计。
- [x] 支持余波和下一卷钩子。
- [x] 支持章节 Beat 序列。
- [x] 支持卷级伏笔投放和回收计划。
- [x] 支持卷级角色弧。
- [x] 支持卷级世界观增量。
- [x] 支持卷级 Must Avoid 约束。
- [x] 支持 `PlanVolumeArc` 插件工具。
- [x] 支持 `CommitVolumeArc` 插件工具。
- [x] 卷级规划提交需要用户确认。
- [x] 同一 `volumeId` 覆盖需要显式允许。
- [x] 章节规划自动匹配当前卷。
- [x] 章节规划自动匹配当前章节 Beat。
- [x] 章节候选评分接入卷级 Beat 匹配。

## P1 伏笔账本闭环

- [x] 新增 `ForeshadowLedgerEntry` 伏笔账本模型。
- [x] 支持伏笔类型：
  - [x] `Plot`
  - [x] `WorldRule`
  - [x] `CharacterSecret`
  - [x] `Relationship`
  - [x] `Object`
  - [x] `Threat`
  - [x] `Theme`
  - [x] `Other`
- [x] 支持伏笔状态：
  - [x] `Draft`
  - [x] `Proposed`
  - [x] `Planned`
  - [x] `Setup`
  - [x] `Reinforced`
  - [x] `Due`
  - [x] `PaidOff`
  - [x] `Abandoned`
  - [x] `Conflict`
- [x] `StoryBibleDocument` 持久化 `ForeshadowLedger`。
- [x] `CommitVolumeArc` 自动把卷级伏笔计划导入伏笔账本。
- [x] `StoryStateSnapshotService` 章节规划前召回活跃伏笔和临近到期伏笔。
- [x] `ChapterPostGenerationReviewer` 生成后提出 `ProposedForeshadowEntries`。
- [x] `ForeshadowLedgerService` 支持从复盘导入低风险伏笔变化。
- [x] 高风险 `PaidOff` / `Abandoned` / `Conflict` 状态需要用户确认。
- [x] `NovelAgentOrchestrator` 支持 `ImportForeshadowFromReview`。
- [x] `NovelAgentOrchestrator` 支持 `ConfirmForeshadowStatusFromRun`。
- [x] `ContinueAgentRun` 可自动导入低风险伏笔变化。
- [x] 新增 `AddForeshadowLedgerEntry` 插件工具。
- [x] 新增 `UpdateForeshadowLedgerStatus` 插件工具。

## P1 角色状态账本闭环

- [x] 新增 `CharacterLedgerEntry` 角色状态账本模型。
- [x] 支持角色状态类型：
  - [x] `Goal`
  - [x] `Secret`
  - [x] `Relationship`
  - [x] `AbilityCost`
  - [x] `Psychology`
  - [x] `Belief`
  - [x] `Identity`
  - [x] `Role`
  - [x] `Death`
  - [x] `Other`
- [x] 支持角色状态：
  - [x] `Draft`
  - [x] `Proposed`
  - [x] `Active`
  - [x] `GoalUpdated`
  - [x] `SecretSeeded`
  - [x] `SecretRevealed`
  - [x] `RelationshipChanged`
  - [x] `RelationshipReversed`
  - [x] `AbilityChanged`
  - [x] `AbilityRuleChanged`
  - [x] `PsychologicalShifted`
  - [x] `BeliefShifted`
  - [x] `IdentityRewritten`
  - [x] `LeftStage`
  - [x] `Dead`
  - [x] `Conflict`
  - [x] `Rejected`
- [x] `StoryBibleDocument` 持久化 `CharacterLedger`。
- [x] `StoryStateSnapshotService` 章节规划前召回角色目标、秘密、关系、能力代价和心理压力。
- [x] `ChapterPostGenerationReviewer` 生成后提出 `ProposedCharacterEntries`。
- [x] `CharacterLedgerService` 支持从复盘导入低风险角色变化。
- [x] 高风险 `SecretRevealed` / `RelationshipReversed` / `AbilityRuleChanged` / `IdentityRewritten` / `LeftStage` / `Dead` / `Conflict` / `Rejected` 状态需要用户确认。
- [x] `NovelAgentOrchestrator` 支持 `ImportCharacterStateFromReview`。
- [x] `NovelAgentOrchestrator` 支持 `ConfirmCharacterStateFromRun`。
- [x] `ContinueAgentRun` 可自动导入低风险角色状态变化。
- [x] 新增 `AddCharacterLedgerEntry` 插件工具。
- [x] 新增 `UpdateCharacterLedgerStatus` 插件工具。

## P1 章节创意简报

- [x] 将 `ChapterCreativeBrief` 从单一规则模板升级为知识库 + 规则混合生成。
- [x] 每章生成至少 3 个剧情候选。
- [ ] 对剧情候选评分：
  - [x] 新鲜度
  - [x] 一致性
  - [x] 戏剧张力
  - [x] 套路风险
  - [x] 类型匹配度
  - [x] 知识库支持 / 风险
  - [x] 相似正文重复风险
- [x] 支持用户选择候选。
- [x] 支持 Agent 推荐最佳候选并解释理由。
- [x] 支持用户混合多个候选。
- [x] 简报确认后再允许调用 `Writer.GenerateChapter`。

## P2 创意系统增强

- [x] 建立类型知识库。
- [x] 建立爽文节奏知识库。
- [x] 建立悬疑 / 烧脑线索知识库。
- [x] 建立群像势力博弈知识库。
- [x] 建立情绪线与关系变化知识库。
- [x] 建立套路桥段库。
- [x] 建立反套路策略库。
- [x] 建立已用桥段模式库。
- [x] 建立创意新鲜度评分器。
- [x] 建立读者承诺检查器。
- [x] 建立商业节奏检查器。
- [x] 建立主题深度检查器。

## P2 章节生成闭环

- [x] Agent 确认章节简报后调用 `WriterPlugin.GenerateChapter`。
- [x] 生成后进入 Critic 检查。
- [x] 检查是否重复旧桥段。
- [x] 检查是否违反 Story Bible。
- [x] 检查是否产生未确认新设定。
- [x] 检查是否有反派降智、无代价开挂、机械反转。
- [x] 检查是否每章改变了至少一个故事变量。
- [x] 检查爽点兑现、线索推进、情绪回报、章末钩子和连续阅读驱动力。
- [x] 不通过则进入 Rewrite Loop。
- [x] 通过后进入 `GenerationGate`。
- [x] 保存章节。
- [x] 提取事实快照。
- [x] 更新向量索引。
- [x] 生成下一章建议。

说明：

- `GenerationGate`、保存章节、事实快照提取、向量索引更新由现有 `WriterPlugin.GenerateChapter` 与 `ContentGenerationCallback` 链路复用，Agent 层不重复实现。
- 当前 Critic 是规则 + 现有校验服务的工程保底版本，后续在“创意候选闭环”和“知识库闭环”中升级为 LLM + RAG 混合评审。

## P2 世界观增量维护

- [ ] 写作中检测世界观缺口。
- [x] 自动生成 Proposed 设定。
- [x] 标明新增设定的动机。
- [x] 标明新增设定影响范围。
- [x] 检查和现有 Canon 是否冲突。
- [x] 用户确认后写入 Canon Ledger。
- [x] 被拒绝的设定不得进入正文依据。
- [ ] 已确认设定触发索引更新。

## P3 UI 与交互

- [x] 新增 Agent Run 面板。
- [x] 新增 Story Bible 面板。
- [x] 新增宏观创意候选选择界面。
- [x] 新增章节创意简报确认界面。
- [x] 新增 Proposed 设定确认界面。
- [x] 新增 Agent 步骤时间线。
- [x] 新增高风险操作确认弹窗。
- [x] 新增章节生成后质量报告。
- [x] 新增下一章建议面板。

## P3 测试与质量

- [x] 为 `GenreDirectionPlanner` 添加单元测试。
- [x] 为 `BookConceptDesigner` 添加单元测试。
- [x] 为 `CreativeKnowledgeBaseService` 添加种子知识库和项目记忆检索测试。
- [x] 为 `ChapterNoveltyPlanner` 添加核心回归测试。
- [x] 为 `NovelAgentOrchestrator` 添加半集成测试。
- [x] 为 Story Bible 持久化添加测试。
- [x] 为 Proposed -> Canon 流程添加测试。
- [x] 为章节生成 Agent 流程添加 fake writer / fake reviewer 集成测试。
- [x] 为 RAG 检索上下文构建添加核心回归测试。
- [x] 加入示例小说项目作为回归测试数据。
- [x] 加入多章正文样本作为 RAG 相似片段、旧桥段重复和章节规划风险回归数据。
- [x] 为角色状态账本低风险导入和高风险确认添加核心回归测试。
- [x] 为卷级规划提交自动创建伏笔账本添加核心回归测试。
- [x] 为 `StoryStateSnapshotService` 向量章节粗召回 + chunk 细召回添加核心回归测试。
- [x] 为 `CommercialRhythmChecker` 商业节奏规划和复盘添加核心回归测试。
- [x] 新增工程质量说明文档。

## P4 短线文章写作扩展

- [ ] 暂不进入主线。
- [ ] 后续可复用 Agent Run、RAG、创意简报、事实校验能力。
- [ ] 单独设计 ArticleAgent，不要污染 NovelAgent 主流程。
- [ ] 保持 NovelAgent 以长篇小说创作为第一优先级。

## 当前关键文件

```text
Services/Framework/AI/NovelAgent/Models/NovelAgentRun.cs
Services/Framework/AI/NovelAgent/Models/CanonMaintenanceModels.cs
Services/Framework/AI/NovelAgent/Models/CreativeKnowledgeModels.cs
Services/Framework/AI/NovelAgent/Models/CharacterLedgerModels.cs
Services/Framework/AI/NovelAgent/Models/VolumeArcModels.cs
Services/Framework/AI/NovelAgent/Models/ForeshadowLedgerModels.cs
Services/Framework/AI/NovelAgent/Models/PostGenerationReviewModels.cs
Services/Framework/AI/NovelAgent/Models/RewriteLoopModels.cs
Services/Framework/AI/NovelAgent/Models/StoryCreativeConstitution.cs
Services/Framework/AI/NovelAgent/Models/CreativePlanningModels.cs
Services/Framework/AI/NovelAgent/Services/GenreDirectionPlanner.cs
Services/Framework/AI/NovelAgent/Services/BookConceptDesigner.cs
Services/Framework/AI/NovelAgent/Services/CanonMaintenanceService.cs
Services/Framework/AI/NovelAgent/Services/ForeshadowLedgerService.cs
Services/Framework/AI/NovelAgent/Services/CharacterLedgerService.cs
Services/Framework/AI/NovelAgent/Services/CommercialRhythmChecker.cs
Services/Framework/AI/NovelAgent/Services/ChapterNoveltyPlanner.cs
Services/Framework/AI/NovelAgent/Services/ChapterPostGenerationReviewer.cs
Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs
Services/Framework/AI/NovelAgent/Services/VolumeArcPlanner.cs
Services/Framework/AI/NovelAgent/Services/NovelAgentRewriteLoopService.cs
Services/Framework/AI/NovelAgent/Services/NovelAgentOrchestrator.cs
Services/Framework/AI/NovelAgent/Plugins/NovelAgentPlugin.cs
Web/NovelAgentWeb/NovelAgentWeb.csproj
Web/NovelAgentWeb/Support/AgentRouter.cs
Web/NovelAgentWeb/Support/WebRuntime.cs
Web/NovelAgentWeb.Frontend/src/App.tsx
Web/NovelAgentWeb.Frontend/src/pages/AgentPage.tsx
Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx
Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx
Tests/NovelAgentRegression/NovelAgentRegression.csproj
Tests/NovelAgentRegression/Program.cs
Tests/NovelAgentRegression/TestInfrastructure.cs
Tests/NovelAgentRegression/ProjectDataStubs.cs
Tests/Fixtures/NovelAgent/rules-debt-academy.story_bible.json
Docs/工程质量/小说Agent工程质量闭环.md
```

## 最近下一步

建议下一轮开发顺序：

1. 启动 `Web/NovelAgentWeb`，用浏览器验证：创建故事地基、提交宏观候选、创建卷规划、提交卷规划、创建章节规划、确认候选、执行章节生成、导入 Canon/伏笔/角色账本。
2. 将 Web 运行时支撑层中的 fake writer / fake reviewer / fake guide context 替换为真实项目服务适配器。
3. 扩展示例小说回归数据：加入生成后复盘样本，用于 Proposed Canon、伏笔回收、角色状态变化的端到端回归。
