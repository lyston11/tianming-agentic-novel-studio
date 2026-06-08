using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 结果聚合

        private ValidationSummaryData AggregateToVolumeSummary(
            int volumeNumber,
            string volumeName,
            List<ChapterInfo> sampledChapters,
            List<ChapterValidationResult> chapterResults)
        {
            var moduleResults = new List<ModuleValidationResult>();

            foreach (var moduleName in ValidationRules.AllModuleNames)
            {
                var result = AggregateModuleResult(moduleName, chapterResults);
                moduleResults.Add(result);
            }

            var overallResult = CalculateOverallResult(moduleResults);

            return new ValidationSummaryData
            {
                Id = ShortIdGenerator.New("D"),
                Name = $"第{volumeNumber}卷校验",
                Icon = GetOverallResultIcon(overallResult),
                Category = $"第{volumeNumber}卷",
                TargetVolumeNumber = volumeNumber,
                TargetVolumeName = volumeName,
                SampledChapterCount = sampledChapters.Count,
                SampledChapterIds = sampledChapters.Select(c => c.Id).ToList(),
                LastValidatedTime = DateTime.Now,
                OverallResult = overallResult,
                ModuleResults = moduleResults,
                DependencyModuleVersions = GetCurrentDependencyVersions()
            };
        }

        private ModuleValidationResult AggregateModuleResult(
            string moduleName,
            List<ChapterValidationResult> chapterResults)
        {
            var allIssues = chapterResults
                .SelectMany(c => c.IssuesByModule.TryGetValue(moduleName, out var issues) ? issues : [])
                .ToList();

            string aggregatedResult;
            if (allIssues.Any(i => i.Severity == "Error"))
                aggregatedResult = "失败";
            else if (allIssues.Any(i => i.Severity == "Warning"))
                aggregatedResult = "警告";
            else if (allIssues.Count == 0)
                aggregatedResult = "通过";
            else
                aggregatedResult = "警告";

            var problemItems = chapterResults
                .SelectMany(c => c.IssuesByModule.TryGetValue(moduleName, out var moduleIssues)
                    ? moduleIssues.Select(issue => new ProblemItem
                    {
                        Summary = issue.Message,
                        Reason = issue.Type,
                        Details = !string.IsNullOrEmpty(issue.EntityName) ? $"相关实体: {issue.EntityName}" : null,
                        Suggestion = !string.IsNullOrEmpty(issue.Suggestion) ? issue.Suggestion : null,
                        ChapterId = c.ChapterId,
                        ChapterTitle = c.ChapterTitle
                    })
                    : [])
                .ToList();

            var extendedData = GenerateExtendedData(moduleName);

            return new ModuleValidationResult
            {
                ModuleName = moduleName,
                DisplayName = ValidationRules.GetDisplayName(moduleName),
                VerificationType = GetVerificationType(moduleName),
                Result = aggregatedResult,
                IssueDescription = GenerateIssueDescription(allIssues),
                FixSuggestion = GenerateFixSuggestion(allIssues),
                ExtendedDataJson = JsonSerializer.Serialize(extendedData),
                ProblemItemsJson = JsonSerializer.Serialize(problemItems)
            };
        }

        private string CalculateOverallResult(List<ModuleValidationResult> moduleResults)
        {
            if (moduleResults.Any(m => m.Result == "失败"))
                return "失败";
            if (moduleResults.Any(m => m.Result == "警告"))
                return "警告";
            if (moduleResults.All(m => m.Result == "通过"))
                return "通过";
            return "未校验";
        }

        private string GetOverallResultIcon(string overallResult)
        {
            return overallResult switch
            {
                "通过" => "Icon.CheckCircle",
                "警告" => "Icon.Warning",
                "失败" => "Icon.Error",
                _ => "Icon.Clock"
            };
        }

        private string GenerateIssueDescription(List<ValidationIssue> issues)
        {
            if (issues.Count == 0)
                return string.Empty;

            var descriptions = issues
                .Select(i => i.Message)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .Take(3);

            return string.Join("; ", descriptions);
        }

        private string GenerateFixSuggestion(List<ValidationIssue> issues)
        {
            var suggestions = issues
                .Select(i => i.Suggestion)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Take(3);

            return string.Join("; ", suggestions);
        }

        private Dictionary<string, string> GenerateExtendedData(string moduleName)
        {
            var schema = ValidationRules.GetExtendedDataSchema(moduleName);
            var extendedData = new Dictionary<string, string>();

            foreach (var fieldName in schema)
            {
                var camelCaseName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
                extendedData[camelCaseName] = string.Empty;
            }

            return extendedData;
        }

        private string GetVerificationType(string moduleName)
        {
            return moduleName switch
            {
                "StyleConsistency" => "文风",
                "WorldviewConsistency" => "世界观",
                "CharacterConsistency" => "角色",
                "FactionConsistency" => "势力",
                "LocationConsistency" => "地点",
                "PlotConsistency" => "剧情",
                "OutlineConsistency" => "大纲",
                "ChapterPlanConsistency" => "章节规划",
                "BlueprintConsistency" => "章节蓝图",
                "VolumeDesignConsistency" => "分卷设计",
                _ => "通用"
            };
        }

        private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines, int max = 8)
        {
            var list = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(max)
                .ToList();
            if (list.Count == 0) return;

            sb.AppendLine($"<section name=\"{title}\">");
            foreach (var line in list)
            {
                sb.AppendLine($"- {line}");
            }
            sb.AppendLine($"</section>");
            sb.AppendLine();
        }

        private static string TruncateString(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private static Models.Generate.StrategicOutline.OutlineData? ResolveOutline(
            ValidationContext context,
            string? outlineId)
        {
            if (context.Generate?.Outline?.Outlines == null) return null;
            if (string.IsNullOrWhiteSpace(outlineId))
            {
                return null;
            }
            return context.Generate.Outline.Outlines.FirstOrDefault(o => o.Id == outlineId);
        }

        private static IEnumerable<string> BuildChapterPlanLines(
            ValidationContext context,
            Models.Guides.ContextIdCollection? contextIds)
        {
            if (contextIds == null || string.IsNullOrWhiteSpace(contextIds.ChapterPlanId))
            {
                return Enumerable.Empty<string>();
            }

            var chapterPlan = context.Generate?.Planning?.Chapters
                ?.FirstOrDefault(c => string.Equals(c.Id, contextIds.ChapterPlanId, StringComparison.Ordinal));
            if (chapterPlan == null)
            {
                return Enumerable.Empty<string>();
            }

            return new[]
            {
                $"标题={chapterPlan.ChapterTitle}",
                $"主题={TruncateString(chapterPlan.ChapterTheme, 60)}",
                $"主目标={TruncateString(chapterPlan.MainGoal, 60)}",
                $"关键转折={TruncateString(chapterPlan.KeyTurn, 60)}",
                $"结尾钩子={TruncateString(chapterPlan.Hook, 60)}",
                $"伏笔={TruncateString(chapterPlan.Foreshadowing, 60)}"
            };
        }

        private static IEnumerable<string> ResolveBlueprintItems(
            ValidationContext context,
            List<string>? blueprintIds)
        {
            if (context.Generate?.Blueprint?.Blueprints == null || blueprintIds == null || blueprintIds.Count == 0)
                return Enumerable.Empty<string>();

            var blueprintIdSet = new HashSet<string>(blueprintIds);
            return context.Generate.Blueprint.Blueprints
                .Where(b => blueprintIdSet.Contains(b.Id))
                .Take(5)
                .Select(b => $"结构={TruncateString(b.OneLineStructure, 60)}, 节奏={TruncateString(b.PacingCurve, 40)}, 角色={TruncateString(b.Cast, 40)}, 地点={TruncateString(b.Locations, 40)}");
        }

        private static Models.Generate.VolumeDesign.VolumeDesignData? ResolveVolumeDesign(
            ValidationContext context,
            string? volumeDesignId)
        {
            if (context.Generate?.VolumeDesign?.VolumeDesigns == null || string.IsNullOrWhiteSpace(volumeDesignId))
                return null;
            return context.Generate.VolumeDesign.VolumeDesigns
                .FirstOrDefault(v => string.Equals(v.Id, volumeDesignId, StringComparison.Ordinal));
        }

        private Dictionary<string, int> GetCurrentDependencyVersions()
        {
            return new Dictionary<string, int>
            {
                ["Design"] = _versionTrackingService.GetModuleVersion("Design"),
                ["Generate"] = _versionTrackingService.GetModuleVersion("Generate")
            };
        }

        #endregion
    }
}
