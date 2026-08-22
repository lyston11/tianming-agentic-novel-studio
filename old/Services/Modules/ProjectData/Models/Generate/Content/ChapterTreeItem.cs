using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generate.Content
{
    public class VolumeTreeItem
    {
        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; }
        [JsonPropertyName("Name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("Icon")]
        public string Icon { get; set; } = "Icon.Books";
        [JsonPropertyName("IsExpanded")]
        public bool IsExpanded { get; set; } = false;
        public ObservableCollection<ChapterTreeItem> Chapters { get; } = new();
    }

    public class ChapterTreeItem
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = "";
        [JsonPropertyName("Title")]
        public string Title { get; set; } = "";
        [JsonPropertyName("Icon")]
        public string Icon { get; set; } = "Icon.Document";
        public string DisplayName => string.IsNullOrEmpty(Title) ? ChapterId : Title;
    }
}
