using Microsoft.EntityFrameworkCore;
using Xunit;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace Tests.Unit.Services.AgentApplication;

public sealed class LegacyProjectSnapshotReaderAdapterTests
{
    [Fact]
    public async Task Snapshot_contains_only_current_committed_chapter_versions_and_active_knowledge()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-1",
            ProjectId = "project-1",
            Title = "One",
            ChapterNumber = 1,
            CurrentDocumentId = "document-current"
        });
        db.ChapterVersions.AddRange(
            Version("version-current", "document-current", "committed", 2),
            Version("version-old", "document-old", "committed", 1),
            Version("version-draft", "document-draft", "draft", 3));
        db.ProjectCollaborationDecisions.Add(new ProjectCollaborationDecision
        {
            Id = "decision-active",
            UserId = "user-1",
            ProjectId = "project-1",
            Status = "active"
        });
        db.KnowledgeEntries.AddRange(
            Knowledge("knowledge-active", "active"),
            Knowledge("knowledge-archived", "archived"));
        await db.SaveChangesAsync();

        var adapter = new LegacyProjectSnapshotReaderAdapter(db, new StubBaselines());
        var snapshot = await adapter.ReadRecoverableSnapshotAsync("user-1", "project-1", CancellationToken.None);

        Assert.Equal(["version-current"], snapshot.FormalChapterVersionIds);
        Assert.Contains("decision-active", snapshot.ConfirmedDecisionIds);
        Assert.Equal(["knowledge-active"], snapshot.KnowledgeIds);
        Assert.Equal("canon-v2", snapshot.CanonBaselineVersion);
    }

    private static ChapterVersion Version(string id, string documentId, string status, int version) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        ChapterId = "chapter-1",
        ContentDocumentId = documentId,
        VersionNumber = version,
        Title = "One",
        Status = status
    };

    private static KnowledgeEntry Knowledge(string id, string status) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        LogicalKnowledgeId = id,
        DocumentBlobId = "blob-1",
        EntryType = "fact",
        Title = id,
        Content = id,
        Status = status
    };

    private sealed class StubBaselines : IGoalBaselineProvider
    {
        public Task<GoalBaselines> CaptureAsync(
            string userId,
            string projectId,
            CancellationToken cancellationToken = default) => Task.FromResult(new GoalBaselines(
                "canon-v2",
                "knowledge-v3",
                "quality-v1",
                "style-v1",
                "{}",
                "{\"agent\":\"v1\"}",
                "{}"));
    }
}
