using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public interface ITianmingWritingGateway
{
    Task<ChapterDraftArtifact> GenerateAsync(
        string userId,
        string projectId,
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceTianmingWritingGateway : ITianmingWritingGateway
{
    private readonly IWorkspaceFactory _workspaces;

    public WorkspaceTianmingWritingGateway(IWorkspaceFactory workspaces)
    {
        _workspaces = workspaces;
    }

    public async Task<ChapterDraftArtifact> GenerateAsync(
        string userId,
        string projectId,
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        CancellationToken cancellationToken = default)
    {
        var entry = await _workspaces.AcquireAsync(userId, projectId, cancellationToken);
        try
        {
            return await entry.Workspace.ProductionKernel.GenerateDraftWithChangesAsync(
                run,
                package,
                cancellationToken);
        }
        finally
        {
            _workspaces.Release(userId, projectId);
        }
    }
}
