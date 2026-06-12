using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;

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
    private readonly ILogger<KnowledgeProcessingService> _logger;

    public KnowledgeProcessingService(
        NovelAgentDbContext db,
        IKnowledgeService knowledgeService,
        IMicroEmbeddingService embedding,
        ILogger<KnowledgeProcessingService> logger)
    {
        _db = db;
        _knowledgeService = knowledgeService;
        _embedding = embedding;
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
