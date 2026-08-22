using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Publishing
{
    public class PublishResult
    {
        [JsonPropertyName("IsSuccess")] public bool IsSuccess { get; set; }
        [JsonPropertyName("Message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("PublishTime")] public DateTime PublishTime { get; set; }
        [JsonPropertyName("Version")] public int Version { get; set; }
        [JsonPropertyName("PackagedModules")] public List<string> PackagedModules { get; set; } = new();
        [JsonPropertyName("ErrorDetail")] public string? ErrorDetail { get; set; }

        public static PublishResult Success(int version, List<string> modules)
        {
            return new PublishResult
            {
                IsSuccess = true,
                Message = "打包成功",
                PublishTime = DateTime.Now,
                Version = version,
                PackagedModules = modules
            };
        }

        public static PublishResult Failed(string message, string? detail = null)
        {
            return new PublishResult
            {
                IsSuccess = false,
                Message = message,
                PublishTime = DateTime.Now,
                ErrorDetail = detail
            };
        }
    }

    public class PublishStatus
    {
        [JsonPropertyName("IsPublished")] public bool IsPublished { get; set; }
        [JsonPropertyName("LastPublishTime")] public DateTime? LastPublishTime { get; set; }
        [JsonPropertyName("CurrentVersion")] public int CurrentVersion { get; set; }
        [JsonPropertyName("NeedsRepublish")] public bool NeedsRepublish { get; set; }
        [JsonPropertyName("ChangedModuleCount")] public int ChangedModuleCount { get; set; }
    }

    public class ManifestInfo
    {
        [JsonPropertyName("ProjectName")] public string ProjectName { get; set; } = string.Empty;
        [JsonPropertyName("PublishTime")] public DateTime PublishTime { get; set; }
        [JsonPropertyName("Version")] public int Version { get; set; }
        [JsonPropertyName("Files")] public Dictionary<string, List<string>> Files { get; set; } = new();
        [JsonPropertyName("EnabledModules")] public Dictionary<string, Dictionary<string, bool>> EnabledModules { get; set; } = new();
        [JsonPropertyName("Statistics")] public StatisticsInfo Statistics { get; set; } = new();
    }

    public class StatisticsInfo
    {
        [JsonPropertyName("TotalCharacters")] public int TotalCharacters { get; set; }
        [JsonPropertyName("TotalLocations")] public int TotalLocations { get; set; }
        [JsonPropertyName("TotalChapters")] public int TotalChapters { get; set; }
        [JsonPropertyName("TotalWords")] public long TotalWords { get; set; }
    }

    public class PackageHistoryEntry
    {
        [JsonPropertyName("Version")] public int Version { get; set; }
        [JsonPropertyName("PublishTime")] public DateTime PublishTime { get; set; }
        [JsonPropertyName("EnabledSummary")] public string EnabledSummary { get; set; } = string.Empty;
        [JsonPropertyName("EnabledModules")] public Dictionary<string, Dictionary<string, bool>> EnabledModules { get; set; } = new();
        [JsonPropertyName("IsCurrent")] public bool IsCurrent { get; set; }
        [JsonPropertyName("HistoryPath")] public string HistoryPath { get; set; } = string.Empty;
    }

    public class PackageVersionDiff
    {
        [JsonPropertyName("CurrentVersion")] public int CurrentVersion { get; set; }
        [JsonPropertyName("HistoryVersion")] public int HistoryVersion { get; set; }
        [JsonPropertyName("DiffItems")] public List<ModuleDiffItem> DiffItems { get; set; } = new();
    }

    public class ModuleDiffItem
    {
        [JsonPropertyName("ModulePath")] public string ModulePath { get; set; } = string.Empty;
        [JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public DiffType Type { get; set; }
        [JsonPropertyName("CurrentState")] public string CurrentState { get; set; } = string.Empty;
        [JsonPropertyName("HistoryState")] public string HistoryState { get; set; } = string.Empty;
    }

    public enum DiffType
    {
        None,

        EnabledChanged,

        DataChanged
    }
}
