namespace TM.Web.NovelAgentWeb.Support;

public class AgentMemoryUpdate
{
    public SessionMemoryUpdate SessionMemory { get; set; } = new();
    public ProjectMemoryUpdate ProjectMemory { get; set; } = new();
    public AuthorMemoryUpdate AuthorMemory { get; set; } = new();
    public ExecutionMemoryUpdate ExecutionMemory { get; set; } = new();
    public List<string> UsedKnowledgeIds { get; set; } = new();
    public List<string> UsedTropePatterns { get; set; } = new();
}

public class SessionMemoryUpdate
{
    public string? ChatSummary { get; set; }
    public List<string> ExtractedPreferences { get; set; } = new();
}

public class ProjectMemoryUpdate
{
    public List<string> NewConstraints { get; set; } = new();
    // UnresolvedThreads removed - Agent should query StoryBible.ForeshadowLedger directly
}

public class AuthorMemoryUpdate
{
    public string? DisplayName { get; set; }
    public List<string> StyleLikes { get; set; } = new();
    public List<string> StyleDislikes { get; set; } = new();
    public string? ConfirmationTolerance { get; set; }
    public List<string> GenreHabits { get; set; } = new();
    public List<string> FavoriteKnowledgeIds { get; set; } = new();
}

public class ExecutionMemoryUpdate
{
    public string? ToolSuccess { get; set; }
    public string? ToolFailure { get; set; }
    public List<string> ToolFailurePatterns { get; set; } = new();
    public List<string> KnowledgeProcessingFailures { get; set; } = new();
}
