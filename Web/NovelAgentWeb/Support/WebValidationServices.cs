using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebUnifiedValidationService : IUnifiedValidationService
{
    public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default)
    {
        return Task.FromResult(new ChapterValidationResult
        {
            ChapterId = chapterId,
            OverallResult = "通过",
            ValidatedTime = DateTime.Now
        });
    }

    public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default)
    {
        return Task.FromResult(new VolumeValidationResult
        {
            VolumeNumber = volumeNumber,
            ValidatedTime = DateTime.Now
        });
    }

    public Task<bool> NeedsRepublishAsync() => Task.FromResult(false);
}

public sealed class UnavailableGeneratedContentService : IGeneratedContentService
{
    public Task SaveChapterAsync(string chapterId, string content) =>
        throw new InvalidOperationException("Web generated content service requires a request service scope.");

    public Task<string?> GetChapterAsync(string chapterId) => Task.FromResult<string?>(null);

    public Task<bool> DeleteChapterAsync(string chapterId) => Task.FromResult(false);

    public bool ChapterExists(string chapterId) => false;

    public Task<List<ChapterInfo>> GetGeneratedChaptersAsync() => Task.FromResult(new List<ChapterInfo>());

    public Task<bool> VolumeExistsAsync(int volumeNumber) => Task.FromResult(false);

    public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId) =>
        Task.FromResult("chapter-001");
}
