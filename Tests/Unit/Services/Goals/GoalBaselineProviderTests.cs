using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class GoalBaselineProviderTests
{
    [Fact]
    public async Task CaptureAsync_UsesLatestMergeRecordAsCanonicalHead()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        db.BranchMergeRecords.Add(new BranchMergeRecord
        {
            Id = "merge-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            StartChapterNumber = 1,
            EndChapterNumber = 3,
            PreviousCanonVersion = "canon:empty",
            NewCanonVersion = "canon:merge-1",
            MergedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await new GoalBaselineProvider(db).CaptureAsync("user-1", "project-1");

        Assert.Equal("canon:merge-1", result.CanonVersion);
    }

    [Fact]
    public async Task CaptureAsync_FreezesExplicitUserKnowledgeAndAbstractStyleVersions()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        db.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
        {
            Id = "blob-1",
            UserId = "user-1",
            ProjectId = "project-1",
            FileName = "source.txt",
            Data = [1],
            ContentHash = "hash",
            KnowledgeVersion = 7,
            Status = "processed"
        });
        db.StyleProfiles.Add(new StyleProfile
        {
            Id = "style-1",
            UserId = "user-1",
            ProjectId = "project-1",
            DocumentBlobId = "blob-1",
            KnowledgeVersion = 7,
            Version = 2,
            FeaturesJson = "{}",
            Status = "active"
        });
        await db.SaveChangesAsync();

        var result = await new GoalBaselineProvider(db).CaptureAsync("user-1", "project-1");

        Assert.Equal("knowledge:user-1:v7", result.KnowledgeVersion);
        Assert.Equal("style:style-1:v2:knowledge-v7", result.StyleProfileVersion);
    }
}
