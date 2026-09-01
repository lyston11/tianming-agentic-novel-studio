using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public interface IVectorIndexRebuilder
{
    Task<int> RebuildUserAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class VectorIndexRebuilder : IVectorIndexRebuilder
{
    private readonly NovelAgentDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IMultiScaleVectorIndexer _indexer;
    private readonly IAuthoritativeStoryVectorIndexer _storyIndexer;
    private readonly ILegacyVectorSourceRebuilder _legacyRebuilder;

    public VectorIndexRebuilder(
        NovelAgentDbContext db,
        IVectorStore vectors,
        IMultiScaleVectorIndexer indexer,
        IAuthoritativeStoryVectorIndexer storyIndexer,
        ILegacyVectorSourceRebuilder legacyRebuilder)
    {
        _db = db;
        _vectors = vectors;
        _indexer = indexer;
        _storyIndexer = storyIndexer;
        _legacyRebuilder = legacyRebuilder;
    }

    public async Task<int> RebuildUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var documentIds = await _db.KnowledgeDocumentBlobs.AsNoTracking()
            .Where(blob => blob.UserId == userId && blob.Status == "processed")
            .OrderBy(blob => blob.KnowledgeVersion)
            .Select(blob => blob.Id)
            .ToListAsync(cancellationToken);
        await _vectors.DeleteUserCollectionAsync(userId, cancellationToken);
        var records = await _db.VectorIndexRecords.Where(record => record.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.VectorIndexRecords.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken);
        await _vectors.InitializeUserCollectionAsync(userId, cancellationToken);
        var count = 0;
        foreach (var documentId in documentIds)
            count += await _indexer.IndexDocumentAsync(userId, documentId, cancellationToken);
        count += await _storyIndexer.IndexUserAsync(userId, cancellationToken);
        count += await _legacyRebuilder.RebuildUserAsync(userId, cancellationToken);
        return count;
    }
}
