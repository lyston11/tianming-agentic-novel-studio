using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {

        private static readonly Dictionary<string, string[]> NovelOralPhrases = new()
        {
            [@"看了很久"] = new[] { @"看了很久很久" },
            [@"等了很久"] = new[] { @"已经等了很久很久了" },
            [@"深吸一口气"] = new[] { @"深深吸了一口气" },
            [@"就在这时"] = new[] { @"就在这个时候" },
            [@"第二天"] = new[] { @"到了第二天" },
            [@"深吸了一口气"] = new[] { @"深深吸了一口气" },
        };

        private static readonly Dictionary<string, string[]> NovelOralWords = new()
        {
            [@"看"] = new[] { @"瞧" },
            [@"看见"] = new[] { @"瞧见" },
            [@"看到"] = new[] { @"瞧见", @"瞧到" },
            [@"整齐地"] = new[] { @"齐刷刷地" },
            [@"漆黑"] = new[] { @"黑乎乎", @"黑漆漆" },
            [@"寂静"] = new[] { @"死寂", @"静悄悄" },
            [@"急促"] = new[] { @"又急又促" },
            [@"模糊"] = new[] { @"模模糊糊" },
            [@"清晰"] = new[] { @"真真切切" },
            [@"清楚"] = new[] { @"清清楚楚" },
            [@"慢慢"] = new[] { @"慢慢吞吞" },
            [@"然后"] = new[] { @"紧接着", @"跟着" },
            [@"跑出"] = new[] { @"拔腿就跑" },
            [@"但是"] = new[] { @"可是", @"可" },
            [@"猛然"] = new[] { @"猛地" },
            [@"突然"] = new[] { @"猛地", @"冷不丁" },
            [@"声音"] = new[] { @"响动" },
            [@"立即"] = new[] { @"马上", @"当下" },
            [@"立刻"] = new[] { @"马上", @"当下" },
        };
    }
}
