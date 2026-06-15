namespace TM.Web.NovelAgentWeb.Services.Memory;

public static class AgentMemoryScopes
{
    public const string ProjectlessProjectId = "projectless";

    public static bool IsProjectless(string? projectId) =>
        string.Equals(projectId, ProjectlessProjectId, StringComparison.OrdinalIgnoreCase);

    public static string? ToStoreProjectId(string? projectId) =>
        IsProjectless(projectId) ? null : projectId;

    public static string ToExecutionCacheProjectId(string? projectId) =>
        IsProjectless(projectId) || string.IsNullOrWhiteSpace(projectId)
            ? ProjectlessProjectId
            : projectId.Trim();
}
