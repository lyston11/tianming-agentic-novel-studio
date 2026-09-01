namespace TM.Web.NovelAgentWeb.Services.Agent;

public sealed class PiRuntimeOptions
{
    public const string SectionName = "PiRuntime";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:4317";
    public string InternalApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
}
