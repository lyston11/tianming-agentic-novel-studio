# 实施记录

## 第一批（commit 0887c1af，2026-09-01）

R1/R2/R4/R5/R6 落地：删除 8 个零引用契约类型（contracts.ts 678→609 行）、continuity-gate 降级为
preflight 并注明非权威、build 先清理 dist、新增 `Scripts/verify-node-packages.sh` 单一校验入口
（边界 grep → dist 新鲜度 → 按构建顺序 type-check + test）、6/6 guard 以刻意反例验证可证伪。
契约分类依据见 `research/contracts-classification.md`（35 跨边界类型与 openapi.json 零命中，
生成路线不成立的实测记录）。

## 第二批（本 session，2026-09-01）

- **R3/AC-5**：`src/index.ts` 收敛为纯 adapter 公共面——仅导出 ports、context provider、
  role/skill、domain tools、event mapper、loop 接线；`contracts.ts`（领域类型）、
  `continuity-gate.ts`（领域 preflight）、`fake-model.ts`（测试 fake）不再经包公共 API 暴露。
  vertical-slice 测试的领域类型导入改为直连 `../src/contracts.js`（包内部模块，测试可用，
  公共 API 不暴露）。
- **R7/AC-10**：`AGENT_CORE_ARCHITECTURE.md` 新增第 9 节"领域归属裁决"，记录 C# 所有权的
  证据、五条约束（契约真源、gate 权威、导出面、回流防护、接线另议）与"后续 session 不再追问"
  的指令。CLAUDE.md 的四项失真（.NET 版本、目录结构、数据库真源、硬编码路径）经核实已在
  此前 session 修正（当前仅存的 `NovelAgentWeb.csproj` 字样是真实文件名，非路径失真）；
  `/agent/chat` 相关描述由任务 `09-01-retire-wwwroot-legacy-frontend` 于同日修正。

## 验证

`./Scripts/verify-node-packages.sh` 全绿：tianming-ai 3、tianming-agent-core 10、
tianming-novel-agent 11（vertical-slice 11/11），边界 guard、dist 新鲜度、type-check 全部通过。
实施中途 dist 新鲜度 guard 曾正确拦截一次未构建的 src 修改（R4 机制实测有效）。
