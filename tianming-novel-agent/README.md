# tianming-novel-agent

天命 Agent 的小说领域层：以通用 `@tianming/agent-core` 为推理内核，通过领域 contracts、ports、roles、skills、context adapter 和 application-facing orchestration 实现可审计的小说生产切片。

## 当前 vertical slice

```text
Conversation
  → GoalProposal（AgentCore + propose_goal tool）
  → 用户确认
  → Goal / GoalRevision / Production / Batch / Task
  → 冻结 NovelContextPackage（版本 + SHA-256）
  → chapter-writer + deterministic fake model
  → CandidateChapter
  → continuity Review
  → awaiting_acceptance
  → Acceptance
  → Canon merge
  → CharacterState / ForeshadowEntry / WorkflowProjection
```

当前实现使用 `InMemoryNovelStore` 和 deterministic fake model，只用于合同与状态链路测试；它不代表 PostgreSQL、Outbox、Worker lease/fence、RLS、真实模型 Provider 或 SSE relay 已经实现。

职责边界：

- `contracts.ts`：结构化领域合同、状态、版本、scope、幂等和 hash。
- `ports.ts`：Application-facing ports；DomainTool 不直接操作数据库。
- `roles/`、`skills/`：`chapter-writer` 工具白名单与章节输出合同。
- `context/`：项目 snapshot 读取和执行前冻结上下文。
- `runtime/`：Core event durable-message 映射与 deterministic fake model。
- `application/`：Conversation、Goal confirmation、chapter task、acceptance、workflow query 的用例编排。
- `store/`：仅用于测试的内存适配，不是生产真源。

依赖方向：只能依赖 `tianming-agent-core`（经其受控导出使用 `tianming-ai` 类型），不得直接导入任何 Pi 系列包，也不得反向依赖 Web。
