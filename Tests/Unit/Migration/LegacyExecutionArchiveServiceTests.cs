using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DataMigration;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Migration;

public sealed class LegacyExecutionArchiveServiceTests
{
    [Fact]
    public async Task PrepareProjectAsync_ArchivesLegacyExecutionAndCreatesRecoveryIntentIdempotently()
    {
        await using var db = CreateDb();
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "旧灯城",
            Status = "active"
        });
        db.AgentRuntimeRuns.Add(new AgentRuntimeRun
        {
            Id = "runtime-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            Status = "running",
            Mode = "write",
            UserMessage = "继续写第一章",
            SourceMessageId = "message-1",
            IdempotencyKey = "runtime-1"
        });
        db.AgentRuns.Add(new AgentRun
        {
            Id = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            RunType = "chapter_generation",
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "tool-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "runtime-1",
            ToolName = "ProduceChapter",
            ArgumentsHash = "hash",
            Status = "running"
        });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>();
        current.Setup(service => service.GetUserId()).Returns("user-1");
        var sessions = new Mock<IAgentSessionApplicationService>();
        sessions.Setup(service => service.ListRuntimeSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TM.Web.NovelAgentWeb.Support.AgentSession
                {
                    SessionId = "session-1",
                    UserId = "user-1",
                    ActiveProjectId = "project-1",
                    Phase = "writing"
                }
            ]);
        var service = new LegacyExecutionArchiveService(db, current.Object, sessions.Object);

        var first = await service.PrepareProjectAsync("project-1");
        var second = await service.PrepareProjectAsync("project-1");

        Assert.False(first.Reused);
        Assert.True(second.Reused);
        Assert.Equal(first.ArchiveDocumentId, second.ArchiveDocumentId);
        var archive = Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Equal(LegacyExecutionArchiveService.ArchiveSourceType, archive.SourceType);
        var archiveJson = string.Concat(await db.ContentChunks
            .Where(item => item.DocumentId == archive.Id)
            .OrderBy(item => item.ChunkIndex)
            .Select(item => item.ChunkText)
            .ToArrayAsync());
        Assert.Contains("runtime-1", archiveJson);
        Assert.Contains("ProduceChapter", archiveJson);
        var intent = Assert.Single(await db.CreativeIntents.ToListAsync());
        Assert.Equal(LegacyExecutionArchiveService.RecoveryIntentSource, intent.Source);
        Assert.True(intent.RequiresConfirmation);
        Assert.Equal("archived", (await db.AgentRuntimeRuns.SingleAsync()).Status);
        Assert.Empty(await db.ContentVectorPoints.ToListAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
