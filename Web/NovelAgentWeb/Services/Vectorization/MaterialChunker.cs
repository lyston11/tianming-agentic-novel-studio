using System.Text;

namespace TM.Web.NovelAgentWeb.Services.Vectorization;

/// <summary>
/// Implements text chunking functionality for vectorization of large materials.
/// Splits long text into overlapping chunks suitable for embedding and vector storage.
/// </summary>
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

        var tokens = TokenizeForEmbedding(text);
        var chunks = new List<MaterialChunk>();
        var startIndex = 0;

        while (startIndex < tokens.Count)
        {
            var endIndex = startIndex;
            var tokenCount = 0;
            while (endIndex < tokens.Count)
            {
                var next = tokens[endIndex];
                if (tokenCount > 0 && tokenCount + next.EstimatedTokens > MaxTokensPerChunk)
                    break;

                tokenCount += next.EstimatedTokens;
                endIndex++;
            }

            if (endIndex == startIndex)
            {
                tokenCount = tokens[startIndex].EstimatedTokens;
                endIndex = startIndex + 1;
            }

            var chunkTokens = tokens.GetRange(startIndex, endIndex - startIndex);
            var chunkContent = string.Concat(chunkTokens.Select(t => t.Text)).Trim();

            chunks.Add(new MaterialChunk
            {
                ChunkId = $"{materialId}_chunk_{chunks.Count}",
                ChunkIndex = chunks.Count,
                ChunkTotal = 0, // Will be set after loop
                Content = chunkContent,
                TokenCount = tokenCount
            });

            // Move to next chunk with overlap
            if (endIndex >= tokens.Count)
            {
                break;
            }

            startIndex = Math.Max(startIndex + 1, endIndex - OverlapTokens);
        }

        // Set ChunkTotal for all chunks
        foreach (var chunk in chunks)
        {
            chunk.ChunkTotal = chunks.Count;
        }

        return chunks;
    }

    private static List<ChunkToken> TokenizeForEmbedding(string text)
    {
        var tokens = new List<ChunkToken>();
        var pendingSpace = false;
        var word = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                FlushWord(tokens, word, ref pendingSpace);
                if (tokens.Count > 0)
                    pendingSpace = true;
                continue;
            }

            if (IsStandaloneTextUnit(ch))
            {
                FlushWord(tokens, word, ref pendingSpace);
                var textUnit = pendingSpace ? $" {ch}" : ch.ToString();
                tokens.Add(new ChunkToken(textUnit, 1));
                pendingSpace = false;
                continue;
            }

            word.Append(ch);
            if (word.Length >= 80)
                FlushWord(tokens, word, ref pendingSpace);
        }

        FlushWord(tokens, word, ref pendingSpace);
        return tokens;
    }

    private static void FlushWord(List<ChunkToken> tokens, StringBuilder word, ref bool pendingSpace)
    {
        if (word.Length == 0)
            return;

        var value = word.ToString();
        var text = pendingSpace && tokens.Count > 0 ? $" {value}" : value;
        tokens.Add(new ChunkToken(text, EstimateLatinTokenCount(value)));
        word.Clear();
        pendingSpace = false;
    }

    private static int EstimateLatinTokenCount(string value) =>
        Math.Max(1, (int)Math.Ceiling(value.Length / 4.0));

    private static bool IsStandaloneTextUnit(char ch) =>
        IsCjk(ch) || IsWidePunctuation(ch);

    private static bool IsCjk(char ch) =>
        ch is >= '\u3400' and <= '\u9FFF' or
            >= '\uF900' and <= '\uFAFF' or
            >= '\u3040' and <= '\u30FF' or
            >= '\uAC00' and <= '\uD7AF';

    private static bool IsWidePunctuation(char ch) =>
        ch is >= '\u3000' and <= '\u303F' or
            >= '\uFF00' and <= '\uFFEF';

    private sealed record ChunkToken(string Text, int EstimatedTokens);
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
