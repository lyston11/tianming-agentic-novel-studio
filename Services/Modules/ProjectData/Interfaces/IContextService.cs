using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Contexts.Generate;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IContextService
    {
        #region Design模块使用（读取Modules/原始数据）

        Task<SmartParsingContext> GetSmartParsingContextAsync();

        Task<TemplatesContext> GetTemplatesContextAsync();

        Task<WorldviewContext> GetWorldviewContextAsync();

        Task<CharacterContext> GetCharacterContextAsync();

        Task<FactionsContext> GetFactionsContextAsync();

        Task<LocationContext> GetLocationsContextAsync();

        Task<PlotContext> GetPlotContextAsync();

        Task<DesignData> GetDesignContextAsync();

        Task<string> GetCreativeMaterialsContextAsync();

        Task<string> GetCoreDesignContextAsync();

        #endregion

        #region Generate模块使用（读取Modules/原始数据）

        Task<OutlineContext> GetOutlineContextAsync();

        Task<PlanningContext> GetPlanningContextAsync();

        Task<BlueprintContext> GetBlueprintContextAsync();

        #endregion

        #region Validate模块使用

        Task<ValidationContext> GetValidationContextAsync(string chapterId);

        #endregion
    }
}
