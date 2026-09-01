using TM.Web.NovelAgentWeb.Services.Vectorization;
using Xunit;

namespace Tests.Unit.Services.Vectorization;

public sealed class MaterialChunkerTests
{
    [Fact]
    public void ChunkText_SplitsLongChineseTextIntoBudgetedChunks()
    {
        var chunker = new MaterialChunker();
        var text = string.Concat(Enumerable.Repeat("天命知识库需要承接项目设定、角色事实和章节连续性。", 260));

        var chunks = chunker.ChunkText(text, "knowledge-1");

        Assert.True(chunks.Count > 1, "Long Chinese text must not be treated as one whitespace token.");
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 1500));
        Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= 1700));
        Assert.All(chunks, chunk => Assert.Equal(chunks.Count, chunk.ChunkTotal));
    }
}
