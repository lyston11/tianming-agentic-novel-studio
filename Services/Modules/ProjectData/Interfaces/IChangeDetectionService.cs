using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.ChangeDetection;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IChangeDetectionService
    {
        bool HasChanges(string modulePath);

        List<string> GetChangedModules();

        Task RefreshAllAsync();

        ChangeStatus GetStatus(string modulePath);

        List<ChangeStatus> GetAllStatuses();

        List<ChangeStatus> GetDesignStatuses();

        List<ChangeStatus> GetGenerateStatuses();

        List<ChangeStatus> GetValidateStatuses();

        void MarkAsPackaged(string modulePath);

        void MarkAllAsPackaged();

        void MarkModuleEnabled(string modulePath, bool isEnabled);
    }
}
