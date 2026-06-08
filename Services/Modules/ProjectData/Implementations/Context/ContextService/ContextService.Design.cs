using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region Design

        public async Task<SmartParsingContext> GetSmartParsingContextAsync()
        {
            TM.App.Log("[ContextService] 构建SmartParsingContext");

            var context = new SmartParsingContext
            {
                BookAnalyses = await LoadFunctionDataAsync<BookAnalysisData>("BookAnalysis").ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] SmartParsingContext构建完成: BookAnalyses={context.BookAnalyses.Count}");
            return context;
        }

        public async Task<TemplatesContext> GetTemplatesContextAsync()
        {
            TM.App.Log("[ContextService] 构建TemplatesContext");

            var context = new TemplatesContext
            {
                CreativeMaterials = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials").ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] TemplatesContext构建完成: CreativeMaterials={context.CreativeMaterials.Count}");
            return context;
        }

        public async Task<WorldviewContext> GetWorldviewContextAsync()
        {
            TM.App.Log("[ContextService] 构建WorldviewContext");

            var templatesTask = LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesTask = LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            await Task.WhenAll(templatesTask, worldRulesTask).ConfigureAwait(false);

            var templates = new TemplatesContext { CreativeMaterials = await templatesTask.ConfigureAwait(false) };
            var context = new WorldviewContext
            {
                Templates = templates,
                WorldRules = await worldRulesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] WorldviewContext构建完成: WorldRules={context.WorldRules.Count}");
            return context;
        }

        public async Task<CharacterContext> GetCharacterContextAsync()
        {
            TM.App.Log("[ContextService] 构建CharacterContext");

            var templatesTask = LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesTask = LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesTask = LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            await Task.WhenAll(templatesTask, worldRulesTask, characterRulesTask).ConfigureAwait(false);

            var templates = new TemplatesContext { CreativeMaterials = await templatesTask.ConfigureAwait(false) };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = await worldRulesTask.ConfigureAwait(false) };
            var context = new CharacterContext
            {
                Templates = templates,
                Worldview = worldview,
                CharacterRules = await characterRulesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] CharacterContext构建完成");
            return context;
        }

        public async Task<FactionsContext> GetFactionsContextAsync()
        {
            TM.App.Log("[ContextService] 构建FactionsContext");

            var templatesTask = LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesTask = LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesTask = LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesTask = LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            await Task.WhenAll(templatesTask, worldRulesTask, characterRulesTask, factionRulesTask).ConfigureAwait(false);

            var templates = new TemplatesContext { CreativeMaterials = await templatesTask.ConfigureAwait(false) };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = await worldRulesTask.ConfigureAwait(false) };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = await characterRulesTask.ConfigureAwait(false) };
            var context = new FactionsContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                FactionRules = await factionRulesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] FactionsContext构建完成");
            return context;
        }

        public async Task<LocationContext> GetLocationsContextAsync()
        {
            TM.App.Log("[ContextService] 构建LocationContext");

            var templatesTask = LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesTask = LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesTask = LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesTask = LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locationRulesTask = LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            await Task.WhenAll(templatesTask, worldRulesTask, characterRulesTask, factionRulesTask, locationRulesTask).ConfigureAwait(false);

            var templates = new TemplatesContext { CreativeMaterials = await templatesTask.ConfigureAwait(false) };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = await worldRulesTask.ConfigureAwait(false) };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = await characterRulesTask.ConfigureAwait(false) };
            var factions = new FactionsContext { Templates = templates, Worldview = worldview, Characters = characters, FactionRules = await factionRulesTask.ConfigureAwait(false) };
            var context = new LocationContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                LocationRules = await locationRulesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] LocationContext构建完成: LocationRules={context.LocationRules.Count}");
            return context;
        }

        public async Task<PlotContext> GetPlotContextAsync()
        {
            TM.App.Log("[ContextService] 构建PlotContext");

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
            var context = new PlotContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                Locations = locations,
                PlotRules = await plotRulesTask.ConfigureAwait(false)
            };

            TM.App.Log($"[ContextService] PlotContext构建完成");
            return context;
        }

        public async Task<DesignData> GetDesignContextAsync()
        {
            TM.App.Log("[ContextService] 构建DesignContext");
            var designData = await BuildDesignDataAsync().ConfigureAwait(false);
            TM.App.Log($"[ContextService] DesignContext构建完成");
            return designData;
        }

        #endregion
    }
}
