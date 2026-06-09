# 多用户小说创作系统 - Phase 1: 基础设施实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 SQLite 数据库、配置 Qdrant 向量数据库、创建数据迁移脚本

**Architecture:** 混合存储架构 - SQLite 管理结构化数据（用户、项目、章节元数据），Qdrant 管理向量数据（512维 embeddings），文件系统保留章节正文和 Story Bible

**Tech Stack:** 
- .NET 8.0 / C#
- SQLite with EF Core 8.0
- Qdrant v1.8.0 (Docker)
- Docker Compose
- Qdrant.Client NuGet package

**Duration:** 1 周（Day 1-7）

---

## Task 1: SQLite 数据库初始化

**Files:**
- Create: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/User.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/UserSettings.cs`
- Create: `Web/NovelAgentWeb/Data/Migrations/InitialCreate.cs`
- Modify: `Web/NovelAgentWeb/NovelAgentWeb.csproj`
- Create: `Web/NovelAgentWeb/appsettings.json` (if not exists)

- [ ] **Step 1: 添加 EF Core SQLite NuGet 包**

修改 `Web/NovelAgentWeb/NovelAgentWeb.csproj`，在 `<ItemGroup>` 中添加：

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
```

- [ ] **Step 2: 安装包**

Run: `dotnet restore Web/NovelAgentWeb/NovelAgentWeb.csproj`
Expected: 成功下载 EntityFrameworkCore.Sqlite 包

- [ ] **Step 3: 创建 User 实体**

Create `Web/NovelAgentWeb/Data/Entities/User.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "author"; // "admin" | "author"
    public int StorageQuotaMb { get; set; } = 5120;
    public int ApiCallQuota { get; set; } = 10000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public UserSettings? Settings { get; set; }
    public List<NovelProject> Projects { get; set; } = new();
    public List<Material> Materials { get; set; } = new();
    public List<AgentMemory> Memories { get; set; } = new();
}
```

- [ ] **Step 4: 创建 UserSettings 实体**

Create `Web/NovelAgentWeb/Data/Entities/UserSettings.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class UserSettings
{
    public string UserId { get; set; } = string.Empty;
    
    // LLM 配置
    public string? LlmProvider { get; set; }
    public string? LlmApiKeyEncrypted { get; set; }
    public string? LlmBaseUrl { get; set; }
    public string? LlmModel { get; set; }
    public double LlmTemperature { get; set; } = 0.7;
    public int LlmMaxTokens { get; set; } = 4096;
    
    // Embedding 配置
    public string EmbeddingProvider { get; set; } = "local";
    public string EmbeddingModel { get; set; } = "bge-small-zh-v1.5";
    
    // Agent 配置
    public string AgentDefaultRisk { get; set; } = "Medium";
    public bool AgentAutoContinue { get; set; } = true;
    public int AgentMaxAutoSteps { get; set; } = 12;
    
    // 创作默认值
    public string DefaultGenre { get; set; } = "玄幻";
    public int DefaultChapterWordCount { get; set; } = 3000;
    
    // 界面偏好
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh-CN";
    
    // Navigation property
    public User User { get; set; } = null!;
}
```

- [ ] **Step 5: 创建 DbContext**

Create `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data;

public class NovelAgentDbContext : DbContext
{
    public NovelAgentDbContext(DbContextOptions<NovelAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User 配置
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.Username).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired();
        });

        // UserSettings 配置
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.UserId);
            
            entity.HasOne(e => e.User)
                .WithOne(e => e.Settings)
                .HasForeignKey<UserSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

- [ ] **Step 6: 配置连接字符串**

Modify/Create `Web/NovelAgentWeb/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "NovelAgentDb": "Data Source=App_Data/Database/novelagent.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 7: 创建数据库目录**

Run: `mkdir -p Web/NovelAgentWeb/App_Data/Database`
Expected: 目录创建成功

- [ ] **Step 8: 提交基础实体和 DbContext**

```bash
git add Web/NovelAgentWeb/Data/ Web/NovelAgentWeb/NovelAgentWeb.csproj Web/NovelAgentWeb/appsettings.json
git commit -m "feat(db): add SQLite DbContext with User and UserSettings entities"
```

## Task 2: 添加所有数据库实体

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/NovelProject.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/Volume.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/Chapter.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/Foreshadow.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/Character.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/WorldSetting.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/Material.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/KnowledgeBase.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentMemory.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentSession.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`

- [ ] **Step 1: 创建 NovelProject 实体**

Create `Web/NovelAgentWeb/Data/Entities/NovelProject.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class NovelProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public string? SubGenre { get; set; }
    public string? CoreHook { get; set; }
    public string Status { get; set; } = "draft"; // draft | writing | completed
    public int WordCount { get; set; } = 0;
    public string? CoverImageUrl { get; set; }
    public string? StorageProjectName { get; set; } // 文件系统目录名
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public List<Volume> Volumes { get; set; } = new();
    public List<Chapter> Chapters { get; set; } = new();
    public List<Foreshadow> Foreshadows { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
    public List<WorldSetting> WorldSettings { get; set; } = new();
    public List<KnowledgeBase> KnowledgeBases { get; set; } = new();
    public List<Material> Materials { get; set; } = new();
}
```

- [ ] **Step 2: 创建 Chapter 实体**

Create `Web/NovelAgentWeb/Data/Entities/Chapter.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Chapter
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string? VolumeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int ChapterNumber { get; set; }
    public int WordCount { get; set; } = 0;
    public string Status { get; set; } = "draft"; // draft | writing | completed
    public string ContentPath { get; set; } = string.Empty; // 指向 Markdown 文件
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Volume? Volume { get; set; }
    public List<Foreshadow> ForeshadowsSetup { get; set; } = new();
    public List<Foreshadow> ForeshadowsPayoff { get; set; } = new();
    public List<Character> CharactersFirstAppearance { get; set; } = new();
}
```

- [ ] **Step 3: 创建 Foreshadow 实体（关键：外键约束）**

Create `Web/NovelAgentWeb/Data/Entities/Foreshadow.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Foreshadow
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Status { get; set; } = "planned"; // planned | setup | developing | resolved
    public string? SetupChapterId { get; set; }
    public string? PayoffChapterId { get; set; }
    public int Importance { get; set; } = 0;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Chapter? SetupChapter { get; set; }
    public Chapter? PayoffChapter { get; set; }
}
```

- [ ] **Step 4: 创建其余实体**

Create `Web/NovelAgentWeb/Data/Entities/Volume.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Volume
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int VolumeNumber { get; set; }
    public string? Summary { get; set; }

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public List<Chapter> Chapters { get; set; } = new();
}
```

Create `Web/NovelAgentWeb/Data/Entities/Character.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; } // protagonist | antagonist | supporting | minor
    public string? Identity { get; set; }
    public string? Description { get; set; }
    public string? Personality { get; set; }
    public string? FirstAppearanceChapterId { get; set; }
    public string? DetailJsonPath { get; set; } // 复杂数据存文件

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public Chapter? FirstAppearanceChapter { get; set; }
}
```

Create `Web/NovelAgentWeb/Data/Entities/WorldSetting.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class WorldSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // location | power_system | culture | history | other
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Rules { get; set; } // JSON 格式

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
}
```

Create `Web/NovelAgentWeb/Data/Entities/Material.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Material
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; } // NULL = 全局素材
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ContentType { get; set; } // text | image | url | file
    public string? Content { get; set; } // 短文本直接存
    public string? FilePath { get; set; } // 长文本/文件存路径
    public string? Tags { get; set; } // JSON 数组
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}
```

Create `Web/NovelAgentWeb/Data/Entities/KnowledgeBase.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int UsageCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
}
```

Create `Web/NovelAgentWeb/Data/Entities/AgentMemory.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; } // NULL = author_memory（全局）
    public string MemoryType { get; set; } = string.Empty; // author | project | execution
    public string Content { get; set; } = string.Empty; // JSON 格式
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}
```

Create `Web/NovelAgentWeb/Data/Entities/AgentSession.cs`:

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? SessionData { get; set; } // JSON 格式
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}
```

- [ ] **Step 5: 更新 DbContext 添加所有实体和关系**

Modify `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data;

public class NovelAgentDbContext : DbContext
{
    public NovelAgentDbContext(DbContextOptions<NovelAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<NovelProject> NovelProjects => Set<NovelProject>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Foreshadow> Foreshadows => Set<Foreshadow>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<WorldSetting> WorldSettings => Set<WorldSetting>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureUserSettings(modelBuilder);
        ConfigureNovelProject(modelBuilder);
        ConfigureVolume(modelBuilder);
        ConfigureChapter(modelBuilder);
        ConfigureForeshadow(modelBuilder);
        ConfigureCharacter(modelBuilder);
        ConfigureWorldSetting(modelBuilder);
        ConfigureMaterial(modelBuilder);
        ConfigureKnowledgeBase(modelBuilder);
        ConfigureAgentMemory(modelBuilder);
        ConfigureAgentSession(modelBuilder);
        
        CreateIndexes(modelBuilder);
    }

    private void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.Username).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired();
        });
    }

    private void ConfigureUserSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.UserId);
            
            entity.HasOne(e => e.User)
                .WithOne(e => e.Settings)
                .HasForeignKey<UserSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNovelProject(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NovelProject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StorageProjectName).IsUnique();
            
            entity.Property(e => e.Title).IsRequired();
            
            entity.HasOne(e => e.User)
                .WithMany(e => e.Projects)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureVolume(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Volume>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Volumes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureChapter(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.ContentPath).IsRequired();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Chapters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Volume)
                .WithMany(e => e.Chapters)
                .HasForeignKey(e => e.VolumeId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureForeshadow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Foreshadow>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Name).IsRequired();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Foreshadows)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // 关键：外键约束，删除章节自动清理伏笔引用
            entity.HasOne(e => e.SetupChapter)
                .WithMany(e => e.ForeshadowsSetup)
                .HasForeignKey(e => e.SetupChapterId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.PayoffChapter)
                .WithMany(e => e.ForeshadowsPayoff)
                .HasForeignKey(e => e.PayoffChapterId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureCharacter(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Name).IsRequired();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Characters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.FirstAppearanceChapter)
                .WithMany(e => e.CharactersFirstAppearance)
                .HasForeignKey(e => e.FirstAppearanceChapterId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureWorldSetting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Category).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.WorldSettings)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureMaterial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Title).IsRequired();
            
            entity.HasOne(e => e.User)
                .WithMany(e => e.Materials)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Materials)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureKnowledgeBase(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.EntryType).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.KnowledgeBases)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureAgentMemory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentMemory>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.MemoryType).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            
            entity.HasOne(e => e.User)
                .WithMany(e => e.Memories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureAgentSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void CreateIndexes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chapter>()
            .HasIndex(e => e.ProjectId);
        
        modelBuilder.Entity<Chapter>()
            .HasIndex(e => e.VolumeId);
        
        modelBuilder.Entity<Foreshadow>()
            .HasIndex(e => e.Status);
        
        modelBuilder.Entity<Foreshadow>()
            .HasIndex(e => e.ProjectId);
        
        modelBuilder.Entity<Character>()
            .HasIndex(e => e.ProjectId);
        
        modelBuilder.Entity<Material>()
            .HasIndex(e => e.UserId);
        
        modelBuilder.Entity<Material>()
            .HasIndex(e => e.ProjectId);
        
        modelBuilder.Entity<AgentMemory>()
            .HasIndex(e => new { e.UserId, e.ProjectId });
    }
}
```

- [ ] **Step 6: 提交所有实体**

```bash
git add Web/NovelAgentWeb/Data/Entities/ Web/NovelAgentWeb/Data/NovelAgentDbContext.cs
git commit -m "feat(db): add all database entities with foreign key constraints"
```

// __CONTINUE_HERE__