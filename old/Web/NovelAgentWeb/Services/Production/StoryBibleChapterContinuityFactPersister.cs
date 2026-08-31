using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class StoryBibleChapterContinuityFactPersister : IChapterContinuityFactPersister
{
    private readonly IServiceScopeFactory _scopeFactory;

    public StoryBibleChapterContinuityFactPersister(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task<StoryBibleCommitResult> PersistAsync(
        string userId,
        string projectId,
        ChapterContinuityFacts facts,
        CancellationToken ct = default)
    {
        var storyBible = new StoryBibleService(new WebStoryBibleDocumentStore(_scopeFactory, userId, projectId));
        return storyBible.UpsertContinuityFactsAsync(facts, ct);
    }
}
