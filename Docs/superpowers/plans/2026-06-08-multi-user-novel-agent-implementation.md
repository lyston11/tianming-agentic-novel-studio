# 多用户小说创作系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将单用户小说创作系统升级为多租户 SaaS 平台，支持用户认证、数据隔离、向量检索优化

**Architecture:** 混合存储 - SQLite（结构化数据）+ Qdrant（向量数据）+ 文件系统（章节内容）

**Tech Stack:** .NET 8.0/C#, Entity Framework Core 8.0, SQLite, Qdrant v1.8.0, React 19, TanStack Query, JWT Authentication

**Total Duration:** 6 周

---

## 实施概览

本计划分为4个阶段，每个阶段产出可独立测试的工作软件：

- **Phase 1: 基础设施**（1周）- SQLite 数据库、Qdrant 部署、迁移脚本
- **Phase 2: 后端开发**（2周）- 认证授权、API 重构、向量检索服务
- **Phase 3: 前端开发**（1.5周）- 登录注册、设置页重组、多用户界面
- **Phase 4: 测试优化**（1.5周）- 单元测试、集成测试、性能优化

---

## Phase 1: 基础设施（Week 1）

### 里程碑
- SQLite 数据库创建，所有表和索引就绪
- Qdrant Docker 运行，Collection 初始化脚本完成
- 数据迁移脚本完成，现有数据可导入

### Task 1.1: SQLite 数据库初始化

**目标:** 创建 DbContext 和所有16个实体，配置外键关系

**Files:**
- Create: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/*.cs` (16个实体文件)
- Modify: `Web/NovelAgentWeb/NovelAgentWeb.csproj` (添加 EF Core)
- Modify: `Web/NovelAgentWeb/appsettings.json` (连接字符串)

**Key Steps:**
- [ ] 添加 EF Core SQLite NuGet 包 (Microsoft.EntityFrameworkCore.Sqlite v8.0.0)
- [ ] 创建核心实体：User, UserSettings, NovelProject, Chapter
- [ ] 创建关联实体：Volume, Foreshadow, Character, WorldSetting
- [ ] 创建扩展实体：Material, KnowledgeBase, AgentMemory, AgentSession
- [ ] 配置 DbContext 关系映射（特别注意 Foreshadow 的外键约束）
- [ ] 创建索引（projectId, status, userId 等高频查询字段）
- [ ] 测试：`dotnet ef migrations add InitialCreate --project Web/NovelAgentWeb`
- [ ] 生成数据库：`dotnet ef database update --project Web/NovelAgentWeb`
- [ ] 验证：检查 `App_Data/Database/novelagent.db` 文件存在，表结构正确

**Commit:** `feat(db): add SQLite database with all 16 entities and relationships`

### Task 1.2: Qdrant Docker 配置

**目标:** 配置 Qdrant Docker 容器，创建健康检查

**Files:**
- Create: `docker-compose.yml`
- Create: `Web/NovelAgentWeb/Services/VectorStore/QdrantHealthCheck.cs`

**Key Steps:**
- [ ] 创建 docker-compose.yml（Qdrant v1.8.0，端口 6333/6334，数据卷映射）
- [ ] 启动容器：`docker-compose up -d qdrant`
- [ ] 验证健康：`curl http://localhost:6333/health`
- [ ] 创建健康检查服务（定期 ping Qdrant）
- [ ] 测试重启：`docker-compose restart qdrant`，验证数据持久化

**Commit:** `feat(infra): add Qdrant Docker configuration with health check`

### Task 1.3: Qdrant Client 集成

**目标:** 集成 Qdrant.Client，创建 Collection 管理服务

**Files:**
- Create: `Web/NovelAgentWeb/Services/VectorStore/QdrantVectorStore.cs`
- Create: `Web/NovelAgentWeb/Services/VectorStore/IVectorStore.cs`
- Modify: `Web/NovelAgentWeb/NovelAgentWeb.csproj` (添加 Qdrant.Client)
- Modify: `Web/NovelAgentWeb/Program.cs` (DI 注册)

**Key Steps:**
- [ ] 添加 NuGet 包：`Qdrant.Client` (最新版本)
- [ ] 创建 IVectorStore 接口（CreateCollection, UpsertVectors, Search, DeleteCollection）
- [ ] 实现 QdrantVectorStore（封装 Qdrant.Client）
- [ ] 实现 InitializeProjectCollectionAsync（创建 Collection，配置 HNSW 索引）
- [ ] 实现 UpsertChapterVectorsAsync（批量插入，batch_size=100）
- [ ] 实现 SearchSimilarAsync（过滤 user_id, project_id）
- [ ] 在 Program.cs 注册服务：`services.AddSingleton<IVectorStore, QdrantVectorStore>()`
- [ ] 测试：创建测试 Collection，插入模拟向量，执行相似度搜索

**Commit:** `feat(vectorstore): integrate Qdrant client with collection management`

### Task 1.4: 数据迁移脚本 - JSON to SQLite

**目标:** 编写脚本将现有 JSON 数据导入 SQLite

**Files:**
- Create: `Scripts/Migration/DataMigrationService.cs`
- Create: `Scripts/Migration/MigrateCommand.cs`
- Create: `Scripts/Migration/Models/LegacyProject.cs` (现有 JSON 结构)

**Key Steps:**
- [ ] 读取 `App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json`
- [ ] 为每个项目创建 NovelProject 记录（生成新 GUID）
- [ ] 解析 `story_bible.json`，提取 characters, foreshadows 到对应表
- [ ] 扫描章节 Markdown 文件，创建 Chapter 记录（content_path 指向文件）
- [ ] 导入 `user_settings.json` → UserSettings 表
- [ ] 导入 Agent 记忆 JSON → AgentMemories 表
- [ ] 创建默认 Admin 用户（username: admin, 密码: 提示用户首次登录修改）
- [ ] 事务保护：全部成功或全部回滚
- [ ] 备份原文件到 `App_Data/Backup/{timestamp}/`
- [ ] 测试：在测试数据库上运行，验证记录数和外键完整性

**Commit:** `feat(migration): add script to migrate JSON data to SQLite`

### Task 1.5: 数据迁移脚本 - 文件向量 to Qdrant

**目标:** 将现有文件向量索引迁移到 Qdrant

**Files:**
- Modify: `Scripts/Migration/DataMigrationService.cs`
- Create: `Scripts/Migration/VectorMigrationService.cs`

**Key Steps:**
- [ ] 读取 `Config/guides/chapter_embeddings.json`
- [ ] 读取 `Config/guides/chunk_embeddings.json`
- [ ] 为每个项目创建 Qdrant Collection（`project_{project_id}`）
- [ ] 批量插入章节向量（payload 包含 user_id, project_id, source_type, content）
- [ ] 批量插入分块向量
- [ ] 创建 payload 索引（user_id, project_id, source_type）
- [ ] 验证迁移：对比向量数量，执行相似度搜索测试
- [ ] 保留原文件索引30天（Feature Flag 控制降级）

**Commit:** `feat(migration): migrate file-based vector index to Qdrant`

---

## Phase 2: 后端开发（Week 2-3）

### 里程碑
- JWT 认证授权完成，Postman 可测试登录/注册
- 所有 API 增加用户隔离过滤
- 向量检索服务集成 Qdrant，性能提升10倍以上

### Task 2.1: JWT 认证服务

**目标:** 实现用户注册、登录、Token 验证

**Files:**
- Create: `Web/NovelAgentWeb/Services/Auth/AuthService.cs`
- Create: `Web/NovelAgentWeb/Services/Auth/IAuthService.cs`
- Create: `Web/NovelAgentWeb/Services/Auth/JwtTokenGenerator.cs`
- Create: `Web/NovelAgentWeb/Controllers/AuthController.cs`
- Create: `Web/NovelAgentWeb/Models/Auth/RegisterRequest.cs`
- Create: `Web/NovelAgentWeb/Models/Auth/LoginRequest.cs`
- Create: `Web/NovelAgentWeb/Models/Auth/AuthResponse.cs`
- Modify: `Web/NovelAgentWeb/appsettings.json` (JWT 配置)
- Modify: `Web/NovelAgentWeb/Program.cs` (JWT 中间件)

**Key Steps:**
- [ ] 添加 NuGet 包：`BCrypt.Net-Next`, `System.IdentityModel.Tokens.Jwt`
- [ ] 实现 AuthService.RegisterAsync（BCrypt hash 密码，创建用户+设置）
- [ ] 实现 AuthService.LoginAsync（验证密码，生成 JWT Token）
- [ ] 实现 JwtTokenGenerator（Payload 包含 userId, username, role）
- [ ] 创建 AuthController（POST /api/auth/register, POST /api/auth/login）
- [ ] 配置 JWT 中间件（Authentication, Authorization）
- [ ] 测试：Postman 注册用户，登录获取 Token，使用 Token 访问受保护端点

**Commit:** `feat(auth): implement JWT authentication with register and login`

### Task 2.2: 授权中间件和数据隔离

**目标:** 实现 RBAC，所有查询自动添加 user_id 过滤

**Files:**
- Create: `Web/NovelAgentWeb/Middleware/UserContextMiddleware.cs`
- Create: `Web/NovelAgentWeb/Services/Auth/ICurrentUserService.cs`
- Create: `Web/NovelAgentWeb/Services/Auth/CurrentUserService.cs`
- Create: `Web/NovelAgentWeb/Attributes/ValidateUserOwnershipAttribute.cs`
- Modify: `Web/NovelAgentWeb/Program.cs` (注册中间件)

**Key Steps:**
- [ ] 实现 UserContextMiddleware（从 JWT 提取 userId, role 到 HttpContext）
- [ ] 实现 CurrentUserService（提供 GetUserId(), GetRole(), IsAdmin()）
- [ ] 实现 ValidateUserOwnershipAttribute（验证资源所有权）
- [ ] 创建 QueryFilter 扩展方法（自动添加 .Where(e => e.UserId == currentUserId)）
- [ ] 测试：创建两个用户，验证无法访问对方项目

**Commit:** `feat(auth): add RBAC and data isolation with automatic user filtering`

### Task 2.3: 项目管理 API 重构

**目标:** 重构现有 API，添加用户隔离

**Files:**
- Modify: `Web/NovelAgentWeb/Controllers/ProjectController.cs`
- Create: `Web/NovelAgentWeb/Services/Projects/IProjectService.cs`
- Create: `Web/NovelAgentWeb/Services/Projects/ProjectService.cs`
- Create: `Web/NovelAgentWeb/Models/Projects/CreateProjectRequest.cs`

**Key Steps:**
- [ ] 重构 GET /api/project（添加用户过滤，分页支持）
- [ ] 重构 POST /api/project（自动设置 userId）
- [ ] 重构 GET /api/project/{id}（验证所有权）
- [ ] 重构 DELETE /api/project/{id}（级联删除章节、伏笔、Qdrant Collection）
- [ ] 测试：CRUD 操作，验证用户隔离

**Commit:** `feat(api): refactor project API with user isolation`

### Task 2.4: 向量检索服务重构

**目标:** 替换文件索引为 Qdrant，保持相同接口

**Files:**
- Modify: `Services/Modules/ProjectData/Implementations/Indexing/ContentChunkSearchService.cs`
- Create: `Web/NovelAgentWeb/Services/VectorStore/QdrantSearchService.cs`

**Key Steps:**
- [ ] 实现 QdrantSearchService（封装相似度搜索）
- [ ] 重构 ContentChunkSearchService（调用 Qdrant 而非文件索引）
- [ ] 保持混合检索策略（TF-IDF + Keyword + Vector RRF）
- [ ] 添加用户隔离过滤（must filter: user_id）
- [ ] 性能测试：对比文件索引和 Qdrant 延迟

**Commit:** `feat(vectorstore): replace file index with Qdrant for 10x speedup`

### Task 2.5: Chapter CRUD API

**目标:** 章节创建、更新、删除时同步 Qdrant

**Files:**
- Create: `Web/NovelAgentWeb/Controllers/ChapterController.cs`
- Create: `Web/NovelAgentWeb/Services/Chapters/ChapterService.cs`

**Key Steps:**
- [ ] POST /api/chapters - 创建章节元数据 + Markdown 文件 + 生成向量 + 插入 Qdrant
- [ ] PUT /api/chapters/{id} - 更新章节 + 重新生成向量 + 更新 Qdrant
- [ ] DELETE /api/chapters/{id} - 删除 DB 记录 + 删除文件 + 删除 Qdrant 向量（外键自动清理 Foreshadow 引用）
- [ ] GET /api/chapters/{id} - 读取元数据 + Markdown 内容
- [ ] 测试：创建章节，删除章节，验证伏笔引用被清理

**Commit:** `feat(api): add chapter CRUD with Qdrant synchronization`

---

## Phase 3: 前端开发（Week 4-4.5）

### 里程碑
- 登录注册页面完成，可创建用户账号
- 设置页面重组为 4 标签页 + 折叠面板
- 项目列表显示当前用户项目

### Task 3.1: 登录注册页面

**目标:** 创建认证流程 UI

**Files:**
- Create: `Web/NovelAgentWeb.Frontend/src/pages/LoginPage.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/pages/RegisterPage.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/services/authService.ts`
- Create: `Web/NovelAgentWeb.Frontend/src/hooks/useAuth.ts`
- Create: `Web/NovelAgentWeb.Frontend/src/stores/authStore.ts` (Zustand)

**Key Steps:**
- [ ] 创建 authService（register, login, logout, getStoredToken）
- [ ] 创建 Zustand store（存储 user, token, isAuthenticated）
- [ ] 创建 LoginPage（表单验证，错误提示，记住我）
- [ ] 创建 RegisterPage（密码强度检查，邮箱验证）
- [ ] 创建 ProtectedRoute 组件（未登录重定向到 /login）
- [ ] 集成 React Query 的 useMutation（登录/注册）
- [ ] 测试：注册新用户，登录，刷新页面保持登录状态

**Commit:** `feat(ui): add login and register pages with JWT authentication`

### Task 3.2: 设置页面重组

**目标:** 重构第五页为 4 标签页 + 折叠面板

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/SettingsPage.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/components/Settings/AccountTab.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/components/Settings/AIConfigTab.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/components/Settings/CreativeTab.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/components/Settings/UITab.tsx`
- Create: `Web/NovelAgentWeb.Frontend/src/components/Settings/CollapsePanel.tsx`

**Key Steps:**
- [ ] 创建可复用的 CollapsePanel 组件（受控展开/折叠）
- [ ] 重构 SettingsPage（4个标签页：账号、AI配置、创作、界面）
- [ ] AccountTab: 折叠组（个人信息、安全设置、使用统计、配额管理[仅Admin]）
- [ ] AIConfigTab: 折叠组（大模型配置、Embedding配置、预设管理）
- [ ] CreativeTab: 折叠组（Agent行为、创作默认值、写作偏好）
- [ ] UITab: 折叠组（主题设置、语言设置、编辑器配置）
- [ ] 折叠状态持久化到 localStorage
- [ ] 移动端自适应：标签页变下拉菜单
- [ ] 测试：切换标签，展开折叠，保存设置

**Commit:** `feat(ui): redesign settings page with 4 tabs and collapsible panels`

### Task 3.3: 多用户项目列表

**目标:** 项目列表仅显示当前用户项目

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/ProjectListPage.tsx`
- Modify: `Web/NovelAgentWeb.Frontend/src/services/projectService.ts`

**Key Steps:**
- [ ] 更新 projectService（请求头自动添加 Authorization: Bearer {token}）
- [ ] 重构 ProjectListPage（显示用户名、项目数、存储使用量）
- [ ] 添加空状态提示（"您还没有创建任何项目"）
- [ ] 测试：多个用户登录，验证只能看到自己的项目

**Commit:** `feat(ui): add user-specific project list with isolation`

---

## Phase 4: 测试与优化（Week 5-6）

### 里程碑
- 单元测试覆盖率 >70%
- 集成测试验证外键完整性、用户隔离
- 性能测试通过（1000章节，查询 <50ms）

### Task 4.1: 单元测试 - 数据访问层

**目标:** 测试 Repository 和 DbContext

**Files:**
- Create: `Tests/NovelAgentRegression/Data/UserRepositoryTests.cs`
- Create: `Tests/NovelAgentRegression/Data/ProjectRepositoryTests.cs`
- Create: `Tests/NovelAgentRegression/Data/ForeshadowRepositoryTests.cs`

**Key Steps:**
- [ ] 使用内存 SQLite（`:memory:`）
- [ ] 测试用户 CRUD
- [ ] 测试项目级联删除
- [ ] 测试伏笔外键约束（删除章节后 setup_chapter_id 变 NULL）
- [ ] 测试唯一性约束（重复 username 抛异常）

**Commit:** `test(db): add unit tests for data access layer`

### Task 4.2: 集成测试 - Qdrant

**目标:** 测试向量存储集成

**Files:**
- Create: `Tests/NovelAgentRegression/VectorStore/QdrantIntegrationTests.cs`

**Key Steps:**
- [ ] 使用 Testcontainers 启动 Qdrant（Docker in Docker）
- [ ] 测试 Collection 创建、删除
- [ ] 测试向量插入、查询、过滤（user_id 隔离）
- [ ] 测试批量操作性能（1000 向量 <5秒）

**Commit:** `test(vectorstore): add Qdrant integration tests with Testcontainers`

### Task 4.3: 端到端测试

**目标:** 测试关键用户流程

**Files:**
- Create: `Tests/NovelAgentRegression/E2E/UserJourneyTests.cs`

**Key Steps:**
- [ ] 用户注册 → 登录 → 创建项目 → 创建章节 → 语义检索
- [ ] 删除章节 → 验证伏笔引用被清理
- [ ] Admin 查看其他用户项目

**Commit:** `test(e2e): add end-to-end user journey tests`

### Task 4.4: 性能优化

**目标:** 数据库查询优化、缓存策略

**Files:**
- Create: `Web/NovelAgentWeb/Services/Caching/MemoryCacheService.cs`
- Modify: `Web/NovelAgentWeb/Services/Projects/ProjectService.cs`

**Key Steps:**
- [ ] 添加内存缓存（IMemoryCache）
- [ ] 缓存 UserSettings（5分钟）
- [ ] 缓存 ProjectMetadata（1分钟）
- [ ] 添加 SQL 查询日志（识别慢查询）
- [ ] 优化 N+1 查询（使用 Include）
- [ ] 压力测试：100并发用户，响应时间 <100ms

**Commit:** `perf: add caching and optimize database queries`

### Task 4.5: 文档和部署

**目标:** 编写部署文档，创建一键启动脚本

**Files:**
- Create: `docs/DEPLOYMENT.md`
- Create: `scripts/deploy.sh`
- Update: `README.md`

**Key Steps:**
- [ ] 编写部署文档（环境要求、配置步骤、故障排查）
- [ ] 创建部署脚本（数据库迁移 + Docker 启动 + 应用启动）
- [ ] 更新 README（架构图、快速开始、贡献指南）

**Commit:** `docs: add deployment guide and startup scripts`

---

## 自检清单

实施前验证：
- [ ] 所有实体外键关系正确（特别是 Foreshadow → Chapter）
- [ ] Qdrant Collection 命名规则明确（`project_{id}`）
- [ ] JWT Secret 存环境变量，不提交代码
- [ ] 数据迁移脚本有回滚机制
- [ ] 所有 API 添加 [Authorize] 特性
- [ ] 前端所有请求包含 Authorization Header
- [ ] 测试覆盖关键场景（用户隔离、外键完整性、向量检索）

---

## 执行选项

**Plan complete and saved to `docs/superpowers/plans/2026-06-08-multi-user-novel-agent-implementation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
