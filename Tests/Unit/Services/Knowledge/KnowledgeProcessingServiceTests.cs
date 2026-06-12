using System.Net;
using System.Text;
using Moq;
using Moq.Protected;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Support;

namespace Tests.Unit.Services.Knowledge;

/// <summary>
/// Unit tests for KnowledgeProcessingService.
/// Tests token estimation, JSON parsing, chunking logic, and LLM integration.
/// </summary>
public class KnowledgeProcessingServiceTests : IDisposable
{
    private readonly NovelAgentDbContext _db;
    private readonly Mock<IKnowledgeService> _mockKnowledgeService;
    private readonly Mock<IMicroEmbeddingService> _mockEmbedding;
    private readonly UserSettingsManager _settingsManager;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<KnowledgeProcessingService>> _mockLogger;
    private readonly KnowledgeProcessingService _service;
    private readonly string _tempSettingsPath;

    public KnowledgeProcessingServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new NovelAgentDbContext(options);

        // Create a real UserSettingsManager with temp path
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempSettingsPath);
        _settingsManager = new UserSettingsManager(_tempSettingsPath, "test-project", null, null);

        // Write default settings file
        var settingsDir = Path.Combine(_tempSettingsPath, "Projects", "test-project", "Settings");
        Directory.CreateDirectory(settingsDir);
        var settingsFile = Path.Combine(settingsDir, "user_settings.json");
        File.WriteAllText(settingsFile, System.Text.Json.JsonSerializer.Serialize(new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4",
            LlmApiKey = "test-key",
            LlmTemperature = 0.7,
            LlmMaxTokens = 2000
        }));

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
            _mockLogger.Object
        );
    }

    public void Dispose()
    {
        _db.Dispose();

        // Clean up temp settings directory
        if (Directory.Exists(_tempSettingsPath))
        {
            Directory.Delete(_tempSettingsPath, true);
        }

        GC.SuppressFinalize(this);
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

    /// <summary>
    /// Test JSON parsing returns empty list on invalid JSON.
    /// </summary>
    [Fact]
    public void ParseEntriesFromJson_ReturnsEmptyOnInvalidJson()
    {
        // Arrange
        var invalidJson = "This is not valid JSON {[}";
        var method = typeof(KnowledgeProcessingService)
            .GetMethod("ParseEntriesFromJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var entries = (List<ExtractedKnowledgeEntryDto>)method!.Invoke(_service, new object[] { invalidJson })!;

        // Assert
        Assert.Empty(entries);
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
        // Arrange: Create a test file with short content
        var testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        var shortContent = new string('测', 2000); // ~1500 tokens
        await File.WriteAllTextAsync(testFilePath, shortContent);

        var task = new TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = "test-project",
            FilePath = testFilePath,
            Status = "pending"
        };
        _db.KnowledgeProcessingTasks.Add(task);
        await _db.SaveChangesAsync();

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
        _mockKnowledgeService.Setup(x => x.CreateKnowledgeAsync(It.IsAny<CreateKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse { Id = "test-id", ProjectId = "test-project", EntryType = "GenrePrinciple", Title = "Test", Content = "Test", CreatedAt = DateTime.UtcNow });

        // Act
        var result = await _service.ProcessFileAsync(task.Id);

        // Assert
        Assert.Contains("成功提取", result);
        Assert.Contains("1", result);

        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("completed", updatedTask!.Status);
        Assert.Equal("single_pass", updatedTask.Strategy);
        Assert.Equal(1, updatedTask.ExtractedEntriesCount);

        // Cleanup
        File.Delete(testFilePath);
    }

    /// <summary>
    /// Integration test: ProcessFileAsync with long file (chunked strategy).
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_LongFile_UsesChunkedStrategy()
    {
        // Arrange: Create a test file with long content (>6000 tokens)
        var testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        var longContent = new string('测', 10000); // ~7500 tokens
        await File.WriteAllTextAsync(testFilePath, longContent);

        var task = new TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = "test-project",
            FilePath = testFilePath,
            Status = "pending"
        };
        _db.KnowledgeProcessingTasks.Add(task);
        await _db.SaveChangesAsync();

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
        _mockKnowledgeService.Setup(x => x.CreateKnowledgeAsync(It.IsAny<CreateKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResponse { Id = "test-id", ProjectId = "test-project", EntryType = "GenrePrinciple", Title = "Test", Content = "Test", CreatedAt = DateTime.UtcNow });

        // Act
        var result = await _service.ProcessFileAsync(task.Id);

        // Assert
        Assert.Contains("成功提取", result);

        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("completed", updatedTask!.Status);
        Assert.Equal("chunked", updatedTask.Strategy);
        Assert.True(updatedTask.TotalChunks > 1);
        Assert.Equal(updatedTask.TotalChunks, updatedTask.ProcessedChunks);
        Assert.Equal(100, updatedTask.Progress);

        // Cleanup
        File.Delete(testFilePath);
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
        var testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(testFilePath, "test content");

        var task = new TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = "test-project",
            FilePath = testFilePath,
            Status = "pending"
        };
        _db.KnowledgeProcessingTasks.Add(task);
        await _db.SaveChangesAsync();

        // Write settings with missing API key
        var settingsFile = Path.Combine(_tempSettingsPath, "Projects", "test-project", "Settings", "user_settings.json");
        File.WriteAllText(settingsFile, System.Text.Json.JsonSerializer.Serialize(new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4",
            LlmApiKey = "", // Missing
            LlmTemperature = 0.7,
            LlmMaxTokens = 2000
        }));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.ProcessFileAsync(task.Id));

        // Verify task marked as failed
        var updatedTask = await _db.KnowledgeProcessingTasks.FindAsync(task.Id);
        Assert.Equal("failed", updatedTask!.Status);

        // Cleanup
        File.Delete(testFilePath);
    }
}
