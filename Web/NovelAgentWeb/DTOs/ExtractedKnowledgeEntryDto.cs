namespace TM.Web.NovelAgentWeb.DTOs;

/// <summary>
/// DTO for a single extracted knowledge entry from LLM analysis.
/// </summary>
public class ExtractedKnowledgeEntryDto
{
    /// <summary>
    /// Knowledge title (should be within 10 characters).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Knowledge category type.
    /// Valid values: GenrePrinciple, TropePattern, AntiTropeStrategy, StyleExample.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Detailed content of the knowledge entry (50-200 characters recommended).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// List of tags associated with this knowledge entry.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Importance weight of this knowledge entry (1-10, default 5).
    /// </summary>
    public int Weight { get; set; } = 5;

    /// <summary>
    /// Original text excerpt from the source material (optional).
    /// </summary>
    public string? OriginalText { get; set; }
}

/// <summary>
/// DTO for the result of analyzing a single chunk of a long file.
/// </summary>
public class ChunkedAnalysisResultDto
{
    /// <summary>
    /// List of knowledge entries extracted from this chunk.
    /// </summary>
    public List<ExtractedKnowledgeEntryDto> Entries { get; set; } = new();

    /// <summary>
    /// Summary of this chunk's content, used as context for the next chunk.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// DTO for the aggregated result of processing all chunks of a long file.
/// </summary>
public class AggregatedAnalysisResultDto
{
    /// <summary>
    /// Deduplicated knowledge entries from all chunks.
    /// </summary>
    public List<ExtractedKnowledgeEntryDto> Deduplicated { get; set; } = new();

    /// <summary>
    /// Newly generated knowledge entries from full-file analysis and aggregation.
    /// </summary>
    public List<ExtractedKnowledgeEntryDto> Aggregated { get; set; } = new();
}
