namespace TM.Web.NovelAgentWeb.Data.Entities;

public class UserSettings
{
    public string UserId { get; set; } = null!;

    // LLM Configuration
    public string? LlmProvider { get; set; }
    public string? LlmApiKeyEncrypted { get; set; }
    public string? LlmBaseUrl { get; set; }
    public string? LlmModel { get; set; }
    public float LlmTemperature { get; set; } = 0.7f;
    public int LlmMaxTokens { get; set; } = 4096;

    // Embedding Configuration
    public string EmbeddingProvider { get; set; } = "local";
    public string EmbeddingModel { get; set; } = "bge-small-zh-v1.5";

    // Agent Configuration
    public string AgentDefaultRisk { get; set; } = "Medium";
    public bool AgentLoopAutoProceed { get; set; } = true;
    public int AgentLoopMaxSteps { get; set; } = 12;

    // Creative Defaults
    public string DefaultGenre { get; set; } = "玄幻";
    public int DefaultChapterWordCount { get; set; } = 3000;

    // UI Preferences
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh-CN";

    // Navigation property
    public User User { get; set; } = null!;
}
