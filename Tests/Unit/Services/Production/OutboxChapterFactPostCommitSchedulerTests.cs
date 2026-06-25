using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class OutboxChapterFactPostCommitSchedulerTests
{
    [Fact]
    public async Task ScheduleAsync_EnqueuesContinuityFactExtractionWithoutCallingWriter()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        var scheduler = new OutboxChapterFactPostCommitScheduler(
            truthStore,
            userId: "user-1",
            projectId: "project-1",
            NullLogger<OutboxChapterFactPostCommitScheduler>.Instance);
        var run = new NovelAgentRun
        {
            RunId = "run-1",
            TargetChapterId = "chapter-001",
            UserGoal = "写第一章并提交书城。"
        };
        var package = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            HardContinuityFacts = { "银蓝邮徽不能攻击。" }
        };

        await scheduler.ScheduleAsync(
            new ChapterFactWriteRequest
            {
                Run = run,
                ContextPackage = package,
                CommittedContent = "沈砚按住银蓝邮徽，邮徽只帮他辨认旧邮路。"
            });

        var outbox = await db.OutboxEvents.SingleAsync();
        Assert.Equal("extract_chapter_continuity_facts", outbox.EventType);
        Assert.Equal("chapter", outbox.AggregateType);
        Assert.Equal("chapter-001", outbox.AggregateId);
        Assert.Equal("user-1", outbox.UserId);
        Assert.Equal("project-1", outbox.ProjectId);
        Assert.Equal("run-1", outbox.RuntimeRunId);

        using var payload = JsonDocument.Parse(outbox.PayloadJson);
        var root = payload.RootElement;
        Assert.Equal("chapter-001", root.GetProperty("run").GetProperty("targetChapterId").GetString());
        Assert.Equal("pkg-1", root.GetProperty("contextPackage").GetProperty("packageId").GetString());
        Assert.Contains("银蓝邮徽", root.GetProperty("committedContent").GetString());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

}
