using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Models.Design.Templates
{
    public class ShortStoryBlueprintData : IIndexable, IDataItem, IDependencyTracked
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("DependencyModuleVersions")] public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Book";
        [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;

        [JsonPropertyName("SourceBookName")] public string SourceBookName { get; set; } = string.Empty;
        [JsonPropertyName("Genre")] public string Genre { get; set; } = string.Empty;
        [JsonPropertyName("TotalChapters")] public string TotalChapters { get; set; } = string.Empty;
        [JsonPropertyName("WordsPerChapter")] public string WordsPerChapter { get; set; } = string.Empty;
        [JsonPropertyName("ToneGuide")] public string ToneGuide { get; set; } = string.Empty;
        [JsonPropertyName("Synopsis")] public string Synopsis { get; set; } = string.Empty;

        [JsonPropertyName("ChapterBlueprints")]
        public List<ShortStoryChapterBlueprint> ChapterBlueprints { get; set; } = new();

        public string GetItemType() => "短篇蓝图";

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(SourceBookName)) parts.Add($"来源:{SourceBookName}");
            if (!string.IsNullOrEmpty(Genre)) parts.Add($"题材:{Genre}");
            if (int.TryParse(TotalChapters, out var tc) && tc > 0) parts.Add($"共{tc}章");
            if (!string.IsNullOrEmpty(Synopsis))
                parts.Add(Synopsis.Length > 60 ? Synopsis[..60] + "..." : Synopsis);
            return string.Join("。", parts);
        }

        public string GetBriefSummary() => $"{Name}(短篇蓝图,{TotalChapters}章)";
    }
}
