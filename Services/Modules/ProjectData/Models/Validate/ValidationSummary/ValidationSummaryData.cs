using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary
{
    public class ValidationSummaryData : IDependencyTracked, IDataItem
    {
        #region 标准6字段

        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.CheckCircle";

        [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;

        [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;

        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;

        [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;

        #endregion

        #region 卷关联字段

        [JsonPropertyName("TargetVolumeNumber")] public int TargetVolumeNumber { get; set; }

        [JsonPropertyName("TargetVolumeName")] public string TargetVolumeName { get; set; } = string.Empty;
        [JsonPropertyName("SampledChapterCount")] public int SampledChapterCount { get; set; }
        [JsonPropertyName("SampledChapterIds")] public List<string> SampledChapterIds { get; set; } = new();
        [JsonPropertyName("LastValidatedTime")] public DateTime LastValidatedTime { get; set; }

        #endregion

        #region 校验汇总字段

        [JsonPropertyName("OverallResult")] public string OverallResult { get; set; } = "未校验";
        [JsonPropertyName("ModuleResults")] public List<ModuleValidationResult> ModuleResults { get; set; } = new();

        #endregion

        #region 版本追踪（IDependencyTracked）

        [JsonPropertyName("DependencyModuleVersions")] public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        #endregion
    }
}
