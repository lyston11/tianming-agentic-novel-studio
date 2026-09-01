using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IGeneratedContentService
    {
        Task SaveChapterAsync(string chapterId, string content);

        Task<string?> GetChapterAsync(string chapterId);

        Task<bool> DeleteChapterAsync(string chapterId);

        bool ChapterExists(string chapterId);

        Task<List<ChapterInfo>> GetGeneratedChaptersAsync();

        #region 分类（卷）管理

        Task<bool> VolumeExistsAsync(int volumeNumber);

        Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId);

        #endregion
    }

    public interface IGeneratedChapterMetadataWriter
    {
        Task SaveChapterAsync(string chapterId, string content, string? title);
    }

    public interface IAtomicGeneratedChapterCommitService : IGeneratedChapterMetadataWriter
    {
        Task SaveChapterAtomicallyAsync(
            string chapterId,
            string content,
            string? title,
            IReadOnlyList<GeneratedChapterOutboxWrite> outboxWrites);
    }

    public sealed record GeneratedChapterOutboxWrite(
        string RuntimeRunId,
        string EventType,
        string AggregateType,
        string AggregateId,
        string PayloadJson,
        string? IdempotencyKey = null);
}
