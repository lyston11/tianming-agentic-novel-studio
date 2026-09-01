using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterData : BusinessDataBase, ICoreRuleSummaryProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 章节目标（Goal）

        [JsonPropertyName("ChapterTitle")]
        public string ChapterTitle { get; set; } = string.Empty;

        [JsonPropertyName("ChapterNumber")]
        public int ChapterNumber { get; set; } = 0;

        [JsonPropertyName("Volume")]
        public string Volume { get; set; } = string.Empty;

        [JsonPropertyName("ChapterTheme")]
        public string ChapterTheme { get; set; } = string.Empty;

        [JsonPropertyName("ReaderExperienceGoal")]
        public string ReaderExperienceGoal { get; set; } = string.Empty;

        [JsonPropertyName("MainGoal")]
        public string MainGoal { get; set; } = string.Empty;

        #endregion

        #region Tab2: 冲突与转折（Turn）

        [JsonPropertyName("ResistanceSource")]
        public string ResistanceSource { get; set; } = string.Empty;

        [JsonPropertyName("KeyTurn")]
        public string KeyTurn { get; set; } = string.Empty;

        [JsonPropertyName("Hook")]
        public string Hook { get; set; } = string.Empty;

        #endregion

        #region Tab3: 交付物（Deliverables）

        [JsonPropertyName("WorldInfoDrop")]
        public string WorldInfoDrop { get; set; } = string.Empty;

        [JsonPropertyName("CharacterArcProgress")]
        public string CharacterArcProgress { get; set; } = string.Empty;

        [JsonPropertyName("MainPlotProgress")]
        public string MainPlotProgress { get; set; } = string.Empty;

        [JsonPropertyName("Foreshadowing")]
        public string Foreshadowing { get; set; } = string.Empty;

        #endregion

        #region Tab4: 出场实体引用（EntityRefs）

        [JsonPropertyName("ReferencedCharacterNames")]
        public List<string> ReferencedCharacterNames { get; set; } = new();

        [JsonPropertyName("ReferencedFactionNames")]
        public List<string> ReferencedFactionNames { get; set; } = new();

        [JsonPropertyName("ReferencedLocationNames")]
        public List<string> ReferencedLocationNames { get; set; } = new();

        #endregion

        #region 摘要与描述

        public string Description => MainGoal;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ChapterTitle)) parts.Add(ChapterTitle);
            if (!string.IsNullOrWhiteSpace(MainGoal)) parts.Add($"目标: {MainGoal}");
            if (!string.IsNullOrWhiteSpace(ChapterTheme)) parts.Add($"主题: {ChapterTheme}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(MainGoal))
                return MainGoal;
            if (!string.IsNullOrWhiteSpace(ChapterTitle))
                return ChapterTitle;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(MainGoal) && !string.IsNullOrWhiteSpace(KeyTurn)) return 80;
            if (!string.IsNullOrWhiteSpace(ChapterTitle)) return 60;
            return 40;
        }

        #endregion
    }
}
