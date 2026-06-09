# 天命 Agentic Novel Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-19.2-blue.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-6.0-blue.svg)](https://www.typescriptlang.org/)

> AI 不会天然记得一本千万字小说。这个项目做的事情是：把故事变成系统能管理的数据，让 Agent 按状态、账本和创作约束推进长篇小说。

**Tianming Agentic Novel Studio** 是一个基于 AI Agent 的长篇小说创作工作台，支持多用户协作、向量检索和智能内容生成。系统通过结构化的故事管理、伏笔账本和 RAG 技术，帮助创作者管理复杂的长篇叙事。

---

## 特性

### 核心功能

- **🔐 多用户支持**: JWT 认证，用户数据隔离，多项目管理
- **📚 项目管理**: 创建、编辑、删除小说项目，支持项目元数据管理
- **📖 章节管理**: 完整的 CRUD 操作，Markdown 格式存储
- **🔍 向量检索**: 基于 Qdrant 的语义搜索，找到相似章节和相关片段
- **🤖 NovelAgent**: AI 驱动的章节生成，包含故事地基、卷规划、章节规划和生成后复盘
- **📊 账本管理**: 
  - **伏笔账本**: Planned → Setup → Reinforced → Due → Paid Off 生命周期
  - **角色账本**: 目标、秘密、关系、能力代价、心理压力
  - **Canon 账本**: 整书创意宪法、类型承诺、禁区
- **💾 数据持久化**: SQLite 数据库 + 文件系统 + Qdrant 向量存储
- **🚀 现代化前端**: React + TypeScript + Vite，响应式设计

### 技术亮点

- **三层架构**: 前端 (React) → API (ASP.NET Core) → 数据层 (SQLite + Qdrant)
- **RESTful API**: 标准化的 HTTP 接口，易于扩展
- **向量嵌入**: 章节内容自动向量化，支持语义搜索
- **实时状态管理**: Zustand + TanStack Query 数据管理
- **自动化部署**: 一键部署脚本，健康检查

---

## 系统架构

```
┌─────────────┐      HTTP/JSON      ┌──────────────────┐
│   React     │ ──────────────────> │  ASP.NET Core    │
│  Frontend   │                     │   Web API        │
│  (Vite)     │ <────────────────── │   (.NET 8)       │
└─────────────┘                     └──────────────────┘
                                            │
                    ┌───────────────────────┼───────────────────────┐
                    ↓                       ↓                       ↓
            ┌───────────────┐      ┌──────────────┐      ┌──────────────┐
            │    SQLite     │      │   Qdrant     │      │ File System  │
            │   Database    │      │  Vector DB   │      │   (Markdown) │
            │               │      │              │      │              │
            │ • Users       │      │ • Embeddings │      │ • Chapters   │
            │ • Projects    │      │ • Collections│      │ • Sessions   │
            │ • Chapters    │      │ • Vectors    │      │ • Configs    │
            │ • Ledgers     │      │              │      │              │
            └───────────────┘      └──────────────┘      └──────────────┘
```

详细架构文档请参考 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

---

## 核心机制

每一章都应围绕闭环推进，而不是只生成一段正文：

```
故事地基 → 卷级规划 → 章节候选 → 用户确认 → 正文生成 → 生成后复盘 → 账本沉淀 → 下一章读取最新状态
```

NovelAgent 重点维护这些长期状态：

- **Story Bible**: 整书创意宪法、类型承诺、禁区、Canon Ledger
- **卷级规划**: 卷承诺、节拍、反转、高潮、伏笔投放/回收计划
- **章节创意简报**: 多个候选、商业节奏、重复桥段风险、RAG 相似片段
- **伏笔账本**: Planned、Setup、Reinforced、Due、PaidOff 等状态
- **角色账本**: 目标、秘密、关系、能力代价、心理压力
- **生成后复盘**: Proposed Canon、伏笔变化、角色状态变化、质量分和下一章建议

---

## 快速开始

### 前置要求

- .NET SDK 8.0+
- Node.js 18.0+
- Docker 24.0+ (用于 Qdrant)
- npm 9.0+

### 一键部署

使用自动化部署脚本：

```bash
chmod +x scripts/deploy.sh
./scripts/deploy.sh
```

脚本将自动完成：
- ✓ 检查环境依赖
- ✓ 启动 Qdrant 容器
- ✓ 运行数据库迁移
- ✓ 构建后端
- ✓ 构建前端
- ✓ 运行健康检查

### 手动部署

#### 1. 启动 Qdrant

```bash
docker-compose up -d qdrant
```

验证 Qdrant 运行状态：

```bash
curl http://localhost:6333/health
```

#### 2. 数据库迁移

```bash
cd Web/NovelAgentWeb
dotnet ef database update
```

#### 3. 构建并启动后端

```bash
cd Web/NovelAgentWeb
dotnet run
```

后端默认运行在 `http://localhost:5000`

#### 4. 构建前端

```bash
cd Web/NovelAgentWeb.Frontend
npm install
npm run build
```

开发模式（带热重载）：

```bash
npm run dev
```

前端开发服务器运行在 `http://localhost:5173`

#### 5. 访问应用

打开浏览器访问：`http://localhost:5000`

---

## 项目结构

```
tianming-agentic-novel-studio/
├── Web/
│   ├── NovelAgentWeb/              # ASP.NET Core Web API
│   │   ├── Controllers/            # API 控制器
│   │   ├── Services/               # 业务逻辑服务
│   │   ├── Data/                   # Entity Framework 数据访问
│   │   ├── Models/                 # 数据模型
│   │   └── App_Data/               # 应用数据目录
│   │       ├── Database/           # SQLite 数据库
│   │       ├── Qdrant/             # Qdrant 向量存储
│   │       └── Projects/           # 项目文件
│   └── NovelAgentWeb.Frontend/     # React + TypeScript 前端
│       ├── src/
│       │   ├── components/         # React 组件
│       │   ├── pages/              # 页面组件
│       │   ├── services/           # API 服务
│       │   ├── stores/             # Zustand 状态管理
│       │   └── types/              # TypeScript 类型定义
│       └── dist/                   # 构建输出
├── Services/Framework/AI/NovelAgent/ # NovelAgent 核心逻辑
│   ├── Models/                     # Agent 数据模型
│   ├── Orchestrators/              # 编排器
│   └── Services/                   # Agent 服务
├── Tests/
│   ├── NovelAgentRegression/       # 核心回归测试
│   └── Fixtures/NovelAgent/        # 测试数据
├── Scripts/
│   ├── deploy.sh                   # 自动化部署脚本
│   └── Migration/                  # 数据迁移工具
├── docs/
│   ├── ARCHITECTURE.md             # 架构文档
│   ├── DEPLOYMENT.md               # 部署文档
│   └── superpowers/plans/          # 实施计划
├── docker-compose.yml              # Docker Compose 配置
└── README.md                       # 本文件
```

---

## 技术栈

### 后端

| 技术 | 版本 | 用途 |
|------|------|------|
| ASP.NET Core | 8.0 | Web 框架 |
| Entity Framework Core | 8.0 | ORM 数据访问 |
| SQLite | 3.x | 关系数据库 |
| Qdrant | 1.8.0 | 向量数据库 |
| JWT | - | 身份认证 |

### 前端

| 技术 | 版本 | 用途 |
|------|------|------|
| React | 19.2 | UI 框架 |
| TypeScript | 6.0 | 类型安全 |
| Vite | 8.0 | 构建工具 |
| Zustand | 5.0 | 状态管理 |
| TanStack Query | 5.101 | 数据获取 |
| React Router | 7.17 | 路由管理 |

### 基础设施

| 技术 | 版本 | 用途 |
|------|------|------|
| Docker | 24+ | 容器化 |
| Docker Compose | 2+ | 容器编排 |
| Nginx | - | 反向代理（生产环境推荐） |

---

## API 文档

### 认证

#### 注册
```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "user123",
  "password": "SecurePass123!",
  "email": "user@example.com"
}
```

#### 登录
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "user123",
  "password": "SecurePass123!"
}
```

返回 JWT Token，用于后续请求：
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiry": "2026-06-15T10:30:00Z"
}
```

### 项目管理

#### 获取项目列表
```http
GET /api/projects
Authorization: Bearer {token}
```

#### 创建项目
```http
POST /api/projects
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "我的小说",
  "description": "一个精彩的故事"
}
```

#### 获取项目详情
```http
GET /api/projects/{projectId}
Authorization: Bearer {token}
```

### 章节管理

#### 获取章节列表
```http
GET /api/projects/{projectId}/chapters
Authorization: Bearer {token}
```

#### 创建章节
```http
POST /api/projects/{projectId}/chapters
Authorization: Bearer {token}
Content-Type: application/json

{
  "title": "第一章",
  "content": "章节内容...",
  "order": 1
}
```

### 向量检索

#### 语义搜索
```http
POST /api/vector/search
Authorization: Bearer {token}
Content-Type: application/json

{
  "query": "主角的冒险经历",
  "projectId": "abc123",
  "limit": 5
}
```

---

## 配置

### 环境变量

生产环境必须设置的环境变量：

```bash
# JWT 密钥 (必须修改！)
export JWT_SECRET_KEY="your-super-secure-random-string-min-32-chars"

# 环境标识
export ASPNETCORE_ENVIRONMENT="Production"

# 数据库路径 (可选)
export ConnectionStrings__NovelAgentDb="Data Source=/data/novelagent.db"

# Qdrant 连接 (可选)
export Qdrant__BaseUrl="http://qdrant-server:6333"
export Qdrant__Host="qdrant-server"
```

### appsettings.json

位于 `Web/NovelAgentWeb/appsettings.json`，包含核心配置：

```json
{
  "ConnectionStrings": {
    "NovelAgentDb": "Data Source=App_Data/Database/novelagent.db"
  },
  "NovelAgent": {
    "StorageRoot": "App_Data",
    "ProjectName": "AgenticNovelStudio"
  },
  "Qdrant": {
    "BaseUrl": "http://localhost:6333",
    "Host": "localhost",
    "Port": 6334,
    "VectorDimension": 512
  },
  "JwtSettings": {
    "SecretKey": "CHANGE_THIS_IN_PRODUCTION",
    "Issuer": "NovelAgentWeb",
    "Audience": "NovelAgentWeb",
    "ExpiryDays": "7"
  }
}
```

详细配置说明请参考 [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)。

---

## 测试

### 运行单元测试

```bash
cd Tests/NovelAgentRegression
dotnet test
```

### 核心回归测试

```bash
cd Tests/NovelAgentRegression
dotnet run
```

回归测试覆盖：
- ✓ 故事地基 (Story Foundation)
- ✓ 卷规划 (Volume Planning)
- ✓ 章节规划 (Chapter Planning)
- ✓ 章节执行 (Chapter Execution)
- ✓ 生成后复盘 (Post-Reflection)
- ✓ Canon/伏笔/角色账本 (Ledgers)
- ✓ RAG 相似片段 (Vector Retrieval)

---

## 部署

### 开发环境

```bash
# 1. 启动 Qdrant
docker-compose up -d qdrant

# 2. 启动后端
cd Web/NovelAgentWeb
dotnet run

# 3. 启动前端开发服务器
cd Web/NovelAgentWeb.Frontend
npm run dev
```

### 生产环境

详细部署指南请参考 [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)，包括：

- ✓ 环境要求和前置依赖
- ✓ 数据库配置和迁移
- ✓ Qdrant 向量数据库设置
- ✓ 前后端构建和部署
- ✓ 健康检查和监控
- ✓ 故障排查和日志分析
- ✓ 备份和恢复流程
- ✓ 性能优化和扩展建议

---

## 文档

- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)**: 系统架构详解，包括数据模型、API 设计、服务架构
- **[DEPLOYMENT.md](docs/DEPLOYMENT.md)**: 完整部署指南，包括环境配置、故障排查、生产优化
- **[实施计划](docs/superpowers/plans/)**: 各阶段开发计划和任务清单

---

## 贡献指南

欢迎贡献代码、报告问题和提出建议！

### 开发流程

1. **Fork** 本仓库
2. 创建特性分支 (`git checkout -b feature/amazing-feature`)
3. 提交更改 (`git commit -m 'feat: add amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 创建 **Pull Request**

### 提交规范

使用 [Conventional Commits](https://www.conventionalcommits.org/) 规范：

- `feat:` 新功能
- `fix:` Bug 修复
- `docs:` 文档更新
- `refactor:` 代码重构
- `test:` 测试相关
- `chore:` 构建/工具链更新

### 代码规范

- **后端**: 遵循 C# 编码约定，使用 `.editorconfig`
- **前端**: 使用 ESLint + Prettier，运行 `npm run lint`

---

## 路线图

### 已完成 ✓

- [x] 多用户认证和授权
- [x] SQLite 数据库集成
- [x] Qdrant 向量检索
- [x] 项目和章节 CRUD API
- [x] React 前端界面
- [x] JWT Token 认证
- [x] 用户数据隔离
- [x] 自动化部署脚本
- [x] 单元测试和集成测试
- [x] 性能优化和缓存

### 计划中

- [ ] PostgreSQL 支持（替代 SQLite，提升并发）
- [ ] Redis 缓存层（提升响应速度）
- [ ] WebSocket 实时协作
- [ ] 富文本编辑器集成
- [ ] 章节版本控制
- [ ] AI 模型配置化（支持多种 LLM）
- [ ] 团队协作功能
- [ ] 导出为 EPUB/PDF
- [ ] 移动端适配

---

## 常见问题

### Q: 如何修改 JWT 密钥？

**A:** 编辑 `Web/NovelAgentWeb/appsettings.json` 中的 `JwtSettings:SecretKey`，或通过环境变量设置 `JWT_SECRET_KEY`。密钥至少 32 个字符。

### Q: 如何迁移现有 JSON 数据到数据库？

**A:** 运行迁移脚本：
```bash
cd Scripts/Migration
dotnet run
```

### Q: Qdrant 连接失败怎么办？

**A:** 检查 Docker 容器是否运行：
```bash
docker ps | grep qdrant
curl http://localhost:6333/health
```

### Q: 前端构建失败？

**A:** 清除缓存并重新安装依赖：
```bash
cd Web/NovelAgentWeb.Frontend
rm -rf node_modules dist
npm install
npm run build
```

### Q: 数据库迁移失败？

**A:** 检查 EF Core 工具是否安装：
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations list
dotnet ef database update
```

更多问题请参考 [docs/DEPLOYMENT.md#troubleshooting](docs/DEPLOYMENT.md#troubleshooting)。

---

## 许可证

[MIT License](LICENSE)

> **商用须知**: 本项目代码基于 MIT 协议开源，但任何商业用途（包括但不限于二次包装售卖、商业 SaaS 部署、嵌入付费产品等）请先联系原作者获得授权。

---

## 致谢

- [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet) - 强大的 Web 框架
- [React](https://react.dev/) - 优雅的 UI 库
- [Qdrant](https://qdrant.tech/) - 高性能向量数据库
- [Entity Framework Core](https://docs.microsoft.com/ef/core/) - 现代化 ORM
- [Vite](https://vitejs.dev/) - 极速构建工具

---

## 联系方式

- **GitHub Issues**: 报告 Bug 和功能请求
- **Discussions**: 技术讨论和问题咨询
- **Email**: [联系邮箱]

---

**Built with ❤️ for writers and storytellers**
