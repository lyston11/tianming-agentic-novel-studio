# PRD：tianming-web/frontend 全量重建前端

日期：2026-08-29 · 状态：已完成

## 背景

仓库 2026-08-22 重构后，前端只有 old/ 冻结区里的旧版（React 19 + Vite 8 + zustand + 手写 CSS 1.3 万行，63 文件约 3 万行）。用户要求：新建 frontend 文件夹把前端"迁出来"，并深度参考 `/Users/lyston/PycharmProjects/learngraph` 的前端代码。

用户决策（AskUserQuestion）：
1. 位置：`tianming-web/frontend/`（对齐 AGENT_CORE_ARCHITECTURE.md 四层布局）
2. 方式：**基于 learngraph 全量重建**（页面在新栈上重写，旧代码只作对照，非原样搬迁）

## 决策与约束

- 对接唯一现存后端：old/ ASP.NET `NovelAgentWeb`（dev 代理 127.0.0.1:5002，端口 3002/5002 固定不改）
- 旧前端原样冻结，不删不改；`api/types.ts`（2817 行领域类型）与 `runtime-events.ts`（894 行运行时事件归一化）整体移植
- 技术栈取 learngraph 版本：React 19.2 / Vite 8 / TS 6 / react-query 5 / Tailwind 4（@tailwindcss/vite）/ shadcn radix-nova 单包 radix-ui / oxlint / vitest 4；无 zustand（module store + useSyncExternalStore + Context）
- SSE 三流严格遵循 `.trellis/spec/frontend/state-management.md`：streamKind/streamId/sequence 校验、cursor 续传去重、transient 不推进业务状态、统一 invalidate()
- 生产构建不再 postbuild 同步 old/ wwwroot（旧部署链路冻结）
- npm 官方源本机被重置：安装走 `--registry=https://registry.npmmirror.com`

## 实施结果

- 脚手架：learngraph 配置全家桶 + 28 个 shadcn ui 组件 + index.css（shadcn oklch token 承载旧版 ink/paper/red/jade/gold/blue 中式色板，`--tm-*` 保留品牌原色）
- api 层：client（信封合同/Bearer/401 事件/幂等键 FNV+UUID/GET 502-504 重试/SSE 工厂）+ 14 个按域端点模块 + auth-store（保持旧 zustand persist 键与格式，旧会话无缝迁移）
- 应用壳：lazy 路由 + AuthProvider/RequireAuth + AuthSessionBoundary（401 清缓存跳登录）+ AppRail（五项导航/项目显示/退出）
- 六大功能域全部落地：auth（登录/注册）、library（书城/详情/阅读器+版本对比回滚+生产链路证据）、materials（知识库浏览器：目录/条目 CRUD/语义搜索/导入抽屉+任务进度）、agent（会话/聊天/执行块/导演提案/生产状态卡/MarkdownContent 懒加载 streamdown）、workflow（useGoalWorkflow 双 SSE + 批次导航/章节工作台/定向返工/GoalInspector + 项目工作流总览）、settings（四 tab + 校验/预设/LLM 健康检查）
- 测试：17 个 vitest（信封合同/幂等键/重试退避/401/FormData/auth store 生命周期）
- 验收：typecheck ✓ / lint 0 错误（6 条 React Compiler 提示级警告）/ build ✓ / test 17/17 ✓ / dev 3002 冒烟 ✓

## 差距清单（刻意简化，详见 frontend/README.md）

知识库拖拽改为下拉移动；工作流页工具级时间线未复刻（保留链路摘要）；右键菜单改 DropdownMenu；dark token 就位未全面校准；EventLog 改 toast。
