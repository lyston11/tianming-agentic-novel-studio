using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterContinuityFactPersister
{
    Task<StoryBibleCommitResult> PersistAsync(
        string userId,
        string projectId,
        ChapterContinuityFacts facts,
        CancellationToken ct = default);
}
