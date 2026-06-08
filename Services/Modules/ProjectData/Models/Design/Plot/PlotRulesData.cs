using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;

namespace TM.Services.Modules.ProjectData.Models.Design.Plot
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class PlotRulesData : BusinessDataBase, Worldview.ICoreRuleSummaryProvider, IContextStringProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 事件概览（Overview）

        [JsonPropertyName("TargetVolume")]
        public string TargetVolume { get; set; } = string.Empty;

        [JsonPropertyName("AssignedVolume")]
        public string AssignedVolume { get; set; } = string.Empty;

        [JsonPropertyName("OneLineSummary")]
        public string OneLineSummary { get; set; } = string.Empty;

        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("StoryPhase")]
        public string StoryPhase { get; set; } = string.Empty;

        [JsonPropertyName("PrerequisitesTrigger")]
        public string PrerequisitesTrigger { get; set; } = string.Empty;

        #endregion

        #region Tab2: 参与方（Cast & Stage）

        [JsonPropertyName("MainCharacters")]
        public string MainCharacters { get; set; } = string.Empty;

        [JsonPropertyName("KeyNpcs")]
        public string KeyNpcs { get; set; } = string.Empty;

        [JsonPropertyName("Location")]
        public string Location { get; set; } = string.Empty;

        [JsonPropertyName("TimeDuration")]
        public string TimeDuration { get; set; } = string.Empty;

        #endregion

        #region Tab3: 情节流程（Steps）

        [JsonPropertyName("StepTitle")]
        public string StepTitle { get; set; } = string.Empty;

        [JsonPropertyName("Goal")]
        public string Goal { get; set; } = string.Empty;

        [JsonPropertyName("Conflict")]
        public string Conflict { get; set; } = string.Empty;

        [JsonPropertyName("Result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("EmotionCurve")]
        public string EmotionCurve { get; set; } = string.Empty;

        #endregion

        #region Tab4: 事件影响（Payoff）

        [JsonPropertyName("MainPlotPush")]
        public string MainPlotPush { get; set; } = string.Empty;

        [JsonPropertyName("CharacterGrowth")]
        public string CharacterGrowth { get; set; } = string.Empty;

        [JsonPropertyName("WorldReveal")]
        public string WorldReveal { get; set; } = string.Empty;

        [JsonPropertyName("RewardsClues")]
        public string RewardsClues { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => OneLineSummary;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(OneLineSummary)) parts.Add(OneLineSummary);
            if (!string.IsNullOrWhiteSpace(EventType)) parts.Add($"类型: {EventType}");
            if (!string.IsNullOrWhiteSpace(Goal)) parts.Add($"目标: {Goal}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(OneLineSummary))
                return OneLineSummary;
            if (!string.IsNullOrWhiteSpace(Goal))
                return Goal;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(Conflict) && !string.IsNullOrWhiteSpace(Goal)) return 80;
            if (!string.IsNullOrWhiteSpace(OneLineSummary)) return 60;
            return 40;
        }

        #endregion

        #region IContextStringProvider 实现

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<item name=\"{Name}\">");
            if (!string.IsNullOrWhiteSpace(AssignedVolume))
                sb.AppendLine($"所属卷：{AssignedVolume}");
            if (!string.IsNullOrWhiteSpace(OneLineSummary))
                sb.AppendLine($"简介：{OneLineSummary}");
            if (!string.IsNullOrWhiteSpace(EventType))
                sb.AppendLine($"类型：{EventType}");
            if (!string.IsNullOrWhiteSpace(Goal))
                sb.AppendLine($"目标：{Goal}");
            if (!string.IsNullOrWhiteSpace(Conflict))
                sb.AppendLine($"冲突：{Conflict}");
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
