using Microsoft.EntityFrameworkCore;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebChapterCatalogService : IChapterCatalogService
{
    private const string SourceType = "chapter";
    private const string DocumentRole = "chapter_body";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public WebChapterCatalogService(IServiceScopeFactory scopeFactory, string userId, string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<IReadOnlyList<string>> ListChapterIdsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ProjectChapters(db)
            .OrderBy(c => c.ChapterNumber)
            .ThenBy(c => c.CreatedAt)
            .Select(c => c.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<string> GetLatestChapterIdAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ProjectChapters(db)
            .OrderByDescending(c => c.ChapterNumber)
            .ThenByDescending(c => c.UpdatedAt)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? string.Empty;
    }

    public async Task<int> GetLastChapterNumberOfVolumeAsync(int volumeNumber, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ProjectChapters(db)
            .Where(c => c.Volume != null && c.Volume.VolumeNumber == volumeNumber)
            .Select(c => (int?)c.ChapterNumber)
            .MaxAsync(ct)
            .ConfigureAwait(false) ?? 0;
    }

    public async Task<string> GetChapterContentAsync(string chapterId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return string.Empty;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await ProjectChapters(db)
            .Where(c => c.Id == chapterId || c.Title == chapterId)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (chapter == null)
            return string.Empty;

        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        try
        {
            return await contentDocuments.GetTextAsync(
                    _userId,
                    _projectId,
                    SourceType,
                    chapter.Id,
                    DocumentRole,
                    ct)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return string.Empty;
        }
    }

    public async Task<bool> ChapterExistsAsync(string chapterId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ProjectChapters(db)
            .AnyAsync(c => c.Id == chapterId || c.Title == chapterId, ct)
            .ConfigureAwait(false);
    }

    private IQueryable<Chapter> ProjectChapters(NovelAgentDbContext db) =>
        db.Chapters
            .AsNoTracking()
            .Include(c => c.Project)
            .Include(c => c.Volume)
            .Where(c => c.ProjectId == _projectId && c.Project.UserId == _userId);
}
