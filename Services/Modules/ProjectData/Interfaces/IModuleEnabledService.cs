using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IModuleEnabledService
    {
        Task<int> SetModuleEnabledAsync(string moduleType, string subModule, bool enabled);

        Task<bool> GetModuleEnabledAsync(string moduleType, string subModule);

        Task<(int enabledCount, int totalCount)> GetModuleEnabledStatsAsync(string moduleType, string subModule);

        Task<int> SetAllSubModulesEnabledAsync(string moduleType, bool enabled);
    }
}
