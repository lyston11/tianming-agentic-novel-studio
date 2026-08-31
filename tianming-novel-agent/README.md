# tianming-novel-agent

天命 Agent 的小说领域层（规划中，暂为骨架）。

职责（见根目录 `AGENT_CORE_ARCHITECTURE.md`）：

- `Skill`：说明、资源引用、输入/输出合同、校验规则
- `Role`：工具白名单、资源白名单、最大深度、完成策略
- `DomainTool`：通过 Application port 提交意图，不直接操作数据库
- `ContextProvider`：生成项目/会话上下文
- `Hook`：保护前置条件和版本不变量

依赖方向：只能依赖 `tianming-agent-core`（经其暴露的 `@tianming/agent-core` 契约），不得反向依赖，不得直接导入 pi 系列包。
