using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Content;

public interface IContentDocumentService
{
    Task<ContentDocument> SaveTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        CancellationToken ct = default);

    Task<string?> GetDocumentContentAsync(string documentId, CancellationToken ct = default);

    Task<string?> GetDocumentContentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default);

    Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);

    Task DeleteDocumentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default);

    Task<ContentDocument?> GetDocumentBySourceAsync(string sourceType, string sourceId, CancellationToken ct = default);
}
