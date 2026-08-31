using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class ProjectKnowledgeBindingQueryServiceTests
{
    [Fact]
    public async Task QueryBindingsAsync_ReturnsProjectScopedBoundKnowledgeAndHardFacts()
    {
        await using var db = CreateDb();
        SeedProjectKnowledge(db);
        IProjectKnowledgeBindingQueryService service = new ProjectKnowledgeBindingQueryService(db);

        var result = await service.QueryBindingsAsync("user-1", "project-1");

        Assert.NotNull(result);
        Assert.Equal("project-1", result!.ProjectId);
        Assert.Equal(2, result.BindingCount);
        Assert.Equal(1, result.HardFactCount);
        Assert.Equal(1, result.StatusSummary.ImportedCount);
        Assert.Equal(1, result.StatusSummary.ReferencedCount);
        Assert.Equal(1, result.StatusSummary.ClassifiedCount);
        Assert.Equal(1, result.StatusSummary.PendingClassificationCount);
        Assert.Equal(1, result.StatusSummary.ShouldEnterGateCount);
        var hardFact = Assert.Single(result.Bindings.Where(b => b.EntryType == "HardFact"));
        Assert.Equal("hardfact-1", hardFact.KnowledgeId);
        Assert.Contains("邮徽", hardFact.Tags);
        Assert.Equal("ItemRule", hardFact.Role);
        Assert.Equal("ProjectWide", hardFact.Scope);
        Assert.Equal(80, hardFact.Priority);
        Assert.Equal("HardConstraint", hardFact.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", hardFact.PackagePolicy);
        Assert.Equal("knowledge-v3", hardFact.BoundVersion);
        Assert.Contains("chapter-001", hardFact.UsedByChapters);
        Assert.Equal("classification-hardfact-latest", hardFact.ClassificationId);
        Assert.Equal("fake-llm", hardFact.ClassificationModel);
        Assert.Equal("银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。", hardFact.ClassificationRule);
        Assert.Contains("银蓝邮徽", hardFact.TargetEntities);
        Assert.True(hardFact.ShouldEnterGate);
        Assert.True(hardFact.ShouldEnterBlueprint);
        Assert.True(hardFact.ShouldEnterFactSnapshot);
        Assert.Equal(0.91, hardFact.ClassificationConfidence, precision: 2);
        Assert.Contains(result.HardFacts, fact => fact.Contains("不能攻击", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetProjectHardFactLinesAsync_ReturnsVisibleHardFactsForProjectAndUser()
    {
        await using var db = CreateDb();
        SeedProjectKnowledge(db);
        IProjectKnowledgeBindingQueryService service = new ProjectKnowledgeBindingQueryService(db);

        var hardFacts = await service.GetProjectHardFactLinesAsync("user-1", "project-1");

        Assert.Contains(hardFacts, fact => fact.Contains("银蓝邮徽能力边界", StringComparison.Ordinal));
        Assert.DoesNotContain(hardFacts, fact => fact.Contains("已归档", StringComparison.Ordinal));
        Assert.DoesNotContain(hardFacts, fact => fact.Contains("其他用户", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetProjectHardFactLinesAsync_DoesNotInjectUnboundGlobalKnowledgeIntoProjectPackage()
    {
        await using var db = CreateDb();
        SeedProjectKnowledge(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "unbound-global-hardfact",
            UserId = "user-1",
            SourceProjectId = null,
            EntryType = "HardFact",
            Title = "未绑定全局硬事实",
            Content = "这条全局知识没有被 project-1 引用，不能自动进入章节生产包。",
            Weight = 999,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProjectKnowledgeBindingQueryService service = new ProjectKnowledgeBindingQueryService(db);

        var hardFacts = await service.GetProjectHardFactLinesAsync("user-1", "project-1");

        Assert.DoesNotContain(hardFacts, fact => fact.Contains("未绑定全局硬事实", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryBindingsAsync_ReturnsProjectScopedKnowledgeConflictReports()
    {
        await using var db = CreateDb();
        SeedProjectKnowledge(db);
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-1",
            Title = "其他项目",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeConflictReports.AddRange(
            new KnowledgeConflictReport
            {
                Id = "conflict-open",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                ConflictingKnowledgeIdsJson = "[\"style-1\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "硬事实与风格条目冲突。",
                RecommendedAction = "让用户确认。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeConflictReport
            {
                Id = "conflict-resolved",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                ConflictingKnowledgeIdsJson = "[]",
                ConflictType = "SoftOverlap",
                Severity = "Soft",
                ImpactScope = "Chapter",
                Explanation = "已解决的轻微重叠。",
                RecommendedAction = "已处理。",
                RequiresUserDecision = false,
                Status = "resolved",
                ResolutionNote = "保留硬事实。",
                ResolvedAt = DateTime.UtcNow,
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new KnowledgeConflictReport
            {
                Id = "conflict-other-project",
                UserId = "user-1",
                ProjectId = "project-2",
                KnowledgeId = "hardfact-1",
                ConflictingKnowledgeIdsJson = "[]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "其他项目冲突。",
                RecommendedAction = "不应出现。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        IProjectKnowledgeBindingQueryService service = new ProjectKnowledgeBindingQueryService(db);

        var result = await service.QueryBindingsAsync("user-1", "project-1");

        Assert.NotNull(result);
        Assert.Equal(2, result!.ConflictReports.Count);
        Assert.Equal(1, result.StatusSummary.OpenConflictCount);
        Assert.Contains(result.ConflictReports, report =>
            report.ConflictId == "conflict-open" &&
            report.Status == "open" &&
            report.RequiresUserDecision);
        Assert.Contains(result.ConflictReports, report =>
            report.ConflictId == "conflict-resolved" &&
            report.Status == "resolved" &&
            report.ResolutionNote.Contains("硬事实", StringComparison.Ordinal));
        Assert.DoesNotContain(result.ConflictReports, report => report.ConflictId == "conflict-other-project");
    }

    [Fact]
    public async Task QueryBindingsAsync_ReturnsStoryBibleCanonLedgerEvidenceForAgentTools()
    {
        await using var db = CreateDb();
        SeedProjectKnowledge(db);
        var storyBible = new StoryBibleDocument
        {
            CanonLedger =
            {
                new CanonLedgerEntry
                {
                    Id = "canon-boundary",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Canon,
                    Title = "邮徽能力边界",
                    Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
                    Rationale = "KnowledgeId=hardfact-1",
                    ConflictCheck = "clear"
                },
                new CanonLedgerEntry
                {
                    Id = "canon-conflict",
                    Type = CanonLedgerEntryType.Constraint,
                    Status = CanonLedgerEntryStatus.Conflict,
                    Title = "蓝焰攻击",
                    Content = "银蓝邮徽可以释放蓝焰攻击。",
                    Rationale = "KnowledgeId=style-1",
                    ConflictCheck = "conflict_open; ReportId=conflict-a"
                }
            }
        };
        await new ContentDocumentService(db).SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "story_bible",
            "project-1",
            "aggregate_json",
            "Story Bible",
            JsonSerializer.Serialize(storyBible),
            CancellationToken.None);
        IProjectKnowledgeBindingQueryService service = new ProjectKnowledgeBindingQueryService(db);

        var result = await service.QueryBindingsAsync("user-1", "project-1");

        Assert.NotNull(result);
        Assert.Equal(2, result!.CanonLedger.Count);
        Assert.Equal(1, result.CanonCount);
        Assert.Equal(1, result.StatusSummary.CanonLedgerCount);
        Assert.Equal(1, result.StatusSummary.ConflictCanonLedgerCount);
        Assert.Contains(result.CanonLedger, entry =>
            entry.Id == "canon-boundary" &&
            entry.Status == "Canon" &&
            entry.Content.Contains("不能攻击", StringComparison.Ordinal));
        Assert.Contains(result.CanonLedger, entry =>
            entry.Id == "canon-conflict" &&
            entry.Status == "Conflict" &&
            entry.ConflictCheck.Contains("conflict_open", StringComparison.Ordinal));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectKnowledge(NovelAgentDbContext db)
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
            Title = "知识绑定测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "hardfact-1",
                UserId = "user-1",
                SourceProjectId = null,
                EntryType = "HardFact",
                Title = "银蓝邮徽能力边界",
                Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈或升级。",
                Tags = "[\"邮徽\",\"硬事实\"]",
                Weight = 10,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "style-1",
                UserId = "user-1",
                SourceProjectId = null,
                EntryType = "Style",
                Title = "废土邮路风格",
                Content = "描写要有冷硬废土质感。",
                Weight = 5,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "archived-hardfact",
                UserId = "user-1",
                SourceProjectId = null,
                EntryType = "HardFact",
                Title = "已归档事实",
                Content = "已归档，不应进入生产包。",
                Weight = 100,
                IsArchived = true,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "other-user-hardfact",
                UserId = "user-2",
                SourceProjectId = null,
                EntryType = "HardFact",
                Title = "其他用户事实",
                Content = "其他用户不可见。",
                Weight = 100,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
        db.ProjectKnowledgeUsages.AddRange(
            new ProjectKnowledgeUsage
            {
                Id = "usage-hardfact",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                Status = "referenced",
                SourceSessionId = "session-1",
                SourceRunId = "run-1",
                FirstSeenAt = DateTime.UtcNow.AddMinutes(-3),
                LastUsedAt = DateTime.UtcNow,
                UsageCount = 4,
                Note = "项目硬事实",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 80,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                BoundVersion = "knowledge-v3",
                UsedByChaptersJson = "[\"chapter-001\"]"
            },
            new ProjectKnowledgeUsage
            {
                Id = "usage-style",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "style-1",
                Status = "imported",
                SourceSessionId = "session-1",
                FirstSeenAt = DateTime.UtcNow.AddMinutes(-2),
                UsageCount = 1,
                Note = "风格参考"
            });
        db.KnowledgeClassifications.AddRange(
            new KnowledgeClassification
            {
                Id = "classification-hardfact-old",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                Model = "fake-llm",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 70,
                ConstraintLevel = "Reference",
                PackagePolicy = "RelevantOnly",
                Confidence = 0.4,
                ClassificationJson = """
                {
                  "rule": "旧规则不应进入生产包。",
                  "targetEntities": ["旧邮徽"],
                  "shouldEnterGate": false,
                  "shouldEnterBlueprint": false,
                  "shouldEnterFactSnapshot": false
                }
                """,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new KnowledgeClassification
            {
                Id = "classification-hardfact-latest",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                Model = "fake-llm",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 80,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                Confidence = 0.91,
                ClassificationJson = """
                {
                  "rule": "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                  "targetEntities": ["银蓝邮徽"],
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true
                }
                """,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
        db.SaveChanges();
    }
}
