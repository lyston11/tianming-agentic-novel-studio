using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using Xunit;

namespace Tests.Unit.Services.Canon;

public sealed class LightweightCanonTests
{
    [Fact]
    public async Task ExtractAsync_PersistsOnlySemanticallyApprovedLongTermItemsWithExactBodyEvidence()
    {
        await using var db = CreateDb();
        var candidate = await SeedCandidateAsync(db);
        const string body = "林岚一时紧张。她在议会公开自己是失踪王女。随后走进走廊，能力仍在冷却。";
        var identityQuote = "她在议会公开自己是失踪王女";
        var identityStart = body.IndexOf(identityQuote, StringComparison.Ordinal);
        var draft = new LightweightCanonDraft(
        [
            new LightweightSummaryItemDraft("emotion", "temporary_emotion", "林岚短暂紧张", new CanonEvidenceSpan(2, 6, "一时紧张")),
            new LightweightSummaryItemDraft("identity", "key_change", "林岚公开王女身份", new CanonEvidenceSpan(identityStart, identityStart + identityQuote.Length, identityQuote)),
            new LightweightSummaryItemDraft("location", "ordinary_movement", "林岚进入走廊", new CanonEvidenceSpan(22, 28, "走进走廊")),
            new LightweightSummaryItemDraft("cooldown", "temporary_ability_state", "能力仍在冷却", new CanonEvidenceSpan(body.IndexOf("能力仍在冷却", StringComparison.Ordinal), body.IndexOf("能力仍在冷却", StringComparison.Ordinal) + 6, "能力仍在冷却"))
        ],
        [
            new CanonChangeDraft("change-identity", "identity_reveal", "林岚", "向议会公开失踪王女身份", new CanonEvidenceSpan(identityStart, identityStart + identityQuote.Length, identityQuote))
        ]);
        var review = new LightweightCanonSemanticReview(
            ["identity"],
            ["change-identity"],
            new Dictionary<string, string>
            {
                ["emotion"] = "临时情绪",
                ["location"] = "普通移动",
                ["cooldown"] = "短期能力状态"
            });
        var extractor = new ContinuitySummaryExtractor(
            db,
            new StubCurrentUserService("user-1"),
            new StubExtractionModel(draft),
            new StubSemanticReviewModel(review),
            new CanonChangeExtractor(db));

        var result = await extractor.ExtractAsync(candidate.Id);

        var summary = Assert.Single(await db.ContinuitySummaries.ToListAsync());
        Assert.Equal(summary.Id, result.Summary.Id);
        Assert.Equal("candidate", summary.Status);
        var summaryDocument = JsonDocument.Parse(summary.SummaryJson);
        Assert.Equal("chapter_body", summaryDocument.RootElement.GetProperty("sourcePriority").GetString());
        var itemContents = summaryDocument.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("Content").GetString())
            .ToArray();
        Assert.Equal(new[] { "林岚公开王女身份" }, itemContents);
        var change = Assert.Single(await db.CanonChanges.ToListAsync());
        Assert.Equal("identity_reveal", change.ChangeType);
        Assert.Equal(
            identityQuote,
            JsonDocument.Parse(change.EvidenceRefsJson).RootElement[0].GetProperty("Quote").GetString());
    }

    [Fact]
    public async Task ExtractAsync_RejectsEvidenceThatDoesNotMatchChapterBody()
    {
        await using var db = CreateDb();
        var candidate = await SeedCandidateAsync(db);
        var draft = new LightweightCanonDraft(
        [
            new LightweightSummaryItemDraft(
                "fabricated",
                "key_change",
                "不存在的身份变化",
                new CanonEvidenceSpan(0, 4, "伪造证据"))
        ],
        []);
        var review = new LightweightCanonSemanticReview(["fabricated"], [], new Dictionary<string, string>());
        var extractor = new ContinuitySummaryExtractor(
            db,
            new StubCurrentUserService("user-1"),
            new StubExtractionModel(draft),
            new StubSemanticReviewModel(review),
            new CanonChangeExtractor(db));

        await Assert.ThrowsAsync<InvalidOperationException>(() => extractor.ExtractAsync(candidate.Id));

        Assert.Empty(await db.ContinuitySummaries.ToListAsync());
        Assert.Empty(await db.CanonChanges.ToListAsync());
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task<CandidateChapter> SeedCandidateAsync(NovelAgentDbContext db)
    {
        const string body = "林岚一时紧张。她在议会公开自己是失踪王女。随后走进走廊，能力仍在冷却。";
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "测试小说" });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "task-1",
            BranchId = "branch-1",
            ArtifactType = "ReviewedCandidateChapter",
            ContentJson = JsonSerializer.Serialize(new ChapterDraftArtifact
            {
                ChapterId = "chapter-1",
                DraftContent = body
            }),
            ContentHash = "hash-1"
        });
        var candidate = new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            Version = 1,
            CurrentArtifactId = "artifact-1"
        };
        db.CandidateChapters.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }

    private sealed class StubExtractionModel(LightweightCanonDraft draft) : ILightweightCanonExtractionModelClient
    {
        public Task<LightweightCanonDraft> ExtractAsync(
            LightweightCanonExtractionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(draft);
    }

    private sealed class StubSemanticReviewModel(LightweightCanonSemanticReview review) : ILightweightCanonSemanticReviewModelClient
    {
        public Task<LightweightCanonSemanticReview> ReviewAsync(
            LightweightCanonSemanticReviewRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(review);
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }
}
