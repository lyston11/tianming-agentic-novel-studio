using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterContextPackageRecorder
{
    Task<TianmingPackage> RecordAsync(
        RecordChapterContextPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string projectId,
        string runtimeRunId,
        string packageId,
        CancellationToken cancellationToken = default);
}

public sealed record RecordChapterContextPackageRequest(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string ProjectId,
    string? UserGoal,
    DateTime? RunUpdatedAt,
    ChapterContextPackageSummary Package,
    string? AgentRuntimeRunId = null);
