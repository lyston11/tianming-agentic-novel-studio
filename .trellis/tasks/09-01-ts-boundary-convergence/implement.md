# 实施计划：TS 边界收敛

## 顺序

1. 记录提案生命周期与 continuity gate 的归属裁决、迁移风险和 11 项语义覆盖矩阵。
2. 将完整内存 store 移入 `test/fixtures/`，重命名为测试替身，移除 `src/store/` 生产导出。
3. 删除 TS proposal lifecycle reducer，收窄 `ports.ts`，把 `propose_goal` 改成 Application port adapter。
4. 移除 Application 对提案 hash/reducer 的重复语义，保留 Core/Role/Tool/Context 组装和一次运行编排。
5. 更新 vertical-slice 测试导入与 README，明确生产边界、测试替身和依赖重建顺序。
6. 按 `tianming-ai` -> `tianming-agent-core` -> `tianming-novel-agent` 重建依赖并运行 build/type-check/test；运行边界 grep、`git diff --check` 和 `task.py validate`。

## 变更边界

预期修改：

- `.trellis/tasks/09-01-ts-boundary-convergence/{notes,design,implement}.md`
- `tianming-novel-agent/src/ports.ts`
- `tianming-novel-agent/src/tools/domain-tools.ts`
- `tianming-novel-agent/src/application/novel-agent-application.ts`
- `tianming-novel-agent/src/index.ts`
- 删除 `tianming-novel-agent/src/domain/proposal-lifecycle.ts`
- 移动并重命名 `tianming-novel-agent/src/store/in-memory-store.ts` 到测试 fixtures
- `tianming-novel-agent/test/vertical-slice.test.ts`
- `tianming-novel-agent/README.md`

明确不修改：`tianming-ai`、`tianming-agent-core`、`old/` 下 C# 代码、真实数据库/provider/HTTP 服务、`contracts.ts` 的领域合同、EcomGen 和 `.zcode`。

## 回滚点

- 接口收窄前后分别运行 type-check，确保错误来自已知边界而非依赖重建。
- 测试替身移动后先运行 vertical slice，再做 README/文档收口。
- 若 production-path grep 仍发现 durable 实现，停止收口并修正路径，不用兼容别名把旧生产路径藏起来。
