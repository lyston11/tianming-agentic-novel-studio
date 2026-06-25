using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Workspace;
using Xunit;

namespace Tests.Unit.Services.Workspace;

public sealed class WorkspaceStateQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_AdminReadsGlobalWorkspaceWithoutBindingProject()
    {
        await using var db = CreateDb();
        SeedWorkspace(db);
        IWorkspaceStateQueryService service = new WorkspaceStateQueryService(db);

        var state = await service.QueryAsync(new WorkspaceStateQueryRequest(
            UserId: "admin-1",
            SessionId: "session-1",
            ActiveProjectId: "",
            Phase: "conversation",
            AuthorDisplayName: "lyston",
            StyleLikeCount: 2,
            StyleDislikeCount: 1,
            GenreHabitCount: 3));

        Assert.Equal(2, state.ProjectTotalCount);
        Assert.Equal(2, state.ProjectPreviewCount);
        Assert.Contains(state.VisibleProjects, project =>
            project.Id == "project-user-1" &&
            project.OwnerUserId == "user-1" &&
            project.OwnerUsername == "author" &&
            project.IsOwnedByCurrentUser == false &&
            project.CommittedChapterCount == 1);
        Assert.Contains(state.VisibleProjects, project =>
            project.Id == "project-admin" &&
            project.IsOwnedByCurrentUser);
        Assert.Equal(2, state.KnowledgeBase.TotalCount);
        var hardFactCount = Assert.Single(state.KnowledgeBase.CountsByType.Where(count => count.EntryType == "HardFact"));
        Assert.Equal(1, hardFactCount.Count);
        Assert.Equal(2, state.Workflow.ActiveRunCount);
        Assert.Equal("lyston", state.AuthorProfile.DisplayName);
        Assert.False(state.CurrentSession.HasActiveProject);
        Assert.Contains(state.Notes, note => note.Contains("admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryAsync_NonAdminReadsOnlyOwnWorkspace()
    {
        await using var db = CreateDb();
        SeedWorkspace(db);
        IWorkspaceStateQueryService service = new WorkspaceStateQueryService(db);

        var state = await service.QueryAsync(new WorkspaceStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-2",
            ActiveProjectId: "project-user-1",
            Phase: "writing",
            AuthorDisplayName: "",
            StyleLikeCount: 0,
            StyleDislikeCount: 0,
            GenreHabitCount: 0));

        var project = Assert.Single(state.VisibleProjects);
        Assert.Equal("project-user-1", project.Id);
        Assert.True(project.IsOwnedByCurrentUser);
        Assert.Equal(1, state.ProjectTotalCount);
        Assert.Equal(1, state.KnowledgeBase.TotalCount);
        var usedKnowledge = Assert.Single(state.KnowledgeBase.RecentlyUsedBindings);
        Assert.Equal("knowledge-1", usedKnowledge.KnowledgeId);
        Assert.Equal("邮徽硬事实", usedKnowledge.Title);
        Assert.Equal("project-user-1", usedKnowledge.ProjectId);
        Assert.Equal("天命旧书", usedKnowledge.ProjectTitle);
        Assert.Equal("HardConstraint", usedKnowledge.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", usedKnowledge.PackagePolicy);
        Assert.Contains("chapter-1", usedKnowledge.UsedByChapters);
        var evidence = Assert.Single(state.KnowledgeBase.RecentConstraintEvidence);
        Assert.Equal("knowledge-1", evidence.KnowledgeId);
        Assert.Equal("邮徽硬事实", evidence.Title);
        Assert.Equal("project-user-1", evidence.ProjectId);
        Assert.Equal("chapter-1", evidence.ChapterId);
        Assert.Equal("satisfied", evidence.EvidenceStatus);
        Assert.Equal("validated", evidence.GateStatus);
        Assert.Equal("HardConstraint", evidence.ConstraintLevel);
        var conflict = Assert.Single(state.KnowledgeBase.RecentConflictReports);
        Assert.Equal("conflict-user-open", conflict.ConflictId);
        Assert.Equal("project-user-1", conflict.ProjectId);
        Assert.Equal("open", conflict.Status);
        Assert.Equal("Hard", conflict.Severity);
        Assert.True(conflict.RequiresUserDecision);
        Assert.Contains("邮徽", conflict.Explanation);
        Assert.Equal(1, state.Workflow.ActiveRunCount);
        Assert.True(state.CurrentSession.HasActiveProject);
        Assert.DoesNotContain(state.VisibleProjects, p => p.Id == "project-admin");
        Assert.Contains(state.Notes, note => note.Contains("当前用户拥有", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryAsync_ReturnsRecentMemoryAuditsForCurrentUserAndSession()
    {
        await using var db = CreateDb();
        SeedWorkspace(db);
        db.AgentMemoryReads.Add(new AgentMemoryRead
        {
            Id = "memory-read-user",
            UserId = "user-1",
            ProjectId = "project-user-1",
            SessionId = "session-2",
            MemoryScope = "author",
            MemoryKeysJson = """["author.display_name","author.style_likes"]""",
            SourceType = "memory_repository",
            Consumer = "GetAuthorMemoryAsync",
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = "memory-promotion-user",
            UserId = "user-1",
            ProjectId = "project-user-1",
            SessionId = "session-2",
            SourceScope = "session",
            TargetScope = "project",
            SourceMemoryKey = "session.short_term_preferences",
            TargetMemoryKey = "project.constraints",
            PromotionReason = "preference_sedimentation_threshold",
            PayloadJson = """{"preference":"章节要打怪升级","threshold":3}""",
            CreatedAt = DateTime.UtcNow.AddSeconds(2)
        });
        db.AgentMemoryReads.Add(new AgentMemoryRead
        {
            Id = "memory-read-other-user",
            UserId = "admin-1",
            ProjectId = "project-admin",
            SessionId = "session-admin",
            MemoryScope = "project",
            MemoryKeysJson = """["project.constraints"]""",
            SourceType = "memory_repository",
            Consumer = "GetProjectMemoryAsync",
            CreatedAt = DateTime.UtcNow.AddSeconds(3)
        });
        await db.SaveChangesAsync();
        IWorkspaceStateQueryService service = new WorkspaceStateQueryService(db);

        var state = await service.QueryAsync(new WorkspaceStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-2",
            ActiveProjectId: "project-user-1",
            Phase: "writing",
            AuthorDisplayName: "lyston",
            StyleLikeCount: 1,
            StyleDislikeCount: 0,
            GenreHabitCount: 0));

        var read = Assert.Single(state.Memory.RecentReads);
        Assert.Equal("memory-read-user", read.Id);
        Assert.Equal("author", read.MemoryScope);
        Assert.Contains("author.display_name", read.MemoryKeys);

        var promotion = Assert.Single(state.Memory.RecentPromotions);
        Assert.Equal("memory-promotion-user", promotion.Id);
        Assert.Equal("session.short_term_preferences", promotion.SourceMemoryKey);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.DoesNotContain(state.Memory.RecentReads, read => read.Id == "memory-read-other-user");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedWorkspace(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            new User
            {
                Id = "admin-1",
                Username = "admin",
                Email = "admin@example.com",
                PasswordHash = "hash",
                Role = "admin"
            },
            new User
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
        db.NovelProjects.AddRange(
            new NovelProject
            {
                Id = "project-user-1",
                UserId = "user-1",
                Title = "天命旧书",
                Genre = "玄幻",
                Status = "writing",
                WordCount = 3200,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new NovelProject
            {
                Id = "project-admin",
                UserId = "admin-1",
                Title = "管理员样书",
                Genre = "科幻",
                Status = "draft",
                WordCount = 1200,
                UpdatedAt = DateTime.UtcNow
            });
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-user-1",
            Title = "第一卷",
            VolumeNumber = 1
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "chapter-1",
                ProjectId = "project-user-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed"
            },
            new Chapter
            {
                Id = "chapter-2",
                ProjectId = "project-admin",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "draft"
            });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-1",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "邮徽硬事实",
                Content = "邮徽不能攻击"
            },
            new KnowledgeBase
            {
                Id = "knowledge-2",
                UserId = "admin-1",
                EntryType = "Style",
                Title = "节奏偏好",
                Content = "快节奏"
            });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-knowledge-1",
            UserId = "user-1",
            ProjectId = "project-user-1",
            KnowledgeId = "knowledge-1",
            Status = "referenced",
            UsageCount = 3,
            ConstraintLevel = "HardConstraint",
            PackagePolicy = "DefaultEveryChapter",
            Role = "ItemRule",
            Scope = "ProjectWide",
            Priority = 80,
            UsedByChaptersJson = "[\"chapter-1\"]",
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastUsedAt = DateTime.UtcNow
        });
        db.KnowledgeConflictReports.AddRange(
            new KnowledgeConflictReport
            {
                Id = "conflict-user-open",
                UserId = "user-1",
                ProjectId = "project-user-1",
                KnowledgeId = "knowledge-1",
                ConflictingKnowledgeIdsJson = "[\"knowledge-2\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "邮徽不能攻击与蓝焰攻击设定冲突。",
                RecommendedAction = "询问用户保留哪条设定。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4)
            },
            new KnowledgeConflictReport
            {
                Id = "conflict-admin-open",
                UserId = "admin-1",
                ProjectId = "project-admin",
                KnowledgeId = "knowledge-2",
                ConflictingKnowledgeIdsJson = "[]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "管理员项目冲突。",
                RecommendedAction = "不应出现在普通用户工作台。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3)
            });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "snapshot-knowledge-1",
            UserId = "user-1",
            ProjectId = "project-user-1",
            ChapterId = "chapter-1",
            VersionNumber = 1,
            Source = "chapter_commit",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            SnapshotJson = """
            {
              "chapterId": "chapter-1",
              "knowledgeConstraintEvidence": [
                {
                  "knowledgeId": "knowledge-1",
                  "title": "邮徽硬事实",
                  "entryType": "HardFact",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter",
                  "gateStatus": "validated",
                  "evidenceStatus": "satisfied"
                }
              ]
            }
            """
        });
        db.AgentRuns.AddRange(
            new AgentRun
            {
                Id = "run-user-1",
                UserId = "user-1",
                ProjectId = "project-user-1",
                RunType = "chapter_generation",
                Status = "running",
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new AgentRun
            {
                Id = "run-admin",
                UserId = "admin-1",
                ProjectId = "project-admin",
                RunType = "planning",
                Status = "pending",
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.SaveChanges();
    }
}
