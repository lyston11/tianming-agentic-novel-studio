# tianming-novel-agent

天命 Agent 的小说领域层：以通用 `@tianming/agent-core` 为推理内核，通过领域 contracts、ports、roles、skills、context adapter 和 application-facing orchestration 实现可审计的小说生产切片。

## 当前 vertical slice

```text
Conversation
  → GoalProposal intent（AgentCore + propose_goal tool → C# Application port）
  → 用户确认
  → C# transaction: Goal / GoalRevision / Production / Batch / Task
  → 冻结 NovelContextPackage（版本 + SHA-256）
  → chapter-writer + deterministic fake model
  → CandidateChapter
  → TS 纯 continuity 预检 Review / C# 生产门禁
  → awaiting_acceptance
  → Acceptance
  → C# Canon command + read model
  → CharacterState / ForeshadowEntry / WorkflowProjection read model
```

`tianming-novel-agent` 只组装 Core、Role、Skill、DomainTool 和 ContextProvider，并通过 Application-facing ports 提交意图、读取结果。提案生命周期、Goal/Production/Task、Acceptance、Canon merge、账本更新和 WorkflowProjection 的 durable 真源属于 C# 控制面。

`test/fixtures/InMemoryNovelTestStore` 和 deterministic fake model 只用于合同与状态链路测试；测试替身不代表 PostgreSQL、事务、Outbox、Worker lease/fence、RLS、真实模型 Provider 或 SSE relay 已经实现。

职责边界：

- `contracts.ts`：结构化领域合同、状态、版本、scope、幂等和 hash。
- `ports.ts`：Application-facing ports；DomainTool 不直接操作数据库。
- `roles/`、`skills/`：`chapter-writer` 工具白名单与章节输出合同。
- `context/`：项目 snapshot 读取和执行前冻结上下文。
- `runtime/`：Core event durable-message 映射与 deterministic fake model。
- `application/`：组装 Core/Role/Tools/ContextProvider，并调用 Application-facing ports。
- `test/fixtures/`：仅用于测试的内存 port 替身，不是生产真源。

依赖方向：只能依赖 `tianming-agent-core`（经其受控导出使用 `tianming-ai` 类型），不得直接导入任何 Pi 系列包，也不得反向依赖 Web。

## 依赖重建与验证

上游包使用 `file:` 依赖，必须先构建上游 `dist/`：

```bash
cd ../tianming-ai && npm install && npm run build && npm test
cd ../tianming-agent-core && npm install && npm run build && npm test
cd ../tianming-novel-agent && npm install && npm run build && npm run type-check && npm test
```

镜像源若生成空的 `@types/node` 目录，先运行 `npm install --force` 重装依赖后再检查。
