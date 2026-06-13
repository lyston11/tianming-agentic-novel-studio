using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public interface IToolSearchCacheService
{
    Task<IReadOnlyList<ToolSchema>?> GetAsync(
        AgentSession session,
        string phase,
        CancellationToken ct = default);

    Task SaveAsync(
        AgentSession session,
        string phase,
        IReadOnlyList<ToolSchema> tools,
        CancellationToken ct = default);
}
