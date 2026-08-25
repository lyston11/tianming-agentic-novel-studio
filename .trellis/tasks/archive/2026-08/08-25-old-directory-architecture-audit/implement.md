# 执行计划

1. 盘点 `old/` 顶层目录、解决方案项目、文档、测试和大体积/临时内容。
2. 读取架构、实施总结、阶段报告、目标架构和关键任务文档。
3. 检查各项目引用、入口配置、DbContext、控制面服务、Worker、Pi Runtime、Web API/SSE 与测试锚点。
4. 结合 Git 历史和当前根目录架构，建立时间线及迁移判断。
5. 编写本任务目录下的 `report.md`，以“事实/设计/测试/规划/风险”区分证据。
6. 运行文件路径、章节标题、关键术语和 Git 状态检查；不修改 `old/`。

## 验证

- `test -s .trellis/tasks/08-25-old-directory-architecture-audit/report.md`
- 报告引用路径批量 `test -e` 抽查。
- `git diff --check`
- `git status --short`
