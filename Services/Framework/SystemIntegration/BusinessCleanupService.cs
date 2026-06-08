using System;
using System.Collections.Generic;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Modules.Design.GlobalSettings.WorldRules.Services;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Modules.Design.Templates.CreativeMaterials.Services;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;

namespace TM.Services.Framework.SystemIntegration
{
    public static class BusinessCleanupService
    {
        public static (bool Success, int ClearedCount, List<string> Details) Execute(bool includeBookAnalysis = true)
        {
            var details = new List<string>();
            var clearedCount = 0;
            var hasErrors = false;

            try
            {
                TM.App.Log($"[BusinessCleanupService] 开始执行业务清理 (includeBookAnalysis={includeBookAnalysis})...");

                if (includeBookAnalysis)
                    clearedCount += ClearModuleData("Design/SmartParsing/BookAnalysis", () => ServiceLocator.Get<BookAnalysisService>().ClearAllAnalysis(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/Templates/CreativeMaterials", () => ServiceLocator.Get<CreativeMaterialsService>().ClearAllMaterials(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/GlobalSettings/WorldRules", () => ServiceLocator.Get<WorldRulesService>().ClearAllWorldRules(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/Elements/CharacterRules", () => ServiceLocator.Get<CharacterRulesService>().ClearAllCharacterRules(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/Elements/FactionRules", () => ServiceLocator.Get<FactionRulesService>().ClearAllFactionRules(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/Elements/LocationRules", () => ServiceLocator.Get<LocationRulesService>().ClearAllLocationRules(), details, ref hasErrors);
                clearedCount += ClearModuleData("Design/Elements/PlotRules", () => ServiceLocator.Get<PlotRulesService>().ClearAllPlotRules(), details, ref hasErrors);

                clearedCount += ClearModuleData("Generate/GlobalSettings/Outline", () => ServiceLocator.Get<OutlineService>().ClearAllOutlines(), details, ref hasErrors);
                clearedCount += ClearModuleData("Generate/Elements/Chapter", () => ServiceLocator.Get<ChapterService>().ClearAllChapters(), details, ref hasErrors);
                clearedCount += ClearModuleData("Generate/Elements/Blueprint", () => ServiceLocator.Get<BlueprintService>().ClearAllBlueprints(), details, ref hasErrors);
                clearedCount += ClearModuleData("Generate/Elements/VolumeDesign", () => ServiceLocator.Get<VolumeDesignService>().ClearAllVolumeDesigns(), details, ref hasErrors);

                TM.App.Log($"[BusinessCleanupService] 业务清理完成，共清空 {clearedCount} 条业务数据");
                return (!hasErrors, clearedCount, details);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BusinessCleanupService] 业务清理失败: {ex.Message}");
                return (false, clearedCount, details);
            }
        }

        private static int ClearModuleData(string moduleName, Func<int> clearAction, List<string> details, ref bool hasErrors)
        {
            try
            {
                var count = clearAction();
                details.Add($"{moduleName}: {count}");
                TM.App.Log($"[BusinessCleanupService] 已清空 {moduleName}: {count}");
                return count;
            }
            catch (Exception ex)
            {
                hasErrors = true;
                details.Add($"{moduleName}: 失败({ex.Message})");
                TM.App.Log($"[BusinessCleanupService] 清理失败 {moduleName}: {ex.Message}");
                return 0;
            }
        }
    }
}
