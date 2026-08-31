using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary
{
    public static class ValidationRules
    {
        public static readonly string[] AllModuleNames = new[]
        {
            "StyleConsistency",
            "WorldviewConsistency",
            "CharacterConsistency",
            "FactionConsistency",
            "LocationConsistency",
            "PlotConsistency",
            "OutlineConsistency",
            "ChapterPlanConsistency",
            "BlueprintConsistency",
            "VolumeDesignConsistency"
        };

        public static readonly Dictionary<string, string> DisplayNames = new()
        {
            ["StyleConsistency"] = "文风模板一致性",
            ["WorldviewConsistency"] = "世界观一致性",
            ["CharacterConsistency"] = "角色设定一致性",
            ["FactionConsistency"] = "势力设定一致性",
            ["LocationConsistency"] = "地点设定一致性",
            ["PlotConsistency"] = "剧情规则一致性",
            ["OutlineConsistency"] = "大纲一致性",
            ["ChapterPlanConsistency"] = "章节规划一致性",
            ["BlueprintConsistency"] = "章节蓝图一致性",
            ["VolumeDesignConsistency"] = "分卷设计一致性"
        };

        public static readonly Dictionary<string, string[]> ExtendedDataSchemas = new()
        {
            ["StyleConsistency"] = new[] { "TemplateName", "Genre", "OverallIdea", "StyleHint" },
            ["WorldviewConsistency"] = new[] { "WorldRuleName", "HardRules", "PowerSystem", "SpecialLaws" },
            ["CharacterConsistency"] = new[] { "CharacterName", "Identity", "CoreTraits", "ArcGoal" },
            ["FactionConsistency"] = new[] { "FactionName", "FactionType", "Goal", "Leader" },
            ["LocationConsistency"] = new[] { "LocationName", "LocationType", "Description", "Terrain" },
            ["PlotConsistency"] = new[] { "PlotName", "StoryPhase", "Goal", "Conflict", "Result" },
            ["OutlineConsistency"] = new[] { "OneLineOutline", "CoreConflict", "Theme", "EndingState" },
            ["ChapterPlanConsistency"] = new[] { "ChapterTitle", "MainGoal", "KeyTurn", "Hook", "Foreshadowing" },
            ["BlueprintConsistency"] = new[] { "ChapterId", "OneLineStructure", "PacingCurve", "Cast", "Locations" },
            ["VolumeDesignConsistency"] = new[] { "VolumeTitle", "VolumeTheme", "StageGoal", "MainConflict", "KeyEvents" }
        };

        public static string GetDisplayName(string moduleName)
        {
            return DisplayNames.TryGetValue(moduleName, out var displayName) ? displayName : moduleName;
        }

        public static string[] GetExtendedDataSchema(string moduleName)
        {
            return ExtendedDataSchemas.TryGetValue(moduleName, out var schema) ? schema : System.Array.Empty<string>();
        }

        public static int TotalRuleCount => AllModuleNames.Length;
    }
}
