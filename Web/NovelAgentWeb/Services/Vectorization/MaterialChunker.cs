namespace TM.Web.NovelAgentWeb.Services.Vectorization;

/// <summary>
/// Implements text chunking functionality for vectorization of large materials.
/// Splits long text into overlapping chunks suitable for embedding and vector storage.
/// </summary>
/// <remarks>
/// IMPORTANT: This implementation uses simplified whitespace-based tokenization.
/// Token counts are approximations and do NOT reflect actual LLM token counts,
/// especially for non-Latin scripts (Chinese, Japanese, etc.) where character-to-token
/// ratios differ significantly from English.
/// </remarks>
public class MaterialChunker : IMaterialChunker
{
    private const int MaxTokensPerChunk = 1500;
    private const int OverlapTokens = 200;

    /// <summary>
    /// Splits text into overlapping chunks suitable for embedding and vector storage.
    /// Uses a sliding window approach with configurable chunk size and overlap.
    /// </summary>
    /// <param name="text">The text content to chunk</param>
    /// <param name="materialId">The unique identifier of the material being chunked</param>
    /// <returns>A list of MaterialChunk objects with metadata including chunk IDs, indices, and token counts</returns>
    public List<MaterialChunk> ChunkText(string text, string materialId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<MaterialChunk>();
        }

        var tokens = TokenizeSimple(text);
        var chunks = new List<MaterialChunk>();
        var startIndex = 0;

        while (startIndex < tokens.Count)
        {
            var endIndex = Math.Min(startIndex + MaxTokensPerChunk, tokens.Count);
            var chunkTokens = tokens.GetRange(startIndex, endIndex - startIndex);
            var chunkContent = string.Join(" ", chunkTokens);

            chunks.Add(new MaterialChunk
            {
                ChunkId = $"{materialId}_chunk_{chunks.Count}",
                ChunkIndex = chunks.Count,
                ChunkTotal = 0, // Will be set after loop
                Content = chunkContent,
                TokenCount = chunkTokens.Count
            });

            // Move to next chunk with overlap
            if (endIndex >= tokens.Count)
            {
                break;
            }

            startIndex = endIndex - OverlapTokens;
        }

        // Set ChunkTotal for all chunks
        foreach (var chunk in chunks)
        {
            chunk.ChunkTotal = chunks.Count;
        }

        return chunks;
    }

    /// <summary>
    /// Performs simplified whitespace-based tokenization.
    /// </summary>
    /// <remarks>
    /// WARNING: This is a placeholder implementation for development/testing only.
    /// Token counts produced by this method are INACCURATE and do not reflect actual
    /// LLM tokenization behavior. Whitespace splitting is particularly problematic for:
    /// - Chinese text (each character may be 2-3 tokens)
    /// - Japanese text (mixing kanji/hiragana/katakana)
    /// - Code and technical content
    /// - Special characters and punctuation
    ///
    /// TODO: Replace with proper tokenizer integration:
    /// - For OpenAI models: Use tiktoken library (cl100k_base encoding for text-embedding-3-*)
    /// - For other providers: Use their respective tokenization libraries
    /// - Consider caching token counts to avoid repeated tokenization overhead
    ///
    /// FIXME: Add logging/metrics to track when this method produces significantly
    /// inaccurate counts (e.g., when chunk size exceeds embedding model limits).
    /// </remarks>
    private List<string> TokenizeSimple(string text)
    {
        // Simplified whitespace-based tokenization for development
        // This DOES NOT accurately represent actual token counts
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}

/// <summary>
/// Represents a single chunk of text material with metadata for vector storage.
/// </summary>
public class MaterialChunk
{
    /// <summary>
    /// Gets or sets the unique identifier for this chunk.
    /// Format: {materialId}_chunk_{index}
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the zero-based index of this chunk within the material.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Gets or sets the total number of chunks for the material.
    /// </summary>
    public int ChunkTotal { get; set; }

    /// <summary>
    /// Gets or sets the text content of this chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the approximate token count for this chunk.
    /// Note: This is based on simplified tokenization and may not accurately reflect actual LLM token counts.
    /// </summary>
    public int TokenCount { get; set; }
}
