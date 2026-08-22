# 仓库重构：四部分代码基础与 legacy 隔离

## Goal

把单仓库重构为 Core-first 四层布局：根目录只保留 `tianming-ai`、`tianming-agent-core`、`tianming-novel-agent`、`tianming-web` 四个部分文件夹与工作流基础设施；现有全部旧代码与文档移入 `old/` 冻结；`pi-agent/` 保存 Pi 参考源码。用户已于会话内确认方案（含四个默认项）。

## Requirements

### 目标布局（仓库根）

```text
├── tianming-ai/            ← 由 Agent/Tianming.Agent.Ai 迁入（@tianming/agent-ai）
├── tianming-agent-core/    ← 由 Agent/Tianming.Agent.Core 迁入（@tianming/agent-core）
├── tianming-novel-agent/   ← 新骨架 README
├── tianming-web/           ← 新空骨架 README
├── pi-agent/               ← Pi Agent 参考代码（v0.57.1 对应源码）
├── old/                    ← 其余全部旧代码/文档/配置
└── 根保留：.git、.gitignore、.gitattributes、.trellis、.agents、.codex、.pi、
          AGENTS.md、CLAUDE.md、README.md
```

### 移动规则

- tracked 文件用 `git mv` 保持历史；gitignored 构建产物随目录自然迁移。
- `Agent/Tianming.Agent.Ai` → `tianming-ai`，`Agent/Tianming.Agent.Core` → `tianming-agent-core` 先行迁出，其余 `Agent/` 整体进 `old/Agent`。
- 进 old/ 的可见内容：Web、Services、Tests、Scripts、App_Data、deploy、Docs、backups、天命PPT、【角色定义+创作规范】组合提示词、Dockerfile、docker-compose.yml、global.json、TianmingAgenticNovelStudio.slnx、IMPLEMENTATION_SUMMARY.md、PHASE1_COMPLETION_REPORT.md、开源说明.md。
- 进 old/ 的隐藏遗留项：.claude、.config、.dotnet、.idea、.tmp、.env、.env.example、.dockerignore、.DS_Store。

### 依赖修复

- Core 包对 AI 包的 file: 相对路径从 `file:../Tianming.Agent.Ai` 改为 `file:../tianming-ai`；两包重新 `npm install` 重建链接与 lockfile。
- 迁移后两包 type-check/build/test 必须重新全绿；PiRuntime 回归从 old/ 路径运行验证仍通过。

### 文档一致性

- `Docs/AGENT_CORE_ARCHITECTURE.md` 是当前架构权威，不随 legacy Docs 进入 old/：上移到仓库根目录，并更新其中包路径表与引用它的 README 指针。

## Acceptance Criteria

- [ ] 根目录仅剩四个部分文件夹 + pi-agent + old + 工作流保留文件。
- [ ] `git mv` 保留历史；提交信息清晰记录重构。
- [ ] tianming-ai 与 tianming-agent-core 在新路径下 npm install/type-check/build/test 全绿。
- [ ] pi-agent/ 含 v0.57.1 参考代码并附来源说明。
- [ ] 架构基线文档路径引用与新布局一致。
- [ ] `task.py validate` 与 `git diff --check` 通过。

## Out of Scope

- 不修改任何 legacy 代码内容；不在本任务实现 novel-agent/web 层功能。
- 不重写 README.md 正文（仅后续任务）。
