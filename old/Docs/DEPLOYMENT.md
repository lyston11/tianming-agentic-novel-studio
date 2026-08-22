# 天命小说 Agent 部署与运维

**适用架构：** PostgreSQL 权威状态 + Redis 协调 + Qdrant 可重建索引
**更新时间：** 2026-07-31

## 1. 运行组件

| 组件 | 版本 | 端口 | 职责 |
|---|---:|---:|---|
| API | .NET 8 | 5002 | REST、SSE、worker、静态前端 |
| PostgreSQL | 16 | 5432 | 唯一业务真源、RLS、任务队列、Outbox |
| Redis | 7 | 6379 | lease、缓存、SSE live/replay、fan-out |
| Qdrant | 1.18.1 | 6333/6334 | 可重建语义索引 |
| Frontend | React 19 / Vite 8 | 由 API 提供 | Agent 与 Goal Console |

OrbStack 与 Docker Desktop 均可运行 Compose；本地验收默认使用 OrbStack。

## 2. 数据所有权

- PostgreSQL 保存用户、项目、章节正文和版本、上传二进制、知识、记忆、CreativeGoal、任务图、Artifact、DomainEvent、模型调用与审稿。
- Qdrant 只保存向量、内容哈希和定位 payload，清空后由 PostgreSQL 重建。
- Redis 只保存可丢弃的短期状态，清空不能造成作品丢失。
- `App_Data` 只允许保存 DataProtection 等运行密钥，不保存业务正文、知识原文或项目 JSON。
- 生产不使用 SQLite，不执行 `SqliteSchemaNormalizer`，不使用 MinIO/S3 或本地目录作为业务真源。

## 3. 首次启动

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio

cd Web/NovelAgentWeb.Frontend
npm ci
npm run test
npm run lint
npm run build

cd ../..
docker compose up --build -d
```

访问：

- 应用：`http://localhost:5002/`
- 健康检查：`http://localhost:5002/health`
- Qdrant REST：`http://localhost:6333/`

查看服务：

```bash
docker compose ps
docker logs --tail 200 novelagent-api
```

## 4. 核心配置

Compose 使用以下环境变量覆盖 `appsettings.json`：

```text
ConnectionStrings__NovelAgentDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_app;Password=...
ConnectionStrings__NovelAgentWorkerDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_worker;Password=...
ConnectionStrings__NovelAgentMigrationDb=Host=postgres;Port=5432;Database=novelagent;Username=novelagent_admin;Password=...
ASPNETCORE_ENVIRONMENT=Production
Redis__Enabled=true
Redis__ConnectionString=redis:6379
Qdrant__BaseUrl=http://qdrant:6333
Qdrant__Host=qdrant
Qdrant__Port=6334
```

生产必须额外设置：

- 至少 32 字节随机 `JwtSettings__SecretKey`。
- 独立 PostgreSQL 管理员和应用账号密码。
- 正确的 `JwtSettings__Issuer`、`JwtSettings__Audience` 和过期策略。
- DataProtection 持久化与密钥轮换策略。
- 各专业内核模型 Provider、Base URL、模型、加密 API Key 和单价。

API Key 不得出现在源码、镜像、URL 或日志中。高级附加提示词不能覆盖系统协议、权限或输出 Schema。

## 5. PostgreSQL 初始化与迁移

生产上下文是 `PostgresNovelAgentDbContext`：

```bash
dotnet ef database update \
  --project Web/NovelAgentWeb/NovelAgentWeb.csproj \
  --context PostgresNovelAgentDbContext
```

检查实体与 migration 是否漂移：

```bash
dotnet ef migrations has-pending-model-changes \
  --project Web/NovelAgentWeb/NovelAgentWeb.csproj \
  --context PostgresNovelAgentDbContext
```

期望输出：

```text
No changes have been made to the model since the last migration.
```

所有用户拥有表均强制 RLS。应用连接由拦截器设置 `app.current_user_id`；后台 worker 必须显式进入目标用户作用域。

Compose 的 `postgres-role-bootstrap` 会在 PostgreSQL 健康后创建或更新 `novelagent_app`、`novelagent_worker` 密码，并在 API 启动前退出。目标架构 migration 负责授予 worker 后台 claim 函数的 `EXECUTE` 权限。

已有 `postgres-data` volume 时，修改 `.env` 中的 `POSTGRES_PASSWORD` 不会自动修改数据库内 `novelagent_admin` 的密码，因为官方镜像初始化变量只在空数据目录生效。管理员密码轮换必须先在 PostgreSQL 内执行 `ALTER ROLE novelagent_admin PASSWORD ...`，再同步 `.env` 并重启；应用与 worker 角色密码由 bootstrap 每次启动显式刷新。

## 6. SQLite 一次性 staging 导入

SQLite 仅允许作为旧版本只读迁移来源，不得在切换后继续写入。

演练：

```bash
dotnet run --project Web/NovelAgentWeb/NovelAgentWeb.csproj -- \
  --target-migration-mode=rehearse \
  --target-migration-source=/absolute/path/to/novelagent.db
```

同一副本至少运行两次：第一次导入权威数据，第二次必须 `ImportedCount=0` 且 `ReusedCount>0`，核验报告仍通过。

正式切换：

```bash
dotnet run --project Web/NovelAgentWeb/NovelAgentWeb.csproj -- \
  --target-migration-mode=cutover \
  --target-migration-source=/absolute/path/to/novelagent.db
```

可使用 `--target-migration-user=<user-id>` 限定用户演练。导入核验覆盖：

- 用户、项目、章节和正文内容哈希。
- Story Bible、角色、卷弧和伏笔。
- 知识、项目知识使用记录和 Author Memory。
- 终态 run、事件与工具 ledger 的只读审计数据。

以下内容不迁移：WorkingMemory、MissionPlan、未确认提案、未完成 ReAct 中间态、旧加密模型 API Key。

`cutover` 只在核验通过且旧 runtime/outbox 无未完成工作时重建 Qdrant 并开放新写入。

## 7. Qdrant 重建

Qdrant collection 按用户隔离，项目、分支、来源、版本和状态由 payload 过滤。向量维度与健康检查必须和当前 embedding 模型一致。

重建原则：

1. 暂停索引 dispatcher。
2. 保留 PostgreSQL 权威内容和 `vector_index_records`。
3. 删除目标用户的 Qdrant collection。
4. 由 cutover/rebuild 流程重新发出索引任务。
5. 对内容哈希、向量模型、点数量和用户过滤做抽样校验。
6. 恢复 dispatcher。

Qdrant 不提供业务恢复来源，不得从 Qdrant 反向覆盖 PostgreSQL。

## 8. Redis 运维

Redis 是运行态必需依赖，但不是业务真源：

- 缓存 key 使用用户、项目和版本维度。
- SSE replay key 使用用户与 session/goal/run 维度，并设置短 TTL。
- 分布式 lease 使用 owner 与过期时间。
- cache miss 可以回 PostgreSQL 重建；协调路径不可用时 health 必须明确失败。

清空 Redis 后应执行健康检查、重新登录并验证 Goal 状态、SSE 新事件和 worker claim，不需要恢复作品数据。

## 9. 发布流程

```bash
dotnet test Tests/Unit/Unit.csproj --nologo
dotnet test Tests/NovelAgentRegression/NovelAgentRegression.csproj --nologo
dotnet run --project Tests/AgentKernelRegression/AgentKernelRegression.csproj --no-restore

cd Web/NovelAgentWeb.Frontend
npm test
npm run lint
npm run build

cd ../..
docker compose build api
docker compose up -d api
curl -fsS http://localhost:5002/health
```

发布前还必须执行：

```bash
git diff --check
docker exec novelagent-postgres psql -U novelagent_admin -d novelagent -c \
  "SELECT status, count(*) FROM outbox_events GROUP BY status;"
```

不得在仍有意外 `committed/running/resumed/pause_requested/paused` Goal 或非预期 pending outbox 时切换镜像。

## 10. 健康检查

`GET /health` 至少返回：

- PostgreSQL `Healthy`，该探针使用应用连接执行 `SELECT 1`。
- Redis `Healthy`。
- Qdrant `Healthy`。
- embedding provider、model、dimension 与 `degraded=false`。
- 当前 API、tool schema、agent loop 和 kernel version。

任一关键依赖探测失败时，整体状态必须为 `Degraded` 且端点返回 HTTP 503。匿名健康响应只返回通用原因，不得输出连接串、用户名、密码或 Provider 凭据。

同时检查：

```bash
docker compose ps
docker exec novelagent-postgres pg_isready -U novelagent_admin -d novelagent
curl -fsS http://localhost:6333/healthz
docker exec novelagent-redis redis-cli ping
```

## 11. 备份与恢复

### PostgreSQL

```bash
docker exec novelagent-postgres pg_dump \
  -U novelagent_admin -d novelagent -Fc \
  -f /tmp/novelagent.dump
docker cp novelagent-postgres:/tmp/novelagent.dump ./novelagent.dump
```

恢复必须在维护窗口停止 API/worker 后执行：

```bash
docker cp ./novelagent.dump novelagent-postgres:/tmp/novelagent.dump
docker exec novelagent-postgres pg_restore \
  -U novelagent_admin -d novelagent \
  --clean --if-exists /tmp/novelagent.dump
```

恢复 PostgreSQL 后清空 Redis 派生缓存，并重建或恢复 Qdrant 索引。DataProtectionKeys 应按同一恢复点单独备份。

### 整体回滚

1. 停止 API、worker 和写入入口。
2. 恢复切换前 PostgreSQL custom-format dump。
3. 回退到匹配的 API 镜像。
4. 清空 Redis 派生状态。
5. 恢复 Qdrant snapshot，或从 PostgreSQL 全量重建。
6. 完成双用户只读隔离与内容哈希检查后再开放写入。

## 12. 常见故障

### API 启动失败

- 检查 PostgreSQL app 用户是否存在以及 migration 是否完成。
- 检查 Redis、Qdrant 和 embedding health。
- 检查日志是否出现 RLS user scope 缺失或 model configuration 缺失。

### Goal 长时间 queued

- 查询 `kernel_tasks` 的 status、lease_owner、lease_expires_at 和依赖任务。
- 确认 Goal 未处于暂停、取消或金额超限状态。
- 重启 worker 后验证过期 lease 能被恢复，不要直接修改 task status。

### 模型调用失败后预算未释放

- 先查 `model_executions.status`、`lease_expires_at`、`reserved_cost` 和 Goal 的 `reserved_cost`。
- 配置缺失、凭据错误或 HTTP 明确拒绝应立即进入 `failed`，预留应为 0；否则检查 `FailKnownAsync` 事务是否失败。
- 网络超时或连接中断属于结果未知，不能手工清零预留；由 lease recovery 查询 Provider 状态或执行保守结算。
- 不要把结果未知误标为已知失败，否则可能突破用户总金额上限。

### Outbox 堆积

- 查询 status、attempts、processing_owner、processing_lease_expires_at、last_error。
- 确认 dispatcher 已进入事件所属用户作用域。
- 检查 Redis/Qdrant 目标是否可用。
- 不要删除未完成 outbox；修复依赖后由 lease/retry 继续处理。

### SSE 无事件或越权

- 确认 Authorization header 有效，不把 JWT 放 query string。
- 确认 session ownership 在 replay 与 live subscribe 前通过。
- 检查 Redis replay key 是否包含 user/session，是否已超过 TTL。

### RAG 无结果

- 确认 PostgreSQL 有对应 ContentDocument/Chunk 和可用知识版本。
- 检查 `vector_index_records` 与 Qdrant point 的内容哈希、模型和用户 payload。
- 使用 PostgreSQL full-text 结果区分“无内容”与“向量索引缺失”。
- 索引缺失时重建，不要绕过 PostgreSQL 权限和版本复核。

## 13. 生产约束

- TLS 终止、反向代理、限流和安全响应头必须在公网部署前配置。
- PostgreSQL、Redis 和 Qdrant 端口不得直接暴露公网。
- JWT、模型 API Key、数据库密码和 DataProtectionKeys 必须使用秘密管理系统。
- 监控 Goal 各状态、task lease、outbox backlog、模型费用、SSE 连接、RAG 延迟和 Qdrant 重建进度。
- 每次发布保留可恢复的 PostgreSQL 备份和上一版镜像。
