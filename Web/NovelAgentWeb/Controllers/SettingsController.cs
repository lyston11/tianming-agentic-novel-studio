using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Settings;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILlmApiKeyProtector _apiKeyProtector;
    private readonly ILlmConnectionHealthService _llmConnectionHealth;

    public SettingsController(
        IAuthService authService,
        ICurrentUserService currentUserService,
        ILlmApiKeyProtector apiKeyProtector,
        ILlmConnectionHealthService llmConnectionHealth)
    {
        _authService = authService;
        _currentUserService = currentUserService;
        _apiKeyProtector = apiKeyProtector;
        _llmConnectionHealth = llmConnectionHealth;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var settings = await _authService.GetUserSettingsAsync(userId, ct);

        if (settings == null)
        {
            return NotFound(ApiErrors.NotFound("用户设置未找到"));
        }

        return Ok(ToClientSettings(settings));
    }

    [HttpGet("settings/llm-health")]
    public async Task<IActionResult> GetLlmHealth(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var settings = await _authService.GetUserSettingsAsync(userId, ct);

        if (settings == null)
        {
            return NotFound(ApiErrors.NotFound("用户设置未找到"));
        }

        var result = await _llmConnectionHealth.CheckAsync(new LlmConnectionHealthInput(
                Provider: settings.LlmProvider ?? string.Empty,
                BaseUrl: settings.LlmBaseUrl ?? string.Empty,
                Model: settings.LlmModel ?? string.Empty,
                ApiKey: _apiKeyProtector.Unprotect(settings.LlmApiKeyEncrypted)),
            ct);
        return Ok(result);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> Save([FromBody] UserSettingsDto dto, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var existing = await _authService.GetUserSettingsAsync(userId, ct);

        if (existing == null)
        {
            return NotFound(ApiErrors.NotFound("用户设置未找到"));
        }

        // Don't overwrite API keys with masked values
        if (!IsMasked(dto.LlmApiKey))
        {
            existing.LlmApiKeyEncrypted = _apiKeyProtector.Protect(dto.LlmApiKey);
        }

        // Update fields
        existing.LlmProvider = dto.LlmProvider;
        existing.LlmBaseUrl = dto.LlmBaseUrl;
        existing.LlmModel = dto.LlmModel;
        existing.LlmTemperature = (float)dto.LlmTemperature;
        existing.LlmMaxTokens = dto.LlmMaxTokens;
        existing.EmbeddingProvider = dto.EmbeddingProvider;
        existing.EmbeddingModel = dto.EmbeddingModel;
        existing.AgentDefaultRisk = dto.AgentDefaultRisk;
        existing.AgentLoopAutoProceed = dto.AgentLoopAutoProceed;
        existing.AgentLoopMaxSteps = dto.AgentLoopMaxSteps;
        existing.DefaultGenre = dto.DefaultGenre;
        existing.DefaultChapterWordCount = dto.DefaultChapterWordCount;
        existing.Theme = dto.Theme;
        existing.Language = dto.Language;

        await _authService.UpdateUserSettingsAsync(existing, ct);
        return Ok(ToClientSettings(existing));
    }

    [HttpPost("settings/reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var existing = await _authService.GetUserSettingsAsync(userId, ct);

        if (existing == null)
        {
            return NotFound(ApiErrors.NotFound("用户设置未找到"));
        }

        // Reset to defaults
        existing.LlmProvider = null;
        existing.LlmApiKeyEncrypted = null;
        existing.LlmBaseUrl = null;
        existing.LlmModel = null;
        existing.LlmTemperature = 0.7f;
        existing.LlmMaxTokens = 4096;
        existing.EmbeddingProvider = "local";
        existing.EmbeddingModel = "bge-small-zh-v1.5";
        existing.AgentDefaultRisk = "Medium";
        existing.AgentLoopAutoProceed = true;
        existing.AgentLoopMaxSteps = 12;
        existing.DefaultGenre = "玄幻";
        existing.DefaultChapterWordCount = 3000;
        existing.Theme = "dark";
        existing.Language = "zh-CN";

        await _authService.UpdateUserSettingsAsync(existing, ct);
        return Ok(ToClientSettings(existing));
    }

    [HttpPost("settings/test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl))
                return Ok(new { success = false, message = "请先填写 API Base URL。" });

            if (string.IsNullOrWhiteSpace(request.Model))
                return Ok(new { success = false, message = "请先填写模型名称。" });

            var userId = _currentUserService.GetUserId();
            var settings = await _authService.GetUserSettingsAsync(userId, ct);
            var apiKey = IsMasked(request.ApiKey)
                ? _apiKeyProtector.Unprotect(settings?.LlmApiKeyEncrypted)
                : request.ApiKey;
            if (RequiresApiKey(request.Provider) && string.IsNullOrWhiteSpace(apiKey))
                return Ok(new { success = false, message = "请先填写 API Key。" });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (string.Equals(request.Provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                http.DefaultRequestHeaders.Add("api-key", apiKey);
                http.DefaultRequestHeaders.Add("x-api-key", apiKey);
                http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else if (!string.IsNullOrWhiteSpace(apiKey))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var (url, payload) = BuildTestPayload(request);

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            var response = await http.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode,
                message = response.IsSuccessStatusCode
                    ? "连接成功，模型接口可用。"
                    : $"模型接口返回 {(int)response.StatusCode}: {TrimBody(body)}",
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = $"连接失败: {ex.Message}" });
        }
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        if (key.Length <= 8) return "****";
        return key[..4] + "****" + key[^4..];
    }

    private static bool IsMasked(string? key) =>
        key != null && key.Contains("****");

    private object ToClientSettings(UserSettings settings) => new
    {
        settings.LlmProvider,
        LlmApiKey = MaskKey(_apiKeyProtector.Unprotect(settings.LlmApiKeyEncrypted)),
        settings.LlmBaseUrl,
        settings.LlmModel,
        settings.LlmTemperature,
        settings.LlmMaxTokens,
        settings.EmbeddingProvider,
        settings.EmbeddingModel,
        settings.AgentDefaultRisk,
        settings.AgentLoopAutoProceed,
        settings.AgentLoopMaxSteps,
        settings.DefaultGenre,
        settings.DefaultChapterWordCount,
        settings.Theme,
        settings.Language,
    };

    private static bool RequiresApiKey(string? provider) =>
        !string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase);

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{url}/chat/completions";
    }

    private static (string Url, object Payload) BuildTestPayload(TestConnectionRequest request)
    {
        if (string.Equals(request.Provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return (BuildAnthropicMessagesUrl(request.BaseUrl), new
            {
                model = NormalizeProviderModelId(request.Model),
                max_tokens = 4,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { type = "text", text = "ping" }
                        }
                    }
                }
            });
        }

        return (BuildChatCompletionsUrl(request.BaseUrl), new
        {
            model = NormalizeProviderModelId(request.Model),
            messages = new[]
            {
                new { role = "user", content = "ping" }
            },
            max_tokens = 4,
            temperature = 0
        });
    }

    private static string BuildAnthropicMessagesUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{url}/messages";
        return $"{url}/v1/messages";
    }

    private static string NormalizeProviderModelId(string model)
    {
        var value = model.Trim();
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "无响应正文";
        var compact = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 180 ? compact : compact[..180] + "...";
    }
}

public sealed record TestConnectionRequest(
    string Provider = "",
    string BaseUrl = "",
    string ApiKey = "",
    string Model = "");

public sealed record UserSettingsDto
{
    public string? LlmProvider { get; set; }
    public string? LlmApiKey { get; set; }
    public string? LlmBaseUrl { get; set; }
    public string? LlmModel { get; set; }
    public double LlmTemperature { get; set; } = 0.7;
    public int LlmMaxTokens { get; set; } = 4096;
    public string EmbeddingProvider { get; set; } = "local";
    public string EmbeddingModel { get; set; } = "bge-small-zh-v1.5";
    public string AgentDefaultRisk { get; set; } = "Medium";
    public bool AgentLoopAutoProceed { get; set; } = true;
    public int AgentLoopMaxSteps { get; set; } = 12;
    public string DefaultGenre { get; set; } = "玄幻";
    public int DefaultChapterWordCount { get; set; } = 3000;
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh-CN";
}
