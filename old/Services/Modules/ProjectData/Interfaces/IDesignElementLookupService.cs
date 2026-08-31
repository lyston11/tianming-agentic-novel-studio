using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IDesignElementLookupService
    {
        Task<string?> ResolveCharacterNameAsync(string characterId);

        Task<string?> ResolveLocationNameAsync(string locationId);

        Task<string?> ResolveFactionNameAsync(string factionId);

        Task<string?> ResolveConflictNameAsync(string conflictId);
    }
}
