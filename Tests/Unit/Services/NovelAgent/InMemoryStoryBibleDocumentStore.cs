using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;

namespace Tests.Unit.Services.NovelAgent;

internal sealed class InMemoryStoryBibleDocumentStore : IStoryBibleDocumentStore
{
    private StoryBibleDocument? _document;

    public Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(Clone(_document));

    public Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default)
    {
        _document = Clone(document) ?? new StoryBibleDocument();
        return Task.CompletedTask;
    }

    private static StoryBibleDocument? Clone(StoryBibleDocument? document) =>
        document == null
            ? null
            : JsonSerializer.Deserialize<StoryBibleDocument>(JsonSerializer.Serialize(document));
}
