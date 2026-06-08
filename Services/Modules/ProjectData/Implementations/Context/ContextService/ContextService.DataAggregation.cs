using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region DataAggregation

        private async Task<DesignData> BuildDesignDataAsync()
        {
            var templatesTask = LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesTask = LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesTask = LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesTask = LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locationRulesTask = LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            var plotRulesTask = LoadFunctionDataAsync<PlotRulesData>("PlotRules");
            await Task.WhenAll(templatesTask, worldRulesTask, characterRulesTask, factionRulesTask, locationRulesTask, plotRulesTask).ConfigureAwait(false);

            var templates = new TemplatesContext { CreativeMaterials = await templatesTask.ConfigureAwait(false) };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = await worldRulesTask.ConfigureAwait(false) };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = await characterRulesTask.ConfigureAwait(false) };
            var factions = new FactionsContext { Templates = templates, Worldview = worldview, Characters = characters, FactionRules = await factionRulesTask.ConfigureAwait(false) };
            var locations = new LocationContext { Templates = templates, Worldview = worldview, Characters = characters, Factions = factions, LocationRules = await locationRulesTask.ConfigureAwait(false) };
            var plot = new PlotContext { Templates = templates, Worldview = worldview, Characters = characters, Factions = factions, Locations = locations, PlotRules = await plotRulesTask.ConfigureAwait(false) };

            return new DesignData
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                Locations = locations,
                Plot = plot
            };
        }

        private async Task<OutlineDataAggregate> BuildOutlineDataAsync()
        {
            return new OutlineDataAggregate
            {
                Outlines = await LoadDataListAsync<Models.Generate.StrategicOutline.OutlineData>(
                    "Modules/Generate/GlobalSettings/Outline", "outline_data.json").ConfigureAwait(false)
            };
        }

        private async Task<PlanningData> BuildPlanningDataAsync()
        {
            return new PlanningData
            {
                Chapters = await LoadDataListAsync<Models.Generate.ChapterPlanning.ChapterData>(
                    "Modules/Generate/Elements/Chapter", "chapter_data.json").ConfigureAwait(false)
            };
        }

        #endregion
    }
}
