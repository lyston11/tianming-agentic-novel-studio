using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IPublishService
    {
        Task<PublishResult> PublishAllAsync();

        Task<PublishResult> PublishModuleAsync(string moduleName);

        PublishStatus GetPublishStatus();

        ManifestInfo? GetManifest();

        Task<ManifestInfo?> GetManifestAsync();

        bool NeedsRepublish();

        void ClearCache();
    }
}
