using TM.Web.NovelAgentWeb.Services.Vectorization;
using Xunit;

namespace NovelAgentRegression;

public class MaterialChunkerTests
{
    [Fact]
    public void ChunkText_ProducesCorrectOverlap()
    {
        var chunker = new MaterialChunker();
        var text = string.Join(" ", Enumerable.Range(1, 3000).Select(i => $"word{i}"));

        var chunks = chunker.ChunkText(text, "test-material");

        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count > 1, "Should produce multiple chunks for 3000 words");
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 1500, $"Chunk {c.ChunkIndex} has {c.TokenCount} tokens, should be <= 1500"));

        // Verify overlap: last 200 tokens of chunk N should match first 200 tokens of chunk N+1
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var currentChunkTokens = chunks[i].Content.Split(' ');
            var nextChunkTokens = chunks[i + 1].Content.Split(' ');
            var overlapSize = Math.Min(200, currentChunkTokens.Length);
            var currentOverlap = string.Join(" ", currentChunkTokens.TakeLast(overlapSize));
            var nextOverlap = string.Join(" ", nextChunkTokens.Take(overlapSize));
            Assert.Equal(currentOverlap, nextOverlap);
        }
    }
}
