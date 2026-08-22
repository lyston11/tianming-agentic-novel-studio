# 实施计划

## Step 1 — 任务与规划
- [x] 创建任务，写入 prd/design/implement。
- [x] `task.py start` 激活。

## Step 2 — 迁出四部分中的已有内容
- [x] `git mv Agent/Tianming.Agent.Ai tianming-ai`
- [x] `git mv Agent/Tianming.Agent.Core tianming-agent-core`
- [x] `git mv Docs/AGENT_CORE_ARCHITECTURE.md AGENT_CORE_ARCHITECTURE.md`

## Step 3 — old/ 隔离
- [x] `mkdir old`；对 design.md §1 清单逐项 `git mv old/<name>`（可见项）与 plain mv（隐藏遗留项）。

## Step 4 — 新骨架与 pi-agent
- [x] `tianming-novel-agent/README.md`、`tianming-web/README.md` 骨架（层职责 + 边界一句话）。
- [x] 建 `pi-agent/`：优先浅克隆上游 v0.57.1 tag；失败则拷贝 node_modules dist 并注明。附来源 README。

## Step 5 — 依赖修复与验证
- [x] Core package.json 改 `file:../tianming-ai`；两包重装依赖。
- [x] tianming-ai / tianming-agent-core：type-check、build、test 全绿。
- [x] PiRuntime 回归（old/ 路径）4/4 通过。

## Step 6 — 文档与提交
- [x] 根 AGENT_CORE_ARCHITECTURE.md 路径表更新；检查根 README 入口注记。
- [x] `task.py validate`、`git diff --check`。
- [x] 单次重构提交（含历史保留说明）；更新记忆库路径。
