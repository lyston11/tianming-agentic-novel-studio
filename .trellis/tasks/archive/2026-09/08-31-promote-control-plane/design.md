# 技术设计：控制面提升

## 1. 目标布局

```text
tianming-web/
├── backend/
│   ├── Tianming.NovelAgent.Domain/          ← old/Agent/Tianming.NovelAgent.Domain
│   ├── Tianming.NovelAgent.Contracts/       ← old/Agent/Tianming.NovelAgent.Contracts
│   ├── Tianming.NovelAgent.Application/     ← old/Agent/Tianming.NovelAgent.Application
│   ├── Tianming.NovelAgent.Infrastructure/  ← old/Agent/Tianming.NovelAgent.Infrastructure
│   ├── Tianming.Web/                        ← old/Web/NovelAgentWeb
│   ├── Tests/
│   │   ├── AgentArchitecture/               ← old/Tests/AgentArchitecture
│   │   ├── NovelAgentRegression/            ← old/Tests/NovelAgentRegression
│   │   ├── Unit/                            ← old/Tests/Unit
│   │   ├── AgentKernelRegression/           ← old/Tests/AgentKernelRegression
│   │   └── Fixtures/                        ← old/Tests/Fixtures
│   ├── Scripts/dotnet                       ← old/Scripts/dotnet（repo_root 推导重写）
│   ├── .dotnet/                             ← SDK 10.0.400（gitignored，本地/按需安装）
│   ├── global.json                          ← old/global.json
│   └── TianmingWeb.slnx                     ← old/TianmingAgenticNovelStudio.slnx（路径重写）
└── frontend/                                 ← 不动

old/  保留：Web/NovelAgentWeb.Frontend、Services、Docs、Scripts（其余）、App_Data、deploy、
      backups、题材提示词、天命PPT、历史 MD
```

`NovelAgentWeb` → `Tianming.Web` 是目录改名。**程序集名、RootNamespace、命名空间 `TM.Web.NovelAgentWeb.*` 全部不动**——改命名空间等于改业务代码，违反 AC-5，且会波及 566 个文件。目录名与命名空间不一致是可接受的临时状态，留给后续退役任务统一。

## 2. 迁移顺序（每步必须构建通过）

分四阶段，因为 566 文件一次搬完若断裂难定位。

### 阶段 A — Agent 四项目

自洽（只互相引用），先搬。

```text
git mv old/Agent/Tianming.NovelAgent.{Domain,Contracts,Application,Infrastructure} \
       tianming-web/backend/
```

`ProjectReference` 是同级相对路径（`../Tianming.NovelAgent.Contracts/...`），同级关系不变，**无需改动**。

验证：四项目 `dotnet build`。

### 阶段 B — Web

```text
git mv old/Web/NovelAgentWeb tianming-web/backend/Tianming.Web
git mv old/global.json tianming-web/backend/global.json
git mv old/Scripts/dotnet tianming-web/backend/Scripts/dotnet
```

改 `Tianming.Web/NovelAgentWeb.csproj` 的三条 `ProjectReference`：

```diff
- <ProjectReference Include="..\..\Agent\Tianming.NovelAgent.Application\...csproj" />
+ <ProjectReference Include="..\Tianming.NovelAgent.Application\...csproj" />
```

（`Contracts`、`Infrastructure` 同理。深度从 `..\..\Agent\` 变为 `..\`。）

`Scripts/dotnet` 的 `repo_root` 从 `$script_dir/..` 推出 `.dotnet`——迁移后 `backend/Scripts/../.dotnet` = `backend/.dotnet`，语义不变，**无需改动**。

验证：`dotnet build Tianming.Web`。

### 阶段 C — Tests

```text
git mv old/Tests/{AgentArchitecture,NovelAgentRegression,Unit,AgentKernelRegression,Fixtures} \
       tianming-web/backend/Tests/
```

改路径：

| 项目 | 原 | 新 |
|---|---|---|
| `AgentArchitecture` | `../../Agent/Tianming.NovelAgent.X/` | `../../Tianming.NovelAgent.X/` |
| `NovelAgentRegression` / `Unit` / `AgentKernelRegression` | `..\..\Web\NovelAgentWeb\NovelAgentWeb.csproj` | `..\..\Tianming.Web\NovelAgentWeb.csproj` |

验证：四项目 build。

### 阶段 D — solution + 路径解析修复

新建 `tianming-web/backend/TianmingWeb.slnx`（9 项目），删除 `old/TianmingAgenticNovelStudio.slnx`。

修 §3 的路径解析器。

验证：全套测试。

## 3. 路径解析修复（唯一允许的 .cs 改动）

### 3.1 `Tests/Unit/Architecture/TargetArchitecturePurityTests.cs:251-259`

```csharp
private static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null &&
           (!File.Exists(Path.Combine(directory.FullName, "README.md")) ||
            !Directory.Exists(Path.Combine(directory.FullName, "Web", "NovelAgentWeb"))))
        directory = directory.Parent;
    return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
}
```

问题：`README.md`（仓库根）与 `Web/NovelAgentWeb`（`old/` 下）在 08-22 后不再同处一层，16 个测试自此失败。

修法：改为定位 backend 根，用新布局下稳定的双标记。

```csharp
// 定位 tianming-web/backend：含 global.json 与 Tianming.Web/
while (directory != null &&
       (!File.Exists(Path.Combine(directory.FullName, "global.json")) ||
        !Directory.Exists(Path.Combine(directory.FullName, "Tianming.Web"))))
```

调用方 `Read(relativePath)` 的相对路径基准随之从仓库根变为 backend 根，需同步调整各处 `Read("Web/NovelAgentWeb/...")` → `Read("Tianming.Web/...")`。这是路径字符串改动，不是逻辑改动。

### 3.2 `Tests/Unit/ProgramConfigurationTests.cs`、`Tests/AgentKernelRegression/Program.cs`

同类硬编码 `Web/NovelAgentWeb` 路径，按同一基准修正。实施时逐个核对实际写法。

## 4. `old/Services` 跨界引用处置

4 个文件 `using TM.Services.Framework.AI.NovelAgent.Models`：`Services/Canon/{PrefixMergeService,ContinuitySummaryExtractor}.cs`、`Services/Content/{IProjectContentQueryService,ProjectContentQueryService}.cs`。

先查证：`old/Web/NovelAgentWeb.csproj` 是否已 `ProjectReference` 到 `old/Services` 的项目，还是靠源码 `Compile Include`。

- 若是 ProjectReference：改相对路径即可（`old/Services` 暂留原地）。
- 若是源码包含：把被引用的 `Models` 子集随迁，或保留指向 `old/Services` 的相对引用。

**优先保持引用而非复制代码**——复制会造成双真源。跨界引用留作后续退役任务的输入。

## 5. 回滚

每阶段一个 commit。任一阶段测试下降即 `git revert` 该阶段，不继续往下。`git mv` 是纯 rename，revert 无残留。

## 6. 验证命令

```bash
cd tianming-web/backend
./Scripts/dotnet build TianmingWeb.slnx
./Scripts/dotnet test Tests/AgentArchitecture/AgentArchitecture.csproj      # 期望 29/29
./Scripts/dotnet test Tests/Unit/Unit.csproj                                # 期望 837/837（修复后）
./Scripts/dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj # 期望 159/159，需 Docker
./Scripts/dotnet run --project Tests/AgentKernelRegression                   # 记录基线
```

前端冒烟：backend 以 `ASPNETCORE_URLS=http://+:5002` 启动，`tianming-web/frontend` `npm run dev`（:3002），验证登录 + 一个受保护读接口。

## 7. 边界不变式

- 依赖方向仍是 `tianming-web → tianming-novel-agent → tianming-agent-core → tianming-ai`。本任务只影响最上层的物理位置。
- 三个 Node 包不受影响，不重装不重构建。
- PostgreSQL 仍是唯一生产真源；不动 schema、不写迁移。
- Core event 仍非 durable truth。
