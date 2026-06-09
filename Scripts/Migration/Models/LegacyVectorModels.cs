using System.Text.Json.Serialization;

namespace TM.Scripts.Migration.Models;

/// <summary>
/// Legacy vector embedding data from chapter_embeddings.json
/// </summary>
public class LegacyChapterEmbedding
{
    [JsonPropertyName("chapter_id")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("chapter_number")]
    public int? ChapterNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Legacy chunk embedding data from chunk_embeddings.json
/// </summary>
public class LegacyChunkEmbedding
{
    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; set; } = string.Empty;

    [JsonPropertyName("chapter_id")]
    public string? ChapterId { get; set; }

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Root structure for chapter embeddings file
/// </summary>
public class LegacyChapterEmbeddingsRoot
{
    [JsonPropertyName("embeddings")]
    public List<LegacyChapterEmbedding> Embeddings { get; set; } = new();

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Root structure for chunk embeddings file
/// </summary>
public class LegacyChunkEmbeddingsRoot
{
    [JsonPropertyName("embeddings")]
    public List<LegacyChunkEmbedding> Embeddings { get; set; } = new();

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Vector migration result
/// </summary>
public class VectorMigrationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public int ProjectsProcessed { get; set; }
    public int CollectionsCreated { get; set; }
    public int ChapterVectorsImported { get; set; }
    public int ChunkVectorsImported { get; set; }
    public int TotalVectorsImported { get; set; }
    public int SkippedInvalidVectors { get; set; }
    public string? BackupPath { get; set; }
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, ProjectVectorStats> ProjectStats { get; set; } = new();

    public override string ToString()
    {
        if (!Success)
        {
            return $"Vector migration failed: {ErrorMessage} (Duration: {Duration.TotalSeconds:F2}s)";
        }

        var warnings = Warnings.Count > 0 ? $"\nWarnings:\n  - {string.Join("\n  - ", Warnings)}" : "";

        return $@"Vector migration completed successfully in {Duration.TotalSeconds:F2}s
- Projects Processed: {ProjectsProcessed}
- Collections Created: {CollectionsCreated}
- Chapter Vectors: {ChapterVectorsImported}
- Chunk Vectors: {ChunkVectorsImported}
- Total Vectors: {TotalVectorsImported}
- Skipped (Invalid): {SkippedInvalidVectors}
- Backup Location: {BackupPath ?? "N/A"}{warnings}";
    }
}

/// <summary>
/// Per-project vector statistics
/// </summary>
public class ProjectVectorStats
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public int ChapterVectors { get; set; }
    public int ChunkVectors { get; set; }
    public int TotalVectors { get; set; }
    public bool CollectionCreated { get; set; }
    public bool VerificationPassed { get; set; }
}
