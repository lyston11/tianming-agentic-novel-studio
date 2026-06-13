using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Content;
using Xunit;

namespace Tests.Unit.Services.Content;

public class ContentDocumentServiceTests
{
    [Fact]
    public async Task SaveTextAsync_CreatesDocumentChunksAndPendingVectorRows()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var service = new ContentDocumentService(db);

        var document = await service.SaveTextAsync(
            "user-1",
            "project-1",
            "knowledge",
            "knowledge-1",
            "raw_upload",
            "素材.txt",
            "第一段\n\n第二段",
            CancellationToken.None);

        Assert.Equal("knowledge", document.SourceType);
        Assert.Equal(2, await db.ContentChunks.CountAsync());
        Assert.Equal(2, await db.ContentVectorPoints.CountAsync());
        Assert.All(await db.ContentVectorPoints.ToListAsync(), p => Assert.Equal("pending", p.IndexStatus));
    }
}
