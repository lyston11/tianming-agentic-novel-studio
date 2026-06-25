using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class DefaultWritingModelCompletionService : IWritingModelCompletionService
{
    private readonly UserSettingsManager _settingsManager;
    private readonly IBackgroundUserContext _backgroundUserContext;
    private readonly IHttpClientFactory _httpClientFactory;

    public DefaultWritingModelCompletionService(
        UserSettingsManager settingsManager,
        IBackgroundUserContext backgroundUserContext,
        IHttpClientFactory httpClientFactory)
    {
        _settingsManager = settingsManager;
        _backgroundUserContext = backgroundUserContext;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> CompleteAsync(
        string userId,
        string system,
        string user,
        CancellationToken ct = default)
    {
        using var _ = _backgroundUserContext.Push(userId);
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            throw new InvalidOperationException("模型配置不完整，无法执行章节连续性事实沉淀。");
        }

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        var provider = settings.LlmProvider ?? string.Empty;
        var baseUrl = settings.LlmBaseUrl.TrimEnd('/');
        var model = NormalizeProviderModelId(settings.LlmModel);

        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            http.DefaultRequestHeaders.Add("api-key", settings.LlmApiKey);
            http.DefaultRequestHeaders.Add("x-api-key", settings.LlmApiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            var anthropicPayload = new
            {
                model,
                system,
                max_tokens = NormalizeMaxTokens(settings.LlmMaxTokens),
                temperature = settings.LlmTemperature,
                messages = new[] { new { role = "user", content = user } }
            };
            using var response = await http.PostAsync(
                    BuildAnthropicMessagesUrl(baseUrl),
                    new StringContent(JsonSerializer.Serialize(anthropicPayload), Encoding.UTF8, "application/json"),
                    ct)
                .ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"写作模型接口返回 {(int)response.StatusCode}: {text}");

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array)
            {
                return string.Join("\n", content.EnumerateArray()
                    .Select(item => item.TryGetProperty("text", out var t) ? t.GetString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            return text;
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.LlmApiKey);
        var payload = new
        {
            model,
            temperature = settings.LlmTemperature,
            max_tokens = NormalizeMaxTokens(settings.LlmMaxTokens),
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        var url = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";
        using var openAiResponse = await http.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                ct)
            .ConfigureAwait(false);
        var json = await openAiResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!openAiResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"写作模型接口返回 {(int)openAiResponse.StatusCode}: {json}");

        using var openAiDoc = JsonDocument.Parse(json);
        return openAiDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? json;
    }

    private static string BuildAnthropicMessagesUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{url}/messages";
        return $"{url}/v1/messages";
    }

    private static string NormalizeProviderModelId(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            value = value["models/".Length..];
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static int NormalizeMaxTokens(int maxTokens) =>
        Math.Clamp(maxTokens <= 0 ? 4096 : maxTokens, 256, 200000);
}
