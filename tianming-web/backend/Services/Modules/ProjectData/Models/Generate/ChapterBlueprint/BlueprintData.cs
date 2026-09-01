using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class BlueprintData : BusinessDataBase, ICoreRuleSummaryProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 蓝图概览（Overview）

        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;

        [JsonPropertyName("OneLineStructure")]
        public string OneLineStructure { get; set; } = string.Empty;

        [JsonPropertyName("PacingCurve")]
        public string PacingCurve { get; set; } = string.Empty;

        #endregion

        #region Tab2: 场景列表（Scenes）

        [JsonPropertyName("SceneNumber")]
        public int SceneNumber { get; set; } = 0;

        [JsonPropertyName("SceneTitle")]
        public string SceneTitle { get; set; } = string.Empty;

        [JsonPropertyName("PovCharacter")]
        public string PovCharacter { get; set; } = string.Empty;

        [JsonPropertyName("Opening")]
        public string Opening { get; set; } = string.Empty;

        [JsonPropertyName("Development")]
        public string Development { get; set; } = string.Empty;

        [JsonPropertyName("Turning")]
        public string Turning { get; set; } = string.Empty;

        [JsonPropertyName("Ending")]
        public string Ending { get; set; } = string.Empty;

        [JsonPropertyName("InfoDrop")]
        public string InfoDrop { get; set; } = string.Empty;

        #endregion

        #region Tab3: 要素清单（Elements）- ID列表版

        [JsonPropertyName("Cast")]
        public string Cast { get; set; } = string.Empty;

        [JsonPropertyName("Locations")]
        public string Locations { get; set; } = string.Empty;

        [JsonPropertyName("Factions")]
        public string Factions { get; set; } = string.Empty;

        [JsonPropertyName("ItemsClues")]
        public string ItemsClues { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => OneLineStructure;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SceneTitle)) parts.Add(SceneTitle);
            if (!string.IsNullOrWhiteSpace(OneLineStructure)) parts.Add($"结构: {OneLineStructure}");
            if (!string.IsNullOrWhiteSpace(PovCharacter)) parts.Add($"视角: {PovCharacter}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(OneLineStructure))
                return OneLineStructure;
            if (!string.IsNullOrWhiteSpace(SceneTitle))
                return SceneTitle;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(OneLineStructure) && !string.IsNullOrWhiteSpace(SceneTitle)) return 80;
            if (!string.IsNullOrWhiteSpace(SceneTitle)) return 60;
            return 40;
        }

        #endregion
    }
}
