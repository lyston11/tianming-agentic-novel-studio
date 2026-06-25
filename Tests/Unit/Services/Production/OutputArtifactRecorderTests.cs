using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class OutputArtifactRecorderTests
{
    [Fact]
    public async Task RecordAsync_PersistsOutputArtifactEventWithVisibilityMetadata()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder recorder = new OutputArtifactRecorder(eventWriter);

        var record = await recorder.RecordAsync(new OutputArtifactRecordRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: "pkg-1",
            ToolName: "ProduceChapter",
            Stage: "draft_generation",
            Status: "completed",
            ArtifactType: "chapter_draft",
            ArtifactId: "draft-chapter-001-v1",
            OutputKind: "ProcessArtifact",
            Summary: "第一章草稿已生成。",
            UserVisibleWhere: new[] { "创作工作流" },
            VisibleInWorkflow: true,
            VisibleInLibrary: false,
            SourceEventType: "chapter_draft_generated",
            SourceEventId: "evt-draft-1",
            Data: new { wordCount = 3200 }));

        Assert.NotNull(record.Event);
        Assert.Equal("tool_output_artifact_recorded", record.Event.EventType);
        Assert.Equal(NovelAgentProductionStages.DraftGenerated, record.Event.Stage);
        Assert.Equal("chapter_draft", record.Event.ArtifactType);
        Assert.Equal("draft-chapter-001-v1", record.Event.ArtifactId);
        Assert.Equal("chapter_draft", record.Artifact.ArtifactType);
        Assert.Equal("draft-chapter-001-v1", record.Artifact.ArtifactId);
        Assert.Equal("ProcessArtifact", record.Artifact.OutputKind);

        var evt = await db.ProductionEvents.AsNoTracking().SingleAsync();
        Assert.Contains("\"toolName\":\"ProduceChapter\"", evt.DataJson);
        Assert.Contains("\"sourceEventType\":\"chapter_draft_generated\"", evt.DataJson);
        Assert.Contains("\"sourceEventId\":\"evt-draft-1\"", evt.DataJson);
        Assert.Contains("\"visibleInWorkflow\":true", evt.DataJson);
        Assert.Contains("\"visibleInLibrary\":false", evt.DataJson);
        Assert.Contains("\"userVisibleWhere\":[\"创作工作流\"]", evt.DataJson);
        Assert.Contains("\"wordCount\":3200", evt.DataJson);
    }

    [Fact]
    public async Task RecordAsync_RejectsMissingArtifactIdentity()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder recorder = new OutputArtifactRecorder(eventWriter);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordAsync(new OutputArtifactRecordRequest(
                RuntimeRunId: "run-1",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "chapter-001",
                PackageId: "pkg-1",
                ToolName: "ProduceChapter",
                Stage: "draft_generation",
                Status: "completed",
                ArtifactType: "",
                ArtifactId: "draft-chapter-001-v1",
                OutputKind: "ProcessArtifact",
                Summary: "第一章草稿已生成。",
                UserVisibleWhere: Array.Empty<string>(),
                VisibleInWorkflow: true,
                VisibleInLibrary: false)));

        Assert.Contains("Output artifact requires artifactType and artifactId", ex.Message);
        Assert.Empty(await db.ProductionEvents.ToListAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectChapterAndPackage(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            Role = "User",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "旧邮路",
            Genre = "末世",
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-001",
            ProjectId = "project-1",
            Title = "第一章 银蓝邮徽",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            RuntimeRunId = "run-1",
            PackageKind = "chapter_context_package",
            Status = "pending",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}
