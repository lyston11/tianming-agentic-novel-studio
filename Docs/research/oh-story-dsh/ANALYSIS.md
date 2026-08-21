# oh-story-dsh 深度研究分析

> 研究日期:2026-08-21
> 上游:https://github.com/worldwonderer/oh-story-dsh.git
> 快照 commit:`02f48c1f4f390598d377e3c2027527c76c51bc06`(2026-08-20)
> 源码快照:本目录 `repo/`(已去除 `.git`,上游 manifest 自带 commit 钉定与文件 hash)

## 一、总体架构:三层结构

```
┌─────────────────────────────────────────────────────┐
│ ③ 宿主桥接层(~1950 行 TS)                            │
│    Skill Provider / Role 工具 / hooks / 文件路由 / UI │
│    repo/packages/dsh-plugin/src/                     │
├─────────────────────────────────────────────────────┤
│ ② 流程剧本层(13 SKILL.md,纯文本 prompt)              │
│    固定 Phase 序列 + 意图路由 + 停靠点 + 降级策略      │
│    repo/packages/knowledge/oh-story/skills/*/SKILL.md│
├─────────────────────────────────────────────────────┤
│ ① 知识资产层(references/,~3.5MB 纯文本+脚本)         │
│    方法论文档 / 题材卡 / 追踪协议 / 校验脚本           │
│    repo/packages/knowledge/                          │
└─────────────────────────────────────────────────────┘
```

关键认知:**这个插件真正的"生产内核"是第①层**。第②层只是路由和编排,第③层只是挂载。知识资产约 3.5MB,其中 `story-long-write` 一个 Skill 就占 1.1MB/81 个文件。

### 宿主桥接层(5 个 Host 贡献 + 2 个 Browser 槽)

| 贡献 | 文件(dsh-plugin/src/) | 职责 |
|------|----------|------|
| oh-story Skill Provider | `skill-provider.ts` | 13 小说 SKILL.md,加载时前置 DSH bridge 文本(禁止第二 runtime/Dashboard/SSE),个别 Skill 注入 override |
| short-drama Skill Provider | `skill-provider.ts` | 10 短剧 SKILL.md |
| `oh_story_role` 工具 | `role-tool.ts` | 7 个 Role 作为 spawn 子 Agent;每 Role 工具白名单;maxDepth:1;完成后 dispose |
| pre/post-execute hooks | `native-hooks.ts` | 写正文前校验对应细纲存在(缺则 deny);写后注入提醒更新 `_tracking-state.json` |
| `/oh-story/*` HTTP 路由 | `workspace-route.ts` | 工作台文件 API;创作目录白名单+扩展名白名单;sha256 baseVersion 乐观锁 PUT;tmp+rename 原子写 |

Browser 侧(`client/index.tsx`):三栏创作工作台 portal 到官方会话视图旁(不替换 Chat);从 `ConversationSnapshot.runningCalls/partial` 解码流式 write/edit 参数实现"文件跟随";human-dirty buffer 优先于 Agent 更新。

## 二、核心发现 1:文件系统即数据库

`story-long-write` 定义完整项目文件契约(产物映射表:文件×粒度×创建阶段×读取时机):

```
{书名}/
├── 设定/     角色一人一文件、势力一组织一文件、世界观按主题拆分
├── 大纲/     卷纲一卷一文件、细纲一章一文件(含钩子设计)
├── 正文/     一章一文件
├── 对标/     拆文产出的结构化参照(角色/剧情/设定/文风/节奏/情绪模块)
├── 追踪/     _tracking-state.json(唯一权威)+ 派生视图
└── 参考资料/  researcher 输出
```

三个精巧机制:

- **按需加载**:SKILL.md 只常驻路由表,references 按 Phase/场景索引加载,"只加载不知道就会写错的信息"——LLM 上下文预算管理;
- **权威优先级链**:`剧情/情绪模块.md` > `剧情/节奏.md` > `文风.md` > `章节摘要` > `拆文报告`;冲突时高权威胜出并记录 `gaps.conflict`;
- **fail-fast 缺失处理**:主产物缺失 → 停止并给出 `repair_action`(如"重跑 analyze Stage 3+");可选产物缺失 → 跳过并记录缺口。绝不拼装降级结果假装召回成功。

## 三、核心发现 2:追踪状态机(最工程化的部分)

`tracking_commit.py`(1139 行)实现了小型事务系统,本质是把数据库的 WAL/事务/物化视图思想搬到文件系统:

| 设计点 | 实现 |
|--------|------|
| 单一权威 | `_tracking-state.json`(schema v4)存全部当前状态;Markdown 一律是派生视图 |
| 确定性派生 | `上下文.md`(固定7栏≤12KB)、角色快照(目标4KB硬上限8KB)、伏笔、双时间线(作者真相/读者已知)全由工具渲染;禁止手改、禁止程序反向解析 Markdown |
| 事务提交 | 模型只提交语义 JSON delta → 工具内存合并校验 → 渲染全部视图 → 原子替换权威 JSON 作为唯一提交点 |
| 乐观并发 | `expected_state_revision` 拒绝基于旧状态的 stale 事务(单写者串行,非并发锁) |
| 幂等重放 | 写入失败→原事务直接重跑;append 只接受内容完全相同的既有逐章记录 |
| 退役登记 | 角色/约束退役必须显式声明(`retired_characters`/`retired_context_items`),漏写即拒——防隐式删除 |
| 中途快照 | 每 3 章强制 check 全量一致性 |

## 四、核心发现 3:创作方法论资产

- **题材正文卡体系**:30 张单题材卡(`story-long-write/references/genre-prose-cards/`:都市脑洞/东方仙侠/年代/双男主…),固定结构:正文提示词/开场抓手/冲突发动机/爽点与情绪释放/对话声线;frontmatter 带 `confidence: high/medium/low`;缺失时从题材定位即时生成低置信卡并标注。
- **写作方法参考**(40+ 文件):爽点六类型倒推法、高潮三段公式(蓄能→假胜→崩解)、反转工具箱、钩子三级体系(章首/章尾/段落级)、读者契约与期待债、金手指战力防崩、女频专项、黄金三章设计等。
- **去 AI 味体系**(story-deslop):7 Gate 分类(禁用词A/句式套路B/心理告知C/节奏均匀D/对话腔调E/结尾升华F/解释腔G)+ 最毒句式速查(「不是A而是B」全家族、声线反差、预告式收尾)+ JS 确定性扫描脚本(`check-ai-patterns.js` 等)+ 分级处置。
- **对抗式审查**(story-review):多 reviewer 并行 spawn → 统一 Findings Schema(location 用原始行号)→ 综合裁决时分歧呈现给用户而非自动妥协 → 跨批审查继承未解决项(state.md 注入下一批 prompt)。

## 五、核心发现 4:Role 子代理体系

7 个 Role 各有职责边界与工具白名单(`role-tool.ts`):

| Role | 职责 | 工具 |
|------|------|------|
| narrative-writer | 正文写作+去AI味(30K 最大人格:7 Gate 自检、元信息隔离、节长达标硬门槛) | read/glob/grep/bash |
| story-architect | 题材/世界观/大纲/细纲蓝图/反转工程 | read/glob/grep |
| consistency-checker | 只读一致性审查,S1-S4 分级冲突报告 | read/glob/grep |
| story-explorer | 项目结构化查询(上下文加载/文风召回快捷路径) | read/glob/grep |
| chapter-extractor | 拆解管道 Stage 2 并行调用 | read/glob/grep |
| character-designer | 角色/对话/动机链 | read/glob/grep |
| story-researcher | 外部资料研究,带来源引用 | +web_search/web_fetch |

细节:narrative-writer 头部注释明确"不加载 story-review,因为 Claude Code subagent 不允许嵌套 spawn"——人格设计受宿主能力约束且被显式文档化。

## 六、核心发现 5:防失控的流程控制

SKILL.md 层的"停靠点"设计:

- **裸调用诊断**:无明确意图时只做状态诊断+列选项,不自动写作;
- **开书默认停靠**:Phase 1-3 完成+首批10章细纲后停止,除非用户同句明确要正文;
- **批量上限**:日更默认 2-3 章,用户给 N 也单轮最多 3 章;
- **匹配优先级**:大修>写指定章>补纲>日更>开书;歧义时列表让用户选,不开放式提问;
- **workflow 锁定**:进入日更流程后"继续/续写"都在流程内;正常批量不询问是否继续,只有细纲缺失/章节冲突/改大纲才暂停确认。

## 七、弱点与局限

1. **无持久化保证**:状态全在文件系统,无备份/历史/回滚(git 除外);`_tracking-state.json` 损坏即丢全部当前状态;
2. **单写者假设**:明确不支持多 Agent 并发写一本书;
3. **平台耦合残留**:上游 Skill 仍带 `.claude/agents` 探测逻辑与 6 平台部署变体(story-setup),DSH 桥接只能覆盖不能根除;
4. **剧本即脆弱点**:13 步单章流程依赖模型严格遵循长指令,漂移风险随流程长度上升;hook 兜底只覆盖"无细纲禁写"一个检查点;
5. **遗留资产**:CDP 爬虫脚本仍在包里但 DSH 侧被排除。

## 八、可迁移的设计模式(与具体平台无关)

1. **单一权威状态 + 确定性派生视图 + 事务提交** —— 可映射为"PostgreSQL 权威 + Read Model 投影";
2. **产物映射表**(文件×粒度×创建阶段×读取时机)—— 知识资产的 schema 化组织方式;
3. **fail-fast 缺失契约 + repair_action** —— 缺数据时停止并给出修复指令,而非静默降级;
4. **权威优先级链** —— 多来源知识冲突时的确定性裁决规则;
5. **题材卡置信度标注** —— 知识条目自带 confidence 元数据;
6. **停靠点/批量上限** —— Agent 批量执行护栏模式;
7. **分歧呈现不自动妥协** —— 多视角审查的综合裁决原则;
8. **manifest 钉版本 + hash parity** —— 上游知识资产的版本管理。

## 九、目录索引

| 路径 | 内容 |
|------|------|
| `repo/packages/dsh-plugin/src/` | 宿主桥接层源码(~1950 行 TS/TSX) |
| `repo/packages/knowledge/oh-story/skills/` | 13 小说 Skill(含 references 方法论库) |
| `repo/packages/knowledge/oh-story/roles/` | 7 个 Role 人格全文 |
| `repo/packages/knowledge/drama/skills/` | 10 短剧 Skill |
| `repo/packages/knowledge/*/manifest.json` | 上游版本钉定(commit + 文件 hash) |
| `repo/docs/ARCHITECTURE.md` | 上游官方架构文档 |
