using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public sealed class AgentMemoryAuditSummaryBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReturnsSafeAuditSummaryForCurrentRunOnly()
    {
        await using var db = CreateDb();
        db.AgentMemoryReads.AddRange(
            new AgentMemoryRead
            {
                Id = "read-current",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RunId = "run-1",
                MemoryScope = "author",
                MemoryKeysJson = "[\"display_name\",\"style_likes\"]",
                SourceType = "memory_repository",
                Consumer = "AgentObservationBuilder",
                CreatedAt = DateTime.UtcNow.AddSeconds(1)
            },
            new AgentMemoryRead
            {
                Id = "read-other-user",
                UserId = "user-2",
                ProjectId = "project-1",
                SessionId = "session-1",
                RunId = "run-1",
                MemoryScope = "project",
                MemoryKeysJson = "[\"secret\"]",
                SourceType = "memory_repository",
                Consumer = "AgentObservationBuilder",
                CreatedAt = DateTime.UtcNow.AddSeconds(2)
            });
        db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = "promotion-current",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            SourceScope = "session",
            TargetScope = "project",
            SourceMemoryKey = "user_preferences",
            TargetMemoryKey = "project.constraints",
            PromotionReason = "用户明确要求后续章节保持打怪升级节奏。",
            PayloadJson = "{\"private\":\"不应该出现在摘要契约里\"}",
            CreatedAt = DateTime.UtcNow.AddSeconds(3)
        });
        await db.SaveChangesAsync();

        var summary = await AgentMemoryAuditSummaryBuilder.BuildAsync(
            db,
            "user-1",
            "session-1",
            "project-1",
            "run-1",
            CancellationToken.None);

        var read = Assert.Single(summary.Reads);
        Assert.Equal("read-current", read.Id);
        Assert.Equal("author", read.MemoryScope);
        Assert.Equal(new[] { "display_name", "style_likes" }, read.MemoryKeys);
        var promotion = Assert.Single(summary.Promotions);
        Assert.Equal("promotion-current", promotion.Id);
        Assert.Equal("session", promotion.SourceScope);
        Assert.Equal("project", promotion.TargetScope);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.Equal("用户明确要求后续章节保持打怪升级节奏。", promotion.PromotionReason);
    }

    [Fact]
    public async Task BuildAsync_IncludesSessionAuditRowsWithoutRunIdWhenCurrentRunIsKnown()
    {
        await using var db = CreateDb();
        db.AgentMemoryReads.AddRange(
            new AgentMemoryRead
            {
                Id = "read-session-fallback",
                UserId = "user-1",
                ProjectId = null,
                SessionId = "session-1",
                RunId = null,
                MemoryScope = "author",
                MemoryKeysJson = "[\"display_name\"]",
                SourceType = "memory_repository",
                Consumer = "AgentObservationBuilder",
                CreatedAt = DateTime.UtcNow.AddSeconds(1)
            },
            new AgentMemoryRead
            {
                Id = "read-other-session",
                UserId = "user-1",
                ProjectId = null,
                SessionId = "session-2",
                RunId = null,
                MemoryScope = "author",
                MemoryKeysJson = "[\"display_name\"]",
                SourceType = "memory_repository",
                Consumer = "AgentObservationBuilder",
                CreatedAt = DateTime.UtcNow.AddSeconds(2)
            });
        db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = "promotion-session-fallback",
            UserId = "user-1",
            ProjectId = null,
            SessionId = "session-1",
            RunId = null,
            SourceScope = "session",
            TargetScope = "author",
            SourceMemoryKey = "session.display_name",
            TargetMemoryKey = "author.display_name",
            PromotionReason = "用户在当前会话确认了称呼。",
            PayloadJson = "{\"displayName\":\"lyston\"}",
            CreatedAt = DateTime.UtcNow.AddSeconds(3)
        });
        await db.SaveChangesAsync();

        var summary = await AgentMemoryAuditSummaryBuilder.BuildAsync(
            db,
            "user-1",
            "session-1",
            "project-1",
            "run-1",
            CancellationToken.None);

        var read = Assert.Single(summary.Reads);
        Assert.Equal("read-session-fallback", read.Id);
        Assert.Equal(new[] { "display_name" }, read.MemoryKeys);

        var promotion = Assert.Single(summary.Promotions);
        Assert.Equal("promotion-session-fallback", promotion.Id);
        Assert.Equal("author.display_name", promotion.TargetMemoryKey);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NovelAgentDbContext(options);
    }
}
