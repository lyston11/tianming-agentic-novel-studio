using System.Text.Json.Serialization;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IBookValidationService
{
    Task<BookValidationReport> ValidateAsync(
        BookValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BookValidationRequest(
    string UserId,
    string ProjectId,
    int StartChapterNumber = 0,
    int EndChapterNumber = 0,
    bool IncludeBodyPreview = false);

public sealed class BookValidationReport
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("projectTitle")]
    public string ProjectTitle { get; set; } = string.Empty;

    [JsonPropertyName("startChapterNumber")]
    public int StartChapterNumber { get; set; }

    [JsonPropertyName("endChapterNumber")]
    public int EndChapterNumber { get; set; }

    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = "validated";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("issues")]
    public List<BookValidationIssue> Issues { get; set; } = new();

    [JsonPropertyName("chapters")]
    public List<BookValidationChapter> Chapters { get; set; } = new();
}

public sealed class BookValidationIssue
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("chapterNumber")]
    public int ChapterNumber { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("suggestion")]
    public string Suggestion { get; set; } = string.Empty;
}

public sealed class BookValidationChapter
{
    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("chapterNumber")]
    public int ChapterNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("hasBody")]
    public bool HasBody { get; set; }

    [JsonPropertyName("currentVersionId")]
    public string CurrentVersionId { get; set; } = string.Empty;

    [JsonPropertyName("factSnapshotId")]
    public string FactSnapshotId { get; set; } = string.Empty;

    [JsonPropertyName("factSnapshotVersion")]
    public int FactSnapshotVersion { get; set; }

    [JsonPropertyName("protagonistName")]
    public string ProtagonistName { get; set; } = string.Empty;

    [JsonPropertyName("endingState")]
    public string EndingState { get; set; } = string.Empty;

    [JsonPropertyName("nextChapterMustCarry")]
    public List<string> NextChapterMustCarry { get; set; } = new();

    [JsonPropertyName("bodyPreview")]
    public string BodyPreview { get; set; } = string.Empty;
}
