# 小说 Agent 工程质量闭环

日期：2026-06-04

## 目标

让小说 Agent 不只是“能生成”，还要做到核心链路可构建、可测试、可回归。当前质量闭环分成两层：

- 核心 Agent 逻辑回归：不依赖 WPF，macOS / Windows 都可运行。
- Web 工作台构建与浏览器验证：作为当前主交互壳，不依赖 WindowsDesktop。
- 旧 WPF 壳：已删除，不再作为仓库维护对象。

## 新增测试工程

路径：

```text
Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

说明：

- 使用 `net8.0` console 测试工程，不引用主 WPF 项目，避免被 `Microsoft.NET.Sdk.WindowsDesktop` 阻塞。
- 直接包含 NovelAgent 核心模型和纯逻辑服务源码。
- 通过测试桩提供 `TM.App`、`StoragePathHelper`、`JsonHelper`，隔离项目全局 UI / 存储依赖。
- 不依赖 xUnit / NUnit / MSTest，当前受限网络环境下也能运行。

## 当前回归用例

已覆盖 15 个核心检查：

1. 题材风向规划能识别爽文、悬疑、情绪代价等核心类型承诺，并输出风险警告。
2. 整书创意设计器能生成带代价、禁区和排序规则的宏观候选。
3. 创意知识库能初始化内置种子、检索反套路策略，并召回项目已用桥段记忆。
4. `NovelAgentOrchestrator` 能跑通故事地基、宏观候选提交、章节规划、章节候选确认的半集成链路。
5. `NovelAgentOrchestrator` 能用 fake writer / fake reviewer 跑通确认后的章节生成、生成后复盘和 Rewrite 跳过链路。
6. Story Bible 能提交创意宪法，并能持久化角色目标、心理压力和数值归一化。
7. 章节复盘中的低风险角色变化能自动导入。
8. 高风险角色变化，如秘密揭露，必须停在确认点。
9. 卷级规划提交后能自动生成伏笔账本，并保持章节编号补零格式。
10. Canon Ledger 从 Proposed 升级 Canon 必须显式确认，并保留冲突检查说明。
11. 向量章节粗召回和 chunk 细召回能写入章节规划前的故事状态快照。
12. 商业节奏检查器能同时影响章节规划备注和生成后复盘检查项。
13. 情绪线与关系变化知识能影响章节候选和推荐理由。
14. 示例小说多章正文样本能驱动 RAG 相似片段、旧桥段重复和章节规划风险提示。
15. 示例小说 Story Bible 回归数据可反序列化，并包含卷规划、伏笔、角色账本。

## 示例小说回归数据

路径：

```text
Tests/Fixtures/NovelAgent/rules-debt-academy.story_bible.json
Tests/Fixtures/NovelAgent/rules-debt-academy.chapters.json
```

样例名：规则债务学院

覆盖内容：

- 整书创意宪法。
- 宏观创意候选。
- 第一卷卷级规划。
- 章节 Beat。
- 伏笔账本。
- 角色状态账本。
- Canon Ledger。
- 多章正文 chunk 样本。
- 已用桥段模式。
- RAG 相似正文片段和旧桥段重复风险。

注意：当前项目默认 JSON 配置未启用字符串枚举转换器，因此样例中的枚举使用数字格式，和生产序列化保持一致。

## 运行命令

构建测试工程：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet build /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

运行回归测试：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

当前验证结果：

```text
All 15 regression checks passed.
```

## Web 工作台

路径：

```text
Web/NovelAgentWeb/NovelAgentWeb.csproj
```

说明：

- 使用 `Microsoft.NET.Sdk.Web` + `net8.0`，不依赖 `Microsoft.NET.Sdk.WindowsDesktop`。
- 后端为 ASP.NET Core Minimal API，直接复用 NovelAgent 核心模型和服务。
- 前端位于 `Web/NovelAgentWeb/wwwroot`，按 Claude Code 官方 `frontend-design` skill 的方向实现为 Agent-first 作家工作台，而不是营销页或后台管理页。
- 页面拆分为三条用户心智清晰的产品入口：首页 `Agent 对话`，第二页 `素材参考`，第三页 `创作工作流`。
- 首页必须以 Agent 对话框为第一视觉重心；素材上传、Story Bible、卷规划、章节工坊都由 Agent 判断和引导，而不是让用户先面对一组后台菜单。
- `创作工作流` 页把故事地基、卷规划和章节工坊放在同一页，因为它们属于同一条小说创作生产线：先定大框架，再定卷级承诺和节拍，再产出章节候选与正文。
- `素材参考` 页支持上传或粘贴素材，当前先解析为摘要、标签和三段工作流参考，后续扩展为向量化素材库和引用溯源。
- 当前 Web 运行时用可替换适配器承接 writer / reviewer / guide context，保证大框架、卷规划、章节候选、确认、生成、素材引用、账本导入能先闭环运行。

构建 Web 工程：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet build /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb/NovelAgentWeb.csproj
```

启动 Web 工作台：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb/NovelAgentWeb.csproj
```

当前验证结果：

```text
NovelAgentWeb build succeeded.
```

## 本轮发现并修复的问题

问题：

- `CommitVolumeArc` 根据 `chapter-001` 和 Beat 序号推导伏笔兑现章节时，会得到 `chapter-24`，丢失原始章节编号补零格式。

影响：

- 后续章节文件、RAG 检索、伏笔回收和 UI 展示可能无法准确匹配 `chapter-024` 这类项目常用章节 ID。

修复：

- 在 `StoryBibleService.ResolveBeatChapterId` 中保留起始章节尾部数字 token 的长度。
- 当起始章节为 `chapter-001` 时，第 24 个 Beat 现在推导为 `chapter-024`。

问题：

- `NovelAgentOrchestrator.ApplySelectedMacroCandidate` 调用了未定义的 `FirstNonEmpty`，在能进入 C# 编译的环境会报错。
- `CommitStoryFoundationAsync` / `CommitVolumeArcAsync` 提交成功后会保存 Run 状态，但返回的 `StoryBibleCommitResult.Document` 仍可能是保存 Run 前的旧快照。

影响：

- UI 或插件调用提交操作后，可能拿到未包含最新 Run 状态的 Story Bible 文档。

修复：

- 为 `NovelAgentOrchestrator` 补齐 `FirstNonEmpty` helper。
- 在故事地基和卷级规划提交成功、保存 Run 后，重新加载 Story Bible 并写回 `StoryBibleCommitResult.Document`。

## 旧 WPF 壳删除状态

已删除范围：

```text
Core/
Framework/
Modules/
1.4.6-天命/
Scripts/
Docs/组件文档/
Docs/问题处理/
Docs/功能清单/业务跟新/
Storage/Framework/
Storage/Logs/
```

保留范围：

```text
Services/Framework/AI/NovelAgent/
Web/
Tests/
Storage/Projects/
Storage/Services/
Storage/Config/
```

说明：

- 当前仓库不再包含 `Core/App/天命.csproj`。
- 当前仓库不再需要 `Microsoft.NET.Sdk.WindowsDesktop`。
- Web 工作台和 NovelAgent 回归测试是当前主线验收对象。

## 下一步质量扩展

- 为 `Web/NovelAgentWeb` 增加浏览器级回归脚本，覆盖故事地基、卷规划、章节候选、章节生成和账本导入。
- 把 Web 运行时中的 fake writer / fake reviewer / fake guide context 替换为真实项目服务适配器。
- 扩展示例小说生成后复盘样本，用于 Proposed Canon、伏笔回收、角色状态变化的端到端回归。
