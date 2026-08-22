# 小说 Agent 简化架构建议

## 总体评价

当前架构方向 **60% 正确，40% 过度工程化**。

**核心判断**：你的设计展现了扎实的架构能力，但对一个早期产品来说，复杂度过高。当前阻塞点（AC-5/AC-8 未完成）的根因不是技术债，而是**架构设计过于理想化，导致实施成本远超预期**。

## 你做对的地方

### ✅ 1. PostgreSQL 作为唯一真源
- Redis/Qdrant 只存派生数据，可重建
- 避免了多真源带来的一致性问题
- **保持这个决策**

### ✅ 2. LLM 不决定状态迁移
- "LLM 只产出 Proposal、Artifact 或结构化判断；状态迁移由确定性代码决定"
- 这避免了不确定性导致的数据混乱
- **保持这个决策**

### ✅ 3. 知识库 + RAG 设计
- 用户上传 → RawSource → 解析 → 向量化 → 检索
- 这是小说创作的真实需求（参考设定、人物关系、世界观）
- **保持这个设计**

## 过度复杂的地方

### ❌ 1. 五层抽象：Conversation → Proposal → Goal → Production → Canon

**问题**：
- 用户只关心："我说了什么" → "系统生成了什么" → "我接受/修改"
- Proposal、Goal、Production 这些中间层用户看不见，也感受不到价值
- 每增加一层抽象，事务边界、一致性、测试复杂度都指数级增长

**建议简化为三层**：
```
ChatSession → Decision → Draft/Canon
    ↓            ↓          ↓
  对话历史    LLM决策    草稿/正文
```

### ❌ 2. 双运行时 + 双 DbContext + 严格 DDD 分层

**问题**：
- `AgentControlDbContext` vs `NovelAgentDbContext` 导致 Worker ownership 迁移复杂
- Domain/Contracts/Application/Infrastructure 四层对早期团队来说太重
- 新旧隔离的初衷是"不停机重构"，但实际成本远超预期

**建议**：
- 只用一个 DbContext，通过 Feature Flag 控制新旧代码路径
- 用三层就够：Controllers → Services → Repositories
- 停止"不停机重构"，接受一次性迁移的停机窗口

### ❌ 3. 16 个验收标准 + 9 个 Phase

**问题**：
- AC-1 到 AC-16 的完成度判断标准不清晰（什么叫"部分完成" `[~]`？）
- 9 个 Phase 相互依赖，导致任务阻塞（AC-5 要等 AC-8，AC-8 要等 Worker ownership）
- 这是企业级系统的复杂度，不是 MVP 的规模

**建议简化为 3 个核心 AC**：
- AC-1: 用户能通过对话生成章节
- AC-2: 生成的章节能保存、编辑、版本管理
- AC-3: 知识库能检索并影响生成内容

其他的（事务边界、崩溃恢复、故障注入）都是优化，不是 MVP。

### ❌ 4. Typed DAG + 三种 ProductionMode

**问题**：
- 小说生成的 99% 场景是线性的："生成章节 A → 用户确认 → 生成章节 B"
- 不需要复杂的 DAG 依赖图
- SingleChapter/InteractiveBatch/AutonomousBook 三种模式是提前优化

**建议**：
- 只用简单的 WorkQueue：一个任务 = 生成一个章节或处理一个知识库文件
- 状态只需要：Pending → Running → Completed/Failed
- 等产品验证后再考虑批量生成

## 简化架构提案

### 核心流程

```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│ ChatSession │ ───> │ AgentDecision│ ───> │  WorkQueue  │
└─────────────┘      └──────────────┘      └─────────────┘
      │                     │                      │
      ├─ messages           ├─ type               ├─ task_id
      ├─ context            ├─ parameters         ├─ status
      └─ user_id            └─ created_at         ├─ result
                                                   └─ retry_count
                                    │
                                    ↓
                            ┌───────────────┐
                            │ ChapterDraft  │ → 用户确认 → Canon
                            └───────────────┘
```

### 数据模型（单 DbContext）

```sql
-- 项目管理
projects (id, name, user_id, created_at)
outlines (id, project_id, content, version, created_at)

-- 对话与决策
chat_sessions (id, project_id, user_id, created_at)
messages (id, session_id, role, content, created_at)
decisions (id, session_id, type, parameters, created_at)

-- 异步执行
work_items (id, type, input, status, result, retry_count)

-- 内容管理
chapters (id, project_id, chapter_num, content, version, status)
chapter_drafts (id, chapter_id, content, created_by, created_at)

-- 知识库
knowledge_entries (id, project_id, title, content, embedding, status)
```

### 代码分层（三层）

```
Controllers/        # REST API + SSE
Services/          # 业务逻辑
  ├─ ChatService
  ├─ GenerationService
  ├─ KnowledgeService
  └─ OutlineService
Repositories/      # 数据访问
Data/             # DbContext + Migrations
```

## 当前阻塞点根因分析

### AC-5：单链路 E2E 未完成

**根因**：不是技术债，是**设计过于复杂**
- Conversation → Proposal → Goal → Production → Candidate → Canon 六个步骤
- 每个步骤都需要事务、幂等、重试、审计
- 实现成本被低估了 5-10 倍

**解决方案**：
- 简化为：ChatSession → WorkItem → Draft → Canon（四步）
- 用简单的状态机，不需要 Typed DAG

### AC-8：单一写入所有权未完成

**根因**：双 DbContext 设计导致迁移复杂
- 旧 Worker 通过 `NovelAgentDbContext` 写 `kernel_tasks`
- 新架构想用 `AgentControlDbContext` 独占写入
- 两个 Context 操作同一张表，导致迁移阻塞

**解决方案**：
- 放弃双 DbContext，直接在 `NovelAgentDbContext` 里重构
- 用 Feature Flag 控制新旧代码路径
- 接受一次性迁移的停机窗口（2-4 小时）

## 优先级建议

### P0（必须先修，否则项目无法推进）

1. **简化核心流程**
   - 去掉 Proposal/Goal 层，直接 Decision → WorkItem
   - 去掉 Production/Batch/KernelTask 三层抽象，只保留 WorkItem
   - 目标：1 周内完成 "对话 → 生成 → 保存" 的最小闭环

2. **停止双 DbContext 迁移**
   - 放弃 `AgentControlDbContext`
   - 直接在 `NovelAgentDbContext` 里重构
   - 目标：解除 AC-5/AC-8 阻塞

### P1（高价值，应尽快完成）

3. **重新定义验收标准**
   - 把 16 个 AC 简化为 3-5 个核心 AC
   - 每个 AC 必须有明确的"完成"定义，不要 `[~]` 部分完成
   - 目标：让团队知道"什么叫做完了"

4. **分阶段交付**
   - Phase 1（2 周）：对话 + 生成 + 保存
   - Phase 2（2 周）：知识库 + RAG
   - Phase 3（2 周）：多章节 + 大纲管理
   - 不要一次性做 9 个 Phase

### P2（可延后）

5. **Provider-neutral 设计**
   - 隔离 OpenAI/MAF 是有价值的，但不是 MVP 的阻塞点
   - 可以先硬编码 OpenAI，等产品验证后再抽象

6. **事务边界 + 崩溃恢复**
   - Outbox/StreamEvent/崩溃恢复 是优化，不是 MVP
   - 先用简单的重试机制，等产品稳定后再加强

## 设计哲学对比

| 维度 | 当前设计 | 建议设计 |
|------|---------|---------|
| 核心流程 | 6 层抽象 | 3 层抽象 |
| 数据库 | 双 DbContext | 单 DbContext |
| 代码分层 | 4 层 DDD | 3 层 MVC |
| 验收标准 | 16 个 AC | 3-5 个核心 AC |
| 实施计划 | 9 个 Phase | 3 个 Phase |
| 开发周期 | 6-12 个月 | 2-3 个月 MVP |
| 复杂度 | 企业级 | 创业产品级 |

## 最后的建议

**如果只能给一个建议**：
- **砍掉 50% 的抽象层**
- 用 2-3 个月做出"能用的小说 Agent"
- 然后根据用户反馈决定下一步
- 而不是花 6-12 个月做"架构完美但功能不全"的系统

**关键问题**：
- 你是想做一个"架构教科书级别的示范项目"？
- 还是想做一个"帮助用户写小说的产品"？

如果是后者，当前设计需要大幅简化。
