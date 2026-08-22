# 重构技术设计

## 1. 移动分类清单

**根保留（不动）**：`.git`、`.gitignore`、`.gitattributes`、`.trellis`、`.agents`、`.codex`、`.pi`、`AGENTS.md`、`CLAUDE.md`、`README.md`

**先行迁出（git mv 到根）**：
- `Agent/Tianming.Agent.Ai` → `tianming-ai`
- `Agent/Tianming.Agent.Core` → `tianming-agent-core`
- `Docs/AGENT_CORE_ARCHITECTURE.md` → `AGENT_CORE_ARCHITECTURE.md`（根）

**整体进 old/（git mv old/<name>）**：Web、Services、Tests、Scripts、App_Data、deploy、Docs（剩余）、backups、天命PPT、【角色定义+创作规范】组合提示词、Agent（剩余）、Dockerfile、docker-compose.yml、global.json、TianmingAgenticNovelStudio.slnx、IMPLEMENTATION_SUMMARY.md、PHASE1_COMPLETION_REPORT.md、开源说明.md

**隐藏遗留项（plain mv，多为 gitignored）**：.claude、.config、.dotnet、.idea、.tmp、.env、.env.example、.dockerignore、.DS_Store

## 2. pi-agent 参考源码

首选：从 npm 包元数据确认上游仓库与 tag，浅克隆到 v0.57.1 对应 tag/commit（`--depth 1 --branch <tag>`）。若网络不可用或 tag 不存在，回退为拷贝 `node_modules/@mariozechner/{pi-agent-core,pi-ai}@0.57.1` 编译产物并在 README 注明来源与局限（dist JS 无 TS 源）。

## 3. 依赖修复细节

- `tianming-agent-core/package.json`: `"@tianming/agent-ai": "file:../tianming-ai"`。
- 两包删除旧 node_modules 链接后重新 `npm install`（lockfile 中 resolved 相对路径随之更新）。
- 新路径验证：两包 type-check/build/test；PiRuntime 回归经 `old/Agent/Tianming.NovelAgent.PiRuntime` 运行。

## 4. 文档引用修复

- 根目录 `AGENT_CORE_ARCHITECTURE.md`：包路径表改为新位置。
- 根 `README.md` 若含指向 Docs/ 的入口链接仅作最小提示注记，不重写正文（Out of Scope 保持）。
- 记忆库中记录的包路径同步更新。

## 5. 回滚

全部移动在单个工作提交内完成且未推送；如需回滚 `git revert` 即可恢复原布局。构建产物（bin/obj/node_modules）不受 git 管控，回滚提交后需手动移回或在 old/ 内重建。
