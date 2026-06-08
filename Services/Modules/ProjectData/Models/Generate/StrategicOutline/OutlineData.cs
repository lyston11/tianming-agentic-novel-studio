using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class OutlineData : BusinessDataBase, ICoreRuleSummaryProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 全书定位（Positioning）

        [JsonPropertyName("TotalChapterCount")]
        public int TotalChapterCount { get; set; } = 0;

        [JsonPropertyName("OneLineOutline")]
        public string OneLineOutline { get; set; } = string.Empty;

        [JsonPropertyName("EmotionalTone")]
        public string EmotionalTone { get; set; } = string.Empty;

        [JsonPropertyName("PhilosophicalMotif")]
        public string PhilosophicalMotif { get; set; } = string.Empty;

        #endregion

        #region Tab2: 主题内核（Theme）

        [JsonPropertyName("Theme")]
        public string Theme { get; set; } = string.Empty;

        [JsonPropertyName("CoreConflict")]
        public string CoreConflict { get; set; } = string.Empty;

        [JsonPropertyName("EndingState")]
        public string EndingState { get; set; } = string.Empty;

        #endregion

        #region Tab3: 结构规划（Structure）

        [JsonPropertyName("VolumeDivision")]
        public string VolumeDivision { get; set; } = string.Empty;

        [JsonPropertyName("OutlineOverview")]
        public string OutlineOverview { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => OneLineOutline;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(OneLineOutline)) parts.Add(OneLineOutline);
            if (!string.IsNullOrWhiteSpace(CoreConflict)) parts.Add($"冲突: {CoreConflict}");
            if (!string.IsNullOrWhiteSpace(Theme)) parts.Add($"主题: {Theme}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(OneLineOutline))
                return OneLineOutline;
            if (!string.IsNullOrWhiteSpace(CoreConflict))
                return CoreConflict;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(CoreConflict) && !string.IsNullOrWhiteSpace(Theme)) return 80;
            if (!string.IsNullOrWhiteSpace(OneLineOutline)) return 60;
            return 40;
        }

        #endregion
    }
}
