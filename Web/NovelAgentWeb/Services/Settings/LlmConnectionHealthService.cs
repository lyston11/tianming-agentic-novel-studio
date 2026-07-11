using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TM.Web.NovelAgentWeb.Services.Settings;

public interface ILlmConnectionHealthService
{
    Task<LlmConnectionHealthResult> CheckAsync(
        LlmConnectionHealthInput input,
        CancellationToken cancellationToken = default);
}

public sealed class LlmConnectionHealthService : ILlmConnectionHealthService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmConnectionHealthService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<LlmConnectionHealthResult> CheckAsync(
        LlmConnectionHealthInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = input.Normalize();
        var missing = GetMissingConfiguration(normalized);
        if (missing.Count > 0)
        {
            return LlmConnectionHealthResult.MissingConfig(
                normalized,
                $"模型配置不完整：{string.Join("、", missing)}。",
                "请在用户设置中补全模型 Provider、Base URL、模型名称和 API Key。");
        }

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var (url, payload) = BuildProbeRequest(normalized);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyAuthHeaders(request, normalized);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return LlmConnectionHealthResult.Ready(normalized, (int)response.StatusCode);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return LlmConnectionHealthResult.AuthenticationFailed(
                    normalized,
                    (int)response.StatusCode,
                    "模型 API Key 不可用，可能已经过期、失效，或需要重新保存。",
                    "请在用户设置中重新粘贴并保存 API Key，然后重新检测模型连接。");
            }

            return LlmConnectionHealthResult.ProviderError(
                normalized,
                (int)response.StatusCode,
                $"模型接口返回 {(int)response.StatusCode}：{TrimBody(Sanitize(body, normalized.ApiKey))}",
                "请检查模型服务状态、Base URL、模型名称和供应商兼容协议。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LlmConnectionHealthResult.ConnectionFailed(
                normalized,
                "模型接口连接超时。",
                "请检查 Base URL 是否可访问，或稍后重试。");
        }
        catch (HttpRequestException ex)
        {
            return LlmConnectionHealthResult.ConnectionFailed(
                normalized,
                $"模型接口连接失败：{TrimBody(Sanitize(ex.Message, normalized.ApiKey))}",
                "请检查网络、代理、Base URL 和模型供应商服务状态。");
        }
        catch (Exception ex)
        {
            return LlmConnectionHealthResult.ConnectionFailed(
                normalized,
                $"模型健康检查失败：{TrimBody(Sanitize(ex.Message, normalized.ApiKey))}",
                "请检查模型配置后重试。");
        }
    }

    private static List<string> GetMissingConfiguration(LlmConnectionHealthInput input)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Provider))
            missing.Add("Provider");
        if (string.IsNullOrWhiteSpace(input.BaseUrl))
            missing.Add("Base URL");
        if (string.IsNullOrWhiteSpace(input.Model))
            missing.Add("模型名称");
        if (RequiresApiKey(input.Provider) && string.IsNullOrWhiteSpace(input.ApiKey))
            missing.Add("API Key");
        return missing;
    }

    private static (string Url, object Payload) BuildProbeRequest(LlmConnectionHealthInput input)
    {
        if (IsAnthropicCompatible(input.Provider))
        {
            return (BuildAnthropicMessagesUrl(input.BaseUrl), new
            {
                model = NormalizeProviderModelId(input.Model),
                max_tokens = 4,
                temperature = 0,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = "ping" } }
                    }
                }
            });
        }

        return (BuildChatCompletionsUrl(input.BaseUrl), new
        {
            model = NormalizeProviderModelId(input.Model),
            messages = new[] { new { role = "user", content = "ping" } },
            max_tokens = 4,
            temperature = 0
        });
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, LlmConnectionHealthInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ApiKey))
            return;

        if (IsAnthropicCompatible(input.Provider))
        {
            request.Headers.Add("api-key", input.ApiKey);
            request.Headers.Add("x-api-key", input.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.ApiKey);
    }

    private static bool RequiresApiKey(string? provider) =>
        !string.Equals(provider?.Trim(), "ollama", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnthropicCompatible(string? provider)
    {
        var value = provider?.Trim() ?? string.Empty;
        return value.Contains("anthropic", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("mimo", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("xiaomi", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{url}/chat/completions";
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
        if (value.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            value = value["models/".Length..];
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static string Sanitize(string value, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return value;

        return value.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "无响应正文";

        var compact = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 240 ? compact : compact[..240] + "...";
    }
}

public sealed record LlmConnectionHealthInput(
    string Provider,
    string BaseUrl,
    string Model,
    string ApiKey)
{
    public LlmConnectionHealthInput Normalize() => new(
        Provider?.Trim() ?? string.Empty,
        BaseUrl?.Trim() ?? string.Empty,
        Model?.Trim() ?? string.Empty,
        ApiKey?.Trim() ?? string.Empty);
}

public sealed record LlmConnectionHealthResult
{
    public string Status { get; init; } = "unknown";
    public bool IsConfigured { get; init; }
    public bool IsReachable { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool RequiresUserAction { get; init; }
    public int? StatusCode { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string FailureStage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;

    public static LlmConnectionHealthResult Ready(LlmConnectionHealthInput input, int statusCode) => From(
        input,
        status: "ready",
        configured: true,
        reachable: true,
        authenticated: true,
        requiresUserAction: false,
        statusCode: statusCode,
        failureStage: "",
        message: "模型接口可用。",
        recommendedAction: "可以继续执行 Agent 生产链路。");

    public static LlmConnectionHealthResult MissingConfig(
        LlmConnectionHealthInput input,
        string message,
        string recommendedAction) => From(
        input,
        status: "missing_config",
        configured: false,
        reachable: false,
        authenticated: false,
        requiresUserAction: true,
        statusCode: null,
        failureStage: "config",
        message: message,
        recommendedAction: recommendedAction);

    public static LlmConnectionHealthResult ApiKeyUnavailable(
        LlmConnectionHealthInput input,
        string message,
        string recommendedAction) => From(
        input,
        status: "api_key_unavailable",
        configured: false,
        reachable: false,
        authenticated: false,
        requiresUserAction: true,
        statusCode: null,
        failureStage: "config",
        message: message,
        recommendedAction: recommendedAction);

    public static LlmConnectionHealthResult AuthenticationFailed(
        LlmConnectionHealthInput input,
        int statusCode,
        string message,
        string recommendedAction) => From(
        input,
        status: "authentication_failed",
        configured: true,
        reachable: true,
        authenticated: false,
        requiresUserAction: true,
        statusCode: statusCode,
        failureStage: "authentication",
        message: message,
        recommendedAction: recommendedAction);

    public static LlmConnectionHealthResult ProviderError(
        LlmConnectionHealthInput input,
        int statusCode,
        string message,
        string recommendedAction) => From(
        input,
        status: "provider_error",
        configured: true,
        reachable: true,
        authenticated: true,
        requiresUserAction: true,
        statusCode: statusCode,
        failureStage: "provider",
        message: message,
        recommendedAction: recommendedAction);

    public static LlmConnectionHealthResult ConnectionFailed(
        LlmConnectionHealthInput input,
        string message,
        string recommendedAction) => From(
        input,
        status: "connection_failed",
        configured: true,
        reachable: false,
        authenticated: false,
        requiresUserAction: true,
        statusCode: null,
        failureStage: "network",
        message: message,
        recommendedAction: recommendedAction);

    private static LlmConnectionHealthResult From(
        LlmConnectionHealthInput input,
        string status,
        bool configured,
        bool reachable,
        bool authenticated,
        bool requiresUserAction,
        int? statusCode,
        string failureStage,
        string message,
        string recommendedAction) => new()
    {
        Status = status,
        IsConfigured = configured,
        IsReachable = reachable,
        IsAuthenticated = authenticated,
        RequiresUserAction = requiresUserAction,
        StatusCode = statusCode,
        Provider = input.Provider,
        Model = input.Model,
        BaseUrl = input.BaseUrl,
        FailureStage = failureStage,
        Message = message,
        RecommendedAction = recommendedAction
    };
}
