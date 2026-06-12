using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service for processing knowledge files and extracting knowledge entries.
/// Supports both short files (single-pass analysis) and long files (chunked processing).
/// </summary>
public class KnowledgeProcessingService : IKnowledgeProcessingService
{
    private const int ShortFileTokenThreshold = 6000;
    private const int ChunkSize = 4000;
    private const int ChunkOverlap = 200;

    private readonly NovelAgentDbContext _db;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IMicroEmbeddingService _embedding;
    private readonly UserSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private readonly ILogger<KnowledgeProcessingService> _logger;

    public KnowledgeProcessingService(
        NovelAgentDbContext db,
        IKnowledgeService knowledgeService,
        IMicroEmbeddingService embedding,
        UserSettingsManager settingsManager,
        HttpClient httpClient,
        ILogger<KnowledgeProcessingService> logger)
    {
        _db = db;
        _knowledgeService = knowledgeService;
        _embedding = embedding;
        _settingsManager = settingsManager;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Processes a knowledge file by task ID.
    /// Automatically chooses single-pass or chunked strategy based on file size.
    /// </summary>
    public async Task<string> ProcessFileAsync(string taskId, CancellationToken ct = default)
    {
        var task = await _db.KnowledgeProcessingTasks.FindAsync(new object[] { taskId }, ct);
        if (task == null) throw new InvalidOperationException("Task not found");

        task.Status = "processing";
        task.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            var content = await File.ReadAllTextAsync(task.FilePath, ct);
            var tokenCount = EstimateTokenCount(content);

            List<ExtractedKnowledgeEntryDto> entries;

            if (tokenCount < ShortFileTokenThreshold)
            {
                task.Strategy = "single_pass";
                await _db.SaveChangesAsync(ct);
                entries = await ProcessShortFileAsync(content, ct);
            }
            else
            {
                task.Strategy = "chunked";
                await _db.SaveChangesAsync(ct);
                entries = await ProcessLongFileAsync(content, task, ct);
            }

            await SaveExtractedEntriesAsync(task, entries, ct);

            task.Status = "completed";
            task.Progress = 100;
            task.ExtractedEntriesCount = entries.Count;
            task.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return $"成功提取 {entries.Count} 条知识条目";
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Estimates token count using simple character-based heuristic.
    /// Chinese text: ~1.3 characters per token, so multiply by 0.75.
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        return (int)(text.Length * 0.75);
    }

    /// <summary>
    /// Processes a short file in a single LLM call.
    /// </summary>
    private async Task<List<ExtractedKnowledgeEntryDto>> ProcessShortFileAsync(
        string content,
        CancellationToken ct)
    {
        var prompt = BuildShortFilePrompt(content);
        var llmResponse = await CallLLMAsync(prompt, ct);
        return ParseEntriesFromJson(llmResponse);
    }

    /// <summary>
    /// Builds the prompt for short file analysis.
    /// Instructs the LLM to extract 3-10 knowledge entries in JSON format.
    /// </summary>
    private string BuildShortFilePrompt(string content)
    {
        return $@"你是一个创意写作知识提取专家。分析以下文本，提取可用于小说写作的知识条目。

**文本内容**：
{content}

**提取要求**：
1. **类型原则**(GenrePrinciple): 特定题材的写作规则、读者期待、禁忌
2. **套路警告**(TropePattern): 常见俗套桥段、老梗、容易引起反感的模式
3. **反套路策略**(AntiTropeStrategy): 避免套路的技巧、创新手法
4. **风格示例**(StyleExample): 值得学习的叙述风格、对话技巧、节奏控制

**输出格式**（JSON数组）：
[
  {{
    ""title"": ""简短标题（10字内)"",
    ""category"": ""GenrePrinciple|TropePattern|AntiTropeStrategy|StyleExample"",
    ""content"": ""详细说明（50-200字）"",
    ""tags"": [""标签1"", ""标签2""],
    ""weight"": 5,
    ""originalText"": ""原文引用片段（可选）""
  }}
]

提取3-10条最有价值的知识条目，确保每条都实用、具体、可操作。只返回JSON数组，不要其他文字。";
    }

    /// <summary>
    /// Calls the LLM with the given prompt using user settings.
    /// </summary>
    private async Task<string> CallLLMAsync(string prompt, CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            throw new InvalidOperationException("LLM settings are not configured");
        }

        var provider = settings.LlmProvider.Trim().ToLowerInvariant();

        // Use Anthropic-style API for Anthropic and Mimo providers
        if (provider.Contains("anthropic", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("mimo", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("xiaomi", StringComparison.OrdinalIgnoreCase))
        {
            return await CallAnthropicCompletionAsync(settings, prompt, ct);
        }

        // Default to OpenAI-compatible API for all other providers
        return await CallOpenAiCompletionAsync(settings, prompt, ct);
    }

    /// <summary>
    /// Calls OpenAI-compatible completion API.
    /// </summary>
    private async Task<string> CallOpenAiCompletionAsync(UserSettings settings, string prompt, CancellationToken ct)
    {
        var url = BuildOpenAiChatCompletionsUrl(settings.LlmBaseUrl);
        var payload = new
        {
            model = NormalizeModel(settings.LlmModel),
            temperature = settings.LlmTemperature,
            max_tokens = settings.LlmMaxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.LlmApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM API returned {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Invalid OpenAI API response format");
        }

        var firstChoice = choices.EnumerateArray().FirstOrDefault();
        if (firstChoice.ValueKind == JsonValueKind.Undefined ||
            !firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            throw new InvalidOperationException("No content in OpenAI API response");
        }

        return content.GetString() ?? string.Empty;
    }

    /// <summary>
    /// Calls Anthropic-compatible completion API.
    /// </summary>
    private async Task<string> CallAnthropicCompletionAsync(UserSettings settings, string prompt, CancellationToken ct)
    {
        var url = BuildAnthropicMessagesUrl(settings.LlmBaseUrl);
        var payload = new
        {
            model = NormalizeModel(settings.LlmModel),
            max_tokens = settings.LlmMaxTokens,
            temperature = settings.LlmTemperature,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "text", text = prompt } }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", settings.LlmApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic API returned {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Invalid Anthropic API response format");
        }

        var textParts = content.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) &&
                          type.ValueKind == JsonValueKind.String &&
                          type.GetString() == "text")
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join("\n", textParts);
    }

    /// <summary>
    /// Parses JSON response from LLM into a list of knowledge entries.
    /// Handles markdown code fences and returns empty list on parse failure.
    /// </summary>
    private List<ExtractedKnowledgeEntryDto> ParseEntriesFromJson(string jsonResponse)
    {
        try
        {
            // Clean up markdown code fences
            var cleaned = jsonResponse.Trim();
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            // Parse JSON array
            var entries = JsonSerializer.Deserialize<List<ExtractedKnowledgeEntryDto>>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return entries ?? new List<ExtractedKnowledgeEntryDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM JSON response: {Response}", jsonResponse);
            return new List<ExtractedKnowledgeEntryDto>();
        }
    }

    /// <summary>
    /// Builds OpenAI-compatible chat completions URL.
    /// </summary>
    private static string BuildOpenAiChatCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{url}/chat/completions";
    }

    /// <summary>
    /// Builds Anthropic messages API URL.
    /// </summary>
    private static string BuildAnthropicMessagesUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{url}/messages";
        }
        return $"{url}/v1/messages";
    }

    /// <summary>
    /// Normalizes model name by removing suffixes.
    /// </summary>
    private static string NormalizeModel(string model)
    {
        var value = model.Trim();
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4].Trim();
        }
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^9].Trim();
        }
        return value;
    }

    /// <summary>
    /// Processes a long file using chunked analysis.
    /// Implementation will be added in Task 6.
    /// </summary>
    private async Task<List<ExtractedKnowledgeEntryDto>> ProcessLongFileAsync(
        string content,
        Data.Entities.KnowledgeProcessingTask task,
        CancellationToken ct)
    {
        // Will be implemented in Task 6
        throw new NotImplementedException("Long file processing will be implemented in Task 6");
    }

    /// <summary>
    /// Saves extracted knowledge entries to the database and vector store.
    /// Implementation will be added in Task 7.
    /// </summary>
    private async Task SaveExtractedEntriesAsync(
        Data.Entities.KnowledgeProcessingTask task,
        List<ExtractedKnowledgeEntryDto> entries,
        CancellationToken ct)
    {
        // Will be implemented in Task 7
        throw new NotImplementedException("Saving extracted entries will be implemented in Task 7");
    }
}
