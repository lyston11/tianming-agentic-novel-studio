using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts.Generate;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region Generate

        public async Task<OutlineContext> GetOutlineContextAsync()
        {
            TM.App.Log("[ContextService] 构建OutlineContext");

            var designTask = BuildDesignDataAsync();
            var outlinesTask = LoadFunctionDataAsync<Models.Generate.StrategicOutline.OutlineData>("Outline");
            await Task.WhenAll(designTask, outlinesTask).ConfigureAwait(false);

            var context = new OutlineContext
            {
                Design = await designTask.ConfigureAwait(false),
                Outlines = await outlinesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] OutlineContext构建完成");
            return context;
        }

        public async Task<PlanningContext> GetPlanningContextAsync()
        {
            TM.App.Log("[ContextService] 构建PlanningContext");

            var designTask = BuildDesignDataAsync();
            var outlineTask = BuildOutlineDataAsync();
            var chaptersTask = LoadFunctionDataAsync<Models.Generate.ChapterPlanning.ChapterData>("Chapter");
            await Task.WhenAll(designTask, outlineTask, chaptersTask).ConfigureAwait(false);

            var context = new PlanningContext
            {
                Design = await designTask.ConfigureAwait(false),
                Outline = await outlineTask.ConfigureAwait(false),
                Chapters = await chaptersTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] PlanningContext构建完成");
            return context;
        }

        public async Task<BlueprintContext> GetBlueprintContextAsync()
        {
            TM.App.Log("[ContextService] 构建BlueprintContext");

            var designTask = BuildDesignDataAsync();
            var outlineTask = BuildOutlineDataAsync();
            var planningTask = BuildPlanningDataAsync();
            var blueprintsTask = LoadFunctionDataAsync<Models.Generate.ChapterBlueprint.BlueprintData>("Blueprint");
            await Task.WhenAll(designTask, outlineTask, planningTask, blueprintsTask).ConfigureAwait(false);

            var context = new BlueprintContext
            {
                Design = await designTask.ConfigureAwait(false),
                Outline = await outlineTask.ConfigureAwait(false),
                Planning = await planningTask.ConfigureAwait(false),
                Blueprints = await blueprintsTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] BlueprintContext构建完成");
            return context;
        }

        #endregion
    }
}
