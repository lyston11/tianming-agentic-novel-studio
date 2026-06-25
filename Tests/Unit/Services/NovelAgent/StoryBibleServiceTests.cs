using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using Xunit;
using CoreStoryBibleService = TM.Services.Framework.AI.NovelAgent.Services.StoryBibleService;

namespace Tests.Unit.Services.NovelAgent;

public sealed class StoryBibleServiceTests
{
    [Fact]
    public void Constructor_RequiresExplicitDocumentStore()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CoreStoryBibleService(null!));

        Assert.Equal("documentStore", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PropagatesDocumentStoreFailuresInsteadOfReturningEmptyBible()
    {
        var service = new CoreStoryBibleService(new ThrowingStoryBibleDocumentStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LoadAsync(CancellationToken.None));

        Assert.Equal("database unavailable", exception.Message);
    }

    private sealed class ThrowingStoryBibleDocumentStore : IStoryBibleDocumentStore
    {
        public Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("database unavailable");

        public Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
