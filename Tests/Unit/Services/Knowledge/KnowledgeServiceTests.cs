using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public class KnowledgeServiceTests
{
    private const string UserSearchPrefix = "knowledge:search:user-1:";
    private const string SearchPrefix = "knowledge:search:user-1:project-1";
    private const string InventoryKey = "knowledge:inventory:user-1:project-1";

    [Fact]
    public async Task SearchKnowledgeAsync_FallsBackToDatabaseRowsWhenVectorSearchIsEmpty()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-1",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "ReaderPromise",
                Title = "胜利代价原则",
                Content = "主角每次胜利都必须付出清晰代价，避免无成本升级。",
                Tags = """["代价"]""",
                Weight = 8,
                SourceType = "extracted",
                SourceUploadTaskId = "task-search-1",
                ChunkIndex = 2,
                ExtractionContext = "原文中的胜利代价规则",
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-2",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "TropePattern",
                Title = "无关条目",
                Content = "轻松日常桥段。",
                Weight = 5,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        var service = CreateServiceWithUsage(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "主角胜利的代价",
            TopK = 5
        });

        var hit = Assert.Single(results);
        Assert.Equal("knowledge-1", hit.Id);
        Assert.Equal("ReaderPromise", hit.EntryType);
        Assert.Equal("胜利代价原则", hit.Title);
        Assert.Contains("清晰代价", hit.Content);
        Assert.Equal("extracted", hit.SourceType);
        Assert.Equal("task-search-1", hit.SourceUploadTaskId);
        Assert.Equal(2, hit.ChunkIndex);
        Assert.Equal("原文中的胜利代价规则", hit.ExtractionContext);
        Assert.True(hit.Score > 0);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_RespectsEntryTypeWhenUsingDatabaseFallback()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateServiceWithUsage(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            EntryType = "TropePattern",
            TopK = 5
        });

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_NormalizesLegacyEntryTypeAliases()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-legacy-hard-fact",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "hard_fact",
            Title = "银蓝邮徽硬事实",
            Content = "银蓝邮徽只能识别旧邮路，不能直接攻击怪物。",
            Weight = 9,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "银蓝邮徽不能攻击",
            EntryType = "HardFact",
            TopK = 5
        });

        var hit = Assert.Single(results);
        Assert.Equal("knowledge-legacy-hard-fact", hit.Id);
        Assert.Equal("HardFact", hit.EntryType);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_SearchesUserKnowledgeAcrossProjectsButKeepsUsagePerProject()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-1",
            Title = "另一个项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-1",
            Status = "referenced",
            UsageCount = 2,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastUsedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-2",
            Query = "胜利代价",
            TopK = 5
        });

        var hit = Assert.Single(results);
        Assert.Equal("knowledge-1", hit.Id);
        Assert.Equal("none", hit.ProjectUsageStatus);
        Assert.Equal(0, hit.ProjectUsageCount);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_PrioritizesExactHardFactOverSemanticStyleHit()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "hard-fact",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "能力与道具硬约束",
                Content = "【沈砚】银蓝邮徽只能辨认被篡改的邮路，不能攻击，不能治愈。【第九枚空邮票】不可焚毁、不可撕碎。",
                Weight = 5,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new KnowledgeBase
            {
                Id = "style-hit",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "StyleExample",
                Title = "简洁指令式句法",
                Content = "文本采用简洁、有力、充满指令感的叙述风格。",
                Weight = 10,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var vectorStore = new RecordingVectorStore();
        vectorStore.SearchResults.Add(new SearchResult
        {
            Id = "style-vector",
            SourceId = "style-hit",
            SourceType = "knowledge",
            Content = "文本采用简洁、有力、充满指令感的叙述风格。",
            Score = 0.99f
        });
        var service = CreateService(db, "user-1", vectorStore);

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "沈砚 银蓝邮徽 第九枚空邮票",
            TopK = 1
        });

        var hit = Assert.Single(results);
        Assert.Equal("hard-fact", hit.Id);
        Assert.Equal("HardFact", hit.EntryType);
    }

    [Fact]
    public async Task ListKnowledgeAsync_IncludesProjectUsageFields()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var lastUsedAt = DateTime.UtcNow.AddMinutes(-5);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-1",
            Status = "referenced",
            UsageCount = 3,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastUsedAt = lastUsedAt
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var response = Assert.Single(await service.ListKnowledgeAsync("project-1"));

        Assert.Equal("referenced", response.ProjectUsageStatus);
        Assert.Equal(3, response.ProjectUsageCount);
        Assert.Equal(lastUsedAt, response.ProjectLastUsedAt);
    }

    [Fact]
    public async Task ListKnowledgeAsync_WithoutProjectId_ReturnsUserKnowledgeInventory()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            new User
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            },
            new User
            {
                Id = "user-2",
                Username = "other",
                Email = "other@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-visible",
                UserId = "user-1",
                EntryType = "ReaderPromise",
                Title = "读者承诺",
                Content = "每章都要有明确的推进和反馈。",
                Weight = 8,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-archived",
                UserId = "user-1",
                EntryType = "ReaderPromise",
                Title = "已归档知识",
                Content = "不应该出现在知识库工作台。",
                IsArchived = true,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-other-user",
                UserId = "user-2",
                EntryType = "ReaderPromise",
                Title = "其他用户知识",
                Content = "不应该跨用户泄露。",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var response = Assert.Single(await service.ListKnowledgeAsync(""));

        Assert.Equal("knowledge-visible", response.Id);
        Assert.Null(response.UsageProjectId);
        Assert.Equal("none", response.ProjectUsageStatus);
        Assert.Equal(0, response.ProjectUsageCount);
    }

    [Fact]
    public async Task ListKnowledgeDirectoriesAsync_MergesLegacyEntryTypeAliasesIntoSystemDirectories()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-canonical-hard-fact",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "规范硬事实",
                Content = "邮徽只能识路。",
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-legacy-hard-fact",
                UserId = "user-1",
                EntryType = "hard_fact",
                Title = "旧硬事实",
                Content = "邮徽不能攻击。",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var directories = await service.ListKnowledgeDirectoriesAsync();

        var hardFact = Assert.Single(directories.Where(directory => directory.Key == "HardFact"));
        Assert.Equal(2, hardFact.EntryCount);
        Assert.DoesNotContain(directories, directory => directory.Key == "hard_fact");
    }

    [Fact]
    public async Task ListKnowledgeAsync_IncludesConstraintEvidenceFromFactSnapshots()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "HardFact",
            Title = "银蓝邮徽能力边界",
            Content = "银蓝邮徽只能识路，不能攻击。",
            Weight = 9,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            VersionNumber = 2,
            Source = "chapter_commit",
            SnapshotJson = """
            {
              "knowledgeConstraintEvidence": [
                {
                  "knowledgeId": "knowledge-1",
                  "title": "银蓝邮徽能力边界",
                  "entryType": "HardFact",
                  "subject": "银蓝邮徽",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter",
                  "evidenceStatus": "satisfied",
                  "gateStatus": "validated",
                  "allowedTerms": ["识路"],
                  "forbiddenTerms": ["攻击"]
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var response = Assert.Single(await service.ListKnowledgeAsync("project-1"));

        var evidence = Assert.Single(response.ConstraintEvidence);
        Assert.Equal("knowledge-1", evidence.KnowledgeId);
        Assert.Equal("银蓝邮徽能力边界", evidence.Title);
        Assert.Equal("测试项目", evidence.ProjectTitle);
        Assert.Equal("project-1", evidence.ProjectId);
        Assert.Equal("chapter-001", evidence.ChapterId);
        Assert.Equal("HardConstraint", evidence.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", evidence.PackagePolicy);
        Assert.Equal("satisfied", evidence.EvidenceStatus);
        Assert.Equal("validated", evidence.GateStatus);
        Assert.Equal("fact-1", evidence.FactSnapshotId);
        Assert.Equal(2, evidence.FactSnapshotVersion);
        Assert.Contains("识路", evidence.AllowedTerms);
        Assert.Contains("攻击", evidence.ForbiddenTerms);
    }

    [Fact]
    public async Task ListKnowledgeAsync_DoesNotParseNonJsonCommaSeparatedTags()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
                Id = "knowledge-non-json-tags",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
                Title = "非 JSON 标签",
                Content = "这条知识故意使用非 JSON 的逗号分隔标签。",
            Tags = "代价,硬事实",
            Weight = 5,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var response = Assert.Single(await service.ListKnowledgeAsync("project-1"));

        Assert.Empty(response.Tags);
    }

    [Fact]
    public async Task CreateKnowledgeAsync_EnqueuesKnowledgeIndexOutbox()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateService(db, "user-1");

        var response = await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。"
        });

        var saved = await db.KnowledgeBases.FindAsync(response.Id);
        Assert.NotNull(saved);
        Assert.Null(saved.VectorId);
        Assert.Null(response.VectorId);
        var outbox = await db.OutboxEvents.SingleAsync(e => e.AggregateId == response.Id);
        Assert.Equal("index_knowledge_content", outbox.EventType);
        Assert.Equal("knowledge", outbox.AggregateType);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("pending", outbox.Status);
    }

    [Fact]
    public async Task CreateKnowledgeAsync_IdempotentRetryRepairsMissingIndexOutbox()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-existing",
            UserId = "user-1",
            SourceProjectId = "project-1",
            IdempotencyKey = "knowledge-service-key-001",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");

        var response = await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            IdempotencyKey = "knowledge-service-key-001"
        });

        Assert.Equal("knowledge-existing", response.Id);
        Assert.Single(await db.KnowledgeBases.ToListAsync());
        var outbox = await db.OutboxEvents.SingleAsync();
        Assert.Equal("index_knowledge_content", outbox.EventType);
        Assert.Equal("knowledge-existing", outbox.AggregateId);
    }

    [Fact]
    public async Task CreateKnowledgeAsync_DoesNotAutomaticallyBindKnowledgeToProjectUsage()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        var response = await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "全局素材原则",
            Content = "这个知识可以被多个项目显式使用。"
        });

        Assert.NotNull(await db.KnowledgeBases.FindAsync(response.Id));
        Assert.Empty(await db.ProjectKnowledgeUsages.ToListAsync());
        Assert.Equal("none", response.ProjectUsageStatus);
    }

    [Fact]
    public async Task KnowledgeDirectories_CanExistWithoutEntries()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        await service.CreateKnowledgeDirectoryAsync(new CreateKnowledgeDirectoryRequest
        {
            Name = "人物设定"
        });

        var directories = await service.ListKnowledgeDirectoriesAsync();

        var directory = Assert.Single(directories.Where(item => item.Key == "人物设定"));
        Assert.Equal("人物设定", directory.Name);
        Assert.False(directory.IsSystem);
        Assert.Equal(0, directory.EntryCount);
    }

    [Fact]
    public async Task CreateKnowledgeDirectoryAsync_WithSameIdempotencyKey_ReturnsExistingDirectory()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");
        var request = new CreateKnowledgeDirectoryRequest
        {
            Name = "人物设定",
            IdempotencyKey = "directory-key-001"
        };

        var first = await service.CreateKnowledgeDirectoryAsync(request);
        var second = await service.CreateKnowledgeDirectoryAsync(request);

        Assert.Equal(first.Key, second.Key);
        var directory = await db.KnowledgeDirectories.SingleAsync();
        Assert.Equal("directory-key-001", directory.IdempotencyKey);
    }

    [Fact]
    public async Task CreateKnowledgeDirectoryAsync_RejectsSystemDirectoryReservedKey()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateKnowledgeDirectoryAsync(new CreateKnowledgeDirectoryRequest
            {
                Name = "AntiTropeStrategy"
            }));

        Assert.Contains("System directory already exists", ex.Message);
        Assert.Empty(await db.KnowledgeDirectories.ToListAsync());
    }

    [Fact]
    public async Task DeleteKnowledgeDirectoryAsync_MovesEntriesToUncategorized()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeDirectories.Add(new KnowledgeDirectory
        {
            Id = "directory-1",
            UserId = "user-1",
            Key = "人物设定",
            Name = "人物设定",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "人物设定",
            Title = "主角档案",
            Content = "林澈是雾灯修补工。",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        await service.DeleteKnowledgeDirectoryAsync("人物设定");

        Assert.Empty(await db.KnowledgeDirectories.ToListAsync());
        var entry = await db.KnowledgeBases.SingleAsync();
        Assert.Equal("Uncategorized", entry.EntryType);
    }

    [Fact]
    public async Task CreateExtractedKnowledgeAsync_DoesNotAutomaticallyBindKnowledgeToProjectUsage()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        var response = await service.CreateExtractedKnowledgeAsync(new CreateExtractedKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "上传提取原则",
            Content = "上传知识进入用户级知识库，项目使用需要显式绑定。",
            SourceUploadTaskId = "task-1"
        });

        var saved = await db.KnowledgeBases.FindAsync(response.Id);
        Assert.NotNull(saved);
        Assert.Equal("task-1", saved!.SourceUploadTaskId);
        Assert.Equal("task-1", response.SourceUploadTaskId);
        Assert.Empty(await db.ProjectKnowledgeUsages.ToListAsync());
        Assert.Equal("none", response.ProjectUsageStatus);
    }

    [Fact]
    public async Task UpdateKnowledgeAsync_EnqueuesKnowledgeIndexOutbox()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "旧标题",
            Content = "旧内容",
            VectorId = "11111111-1111-1111-1111-111111111111",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");

        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest
        {
            Title = "新标题",
            Content = "新内容"
        });

        var outbox = await db.OutboxEvents.SingleAsync(e => e.AggregateId == "knowledge-1");
        Assert.Equal("index_knowledge_content", outbox.EventType);
        Assert.Equal("knowledge", outbox.AggregateType);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("pending", outbox.Status);
    }

    [Fact]
    public async Task UpdateKnowledgeAsync_CanArchiveAndHideKnowledgeEntry()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-archived",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "ReaderPromise",
                Title = "隐藏火种原则",
                Content = "隐藏火种只用于验证归档后不会出现在知识检索里。",
                Weight = 8,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new KnowledgeBase
            {
                Id = "knowledge-visible",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "ReaderPromise",
                Title = "可见月痕原则",
                Content = "可见月痕用于验证归档不会影响普通知识条目。",
                Weight = 7,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var archived = await service.UpdateKnowledgeAsync("knowledge-archived", new UpdateKnowledgeRequest
        {
            IsArchived = true
        });

        Assert.True(archived.IsArchived);
        var listed = await service.ListKnowledgeAsync("project-1");
        var visible = Assert.Single(listed);
        Assert.Equal("knowledge-visible", visible.Id);

        var readerPromiseDirectory = Assert.Single(
            (await service.ListKnowledgeDirectoriesAsync())
                .Where(directory => directory.Key == "ReaderPromise"));
        Assert.Equal(1, readerPromiseDirectory.EntryCount);

        var hiddenSearch = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "隐藏火种",
            TopK = 5
        });
        Assert.Empty(hiddenSearch);

        var visibleSearch = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "可见月痕",
            TopK = 5
        });
        Assert.Equal("knowledge-visible", Assert.Single(visibleSearch).Id);
    }

    [Fact]
    public async Task DeleteKnowledgeAsync_EnqueuesKnowledgeVectorCleanup()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");

        await service.DeleteKnowledgeAsync("knowledge-1");

        Assert.Null(await db.KnowledgeBases.FindAsync("knowledge-1"));
        var delete = await db.OutboxEvents.SingleAsync(e => e.EventType == "delete_knowledge_content");
        Assert.Equal("knowledge", delete.AggregateType);
        Assert.Equal("knowledge-1", delete.AggregateId);
        Assert.Equal("project-1", delete.ProjectId);
        Assert.Equal("pending", delete.Status);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_CachesResultsByUserProjectQueryAndTopK()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var redis = new PrefixAwareDistributedCache();
        var memory = new PrefixAwareMemoryCache();
        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, "user-1", vectorStore, redis, memory);

        var first = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });
        db.KnowledgeBases.RemoveRange(db.KnowledgeBases);
        await db.SaveChangesAsync();

        var second = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal("knowledge-1", second[0].Id);
        Assert.Equal(1, vectorStore.SearchCount);
    }

    [Fact]
    public async Task KnowledgeMutations_InvalidatesProjectSearchCaches()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "旧标题",
            Content = "旧内容",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        redis.Setup(x => x.RemoveByPrefixAsync(SearchPrefix, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(db, "user-1", new RecordingVectorStore(), redis.Object, memory.Object);

        await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "新知识",
            Content = "新内容"
        });
        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest { Title = "新标题" });
        await service.IncrementUsageAsync("knowledge-1", "project-1");
        await service.DeleteKnowledgeAsync("knowledge-1");

        memory.Verify(x => x.RemoveByPrefix(UserSearchPrefix), Times.Exactly(3));
        memory.Verify(x => x.RemoveByPrefix(SearchPrefix), Times.Exactly(3));
        memory.Verify(x => x.Remove(InventoryKey), Times.Exactly(3));
        redis.Verify(x => x.RemoveByPrefixAsync(UserSearchPrefix, It.IsAny<CancellationToken>()), Times.Exactly(3));
        redis.Verify(x => x.RemoveByPrefixAsync(SearchPrefix, It.IsAny<CancellationToken>()), Times.Exactly(3));
        redis.Verify(x => x.RemoveAsync(InventoryKey, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task IncrementUsageAsync_WithSameIdempotencyKeyDoesNotDoubleCountKnowledgeOrProjectUsage()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        await service.IncrementUsageAsync(
            "knowledge-1",
            "project-1",
            "session-1",
            "run-1",
            "usage-key-001");
        await service.IncrementUsageAsync(
            "knowledge-1",
            "project-1",
            "session-1",
            "run-1",
            "usage-key-001");

        var knowledge = await db.KnowledgeBases.SingleAsync(item => item.Id == "knowledge-1");
        var usage = await db.ProjectKnowledgeUsages.SingleAsync(item => item.KnowledgeId == "knowledge-1");
        Assert.Equal(1, knowledge.UsageCount);
        Assert.Equal(1, usage.UsageCount);
        Assert.Contains("usage-key-001", usage.UsageIdempotencyKeysJson);
    }

    [Fact]
    public async Task KnowledgeMutations_InvalidatesRealMemorySearchCache()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var memory = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);
        var service = CreateService(db, "user-1", new RecordingVectorStore(), Mock.Of<IDistributedCacheService>(), memory);

        var first = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });
        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest
        {
            Title = "新标题",
            Content = "完全不同的内容"
        });

        var second = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task KnowledgeSearchPrefix_RemovesActualSearchCacheKeys()
    {
        const string prefix = "knowledge:search:user-1:project-1";
        const string key = "knowledge:search:user-1:project-1:*:5:abc";
        var memory = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);
        memory.Set(key, new List<KnowledgeSearchResult>(), TimeSpan.FromMinutes(5));

        memory.RemoveByPrefix(prefix);

        Assert.Null(memory.Get<List<KnowledgeSearchResult>>(key));
        Assert.True(RedisCacheService.IsPrefixMatch(key, prefix));
        Assert.True(RedisCacheService.IsPrefixMatch(key, "knowledge:search:user-1:"));
        Assert.False(RedisCacheService.IsPrefixMatch(key, "knowledge:search:user-10:"));
        var deleted = new List<RedisKey>();
        var deletedCount = await RedisCacheService.DeletePrefixMatchesInBatchesAsync(
            new RedisKey[] { key, "knowledge:search:user-1:project-2:*:5:abc" },
            prefix,
            RedisCacheService.PrefixDeleteBatchSize,
            batch =>
            {
                deleted.AddRange(batch);
                return Task.FromResult((long)batch.Count);
            });
        Assert.Equal(1, deletedCount);
        Assert.Equal(key, deleted.Single().ToString());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedUserProject(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static KnowledgeService CreateService(
        NovelAgentDbContext db,
        string userId,
        IVectorStore? vectorStore = null,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null)
    {
        vectorStore ??= new RecordingVectorStore();
        redisCache ??= Mock.Of<IDistributedCacheService>();
        memoryCache ??= Mock.Of<IMemoryCacheService>();
        var embedding = new FixedEmbeddingService();
        var searchService = new SemanticSearchService(
            vectorStore,
            embedding,
            NullLogger<SemanticSearchService>.Instance);

        return new KnowledgeService(
            db,
            new FixedCurrentUserService(userId),
            searchService,
            NullLogger<KnowledgeService>.Instance,
            new ProductionTruthStore(db),
            null,
            redisCache,
            memoryCache);
    }

    private static KnowledgeService CreateServiceWithUsage(NovelAgentDbContext db, string userId)
    {
        var usage = new ProjectKnowledgeUsageService(
            db,
            Mock.Of<IAgentMemoryEventService>(),
            null,
            NullLogger<ProjectKnowledgeUsageService>.Instance,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());

        var vectorStore = new RecordingVectorStore();
        var embedding = new FixedEmbeddingService();
        var searchService = new SemanticSearchService(
            vectorStore,
            embedding,
            NullLogger<SemanticSearchService>.Instance);

        return new KnowledgeService(
            db,
            new FixedCurrentUserService(userId),
            searchService,
            NullLogger<KnowledgeService>.Instance,
            new ProductionTruthStore(db),
            usage,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly string _userId;

        public FixedCurrentUserService(string userId)
        {
            _userId = userId;
        }

        public string GetUserId() => _userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => _userId;
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f });

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorData> Upserted { get; } = new();
        public List<Dictionary<string, object>> DeletedFilters { get; } = new();
        public List<SearchResult> SearchResults { get; } = new();
        public int SearchCount { get; private set; }

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }

        public Task<List<SearchResult>> SearchSimilarAsync(
            string userId,
            float[] queryVector,
            int topK = 10,
            Dictionary<string, object>? filters = null,
            CancellationToken ct = default)
        {
            SearchCount++;
            return Task.FromResult(SearchResults.Take(topK).ToList());
        }

        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
        {
            DeletedFilters.Add(new Dictionary<string, object>(filters));
            return Task.CompletedTask;
        }
    }

    private sealed class PrefixAwareDistributedCache : IDistributedCacheService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.ContainsKey(key));
    }

    private sealed class PrefixAwareMemoryCache : IMemoryCacheService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public T? Get<T>(string key) =>
            _values.TryGetValue(key, out var value) ? (T)value : default;

        public void Set<T>(string key, T value, TimeSpan expiration)
        {
            if (value != null)
                _values[key] = value;
        }

        public void Remove(string key) => _values.Remove(key);

        public void RemoveByPrefix(string keyPrefix)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
        }
    }
}
