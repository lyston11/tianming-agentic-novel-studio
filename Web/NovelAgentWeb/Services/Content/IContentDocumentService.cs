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

    Task<ContentDocument> SaveOrReplaceTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        string title,
        string content,
        CancellationToken ct = default);

    Task<string> GetTextAsync(
        string userId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default);

    Task<string> GetTextAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default);

    Task DeleteBySourceAsync(
        string userId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default);

    Task DeleteBySourceAsync(
        string userId,
        string? projectId,
        string sourceType,
        string sourceId,
        string documentRole,
        CancellationToken ct = default);
}
