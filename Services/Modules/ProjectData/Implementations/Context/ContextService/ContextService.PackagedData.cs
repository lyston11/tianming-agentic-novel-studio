using System;
using System.IO;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region PackagedData

        private async Task<DesignData> LoadPackagedDesignDataAsync()
        {
            var cacheKey = BuildPackagedCacheKey("Design");
            var cached = await _sessionCache.GetOrLoadAsync(cacheKey, async () =>
            {
                var designData = new DesignData();

                try
                {
                    var configPath = GetProjectConfigPath("Design");

                    designData.Templates = new TemplatesContext();

                    var worldRulesTask = LoadPackagedDataAsync<WorldRulesData>(configPath, "globalsettings.json", "worldrules");
                    var characterRulesTask = LoadPackagedDataAsync<CharacterRulesData>(configPath, "elements.json", "characterrules");
                    var factionRulesTask = LoadPackagedDataAsync<FactionRulesData>(configPath, "elements.json", "factionrules");
                    var locationRulesTask = LoadPackagedDataAsync<LocationRulesData>(configPath, "elements.json", "locationrules");
                    var plotRulesTask = LoadPackagedDataAsync<PlotRulesData>(configPath, "elements.json", "plotrules");
                    await Task.WhenAll(worldRulesTask, characterRulesTask, factionRulesTask, locationRulesTask, plotRulesTask).ConfigureAwait(false);

                    designData.Worldview = new WorldviewContext { WorldRules = await worldRulesTask.ConfigureAwait(false) };
                    designData.Characters = new CharacterContext { CharacterRules = await characterRulesTask.ConfigureAwait(false) };
                    designData.Factions = new FactionsContext { FactionRules = await factionRulesTask.ConfigureAwait(false) };
                    designData.Locations = new LocationContext { LocationRules = await locationRulesTask.ConfigureAwait(false) };
                    designData.Plot = new PlotContext { PlotRules = await plotRulesTask.ConfigureAwait(false) };
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContextService] 加载打包Design数据失败: {ex.Message}");
                }

                return designData;
            }).ConfigureAwait(false);

            return cached ?? new DesignData();
        }

        private async Task<GenerateData> LoadPackagedGenerateDataAsync()
        {
            var cacheKey = BuildPackagedCacheKey("Generate");
            var cached = await _sessionCache.GetOrLoadAsync(cacheKey, async () =>
            {
                var generateData = new GenerateData();

                try
                {
                    var configPath = GetProjectConfigPath("Generate");

                    var outlineTask = LoadPackagedDataAsync<Models.Generate.StrategicOutline.OutlineData>(configPath, "globalsettings.json", "outline");
                    var chapterTask = LoadPackagedDataAsync<Models.Generate.ChapterPlanning.ChapterData>(configPath, "elements.json", "chapter");
                    var blueprintTask = LoadPackagedDataAsync<Models.Generate.ChapterBlueprint.BlueprintData>(configPath, "elements.json", "blueprint");
                    var volumeDesignTask = LoadPackagedDataAsync<Models.Generate.VolumeDesign.VolumeDesignData>(configPath, "elements.json", "volumedesign");
                    await Task.WhenAll(outlineTask, chapterTask, blueprintTask, volumeDesignTask).ConfigureAwait(false);

                    generateData.Outline = new OutlineDataAggregate { Outlines = await outlineTask.ConfigureAwait(false) };
                    generateData.Planning = new PlanningData { Chapters = await chapterTask.ConfigureAwait(false) };
                    generateData.Blueprint = new BlueprintDataAggregate { Blueprints = await blueprintTask.ConfigureAwait(false) };
                    generateData.VolumeDesign = new VolumeDesignDataAggregate { VolumeDesigns = await volumeDesignTask.ConfigureAwait(false) };
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContextService] 加载打包Generate数据失败: {ex.Message}");
                }

                return generateData;
            }).ConfigureAwait(false);

            return cached ?? new GenerateData();
        }

        private string BuildPackagedCacheKey(string section)
        {
            var projectName = StoragePathHelper.CurrentProjectName;
            return $"{PackagedCacheLayer}_{projectName}_{section}";
        }

        private async Task<string> LoadGeneratedContentAsync(int volumeNumber, int chapterNumber)
        {
            try
            {
                var filePath = Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"vol{volumeNumber}_ch{chapterNumber}.md");
                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载生成内容失败: {ex.Message}");
            }

            return string.Empty;
        }

        #endregion
    }
}
