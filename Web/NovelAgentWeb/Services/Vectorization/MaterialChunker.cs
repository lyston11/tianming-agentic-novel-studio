namespace TM.Web.NovelAgentWeb.Services.Vectorization;

public class MaterialChunker
{
    private const int MaxTokensPerChunk = 1500;
    private const int OverlapTokens = 200;

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

    private List<string> TokenizeSimple(string text)
    {
        // Simple whitespace tokenization
        // In production, use a proper tokenizer (e.g., tiktoken for OpenAI models)
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}

public class MaterialChunk
{
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int ChunkTotal { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
}
