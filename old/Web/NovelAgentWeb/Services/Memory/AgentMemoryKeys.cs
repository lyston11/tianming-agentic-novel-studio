namespace TM.Web.NovelAgentWeb.Services.Memory;

public static class AgentMemoryKeys
{
    public static string ChatHot(string userId, string sessionId, string? projectId) => $"chat:{userId}:{sessionId}:{(string.IsNullOrWhiteSpace(projectId) ? "*" : projectId)}:hot";
    public static string Session(string userId, string sessionId, string version) => $"memory:session:{userId}:{sessionId}:v{version}";
    public static string Project(string userId, string projectId, string version) => $"memory:project:{userId}:{projectId}:v{version}";
    public static string Author(string userId, string version) => $"memory:author:{userId}:v{version}";
    public static string Execution(string userId, string projectId, string version) => $"memory:execution:{userId}:{projectId}:v{version}";
    public static string MemoryContext(string userId, string sessionId, string projectId, string combinedVersion) => $"memory-context:{userId}:{sessionId}:{projectId}:v{combinedVersion}";
    public static string ToolCache(string userId, string sessionId, string projectId, string phase, string combinedVersion) => $"toolcache:{userId}:{sessionId}:{projectId}:{phase}:v{combinedVersion}";
}
