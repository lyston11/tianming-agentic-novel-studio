using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;
using TM.Web.NovelAgentWeb.Services.Models;
using TM.Web.NovelAgentWeb.Services.Execution;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class DefaultWritingModelCompletionService : IWritingModelCompletionService
{
    private readonly UserSettingsManager _settingsManager;
    private readonly IBackgroundUserContext _backgroundUserContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKernelModelConfigurationService _modelConfigurations;
    private readonly IKernelModelExecutionScopeAccessor _modelExecutionScopes;
    private readonly IGoalModelExecutionEnvelope _goalEnvelope;

    public DefaultWritingModelCompletionService(
        UserSettingsManager settingsManager,
        IBackgroundUserContext backgroundUserContext,
        IHttpClientFactory httpClientFactory,
        IKernelModelConfigurationService modelConfigurations,
        IKernelModelExecutionScopeAccessor modelExecutionScopes,
        IGoalModelExecutionEnvelope goalEnvelope)
    {
        _settingsManager = settingsManager;
        _backgroundUserContext = backgroundUserContext;
        _httpClientFactory = httpClientFactory;
        _modelConfigurations = modelConfigurations;
        _modelExecutionScopes = modelExecutionScopes;
        _goalEnvelope = goalEnvelope;
    }

    public async Task<string> CompleteAsync(
        string userId,
        string system,
        string user,
        CancellationToken ct = default)
    {
        var executionScope = _modelExecutionScopes.Current;
        if (executionScope != null)
        {
            if (!string.Equals(userId, executionScope.UserId, StringComparison.Ordinal))
                throw new InvalidOperationException("模型调用用户与当前 Kernel execution scope 不一致。");
            var resolved = await _modelConfigurations.ResolveAsync(
                executionScope.UserId,
                executionScope.ProjectId,
                executionScope.KernelName,
                "balanced",
                executionScope.GoalId,
                ct);
            var budgeted = await _goalEnvelope.ExecuteAsync(
                resolved,
                system,
                user,
                token => CompleteWithMetadataAsync(userId, resolved, system, user, token),
                ct);
            return budgeted.Text;
        }

        using var _ = _backgroundUserContext.Push(userId);
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var configuration = new ModelCallConfiguration(
            settings.LlmProvider,
            settings.LlmBaseUrl,
            settings.LlmModel,
            settings.LlmApiKey,
            (float)settings.LlmTemperature,
            settings.LlmMaxTokens,
            300,
            0,
            0);
        return (await CompleteCoreAsync(configuration, system, user, ct).ConfigureAwait(false)).Text;
    }

    public async Task<string> CompleteAsync(
        string userId,
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        CancellationToken ct = default)
        => (await CompleteWithMetadataAsync(userId, configuration, system, user, ct)
            .ConfigureAwait(false)).Text;

    public async Task<WritingModelCompletionResult> CompleteWithMetadataAsync(
        string userId,
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        CancellationToken ct = default)
    {
        using var _ = _backgroundUserContext.Push(userId);
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var primary = configuration.SourceLayer == "system"
            ? new ModelCallConfiguration(
                settings.LlmProvider,
                settings.LlmBaseUrl,
                settings.LlmModel,
                settings.LlmApiKey,
                configuration.Temperature,
                configuration.MaxOutputTokens,
                configuration.TimeoutSeconds,
                configuration.InputPricePerMillion,
                configuration.OutputPricePerMillion)
            : ToCallConfiguration(configuration, settings.LlmApiKey);
        var attempts = new List<ModelCallConfiguration> { primary };
        attempts.AddRange(configuration.Fallbacks.Select(fallback => new ModelCallConfiguration(
            fallback.Provider,
            fallback.BaseUrl ?? string.Empty,
            fallback.Model,
            ResolveCredential(fallback.CredentialReference, settings.LlmApiKey),
            configuration.Temperature,
            configuration.MaxOutputTokens,
            configuration.TimeoutSeconds,
            fallback.InputPricePerMillion,
            fallback.OutputPricePerMillion)));

        var errors = new List<string>();
        var hasOutcomeUnknown = false;
        foreach (var attempt in attempts)
        {
            try
            {
                return await CompleteCoreAsync(attempt, system, user, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ModelCallKnownFailureException exception)
            {
                errors.Add($"{attempt.Provider}/{attempt.Model}: {exception.Message}");
            }
            catch (Exception exception)
            {
                hasOutcomeUnknown = true;
                errors.Add($"{attempt.Provider}/{attempt.Model}: {exception.Message}");
            }
        }
        var message = $"逐内核模型及显式 fallback 均失败：{string.Join(" | ", errors)}";
        throw hasOutcomeUnknown
            ? new ModelCallOutcomeUnknownException(message)
            : new ModelCallKnownFailureException(message);
    }

    private async Task<WritingModelCompletionResult> CompleteCoreAsync(
        ModelCallConfiguration configuration,
        string system,
        string user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuration.BaseUrl) ||
            string.IsNullOrWhiteSpace(configuration.Model) ||
            (RequiresApiKey(configuration.Provider) && string.IsNullOrWhiteSpace(configuration.ApiKey)))
        {
            throw new ModelCallKnownFailureException("模型配置不完整，无法执行写作模型请求。");
        }

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds);
        var provider = configuration.Provider;
        var baseUrl = configuration.BaseUrl.TrimEnd('/');
        var model = WritingModelProviderResponseParser.NormalizeModelId(configuration.Model);

        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            http.DefaultRequestHeaders.Add("api-key", configuration.ApiKey);
            http.DefaultRequestHeaders.Add("x-api-key", configuration.ApiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            var anthropicPayload = new
            {
                model,
                system,
                max_tokens = NormalizeMaxTokens(configuration.MaxOutputTokens),
                temperature = configuration.Temperature,
                messages = new[] { new { role = "user", content = user } }
            };
            using var response = await http.PostAsync(
                    BuildAnthropicMessagesUrl(baseUrl),
                    new StringContent(JsonSerializer.Serialize(anthropicPayload), Encoding.UTF8, "application/json"),
                    ct)
                .ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ModelCallKnownFailureException($"写作模型接口返回 {(int)response.StatusCode}: {text}");

            return WritingModelProviderResponseParser.ParseAnthropic(
                text,
                configuration.Provider,
                configuration.Model,
                configuration.InputPricePerMillion,
                configuration.OutputPricePerMillion);
        }

        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        var payload = new
        {
            model,
            temperature = configuration.Temperature,
            max_tokens = NormalizeMaxTokens(configuration.MaxOutputTokens),
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
            throw new ModelCallKnownFailureException($"写作模型接口返回 {(int)openAiResponse.StatusCode}: {json}");

        return WritingModelProviderResponseParser.ParseOpenAi(
            json,
            configuration.Provider,
            configuration.Model,
            configuration.InputPricePerMillion,
            configuration.OutputPricePerMillion);
    }

    private static ModelCallConfiguration ToCallConfiguration(
        ResolvedKernelModelConfiguration configuration,
        string apiKey) => new(
            configuration.Provider,
            configuration.BaseUrl ?? string.Empty,
            configuration.Model,
            ResolveCredential(configuration.CredentialReference, apiKey),
            configuration.Temperature,
            configuration.MaxOutputTokens,
            configuration.TimeoutSeconds,
            configuration.InputPricePerMillion,
            configuration.OutputPricePerMillion);

    private static string ResolveCredential(string reference, string apiKey) =>
        string.Equals(reference, "user-settings:llm", StringComparison.Ordinal)
            ? apiKey
            : throw new ModelCallKnownFailureException("未知的模型凭据引用。");

    private static bool RequiresApiKey(string provider) =>
        !string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase);

    private sealed record ModelCallConfiguration(
        string Provider,
        string BaseUrl,
        string Model,
        string ApiKey,
        float Temperature,
        int MaxOutputTokens,
        int TimeoutSeconds,
        decimal InputPricePerMillion,
        decimal OutputPricePerMillion);

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

    private static int NormalizeMaxTokens(int maxTokens) =>
        Math.Clamp(maxTokens <= 0 ? 4096 : maxTokens, 256, 200000);
}
