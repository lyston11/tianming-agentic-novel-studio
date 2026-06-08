using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Design.Location
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LocationRulesData : BusinessDataBase, ICoreRuleSummaryProvider, IContextStringProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 基本信息（Identity）

        [JsonPropertyName("LocationType")]
        public string LocationType { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("Scale")]
        public string Scale { get; set; } = string.Empty;

        #endregion

        #region Tab2: 地理特征（Geography）

        [JsonPropertyName("Terrain")]
        public string Terrain { get; set; } = string.Empty;

        [JsonPropertyName("Climate")]
        public string Climate { get; set; } = string.Empty;

        [JsonPropertyName("Landmarks")]
        public List<string> Landmarks { get; set; } = new();

        [JsonPropertyName("Resources")]
        public List<string> Resources { get; set; } = new();

        #endregion

        #region Tab3: 故事关联（Story）

        [JsonPropertyName("HistoricalSignificance")]
        public string HistoricalSignificance { get; set; } = string.Empty;

        [JsonPropertyName("Dangers")]
        public List<string> Dangers { get; set; } = new();

        #endregion

        #region 关联字段

        [JsonPropertyName("FactionId")]
        public string? FactionId { get; set; }

        #endregion

        #region 摘要与描述

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Name)) parts.Add(Name);
            if (!string.IsNullOrWhiteSpace(LocationType)) parts.Add($"类型: {LocationType}");
            if (!string.IsNullOrWhiteSpace(Description)) parts.Add($"描述: {Description}");
            if (!string.IsNullOrWhiteSpace(Terrain)) parts.Add($"地形: {Terrain}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(Description))
                return Description;
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(Description) && Landmarks.Count > 0) return 80;
            if (!string.IsNullOrWhiteSpace(Name)) return 60;
            return 40;
        }

        #endregion

        #region IContextStringProvider 实现

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<item name=\"{Name}\">");
            if (!string.IsNullOrWhiteSpace(LocationType))
                sb.AppendLine($"类型：{LocationType}");
            if (!string.IsNullOrWhiteSpace(Description))
                sb.AppendLine($"描述：{Description}");
            if (!string.IsNullOrWhiteSpace(Terrain))
                sb.AppendLine($"地形：{Terrain}");
            if (Landmarks.Count > 0)
                sb.AppendLine($"地标：{string.Join("、", Landmarks)}");
            sb.AppendLine("</item>");
            return sb.ToString();
        }

        public string ToBriefContextString()
        {
            return $"- **{Name}**：{GetCoreSummary()}";
        }

        #endregion
    }

}
