using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class GenreDirectionPlanner
    {
        public GenreDirectionProfile BuildProfile(string genre, string subGenre)
        {
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

            profile.RiskWarnings.Add("避免章节只发生事件但不改变故事状态。");

            return profile;
        }
    }
}
