using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;

namespace TM.Services.Modules.ProjectData.Models.Design.Characters
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CharacterRulesData : BusinessDataBase, Worldview.ICoreRuleSummaryProvider, IContextStringProvider, IDependencyTracked
    {
        [JsonPropertyName("DependencyModuleVersions")]
        public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #region Tab1: 基本信息（Identity）

        [JsonPropertyName("CharacterType")]
        public string CharacterType { get; set; } = string.Empty;

        [JsonPropertyName("Gender")]
        public string Gender { get; set; } = string.Empty;

        [JsonPropertyName("Age")]
        public string Age { get; set; } = string.Empty;

        [JsonPropertyName("Identity")]
        public string Identity { get; set; } = string.Empty;

        [JsonPropertyName("Race")]
        public string Race { get; set; } = string.Empty;

        [JsonPropertyName("Appearance")]
        public string Appearance { get; set; } = string.Empty;

        [JsonPropertyName("Personality")]
        public string Personality { get; set; } = string.Empty;

        #endregion

        #region Tab2: 人物弧光/成长轨迹（Arc）

        [JsonPropertyName("Want")]
        public string Want { get; set; } = string.Empty;

        [JsonPropertyName("Need")]
        public string Need { get; set; } = string.Empty;

        [JsonPropertyName("FlawBelief")]
        public string FlawBelief { get; set; } = string.Empty;

        [JsonPropertyName("GrowthPath")]
        public string GrowthPath { get; set; } = string.Empty;

        #endregion

        #region Tab3: 关系/关系网（Relationships）

        [JsonPropertyName("TargetCharacterName")]
        public string TargetCharacterName { get; set; } = string.Empty;

        [JsonPropertyName("RelationshipType")]
        public string RelationshipType { get; set; } = string.Empty;

        [JsonPropertyName("EmotionDynamic")]
        public string EmotionDynamic { get; set; } = string.Empty;

        [JsonPropertyName("Relationships")]
        public string Relationships { get; set; } = string.Empty;

        #endregion

        #region Tab4: 能力/技能（Abilities）

        [JsonPropertyName("CombatSkills")]
        public string CombatSkills { get; set; } = string.Empty;

        [JsonPropertyName("NonCombatSkills")]
        public string NonCombatSkills { get; set; } = string.Empty;

        [JsonPropertyName("SpecialAbilities")]
        public string SpecialAbilities { get; set; } = string.Empty;

        #endregion

        #region Tab5: 装备/资产（Assets）

        [JsonPropertyName("SignatureItems")]
        public string SignatureItems { get; set; } = string.Empty;

        [JsonPropertyName("CommonItems")]
        public string CommonItems { get; set; } = string.Empty;

        [JsonPropertyName("PersonalAssets")]
        public string PersonalAssets { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => Identity;

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(CharacterType)) parts.Add($"类型: {CharacterType}");
            if (!string.IsNullOrWhiteSpace(Identity)) parts.Add($"身份: {Identity}");
            if (!string.IsNullOrWhiteSpace(Race)) parts.Add($"种族: {Race}");
            if (!string.IsNullOrWhiteSpace(Want)) parts.Add($"目标: {Want}");
            if (!string.IsNullOrWhiteSpace(Personality)) parts.Add($"性格: {Personality}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(Identity))
                return Identity;
            if (!string.IsNullOrWhiteSpace(Race))
                return Race;
            return Name;
        }

        public int GetImportanceWeight()
        {
            var baseWeight = CharacterType switch
            {
                "主角" => 100,
                "主要角色" => 85,
                "重要配角" => 70,
                "次要配角" => 50,
                "龙套" => 30,
                _ => 40
            };
            if (!string.IsNullOrWhiteSpace(Want) && !string.IsNullOrWhiteSpace(Need)) baseWeight = Math.Max(baseWeight, 80);
            if (!string.IsNullOrWhiteSpace(Identity)) baseWeight = Math.Max(baseWeight, 60);
            return baseWeight;
        }

        #endregion

        #region IContextStringProvider 实现

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<item name=\"{Name}\">");
            if (!string.IsNullOrWhiteSpace(CharacterType))
                sb.AppendLine($"角色类型：{CharacterType}");
            if (!string.IsNullOrWhiteSpace(Identity))
                sb.AppendLine($"身份：{Identity}");
            if (!string.IsNullOrWhiteSpace(Race))
                sb.AppendLine($"种族：{Race}");
            if (!string.IsNullOrWhiteSpace(Want))
                sb.AppendLine($"目标：{Want}");
            if (!string.IsNullOrWhiteSpace(Need))
                sb.AppendLine($"内在需求：{Need}");
            if (!string.IsNullOrWhiteSpace(FlawBelief))
                sb.AppendLine($"缺点：{FlawBelief}");
            if (!string.IsNullOrWhiteSpace(Personality))
                sb.AppendLine($"性格：{Personality}");
            if (!string.IsNullOrWhiteSpace(Relationships))
                sb.AppendLine($"关系：{Relationships}");
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
