# Multi-User Workspace Isolation Fix

## 问题描述

当前AgentRuntime及相关组件注册为Singleton，导致所有用户共享同一个workspace实例。新用户登录后可能看到其他用户的项目数据。

## 根本原因

```csharp
// Program.cs
builder.Services.AddSingleton<AgentRuntime>();  // ❌ 单例，所有用户共享
builder.Services.AddSingleton<NovelAgentWorkspace>();  // ❌ 单例workspace
builder.Services.AddSingleton<NovelProjectCatalog>();  // ❌ 基于单例workspace的catalog
```

AgentRuntime构造函数直接注入workspace：
```csharp
public AgentRuntime(
    NovelAgentWorkspace workspace,  // ❌ 构造时绑定，所有用户共享
    NovelProjectCatalog catalog,    // ❌ 基于同一个workspace
    ...
)
```

## 历史临时修复

曾移除 ProjectRouter 的 auto-fallback 逻辑，强制用户明确选择项目：
- Commit: `b5fd7b6` - "fix(agent): remove fallback to active project to prevent cross-user data leak"
- 影响：新用户不会自动加载其他用户的active project

当前主链路已不再使用 ProjectRouter。项目创建、绑定和切换由 LLM 在 AgentPlanner 中基于产品空间、记忆层和工具语义自主决策，并通过 `ResolveNovelProject` 工具执行；Runtime 只负责工作区隔离、工具执行和安全边界。

## 正确架构方案

### 方案A：AgentRuntime改为Scoped（推荐）

```csharp
// Program.cs
builder.Services.AddScoped<AgentRuntime>();
builder.Services.AddScoped<NovelAgentWorkspace>(sp => {
    var factory = sp.GetRequiredService<IWorkspaceFactory>();
    var currentUser = sp.GetRequiredService<ICurrentUserService>();
    var userId = currentUser.GetUserId();
    var projectId = // 从请求上下文获取
    var entry = factory.AcquireAsync(userId, projectId).Result;
    return entry.Workspace;
});
```

优点：
- 每个HTTP请求有独立的AgentRuntime实例
- 自动获取当前用户的workspace

缺点：
- 需要重构整个依赖注入链
- AgentRuntime不能缓存内部状态

### 方案B：AgentRuntime保持Singleton，动态获取workspace

```csharp
public sealed class AgentRuntime
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly ICurrentUserService _currentUserService;
    
    public async Task<AgentChatResponse> RunAsync(string sessionId, string userMessage, CancellationToken ct)
    {
        var session = _sessionManager.GetOrCreateSession(sessionId);
        var userId = _currentUserService.GetUserId();
        var projectId = session.ActiveProjectId ?? await ResolveProjectId(...);
        
        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct);
        try
        {
            var workspace = workspaceEntry.Workspace;
            var catalog = new NovelProjectCatalog(workspace);
            // ... 使用workspace和catalog
        }
        finally
        {
            workspaceEntry.Release();
        }
    }
}
```

优点：
- AgentRuntime可以保持单例，缓存无状态组件
- 每次请求动态获取用户workspace
- 利用WorkspaceFactory的缓存和引用计数

缺点：
- 需要重构RunAsync内部逻辑

## 实施步骤

### Phase 1: 重构AgentRuntime（方案B）

1. [ ] 修改AgentRuntime构造函数，移除workspace/catalog参数，添加IWorkspaceFactory和ICurrentUserService
2. [ ] 修改RunAsync方法，在开始时acquire workspace，结束时release
3. [ ] 更新Program.cs的依赖注入配置

### Phase 2: 测试多用户隔离

1. [ ] 创建两个测试用户A和B
2. [ ] 用户A创建项目"测试A"
3. [ ] 用户B创建项目"测试B"
4. [ ] 验证用户A只能看到"测试A"
5. [ ] 验证用户B只能看到"测试B"
6. [ ] 验证用户A的会话不会加载用户B的项目

### Phase 3: 性能测试

1. [ ] 测试WorkspaceFactory的缓存命中率
2. [ ] 测试并发请求下的workspace acquire/release性能
3. [ ] 测试idle workspace的自动eviction

## 相关代码位置

- `Support/AgentRuntime.cs` - 主要重构目标
- `Services/Workspace/WorkspaceFactory.cs` - 已实现用户隔离，无需修改
- `Program.cs` - 依赖注入配置

## 风险评估

- 🔴 高风险：AgentRuntime是核心组件，重构可能影响所有Agent功能
- 🟡 中风险：需要充分测试多用户场景
- 🟢 低风险：WorkspaceFactory已经实现了正确的用户隔离逻辑

## 优先级

**P0 - 安全漏洞**

当前状态允许用户看到其他用户的项目数据，属于严重的安全问题。临时修复可以缓解，但需要尽快完成架构重构。
