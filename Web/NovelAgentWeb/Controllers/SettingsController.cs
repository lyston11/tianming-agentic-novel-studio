using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class SettingsController : ControllerBase
{
    private readonly UserSettingsManager _settingsManager;

    public SettingsController(UserSettingsManager settingsManager) => _settingsManager = settingsManager;

    [HttpGet("settings")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct);
        return Ok(ToClientSettings(settings));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Save([FromBody] UserSettings settings, CancellationToken ct)
    {
        // Don't overwrite API keys with masked values
        var existing = await _settingsManager.LoadAsync(ct);
        if (IsMasked(settings.LlmApiKey)) settings.LlmApiKey = existing.LlmApiKey;
        if (IsMasked(settings.EmbeddingApiKey)) settings.EmbeddingApiKey = existing.EmbeddingApiKey;

        await _settingsManager.SaveAsync(settings, ct);
        return Ok(new { success = true, message = "设置已保存" });
    }

    [HttpPost("settings/reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        var settings = await _settingsManager.ResetAsync(ct);
        return Ok(ToClientSettings(settings));
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

            var settings = await _settingsManager.LoadAsync(ct);
            var apiKey = IsMasked(request.ApiKey) ? settings.LlmApiKey : request.ApiKey;
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

    private static object ToClientSettings(UserSettings settings) => new
    {
        settings.LlmProvider,
        LlmApiKey = MaskKey(settings.LlmApiKey),
        settings.LlmBaseUrl,
        settings.LlmModel,
        settings.LlmTemperature,
        settings.LlmMaxTokens,
        settings.EmbeddingProvider,
        EmbeddingApiKey = MaskKey(settings.EmbeddingApiKey),
        settings.EmbeddingBaseUrl,
        settings.EmbeddingModel,
        settings.AgentDefaultRisk,
        settings.AgentAutoContinue,
        settings.AgentMaxAutoSteps,
        settings.DefaultGenre,
        settings.DefaultSubGenre,
        settings.DefaultChapterWordCount,
        settings.DefaultVolumeChapterCount,
        settings.Theme,
        settings.Language,
        settings.ShowStepDetails,
        settings.Presets,
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
