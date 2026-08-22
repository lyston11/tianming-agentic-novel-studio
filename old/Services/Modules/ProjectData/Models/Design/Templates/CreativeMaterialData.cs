using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Models.Design.Templates
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CreativeMaterialData : IIndexable, IDataItem, IDependencyTracked
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("DependencyModuleVersions")] public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Lightbulb";
        [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;

        [JsonPropertyName("SourceBookName")] public string SourceBookName { get; set; } = string.Empty;

        [JsonPropertyName("Genre")] public string Genre { get; set; } = string.Empty;

        [JsonPropertyName("OverallIdea")] public string OverallIdea { get; set; } = string.Empty;

        [JsonPropertyName("NovelSynopsis")] public string NovelSynopsis { get; set; } = string.Empty;

        [JsonPropertyName("WorldBuildingMethod")] public string WorldBuildingMethod { get; set; } = string.Empty;

        [JsonPropertyName("PowerSystemDesign")] public string PowerSystemDesign { get; set; } = string.Empty;

        [JsonPropertyName("EnvironmentDescription")] public string EnvironmentDescription { get; set; } = string.Empty;

        [JsonPropertyName("FactionDesign")] public string FactionDesign { get; set; } = string.Empty;

        [JsonPropertyName("WorldviewHighlights")] public string WorldviewHighlights { get; set; } = string.Empty;

        [JsonPropertyName("ProtagonistDesign")] public string ProtagonistDesign { get; set; } = string.Empty;

        [JsonPropertyName("SupportingRoles")] public string SupportingRoles { get; set; } = string.Empty;

        [JsonPropertyName("CharacterRelations")] public string CharacterRelations { get; set; } = string.Empty;

        [JsonPropertyName("GoldenFingerDesign")] public string GoldenFingerDesign { get; set; } = string.Empty;

        [JsonPropertyName("CharacterHighlights")] public string CharacterHighlights { get; set; } = string.Empty;

        [JsonPropertyName("PlotStructure")] public string PlotStructure { get; set; } = string.Empty;

        [JsonPropertyName("ConflictDesign")] public string ConflictDesign { get; set; } = string.Empty;

        [JsonPropertyName("ClimaxArrangement")] public string ClimaxArrangement { get; set; } = string.Empty;

        [JsonPropertyName("ForeshadowingTechnique")] public string ForeshadowingTechnique { get; set; } = string.Empty;

        [JsonPropertyName("PlotHighlights")] public string PlotHighlights { get; set; } = string.Empty;

        public string GetItemType() => "素材";

        public string GetDeepSummary()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(SourceBookName)) parts.Add($"来源:{SourceBookName}");
            if (!string.IsNullOrEmpty(OverallIdea))
                parts.Add(OverallIdea.Length > 50 ? OverallIdea[..50] + "..." : OverallIdea);
            return string.Join("。", parts);
        }

        public string GetBriefSummary() => $"{Name}(素材)";
    }
}
