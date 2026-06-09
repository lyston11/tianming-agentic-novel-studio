using System.Text.Json;
using Microsoft.Extensions.Logging;
using TM.Scripts.Migration.Models;

namespace TM.Scripts.Migration;

/// <summary>
/// Generates sample vector embedding files for testing migration
/// </summary>
public class SampleVectorGenerator
{
    private readonly ILogger<SampleVectorGenerator> _logger;
    private readonly string _appDataPath;
    private readonly int _vectorDimension;
    private readonly Random _random;

    public SampleVectorGenerator(
        ILogger<SampleVectorGenerator> logger,
        string appDataPath,
        int vectorDimension = 512)
    {
        _logger = logger;
        _appDataPath = appDataPath;
        _vectorDimension = vectorDimension;
        _random = new Random(42); // Fixed seed for reproducibility
    }

    /// <summary>
    /// Generate sample vector files for testing
    /// </summary>
    public async Task GenerateSampleVectorFilesAsync(int chapterCount = 5, int chunksPerChapter = 3)
    {
        _logger.LogInformation("Generating sample vector files...");

        // Create directory if needed
        var vectorDir = Path.Combine(_appDataPath, "Config/guides");
        Directory.CreateDirectory(vectorDir);

        // Generate chapter embeddings
        var chapterEmbeddings = new LegacyChapterEmbeddingsRoot
        {
            Model = "bge-small-zh-v1.5",
            Dimension = _vectorDimension,
            CreatedAt = DateTime.UtcNow,
            Embeddings = new List<LegacyChapterEmbedding>()
        };

        for (int i = 1; i <= chapterCount; i++)
        {
            chapterEmbeddings.Embeddings.Add(new LegacyChapterEmbedding
            {
                ChapterId = $"chapter_{i}",
                ChapterNumber = i,
                Title = $"第{i}章：测试章节标题",
                Content = $"这是第{i}章的示例内容，用于测试向量迁移功能。包含一些中文文本来模拟实际章节。",
                Embedding = GenerateRandomVector(),
                ProjectId = "AgenticNovelStudio",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
        }

        var chapterPath = Path.Combine(vectorDir, "chapter_embeddings.json");
        await File.WriteAllTextAsync(chapterPath, JsonSerializer.Serialize(chapterEmbeddings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated {Count} chapter embeddings at {Path}", chapterCount, chapterPath);

        // Generate chunk embeddings
        var chunkEmbeddings = new LegacyChunkEmbeddingsRoot
        {
            Model = "bge-small-zh-v1.5",
            Dimension = _vectorDimension,
            CreatedAt = DateTime.UtcNow,
            Embeddings = new List<LegacyChunkEmbedding>()
        };

        int chunkId = 1;
        for (int chapterNum = 1; chapterNum <= chapterCount; chapterNum++)
        {
            for (int chunkIdx = 0; chunkIdx < chunksPerChapter; chunkIdx++)
            {
                chunkEmbeddings.Embeddings.Add(new LegacyChunkEmbedding
                {
                    ChunkId = $"chunk_{chunkId}",
                    ChapterId = $"chapter_{chapterNum}",
                    ChunkIndex = chunkIdx,
                    Content = $"第{chapterNum}章第{chunkIdx + 1}个分块的内容。这是用于测试的示例文本块。",
                    Embedding = GenerateRandomVector(),
                    ProjectId = "AgenticNovelStudio",
                    CreatedAt = DateTime.UtcNow.AddDays(-chapterNum)
                });
                chunkId++;
            }
        }

        var chunkPath = Path.Combine(vectorDir, "chunk_embeddings.json");
        await File.WriteAllTextAsync(chunkPath, JsonSerializer.Serialize(chunkEmbeddings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated {Count} chunk embeddings at {Path}",
            chapterCount * chunksPerChapter, chunkPath);

        _logger.LogInformation("Sample vector files generated successfully");
    }

    private float[] GenerateRandomVector()
    {
        var vector = new float[_vectorDimension];

        // Generate normalized random vector
        double sumSquares = 0;
        for (int i = 0; i < _vectorDimension; i++)
        {
            vector[i] = (float)(_random.NextDouble() * 2 - 1); // Range: -1 to 1
            sumSquares += vector[i] * vector[i];
        }

        // Normalize to unit length
        var magnitude = Math.Sqrt(sumSquares);
        for (int i = 0; i < _vectorDimension; i++)
        {
            vector[i] = (float)(vector[i] / magnitude);
        }

        return vector;
    }

    /// <summary>
    /// Clean up generated sample files
    /// </summary>
    public void CleanupSampleFiles()
    {
        var vectorDir = Path.Combine(_appDataPath, "Config/guides");

        var chapterPath = Path.Combine(vectorDir, "chapter_embeddings.json");
        if (File.Exists(chapterPath))
        {
            File.Delete(chapterPath);
            _logger.LogInformation("Deleted sample chapter embeddings");
        }

        var chunkPath = Path.Combine(vectorDir, "chunk_embeddings.json");
        if (File.Exists(chunkPath))
        {
            File.Delete(chunkPath);
            _logger.LogInformation("Deleted sample chunk embeddings");
        }
    }
}
