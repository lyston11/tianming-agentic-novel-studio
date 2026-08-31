using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Services.Modules.VersionTracking
{
    public static class DependencyConfig
    {
        public static readonly Dictionary<string, string[]> ModuleDependencies = new()
        {
            ["CreativeMaterials"] = Array.Empty<string>(),
            ["BookAnalysis"] = Array.Empty<string>(),
            ["WorldRules"] = new[] { "CreativeMaterials" },
            ["CharacterRules"] = new[] { "CreativeMaterials", "WorldRules" },
            ["FactionRules"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules" },
            ["LocationRules"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules" },
            ["PlotRules"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules", "LocationRules" },

            ["Outline"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules", "LocationRules", "PlotRules" },
            ["VolumeDesign"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules", "LocationRules", "PlotRules", "Outline" },
            ["Chapter"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules", "LocationRules", "PlotRules", "Outline", "VolumeDesign" },
            ["Blueprint"] = new[] { "CreativeMaterials", "WorldRules", "CharacterRules", "FactionRules", "LocationRules", "PlotRules", "Outline", "VolumeDesign", "Chapter" },

            ["ValidationSummary"] = new[] {
                "CreativeMaterials", "BookAnalysis",
                "WorldRules", "CharacterRules", "FactionRules", "LocationRules", "PlotRules",
                "Outline", "VolumeDesign", "Chapter", "Blueprint"
            },
        };

        public static readonly Dictionary<string, string> ModuleDisplayNames = new()
        {
            ["CreativeMaterials"] = "创作目录",
            ["BookAnalysis"] = "智能拆书",
            ["WorldRules"] = "世界观规则",
            ["CharacterRules"] = "角色规则",
            ["FactionRules"] = "势力规则",
            ["LocationRules"] = "位置规则",
            ["PlotRules"] = "剧情规则",
            ["Outline"] = "大纲",
            ["VolumeDesign"] = "分卷设计",
            ["Chapter"] = "章节规划",
            ["Blueprint"] = "蓝图",
            ["ValidationSummary"] = "验证结果",
        };

        public static string GetDisplayName(string moduleName)
            => ModuleDisplayNames.TryGetValue(moduleName, out var name) ? name : moduleName;

        public static string GetDisplayNames(IEnumerable<string> moduleNames)
            => string.Join("、", moduleNames.Select(GetDisplayName));

        public static string[] GetDependencies(string moduleName)
            => ModuleDependencies.TryGetValue(moduleName, out var deps) ? deps : Array.Empty<string>();

        public static List<string> GetDownstreamModules(string moduleName)
            => ModuleDependencies
                .Where(kv => kv.Value.Contains(moduleName))
                .Select(kv => kv.Key)
                .ToList();
    }
}
