# 有序退役 legacy runtime：MAF adapter 优先，按 §6 四条件逐项删除

## 1. 目标与用户价值

`AGENT_CORE_ARCHITECTURE.md` §6 冻结了一批 legacy runtime 但明确「本阶段不要求删除」，同时给了四个必须**同时满足**的删除条件。本任务按那四条逐项核验并删除已达标的部分。

用户价值是减少维护面和误改风险：现在有两套 `IConversationAgentRuntime` 实现、一个从未被生产注册的 MAF adapter、一个仍在 feature flag 兜底位上的 Structured runtime。每个新会话读代码都要先分辨哪条路径是活的。

## 2. §6 的删除条件（必须同时满足）

1. 生产 callers 为零
2. 替代路径有 targeted regression
3. 历史数据/迁移仍可读取
4. 完整回归通过

§6 列出的冻结项：`old/Agent/Tianming.NovelAgent.PiRuntime` 直接持有 pi-agent-core `Agent` 的 turn path；C# MAF adapter、Structured Runtime、TargetArchitectureDirector；legacy Web turn path 及其控制面写入者。

## 3. 实测的达标情况

### 3.1 MAF adapter —— 条件 1、2 已满足，可先删

**条件 1（生产 callers 为零）✅**

`old/Web/NovelAgentWeb/Program.cs` 里**没有**调用 `AddMafConversationRuntime`。实际注册的是 feature flag 两分支：

```csharp
// Program.cs:295-307
if (builder.Configuration.GetValue<bool>($"{PiRuntimeOptions.SectionName}:Enabled"))
{
    builder.Services.AddHttpClient<IConversationAgentRuntime, PiConversationAgentRuntime>(...);
}
else
{
    builder.Services.AddScoped<IConversationAgentRuntime, StructuredConversationAgentRuntime>();
}
```

MAF 相关代码只存在于：
- `old/Agent/Tianming.NovelAgent.Infrastructure/Conversation/MafConversationAgentRuntime.cs`
- `old/Agent/Tianming.NovelAgent.Infrastructure/DependencyInjection.cs:69-76`（`AddMafConversationRuntime` 扩展方法，含 `IMafSessionCheckpointStore`/`EfMafSessionCheckpointStore`、`IMafAgentInvoker`/`MafAIAgentInvoker`）
- `old/Agent/Tianming.NovelAgent.Infrastructure/Tianming.NovelAgent.Infrastructure.csproj`：`Microsoft.Agents.AI 1.17.0`
- `old/Tests/AgentArchitecture/ConversationRuntimeReplacementTests.cs`

**条件 2（替代路径有 targeted regression）✅**

`ConversationRuntimeReplacementTests.cs:33` 已有守卫断言：

```csharp
Assert.DoesNotContain("Microsoft.Agents",
    typeof(IConversationAgentRuntime).Assembly.GetReferencedAssemblies().Select(x => x.Name));
```

**注意这个 guard 比看起来窄**：`IConversationAgentRuntime` 定义在 `old/Agent/Tianming.NovelAgent.Application/Ports/AgentPorts.cs`，所以它守的是 **Application** 程序集不引用 MAF。而 MAF 的包依赖在 **Infrastructure**。删除后应把该断言扩展到 Infrastructure 程序集，否则守不住回流。

### 3.2 TargetArchitectureDirector —— 条件 1 未满足

生产注册存在：

```
Program.cs:194  builder.Services.AddScoped<TargetArchitectureDirector>();
Program.cs:385  builder.Services.AddScoped<IAgentForegroundTurnRunner>(
                    sp => sp.GetRequiredService<TargetArchitectureDirector>());
```

它是 `IAgentForegroundTurnRunner` 的现役实现。删除前必须先有替代实现或确认该接口整体退役。相关文件还包括 `old/Tests/Unit/Services/Goals/TargetArchitectureDirectorTests.cs`、`old/Tests/Unit/ProgramConfigurationTests.cs`、`old/Tests/Unit/Architecture/TargetArchitecturePurityTests.cs`、`old/Tests/AgentKernelRegression/Program.cs`。

### 3.3 StructuredConversationAgentRuntime —— 条件 1 未满足

它在 `PiRuntime:Enabled` 为假时是**默认实现**（§3.1 的 else 分支）。这不是死代码，是活的兜底路径。删除前必须先决定：把 flag 固定为开、并接受 Pi runtime 不可用时服务直接失败；还是保留兜底。**这是产品可用性决定，不是纯技术清理。**

### 3.4 PiRuntime 直接持有 pi-agent-core —— 条件 1 未满足

`old/Agent/Tianming.NovelAgent.PiRuntime/package.json:16` 依赖 `@mariozechner/pi-agent-core 0.57.1`；`src/runtime.ts:1` 直接 `import { Agent, ... }`；`src/tools.ts:2` 导入 `AgentTool`/`AgentToolResult`。

§6 原文已把它标为「当前未迁移 legacy caller」。§7 的迁移顺序要求先有「只依赖新 Core 的 Novel Agent Runtime adapter」并用 vertical slice 验证。当前 `tianming-novel-agent` 的 type-check 是红的（见 `09-01-ts-boundary-convergence`），所以替代路径尚未就绪。

### 3.5 /agent/chat 兼容入口 —— 需单独核验

`old/Web/NovelAgentWeb/Controllers/AgentController.cs:49` 有 `[HttpPost("agent/chat")]`。08-18 审计报告 §6.3 记它是「迁移期双入口」之一。删除前需确认新前端（`tianming-web/frontend`）是否仍调用它。

## 4. 范围内

按达标情况分批，**每批一个独立 commit + 完整回归**：

### 批次 A —— MAF adapter（本任务主体）

- 删 `Conversation/MafConversationAgentRuntime.cs`、`EfMafSessionCheckpointStore`、`MafAIAgentInvoker` 及其接口。
- 删 `DependencyInjection.cs` 的 `AddMafConversationRuntime` 扩展方法。
- 从 `Infrastructure.csproj` 移除 `Microsoft.Agents.AI 1.17.0`。
- **扩展** `ConversationRuntimeReplacementTests.cs:33` 的断言到 Infrastructure 程序集，防止 MAF 回流。
- 核验 §2 条件 3：MAF 的 checkpoint 表/列若有历史数据，迁移必须仍可读取。若 `EfMafSessionCheckpointStore` 对应独立表，**本任务不删表、不写迁移**，只删代码。

### 批次 B —— 为后续退役解除依赖（只调查与记录，不删）

- 输出 `TargetArchitectureDirector` → `IAgentForegroundTurnRunner` 的调用图与替代方案选项。
- 输出 `PiRuntime:Enabled` flag 两分支的产品影响评估（兜底是否必要）。
- 核验 `/agent/chat` 在新前端的实际调用情况。
- 结论写入 `notes.md`，作为后续独立任务的输入。**不在本任务动这三项代码。**

## 5. 明确不做

- 不删 `TargetArchitectureDirector`、`StructuredConversationAgentRuntime`、PiRuntime 的 pi-agent-core 依赖、`/agent/chat`——它们条件 1 未满足（§3.2–3.5）。
- 不写数据库迁移、不删表、不删列。历史数据可读是删除条件之一，不是可牺牲项。
- 不改 `IConversationAgentRuntime` 接口形状。
- 不动 `tianming-novel-agent`（归 `09-01-ts-boundary-convergence`）。
- 不为了让回归变绿而修改业务逻辑。
- 不碰 `EcomGen/`、`.zcode/`。

## 6. 依赖与顺序

- **必须在 `08-31-promote-control-plane` 之后**：迁移会改变全部文件路径，先删再搬会让 rename 历史混乱，且迁移任务的 AC-5 要求「.cs 改动仅限声明的路径解析文件」——本任务的删除会破坏那条验收。
- 批次 B 的调查结论是后续三张独立退役卡的前置输入。
- `09-01-ts-boundary-convergence` 完成后，PiRuntime 的替代路径才具备条件，那时才可开 PiRuntime 退役卡。

## 7. 验收标准

- [ ] **AC-1 四条件逐项留痕**：每个删除项在 `notes.md` 有四个条件的核验结果与证据路径。未达标项必须明确写出缺哪一条。
- [ ] **AC-2 MAF 代码清除**：`grep -rn "Microsoft.Agents" old/ --include="*.cs"` 与 csproj 中无残留（测试中的守卫断言字符串除外）。
- [ ] **AC-3 守卫加强**：`ConversationRuntimeReplacementTests` 断言覆盖 Infrastructure 程序集；故意加回 MAF 包引用时该测试必须失败（可证伪）。
- [ ] **AC-4 回归不降**：`AgentArchitecture` 29/29、`NovelAgentRegression` 159/159、`Unit` 达到迁移后基线（见迁移任务 AC-3 的 837/837）。任一下降即回滚该批次。
- [ ] **AC-5 历史数据可读**：若 MAF 有对应持久化表，说明其迁移仍可前向执行且旧数据可读；本任务不删表。
- [ ] **AC-6 批次 B 结论成文**：三项未达标项各有调用图/影响评估/替代方案选项，可直接作为后续任务 PRD 的输入。
- [ ] **AC-7 文档同步**：`AGENT_CORE_ARCHITECTURE.md` §6 的冻结清单移除已删项，保留未删项及其缺失条件。
- [ ] **AC-8 收口**：`task.py validate` 与 `git diff --check` 通过。

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 删 MAF 时连带删掉被非 MAF 路径复用的 checkpoint 抽象 | 先查 `IMafSessionCheckpointStore` 的全部实现与 caller；只删 MAF 专有部分 |
| 守卫断言只覆盖 Application，MAF 依赖从 Infrastructure 回流 | AC-3 要求断言可证伪：加回包引用必须让测试失败 |
| 把 `StructuredConversationAgentRuntime` 当死代码删掉，导致 flag 关闭时服务无实现 | §3.3 已证它是 else 分支默认实现；§5 明确排除 |
| 一次删多项后回归失败难定位 | 每批独立 commit；批次 A 只含 MAF |
| 与迁移任务的「零业务改动」验收冲突 | §6 强制排在迁移之后 |

## 9. 为什么优先级是 P2

MAF adapter 从未被生产注册，删它不改变任何运行时行为——收益是减少认知负担和防止依赖回流，不是修 bug。真正阻塞后续工作的是另外三项，而它们条件 1 未满足，需要产品决定或替代路径就绪。所以本任务的实际价值一半在批次 A 的清理，一半在批次 B 把「为什么还删不掉」变成有证据的清单。
