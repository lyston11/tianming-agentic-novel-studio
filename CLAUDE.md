# 天命AI写作 - Claude 开发指南

## 项目结构

权威分层文档是根目录 `AGENT_CORE_ARCHITECTURE.md`。依赖只能向下。

```
tianming-agentic-novel-studio/
├── tianming-web/
│   ├── backend/                    # ASP.NET Core (.NET 10) 后端 —— durable truth 权威
│   │   ├── Tianming.NovelAgent.{Domain,Contracts,Application,Infrastructure}/
│   │   ├── Tianming.Web/           # HTTP/SSE 宿主、EF 迁移、Worker
│   │   ├── Services/               # 小说领域内核（Story Bible、账本、ProductionKernel）
│   │   ├── Tests/{Unit,AgentArchitecture,NovelAgentRegression,AgentKernelRegression}/
│   │   ├── Scripts/dotnet          # SDK wrapper（必须用它，见下）
│   │   ├── global.json             # 锁定 SDK 10.0.400
│   │   └── TianmingWeb.slnx
│   └── frontend/                   # React 19 + Vite + Tailwind 4 + shadcn + react-query
├── tianming-novel-agent/           # 小说领域层：Skill/Role/DomainTool/ContextProvider/Hook
├── tianming-agent-core/            # 通用 Agent Loop（无小说字段）
├── tianming-ai/                    # 模型调用边界（固定 @mariozechner/pi-ai 0.57.1）
└── old/                            # 历史快照：旧前端、PiRuntime、Docs、部署脚本
```

`old/` 是历史参考与待退役区，**不是**在跑的后端——后端已于 2026-09-01 提升到 `tianming-web/backend/`。

## 开发服务端口

**固定端口配置（永远不要改）：**
- 后端 API: `5002` (http://127.0.0.1:5002 或 http://[::]:5002)
- 前端: `3002` (http://localhost:3002)
- Qdrant 向量数据库: `6333` (HTTP), `6334` (gRPC)
- PostgreSQL: `5432`

## 启动服务

路径均相对仓库根目录，不要硬编码本机绝对路径。

### 必须用 SDK wrapper

系统 `dotnet` 可能是 8.x，不满足 `global.json` 要求的 `10.0.400`。所有构建和测试走 wrapper：

```bash
./tianming-web/backend/Scripts/dotnet build tianming-web/backend/TianmingWeb.slnx
./tianming-web/backend/Scripts/dotnet test  tianming-web/backend/Tests/Unit/Unit.csproj
```

SDK 实体在 `tianming-web/backend/.dotnet/`（gitignored）。缺失时 wrapper 会明确报错。

### 后端启动

```bash
ASPNETCORE_URLS=http://+:5002 ./tianming-web/backend/Scripts/dotnet run \
  --project tianming-web/backend/Tianming.Web/NovelAgentWeb.csproj
```

**必须设置 `ASPNETCORE_URLS=http://+:5002`，否则监听默认 5000。**

后端要求 `ConnectionStrings:NovelAgentDb`（以及迁移用的 `NovelAgentMigrationDb`、Worker 用的 `NovelAgentWorkerDb`）。未配置时启动即抛 `InvalidOperationException`，这是刻意的 fail-fast。

### 前端启动

```bash
cd tianming-web/frontend && npm run dev     # :3002，dev 代理转发到 :5002
```

### Docker Compose

compose 仍在 `old/`（postgres 16 + qdrant + redis + api）。需先设置
`NOVELAGENT_ADMIN_PASSWORD`、`NOVELAGENT_APP_PASSWORD`、`NOVELAGENT_WORKER_PASSWORD`：

```bash
docker compose -f old/docker-compose.yml up -d
```

### 测试

`NovelAgentRegression` 需要 Docker（Testcontainers 起真 PostgreSQL）。
`AgentKernelRegression` 是 `OutputType=Exe` 控制台 runner，**`dotnet test` 不执行它**，必须 `dotnet run`。

当前基线（2026-09-01 实测）：Unit 837/837、AgentArchitecture 29/29、NovelAgentRegression 159/159、AgentKernelRegression 6/6。

## 核心架构

### Agent 决策系统

**关键文件**（均在 `tianming-web/backend/` 下）：
- `Tianming.Web/Support/AgentCore.cs` - Agent 核心决策逻辑
- `Tianming.Web/Support/AgentForegroundTurnRunner.cs` - 前台回合执行
- `Tianming.Web/Controllers/AgentController.cs` - Agent API 控制器（含 `/agent/chat` 兼容入口：唯一调用方是 `wwwroot/` 里 2026-08-22 构建的旧前端，后端经 `UseStaticFiles` + `MapFallbackToFile` 仍在托管它）
- `Tianming.Web/Services/Agent/PiConversationAgentRuntime.cs` - `PiRuntime:Enabled` 为真时的会话运行时
- `Tianming.Web/Services/Goals/TargetArchitectureDirector.cs` - 现役 `IAgentForegroundTurnRunner`

（旧文档提到的 `AgentToolCallingClient.cs` 已在历史重构中删除，不要再引用。）

**LLM 配置优先级：**
1. 用户设置文件：`App_Data/Projects/{projectName}/Settings/user_settings.json`
2. 用户设置中的 `llmTemperature` 和 `llmMaxTokens` 必须被所有 LLM 调用尊重
3. 永远不要在代码中硬编码 temperature 或 max_tokens

**Agent 行为原则：**
- 优先自然对话，不要强制工具调用
- 涉及书城、工作流、知识库、项目进度、章节状态等真实系统状态时，由 LLM 自主决定是否调用 `tool_search` 发现能力并读取真实状态工具；不能用固定关键词把状态问题硬路由成闲聊
- 只有确实需要写入、修改、提交或长任务推进时才调用写操作工具，Runtime 只负责权限、确认、去重和运行边界

### 多用户隔离

**AsyncLocal Workspace 模式：**
- `tianming-web/backend/Tianming.Web/Services/Workspace/WorkspaceFactory.cs`
- 每个 HTTP 请求有独立的 `NovelAgentWorkspace` 实例
- 通过 `AsyncLocal<T>` 实现线程安全的用户数据隔离
- 永远不要使用全局静态变量存储用户相关数据

### Phase 1 监控系统（已完成）

**监控组件：**
- `ViolationTracker` - 违规追踪服务（24小时内存保留）
- `WorkspaceUsageAuditMiddleware` - 审计中间件
- `WorkspaceMonitoringController` - 管理员监控 API

**配置：** `appsettings.json` → `WorkspaceAudit` 节
```json
{
  "WorkspaceAudit": {
    "EnableStrictMode": false,  // 生产环境仅记录
    "LogViolations": true,
    "TrackViolations": true
  }
}
```

### 知识库自动处理（已完成）

**文件处理流程：**
1. POST /api/knowledge/upload - 创建处理任务
2. Agent 调用 ProcessKnowledgeFile 工具（Idle/Planning/Reflection 阶段可用）
3. 短文件(<6K tokens)单次分析，长文件分块+聚合
4. 提取的知识条目自动向量化到 Qdrant
5. Reflection 阶段自动关联到 ProjectMemory

**向量检索：**
- CreativeKnowledgeBaseService.RetrieveAsync 使用 Qdrant 向量检索
- 融合 ProjectMemory 和 AuthorMemory 进行 Boost（引用过的知识+2.0，收藏的知识+1.5）
- 过滤已用套路模式（ProjectMemory.UsedTropePatterns）
- 题材匹配增强（GenrePrinciple + constitution.Genre）

**记忆关联字段：**
- ProjectMemory: ReferencedKnowledgeIds, UsedTropePatterns
- AuthorMemory: FavoriteKnowledgeIds
- AgentMemoryUpdate: UsedKnowledgeIds, UsedTropePatterns（Reflection 阶段自动提取）

## 提交规范

**Commit 消息格式：**
```
<type>(<scope>): <subject>

<body>

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

**Type 类型：**
- `feat`: 新功能
- `fix`: Bug 修复
- `refactor`: 重构（不改变功能）
- `docs`: 文档更新
- `test`: 测试相关

## 常见陷阱

1. **端口错误**：启动后端时忘记设置 `ASPNETCORE_URLS=http://+:5002`
2. **硬编码参数**：在 LLM 调用中硬编码 `temperature=0` 或 `max_tokens=1200`
3. **跨用户数据泄漏**：使用静态变量或全局单例存储用户数据
4. **过度工具调用**：简单对话也强制调用工具，导致回复机械化

## 开发前检查

在修改 Agent 行为或 LLM 调用前：
1. 读取用户设置：`App_Data/Projects/{projectName}/Settings/user_settings.json`
2. 确认 `llmTemperature` 和 `llmMaxTokens` 被正确使用
3. 验证系统提示符（system prompt）是否优先自然对话
4. 检查是否有硬编码的 LLM 参数
