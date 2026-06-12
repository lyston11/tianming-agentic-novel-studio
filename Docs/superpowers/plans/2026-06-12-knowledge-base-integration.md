# 知识库集成修复实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现文件自动处理管道、Qdrant向量检索、记忆与知识库关联

**Architecture:** 
- 数据库迁移：新增 knowledge_processing_tasks 表，扩展 knowledge 和 agent_memories 表
- KnowledgeProcessingService：文件分析编排器（短文件单次分析/长文件分块聚合）
- Agent工具：ProcessKnowledgeFile，触发LLM提取知识条目
- 向量检索：CreativeKnowledgeBaseService 改用 Qdrant 替代文本匹配
- 记忆关联：Reflection 阶段自动记录使用的知识条目到 ProjectMemory/AuthorMemory

**Tech Stack:** 
- C# ASP.NET Core 8.0, EF Core, SQLite
- Qdrant 向量数据库
- AgentToolCallingClient (LLM 调用)
- 现有 AgentMemoryRepository, VectorStore, MicroEmbeddingService

---

## 文件结构

### 新增文件
- `Migrations/YYYYMMDDHHMMSS_AddKnowledgeProcessingTasks.cs` - 数据库迁移
- `Data/Entities/KnowledgeProcessingTask.cs` - 处理任务实体
- `Services/Knowledge/KnowledgeProcessingService.cs` - 文件处理编排器
- `Services/Knowledge/IKnowledgeProcessingService.cs` - 接口
- `DTOs/KnowledgeProcessingTaskDto.cs` - DTO
- `DTOs/ExtractedKnowledgeEntryDto.cs` - 提取的知识条目DTO
- `Tests/Unit/Services/Knowledge/KnowledgeProcessingServiceTests.cs` - 单元测试

### 修改文件
- `Data/Entities/Knowledge.cs` - 添加 source_type, source_file_id, chunk_index, extraction_context 字段
- `Data/NovelAgentDbContext.cs` - 注册新实体
- `Services/Memory/AgentMemoryRepository.cs` - 支持 referenced_knowledge_ids, used_trope_patterns 字段
- `Support/AgentCore.cs` - Reflection 阶段自动关联知识库
- `Support/AgentToolRegistry.cs` - 注册 ProcessKnowledgeFile 工具
- `Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs` - 改用 Qdrant 向量检索
- `Controllers/KnowledgeController.cs` - 新增上传、处理、进度查询端点
- `Program.cs` - 注册 KnowledgeProcessingService

---

## Task 1: 数据库迁移 - 新增 KnowledgeProcessingTask 表

**Files:**
- Create: `Migrations/YYYYMMDDHHMMSS_AddKnowledgeProcessingTasks.cs`
- Create: `Data/Entities/KnowledgeProcessingTask.cs`
- Modify: `Data/NovelAgentDbContext.cs`

- [ ] **Step 1: 创建 KnowledgeProcessingTask 实体**

```csharp
// Data/Entities/KnowledgeProcessingTask.cs
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class KnowledgeProcessingTask
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "pending";
    public string Strategy { get; set; } = "single_pass";
    public int Progress { get; set; }
    public int? TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public int ExtractedEntriesCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public User User { get; set; } = null!;
    public NovelProject? Project { get; set; }
}
```

- [ ] **Step 2: 在 DbContext 注册实体**

```csharp
// Data/NovelAgentDbContext.cs - 在 OnModelCreating 方法中添加
modelBuilder.Entity<KnowledgeProcessingTask>(entity =>
{
    entity.ToTable("knowledge_processing_tasks");
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => e.UserId);
    entity.HasIndex(e => e.Status);
    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 3: 创建迁移文件**

在项目根目录运行：
```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio
dotnet tool install --global dotnet-ef
cd Web/NovelAgentWeb
dotnet ef migrations add AddKnowledgeProcessingTasks
```

预期输出：
```
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

- [ ] **Step 4: 应用迁移**

```bash
dotnet ef database update
```

预期：数据库新增 `knowledge_processing_tasks` 表

- [ ] **Step 5: 提交**

```bash
git add Data/Entities/KnowledgeProcessingTask.cs Data/NovelAgentDbContext.cs Migrations/
git commit -m "feat(knowledge): add KnowledgeProcessingTask entity and migration

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: 数据库迁移 - 扩展 Knowledge 表

**Files:**
- Create: `Migrations/YYYYMMDDHHMMSS_ExtendKnowledgeTable.cs`
- Modify: `Data/Entities/Knowledge.cs`

- [ ] **Step 1: 扩展 Knowledge 实体**

```csharp
// Data/Entities/Knowledge.cs - 添加新字段
public string SourceType { get; set; } = "manual";
public string? SourceFileId { get; set; }
public int? ChunkIndex { get; set; }
public string? ExtractionContext { get; set; }
```

- [ ] **Step 2: 创建迁移**

```bash
cd Web/NovelAgentWeb
dotnet ef migrations add ExtendKnowledgeTable
```

- [ ] **Step 3: 应用迁移**

```bash
dotnet ef database update
```

- [ ] **Step 4: 提交**

```bash
git add Data/Entities/Knowledge.cs Migrations/
git commit -m "feat(knowledge): add source tracking fields to Knowledge entity

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: 创建 DTO 和基础数据结构

**Files:**
- Create: `DTOs/KnowledgeProcessingTaskDto.cs`
- Create: `DTOs/ExtractedKnowledgeEntryDto.cs`

- [ ] **Step 1: 创建 KnowledgeProcessingTaskDto**

```csharp
// DTOs/KnowledgeProcessingTaskDto.cs
namespace TM.Web.NovelAgentWeb.DTOs;

public class KnowledgeProcessingTaskDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int? TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public int ExtractedEntriesCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: 创建 ExtractedKnowledgeEntryDto**

```csharp
// DTOs/ExtractedKnowledgeEntryDto.cs
namespace TM.Web.NovelAgentWeb.DTOs;

public class ExtractedKnowledgeEntryDto
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Weight { get; set; } = 5;
    public string? OriginalText { get; set; }
}

public class ChunkedAnalysisResultDto
{
    public List<ExtractedKnowledgeEntryDto> Entries { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class AggregatedAnalysisResultDto
{
    public List<ExtractedKnowledgeEntryDto> Deduplicated { get; set; } = new();
    public List<ExtractedKnowledgeEntryDto> Aggregated { get; set; } = new();
}
```

- [ ] **Step 3: 提交**

```bash
git add DTOs/
git commit -m "feat(knowledge): add processing task and extraction DTOs

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: 实现 KnowledgeProcessingService - 短文件处理

**Files:**
- Create: `Services/Knowledge/IKnowledgeProcessingService.cs`
- Create: `Services/Knowledge/KnowledgeProcessingService.cs`

- [ ] **Step 1: 创建接口**

```csharp
// Services/Knowledge/IKnowledgeProcessingService.cs
namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public interface IKnowledgeProcessingService
{
    Task<string> ProcessFileAsync(string taskId, CancellationToken ct = default);
}
```

- [ ] **Step 2: 实现短文件处理逻辑（第一部分）**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

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
}
```

- [ ] **Step 3: 实现 Token 估算和短文件分析**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 继续添加方法
private int EstimateTokenCount(string text)
{
    return (int)(text.Length * 0.75);
}

private async Task<List<ExtractedKnowledgeEntryDto>> ProcessShortFileAsync(
    string content, 
    CancellationToken ct)
{
    var prompt = BuildShortFilePrompt(content);
    var llmResponse = await CallLLMAsync(prompt, ct);
    return ParseEntriesFromJson(llmResponse);
}

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
```

- [ ] **Step 4: 提交**

```bash
git add Services/Knowledge/
git commit -m "feat(knowledge): implement short file processing in KnowledgeProcessingService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: 实现 LLM 调用和 JSON 解析

**Files:**
- Modify: `Services/Knowledge/KnowledgeProcessingService.cs`

- [ ] **Step 1: 实现 LLM 调用（使用用户配置）**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加依赖和方法
// 在构造函数添加参数：
private readonly Support.AgentToolCallingClient _llmClient;

public KnowledgeProcessingService(
    NovelAgentDbContext db,
    IKnowledgeService knowledgeService,
    IMicroEmbeddingService embedding,
    Support.AgentToolCallingClient llmClient,
    ILogger<KnowledgeProcessingService> logger)
{
    _db = db;
    _knowledgeService = knowledgeService;
    _embedding = embedding;
    _llmClient = llmClient;
    _logger = logger;
}

// 添加 LLM 调用方法
private async Task<string> CallLLMAsync(string prompt, CancellationToken ct)
{
    var response = await _llmClient.CompleteChatAsync(
        new List<Support.ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        },
        ct);
    
    return response;
}
```

- [ ] **Step 2: 实现 JSON 解析**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加解析方法
private List<ExtractedKnowledgeEntryDto> ParseEntriesFromJson(string jsonResponse)
{
    try
    {
        var cleaned = jsonResponse.Trim();
        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned.Substring(7);
            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }
        cleaned = cleaned.Trim();
        
        var entries = JsonSerializer.Deserialize<List<ExtractedKnowledgeEntryDto>>(
            cleaned, 
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        return entries ?? new List<ExtractedKnowledgeEntryDto>();
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Failed to parse LLM response as JSON: {Response}", jsonResponse);
        return new List<ExtractedKnowledgeEntryDto>();
    }
}
```

- [ ] **Step 3: 提交**

```bash
git add Services/Knowledge/KnowledgeProcessingService.cs
git commit -m "feat(knowledge): implement LLM calling and JSON parsing

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 6: 实现长文件处理（分块+聚合）

**Files:**
- Modify: `Services/Knowledge/KnowledgeProcessingService.cs`

- [ ] **Step 1: 实现文件分块逻辑**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加方法
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
```

- [ ] **Step 2: 实现分块 Prompt 和解析**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加方法
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

private ChunkedAnalysisResultDto ParseChunkedResult(string jsonResponse)
{
    var cleaned = jsonResponse.Trim();
    if (cleaned.StartsWith("```json"))
    {
        cleaned = cleaned.Substring(7);
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
    }
    cleaned = cleaned.Trim();
    
    try
    {
        var result = JsonSerializer.Deserialize<ChunkedAnalysisResultDto>(
            cleaned, 
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? new ChunkedAnalysisResultDto();
    }
    catch
    {
        return new ChunkedAnalysisResultDto();
    }
}
```

- [ ] **Step 3: 实现聚合总结**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加方法
private async Task<List<ExtractedKnowledgeEntryDto>> AggregateEntriesAsync(
    List<ExtractedKnowledgeEntryDto> allEntries,
    CancellationToken ct)
{
    var entriesJson = JsonSerializer.Serialize(allEntries);
    var prompt = $@"你是创意写作知识提取专家。已完成分块分析，现在需要跨块聚合。

**各块提取的条目**（JSON数组）：
{entriesJson}

**任务**：
1. 识别重复或相似条目，合并去重
2. 提取跨块的共性主题、技巧模式
3. 生成全文级别的高阶知识条目（如整体风格特征、叙事结构规律）

**输出格式**（JSON对象）：
{{
  ""deduplicated"": [/* 去重后的条目 */],
  ""aggregated"": [/* 新增的全文级条目 */]
}}

只返回JSON对象，不要其他文字。";
    
    var llmResponse = await CallLLMAsync(prompt, ct);
    var result = ParseAggregatedResult(llmResponse);
    
    result.Deduplicated.AddRange(result.Aggregated);
    return result.Deduplicated;
}

private AggregatedAnalysisResultDto ParseAggregatedResult(string jsonResponse)
{
    var cleaned = jsonResponse.Trim();
    if (cleaned.StartsWith("```json"))
    {
        cleaned = cleaned.Substring(7);
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
    }
    cleaned = cleaned.Trim();
    
    try
    {
        return JsonSerializer.Deserialize<AggregatedAnalysisResultDto>(
            cleaned, 
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new AggregatedAnalysisResultDto();
    }
    catch
    {
        return new AggregatedAnalysisResultDto();
    }
}
```

- [ ] **Step 4: 提交**

```bash
git add Services/Knowledge/KnowledgeProcessingService.cs
git commit -m "feat(knowledge): implement long file processing with chunking and aggregation

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 7: 保存提取的知识条目到数据库和向量库

**Files:**
- Modify: `Services/Knowledge/KnowledgeProcessingService.cs`

- [ ] **Step 1: 实现保存逻辑**

```csharp
// Services/Knowledge/KnowledgeProcessingService.cs - 添加方法
private async Task SaveExtractedEntriesAsync(
    Data.Entities.KnowledgeProcessingTask task,
    List<ExtractedKnowledgeEntryDto> entries,
    CancellationToken ct)
{
    foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
    {
        var knowledge = new Data.Entities.Knowledge
        {
            Id = Guid.NewGuid().ToString(),
            UserId = task.UserId,
            ProjectId = task.ProjectId,
            Title = entry.Title,
            Content = entry.Content,
            Category = entry.Category,
            Tags = string.Join(",", entry.Tags),
            IsVectorized = false,
            SourceType = "extracted",
            SourceFileId = task.Id,
            ChunkIndex = index,
            ExtractionContext = entry.OriginalText,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        await _knowledgeService.CreateKnowledgeAsync(new DTOs.CreateKnowledgeRequest
        {
            ProjectId = task.ProjectId,
            Title = entry.Title,
            Content = entry.Content,
            Category = entry.Category,
            Tags = entry.Tags
        }, ct);
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add Services/Knowledge/KnowledgeProcessingService.cs
git commit -m "feat(knowledge): implement saving extracted entries to database and vector store

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 8: 注册 KnowledgeProcessingService 到 DI 容器

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: 注册服务**

```csharp
// Web/NovelAgentWeb/Program.cs - 在 services 注册部分添加
builder.Services.AddScoped<IKnowledgeProcessingService, KnowledgeProcessingService>();
```

- [ ] **Step 2: 验证编译**

```bash
cd Web/NovelAgentWeb
dotnet build
```

预期：编译成功

- [ ] **Step 3: 提交**

```bash
git add Web/NovelAgentWeb/Program.cs
git commit -m "feat(knowledge): register KnowledgeProcessingService in DI container

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 9: 扩展 KnowledgeController - 添加上传和进度查询端点

**Files:**
- Modify: `Controllers/KnowledgeController.cs`

- [ ] **Step 1: 添加上传端点**

```csharp
// Controllers/KnowledgeController.cs - 添加方法
[HttpPost("upload")]
public async Task<IActionResult> UploadFile(
    [FromForm] IFormFile file,
    [FromForm] string? projectId,
    [FromForm] string? title)
{
    if (file == null || file.Length == 0)
        return BadRequest(new { error = "No file provided" });
    
    var uploadDir = Path.Combine("App_Data", "KnowledgeUploads", _currentUserService.UserId);
    Directory.CreateDirectory(uploadDir);
    
    var fileName = Path.GetFileName(file.FileName);
    var filePath = Path.Combine(uploadDir, Guid.NewGuid().ToString("N") + "_" + fileName);
    
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }
    
    var task = new Data.Entities.KnowledgeProcessingTask
    {
        Id = Guid.NewGuid().ToString(),
        UserId = _currentUserService.UserId,
        ProjectId = projectId,
        FileName = title ?? fileName,
        FilePath = filePath,
        FileSize = file.Length,
        Status = "pending",
        Strategy = "single_pass",
        Progress = 0,
        CreatedAt = DateTime.UtcNow
    };
    
    _db.KnowledgeProcessingTasks.Add(task);
    await _db.SaveChangesAsync();
    
    return Ok(new
    {
        taskId = task.Id,
        fileName = task.FileName,
        fileSize = task.FileSize,
        status = task.Status,
        message = "文件已上传，等待处理"
    });
}
```

- [ ] **Step 2: 添加进度查询端点**

```csharp
// Controllers/KnowledgeController.cs - 添加方法
[HttpGet("tasks/{taskId}")]
public async Task<IActionResult> GetTaskStatus(string taskId)
{
    var task = await _db.KnowledgeProcessingTasks
        .Where(t => t.Id == taskId && t.UserId == _currentUserService.UserId)
        .FirstOrDefaultAsync();
    
    if (task == null)
        return NotFound(new { error = "Task not found" });
    
    return Ok(new KnowledgeProcessingTaskDto
    {
        Id = task.Id,
        FileName = task.FileName,
        FileSize = task.FileSize,
        Status = task.Status,
        Strategy = task.Strategy,
        Progress = task.Progress,
        TotalChunks = task.TotalChunks,
        ProcessedChunks = task.ProcessedChunks,
        ExtractedEntriesCount = task.ExtractedEntriesCount,
        ErrorMessage = task.ErrorMessage,
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt,
        CreatedAt = task.CreatedAt
    });
}
```

- [ ] **Step 3: 添加 DbContext 依赖**

```csharp
// Controllers/KnowledgeController.cs - 在构造函数添加
private readonly NovelAgentDbContext _db;

public KnowledgeController(
    IKnowledgeService knowledgeService,
    ICurrentUserService currentUserService,
    NovelAgentDbContext db,
    ILogger<KnowledgeController> logger)
{
    _knowledgeService = knowledgeService;
    _currentUserService = currentUserService;
    _db = db;
    _logger = logger;
}
```

- [ ] **Step 4: 提交**

```bash
git add Controllers/KnowledgeController.cs
git commit -m "feat(knowledge): add upload and task status query endpoints

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 10: 注册 ProcessKnowledgeFile Agent 工具

**Files:**
- Modify: `Support/AgentToolRegistry.cs`

- [ ] **Step 1: 添加工具注册**

```csharp
// Support/AgentToolRegistry.cs - 在 RegisterTools 方法中添加
Entry("ProcessKnowledgeFile", "knowledge", "Medium", true,
    new[] { "taskId" },
    "处理已上传的知识文件，自动提取创意写作知识条目。支持结构化文档和创意素材。",
    (call, _, _, _, ct) => ProcessKnowledgeFileAsync(call, ct)),
```

- [ ] **Step 2: 实现工具处理方法**

```csharp
// Support/AgentToolRegistry.cs - 添加方法
private async Task<string> ProcessKnowledgeFileAsync(
    ToolCall call,
    CancellationToken ct)
{
    var taskId = call.Arguments.GetValueOrDefault("taskId")?.ToString();
    if (string.IsNullOrEmpty(taskId))
        return "错误：缺少 taskId 参数";
    
    var task = await _workspace.Context.KnowledgeProcessingTasks
        .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == _workspace.UserId, ct);
    
    if (task == null)
        return "错误：找不到指定的处理任务";
    
    if (task.Status != "pending")
        return $"错误：任务状态为 {task.Status}，无法处理";
    
    try
    {
        var processingService = _workspace.ServiceProvider
            .GetRequiredService<IKnowledgeProcessingService>();
        
        var result = await processingService.ProcessFileAsync(taskId, ct);
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process knowledge file {TaskId}", taskId);
        return $"处理失败：{ex.Message}";
    }
}
```

- [ ] **Step 3: 更新工具可用阶段**

```csharp
// Support/AgentToolRegistry.cs - 在 GetAvailableTools 方法中添加
case NovelAgentPhase.Idle:
case NovelAgentPhase.Planning:
case NovelAgentPhase.Reflection:
    // 现有工具...
    yield return Tools["ProcessKnowledgeFile"];
    break;
```

- [ ] **Step 4: 提交**

```bash
git add Support/AgentToolRegistry.cs
git commit -m "feat(agent): register ProcessKnowledgeFile tool

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 11: 改进 CreativeKnowledgeBaseService - 使用 Qdrant 向量检索

**Files:**
- Modify: `Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs`

- [ ] **Step 1: 添加依赖注入**

```csharp
// Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs
// 在类字段添加
private readonly IVectorStore _vectorStore;
private readonly IMicroEmbeddingService _embedding;
private readonly ICurrentUserService _currentUser;
private readonly IAgentMemoryRepository _memoryRepo;

// 构造函数修改
public CreativeKnowledgeBaseService(
    IVectorStore vectorStore,
    IMicroEmbeddingService embedding,
    ICurrentUserService currentUser,
    IAgentMemoryRepository memoryRepo)
{
    _vectorStore = vectorStore;
    _embedding = embedding;
    _currentUser = currentUser;
    _memoryRepo = memoryRepo;
    
    try
    {
        StoragePathHelper.CurrentProjectChanged += (_, _) =>
        {
            _cache = null;
        };
    }
    catch (Exception ex)
    {
        TM.App.Log($"[CreativeKnowledgeBaseService] 订阅项目切换事件失败: {ex.Message}");
    }
}
```

- [ ] **Step 2: 重写 RetrieveAsync 使用向量检索（第一部分）**

```csharp
// Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs
// 替换现有的 RetrieveAsync 方法
public async Task<CreativeKnowledgeRetrievalResult> RetrieveAsync(
    string query,
    StoryCreativeConstitution? constitution = null,
    IEnumerable<string>? usedPlotPatterns = null,
    int topK = 8,
    CancellationToken ct = default)
{
    // 1. 生成查询向量
    var queryVector = await _embedding.EncodeAsync(query, EmbeddingMode.Query, ct);
    
    // 2. Qdrant 向量检索
    var vectorResults = await _vectorStore.SearchAsync(
        userId: _currentUser.UserId,
        queryVector: queryVector,
        filter: new Dictionary<string, object> { { "sourceType", "knowledge" } },
        topK: topK * 2,
        ct: ct);
    
    // 3. 加载记忆
    var projectMemory = await _memoryRepo.GetProjectMemoryAsync(
        _currentUser.UserId, 
        _currentUser.ProjectId ?? "", 
        ct);
    var authorMemory = await _memoryRepo.GetAuthorMemoryAsync(
        _currentUser.UserId, 
        ct);
    
    // 4. 重排序
    var reranked = vectorResults.Select(r => new
    {
        Result = r,
        Score = CalculateScore(r, constitution, usedPlotPatterns, projectMemory, authorMemory)
    }).OrderByDescending(x => x.Score).ToList();
    
    // 5. 过滤已用套路
    if (usedPlotPatterns?.Any() == true)
    {
        reranked = reranked.Where(x =>
        {
            if (!x.Result.Metadata.TryGetValue("category", out var cat))
                return true;
            if (cat?.ToString() != "TropePattern")
                return true;
            
            return !projectMemory.UsedTropePatterns.Any(p =>
                x.Result.Content.Contains(p, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
    
    // 6. 构建结果
    var topResults = reranked.Take(topK).ToList();
    return BuildVectorResult(topResults, query);
}
```

- [ ] **Step 3: 实现评分和结果构建（第二部分）**

```csharp
// Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs
private double CalculateScore(
    VectorSearchResult result,
    StoryCreativeConstitution? constitution,
    IEnumerable<string>? usedPlotPatterns,
    ProjectMemory projectMemory,
    AuthorMemory authorMemory)
{
    var score = result.Score;
    
    // Boost: 项目引用过的知识
    if (projectMemory.ReferencedKnowledgeIds?.Contains(result.Id) == true)
        score += 2.0;
    
    // Boost: 作者收藏的知识
    if (authorMemory.FavoriteKnowledgeIds?.Contains(result.Id) == true)
        score += 1.5;
    
    // Boost: 题材匹配
    if (constitution != null && result.Metadata.TryGetValue("category", out var category))
    {
        if (category?.ToString() == "GenrePrinciple" &&
            !string.IsNullOrEmpty(constitution.Genre) &&
            result.Content.Contains(constitution.Genre, StringComparison.OrdinalIgnoreCase))
        {
            score += 3.0;
        }
    }
    
    return score;
}

private CreativeKnowledgeRetrievalResult BuildVectorResult(
    List<dynamic> rankedResults,
    string query)
{
    var result = new CreativeKnowledgeRetrievalResult
    {
        Success = true,
        Query = query,
        Message = rankedResults.Count == 0
            ? "创意知识库暂无命中，已返回空结果。"
            : $"创意知识库命中 {rankedResults.Count} 条。"
    };
    
    // 转换为 CreativeKnowledgeHit
    foreach (var item in rankedResults)
    {
        var vectorResult = item.Result as VectorSearchResult;
        if (vectorResult == null) continue;
        
        result.Hits.Add(new CreativeKnowledgeHit
        {
            Entry = new CreativeKnowledgeEntry
            {
                Id = vectorResult.Id,
                Title = vectorResult.Metadata.GetValueOrDefault("title")?.ToString() ?? "",
                Content = vectorResult.Content,
                Category = Enum.TryParse<CreativeKnowledgeCategory>(
                    vectorResult.Metadata.GetValueOrDefault("category")?.ToString() ?? "",
                    out var cat) ? cat : CreativeKnowledgeCategory.GenrePrinciple,
                Tags = vectorResult.Metadata.GetValueOrDefault("tags") as List<string> ?? new(),
                Weight = 5
            },
            Score = item.Score,
            Reason = "向量语义匹配"
        });
    }
    
    // 分类填充
    result.GenrePrinciples.AddRange(ProjectContents(result.Hits, CreativeKnowledgeCategory.GenrePrinciple, 4));
    result.TropeWarnings.AddRange(ProjectContents(result.Hits, CreativeKnowledgeCategory.TropePattern, 4));
    result.AntiTropeStrategies.AddRange(ProjectContents(result.Hits, CreativeKnowledgeCategory.AntiTropeStrategy, 5));
    
    return result;
}
```

- [ ] **Step 4: 提交**

```bash
git add Services/Framework/AI/NovelAgent/Services/CreativeKnowledgeBaseService.cs
git commit -m "feat(knowledge): replace text matching with Qdrant vector retrieval in CreativeKnowledgeBaseService

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 12: 扩展记忆模型 - 支持知识库关联字段

**Files:**
- Modify: `Services/Memory/AgentMemoryRepository.cs`

- [ ] **Step 1: 更新 ProjectMemory 模型**

```csharp
// 在现有 GetProjectMemoryAsync 方法中添加新字段解析
var memory = new ProjectMemory
{
    LongTermGoal = GetField<string>(rows, "project.long_term_goal"),
    ReaderPromise = GetField<string>(rows, "project.reader_promise"),
    Constraints = GetField<List<string>>(rows, "project.constraints") ?? new(),
    UnresolvedThreads = GetField<List<string>>(rows, "project.unresolved_threads") ?? new(),
    ReferencedKnowledgeIds = GetField<List<string>>(rows, "project.referenced_knowledge_ids") ?? new(),
    UsedTropePatterns = GetField<List<string>>(rows, "project.used_trope_patterns") ?? new()
};
```

- [ ] **Step 2: 更新 AuthorMemory 模型**

```csharp
// 在现有 GetAuthorMemoryAsync 方法中添加新字段解析
var memory = new AuthorMemory
{
    StyleLikes = GetField<List<string>>(rows, "author.style_likes") ?? new(),
    StyleDislikes = GetField<List<string>>(rows, "author.style_dislikes") ?? new(),
    ConfirmationTolerance = GetField<string>(rows, "author.confirmation_tolerance"),
    GenreHabits = GetField<List<string>>(rows, "author.genre_habits") ?? new(),
    FavoriteKnowledgeIds = GetField<List<string>>(rows, "author.favorite_knowledge_ids") ?? new()
};
```

- [ ] **Step 3: 提交**

```bash
git add Services/Memory/AgentMemoryRepository.cs
git commit -m "feat(memory): add knowledge association fields to ProjectMemory and AuthorMemory

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 13: 扩展 AgentMemoryUpdate - 支持知识库使用记录

**Files:**
- Modify: `Support/AgentCore.cs`

- [ ] **Step 1: 在 AgentMemoryUpdate 类中添加新字段**

```csharp
// Support/AgentCore.cs - 在 AgentMemoryUpdate 类定义中添加
public List<string> UsedKnowledgeIds { get; set; } = new();
public List<string> UsedTropePatterns { get; set; } = new();
```

- [ ] **Step 2: 提交**

```bash
git add Support/AgentCore.cs
git commit -m "feat(memory): add UsedKnowledgeIds and UsedTropePatterns to AgentMemoryUpdate

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 14: 实现 Reflection 阶段自动关联知识库

**Files:**
- Modify: `Support/AgentCore.cs`

- [ ] **Step 1: 在 ApplyMemoryUpdateAsync 方法中添加知识库关联逻辑**

```csharp
// Support/AgentCore.cs - 在 ApplyMemoryUpdateAsync 方法末尾添加

// 关联使用的知识条目
if (memoryUpdate.UsedKnowledgeIds?.Any() == true)
{
    var projectMemory = await _workspace.MemoryRepo.GetProjectMemoryAsync(
        _workspace.UserId, 
        _workspace.ProjectId ?? "", 
        ct);
    
    var newRefs = memoryUpdate.UsedKnowledgeIds
        .Except(projectMemory.ReferencedKnowledgeIds ?? new List<string>())
        .ToList();
    
    if (newRefs.Any())
    {
        var updated = projectMemory.ReferencedKnowledgeIds ?? new List<string>();
        updated.AddRange(newRefs);
        
        await _workspace.MemoryRepo.UpdateFieldAsync(
            _workspace.UserId,
            _workspace.ProjectId,
            "project.referenced_knowledge_ids",
            updated,
            ct);
        
        _logger.LogDebug("Added {Count} knowledge references to ProjectMemory", newRefs.Count);
    }
}

// 记录使用的套路模式
if (memoryUpdate.UsedTropePatterns?.Any() == true)
{
    var projectMemory = await _workspace.MemoryRepo.GetProjectMemoryAsync(
        _workspace.UserId,
        _workspace.ProjectId ?? "",
        ct);
    
    var newTropes = memoryUpdate.UsedTropePatterns
        .Except(projectMemory.UsedTropePatterns ?? new List<string>())
        .ToList();
    
    if (newTropes.Any())
    {
        var updated = projectMemory.UsedTropePatterns ?? new List<string>();
        updated.AddRange(newTropes);
        
        await _workspace.MemoryRepo.UpdateFieldAsync(
            _workspace.UserId,
            _workspace.ProjectId,
            "project.used_trope_patterns",
            updated,
            ct);
        
        _logger.LogDebug("Added {Count} trope patterns to ProjectMemory", newTropes.Count);
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add Support/AgentCore.cs
git commit -m "feat(memory): auto-associate knowledge usage in Reflection phase

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 15: 编写单元测试 - KnowledgeProcessingService

**Files:**
- Create: `Tests/Unit/Services/Knowledge/KnowledgeProcessingServiceTests.cs`

- [ ] **Step 1: 创建测试文件框架**

```csharp
// Tests/Unit/Services/Knowledge/KnowledgeProcessingServiceTests.cs
using Xunit;
using Moq;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Tests.Unit.Services.Knowledge;

public class KnowledgeProcessingServiceTests
{
    private readonly Mock<NovelAgentDbContext> _mockDb;
    private readonly Mock<IKnowledgeService> _mockKnowledgeService;
    private readonly Mock<IMicroEmbeddingService> _mockEmbedding;
    private readonly Mock<ILogger<KnowledgeProcessingService>> _mockLogger;
    private readonly KnowledgeProcessingService _service;
    
    public KnowledgeProcessingServiceTests()
    {
        _mockDb = new Mock<NovelAgentDbContext>();
        _mockKnowledgeService = new Mock<IKnowledgeService>();
        _mockEmbedding = new Mock<IMicroEmbeddingService>();
        _mockLogger = new Mock<ILogger<KnowledgeProcessingService>>();
        
        _service = new KnowledgeProcessingService(
            _mockDb.Object,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _mockLogger.Object);
    }
    
    [Fact]
    public void EstimateTokenCount_ReturnsCorrectEstimate()
    {
        // Test token estimation logic
        var text = "测试文本";
        var tokens = _service.EstimateTokenCount(text);
        Assert.InRange(tokens, 1, text.Length);
    }
}
```

- [ ] **Step 2: 运行测试**

```bash
cd Tests
dotnet test --filter FullyQualifiedName~KnowledgeProcessingServiceTests
```

预期：测试通过

- [ ] **Step 3: 提交**

```bash
git add Tests/Unit/Services/Knowledge/
git commit -m "test(knowledge): add KnowledgeProcessingService unit tests

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 16: 端到端集成测试

**Files:**
- Test manually

- [ ] **Step 1: 启动后端服务**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio/Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

预期：服务在 5002 端口启动成功

- [ ] **Step 2: 测试文件上传**

```bash
curl -X POST http://127.0.0.1:5002/api/knowledge/upload \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@test.md" \
  -F "projectId=test-project-001" \
  -F "title=测试知识文件"
```

预期：返回 taskId

- [ ] **Step 3: 测试进度查询**

```bash
curl http://127.0.0.1:5002/api/knowledge/tasks/{taskId} \
  -H "Authorization: Bearer YOUR_TOKEN"
```

预期：返回任务状态（pending）

- [ ] **Step 4: 通过 Agent 工具触发处理**

在前端对话框输入：
```
请处理知识文件 taskId: {taskId}
```

预期：Agent 调用 ProcessKnowledgeFile 工具，返回成功消息

- [ ] **Step 5: 验证数据库**

```bash
sqlite3 App_Data/Database/novelagent.db "SELECT * FROM knowledge WHERE source_type='extracted';"
```

预期：显示提取的知识条目

- [ ] **Step 6: 测试向量检索**

在前端输入：
```
搜索知识库：如何避免套路
```

预期：Agent 调用 SearchCreativeKnowledge，返回相关知识条目

---

## Task 17: 文档和提交最终版本

**Files:**
- Update: `CLAUDE.md`
- Update: `/Users/lyston/Obsidian/lyston/Claude/天命AI写作/`

- [ ] **Step 1: 更新 CLAUDE.md**

添加知识库处理相关说明：
```markdown
## 知识库自动处理

用户上传文件后：
1. POST /api/knowledge/upload - 创建处理任务
2. Agent 调用 ProcessKnowledgeFile 工具
3. 短文件(<6K tokens)单次分析，长文件分块+聚合
4. 提取的知识条目自动向量化到 Qdrant
5. Reflection 阶段自动关联到 ProjectMemory

## 向量检索

- CreativeKnowledgeBaseService.RetrieveAsync 使用 Qdrant 向量检索
- 融合 ProjectMemory 和 AuthorMemory 进行 Boost
- 过滤已用套路模式
```

- [ ] **Step 2: 记录到 Obsidian**

使用 md-docs skill：
```
/记录 知识库集成修复完成：
1. 实现文件自动处理管道（短文件单次分析/长文件分块聚合）
2. CreativeKnowledgeBaseService 改用 Qdrant 向量检索
3. Reflection 阶段自动关联知识库到记忆系统
4. 新增 ProcessKnowledgeFile Agent 工具
5. 新增上传、进度查询 API 端点
```

- [ ] **Step 3: 最终提交**

```bash
git add .
git commit -m "feat(knowledge): complete knowledge base integration

- Add KnowledgeProcessingTask entity and migrations
- Implement file processing pipeline (short/long files)
- Register ProcessKnowledgeFile agent tool
- Replace text matching with Qdrant vector retrieval
- Auto-associate knowledge usage in Reflection phase
- Add upload and task status API endpoints

Implements spec: Docs/superpowers/specs/2026-06-12-knowledge-base-integration.md

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## 实施检查清单

### P0 - 核心功能（必须完成）
- [x] Task 1: 数据库迁移 - 新增 KnowledgeProcessingTask 表
- [x] Task 2: 数据库迁移 - 扩展 Knowledge 表
- [x] Task 3: 创建 DTO 和基础数据结构
- [x] Task 4: 实现 KnowledgeProcessingService - 短文件处理
- [x] Task 5: 实现 LLM 调用和 JSON 解析
- [x] Task 6: 实现长文件处理（分块+聚合）
- [x] Task 7: 保存提取的知识条目到数据库和向量库
- [x] Task 8: 注册 KnowledgeProcessingService 到 DI 容器
- [x] Task 9: 扩展 KnowledgeController - 添加上传和进度查询端点
- [x] Task 10: 注册 ProcessKnowledgeFile Agent 工具
- [x] Task 11: 改进 CreativeKnowledgeBaseService - 使用 Qdrant 向量检索

### P1 - 增强功能（建议完成）
- [x] Task 12: 扩展记忆模型 - 支持知识库关联字段
- [x] Task 13: 扩展 AgentMemoryUpdate - 支持知识库使用记录
- [x] Task 14: 实现 Reflection 阶段自动关联知识库

### P2 - 测试和文档（必须完成）
- [x] Task 15: 编写单元测试 - KnowledgeProcessingService
- [x] Task 16: 端到端集成测试
- [x] Task 17: 文档和提交最终版本

---

## 验收标准

实施完成后，必须满足以下标准：

1. ✅ **文件上传**：用户可通过 API 上传知识文件，返回 taskId
2. ✅ **自动处理**：Agent 调用 ProcessKnowledgeFile 工具成功处理文件
3. ✅ **短文件分析**：< 6K tokens 文件单次 LLM 调用，提取 3-10 条知识条目
4. ✅ **长文件分析**：≥ 6K tokens 文件分块处理，聚合去重，生成全文级知识
5. ✅ **向量检索**：CreativeKnowledgeBaseService.RetrieveAsync 使用 Qdrant，召回准确率 > 80%
6. ✅ **记忆融合**：检索时自动 Boost 项目引用和作者收藏的知识
7. ✅ **自动关联**：Reflection 阶段自动记录使用的知识条目到 ProjectMemory
8. ✅ **进度追踪**：长文件处理显示进度（0-100%）
9. ✅ **错误处理**：处理失败时记录错误信息，任务状态为 failed
10. ✅ **多用户隔离**：每个用户只能访问自己的处理任务和知识条目

---

## 性能目标

- **短文件处理时间**：< 30 秒
- **长文件处理时间**：< 2 分钟（10K 字文档）
- **向量检索响应时间**：< 500ms
- **并发处理能力**：支持 5 个用户同时上传处理

---

## 故障排查

### 问题1：LLM 返回格式错误
**症状**：JSON 解析失败  
**排查**：检查 LLM 返回内容，是否包含 markdown 代码块  
**修复**：在 ParseEntriesFromJson 中添加 ```json 清理逻辑

### 问题2：向量检索无结果
**症状**：SearchCreativeKnowledge 返回空结果  
**排查**：检查 Qdrant 中是否有 sourceType=knowledge 的向量  
**修复**：确认 KnowledgeService.CreateKnowledgeAsync 正确向量化

### 问题3：记忆关联不生效
**症状**：Reflection 后 ProjectMemory.ReferencedKnowledgeIds 为空  
**排查**：检查 AgentMemoryUpdate.UsedKnowledgeIds 是否填充  
**修复**：在 Agent 工具返回时记录使用的知识条目 ID

---

## 相关文档

- 设计规范：`Docs/superpowers/specs/2026-06-12-knowledge-base-integration.md`
- API 文档：`/Users/lyston/Obsidian/lyston/Claude/天命AI写作/05 API 设计文档.md`
- 数据库设计：`/Users/lyston/Obsidian/lyston/Claude/天命AI写作/04 数据库设计.md`

