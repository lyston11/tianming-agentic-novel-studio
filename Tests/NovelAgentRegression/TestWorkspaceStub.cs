using TM.Services.Framework.AI.NovelAgent.Services;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Test stub for NovelAgentWorkspace to avoid pulling in full WebRuntime dependencies.
/// </summary>
public sealed class NovelAgentWorkspace
{
    public string UserId { get; init; } = "test-user";
    public string ProjectId { get; init; } = string.Empty;
    public string ProjectName { get; init; } = "TestProject";
    public string StorageRoot { get; init; } = "./test-storage";
    public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
    public required NovelAgentOrchestrator Orchestrator { get; init; }
}
