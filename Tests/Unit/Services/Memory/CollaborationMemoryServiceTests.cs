using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public sealed class CollaborationMemoryServiceTests
{
    [Fact]
    public async Task AddProjectDecisionAsync_RejectsWorkFactsFromCollaborationMemory()
    {
        await using var db = CreateDb();
        var service = new CollaborationMemoryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddProjectDecisionAsync(
            "user-1",
            "project-1",
            CollaborationMemoryKind.WorkFact,
            "{\"character\":\"林岚\",\"ability\":\"御风\"}",
            "user"));

        Assert.Empty(db.ProjectCollaborationDecisions);
    }

    [Fact]
    public async Task PendingSessionProposal_DoesNotCrossSessionsUntilUserAcceptsIt()
    {
        await using var db = CreateDb();
        var service = new CollaborationMemoryService(db);
        var proposal = await service.AddSessionStateAsync(
            "user-1",
            "project-1",
            "session-1",
            CollaborationMemoryKind.DialogueProposal,
            "{\"proposal\":\"后续章节增加冷幽默对白\"}",
            accepted: false);

        var sameSession = await service.GetSessionStateAsync("user-1", "project-1", "session-1");
        var otherSession = await service.GetSessionStateAsync("user-1", "project-1", "session-2");

        Assert.Contains(sameSession, item => item.Id == proposal.Id);
        Assert.Empty(otherSession);
        Assert.Empty(db.ProjectCollaborationDecisions);

        var accepted = await service.AcceptSessionProposalAsync(
            "user-1",
            "project-1",
            "session-1",
            proposal.Id,
            CollaborationMemoryKind.AestheticDirection);

        Assert.Equal("active", accepted.Status);
        Assert.Equal(proposal.Id, accepted.SourceSessionStateId);
        Assert.Equal("accepted", (await db.SessionDialogueStates.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExperienceDecisions_CreateProjectDecisionSuppressionAuditAndSingleGoalOverride()
    {
        await using var db = CreateDb();
        var service = new CollaborationMemoryService(db);
        var observations = new[]
        {
            await service.AddExperienceObservationAsync("user-1", "project-1", "goal-1", "quality", "{}", "{}"),
            await service.AddExperienceObservationAsync("user-1", "project-1", "goal-1", "quality", "{}", "{}"),
            await service.AddExperienceObservationAsync("user-1", "project-1", "goal-1", "quality", "{}", "{}")
        };
        var acceptedSuggestion = await service.AddExperienceSuggestionAsync(
            "user-1", "project-1", observations[0].Id, "retrieval_weight", "{\"continuityBoost\":0.1}", "连续性召回在返工中更有效", "retrieval:continuity-boost");
        var rejectedSuggestion = await service.AddExperienceSuggestionAsync(
            "user-1", "project-1", observations[1].Id, "style", "{\"dialogueDensity\":0.3}", "对白密度实验没有改善", "style:dialogue-density");
        var tryOnceSuggestion = await service.AddExperienceSuggestionAsync(
            "user-1", "project-1", observations[2].Id, "quality", "{\"literaryReview\":\"strict\"}", "严格审稿可能改善下一批章节", "quality:strict-review");

        var accepted = await service.DecideSuggestionAsync(
            "user-1", "project-1", acceptedSuggestion.Id, ExperienceSuggestionAction.Accept, null, "采用为项目默认");
        var rejected = await service.DecideSuggestionAsync(
            "user-1", "project-1", rejectedSuggestion.Id, ExperienceSuggestionAction.Reject, null, "不适合本项目");
        var tryOnce = await service.DecideSuggestionAsync(
            "user-1", "project-1", tryOnceSuggestion.Id, ExperienceSuggestionAction.TryOnce, "goal-next", "下一目标试一次");

        Assert.Equal("project", accepted.Decision!.Scope);
        Assert.False(accepted.Decision.ExpiresAfterGoal);
        Assert.Equal("rejected", rejected.Suggestion.Status);
        Assert.Equal("style:dialogue-density", rejected.Suggestion.SuppressionFingerprint);
        Assert.Equal("不适合本项目", rejected.Suggestion.DecisionReason);
        Assert.Null(rejected.Decision);
        Assert.Equal("goal", tryOnce.Decision!.Scope);
        Assert.Equal("goal-next", tryOnce.Decision.EffectiveGoalId);
        Assert.True(tryOnce.Decision.ExpiresAfterGoal);
    }

    [Fact]
    public async Task CompileWorkingContextAsync_ReturnsReadOnlyViewWithoutPersistingIt()
    {
        await using var db = CreateDb();
        var service = new CollaborationMemoryService(db);
        await service.AddProjectDecisionAsync(
            "user-1", "project-1", CollaborationMemoryKind.CollaborationMode, "{\"mode\":\"先建议后采用\"}", "user");
        await service.AddSessionStateAsync(
            "user-1", "project-1", "session-1", CollaborationMemoryKind.DialogueReference, "{\"it\":\"归家承诺\"}", false);
        var observation = await service.AddExperienceObservationAsync(
            "user-1", "project-1", "goal-1", "retrieval", "{}", "{}");
        await service.AddExperienceSuggestionAsync(
            "user-1", "project-1", observation.Id, "retrieval", "{\"neighborWindow\":2}", "扩大邻窗可能改善召回", "retrieval:neighbor-window");
        var observationCount = await db.ExperienceObservations.CountAsync();
        var before = db.ChangeTracker.Entries().Count();

        var context = await service.CompileWorkingContextAsync(new WorkingContextRequest(
            "user-1",
            "project-1",
            "session-1",
            "goal-1",
            "canon-v8",
            "{\"objective\":\"写第三十章\"}",
            "knowledge-v12",
            ["canon-change-1"],
            ["knowledge-entry-3"]));

        Assert.Equal("canon-v8", context.CanonVersion);
        Assert.Equal("knowledge-v12", context.KnowledgeSnapshotVersion);
        Assert.Single(context.ProjectDecisions);
        Assert.Single(context.SessionState);
        Assert.Single(context.AdvisorySuggestions);
        Assert.Equal(before, db.ChangeTracker.Entries().Count());
        Assert.Equal(observationCount, await db.ExperienceObservations.CountAsync());
    }

    [Fact]
    public async Task AddExperienceSuggestionAsync_RequiresOwnedObservationAndSuppressesRejectedFingerprint()
    {
        await using var db = CreateDb();
        var service = new CollaborationMemoryService(db);
        var otherObservation = await service.AddExperienceObservationAsync(
            "user-2", "project-2", null, "retrieval", "{}", "{}");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddExperienceSuggestionAsync(
            "user-1", "project-1", otherObservation.Id, "retrieval", "{\"window\":2}", "扩大邻窗", "retrieval:window"));

        var ownObservation = await service.AddExperienceObservationAsync(
            "user-1", "project-1", null, "retrieval", "{}", "{}");
        var suggestion = await service.AddExperienceSuggestionAsync(
            "user-1", "project-1", ownObservation.Id, "retrieval", "{\"window\":2}", "扩大邻窗", "retrieval:window");
        await service.DecideSuggestionAsync(
            "user-1", "project-1", suggestion.Id, ExperienceSuggestionAction.Reject, null, "不采用");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExperienceSuggestionAsync(
            "user-1", "project-1", ownObservation.Id, "retrieval", "{\"window\":3}", "再次扩大邻窗", "retrieval:window"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
