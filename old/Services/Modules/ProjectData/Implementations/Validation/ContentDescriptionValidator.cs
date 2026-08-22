using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ContentDescriptionValidator
    {
        private static readonly Dictionary<string, List<string>> AntonymPairs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["沉默寡言"] = new() { "滔滔不绝", "话多", "健谈", "喋喋不休" },
            ["内向"] = new() { "外向", "开朗", "活泼" },
            ["冷漠"] = new() { "热情", "温暖", "友善" },
            ["温柔"] = new() { "冷酷", "暴躁", "粗暴", "残忍" },
            ["沉稳"] = new() { "冲动", "莽撞", "浮躁", "急躁" },
            ["冷静"] = new() { "冲动", "慌乱", "失控", "暴躁" },
            ["谨慎"] = new() { "鲁莽", "冒进", "大意", "草率" },
            ["勇敢"] = new() { "胆小", "懦弱", "怯懦" },
            ["坚强"] = new() { "软弱", "脆弱", "怯弱" },
            ["正直"] = new() { "狡诈", "阴险", "卑鄙", "虚伪" },
            ["善良"] = new() { "邪恶", "残忍", "冷酷" },
            ["忠诚"] = new() { "背叛", "叛变", "反叛" },
            ["聪明"] = new() { "愚蠢", "笨拙", "迟钝" },
            ["黑发"] = new() { "金发", "白发", "红发", "银发", "棕发", "蓝发", "紫发", "绿发", "灰发" },
            ["金发"] = new() { "黑发", "白发", "红发", "银发", "棕发", "蓝发", "紫发", "绿发", "灰发" },
            ["白发"] = new() { "黑发", "金发", "红发", "棕发", "蓝发", "紫发", "绿发", "灰发" },
            ["银发"] = new() { "黑发", "金发", "红发", "棕发", "蓝发", "紫发", "绿发", "灰发" },
            ["红发"] = new() { "黑发", "金发", "白发", "银发", "棕发", "蓝发", "紫发", "绿发", "灰发" },
            ["蓝眼"] = new() { "黑眼", "红眼", "金眼", "绿眼", "紫眼", "灰眼" },
            ["黑眼"] = new() { "蓝眼", "红眼", "金眼", "绿眼", "紫眼", "灰眼" },
            ["红眼"] = new() { "黑眼", "蓝眼", "金眼", "绿眼", "紫眼", "灰眼" },
            ["金眼"] = new() { "黑眼", "蓝眼", "红眼", "绿眼", "紫眼", "灰眼" },
            ["绿眼"] = new() { "黑眼", "蓝眼", "红眼", "金眼", "紫眼", "灰眼" }
        };

        public List<string> ValidateCharacterDescriptions(
            string content,
            Dictionary<string, CharacterCoreDescription> packagedDescriptions)
        {
            var issues = new List<string>();

            if (string.IsNullOrEmpty(content) || packagedDescriptions == null)
                return issues;

            foreach (var (charId, desc) in packagedDescriptions)
            {
                if (string.IsNullOrEmpty(desc.Name))
                    continue;

                if (!string.IsNullOrEmpty(desc.HairColor))
                {
                    var contradiction = FindHairColorContradiction(content, desc.Name, desc.HairColor);
                    if (contradiction != null)
                    {
                        issues.Add($"角色 {desc.Name} 发色矛盾：打包={desc.HairColor}，正文出现={contradiction}");
                    }
                }

                if (desc.PersonalityTags != null && desc.PersonalityTags.Count > 0)
                {
                    foreach (var tag in desc.PersonalityTags)
                    {
                        var antonyms = GetAntonyms(tag);
                        foreach (var antonym in antonyms)
                        {
                            if (ContentContainsNear(content, desc.Name, antonym, 50))
                            {
                                issues.Add($"角色 {desc.Name} 性格矛盾：打包={tag}，正文出现={antonym}");
                            }
                        }
                    }
                }
            }

            return issues;
        }

        public List<string> ValidateLocationDescriptions(
            string content,
            Dictionary<string, LocationCoreDescription> packagedDescriptions)
        {
            var issues = new List<string>();

            if (string.IsNullOrEmpty(content) || packagedDescriptions == null)
                return issues;

            foreach (var (locId, desc) in packagedDescriptions)
            {
                if (string.IsNullOrEmpty(desc.Name) || desc.Features == null)
                    continue;

                foreach (var feature in desc.Features)
                {
                    var antonyms = GetAntonyms(feature);
                    foreach (var antonym in antonyms)
                    {
                        if (ContentContainsNear(content, desc.Name, antonym, 100))
                        {
                            issues.Add($"地点 {desc.Name} 特征矛盾：打包={feature}，正文出现={antonym}");
                        }
                    }
                }
            }

            return issues;
        }

        private static string? FindHairColorContradiction(string content, string characterName, string expectedColor)
        {
            var hairColors = HairColorConstants.HairColorKeywords;
            var otherColors = hairColors.Where(c => !c.Contains(expectedColor) && !expectedColor.Contains(c)).ToList();

            foreach (var color in otherColors)
            {
                if (ContentContainsNear(content, characterName, color, 30))
                {
                    return color;
                }
            }

            return null;
        }

        private static List<string> GetAntonyms(string tag)
        {
            if (AntonymPairs.TryGetValue(tag, out var antonyms))
                return antonyms;
            return new List<string>();
        }

        private static bool ContentContainsNear(string content, string anchor, string target, int maxDistance)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(anchor) || string.IsNullOrEmpty(target))
                return false;

            var anchorIndex = content.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (anchorIndex < 0)
                return false;

            var searchStart = Math.Max(0, anchorIndex - maxDistance);
            var searchEnd = Math.Min(content.Length, anchorIndex + anchor.Length + maxDistance);
            var searchRegion = content.Substring(searchStart, searchEnd - searchStart);

            return searchRegion.Contains(target, StringComparison.OrdinalIgnoreCase);
        }
    }
}
