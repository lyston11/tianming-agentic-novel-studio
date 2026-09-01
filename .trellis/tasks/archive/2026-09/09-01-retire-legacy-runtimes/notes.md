# 研究笔记：有序退役 legacy runtime

标记：**实测**＝本任务运行验证；**代码证据**＝可按路径行号核对。

## 批次 A：MAF adapter 已删除（四条件核验）

| 条件 | 结果 | 证据 |
|---|---|---|
| 1 生产 callers 为零 | ✅ | `AddMafConversationRuntime` 定义于 `Infrastructure/DependencyInjection.cs:69`，全仓无调用点。`Program.cs:295-307` 的两分支注册 `PiConversationAgentRuntime`（`PiRuntime:Enabled` 为真）或 `StructuredConversationAgentRuntime`（为假），MAF 不在任一分支 |
| 2 替代路径有 targeted regression | ✅ | `StructuredConversationAgentRuntime` 有 3 个行为测试；新增两个 guard（见 §1.1） |
| 3 历史数据/迁移可读 | ✅ | `EfMafSessionCheckpointStore` **不拥有表**，读共享 `ConversationRuntimeCheckpoints` 并按 `Runtime == "maf"` 过滤；写入方 `EfAgentControlStore.cs:138` 是 `Runtime = checkpoint.Runtime`（运行时自报）。未动表/列/迁移，既有 maf 行保留，仅不再被读取 |
| 4 完整回归通过 | ✅ | 见 §1.3 |

### 1.1 原 guard 有两个真实漏洞

**漏洞一：范围太窄。** 原断言（`ConversationRuntimeReplacementTests.cs:33`）只查 `typeof(IConversationAgentRuntime).Assembly`，而该接口定义在 `Application/Ports/AgentPorts.cs`——守的是 Application。MAF 包引用却在 **Infrastructure**。

**漏洞二：扩到 Infrastructure 后仍不可证伪。** **实测**：把 `Microsoft.Agents.AI 1.17.0` 加回 Infrastructure csproj，扩范围后的 guard **依然通过**。根因是 `GetReferencedAssemblies()` 只报告编译器实际发出的引用——加了包但无代码使用时，未使用的引用会被丢弃。

所以新增第二个 guard `Agent_control_projects_do_not_declare_the_agent_framework_package`，用 `XDocument` 直接读四个 csproj 的 `PackageReference`。**实测可证伪**：加回包→失败，移除→通过。

教训：程序集级依赖断言只能抓"真在用"，抓不到"包悄悄回来"。两层都需要。

### 1.2 两个看似 MAF 的测试其实与 MAF 无关

`PostgresVerticalSliceTests` 的 `Unbound_MAF_checkpoint_*` 和 `MAF_checkpoint_rolls_back_*` 用的运行时是 `CheckpointRuntime` 测试替身（`:572`），它声明 `ConversationRuntimeCheckpoint("maf", ...)` —— `"maf"` 只是任意标签；`EfMafSessionCheckpointStore` 仅作读取器。

被测行为完全运行时无关：checkpoint 与 turn 原子提交、跨 `DbContext` 重建后可读、持久化失败时回滚。故保留覆盖，改为 `CheckpointRuntimeLabel = "test-runtime"` 常量 + 直接查表 + 测试改名去掉 MAF 字样。

### 1.3 实测回归

| 套件 | 删除前 | 删除后 |
|---|---|---|
| `AgentArchitecture` | 29/29 | **27/27**（删 4 个 MAF 测试、参数化去 MAF 分支、加 2 个 guard） |
| `Unit` | 837/837 | **837/837** |
| `NovelAgentRegression` | 159/159 | **159/159** |
| `AgentKernelRegression` | 6/6 | **6/6** |

### 1.4 我自己写错的一处

新测试原本照搬 `Maf_adapter_rejects_unstructured_output`，断言非法 JSON 抛 `InvalidOperationException`——**失败**。`StructuredConversationAgentRuntime.cs:33-41` 是刻意 catch `JsonException` 后降级为 `DiscussOnly` + 原因说明，不抛。已改为断言真实降级行为，并在注释固定这个刻意差异。

## 批次 B：三项未达标项的调查结论

### 2.1 `TargetArchitectureDirector` —— 整条链生产不可达，但被测试双向锁定

**代码证据**的调用链：

```text
Program.cs:194  AddScoped<TargetArchitectureDirector>()
Program.cs:385  AddScoped<IAgentForegroundTurnRunner>(sp => sp.GetRequiredService<TargetArchitectureDirector>())
                     ↓ 唯一消费者
Support/AgentTurnCoordinator.cs:17,24  构造注入 IAgentForegroundTurnRunner
Program.cs:386  AddScoped<AgentTurnCoordinator>()
                     ↓ 生产消费者
                   无（仅 Tests/Unit/Support/AgentTurnCoordinatorRuntimeQueueTests.cs 使用）
```

Director 除 DI 注册外无任何直接注入点。所以**整条链在生产上不可达**——它注册了但没有请求路径能到达它。

但删不掉，因为测试从两个相反方向锁死了它：

- 要求存在：`Tests/Unit/ProgramConfigurationTests.cs:202,204`（断言两条注册字符串在 `Program.cs` 里）、`Tests/AgentKernelRegression/Program.cs:69`（"conversation entry point must use TargetArchitectureDirector"）
- 要求不被用：`Tests/Unit/Architecture/TargetArchitecturePurityTests.cs:95,97`（断言 `AgentController` 不含 `AgentTurnCoordinator` 与 `IAgentForegroundTurnRunner`）

这个矛盾本身就是结论：Director 是**已注册但未接线的目标态实现**，不是残留死码。删它还是接它，是产品/架构判断（"前台回合应由谁执行"），不是清理动作。给后续任务的输入就是这条矛盾。

另有 `Tests/Unit/Services/Goals/TargetArchitectureDirectorTests.cs` 三个行为测试（`:70,122,166`）保护其内部逻辑。

### 2.2 `StructuredConversationAgentRuntime` —— 活兜底，属产品可用性决定

`Program.cs:295-307`：

```csharp
if (builder.Configuration.GetValue<bool>($"{PiRuntimeOptions.SectionName}:Enabled"))
    builder.Services.AddHttpClient<IConversationAgentRuntime, PiConversationAgentRuntime>(...);
else
    builder.Services.AddScoped<IConversationAgentRuntime, StructuredConversationAgentRuntime>();
```

`PiRuntime:Enabled` 为假时它是**默认实现**。删它等于让 flag 关闭时会话功能无实现可注入。这是产品可用性权衡（"Pi runtime 不可用时是否允许降级"），不是技术债。

且它的降级语义已被本任务的测试固定（非法模型输出→`DiscussOnly` 而非 500），有独立价值。

### 2.3 PiRuntime 直接持有 pi-agent-core —— 替代路径合同就绪但未接线

**代码证据**：`old/Agent/Tianming.NovelAgent.PiRuntime/package.json:16` 依赖 `@mariozechner/pi-agent-core 0.57.1`；`src/runtime.ts:1` 直接 `import { Agent, ... }`；`src/tools.ts:2` 导入 `AgentTool`/`AgentToolResult`。

`AGENT_CORE_ARCHITECTURE.md` §7 要求先有"只依赖新 Core 的 Novel Agent Runtime adapter"并经 vertical slice 验证。`09-01-ts-boundary-convergence` 已让 `tianming-novel-agent` type-check 干净、11/11 通过，**合同层就绪**；但它尚未经 internal API 与 C# 后端接线，`PiConversationAgentRuntime`（`Tianming.Web/Services/Agent/`）仍指向旧 PiRuntime 服务。

条件 1 需等新 adapter 真正成为 `PiRuntime:Enabled` 分支的实现。属独立接线任务。

### 2.4 `/agent/chat` —— 条件 1 不满足，后端仍在服旧前端

我先前草拟的结论（"新前端不调用，故可删"）**是错的**。实测纠正：

- **唯一代码调用方**：`old/Web/NovelAgentWeb.Frontend/src/api/index.ts:457`。新前端 `tianming-web/frontend/src/api/` 只用 `/agent/sessions` 与 `/novel-agent/conversations`，全仓无 `chat.ts`（我先前凭空写了这个文件名）。
- **但旧前端仍在被服务**：`Tianming.Web/Program.cs:576` `app.UseStaticFiles()`、`:586` `app.MapFallbackToFile("index.html")`；`Tianming.Web/wwwroot/` 含 `index.html` 与 2026-08-22 构建的 `assets/index-BaN8_1u2.js`，**该 bundle 内含 `agent/chat` 字符串**。

所以直连 `:5002` 的用户拿到的是旧前端，其 WorkflowPage 会打这个端点——生产 callers **非零**。

退役前置条件：先决定 `wwwroot` 那份 8-22 旧前端的处置（下线静态托管，或用新前端产物替换）。这是跨前后端的部署决定，超出本卡范围。

另注：旧前端自带 `tests/agentChatCutover.test.mjs:17` 断言 "AgentPage must no longer call the legacy /agent/chat entry"，说明其 AgentPage 已切走，仅 WorkflowPage 仍用——与 08-18 审计报告 §6.3 记录一致。

purity 测试 `AgentChatCompatEntry_PersistsOnlyThroughApplicationConversation` 保护该入口只经 `ConversationApplicationService` 落库，删除入口时需一并处理该测试。

## 3. 本任务未做

不删 §2 四项（条件 1 均不满足）；不写迁移、不删表列；不改 `IConversationAgentRuntime` 接口形状；不动 `tianming-novel-agent`；不碰 `EcomGen/`、`.zcode/`。目录名与命名空间统一留待 §2.1 裁决后随该任务一并处理，避免与退役改动混在同一批。
