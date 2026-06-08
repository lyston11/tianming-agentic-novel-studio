using System;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class GenreDirectionPlanner
    {
        public GenreDirectionProfile BuildProfile(string genre, string subGenre, string desiredDirection)
        {
            var text = string.Join(" ", genre, subGenre, desiredDirection).ToLowerInvariant();
            var profile = new GenreDirectionProfile
            {
                PleasureStrength = 6,
                MysteryStrength = 5,
                EmotionStrength = 5,
                WorldbuildingStrength = 6,
                EnsembleStrength = 4,
                DepthStrength = 5,
                PaceStrength = 7,
                Strategy = "保持清晰主线，每章推进至少一个故事变量。"
            };

            if (ContainsAny(text, "爽", "升级", "打脸", "逆袭", "玄幻", "修仙"))
            {
                profile.PleasureStrength = Math.Max(profile.PleasureStrength, 9);
                profile.PaceStrength = Math.Max(profile.PaceStrength, 8);
                profile.WorldbuildingStrength = Math.Max(profile.WorldbuildingStrength, 7);
                profile.Strategy = "以压迫-反击-代价-升级为主循环，爽点必须有铺垫和代价。";
                profile.RiskWarnings.Add("避免反派降智、无代价开挂、重复打脸。");
            }

            if (ContainsAny(text, "烧脑", "悬疑", "谜", "推理", "克苏鲁", "诡秘"))
            {
                profile.MysteryStrength = Math.Max(profile.MysteryStrength, 9);
                profile.DepthStrength = Math.Max(profile.DepthStrength, 7);
                profile.WorldbuildingStrength = Math.Max(profile.WorldbuildingStrength, 8);
                profile.Strategy = "以谜题-线索-误导-反转-更大谜题为主循环，反转必须提前埋证据。";
                profile.RiskWarnings.Add("避免谜底靠临时新设定硬补，避免复杂但不精彩。");
            }

            if (ContainsAny(text, "群像", "权谋", "势力", "战争"))
            {
                profile.EnsembleStrength = Math.Max(profile.EnsembleStrength, 8);
                profile.WorldbuildingStrength = Math.Max(profile.WorldbuildingStrength, 8);
                profile.Strategy = "以多方目标碰撞推动剧情，角色选择必须服务各自利益。";
                profile.RiskWarnings.Add("避免角色同质化、配角工具人、支线无意义。");
            }

            if (ContainsAny(text, "情绪", "虐", "救赎", "爱情", "亲情"))
            {
                profile.EmotionStrength = Math.Max(profile.EmotionStrength, 8);
                profile.DepthStrength = Math.Max(profile.DepthStrength, 7);
                profile.Strategy = "以关系变化和情绪代价推动章节，不让情绪戏脱离主线冲突。";
                profile.RiskWarnings.Add("避免强行误会和廉价牺牲。");
            }

            if (profile.RiskWarnings.Count == 0)
            {
                profile.RiskWarnings.Add("避免章节只发生事件但不改变故事状态。");
            }

            return profile;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
