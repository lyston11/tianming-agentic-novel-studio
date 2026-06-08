using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 辅助方法

        private string BuildRulesSignature()
        {
            var sb = new StringBuilder();
            foreach (var moduleName in ValidationRules.AllModuleNames)
            {
                sb.Append(moduleName).Append(':');
                foreach (var field in ValidationRules.GetExtendedDataSchema(moduleName))
                {
                    sb.Append(field).Append(',');
                }
                sb.Append('|');
            }

            return ComputeHash(sb.ToString());
        }

        private static string ComputeHash(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash)[..16];
        }

        private async Task EnsurePackagedDataOrThrowAsync()
        {
            var manifest = await _publishService.GetManifestAsync().ConfigureAwait(false);
            if (manifest == null)
            {
                throw new InvalidOperationException("未找到打包数据，请先执行打包");
            }

            var designPath = StoragePathHelper.GetProjectConfigPath("Design");
            var generatePath = StoragePathHelper.GetProjectConfigPath("Generate");

            var hasAnyDesign = Directory.Exists(designPath) && Directory.EnumerateFiles(designPath, "*.json", SearchOption.TopDirectoryOnly).Any();
            var hasAnyGenerate = Directory.Exists(generatePath) && Directory.EnumerateFiles(generatePath, "*.json", SearchOption.TopDirectoryOnly).Any();

            if (!hasAnyDesign && !hasAnyGenerate)
            {
                throw new InvalidOperationException("当前没有用于校验的数据，请进行打包");
            }
        }

        private async Task<string> GetVolumeNameAsync(int volumeNumber)
        {
            var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
            await volumeService.InitializeAsync().ConfigureAwait(false);
            var volume = volumeService.GetAllVolumeDesigns()
                .FirstOrDefault(v => v.VolumeNumber == volumeNumber);
            var name = volumeNumber > 0
                ? $"第{volumeNumber}卷 {volume?.VolumeTitle}".Trim()
                : volume?.Name;
            return string.IsNullOrWhiteSpace(name) ? $"第{volumeNumber}卷" : name;
        }

        private string ExtractChapterTitle(string content)
        {
            if (string.IsNullOrEmpty(content)) return "未命名章节";

            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                    return trimmed.Substring(2).Trim();
                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                    return trimmed.Substring(3).Trim();
            }

            return "未命名章节";
        }

        private string DetermineOverallResult(ChapterValidationResult result)
        {
            if (result.HasErrors) return "失败";
            if (result.HasWarnings) return "警告";
            if (result.TotalIssueCount > 0) return "警告";
            return "通过";
        }

        private ChapterValidationResult CreateErrorResult(string chapterId, string message, int volumeNumber = 0, int chapterNumber = 0, string volumeName = "")
        {
            return new ChapterValidationResult
            {
                ChapterId = chapterId,
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                VolumeName = volumeName,
                OverallResult = "失败",
                ValidatedTime = DateTime.Now,
                IssuesByModule = new Dictionary<string, List<ValidationIssue>>
                {
                    ["System"] = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Type = "SystemError",
                            Severity = "Error",
                            Message = message
                        }
                    }
                }
            };
        }

        #endregion
    }
}
