using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public interface IProjectKnowledgeUsageService
{
    Task MarkImportedAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        string source,
        CancellationToken ct = default);

    Task MarkReferencedAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        string? runId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProjectKnowledgeUsage>> ListForProjectAsync(
        string userId,
        string projectId,
        CancellationToken ct = default);
}
