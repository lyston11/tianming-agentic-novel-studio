using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;

namespace TM.Services.Modules.ProjectData.Models.Design.Worldview
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class WorldRulesData : BusinessDataBase, ICoreRuleSummaryProvider, IContextStringProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 核心设定（Rules）

        [JsonPropertyName("OneLineSummary")]
        public string OneLineSummary { get; set; } = string.Empty;

        public string Description => OneLineSummary;

        [JsonPropertyName("PowerSystem")]
        public string PowerSystem { get; set; } = string.Empty;

        [JsonPropertyName("Cosmology")]
        public string Cosmology { get; set; } = string.Empty;

        [JsonPropertyName("SpecialLaws")]
        public string SpecialLaws { get; set; } = string.Empty;

        [JsonPropertyName("HardRules")]
        public string HardRules { get; set; } = string.Empty;

        [JsonPropertyName("SoftRules")]
        public string SoftRules { get; set; } = string.Empty;

        #endregion

        #region Tab2: 历史/时间线（Timeline）

        [JsonPropertyName("AncientEra")]
        public string AncientEra { get; set; } = string.Empty;

        [JsonPropertyName("KeyEvents")]
        public string KeyEvents { get; set; } = string.Empty;

        [JsonPropertyName("ModernHistory")]
        public string ModernHistory { get; set; } = string.Empty;

        [JsonPropertyName("StatusQuo")]
        public string StatusQuo { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(OneLineSummary)) parts.Add(OneLineSummary);
            if (!string.IsNullOrWhiteSpace(PowerSystem)) parts.Add($"力量体系: {PowerSystem}");
            if (!string.IsNullOrWhiteSpace(HardRules)) parts.Add($"硬规则: {HardRules}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(OneLineSummary))
                return OneLineSummary;
            if (!string.IsNullOrWhiteSpace(HardRules))
                return HardRules;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(HardRules)) return 80;
            if (!string.IsNullOrWhiteSpace(OneLineSummary)) return 60;
            return 40;
        }

        #endregion

        #region IContextStringProvider 实现

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<item name=\"{Name}\">");
            if (!string.IsNullOrWhiteSpace(OneLineSummary))
                sb.AppendLine($"简介：{OneLineSummary}");
            if (!string.IsNullOrWhiteSpace(PowerSystem))
                sb.AppendLine($"力量体系：{PowerSystem}");
            if (!string.IsNullOrWhiteSpace(HardRules))
                sb.AppendLine($"硬规则：{HardRules}");
            if (!string.IsNullOrWhiteSpace(SoftRules))
                sb.AppendLine($"软规则：{SoftRules}");
            sb.AppendLine("</item>");
            return sb.ToString();
        }

        public string ToBriefContextString()
        {
            return $"- **{Name}**：{GetCoreSummary()}";
        }

        #endregion
    }

    public interface ICoreRuleSummaryProvider
    {
        string GetCoreSummary();
        int GetImportanceWeight();
    }
}
