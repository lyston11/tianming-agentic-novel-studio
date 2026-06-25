using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public interface IToolSearchCacheService
{
    Task<ToolSearchCacheLookup> GetAsync(
        AgentSession session,
        string phase,
        string toolCatalogSignature,
        CancellationToken ct = default);

    Task SaveAsync(
        AgentSession session,
        string phase,
        IReadOnlyList<ToolSchema> tools,
        string toolCatalogSignature,
        CancellationToken ct = default);
}

public sealed record ToolSearchCacheLookup(
    IReadOnlyList<ToolSchema>? Tools,
    string Source)
{
    public bool Hit => Tools is { Count: > 0 };

    public static ToolSearchCacheLookup Miss { get; } = new(null, "none");
}
