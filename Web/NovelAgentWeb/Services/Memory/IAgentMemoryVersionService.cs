namespace TM.Web.NovelAgentWeb.Services.Memory;

public interface IAgentMemoryVersionService
{
    Task<long> BumpAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct = default);
    Task<string> GetCombinedVersionAsync(string userId, string? projectId, string? sessionId, CancellationToken ct = default);
}
