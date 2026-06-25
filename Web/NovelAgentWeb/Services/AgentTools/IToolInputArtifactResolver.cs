using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public interface IToolInputArtifactResolver
{
    Task<ToolInputArtifactResolution> ResolveAsync(
        ToolInputArtifactResolutionRequest request,
        CancellationToken ct = default);
}

public sealed record ToolInputArtifactResolutionRequest(
    AgentToolDefinition Tool,
    AgentToolCall Call,
    AgentSession Session,
    StoryBibleDocument Bible,
    string WorkspaceProjectId,
    Func<CancellationToken, Task<StoryBibleDocument>>? LatestStoryBibleLoader = null);

public sealed record ToolInputArtifactResolution
{
    public bool BlocksExecution { get; init; }
    public string MissingPrerequisite { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public IReadOnlyList<ToolInputArtifactState> InputArtifacts { get; init; } = Array.Empty<ToolInputArtifactState>();

    public static ToolInputArtifactResolution Allow { get; } = new();
}

public sealed record ToolInputArtifactState(
    string ArtifactName,
    string Status,
    string ArtifactId,
    string Message,
    bool BlocksExecution,
    IReadOnlyList<string> RecommendedActions);
