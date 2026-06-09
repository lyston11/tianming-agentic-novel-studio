namespace TM.Web.NovelAgentWeb.Data.Entities;

public class User
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = null!;
    public int StorageQuotaMb { get; set; } = 5120;
    public int ApiCallQuota { get; set; } = 10000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public UserSettings? UserSettings { get; set; }
    public ICollection<NovelProject> NovelProjects { get; set; } = new List<NovelProject>();
    public ICollection<Material> Materials { get; set; } = new List<Material>();
    public ICollection<AgentMemory> AgentMemories { get; set; } = new List<AgentMemory>();
    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();
}
