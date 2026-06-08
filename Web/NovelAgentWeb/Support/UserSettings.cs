using System.Text.Json;
using System.Text.Json.Serialization;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class UserSettings
{
    // LLM Configuration
    public string LlmProvider { get; set; } = "openai";
    public string LlmApiKey { get; set; } = string.Empty;
    public string LlmBaseUrl { get; set; } = string.Empty;
    public string LlmModel { get; set; } = "gpt-4o";
    public double LlmTemperature { get; set; } = 0.7;
    public int LlmMaxTokens { get; set; } = 4096;

    // Embedding Configuration
    public string EmbeddingProvider { get; set; } = "local";
    public string EmbeddingApiKey { get; set; } = string.Empty;
    public string EmbeddingBaseUrl { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "bge-small-zh-v1.5";

    // Agent Configuration
    public string AgentDefaultRisk { get; set; } = "Medium";
    public bool AgentAutoContinue { get; set; } = true;
    public int AgentMaxAutoSteps { get; set; } = 12;

    // Generation Configuration
    public string DefaultGenre { get; set; } = "玄幻";
    public string DefaultSubGenre { get; set; } = "";
    public int DefaultChapterWordCount { get; set; } = 3000;
    public int DefaultVolumeChapterCount { get; set; } = 6;

    // UI Preferences
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh-CN";
    public bool ShowStepDetails { get; set; } = true;

    // Presets
    public List<LlmPreset> Presets { get; set; } = new()
    {
        new LlmPreset { Name = "OpenAI", Provider = "openai", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o" },
        new LlmPreset { Name = "Anthropic", Provider = "anthropic", BaseUrl = "https://api.anthropic.com/v1", Model = "claude-sonnet-4-20250514" },
        new LlmPreset { Name = "DeepSeek", Provider = "deepseek", BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat" },
        new LlmPreset { Name = "Moonshot", Provider = "moonshot", BaseUrl = "https://api.moonshot.cn/v1", Model = "moonshot-v1-8k" },
        new LlmPreset { Name = "Zhipu (GLM)", Provider = "zhipu", BaseUrl = "https://open.bigmodel.cn/api/paas/v4", Model = "glm-4-flash" },
        new LlmPreset { Name = "Qwen", Provider = "qwen", BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", Model = "qwen-plus" },
        new LlmPreset { Name = "Local (Ollama)", Provider = "ollama", BaseUrl = "http://localhost:11434/v1", Model = "qwen2.5:7b" },
        new LlmPreset { Name = "Custom", Provider = "custom", BaseUrl = "", Model = "" },
    };
}

public sealed class LlmPreset
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public sealed class UserSettingsManager
{
    private readonly string _settingsPath;
    private UserSettings? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserSettingsManager(string storageRoot, string projectName)
    {
        var dir = Path.Combine(storageRoot, "Projects", projectName, "Settings");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "user_settings.json");
    }

    public async Task<UserSettings> LoadAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null) return _cached;
            if (!File.Exists(_settingsPath))
            {
                _cached = new UserSettings();
                return _cached;
            }
            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            _cached = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new UserSettings();
            Normalize(_cached);
            return _cached;
        }
        finally { _lock.Release(); }
    }

    public async Task<UserSettings> SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Normalize(settings);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await File.WriteAllTextAsync(_settingsPath, json, ct);
            _cached = settings;
            return settings;
        }
        finally { _lock.Release(); }
    }

    public Task<UserSettings> ResetAsync(CancellationToken ct = default) =>
        SaveAsync(new UserSettings(), ct);

    public void InvalidateCache() => _cached = null;

    private static void Normalize(UserSettings settings)
    {
        var defaults = new UserSettings();

        settings.LlmProvider = Clean(settings.LlmProvider, defaults.LlmProvider);
        settings.LlmBaseUrl = Clean(settings.LlmBaseUrl);
        settings.LlmModel = Clean(settings.LlmModel, defaults.LlmModel);
        settings.LlmTemperature = Math.Clamp(settings.LlmTemperature, 0, 2);
        settings.LlmMaxTokens = Math.Clamp(settings.LlmMaxTokens <= 0 ? defaults.LlmMaxTokens : settings.LlmMaxTokens, 256, 200000);

        settings.EmbeddingProvider = Clean(settings.EmbeddingProvider, defaults.EmbeddingProvider);
        settings.EmbeddingBaseUrl = Clean(settings.EmbeddingBaseUrl);
        settings.EmbeddingModel = Clean(settings.EmbeddingModel, defaults.EmbeddingModel);

        settings.AgentDefaultRisk = Clean(settings.AgentDefaultRisk, defaults.AgentDefaultRisk);
        if (!new[] { "Low", "Medium", "High", "Critical" }.Contains(settings.AgentDefaultRisk, StringComparer.OrdinalIgnoreCase))
            settings.AgentDefaultRisk = defaults.AgentDefaultRisk;
        settings.AgentMaxAutoSteps = Math.Clamp(settings.AgentMaxAutoSteps <= 0 ? defaults.AgentMaxAutoSteps : settings.AgentMaxAutoSteps, 1, 100);

        settings.DefaultGenre = Clean(settings.DefaultGenre, defaults.DefaultGenre);
        settings.DefaultSubGenre = Clean(settings.DefaultSubGenre);
        settings.DefaultChapterWordCount = Math.Clamp(settings.DefaultChapterWordCount <= 0 ? defaults.DefaultChapterWordCount : settings.DefaultChapterWordCount, 500, 50000);
        settings.DefaultVolumeChapterCount = Math.Clamp(settings.DefaultVolumeChapterCount <= 0 ? defaults.DefaultVolumeChapterCount : settings.DefaultVolumeChapterCount, 1, 200);

        settings.Theme = Clean(settings.Theme, defaults.Theme);
        settings.Language = Clean(settings.Language, defaults.Language);
        settings.Presets = settings.Presets?.Count > 0 ? settings.Presets : defaults.Presets;
    }

    private static string Clean(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
