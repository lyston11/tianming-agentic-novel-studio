# @tianming/web-frontend

天命 Agentic Novel Studio 的新前端（2026-08-29 基于 learngraph 前端栈全量重建）。对接当前唯一后端：`old/` 冻结区中的 ASP.NET `NovelAgentWeb`（开发环境由 Vite 代理到 `http://127.0.0.1:5002`）。

旧前端（`old/Web/NovelAgentWeb.Frontend`，63 文件约 3 万行，zustand + 手写 CSS）**原样冻结在 old/ 作为功能对照**，不再演进。

## 技术栈

- React 19 + Vite 8 + TypeScript 6 + react-router-dom 7（声明式 `<Routes>`）
- 服务端状态：@tanstack/react-query v5（`src/lib/query-keys` 集中管理 key）
- 客户端状态：**无 zustand**。auth（`src/api/auth-store.ts`）、当前项目选择（`src/lib/project-store.ts`）、chat 消息（`src/lib/chat-store.ts`）均为 module-level store + `useSyncExternalStore`
- UI：Tailwind CSS 4（`@tailwindcss/vite`，无 tailwind.config）+ shadcn/radix-ui 单包（`src/components/ui` 28 个组件，radix-nova 风格）+ sonner + lucide
- 流式 Markdown：streamdown（懒加载 + `@streamdown/cjk` 中文优化 + `@streamdown/code`；刻意不装 math/mermaid）
- 工具链：oxlint（替代 eslint）、vitest 4 + testing-library
- 主题：`src/index.css` 以 shadcn oklch token 承载旧版中式色板（ink 墨 / paper 宣纸 / red 印泥朱 / jade 玉 / gold 金 / blue 黛蓝），品牌原色以 `--tm-*` 变量保留

## 目录

```
src/
├── api/            # client（信封/Bearer/幂等键/GET重试/SSE 工厂）+ 按域端点模块 + types.ts（自旧版整体移植）
├── components/ui/  # shadcn 组件（自 learngraph 拷贝）
├── components/     # layout（Rail/守卫）+ shared（BrandMark/PageHeader/EmptyState/MarkdownContent）
├── lib/            # cn、project-store、chat-store、stream-retry、runtime-events（自旧版移植的 894 行归一化逻辑）
├── features/       # auth / library / materials / agent / workflow / settings 六大功能域
└── test/           # vitest setup（jsdom + matchMedia/ResizeObserver stubs）
```

## 命令

```bash
npm install        # 官方源不通时: npm install --registry=https://registry.npmmirror.com
npm run dev        # http://127.0.0.1:3002（strictPort），/api → 127.0.0.1:5002
npm run typecheck  # tsc -b
npm run lint       # oxlint
npm test           # vitest run
npm run build      # tsc -b && vite build（rolldown vendor/react/motion/markdown-render 单 chunk 分组）
```

端口遵循仓库 README 的固定约定：前端 3002 / 后端 5002，**不要改**。生产构建不再自动同步 old/ 后端 wwwroot（旧部署链路已冻结）；联调一律走 dev 代理。

## SSE 流合同（.trellis/spec/frontend/state-management.md）

三条流，URL 与旧后端一致：

| 流 | 端点 | 消费者 |
|---|---|---|
| 兼容 runtime 流 | `GET /agent/sse/:sessionId?afterEventId=` | Agent 页 + useGoalProgressStream |
| Conversation 流 | `GET /novel-agent/streams/conversations/:sessionId?cursor=` | 已接线，等待 Conversation Runtime |
| Workflow 流 | `GET /novel-agent/streams/workflows/:projectId?cursor=` | useGoalWorkflow |

合同要点（实现见 `src/features/workflow/use-novel-agent-workflow-stream.ts`）：

- REST 拥有命令与快照；SSE 只做变更通知 → 触发 `useGoalWorkflow` 的**唯一** `invalidate()`
- 校验 `streamKind`/`streamId`/单调 `sequence`，cursor 持久续传 + 去重；`transient` token delta 不推进业务状态
- 断线以持久 cursor 重连，`runtimeStreamRetryDelay`：1s 起步指数退避、15s 封顶、±20% 抖动

## API 客户端合同（与旧后端一致）

- 统一信封 `ApiEnvelope`（`success/data/error` + 4 个 version 字段），缺失信封即硬错误
- 401 → 清会话 + `auth:unauthorized` 事件 → 路由回 /login
- 写操作携带 `Idempotency-Key`：稳定 FNV 哈希（确定性 payload）与 action UUID 两种
- GET 对 502/503/504 自动重试（500ms/1s/2s 退避，可 abort）
- localStorage `auth-storage` 键与旧前端 zustand persist 格式互通（旧会话无缝迁移）

## 与旧版的功能差距清单（重建范围界定）

功能对齐优先、视觉走 shadcn 新栈，以下为刻意简化或待后续任务补齐：

1. **知识库拖拽移动条目** → 改为条目卡「移动到目录」下拉菜单（数据合同相同）
2. **工作流页工具执行时间线**：旧版 3000+ 行的 per-stage 工具契约/阻断产物/时间线视图未逐一复刻；新页提供生产链路摘要 + 调度任务 + 卷章总览 + 完整 Goal 验收台
3. **书籍卡右键菜单** → 改为卡上悬浮 DropdownMenu；会话右键菜单同理
4. **dark 主题**：token 已就位（`.dark` 块），设置页可切换，但新栈页面未做全面暗色视觉校准
5. **EventLog 全局事件流面板** → 以 sonner toast 替代
6. **引擎拆分/每章字数统计图表** 等次要展示未迁移

## 后续方向

- 等 tianming-web 后端（ASP.NET 新权威层）就位后，仅调整 `resolveApiBaseUrl`/代理目标即可切换
- Conversation Runtime（tianming-agent-core）接通后，Conversation SSE 流按同一信封合同接入聊天页
