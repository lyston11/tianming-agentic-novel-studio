using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Http;
using Moq;
using Moq.Protected;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;

namespace Tests.Unit.Services.Knowledge;

/// <summary>
/// Unit tests for KnowledgeProcessingService.
/// Tests token estimation, JSON parsing, chunking logic, and LLM integration.
/// </summary>
public class KnowledgeProcessingServiceTests : IDisposable
{
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    private readonly NovelAgentDbContext _db;
    private readonly ServiceProvider _settingsProvider;
    private readonly Mock<IKnowledgeService> _mockKnowledgeService;
    private readonly Mock<IMicroEmbeddingService> _mockEmbedding;
    private readonly UserSettingsManager _settingsManager;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<KnowledgeProcessingService>> _mockLogger;
    private readonly KnowledgeProcessingService _service;

    public KnowledgeProcessingServiceTests()
    {
        // Setup in-memory database
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName, DatabaseRoot)
            .Options;
        _db = new NovelAgentDbContext(options);

        var settingsServices = new ServiceCollection();
        settingsServices.AddSingleton<ILlmApiKeyProtector>(
            new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider()));
        settingsServices.AddDbContext<NovelAgentDbContext>(db =>
            db.UseInMemoryDatabase(databaseName: databaseName, DatabaseRoot));
        _settingsProvider = settingsServices.BuildServiceProvider();

        _settingsManager = new UserSettingsManager(
            _settingsProvider.GetRequiredService<IServiceScopeFactory>(),
            new HttpContextAccessor(),
            new FixedBackgroundUserContext("user-1"),
            _settingsProvider.GetRequiredService<ILlmApiKeyProtector>());
        _settingsManager.SaveAsync(new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4",
            LlmApiKey = "test-key",
            LlmTemperature = 0.7,
            LlmMaxTokens = 2000
        }).GetAwaiter().GetResult();

        // Setup mocks
        _mockKnowledgeService = new Mock<IKnowledgeService>();
        _mockEmbedding = new Mock<IMicroEmbeddingService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<KnowledgeProcessingService>>();

        _service = new KnowledgeProcessingService(
            _db,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _settingsManager,
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            new ContentDocumentService(_db)
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _settingsProvider.Dispose();

        GC.SuppressFinalize(this);
    }

    private sealed class FixedBackgroundUserContext : IBackgroundUserContext
    {
        public FixedBackgroundUserContext(string userId)
        {
            Current = new BackgroundUserSnapshot(userId, "author", "author@example.com", "author");
        }

        public BackgroundUserSnapshot? Current { get; }

        public IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author") =>
            new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Test token estimation using reflection to access private method.
    /// Chinese text: ~1.3 characters per token, so multiply by 0.75.
    /// </summary>
    [Fact]
    public void EstimateTokenCount_ChineseText_ReturnsCorrectEstimate()
    {
        // Arrange: 1000 Chinese characters should be ~750 tokens
        var text = new string('中', 1000);
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("EstimateTokenCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var tokens = (int)method!.Invoke(_service, new object[] { text })!;

        // Assert
        Assert.Equal(750, tokens);
    }

    /// <summary>
    /// Test token estimation with English text.
    /// </summary>
    [Fact]
    public void EstimateTokenCount_EnglishText_ReturnsCorrectEstimate()
    {
        // Arrange: 4000 characters should be ~3000 tokens
        var text = new string('a', 4000);
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("EstimateTokenCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var tokens = (int)method!.Invoke(_service, new object[] { text })!;

        // Assert
        Assert.Equal(3000, tokens);
    }

    /// <summary>
    /// Test text chunking with overlap.
    /// </summary>
    [Fact]
    public void ChunkText_CreatesOverlappingChunks()
    {
        // Arrange
        var text = new string('a', 10000);
        var chunkSize = 4000;
        var overlap = 200;
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ChunkText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var chunks = (List<string>)method!.Invoke(_service, new object[] { text, chunkSize, overlap })!;

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Equal(4000, chunks[0].Length);

        // Verify overlap: second chunk starts at 4000-200=3800
        if (chunks.Count > 1)
        {
            // Second chunk should be 4000 characters starting from position 3800
            Assert.Equal(4000, chunks[1].Length);
        }

        // Calculate expected number of chunks
        // Start=0, length=4000; start=3800, length=4000; start=7600, length=2400 (final)
        Assert.Equal(3, chunks.Count);
        Assert.Equal(2400, chunks[2].Length); // Final chunk
    }

    /// <summary>
    /// Test JSON parsing with markdown fences.
    /// </summary>
    [Fact]
    public void ParseEntriesFromJson_HandlesMarkdownFences()
    {
        // Arrange
        var jsonWithFences = @"```json
[
  {
    ""title"": ""测试标题"",
    ""category"": ""GenrePrinciple"",
    ""content"": ""测试内容"",
    ""tags"": [""标签1""],
    ""weight"": 5
  }
]
```";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var entries = (List<ExtractedKnowledgeEntryDto>)method!.Invoke(_service, new object[] { jsonWithFences })!;

        // Assert
        Assert.Single(entries);
        Assert.Equal("测试标题", entries[0].Title);
        Assert.Equal("GenrePrinciple", entries[0].Category);
        Assert.Equal(5, entries[0].Weight);
    }

    /// <summary>
    /// Test JSON parsing with simple JSON array (no fences).
    /// </summary>
    [Fact]
    public void ParseEntriesFromJson_HandlesPlainJson()
    {
        // Arrange
        var plainJson = @"[
  {
    ""title"": ""反套路技巧"",
    ""category"": ""AntiTropeStrategy"",
    ""content"": ""详细说明"",
    ""tags"": [""创新"", ""反转""],
    ""weight"": 7,
    ""originalText"": ""原文片段""
  }
]";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var entries = (List<ExtractedKnowledgeEntryDto>)method!.Invoke(_service, new object[] { plainJson })!;

        // Assert
        Assert.Single(entries);
        Assert.Equal("反套路技巧", entries[0].Title);
        Assert.Equal("AntiTropeStrategy", entries[0].Category);
        Assert.Equal(7, entries[0].Weight);
        Assert.Equal(2, entries[0].Tags.Count);
        Assert.Equal("原文片段", entries[0].OriginalText);
    }

    [Fact]
    public void ParseEntriesFromJson_HandlesEntriesWrapperObject()
    {
        var wrappedJson = @"{
  ""entries"": [
    {
      ""title"": ""硬事实"",
      ""category"": ""HardFact"",
      ""content"": ""沈砚是主角，银蓝邮徽只能辨认被篡改的邮路。"",
      ""tags"": [""沈砚"", ""银蓝邮徽""],
      ""weight"": 9,
      ""originalText"": ""沈砚是雾潮城第七码头的低阶星渊邮差""
    }
  ]
}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var entries = (List<ExtractedKnowledgeEntryDto>)method!.Invoke(_service, new object[] { wrappedJson })!;

        Assert.Single(entries);
        Assert.Equal("HardFact", entries[0].Category);
        Assert.Contains("沈砚", entries[0].Content);
    }

    [Fact]
    public void ParseEntriesFromJson_ExtractsFirstCompletePayloadWhenModelAddsNotes()
    {
        var wrappedJson = @"```json
{
  ""entries"": [
    {
      ""title"": ""邮徽硬事实"",
      ""category"": ""HardFact"",
      ""content"": ""银蓝邮徽只能辨认旧邮路，不能攻击或升级。"",
      ""tags"": [""银蓝邮徽"", ""旧邮路""],
      ""weight"": 10,
      ""originalText"": ""银蓝邮徽只能辨认旧邮路""
    }
  ]
}
```

备注：{""ignored"": [""不要解析这里""]}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var entries = (List<ExtractedKnowledgeEntryDto>)method!.Invoke(_service, new object[] { wrappedJson })!;

        Assert.Single(entries);
        Assert.Equal("邮徽硬事实", entries[0].Title);
        Assert.Equal("HardFact", entries[0].Category);
        Assert.Contains("不能攻击", entries[0].Content);
    }

    [Fact]
    public void ParseChunkedResult_ExtractsFirstCompletePayloadWhenModelAddsNotes()
    {
        var chunkedJson = @"```json
{
  ""entries"": [
    {
      ""title"": ""分块硬事实"",
      ""category"": ""HardFact"",
      ""content"": ""沈砚在第一章获得银蓝邮徽。"",
      ""tags"": [""沈砚""],
      ""weight"": 9
    }
  ],
  ""summary"": ""沈砚获得银蓝邮徽。""
}
```

附言：{""ignored"": true}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseChunkedResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (ChunkedAnalysisResultDto)method!.Invoke(_service, new object[] { chunkedJson })!;

        Assert.Single(result.Entries);
        Assert.Equal("分块硬事实", result.Entries[0].Title);
        Assert.Contains("银蓝邮徽", result.Summary);
    }

    [Fact]
    public void ParseAggregatedResult_ExtractsFirstCompletePayloadWhenModelAddsNotes()
    {
        var aggregatedJson = @"```json
{
  ""deduplicated"": [
    {
      ""title"": ""去重硬事实"",
      ""category"": ""HardFact"",
      ""content"": ""旧邮路开启必须付出真实记忆。"",
      ""tags"": [""旧邮路""],
      ""weight"": 10
    }
  ],
  ""aggregated"": [
    {
      ""title"": ""题材规律"",
      ""category"": ""GenrePrinciple"",
      ""content"": ""每次胜利都要留下可追踪代价。"",
      ""tags"": [""代价""],
      ""weight"": 8
    }
  ]
}
```

补充：{""ignored"": [""尾部对象""]}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseAggregatedResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (AggregatedAnalysisResultDto)method!.Invoke(_service, new object[] { aggregatedJson })!;

        Assert.Single(result.Deduplicated);
        Assert.Single(result.Aggregated);
        Assert.Equal("去重硬事实", result.Deduplicated[0].Title);
        Assert.Equal("题材规律", result.Aggregated[0].Title);
    }

    /// <summary>
    /// Test JSON parsing returns empty list on invalid JSON.
    /// </summary>
    [Fact]
    public void ParseEntriesFromJson_ThrowsOnInvalidJson()
    {
        // Arrange
        var invalidJson = "This is not valid JSON {[}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            method!.Invoke(_service, new object[] { invalidJson }));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void BuildShortFilePrompt_RequiresHardFactEntriesForContinuityCriticalFacts()
    {
        var source = "主角沈砚拥有银蓝邮徽，女主陆知微会纸上复原，第九枚空邮票是禁物。";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("BuildShortFilePrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var prompt = (string)method!.Invoke(_service, new object[] { source })!;

        Assert.Contains("硬事实", prompt);
        Assert.Contains("角色姓名", prompt);
        Assert.Contains("关键道具", prompt);
        Assert.Contains("HardFact", prompt);
        Assert.Contains("沈砚", prompt);
        Assert.Contains("银蓝邮徽", prompt);
    }

    /// <summary>
    /// Test OpenAI URL builder.
    /// </summary>
    [Fact]
    public void BuildOpenAiChatCompletionsUrl_HandlesVariousBaseUrls()
    {
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("BuildOpenAiChatCompletionsUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Test 1: Base URL without endpoint
        var url1 = (string)method!.Invoke(null, new object[] { "https://api.openai.com/v1" })!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", url1);

        // Test 2: Base URL already has endpoint
        var url2 = (string)method.Invoke(null, new object[] { "https://api.openai.com/v1/chat/completions" })!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", url2);

        // Test 3: Base URL with trailing slash
        var url3 = (string)method.Invoke(null, new object[] { "https://api.openai.com/v1/" })!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", url3);
    }

    /// <summary>
    /// Test Anthropic URL builder.
    /// </summary>
    [Fact]
    public void BuildAnthropicMessagesUrl_HandlesVariousBaseUrls()
    {
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("BuildAnthropicMessagesUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Test 1: Base URL without endpoint
        var url1 = (string)method!.Invoke(null, new object[] { "https://api.anthropic.com" })!;
        Assert.Equal("https://api.anthropic.com/v1/messages", url1);

        // Test 2: Base URL with v1
        var url2 = (string)method.Invoke(null, new object[] { "https://api.anthropic.com/v1" })!;
        Assert.Equal("https://api.anthropic.com/v1/messages", url2);

        // Test 3: Base URL already has endpoint
        var url3 = (string)method.Invoke(null, new object[] { "https://api.anthropic.com/v1/messages" })!;
        Assert.Equal("https://api.anthropic.com/v1/messages", url3);
    }

    /// <summary>
    /// Test model name normalization.
    /// </summary>
    [Fact]
    public void NormalizeModel_RemovesSuffixes()
    {
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("NormalizeModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Test 1: Model with [1m] suffix
        var normalized1 = (string)method!.Invoke(null, new object[] { "claude-opus-4-7[1m]" })!;
        Assert.Equal("claude-opus-4-7", normalized1);

        // Test 2: Model with :extended suffix
        var normalized2 = (string)method.Invoke(null, new object[] { "gpt-4-turbo:extended" })!;
        Assert.Equal("gpt-4-turbo", normalized2);

        // Test 3: Model without suffix
        var normalized3 = (string)method.Invoke(null, new object[] { "gpt-4" })!;
        Assert.Equal("gpt-4", normalized3);

        // Test 4: Model with both suffixes
        var normalized4 = (string)method.Invoke(null, new object[] { "claude-sonnet-4-6:extended[1m]" })!;
        Assert.Equal("claude-sonnet-4-6", normalized4);
    }

    /// <summary>
    /// Integration test: ProcessFileAsync with short file (single-pass strategy).
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_ShortFile_UsesSinglePassStrategy()
    {
        // Arrange: Create uploaded content in SQLite content documents
        var shortContent = new string('测', 2000); // ~1500 tokens
        var task = await CreateTaskWithUploadContentAsync(shortContent);

        // Mock HTTP client
        var mockResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(@"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""[{\""title\"":\""测试条目\"",\""category\"":\""GenrePrinciple\"",\""content\"":\""测试内容\"",\""tags\"":[\""测试\""],\""weight\"":5}]""
                    }
                }]
            }", Encoding.UTF8, "application/json")
        };

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Mock knowledge service
        _mockKnowledgeService.Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse { Id = "test-id", UsageProjectId = "test-project", EntryType = "GenrePrinciple", Title = "Test", Content = "Test", CreatedAt = DateTime.UtcNow });

        // Act
        var result = await _service.ProcessFileAsync(task.Id);

        // Assert
        Assert.Contains("成功提取", result);
        Assert.Contains("1", result);

        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("processing", updatedTask!.Status);
        Assert.Equal("single_pass", updatedTask.Strategy);
        Assert.Equal(1, updatedTask.ExtractedEntriesCount);
    }

    [Fact]
    public async Task ProcessFileAsync_BindsExtractedKnowledgeToCurrentProject()
    {
        var task = await CreateTaskWithUploadContentAsync(new string('测', 2000));
        var usage = new Mock<IProjectKnowledgeUsageService>();
        var service = new KnowledgeProcessingService(
            _db,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _settingsManager,
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            new ContentDocumentService(_db),
            projectKnowledgeUsage: usage.Object);

        SetupSingleEntryLlmResponse();
        _mockKnowledgeService
            .Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse
            {
                Id = "knowledge-bound-1",
                UsageProjectId = task.ProjectId,
                EntryType = "HardFact",
                Title = "邮徽边界",
                Content = "银蓝邮徽不能攻击。",
                CreatedAt = DateTime.UtcNow
            });

        await service.ProcessFileAsync(task.Id);

        usage.Verify(x => x.MarkImportedAsync(
                task.UserId,
                task.ProjectId!,
                "knowledge-bound-1",
                null,
                $"knowledge_upload:{task.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessFileAsync_RecordsKnowledgeProcessingOutputArtifact()
    {
        var task = await CreateTaskWithUploadContentAsync(new string('测', 2000));
        var truthStore = new ProductionTruthStore(_db);
        var outputArtifacts = new OutputArtifactRecorder(new ProductionEventWriter(truthStore));
        var service = new KnowledgeProcessingService(
            _db,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _settingsManager,
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            new ContentDocumentService(_db),
            outputArtifacts: outputArtifacts);

        SetupSingleEntryLlmResponse();
        _mockKnowledgeService
            .Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse
            {
                Id = "knowledge-output-1",
                UsageProjectId = task.ProjectId,
                EntryType = "HardFact",
                Title = "邮徽边界",
                Content = "银蓝邮徽不能攻击。",
                CreatedAt = DateTime.UtcNow
            });

        await service.ProcessFileAsync(task.Id);

        var evt = await _db.ProductionEvents.SingleAsync(e =>
            e.EventType == OutputArtifactRecorder.EventType &&
            e.ArtifactType == "knowledge_processing_result" &&
            e.ArtifactId == task.Id);
        Assert.Equal(task.UserId, evt.UserId);
        Assert.Equal(task.ProjectId, evt.ProjectId);
        Assert.Equal("knowledge_extraction_completed", evt.Stage);
        Assert.Equal("completed", evt.Status);
        Assert.Contains("uploaded.txt", evt.Message);
        Assert.Contains("knowledge-output-1", evt.DataJson);
        Assert.Contains("\"visibleInWorkflow\":true", evt.DataJson);
        Assert.Contains("\"知识库\"", evt.DataJson);
        Assert.Contains("\"创作工作流\"", evt.DataJson);
    }

    [Fact]
    public async Task ProcessFileAsync_WithProgressContext_PublishesReadableStages()
    {
        var task = await CreateTaskWithUploadContentAsync(new string('测', 2000));
        var events = new List<KnowledgeProcessingProgressEvent>();
        var progress = new KnowledgeProcessingProgressContext(
            "runtime-run-1",
            task.UserId,
            "session-1",
            task.ProjectId,
            (evt, _) =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            });

        SetupSingleEntryLlmResponse();
        _mockKnowledgeService
            .Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse
            {
                Id = "knowledge-progress-1",
                UsageProjectId = task.ProjectId,
                EntryType = "GenrePrinciple",
                Title = "测试",
                Content = "测试内容",
                CreatedAt = DateTime.UtcNow
            });

        await _service.ProcessFileAsync(task.Id, progress: progress);

        Assert.Contains(events, e => e.Stage == "read_upload");
        Assert.Contains(events, e => e.Stage == "llm_extract");
        Assert.Contains(events, e => e.Stage == "save_entries");
        Assert.Contains(events, e => e.Stage == "extraction_completed" && e.Progress >= 90);
    }

    [Fact]
    public async Task ProcessFileAsync_EmptyExtractionFailsTaskInsteadOfCompletingZeroEntries()
    {
        var task = await CreateTaskWithUploadContentAsync("主角沈砚拥有银蓝邮徽，第九枚空邮票不能被焚毁。");

        var mockResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(@"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""[]""
                    }
                }]
            }", Encoding.UTF8, "application/json")
        };

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(mockHttpMessageHandler.Object));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ProcessFileAsync(task.Id));

        Assert.Contains("没有返回任何知识条目", ex.Message);
        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("failed", updatedTask!.Status);
        Assert.Equal(0, updatedTask.ExtractedEntriesCount);
        Assert.Contains("没有返回任何知识条目", updatedTask.ErrorMessage);
    }

    /// <summary>
    /// Integration test: ProcessFileAsync with long file (chunked strategy).
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_LongFile_UsesChunkedStrategy()
    {
        // Arrange: Create uploaded content in SQLite content documents (>6000 tokens)
        var longContent = new string('测', 10000); // ~7500 tokens
        var task = await CreateTaskWithUploadContentAsync(longContent);

        // Mock HTTP client with multiple responses (chunked + aggregation)
        var callCount = 0;
        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                callCount++;
                var mockHttpMessageHandler = new Mock<HttpMessageHandler>();

                if (callCount <= 3) // Chunked responses
                {
                    mockHttpMessageHandler.Protected()
                        .Setup<Task<HttpResponseMessage>>(
                            "SendAsync",
                            ItExpr.IsAny<HttpRequestMessage>(),
                            ItExpr.IsAny<CancellationToken>()
                        )
                        .ReturnsAsync(new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(@"{
                                ""choices"": [{
                                    ""message"": {
                                        ""content"": ""{\""entries\"":[{\""title\"":\""条目" + callCount + @"\"",\""category\"":\""GenrePrinciple\"",\""content\"":\""内容\"",\""tags\"":[\""测试\""],\""weight\"":5}],\""summary\"":\""摘要\""}""
                                    }
                                }]
                            }", Encoding.UTF8, "application/json")
                        });
                }
                else // Aggregation response
                {
                    mockHttpMessageHandler.Protected()
                        .Setup<Task<HttpResponseMessage>>(
                            "SendAsync",
                            ItExpr.IsAny<HttpRequestMessage>(),
                            ItExpr.IsAny<CancellationToken>()
                        )
                        .ReturnsAsync(new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(@"{
                                ""choices"": [{
                                    ""message"": {
                                        ""content"": ""{\""deduplicated\"":[{\""title\"":\""去重条目\"",\""category\"":\""GenrePrinciple\"",\""content\"":\""内容\"",\""tags\"":[\""测试\""],\""weight\"":5}],\""aggregated\"":[{\""title\"":\""聚合条目\"",\""category\"":\""StyleExample\"",\""content\"":\""高阶规律\"",\""tags\"":[\""全局\""],\""weight\"":8}]}""
                                    }
                                }]
                            }", Encoding.UTF8, "application/json")
                        });
                }

                var httpClient = new HttpClient(mockHttpMessageHandler.Object);
                httpClient.Timeout = TimeSpan.FromSeconds(90);
                return httpClient;
            });

        // Mock knowledge service
        _mockKnowledgeService.Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse { Id = "test-id", UsageProjectId = "test-project", EntryType = "GenrePrinciple", Title = "Test", Content = "Test", CreatedAt = DateTime.UtcNow });

        // Act
        var result = await _service.ProcessFileAsync(task.Id);

        // Assert
        Assert.Contains("成功提取", result);

        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("processing", updatedTask!.Status);
        Assert.Equal("chunked", updatedTask.Strategy);
        Assert.True(updatedTask.TotalChunks > 1);
        Assert.Equal(updatedTask.TotalChunks, updatedTask.ProcessedChunks);
        Assert.True(updatedTask.Progress >= 90);
    }

    [Fact]
    public async Task ProcessFileAsync_DoesNotRecordProcessedKnowledgeAsProjectImportedMemory()
    {
        var shortContent = new string('测', 2000);
        var task = await CreateTaskWithUploadContentAsync(shortContent);
        var memoryEvents = new Mock<IAgentMemoryEventService>();
        var service = new KnowledgeProcessingService(
            _db,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _settingsManager,
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            new ContentDocumentService(_db),
            memoryEvents.Object);

        var mockResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(@"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""[{\""title\"":\""测试条目\"",\""category\"":\""GenrePrinciple\"",\""content\"":\""测试内容\"",\""tags\"":[\""测试\""],\""weight\"":5}]""
                    }
                }]
            }", Encoding.UTF8, "application/json")
        };
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);
        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(mockHttpMessageHandler.Object));
        _mockKnowledgeService
            .Setup(x => x.CreateExtractedKnowledgeAsync(It.IsAny<CreateExtractedKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse
            {
                Id = "test-id",
                UsageProjectId = "test-project",
                EntryType = "GenrePrinciple",
                Title = "Test",
                Content = "Test",
                CreatedAt = DateTime.UtcNow
            });

        await service.ProcessFileAsync(task.Id);

        memoryEvents.Verify(x => x.AppendAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "project",
                "imported_knowledge_ids",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test error handling when task not found.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_TaskNotFound_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.ProcessFileAsync("non-existent-task-id"));
    }

    /// <summary>
    /// Test error handling when LLM settings not configured.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_MissingLlmSettings_ThrowsException()
    {
        // Arrange
        var task = await CreateTaskWithUploadContentAsync("test content");

        await _settingsManager.SaveAsync(new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4",
            LlmApiKey = "", // Missing
            LlmTemperature = 0.7,
            LlmMaxTokens = 2000
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.ProcessFileAsync(task.Id));

        // Verify task marked as failed
        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("failed", updatedTask!.Status);
    }

    [Fact]
    public async Task ProcessFileAsync_FailureRecordsExecutionMemoryPattern()
    {
        var task = await CreateTaskWithUploadContentAsync("test content");
        var memoryRepository = new Mock<IAgentMemoryRepository>();
        memoryRepository
            .Setup(x => x.UnionMemoryAsync(
                "user-1",
                "test-project",
                It.IsAny<Dictionary<string, IReadOnlyList<string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new KnowledgeProcessingService(
            _db,
            _mockKnowledgeService.Object,
            _mockEmbedding.Object,
            _settingsManager,
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            new ContentDocumentService(_db),
            memoryEvents: null,
            memoryRepository.Object);

        await _settingsManager.SaveAsync(new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4",
            LlmApiKey = "",
            LlmTemperature = 0.7,
            LlmMaxTokens = 2000
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessFileAsync(task.Id));

        memoryRepository.Verify(x => x.UnionMemoryAsync(
                "user-1",
                "test-project",
                It.Is<Dictionary<string, IReadOnlyList<string>>>(updates => HasKnowledgeProcessingFailure(updates, task.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessPendingFileAsync_RejectsTaskOwnedByAnotherUser()
    {
        var task = await CreateTaskWithUploadContentAsync("知识库测试内容");

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.ProcessPendingFileAsync(task.Id, "other-user"));

        Assert.Contains("找不到指定的处理任务", ex.Message);
    }

    [Fact]
    public async Task ProcessPendingFileAsync_RejectsTaskThatIsNotPending()
    {
        var task = await CreateTaskWithUploadContentAsync("知识库测试内容");
        task.Status = "completed";
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ProcessPendingFileAsync(task.Id, task.UserId));

        Assert.Contains("任务状态为 completed", ex.Message);
    }

    private static bool HasKnowledgeProcessingFailure(
        Dictionary<string, IReadOnlyList<string>> updates,
        string taskId)
    {
        return updates.TryGetValue("execution.knowledge_processing_failures", out var failures) &&
               failures.Any(f => f.Contains(taskId, StringComparison.Ordinal) &&
                                 f.Contains("LLM settings are not configured", StringComparison.Ordinal));
    }

    private void SetupSingleEntryLlmResponse()
    {
        var mockResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(@"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""[{\""title\"":\""测试条目\"",\""category\"":\""GenrePrinciple\"",\""content\"":\""测试内容\"",\""tags\"":[\""测试\""],\""weight\"":5}]""
                    }
                }]
            }", Encoding.UTF8, "application/json")
        };

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(mockHttpMessageHandler.Object));
    }

    private async Task<TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask> CreateTaskWithUploadContentAsync(string content)
    {
        var task = new TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "user-1",
            ProjectId = "test-project",
            FileName = "uploaded.txt",
            FileSize = Encoding.UTF8.GetByteCount(content),
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.KnowledgeProcessingTasks.Add(task);
        await _db.SaveChangesAsync();

        await new ContentDocumentService(_db).SaveTextAsync(
            task.UserId,
            task.ProjectId,
            "knowledge_upload",
            task.Id,
            "upload_raw",
            task.FileName,
            content);

        return task;
    }
}
