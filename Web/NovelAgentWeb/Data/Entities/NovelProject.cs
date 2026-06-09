namespace TM.Web.NovelAgentWeb.Data.Entities;

public class NovelProject
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Genre { get; set; }
    public string? SubGenre { get; set; }
    public string? CoreHook { get; set; }
    public string Status { get; set; } = "draft";
    public int WordCount { get; set; } = 0;
    public string? CoverImageUrl { get; set; }
    public string? StorageProjectName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public StoryConstitution? StoryConstitution { get; set; }
    public ICollection<Volume> Volumes { get; set; } = new List<Volume>();
    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
    public ICollection<Foreshadow> Foreshadows { get; set; } = new List<Foreshadow>();
    public ICollection<Character> Characters { get; set; } = new List<Character>();
    public ICollection<WorldSetting> WorldSettings { get; set; } = new List<WorldSetting>();
    public ICollection<Material> Materials { get; set; } = new List<Material>();
    public ICollection<KnowledgeBase> KnowledgeBases { get; set; } = new List<KnowledgeBase>();
    public ICollection<AgentMemory> AgentMemories { get; set; } = new List<AgentMemory>();
    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();
}
