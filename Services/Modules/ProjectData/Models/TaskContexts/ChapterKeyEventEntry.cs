using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class ChapterKeyEventEntry
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; }

        [JsonPropertyName("ChapterNumber")]
        public int ChapterNumber { get; set; }

        [JsonPropertyName("Characters")]
        public List<string> Characters { get; set; } = new();

        [JsonPropertyName("Turnings")]
        public List<string> Turnings { get; set; } = new();

        [JsonPropertyName("Foreshadows")]
        public List<string> Foreshadows { get; set; } = new();

        [JsonPropertyName("Factions")]
        public List<string> Factions { get; set; } = new();

        public string ToCompactLine(int maxLength = 200)
        {
            var prefix = (VolumeNumber > 0 && ChapterNumber > 0)
                ? $"[第{VolumeNumber}卷第{ChapterNumber}章]"
                : ChapterNumber > 0 ? $"[第{ChapterNumber}章]" : $"[{ChapterId}]";
            var parts = new System.Collections.Generic.List<string>();

            if (Characters.Count > 0)
                parts.Add(string.Join("/", Characters));
            if (Turnings.Count > 0)
                parts.Add("情节:" + string.Join("/", Turnings));
            if (Foreshadows.Count > 0)
                parts.Add("伏笔:" + string.Join("/", Foreshadows));
            if (Factions.Count > 0)
                parts.Add("势力:" + string.Join("/", Factions));

            if (parts.Count == 0) return string.Empty;

            var line = prefix + " " + string.Join(" | ", parts);
            if (line.Length > maxLength)
                line = line.Substring(0, maxLength - 1) + "…";
            return line;
        }
    }
}
