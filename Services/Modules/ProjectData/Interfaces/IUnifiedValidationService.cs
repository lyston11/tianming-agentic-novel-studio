using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IUnifiedValidationService
    {
        Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default);

        Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default);

        Task<bool> NeedsRepublishAsync();
    }

    public class ChapterValidationResult
    {
        public string ChapterId { get; set; } = string.Empty;

        public string ChapterTitle { get; set; } = string.Empty;

        public int VolumeNumber { get; set; }

        public int ChapterNumber { get; set; }

        public string VolumeName { get; set; } = string.Empty;

        public DateTime ValidatedTime { get; set; } = DateTime.Now;

        public string OverallResult { get; set; } = "未校验";

        public Dictionary<string, List<ValidationIssue>> IssuesByModule { get; set; } = new();

        public int TotalIssueCount => IssuesByModule.Values.Sum(list => list.Count);

        public bool HasErrors => IssuesByModule.Values.Any(list => list.Any(i => i.Severity == "Error"));

        public bool HasWarnings => IssuesByModule.Values.Any(list => list.Any(i => i.Severity == "Warning"));
    }

    public class VolumeValidationResult
    {
        public int VolumeNumber { get; set; }

        public string VolumeName { get; set; } = string.Empty;

        public DateTime ValidatedTime { get; set; } = DateTime.Now;

        public List<ChapterValidationResult> ChapterResults { get; set; } = new();

        public int TotalChapters => ChapterResults.Count;

        public int PassedChapters => ChapterResults.Count(r => r.OverallResult == "通过");

        public int FailedChapters => ChapterResults.Count(r => r.OverallResult != "通过");
    }

    public class ValidationIssue
    {
        public string Type { get; set; } = string.Empty;

        public string Severity { get; set; } = "Warning";

        public string Message { get; set; } = string.Empty;

        public string Suggestion { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public string EntityName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;
    }
}
