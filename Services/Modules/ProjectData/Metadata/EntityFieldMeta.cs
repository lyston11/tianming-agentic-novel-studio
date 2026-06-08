using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Services.Modules.ProjectData.Metadata
{
    public static class EntityFieldMeta
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _propToChinese = new(StringComparer.OrdinalIgnoreCase)
        {
            ["characters"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["CharacterType"] = "角色类型",
                ["Gender"] = "性别",
                ["Age"] = "年龄",
                ["Identity"] = "身份",
                ["Race"] = "种族",
                ["Appearance"] = "外貌特征",
                ["Personality"] = "性格特征",
                ["TargetCharacterName"] = "关联角色姓名",
                ["RelationshipType"] = "关系类型",
                ["EmotionDynamic"] = "情感动态",
                ["Relationships"] = "关系描述",
                ["Want"] = "外在目标",
                ["Need"] = "内在需求",
                ["FlawBelief"] = "致命缺点",
                ["GrowthPath"] = "成长路径",
                ["CombatSkills"] = "战斗技能",
                ["SpecialAbilities"] = "特殊能力",
                ["NonCombatSkills"] = "非战斗技能",
                ["SignatureItems"] = "标志性装备",
                ["CommonItems"] = "常规装备",
                ["PersonalAssets"] = "个人资产",
            },
            ["locations"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["LocationType"] = "位置类型",
                ["Description"] = "位置描述",
                ["Scale"] = "规模范围",
                ["Terrain"] = "地形环境",
                ["Climate"] = "气候特征",
                ["Landmarks"] = "标志地标",
                ["Resources"] = "特产资源",
                ["HistoricalSignificance"] = "历史意义",
                ["Dangers"] = "危险禁忌",
                ["FactionId"] = "所属势力",
            },
            ["factions"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["FactionType"] = "势力类型",
                ["Goal"] = "理念目标",
                ["StrengthTerritory"] = "实力地盘",
                ["Leader"] = "领袖",
                ["CoreMembers"] = "核心成员",
                ["MemberTraits"] = "成员特征",
                ["Allies"] = "盟友势力",
                ["Enemies"] = "敌对势力",
                ["NeutralCompetitors"] = "中立竞争",
            },
            ["plotrules"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["TargetVolume"] = "总卷数",
                ["AssignedVolume"] = "所属卷",
                ["OneLineSummary"] = "一句话简介",
                ["EventType"] = "事件类型",
                ["StoryPhase"] = "所属阶段",
                ["PrerequisitesTrigger"] = "前置条件触发",
                ["MainCharacters"] = "主要角色",
                ["KeyNpcs"] = "关键NPC",
                ["Location"] = "地点",
                ["TimeDuration"] = "时间跨度",
                ["StepTitle"] = "步骤标题",
                ["Goal"] = "目标",
                ["Conflict"] = "冲突",
                ["Result"] = "结果",
                ["EmotionCurve"] = "情绪曲线",
                ["MainPlotPush"] = "主线推进",
                ["CharacterGrowth"] = "角色成长",
                ["WorldReveal"] = "世界观揭示",
                ["RewardsClues"] = "奖励线索",
            },
            ["worldrules"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["OneLineSummary"] = "一句话简介",
                ["PowerSystem"] = "力量体系",
                ["Cosmology"] = "宇宙观",
                ["SpecialLaws"] = "特殊法则",
                ["HardRules"] = "硬规则",
                ["SoftRules"] = "软规则",
                ["AncientEra"] = "创世古代纪元",
                ["KeyEvents"] = "关键历史事件",
                ["ModernHistory"] = "近代史",
                ["StatusQuo"] = "故事开始前现状",
            },
            ["templates"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["NovelSynopsis"] = "小说简介",
                ["OverallIdea"] = "整体构思",
                ["WorldBuildingMethod"] = "世界观素材-构建手法",
                ["PowerSystemDesign"] = "世界观素材-力量体系",
                ["EnvironmentDescription"] = "世界观素材-环境描写",
                ["FactionDesign"] = "世界观素材-势力设计",
                ["WorldviewHighlights"] = "世界观素材-亮点",
                ["ProtagonistDesign"] = "角色素材-主角塑造",
                ["SupportingRoles"] = "角色素材-配角设计",
                ["CharacterRelations"] = "角色素材-人物关系",
                ["GoldenFingerDesign"] = "角色素材-金手指",
                ["CharacterHighlights"] = "角色素材-角色亮点",
                ["PlotStructure"] = "剧情素材-情节结构",
                ["ConflictDesign"] = "剧情素材-冲突设计",
                ["ClimaxArrangement"] = "剧情素材-高潮布局",
                ["ForeshadowingTechnique"] = "剧情素材-伏笔设计",
                ["PlotHighlights"] = "剧情素材-剧情亮点",
            },
            ["outline"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["TotalChapterCount"] = "总章节数",
                ["OneLineOutline"] = "一句话大纲",
                ["EmotionalTone"] = "情感基调",
                ["PhilosophicalMotif"] = "哲学母题",
                ["Theme"] = "主题思想",
                ["CoreConflict"] = "核心冲突",
                ["EndingState"] = "结局/目标状态",
                ["VolumeDivision"] = "卷/幕划分",
                ["OutlineOverview"] = "大纲总览",
            },
            ["volumedesign"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["VolumeNumber"] = "卷序号",
                ["VolumeTitle"] = "卷标题",
                ["VolumeTheme"] = "卷主题",
                ["StageGoal"] = "卷阶段目标",
                ["MainConflict"] = "卷主冲突",
                ["PressureSource"] = "压力来源",
                ["KeyEvents"] = "关键转折",
                ["OpeningState"] = "卷开篇状态",
                ["EndingState"] = "卷收束状态",
                ["ChapterAllocationOverview"] = "章节分配总览",
                ["PlotAllocation"] = "剧情分配",
                ["ChapterGenerationHints"] = "章节生成提示",
                ["ReferencedCharacterNames"] = "出场角色",
                ["ReferencedFactionNames"] = "涉及势力",
                ["ReferencedLocationNames"] = "涉及地点",
            },
            ["chapter"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["MainGoal"] = "章节目标",
                ["ChapterTheme"] = "章节主题",
                ["ReaderExperienceGoal"] = "读者体验目标",
                ["ResistanceSource"] = "阻力来源",
                ["KeyTurn"] = "关键转折",
                ["Hook"] = "钩子",
                ["WorldInfoDrop"] = "世界信息释放",
                ["CharacterArcProgress"] = "角色弧推进",
                ["MainPlotProgress"] = "主线推进",
                ["Foreshadowing"] = "伏笔",
                ["ChapterTitle"] = "章节标题",
                ["ReferencedCharacterNames"] = "出场角色",
                ["ReferencedFactionNames"] = "涉及势力",
                ["ReferencedLocationNames"] = "涉及地点",
            },
            ["blueprint"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["OneLineStructure"] = "一句话结构",
                ["PacingCurve"] = "节奏曲线",
                ["PovCharacter"] = "视点角色",
                ["Opening"] = "开场",
                ["Development"] = "发展",
                ["Turning"] = "转折",
                ["Ending"] = "结尾",
                ["InfoDrop"] = "信息释放",
                ["ItemsClues"] = "物品线索",
                ["Cast"] = "出场角色",
                ["Locations"] = "涉及地点",
                ["Factions"] = "涉及势力",
                ["SceneNumber"] = "场景编号",
                ["SceneTitle"] = "场景标题",
            },
            ["shortstoryblueprint"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "名称",
                ["Synopsis"] = "全篇简介",
                ["ChapterBlueprintText"] = "章节蓝图",
            },
        };

        private static readonly HashSet<string> _nameAliases = new(StringComparer.Ordinal)
        {
            "名称", "规则名称", "素材名称"
        };

        private static readonly Dictionary<string, string> _typeDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["characters"] = "角色",
            ["locations"] = "地点",
            ["factions"] = "势力",
            ["plotrules"] = "剧情",
            ["worldrules"] = "世界观",
            ["templates"] = "素材",
            ["outline"] = "大纲",
            ["volumedesign"] = "分卷设计",
            ["chapter"] = "章节规划",
            ["blueprint"] = "章节蓝图",
            ["shortstoryblueprint"] = "短篇蓝图",
        };

        private static readonly Dictionary<string, Dictionary<string, string>> _chineseToProp = new(StringComparer.OrdinalIgnoreCase);

        public static string GetFieldDisplayName(string entityType, string propertyName)
        {
            if (_propToChinese.TryGetValue(entityType, out var map) && map.TryGetValue(propertyName, out var cn))
                return cn;
            return propertyName;
        }

        public static string ResolvePropertyName(string entityType, string fieldNameOrChinese)
        {
            if (_propToChinese.TryGetValue(entityType, out var map) && map.ContainsKey(fieldNameOrChinese))
                return fieldNameOrChinese;
            if (_nameAliases.Contains(fieldNameOrChinese))
                return "Name";
            var reverse = GetOrBuildReverse(entityType);
            if (reverse != null && reverse.TryGetValue(fieldNameOrChinese, out var prop))
                return prop;
            return fieldNameOrChinese;
        }

        public static string GetEntityTypeDisplayName(string entityType)
        {
            return _typeDisplayNames.TryGetValue(entityType, out var cn) ? cn : entityType;
        }

        public static Dictionary<string, string> GetFieldKeyMap(string entityType)
        {
            var reverse = GetOrBuildReverse(entityType);
            if (reverse == null) return new Dictionary<string, string>();
            return reverse
                .Where(kv => !string.Equals(kv.Value, "Name", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static Dictionary<string, string> GetFieldKeyMapWithName(string entityType)
        {
            var reverse = GetOrBuildReverse(entityType);
            if (reverse == null) return new Dictionary<string, string>();
            return new Dictionary<string, string>(reverse);
        }

        private static Dictionary<string, string>? GetOrBuildReverse(string entityType)
        {
            if (_chineseToProp.TryGetValue(entityType, out var rev)) return rev;
            if (!_propToChinese.TryGetValue(entityType, out var map)) return null;
            rev = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in map) rev[kv.Value] = kv.Key;
            _chineseToProp[entityType] = rev;
            return rev;
        }
    }
}
