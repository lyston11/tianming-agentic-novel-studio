using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IGeneratedContentService
    {
        Task SaveChapterAsync(string chapterId, string content);

        Task<string?> GetChapterAsync(string chapterId);

        Task<List<ChapterInfo>> GetGeneratedChaptersAsync();

        Task<bool> DeleteChapterAsync(string chapterId);

        bool ChapterExists(string chapterId);

        string GetChapterPath(string chapterId);

        #region 分类（卷）管理

        Task<bool> VolumeExistsAsync(int volumeNumber);

        Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId);

        #endregion
    }
}
