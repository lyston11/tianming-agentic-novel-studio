using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Creative;
using Xunit;

namespace Tests.Unit.Services.Creative;

public sealed class CreativeIntentServiceTests
{
    [Fact]
    public async Task CreateDecideAndQueryAsync_ManageProjectScopedCreativeIntents()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        db.CreativeIntents.Add(new CreativeIntent
        {
            Id = "other-project-intent",
            UserId = "user-1",
            ProjectId = "project-2",
            SessionId = "session-other",
            Source = "chat",
            RawContent = "别的项目要写宫廷权谋",
            NormalizedIntent = "别的项目要写宫廷权谋",
            TargetScope = "project",
            Status = "accepted",
            ImpactLevel = "mainline_change",
            ConflictStatus = "none",
            MetadataJson = "{}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        await db.SaveChangesAsync();
        ICreativeIntentService service = new CreativeIntentService(db);

        var created = await service.CreateAsync(new CreateCreativeIntentRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-chapter-002",
            IdempotencyKey: "",
            RawContent: "第二章别写情绪拉扯，改成怪物围攻和打怪升级。",
            NormalizedIntent: "第二章主冲突改为怪物围攻，男主通过银蓝邮徽识别逃生路线，不推进恋爱。",
            Source: "chat",
            TargetScope: "chapter",
            TargetVolumeId: "",
            TargetChapterId: "chapter-002",
            TargetCharacterName: "",
            ImpactLevel: "chapter_rewrite",
            RequiresConfirmation: false,
            ConflictStatus: "none",
            MetadataJson: "{\"from\":\"unit-test\"}"));

        Assert.NotNull(created);
        Assert.Equal("candidate", created!.Status);
        Assert.Equal("project-1", created.ProjectId);
        Assert.Equal("chapter-002", created.TargetChapterId);
        Assert.Equal("chapter_rewrite", created.ImpactLevel);

        var decided = await service.DecideAsync(new DecideCreativeIntentRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            IntentId: created.Id,
            Status: "accepted",
            DecisionReason: "符合用户当前章节重写目标",
            ConflictStatus: "none",
            MarkExecuted: false));

        Assert.NotNull(decided);
        Assert.Equal("accepted", decided!.Status);
        Assert.Contains("当前章节重写目标", decided.DecisionReason);
        Assert.NotNull(decided.DecidedAt);

        var query = await service.QueryAsync(new QueryCreativeIntentsRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            Status: "accepted",
            TargetChapterId: "chapter-002",
            Limit: 20));

        var item = Assert.Single(query.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal("project-1", item.ProjectId);
        Assert.Contains("怪物围攻", item.NormalizedIntent);
        Assert.DoesNotContain(query.Items, intent => intent.Id == "other-project-intent");
    }

    [Fact]
    public async Task GetAcceptedSnapshotsForPackageAsync_ReturnsOnlyAcceptedOrExecutedApplicableIntents()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        db.CreativeIntents.AddRange(
            new CreativeIntent
            {
                Id = "intent-project-mainline",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "agent_suggestion",
                RawContent = "旧邮路成为第一卷主线",
                NormalizedIntent = "旧邮路成为第一卷主线，每次开启都要付出记忆代价。",
                TargetScope = "project",
                Status = "accepted",
                ImpactLevel = "mainline_change",
                ConflictStatus = "none",
                DecisionReason = "用户确认采纳",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-9),
                DecidedAt = DateTime.UtcNow.AddMinutes(-9)
            },
            new CreativeIntent
            {
                Id = "intent-chapter-002",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "第二章改成怪物围攻",
                NormalizedIntent = "第二章主冲突改为怪物围攻，男主用银蓝邮徽识别逃生路线。",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "executed",
                ImpactLevel = "chapter_rewrite",
                ConflictStatus = "none",
                DecisionReason = "用户明确要求",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-8),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-7),
                DecidedAt = DateTime.UtcNow.AddMinutes(-7),
                ExecutedAt = DateTime.UtcNow.AddMinutes(-6)
            },
            new CreativeIntent
            {
                Id = "intent-chapter-003",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "第三章增加邮差公会",
                NormalizedIntent = "第三章再引入邮差公会。",
                TargetScope = "chapter",
                TargetChapterId = "chapter-003",
                Status = "accepted",
                ImpactLevel = "future_carry",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-6),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new CreativeIntent
            {
                Id = "intent-candidate",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "候选创意暂不采纳",
                NormalizedIntent = "候选创意暂不采纳。",
                TargetScope = "project",
                Status = "candidate",
                ImpactLevel = "minor_edit",
                ConflictStatus = "unknown",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
        await db.SaveChangesAsync();
        ICreativeIntentService service = new CreativeIntentService(db);

        var snapshots = await service.GetAcceptedSnapshotsForPackageAsync(
            "user-1",
            "project-1",
            new ChapterContextPackageSummary { ChapterId = "chapter-002" });

        Assert.Contains(snapshots, intent => intent.IntentId == "intent-project-mainline");
        Assert.Contains(snapshots, intent => intent.IntentId == "intent-chapter-002");
        Assert.DoesNotContain(snapshots, intent => intent.IntentId == "intent-chapter-003");
        Assert.DoesNotContain(snapshots, intent => intent.IntentId == "intent-candidate");
        Assert.Contains(snapshots, intent =>
            intent.IntentId == "intent-chapter-002" &&
            intent.NormalizedIntent.Contains("怪物围攻", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkPackageIntentsExecutedAsync_MarksOnlyAcceptedIntentsUsedByCommittedPackage()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        db.CreativeIntents.AddRange(
            new CreativeIntent
            {
                Id = "intent-project-mainline",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "agent_suggestion",
                RawContent = "旧邮路成为第一卷主线",
                NormalizedIntent = "旧邮路成为第一卷主线。",
                TargetScope = "project",
                Status = "accepted",
                ImpactLevel = "mainline_change",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-9),
                DecidedAt = DateTime.UtcNow.AddMinutes(-9)
            },
            new CreativeIntent
            {
                Id = "intent-chapter-002",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "第二章改成怪物围攻",
                NormalizedIntent = "第二章主冲突改为怪物围攻。",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "accepted",
                ImpactLevel = "chapter_rewrite",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-8),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-7),
                DecidedAt = DateTime.UtcNow.AddMinutes(-7)
            },
            new CreativeIntent
            {
                Id = "intent-chapter-003",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "第三章再引入邮差公会",
                NormalizedIntent = "第三章再引入邮差公会。",
                TargetScope = "chapter",
                TargetChapterId = "chapter-003",
                Status = "accepted",
                ImpactLevel = "future_carry",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-6),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new CreativeIntent
            {
                Id = "intent-candidate",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "候选创意暂不采纳",
                NormalizedIntent = "候选创意暂不采纳。",
                TargetScope = "project",
                Status = "candidate",
                ImpactLevel = "minor_edit",
                ConflictStatus = "unknown",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            },
            new CreativeIntent
            {
                Id = "intent-other-project",
                UserId = "user-1",
                ProjectId = "project-2",
                Source = "chat",
                RawContent = "别的项目创意",
                NormalizedIntent = "别的项目创意。",
                TargetScope = "project",
                Status = "accepted",
                ImpactLevel = "future_carry",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
        await db.SaveChangesAsync();
        ICreativeIntentService service = new CreativeIntentService(db);
        var package = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-002",
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-project-mainline",
                    NormalizedIntent = "旧邮路成为第一卷主线。",
                    TargetScope = "project",
                    ImpactLevel = "mainline_change"
                },
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-chapter-002",
                    NormalizedIntent = "第二章主冲突改为怪物围攻。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    ImpactLevel = "chapter_rewrite"
                }
            }
        };

        var result = await service.MarkPackageIntentsExecutedAsync(
            userId: "user-1",
            projectId: "project-1",
            package,
            decisionReason: "章节 chapter-002 已提交书城，生产包内创意已执行。");

        Assert.Equal(2, result.ExecutedCount);
        Assert.Contains(result.IntentIds, id => id == "intent-project-mainline");
        Assert.Contains(result.IntentIds, id => id == "intent-chapter-002");
        Assert.All(await db.CreativeIntents
                .Where(intent => intent.Id == "intent-project-mainline" || intent.Id == "intent-chapter-002")
                .ToListAsync(),
            intent =>
            {
                Assert.Equal("executed", intent.Status);
                Assert.NotNull(intent.ExecutedAt);
                Assert.Contains("已提交书城", intent.DecisionReason);
            });
        Assert.Equal("accepted", (await db.CreativeIntents.FindAsync("intent-chapter-003"))!.Status);
        Assert.Equal("candidate", (await db.CreativeIntents.FindAsync("intent-candidate"))!.Status);
        Assert.Equal("accepted", (await db.CreativeIntents.FindAsync("intent-other-project"))!.Status);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNullWhenProjectIsNotOwnedByUser()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        ICreativeIntentService service = new CreativeIntentService(db);

        var created = await service.CreateAsync(new CreateCreativeIntentRequest(
            UserId: "user-2",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            IdempotencyKey: "",
            RawContent: "尝试写入别人的项目",
            NormalizedIntent: "尝试写入别人的项目",
            Source: "chat",
            TargetScope: "project",
            TargetVolumeId: "",
            TargetChapterId: "",
            TargetCharacterName: "",
            ImpactLevel: "future_carry",
            RequiresConfirmation: false,
            ConflictStatus: "none",
            MetadataJson: "{}"));

        Assert.Null(created);
        Assert.Empty(db.CreativeIntents);
    }

    [Fact]
    public async Task CreateAsync_WithSameIdempotencyKey_ReturnsExistingIntent()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        ICreativeIntentService service = new CreativeIntentService(db);
        var request = new CreateCreativeIntentRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            IdempotencyKey: "creative-service-key-001",
            RawContent: "第二章改成怪物围攻。",
            NormalizedIntent: "第二章主冲突改为怪物围攻。",
            Source: "chat",
            TargetScope: "chapter",
            TargetVolumeId: "",
            TargetChapterId: "chapter-002",
            TargetCharacterName: "",
            ImpactLevel: "chapter_rewrite",
            RequiresConfirmation: false,
            ConflictStatus: "none",
            MetadataJson: "{}");

        var first = await service.CreateAsync(request);
        var second = await service.CreateAsync(request);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        var intent = await db.CreativeIntents.SingleAsync();
        Assert.Equal("creative-service-key-001", intent.IdempotencyKey);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedProjectsAsync(NovelAgentDbContext db)
    {
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
        db.NovelProjects.AddRange(
            new NovelProject
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "创意收件箱测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new NovelProject
            {
                Id = "project-2",
                UserId = "user-1",
                Title = "隔离项目",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }
}
