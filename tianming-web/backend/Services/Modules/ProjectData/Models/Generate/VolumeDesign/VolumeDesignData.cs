using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeDesignData : BusinessDataBase, ICoreRuleSummaryProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 卷定位（Position）

        [JsonPropertyName("VolumeNumber")]
        public int VolumeNumber { get; set; } = 0;

        [JsonPropertyName("VolumeTitle")]
        public string VolumeTitle { get; set; } = string.Empty;

        [JsonPropertyName("VolumeTheme")]
        public string VolumeTheme { get; set; } = string.Empty;

        [JsonPropertyName("StageGoal")]
        public string StageGoal { get; set; } = string.Empty;

        [JsonPropertyName("TargetChapterCount")]
        public int TargetChapterCount { get; set; } = 0;

        [JsonPropertyName("StartChapter")]
        public int StartChapter { get; set; } = 0;

        [JsonPropertyName("EndChapter")]
        public int EndChapter { get; set; } = 0;

        #endregion

        #region Tab2: 冲突与关键节点（Conflict）

        [JsonPropertyName("MainConflict")]
        public string MainConflict { get; set; } = string.Empty;

        [JsonPropertyName("PressureSource")]
        public string PressureSource { get; set; } = string.Empty;

        [JsonPropertyName("KeyEvents")]
        public string KeyEvents { get; set; } = string.Empty;

        [JsonPropertyName("OpeningState")]
        public string OpeningState { get; set; } = string.Empty;

        [JsonPropertyName("EndingState")]
        public string EndingState { get; set; } = string.Empty;

        #endregion

        #region Tab4: 出场实体引用（EntityRefs）

        [JsonPropertyName("ReferencedCharacterNames")]
        public List<string> ReferencedCharacterNames { get; set; } = new();

        [JsonPropertyName("ReferencedFactionNames")]
        public List<string> ReferencedFactionNames { get; set; } = new();

        [JsonPropertyName("ReferencedLocationNames")]
        public List<string> ReferencedLocationNames { get; set; } = new();

        #endregion

        #region Tab3: 章节分配与剧情结构（Allocation）

        [JsonPropertyName("ChapterAllocationOverview")]
        public string ChapterAllocationOverview { get; set; } = string.Empty;

        [JsonPropertyName("PlotAllocation")]
        public string PlotAllocation { get; set; } = string.Empty;

        [JsonPropertyName("ChapterGenerationHints")]
        public string ChapterGenerationHints { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => string.IsNullOrWhiteSpace(StageGoal) ? VolumeTitle : StageGoal;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (VolumeNumber > 0) parts.Add($"第{VolumeNumber}卷");
            if (!string.IsNullOrWhiteSpace(VolumeTitle)) parts.Add(VolumeTitle);
            if (!string.IsNullOrWhiteSpace(VolumeTheme)) parts.Add($"主题: {VolumeTheme}");
            if (!string.IsNullOrWhiteSpace(StageGoal)) parts.Add($"目标: {StageGoal}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(StageGoal))
                return StageGoal;
            if (!string.IsNullOrWhiteSpace(VolumeTitle))
                return VolumeTitle;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (VolumeNumber > 0 && TargetChapterCount > 0) return 80;
            if (!string.IsNullOrWhiteSpace(VolumeTitle)) return 60;
            return 40;
        }

        #endregion
    }
}
