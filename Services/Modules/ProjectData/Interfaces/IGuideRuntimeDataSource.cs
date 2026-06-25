using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public static class GuideRuntimeDataKeys
    {
        public const string ContentGuide = "content";
        public const string OutlineGuide = "outline_guide";
        public const string PlanningGuide = "planning_guide";
        public const string BlueprintGuide = "blueprint_guide";
        public const string WorldRules = "world_rules";
        public const string Characters = "characters";
        public const string Factions = "factions";
        public const string Locations = "locations";
        public const string PlotRules = "plot_rules";
        public const string Outlines = "outlines";
        public const string ChapterPlans = "chapter_plans";
        public const string Blueprints = "blueprints";
        public const string VolumeDesigns = "volume_designs";
        public const string Templates = "templates";
    }

    public interface IGuideRuntimeDataSource
    {
        Task<T> LoadGuideAsync<T>(string guideKey) where T : new();

        Task<IReadOnlyList<T>> LoadItemsAsync<T>(string dataKey);
    }
}
