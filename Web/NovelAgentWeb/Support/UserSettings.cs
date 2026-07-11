using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class UserSettings
{
    // LLM Configuration
    public string LlmProvider { get; set; } = "openai";
    public string LlmApiKey { get; set; } = string.Empty;
    public bool LlmApiKeyEncryptedValuePresent { get; set; }
    public bool LlmApiKeyReadable { get; set; } = true;
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
    public bool AgentLoopAutoProceed { get; set; } = true;
    public int AgentLoopMaxSteps { get; set; } = 12;

    // Generation Configuration
    public string DefaultGenre { get; set; } = "玄幻";
    public string DefaultSubGenre { get; set; } = "";
    public int DefaultChapterWordCount { get; set; } = 3000;
    public int DefaultVolumeChapterCount { get; set; } = 6;

    // UI Preferences
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh-CN";

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IBackgroundUserContext _backgroundUserContext;
    private readonly ILlmApiKeyProtector _apiKeyProtector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserSettingsManager(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        IBackgroundUserContext backgroundUserContext,
        ILlmApiKeyProtector apiKeyProtector)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _backgroundUserContext = backgroundUserContext ?? throw new ArgumentNullException(nameof(backgroundUserContext));
        _apiKeyProtector = apiKeyProtector ?? throw new ArgumentNullException(nameof(apiKeyProtector));
    }

    public async Task<UserSettings> LoadAsync(CancellationToken ct = default)
    {
        var databaseSettings = await TryLoadDatabaseSettingsAsync(ct).ConfigureAwait(false);
        if (databaseSettings != null)
        {
            return databaseSettings;
        }

        throw new InvalidOperationException("Database-backed user settings were not found for the current user.");
    }

    public async Task<UserSettings> SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Normalize(settings);
            if (await TrySaveDatabaseSettingsAsync(settings, ct).ConfigureAwait(false))
            {
                return settings;
            }

            throw new InvalidOperationException("Database-backed user settings could not be saved for the current user.");
        }
        finally { _lock.Release(); }
    }

    public Task<UserSettings> ResetAsync(CancellationToken ct = default) =>
        SaveAsync(new UserSettings(), ct);

    private async Task<UserSettings?> TryLoadDatabaseSettingsAsync(CancellationToken ct)
    {
        var userId = TryGetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Database-backed user settings require an authenticated user and IServiceScopeFactory.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var entity = await db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct)
            .ConfigureAwait(false);

        if (entity == null)
        {
            throw new InvalidOperationException("Database-backed user settings were not found for the current user.");
        }

        var apiKeyReadable = _apiKeyProtector.TryUnprotect(entity.LlmApiKeyEncrypted, out var apiKey);
        var settings = new UserSettings
        {
            LlmProvider = entity.LlmProvider ?? string.Empty,
            LlmApiKey = apiKey,
            LlmApiKeyEncryptedValuePresent = !string.IsNullOrWhiteSpace(entity.LlmApiKeyEncrypted),
            LlmApiKeyReadable = apiKeyReadable,
            LlmBaseUrl = entity.LlmBaseUrl ?? string.Empty,
            LlmModel = entity.LlmModel ?? string.Empty,
            LlmTemperature = entity.LlmTemperature,
            LlmMaxTokens = entity.LlmMaxTokens,
            EmbeddingProvider = entity.EmbeddingProvider,
            EmbeddingModel = entity.EmbeddingModel,
            AgentDefaultRisk = entity.AgentDefaultRisk,
            AgentLoopAutoProceed = entity.AgentLoopAutoProceed,
            AgentLoopMaxSteps = entity.AgentLoopMaxSteps,
            DefaultGenre = entity.DefaultGenre,
            DefaultChapterWordCount = entity.DefaultChapterWordCount,
            Theme = entity.Theme,
            Language = entity.Language
        };

        Normalize(settings);
        return settings;
    }

    private async Task<bool> TrySaveDatabaseSettingsAsync(UserSettings settings, CancellationToken ct)
    {
        var userId = TryGetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Database-backed user settings require an authenticated user and IServiceScopeFactory.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var entity = await db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, ct)
            .ConfigureAwait(false);

        if (entity == null)
        {
            entity = new TM.Web.NovelAgentWeb.Data.Entities.UserSettings
            {
                UserId = userId
            };
            db.UserSettings.Add(entity);
        }

        entity.LlmProvider = settings.LlmProvider;
        entity.LlmApiKeyEncrypted = _apiKeyProtector.Protect(settings.LlmApiKey);
        entity.LlmBaseUrl = settings.LlmBaseUrl;
        entity.LlmModel = settings.LlmModel;
        entity.LlmTemperature = (float)settings.LlmTemperature;
        entity.LlmMaxTokens = settings.LlmMaxTokens;
        entity.EmbeddingProvider = settings.EmbeddingProvider;
        entity.EmbeddingModel = settings.EmbeddingModel;
        entity.AgentDefaultRisk = settings.AgentDefaultRisk;
        entity.AgentLoopAutoProceed = settings.AgentLoopAutoProceed;
        entity.AgentLoopMaxSteps = settings.AgentLoopMaxSteps;
        entity.DefaultGenre = settings.DefaultGenre;
        entity.DefaultChapterWordCount = settings.DefaultChapterWordCount;
        entity.Theme = settings.Theme;
        entity.Language = settings.Language;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private string? TryGetCurrentUserId()
    {
        if (_backgroundUserContext.Current is { } background)
            return background.UserId;

        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

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
        settings.AgentLoopMaxSteps = Math.Clamp(settings.AgentLoopMaxSteps <= 0 ? defaults.AgentLoopMaxSteps : settings.AgentLoopMaxSteps, 1, 100);

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
