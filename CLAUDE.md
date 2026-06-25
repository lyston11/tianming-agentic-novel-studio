# 天命AI写作 - Claude 开发指南

## 项目结构

```
tianming-agentic-novel-studio/
├── Web/NovelAgentWeb/              # ASP.NET Core 8.0 后端（主工作目录）
├── Web/NovelAgentWeb.Frontend/     # React + TypeScript 前端
├── Services/                       # 业务逻辑服务层
├── Migrations/                     # EF Core 数据库迁移
└── Tests/                         # 单元测试和回归测试
```

## 开发服务端口

**固定端口配置（永远不要改）：**
- 后端 API: `5002` (http://127.0.0.1:5002 或 http://[::]:5002)
- 前端: `3002` (http://localhost:3002)
- Qdrant 向量数据库: `6333` (HTTP), `6334` (gRPC)

## 启动服务

### 后端启动
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

**重要：必须设置 `ASPNETCORE_URLS=http://+:5002`，否则会监听默认端口 5000！**

### 前端启动
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb.Frontend
npm run dev
```

### Docker Compose 启动（推荐）
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio
docker-compose up -d
```

## 核心架构

### Agent 决策系统

**关键文件：**
- `Web/NovelAgentWeb/Support/AgentCore.cs` - Agent 核心决策逻辑
- `Web/NovelAgentWeb/Support/AgentToolCallingClient.cs` - LLM 工具调用客户端
- `Web/NovelAgentWeb/Controllers/AgentController.cs` - Agent API 控制器

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
- `Services/Framework/AI/NovelAgent/Workspace/WorkspaceFactory.cs`
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
