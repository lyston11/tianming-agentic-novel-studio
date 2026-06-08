using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IFocusContextService
    {
        Task<DesignFocusContext> GetDesignContextAsync(string focusId, string targetLayer);

        Task<GenerateFocusContext> GetGenerateContextAsync(string focusId, string targetLayer);

        Task<GlobalSummary> GetGlobalSummaryAsync();

        Task<TrackingStatus> GetTrackingStatusAsync();

        Task<DesignFocusContext> GetSmartParsingContextAsync(string focusId);

        Task<DesignFocusContext> GetTemplatesContextAsync(string focusId);

        Task<DesignFocusContext> GetWorldviewContextAsync(string focusId);

        Task<DesignFocusContext> GetCharactersContextAsync(string focusId);

        Task<DesignFocusContext> GetFactionsContextAsync(string focusId);

        Task<DesignFocusContext> GetPlotContextAsync(string focusId);

        Task<GenerateFocusContext> GetOutlineContextAsync(string focusId);

        Task<GenerateFocusContext> GetPlanningContextAsync(string focusId);

        Task<GenerateFocusContext> GetBlueprintContextAsync(string focusId);

        Task<GenerateFocusContext> GetContentContextAsync(string focusId);

        void InvalidateCache();
    }
}
