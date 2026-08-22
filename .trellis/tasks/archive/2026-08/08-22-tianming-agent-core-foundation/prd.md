# Tianming Agent Core 分层与框架基线

## Goal

建立天命 Agent 的新架构基线：以 Pi 的 Agent Loop 设计理念为参考，先固定 `tianming-ai`、`tianming-agent-core`、`tianming-novel-agent` 和 `tianming-web` 的边界，再用最小可测试 Loop 验证核心契约。现有旧 Agent/MAF/Director/控制面代码进入 legacy 隔离区，不再在其上继续增加新 Agent 能力。

## Requirements

### 分层基调

- `tianming-ai` 是模型调用边界；第一阶段实现基于 `@mariozechner/pi-ai@0.57.1` 的薄适配，不引入第二套模型 SDK。
- `tianming-agent-core` 只负责通用 Agent Loop、消息上下文、工具调用、事件、abort、steering/follow-up 和 natural stop；不得依赖小说领域、ASP.NET、PostgreSQL、React 或 Web API。
- `tianming-novel-agent` 后续负责小说 Skills、Roles、Resources、Domain Tools、Context 和授权策略；本任务只冻结边界，不把现有小说工具全部迁入 Core。
- `tianming-web` 是认证、持久化、Application/Domain 权威、HTTP/SSE 和浏览器 UI 宿主；它通过 Runtime/API 使用 Agent，不直接依赖模型 provider 或 Core 内部状态。
- 旧代码保留为兼容/迁移路径，标记为 legacy；本任务不得继续向旧 `NovelPiRuntime`、MAF、Director 或 Web legacy turn path 添加能力，也不要求本阶段删除它们。

### Core Loop

- 采用 Pi 的 natural stop condition：当当前 assistant 响应没有 tool call，且没有 steering/follow-up 消息时结束一次 run；不引入固定 Proposal → Goal → Production 阶段链。
- 工具调用按模型返回顺序串行执行；工具失败转换为可观察的 error tool result，并由模型决定是否继续。
- 支持外部 AbortSignal 和 Agent 主动 abort；abort 必须停止后续模型/工具工作并发出终止事件。
- 支持 steering：工具执行后注入用户新消息并跳过尚未执行的工具；支持 follow-up：自然停止点之后追加消息并继续 Loop。
- 事件顺序稳定，至少覆盖 agent、turn、message、assistant update 和 tool execution 生命周期。
- Core 不负责 durable persistence；宿主可监听事件并自行保存/恢复消息。Checkpoint/resume 只保留为后续宿主能力，不在本阶段伪造持久化协议。
- 设定明确的最大 turn 防护，防止错误模型或工具造成无界 Loop；这是安全上限，不是业务阶段序列。

### 文档与迁移

- 形成一份项目架构基线文档，记录分层、依赖方向、运行时边界、Pi 借鉴与不照搬之处、dsh-story 可借鉴模式、legacy 隔离规则和后续迁移顺序。
- 将旧的“统一上下文/固定生产链”计划标记为历史整合计划，补充本次 Core-first 架构重置决策，避免历史文档继续作为当前架构真源。
- 后续 AC-15 Playwright 必须以稳定的 Core/Novel Agent Runtime 契约为前置，不在本任务中搭建浏览器测试基础设施。

## Acceptance Criteria

- [ ] Trellis PRD、技术设计和实施计划完成，且明确本任务不重写旧系统、不先搭 Playwright。
- [ ] 新增独立 `tianming-ai` Node package，固定使用 `@mariozechner/pi-ai@0.57.1`，并暴露最小模型/消息/流事件边界。
- [ ] 新增独立 `tianming-agent-core` Node package，不能依赖 NovelAgent Pi Runtime、ASP.NET 或 Web；能够执行无工具自然停止和带工具继续 Loop。
- [ ] Core 测试覆盖 natural stop、工具串行执行、工具错误、steering、follow-up、abort、最大 turn 防护和稳定事件顺序。
- [ ] `tianming-ai` 和 `tianming-agent-core` 均通过 type-check、build、targeted tests；现有 Node Pi Runtime 回归测试仍通过。
- [ ] 架构文档已写入用户指定的项目文档空间，并包含当前代码迁移策略、dsh-story 借鉴边界和 Playwright 前置条件。
- [ ] 旧代码未被无授权删除或大规模重写；旧路径的定位和退出条件有文档记录。
- [ ] `task.py validate` 和 `git diff --check` 通过。

## Constraints

- Node package 使用仓库当前 Node >=22、TypeScript 5.9、ESM 和 npm lockfile 约定。
- 版本固定为项目已验证的 `@mariozechner/pi-ai@0.57.1`；不得升级到未验证的新 API。
- Core 不直接导入 `@mariozechner/pi-agent-core`，避免第一阶段只是换名包装 Pi Loop；Pi 的 agent-loop 是设计参考，Tianming Core 的最小公共契约由本项目测试锁定。
- 本任务不改变生产 ASP.NET API/SSE 契约，不迁移旧控制面写入者，不开启新的 Playwright/CI 基础设施。
- 与旧架构冲突时，以本 PRD 和设计文档为当前任务权威，历史 handoff/整合计划仅作背景资料。

## Out of Scope

- 完整小说 Skills、Roles、Knowledge/RAG 体系和 dsh-story 全量移植。
- Node Runtime HTTP 服务重构和 ASP.NET Web 接入切换。
- Agent-to-Canon、Acceptance、Canon Merge 业务流程改造。
- Playwright、Docker E2E stack、浏览器下载和 CI job。
- 删除旧 Director、MAF、Structured Runtime、legacy Web turn path 或历史数据库迁移。
