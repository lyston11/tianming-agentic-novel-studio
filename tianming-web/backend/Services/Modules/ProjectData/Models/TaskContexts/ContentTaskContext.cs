using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Tracking;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public enum ContentContextMode
    {
        Unknown = 0,
        Full = 1,
        PackageOnly = 2,
        MdOnly = 3
    }

    public class ContentTaskContext
    {
        [JsonPropertyName("ContextMode")]
        public ContentContextMode ContextMode { get; set; } = ContentContextMode.Unknown;
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Summary")]
        public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("Characters")]
        public List<CharacterRulesData> Characters { get; set; } = new();
        [JsonPropertyName("Locations")]
        public List<LocationRulesData> Locations { get; set; } = new();
        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();
        [JsonPropertyName("Factions")]
        public List<FactionRulesData> Factions { get; set; } = new();
        [JsonPropertyName("WorldRules")]
        public List<WorldRulesData> WorldRules { get; set; } = new();
        [JsonPropertyName("Templates")]
        public List<CreativeMaterialData> Templates { get; set; } = new();
        [JsonPropertyName("VolumeOutline")]
        public OutlineData? VolumeOutline { get; set; }
        [JsonPropertyName("ChapterPlan")]
        public ChapterData? ChapterPlan { get; set; }
        [JsonPropertyName("Blueprints")]
        public List<BlueprintData> Blueprints { get; set; } = new();
        [JsonPropertyName("VolumeDesign")]
        public VolumeDesignData? VolumeDesign { get; set; }
        [JsonPropertyName("PreviousChapterSummary")]
        public string PreviousChapterSummary { get; set; } = string.Empty;
        [JsonPropertyName("Rhythm")]
        public RhythmInfo? Rhythm { get; set; }
        [JsonPropertyName("Scenes")]
        public List<SceneGuideEntry> Scenes { get; set; } = new();

        [JsonPropertyName("PreviousChapterSummaries")]
        public List<ChapterSummaryEntry> PreviousChapterSummaries { get; set; } = new();

        [JsonPropertyName("MdPreviousChapterSummaries")]
        public List<ChapterSummaryEntry> MdPreviousChapterSummaries { get; set; } = new();

        [JsonPropertyName("PreviousChapterTail")]
        public string PreviousChapterTail { get; set; } = string.Empty;
        [JsonPropertyName("PreviousChapterId")]
        public string PreviousChapterId { get; set; } = string.Empty;

        [JsonPropertyName("ExpandedCharacters")]
        public List<CharacterRulesData> ExpandedCharacters { get; set; } = new();
        [JsonPropertyName("IsKeySceneExpanded")]
        public bool IsKeySceneExpanded { get; set; } = false;

        [JsonPropertyName("FactSnapshot")]
        public FactSnapshot? FactSnapshot { get; set; }

        [JsonPropertyName("ContextIds")]
        public ContextIdCollection? ContextIds { get; set; }

        [JsonPropertyName("HistoricalMilestones")]
        public List<VolumeMilestoneEntry> HistoricalMilestones { get; set; } = new();

        [JsonPropertyName("LongDistanceRecallFragments")]
        public List<LongDistanceRecallFragment> LongDistanceRecallFragments { get; set; } = new();

        [JsonPropertyName("PreviousVolumeArchives")]
        public List<VolumeFactArchive> PreviousVolumeArchives { get; set; } = new();

        [JsonPropertyName("StateDivergenceWarnings")]
        public List<string> StateDivergenceWarnings { get; set; } = new();

        [JsonPropertyName("CompressedKeyEvents")]
        public List<ChapterKeyEventEntry> CompressedKeyEvents { get; set; } = new();

        [JsonPropertyName("FirstDescriptionSnippets")]
        public List<FirstDescriptionSnippet> FirstDescriptionSnippets { get; set; } = new();

        [JsonIgnore]
        public string? RepairHints { get; set; }
    }

    public class LongDistanceRecallFragment
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Content")]
        public string Content { get; set; } = string.Empty;
        [JsonPropertyName("Score")]
        public double Score { get; set; }

        [JsonPropertyName("Category")]
        public string? Category { get; set; }
    }

    public enum RecallCategory
    {
        Foreshadowing = 0,
        Character = 1,
        General = 2
    }

    public record FirstDescriptionSnippet(string EntityId, string EntityName, string ChapterId, string Content);
}
