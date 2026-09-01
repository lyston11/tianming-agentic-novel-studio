using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterFactSnapshotUpdater
{
    Task UpdateAsync(
        string userId,
        string projectId,
        string chapterId,
        string runtimeRunId,
        string? packageId,
        ChapterContinuityFacts facts,
        CancellationToken ct = default);
}
