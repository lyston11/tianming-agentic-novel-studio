using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Memory;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KnowledgeProcessingService> _logger;
    private readonly IAgentMemoryEventService? _memoryEvents;
    private readonly IContentDocumentService _contentDocuments;
    private readonly IAgentMemoryRepository? _memoryRepository;

    public KnowledgeProcessingService(
        NovelAgentDbContext db,
        IKnowledgeService knowledgeService,
        IMicroEmbeddingService embedding,
        UserSettingsManager settingsManager,
        IHttpClientFactory httpClientFactory,
        ILogger<KnowledgeProcessingService> logger,
        IContentDocumentService contentDocuments,
        IAgentMemoryEventService? memoryEvents = null,
        IAgentMemoryRepository? memoryRepository = null)
    {
        _db = db;
        _knowledgeService = knowledgeService;
        _embedding = embedding;
        _settingsManager = settingsManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _memoryEvents = memoryEvents;
        _contentDocuments = contentDocuments;
        _memoryRepository = memoryRepository;
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
            var content = await _contentDocuments.GetTextAsync(task.UserId, task.ProjectId, "knowledge_upload", task.Id, "upload_raw", ct);
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
            await RecordProcessingFailureMemoryAsync(task, ex, ct);
            throw;
        }
    }

    private async Task RecordProcessingFailureMemoryAsync(
        Data.Entities.KnowledgeProcessingTask task,
        Exception exception,
        CancellationToken ct)
    {
        if (_memoryRepository == null || string.IsNullOrWhiteSpace(task.ProjectId))
        {
            return;
        }

        try
        {
            var note = $"{task.Id}: {task.FileName}: {exception.Message}";
            await _memoryRepository.UnionMemoryAsync(
                    task.UserId,
                    task.ProjectId,
                    new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["execution.knowledge_processing_failures"] = new[] { note }
                    },
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception memoryEx) when (memoryEx is not OperationCanceledException)
        {
            _logger.LogWarning(
                memoryEx,
                "Failed to record knowledge processing failure memory for task {TaskId}",
                task.Id);
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

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(90);

        var provider = settings.LlmProvider.Trim().ToLowerInvariant();

        // Use Anthropic-style API for Anthropic and Mimo providers
        if (provider.Contains("anthropic", StringComparison.Ordinal) ||
            provider.Contains("mimo", StringComparison.Ordinal))
        {
            return await CallAnthropicCompletionAsync(httpClient, settings, prompt, ct);
        }

        // Default to OpenAI-compatible API for all other providers
        return await CallOpenAiCompletionAsync(httpClient, settings, prompt, ct);
    }

    /// <summary>
    /// Calls OpenAI-compatible completion API.
    /// </summary>
    private async Task<string> CallOpenAiCompletionAsync(HttpClient httpClient, UserSettings settings, string prompt, CancellationToken ct)
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

        using var response = await httpClient.SendAsync(request, ct);
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
    private async Task<string> CallAnthropicCompletionAsync(HttpClient httpClient, UserSettings settings, string prompt, CancellationToken ct)
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

        using var response = await httpClient.SendAsync(request, ct);
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
            if (cleaned.StartsWith("```json") && cleaned.Length > 7)
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```") && cleaned.Length > 3)
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
    /// Splits the file into overlapping chunks, analyzes each chunk with context from the previous chunk,
    /// and aggregates all entries at the end.
    /// </summary>
    private async Task<List<ExtractedKnowledgeEntryDto>> ProcessLongFileAsync(
        string content,
        Data.Entities.KnowledgeProcessingTask task,
        CancellationToken ct)
    {
        var chunks = ChunkText(content, ChunkSize, ChunkOverlap);
        task.TotalChunks = chunks.Count;
        await _db.SaveChangesAsync(ct);

        var allEntries = new List<ExtractedKnowledgeEntryDto>();
        string previousSummary = "";

        for (int i = 0; i < chunks.Count; i++)
        {
            var prompt = BuildChunkedPrompt(chunks[i], i + 1, chunks.Count, previousSummary);
            var llmResponse = await CallLLMAsync(prompt, ct);
            var result = ParseChunkedResult(llmResponse);

            allEntries.AddRange(result.Entries);
            previousSummary = result.Summary;

            task.ProcessedChunks = i + 1;
            task.Progress = (int)((i + 1) * 80.0 / chunks.Count);
            await _db.SaveChangesAsync(ct);
        }

        var aggregated = await AggregateEntriesAsync(allEntries, ct);
        task.Progress = 100;
        await _db.SaveChangesAsync(ct);

        return aggregated;
    }

    /// <summary>
    /// Splits text into overlapping chunks for processing.
    /// </summary>
    private List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));
            start += chunkSize - overlap;
            if (start >= text.Length) break;
        }

        return chunks;
    }

    /// <summary>
    /// Builds the prompt for analyzing a single chunk of a long file.
    /// Includes context from the previous chunk's summary.
    /// </summary>
    private string BuildChunkedPrompt(string chunk, int index, int total, string previousSummary)
    {
        var contextPart = index > 1
            ? $"\n**上一块摘要**：\n{previousSummary}\n"
            : "";

        return $@"你是创意写作知识提取专家。这是一个长文档的第 {index}/{total} 块。

**当前块内容**：
{chunk}
{contextPart}
提取本块中的写作知识条目，并生成本块摘要（100字内）用于下一块上下文。

**输出格式**（JSON对象）：
{{
  ""entries"": [
    {{
      ""title"": ""简短标题"",
      ""category"": ""GenrePrinciple|TropePattern|AntiTropeStrategy|StyleExample"",
      ""content"": ""详细说明"",
      ""tags"": [""标签1""],
      ""weight"": 5,
      ""originalText"": ""原文引用""
    }}
  ],
  ""summary"": ""本块内容摘要，包含关键主题、人物、技巧""
}}

只返回JSON对象，不要其他文字。";
    }

    /// <summary>
    /// Parses the LLM response for a single chunk into structured result.
    /// Handles markdown code fences and returns empty result on parse failure.
    /// </summary>
    private ChunkedAnalysisResultDto ParseChunkedResult(string jsonResponse)
    {
        try
        {
            var cleaned = jsonResponse.Trim();
            if (cleaned.StartsWith("```json") && cleaned.Length > 7)
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            var result = JsonSerializer.Deserialize<ChunkedAnalysisResultDto>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new ChunkedAnalysisResultDto();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse chunked result: {Response}", jsonResponse);
            return new ChunkedAnalysisResultDto();
        }
    }

    /// <summary>
    /// Aggregates knowledge entries from all chunks using LLM.
    /// Performs deduplication and cross-chunk aggregation to extract higher-level patterns.
    /// </summary>
    private async Task<List<ExtractedKnowledgeEntryDto>> AggregateEntriesAsync(
        List<ExtractedKnowledgeEntryDto> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0) return entries;

        var prompt = BuildAggregationPrompt(entries);
        var llmResponse = await CallLLMAsync(prompt, ct);
        var result = ParseAggregatedResult(llmResponse);

        var final = new List<ExtractedKnowledgeEntryDto>();
        final.AddRange(result.Deduplicated);
        final.AddRange(result.Aggregated);

        return final;
    }

    /// <summary>
    /// Builds the prompt for aggregating entries from all chunks.
    /// Instructs the LLM to deduplicate and identify cross-chunk patterns.
    /// </summary>
    private string BuildAggregationPrompt(List<ExtractedKnowledgeEntryDto> entries)
    {
        var entriesJson = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return $@"你是创意写作知识提取专家。已完成分块分析，现在需要跨块聚合。

**各块提取的条目**（JSON数组）：
{entriesJson}

**任务**：
1. 识别重复或相似条目，合并去重
2. 提取跨块的共性主题、技巧模式
3. 生成全文级别的高阶知识条目（如整体风格特征、叙事结构规律）

**输出格式**：
{{
  ""deduplicated"": [
    {{
      ""title"": ""..."",
      ""category"": ""..."",
      ""content"": ""..."",
      ""tags"": [...],
      ""weight"": 5
    }}
  ],
  ""aggregated"": [
    {{
      ""title"": ""全文级知识标题"",
      ""category"": ""..."",
      ""content"": ""跨块归纳的高阶规律"",
      ""tags"": [...],
      ""weight"": 8
    }}
  ]
}}

只返回JSON对象，不要其他文字。";
    }

    /// <summary>
    /// Parses the LLM response for aggregated results into structured result.
    /// Handles markdown code fences and returns empty result on parse failure.
    /// </summary>
    private AggregatedAnalysisResultDto ParseAggregatedResult(string jsonResponse)
    {
        try
        {
            var cleaned = jsonResponse.Trim();
            if (cleaned.StartsWith("```json") && cleaned.Length > 7)
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            var result = JsonSerializer.Deserialize<AggregatedAnalysisResultDto>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new AggregatedAnalysisResultDto();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse aggregated result: {Response}", jsonResponse);
            return new AggregatedAnalysisResultDto();
        }
    }

    /// <summary>
    /// Saves extracted knowledge entries to the database and vector store.
    /// Each entry is saved with source tracking information for traceability.
    /// </summary>
    private async Task SaveExtractedEntriesAsync(
        Data.Entities.KnowledgeProcessingTask task,
        List<ExtractedKnowledgeEntryDto> entries,
        CancellationToken ct)
    {
        var createdIds = new List<string>();
        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            var created = await _knowledgeService.CreateExtractedKnowledgeAsync(new CreateExtractedKnowledgeRequest
            {
                ProjectId = task.ProjectId,
                EntryType = entry.Category,
                Title = entry.Title,
                Content = entry.Content,
                Tags = entry.Tags,
                Weight = entry.Weight,
                SourceUploadTaskId = task.Id,
                ChunkIndex = index,
                ExtractionContext = entry.OriginalText
            }, ct);
            createdIds.Add(created.Id);
        }

        if (_memoryEvents != null && createdIds.Count > 0)
        {
            await _memoryEvents.AppendAsync(
                task.UserId,
                task.ProjectId,
                null,
                null,
                "knowledge_processed",
                "file_processed",
                "knowledge",
                "processed_knowledge_ids",
                new { taskId = task.Id, knowledgeIds = createdIds },
                ct);
        }
    }

}
