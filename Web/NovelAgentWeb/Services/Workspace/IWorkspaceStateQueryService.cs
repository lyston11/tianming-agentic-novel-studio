using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IWorkspaceStateQueryService
{
    Task<AgentWorkspaceState> QueryAsync(
        WorkspaceStateQueryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceStateQueryRequest(
    string UserId,
    string SessionId,
    string ActiveProjectId,
    string Phase,
    string AuthorDisplayName,
    int StyleLikeCount,
    int StyleDislikeCount,
    int GenreHabitCount);
