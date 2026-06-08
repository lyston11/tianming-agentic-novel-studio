# 多用户小说创作系统架构设计

**设计日期**: 2026-06-08  
**版本**: 1.0  
**架构方案**: 方案C - SQLite + 外部向量数据库（Qdrant）

## 1. 执行摘要

本设计将现有的单用户小说创作系统升级为支持多租户的 SaaS 平台。核心目标是：

1. **多用户隔离**：每个用户独立账号，数据完全隔离
2. **角色管理**：支持管理员（Admin）和作者（Author）两种角色
3. **数据完整性**：通过数据库外键约束解决当前的引用完整性问题
4. **优化设置页**：将混乱的第五页重组为 4 个标签页 + 折叠面板
5. **混合存储架构**：SQLite 管理结构化数据，Qdrant 管理向量数据

### 关键设计决策

- **SaaS 架构模型**：租户隔离型（每用户独立数据空间）
- **用户角色**：极简设计（Admin + Author）
- **记忆系统**：数据库 + 文件混合（元数据入库，详细内容保留文件）
- **设置页面**：分组折叠设计（4 标签页：账号、AI配置、创作、界面）
- **数据库架构**：SQLite（结构化数据）+ Qdrant（向量数据）

## 2. 当前系统分析

### 2.1 现状

**存储方式**：
- 完全基于文件系统，无数据库
- 结构化数据存储为 JSON 文件（`projects.json`, `story_bible.json`, `user_settings.json`）
- 章节内容为 Markdown 文件
- 向量索引为 JSON 文件（`chapter_embeddings.json`, `chunk_embeddings.json`）

**向量检索系统**：
- 自研三层向量索引（章节级、分块级、实体首次出场）
- int8 对称量化压缩
- 本地 Embedding 服务（bge-small-zh-v1.5 ONNX 模型，512维）
- 混合检索策略（TF-IDF + 关键词 + 向量，RRF 融合）
- 当前性能：<100ms（章节数 <1000 时）

**记忆系统**：
- 三层记忆：author_memory（全局）、project_memory（项目级）、execution_memory（执行级）
- 文件存储路径：`App_Data/Projects/{ProjectName}/Agent/`

### 2.2 核心痛点

1. **数据完整性缺失**：删除章节后，伏笔引用、角色首次出场等关联数据不会自动清理
2. **无结构化查询**：无法执行"查询所有未解决的伏笔"等复杂查询
3. **并发安全问题**：文件锁机制简单，高并发场景易出错
4. **设置页混乱**：第五页包含5个标签页，内容组织不清晰
5. **单用户限制**：无法支持多账号、多租户场景

## 3. 架构设计

### 3.1 总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        前端层（React）                            │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 登录注册 │ │ 项目管理 │ │ 章节编辑 │ │ 设置页面 │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
└────────────────────────┬────────────────────────────────────────┘
                         │ REST API
┌────────────────────────▼────────────────────────────────────────┐
│                   后端层（.NET 8.0 / C#）                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ 用户认证服务 │  │ 项目管理服务 │  │ Agent运行时   │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
└─────────┬───────────────────┬───────────────────┬───────────────┘
          │                   │                   │
┌─────────▼─────┐   ┌─────────▼──────┐  ┌─────────▼─────────────┐
│  SQLite DB    │   │ 文件系统       │  │ Qdrant 向量数据库     │
│ ┌───────────┐ │   │ ┌────────────┐ │  │ ┌──────────────────┐ │
│ │用户、项目 │ │   │ │章节 .md    │ │  │ │章节向量          │ │
│ │章节元数据 │ │   │ │Story Bible │ │  │ │分块向量          │ │
│ │伏笔、角色 │ │   │ │素材文件    │ │  │ │HNSW 索引         │ │
│ └───────────┘ │   │ └────────────┘ │  │ └──────────────────┘ │
└───────────────┘   └────────────────┘  └───────────────────────┘
```

### 3.2 混合存储策略

**SQLite 负责**：
- 用户账号（users, user_settings）
- 小说项目元数据（novel_projects, volumes, chapters 表）
- Story Bible 结构化部分（characters, world_settings, plot_threads, foreshadows）
- Agent 记忆索引（agent_memories, agent_sessions）
- 素材库索引（materials, knowledge_base）

**文件系统负责**：
- 章节正文（Markdown 文件，便于版本控制）
- Story Bible 完整 JSON（保持现有结构）
- 素材原文件（图片、文档等）
- 角色详细信息 JSON（复杂嵌套结构）

**Qdrant 负责**：
- 章节 Embedding 向量（512维）
- 分块 Embedding 向量
- 语义检索、相似度搜索

### 3.3 为什么选择 Qdrant

**技术优势**：
- 专为 AI 应用设计的向量数据库
- HNSW 索引算法，比文件索引快 10-100 倍
- 支持过滤、聚合、混合搜索
- gRPC 和 REST API 双协议支持
- 原生支持多租户（Collection per tenant）

**部署简单**：
- Docker 一键部署，无需复杂配置
- 内存消耗低（<512MB 对于中小规模数据）
- 内置持久化，数据不丢失

**可扩展性**：
- 单机可支持百万级向量
- 支持水平扩展（Cluster 模式）
- 云服务版本（Qdrant Cloud）可选

**对比自研文件索引**：
| 特性 | 文件索引 | Qdrant |
|------|---------|--------|
| 检索速度（1000章） | ~100ms | ~5-10ms |
| 过滤查询 | 需要后处理 | 原生支持 |
| 并发性能 | 文件锁限制 | 高并发优化 |
| 内存占用 | 需全部加载 | 按需加载 |
| 可扩展性 | <5000章 | >百万章 |

## 4. 数据库设计

### 4.1 SQLite Schema

```sql
-- 用户表
CREATE TABLE users (
    id TEXT PRIMARY KEY,  -- UUID
    username TEXT UNIQUE NOT NULL,
    email TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,  -- BCrypt
    role TEXT NOT NULL CHECK(role IN ('admin', 'author')),
    storage_quota_mb INTEGER DEFAULT 5120,  -- 5GB
    api_call_quota INTEGER DEFAULT 10000,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    last_login_at DATETIME,
    is_active BOOLEAN DEFAULT 1
);

-- 用户设置表（从 user_settings.json 迁移）
CREATE TABLE user_settings (
    user_id TEXT PRIMARY KEY,
    -- LLM 配置
    llm_provider TEXT,
    llm_api_key_encrypted TEXT,  -- AES-256 加密
    llm_base_url TEXT,
    llm_model TEXT,
    llm_temperature REAL DEFAULT 0.7,
    llm_max_tokens INTEGER DEFAULT 4096,
    -- Embedding 配置
    embedding_provider TEXT DEFAULT 'local',
    embedding_model TEXT DEFAULT 'bge-small-zh-v1.5',
    -- Agent 配置
    agent_default_risk TEXT DEFAULT 'Medium',
    agent_auto_continue BOOLEAN DEFAULT 1,
    agent_max_auto_steps INTEGER DEFAULT 12,
    -- 创作默认值
    default_genre TEXT DEFAULT '玄幻',
    default_chapter_word_count INTEGER DEFAULT 3000,
    -- 界面偏好
    theme TEXT DEFAULT 'dark',
    language TEXT DEFAULT 'zh-CN',
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- 小说项目表（从 projects.json 迁移）
CREATE TABLE novel_projects (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    title TEXT NOT NULL,
    genre TEXT,
    sub_genre TEXT,
    core_hook TEXT,
    status TEXT DEFAULT 'draft' CHECK(status IN ('draft', 'writing', 'completed')),
    word_count INTEGER DEFAULT 0,
    cover_image_url TEXT,
    storage_project_name TEXT UNIQUE,  -- 文件系统目录名
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- 卷表
CREATE TABLE volumes (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    title TEXT NOT NULL,
    volume_number INTEGER NOT NULL,
    summary TEXT,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- 章节元数据表
CREATE TABLE chapters (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    volume_id TEXT,
    title TEXT NOT NULL,
    chapter_number INTEGER NOT NULL,
    word_count INTEGER DEFAULT 0,
    status TEXT DEFAULT 'draft' CHECK(status IN ('draft', 'writing', 'completed')),
    content_path TEXT NOT NULL,  -- 指向 Markdown 文件
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE,
    FOREIGN KEY (volume_id) REFERENCES volumes(id) ON DELETE SET NULL
);

-- 伏笔账本表（关键：解决引用完整性问题）
CREATE TABLE foreshadows (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    name TEXT NOT NULL,
    type TEXT,
    status TEXT DEFAULT 'planned' CHECK(status IN ('planned', 'setup', 'developing', 'resolved')),
    setup_chapter_id TEXT,  -- 外键保护
    payoff_chapter_id TEXT,
    importance INTEGER,
    description TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE,
    FOREIGN KEY (setup_chapter_id) REFERENCES chapters(id) ON DELETE SET NULL,
    FOREIGN KEY (payoff_chapter_id) REFERENCES chapters(id) ON DELETE SET NULL
);

-- 角色表
CREATE TABLE characters (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    name TEXT NOT NULL,
    role TEXT CHECK(role IN ('protagonist', 'antagonist', 'supporting', 'minor')),
    identity TEXT,
    description TEXT,
    personality TEXT,
    first_appearance_chapter_id TEXT,
    detail_json_path TEXT,  -- 复杂数据存文件
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE,
    FOREIGN KEY (first_appearance_chapter_id) REFERENCES chapters(id) ON DELETE SET NULL
);

-- 世界观设定表
CREATE TABLE world_settings (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    category TEXT NOT NULL CHECK(category IN ('location', 'power_system', 'culture', 'history', 'other')),
    name TEXT NOT NULL,
    description TEXT,
    rules TEXT,  -- JSON 格式
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- 素材库表
CREATE TABLE materials (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT,  -- NULL 表示全局素材
    title TEXT NOT NULL,
    category TEXT,
    content_type TEXT CHECK(content_type IN ('text', 'image', 'url', 'file')),
    content TEXT,  -- 短文本直接存
    file_path TEXT,  -- 长文本/文件存路径
    tags TEXT,  -- JSON 数组
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- 知识库表
CREATE TABLE knowledge_base (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    entry_type TEXT NOT NULL,
    title TEXT NOT NULL,
    content TEXT NOT NULL,
    usage_count INTEGER DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- Agent 记忆表
CREATE TABLE agent_memories (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT,  -- NULL = author_memory（全局）
    memory_type TEXT NOT NULL CHECK(memory_type IN ('author', 'project', 'execution')),
    content TEXT NOT NULL,  -- JSON 格式
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- Agent 会话表
CREATE TABLE agent_sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    project_id TEXT,
    session_data TEXT,  -- JSON 格式
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (project_id) REFERENCES novel_projects(id) ON DELETE CASCADE
);

-- 索引
CREATE INDEX idx_chapters_project ON chapters(project_id);
CREATE INDEX idx_chapters_volume ON chapters(volume_id);
CREATE INDEX idx_foreshadows_status ON foreshadows(status);
CREATE INDEX idx_foreshadows_project ON foreshadows(project_id);
CREATE INDEX idx_characters_project ON characters(project_id);
CREATE INDEX idx_materials_user ON materials(user_id);
CREATE INDEX idx_materials_project ON materials(project_id);
CREATE INDEX idx_memories_user_project ON agent_memories(user_id, project_id);
```

### 4.2 Qdrant Collections

**Collection 设计**：每个项目一个 Collection，命名规则：`project_{project_id}`

**向量结构**：
```json
{
  "id": "chapter_uuid_or_chunk_uuid",
  "vector": [0.123, -0.456, ...],  // 512 维
  "payload": {
    "user_id": "uuid",
    "project_id": "uuid",
    "source_type": "chapter|chunk|character",
    "source_id": "uuid",
    "chapter_id": "uuid",
    "chunk_index": 0,
    "content": "原文内容",
    "metadata": {
      "chapter_number": 1,
      "word_count": 3000,
      "status": "completed"
    }
  }
}
```

**索引配置**：
- 向量维度：512
- 距离度量：Cosine
- 索引类型：HNSW (m=16, ef_construct=100)
- 量化：Scalar（保持精度）

**过滤字段**：
- `user_id`：租户隔离
- `project_id`：项目隔离
- `source_type`：数据类型过滤
- `chapter_id`：章节级过滤

## 5. 文件系统布局

```
App_Data/
├── Database/
│   └── novelagent.db                    ← SQLite 数据库
├── Users/
│   └── {userId}/
│       ├── Projects/
│       │   └── {projectId}/
│       │       ├── Chapters/
│       │       │   ├── chapter_001.md
│       │       │   └── chapter_002.md
│       │       ├── StoryBible/
│       │       │   └── story_bible.json  ← 完整 Story Bible
│       │       ├── Characters/
│       │       │   └── {characterId}_detail.json
│       │       └── Materials/
│       │           ├── images/
│       │           └── documents/
│       └── Settings/
│           └── cache/                    ← 临时缓存
└── Qdrant/                               ← Qdrant 数据目录（Docker volume）
```

## 6. 用户界面优化

### 6.1 设置页面重组

**当前问题**：5个标签页内容混乱，无清晰分组

**新设计**：4个标签页 + 折叠面板

**标签页 1：账号管理**
- 折叠组 1.1：个人信息（用户名、邮箱、头像）
- 折叠组 1.2：安全设置（密码修改、两步验证）
- 折叠组 1.3：使用统计（存储空间、API 调用量）
- 折叠组 1.4：配额管理（仅 Admin 可见：调整用户配额）

**标签页 2：AI 配置**
- 折叠组 2.1：大模型配置（Provider、API Key、Model、温度、Token）
- 折叠组 2.2：Embedding 配置（Provider、Model）
- 折叠组 2.3：预设管理（保存/加载常用配置）

**标签页 3：创作设置**
- 折叠组 3.1：Agent 行为（默认风险级别、自动继续、最大步数）
- 折叠组 3.2：创作默认值（默认题材、章节字数）
- 折叠组 3.3：写作偏好（文风、节奏控制）

**标签页 4：界面偏好**
- 折叠组 4.1：主题设置（Dark/Light、自定义颜色）
- 折叠组 4.2：语言设置（中文/英文）
- 折叠组 4.3：编辑器配置（字体、行距、快捷键）

### 6.2 实现技术

- 使用 React 的 `Collapse` 组件（shadcn/ui 或 Ant Design）
- 状态管理：React Query（保持现有架构）
- 折叠状态持久化到 localStorage
- 移动端自适应：标签页折叠为下拉菜单

## 7. 认证与授权

### 7.1 认证流程

**注册**：
1. 用户提交 username, email, password
2. 后端验证邮箱格式、用户名唯一性
3. 密码用 BCrypt 哈希（cost=12）
4. 创建用户记录，初始化默认设置
5. 创建用户文件系统目录

**登录**：
1. 用户提交 email/username + password
2. 后端查询用户，验证 BCrypt 哈希
3. 生成 JWT Token（有效期：7天）
4. 返回 Token + 用户信息

**JWT Payload**：
```json
{
  "sub": "user_id",
  "username": "lyston",
  "role": "author",
  "exp": 1654321098
}
```

### 7.2 授权模型

**角色权限**：

| 功能 | Admin | Author |
|------|-------|--------|
| 管理用户账号 | ✓ | ✗ |
| 调整用户配额 | ✓ | ✗ |
| 创建小说项目 | ✓ | ✓ |
| 编辑自己的项目 | ✓ | ✓ |
| 查看其他人的项目 | ✓ | ✗ |
| 修改系统设置 | ✓ | ✗ |

**数据隔离**：
- 所有查询自动添加 `WHERE user_id = current_user_id` 过滤
- Qdrant 查询自动添加 `must: {key: "user_id", match: {value: current_user_id}}`
- Admin 可通过显式参数跨用户查询（审计、支持场景）

## 8. 迁移策略

### 8.1 数据迁移

**Phase 1：SQLite 初始化**
1. 创建数据库文件：`App_Data/Database/novelagent.db`
2. 执行 Schema 创建脚本
3. 创建默认管理员账号

**Phase 2：现有数据导入**
1. 读取 `projects.json`，为每个项目创建记录
2. 解析 `story_bible.json`，拆分到 characters, foreshadows, world_settings 表
3. 扫描章节 Markdown 文件，创建 chapters 记录
4. 导入 `user_settings.json` → user_settings 表
5. 导入 Agent 记忆 JSON → agent_memories 表

**Phase 3：向量数据迁移到 Qdrant**
1. 启动 Qdrant Docker 容器
2. 读取现有 `chapter_embeddings.json` 和 `chunk_embeddings.json`
3. 为每个项目创建 Qdrant Collection
4. 批量导入向量数据（batch_size=100）
5. 验证迁移完整性（向量数量、检索结果对比）

**迁移脚本**：`Scripts/Migration/MigrateToHybridArchitecture.cs`

### 8.2 向下兼容

**保留旧文件**：
- 迁移后保留原 JSON 文件 30 天（移动到 `App_Data/Backup/`）
- 提供回滚脚本（万一出问题）

**双写期**（1周）：
- 数据写入同时更新数据库和文件
- 定期对比数据一致性
- 验证通过后关闭文件写入

### 8.3 迁移风险与缓解

**风险 1：向量数据迁移失败**
- 缓解：分批迁移，每批次验证
- 回滚：保留原文件索引，切换回文件检索

**风险 2：数据库性能不足**
- 缓解：压力测试，优化索引
- 备选：切换到 PostgreSQL（Schema 兼容）

**风险 3：Qdrant 服务不稳定**
- 缓解：配置健康检查，自动重启
- 备选：降级到文件索引（Feature Flag 控制）

## 9. Qdrant 部署

### 9.1 Docker Compose 配置

```yaml
version: '3.8'
services:
  qdrant:
    image: qdrant/qdrant:v1.8.0
    container_name: novelagent-qdrant
    ports:
      - "6333:6333"  # REST API
      - "6334:6334"  # gRPC
    volumes:
      - ./App_Data/Qdrant:/qdrant/storage
    environment:
      - QDRANT__SERVICE__GRPC_PORT=6334
      - QDRANT__LOG_LEVEL=INFO
    restart: unless-stopped
```

### 9.2 初始化脚本

```csharp
// Services/Framework/VectorStore/QdrantVectorStore.cs
public async Task InitializeProjectCollectionAsync(string projectId)
{
    var collectionName = $"project_{projectId}";
    
    await _qdrantClient.CreateCollectionAsync(collectionName, new VectorParams
    {
        Size = 512,
        Distance = Distance.Cosine
    });
    
    await _qdrantClient.CreatePayloadIndexAsync(collectionName, 
        "user_id", PayloadSchemaType.Keyword);
    await _qdrantClient.CreatePayloadIndexAsync(collectionName, 
        "project_id", PayloadSchemaType.Keyword);
    await _qdrantClient.CreatePayloadIndexAsync(collectionName, 
        "source_type", PayloadSchemaType.Keyword);
}
```

### 9.3 健康监控

- 健康检查端点：`http://localhost:6333/health`
- 指标收集：内存使用、向量数量、查询延迟
- 告警阈值：延迟 >100ms、内存 >80%

## 10. 性能优化

### 10.1 数据库优化

**连接池**：
- Min Pool Size: 5
- Max Pool Size: 50
- Connection Lifetime: 300s

**查询优化**：
- 所有外键字段建立索引
- 高频查询使用预编译语句
- 大结果集分页加载（Page Size: 50）

**缓存策略**：
- User Settings：内存缓存 5 分钟
- Project Metadata：内存缓存 1 分钟
- Chapter List：按需加载，不全量缓存

### 10.2 向量检索优化

**HNSW 参数调优**：
- `ef_search`: 64（平衡速度和召回率）
- `m`: 16（连接数）
- `ef_construct`: 100（构建索引质量）

**批量查询**：
- 单次查询最多返回 20 个结果
- 使用 `with_payload`: false 减少数据传输（按需获取）

**预热**：
- 启动时预加载最近使用的 Collection
- 定期执行 dummy 查询保持热度

## 11. 安全性

### 11.1 数据加密

**静态数据**：
- API Key：AES-256 加密存储，密钥存环境变量
- 密码：BCrypt 哈希（cost=12）
- SQLite 文件：可选 SQLCipher 加密（企业版）

**传输数据**：
- 前后端通信：HTTPS（TLS 1.3）
- Qdrant 通信：gRPC over TLS（生产环境）

### 11.2 注入防护

**SQL 注入**：
- 使用参数化查询（EF Core）
- 禁止动态 SQL 拼接

**XSS 防护**：
- 所有用户输入 HTML 转义
- CSP Header：`default-src 'self'`

### 11.3 RBAC 实施

**Middleware 验证**：
```csharp
[Authorize(Roles = "admin")]
public async Task<IActionResult> UpdateUserQuota(string userId, int quotaMb)
{
    // 仅 Admin 可调整配额
}

[Authorize]
[ValidateUserOwnership] // 自定义 Attribute
public async Task<IActionResult> UpdateProject(string projectId, ...)
{
    // 验证 project.user_id == current_user_id
}
```

## 12. 测试策略

### 12.1 单元测试

**数据访问层**：
- Repository 模式测试（使用内存 SQLite）
- Qdrant Client 测试（Mock）

**业务逻辑层**：
- 用户认证流程
- 数据隔离验证
- 引用完整性测试

### 12.2 集成测试

**数据库集成**：
- 外键约束验证（删除章节后伏笔自动清理）
- 事务回滚测试
- 并发写入测试

**Qdrant 集成**：
- 向量插入、查询、删除
- 过滤查询准确性
- 租户隔离验证

### 12.3 端到端测试

**关键流程**：
- 用户注册 → 创建项目 → 创建章节 → 语义检索
- 删除章节 → 验证伏笔引用被清理
- Admin 管理用户配额

## 13. 实施计划

### 13.1 阶段划分

**阶段 1：基础设施（1周）**
- Day 1-2：SQLite Schema 设计与创建
- Day 3-4：Qdrant Docker 配置与测试
- Day 5-7：数据迁移脚本开发

**阶段 2：后端开发（2周）**
- Week 1：用户认证、项目管理 API
- Week 2：向量检索服务、数据访问层重构

**阶段 3：前端开发（1.5周）**
- Day 1-3：登录注册页面
- Day 4-7：设置页面重组（4 标签页 + 折叠面板）
- Day 8-10：项目管理界面调整

**阶段 4：测试与优化（1.5周）**
- Day 1-4：单元测试、集成测试
- Day 5-7：性能测试、压力测试
- Day 8-10：Bug 修复、文档完善

**总工期：6周**

### 13.2 里程碑

- Week 1 End：数据迁移完成，Qdrant 运行正常
- Week 3 End：后端 API 开发完成，通过 Postman 测试
- Week 4.5 End：前端界面完成，功能可演示
- Week 6 End：测试通过，正式上线

### 13.3 风险缓冲

- 每个阶段预留 20% 缓冲时间
- 关键路径：Qdrant 迁移（风险最高）
- 如超期 1 周，考虑降级到方案 A（SQLite + 文件索引）

## 14. 运维与监控

### 14.1 日志记录

**分级**：
- ERROR：数据库连接失败、Qdrant 不可用、认证失败
- WARN：查询超时、缓存未命中
- INFO：用户登录、项目创建、章节保存

**存储**：
- 文件日志：`Logs/novelagent-{date}.log`（滚动，保留 30 天）
- 结构化日志：Serilog + Seq（可选）

### 14.2 监控指标

**系统指标**：
- SQLite 查询延迟（P50, P95, P99）
- Qdrant 查询延迟
- 内存使用、磁盘空间

**业务指标**：
- 活跃用户数（DAU/MAU）
- 项目创建数
- 章节生成数
- API 调用失败率

### 14.3 备份策略

**SQLite 备份**：
- 每日全量备份（凌晨 3:00）
- 保留最近 7 天
- 备份到 `App_Data/Backups/`

**Qdrant 备份**：
- 每日快照（`qdrant-cli snapshot`）
- 保留最近 3 天

**文件系统备份**：
- 章节 Markdown 文件：Git 版本控制
- 素材文件：定期同步到对象存储（可选）

## 15. 未来扩展

### 15.1 水平扩展

**数据库层**：
- SQLite → PostgreSQL（单主）
- PostgreSQL → Citus（分布式）

**向量层**：
- Qdrant 单机 → Qdrant Cluster
- 或迁移到 Qdrant Cloud

### 15.2 功能扩展

- 多人协作编辑（WebSocket + CRDT）
- 版本历史管理（集成 Git）
- AI 辅助翻译（多语言小说）
- 出版导出（EPUB、PDF）

## 16. 总结

本设计采用**混合架构**（SQLite + Qdrant），在保持现有优势（文件存储章节、本地 Embedding）的基础上，引入数据库解决引用完整性、结构化查询、并发安全等核心问题，同时用专业向量数据库提升检索性能。

**关键优势**：
- ✅ 解决数据完整性问题（外键约束）
- ✅ 支持复杂查询（"查询所有未解决的伏笔"）
- ✅ 向量检索性能提升 10-100 倍
- ✅ 支持多租户隔离
- ✅ 设置页面清晰易用（4 标签页 + 折叠面板）

**实施可行性**：
- 工期：6 周
- 风险：中等（Qdrant 迁移是关键路径）
- 可回滚：保留原文件 30 天

**长期收益**：
- 可扩展到万级用户
- 向量检索可处理百万章节
- 平滑升级路径（SQLite → PostgreSQL）
