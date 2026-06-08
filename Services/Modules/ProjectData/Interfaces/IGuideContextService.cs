using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Index;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IGuideContextService
    {
        Task InitializeCacheAsync();
        void ClearCache();

        Task<List<CharacterRulesData>> ExtractCharactersAsync(List<string>? ids);
        Task<List<CharacterRulesData>> GetAllCharactersAsync();

        Task<List<LocationRulesData>> ExtractLocationsAsync(List<string>? ids);
        Task<List<LocationRulesData>> GetAllLocationsAsync();

        Task<List<PlotRulesData>> ExtractPlotRulesAsync(List<string>? ids);
        Task<List<PlotRulesData>> GetAllPlotRulesAsync();

        Task<List<FactionRulesData>> ExtractFactionsAsync(List<string>? ids);
        Task<List<FactionRulesData>> GetAllFactionsAsync();

        Task<List<CreativeMaterialData>> ExtractTemplatesAsync(List<string>? ids);
        Task<List<CreativeMaterialData>> GetAllTemplatesAsync();

        Task<List<WorldRulesData>> ExtractWorldRulesAsync(List<string>? ids);
        Task<List<WorldRulesData>> GetAllWorldRulesAsync();

        Task<OutlineData> ExtractVolumeAsync(string volumeId);
        Task<ChapterData?> ExtractChapterPlanAsync(string chapterPlanId);
        Task<List<BlueprintData>> ExtractBlueprintsAsync(List<string>? blueprintIds);
        Task<VolumeDesignData?> ExtractVolumeDesignAsync(string volumeDesignId);
        Task<List<OutlineData>> ExtractPreviousOutlinesAsync(List<string> outlineIds);

        Task<ContextIdValidationResult> ValidateContextIdsAsync(ContextIdCollection? contextIds);

        Task<OutlineTaskContext?> BuildOutlineContextAsync(string volumeId);
        Task<PlanningTaskContext?> BuildPlanningContextAsync(string volumeId);
        Task<BlueprintTaskContext?> BuildBlueprintContextAsync(string chapterId);
        Task<ContentTaskContext?> BuildContentContextAsync(string chapterId, CancellationToken ct = default);

        Task<string?> GetChapterTitleAsync(string chapterId);
        Task<int> GetVolumeMaxChapterAsync(int volumeNumber);
        Task<FactSnapshot> ExtractFactSnapshotForChapterAsync(string chapterId, ContextIdCollection contextIds);

        Task<ContentGuide> GetContentGuideAsync();
        void InvalidateContentGuideCache();

        Task<string> GetChapterSummaryAsync(string chapterId);

        Task<(List<IndexItem> Direct, List<IndexItem> Indirect)> GetRelatedEntitiesAsync(string focusId, string layer);
    }
}
