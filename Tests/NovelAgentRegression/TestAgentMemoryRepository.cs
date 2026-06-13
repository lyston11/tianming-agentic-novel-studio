using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Tests.NovelAgentRegression;

internal sealed class TestAgentMemoryRepository : IAgentMemoryRepository
{
    public ProjectMemory ProjectMemory { get; } = new();
    public SessionMemory SessionMemory { get; } = new();
    public AuthorMemory AuthorMemory { get; } = new();
    public ExecutionMemory ExecutionMemory { get; } = new();
    public Dictionary<string, object> Updates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ProjectMemory> GetProjectMemoryAsync(string userId, string projectId, CancellationToken ct = default)
        => Task.FromResult(ProjectMemory);

    public Task<SessionMemory> GetSessionMemoryAsync(string userId, string projectId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(SessionMemory);

    public Task<AuthorMemory> GetAuthorMemoryAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(AuthorMemory);

    public Task<ExecutionMemory> GetExecutionMemoryAsync(string userId, string projectId, CancellationToken ct = default)
        => Task.FromResult(ExecutionMemory);

    public Task UpdateFieldAsync(string userId, string? projectId, string memoryType, object value, CancellationToken ct = default)
    {
        Updates[memoryType] = value;
        return Task.CompletedTask;
    }

    public Task UpdateMemoryAsync(string userId, string? projectId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        foreach (var (key, value) in updates)
            Updates[key] = value;
        return Task.CompletedTask;
    }

    public Task UpdateSessionMemoryAsync(string userId, string projectId, string sessionId, Dictionary<string, object> updates, CancellationToken ct = default)
    {
        foreach (var (key, value) in updates)
            Updates[key] = value;
        return Task.CompletedTask;
    }
}
