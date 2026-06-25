using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Creative;

public interface ICreativeIntentService
{
    Task<CreativeIntentItem?> CreateAsync(
        CreateCreativeIntentRequest request,
        CancellationToken cancellationToken = default);

    Task<CreativeIntentItem?> DecideAsync(
        DecideCreativeIntentRequest request,
        CancellationToken cancellationToken = default);

    Task<CreativeIntentQueryResult> QueryAsync(
        QueryCreativeIntentsRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedCreativeIntentSnapshot>> GetAcceptedSnapshotsForPackageAsync(
        string userId,
        string projectId,
        ChapterContextPackageSummary package,
        int limit = 48,
        CancellationToken cancellationToken = default);

    Task<CreativeIntentExecutionResult> MarkPackageIntentsExecutedAsync(
        string userId,
        string projectId,
        ChapterContextPackageSummary package,
        string decisionReason,
        CancellationToken cancellationToken = default);
}

public sealed record CreateCreativeIntentRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string RunId,
    string IdempotencyKey,
    string RawContent,
    string NormalizedIntent,
    string Source,
    string TargetScope,
    string TargetVolumeId,
    string TargetChapterId,
    string TargetCharacterName,
    string ImpactLevel,
    bool RequiresConfirmation,
    string ConflictStatus,
    string MetadataJson);

public sealed record DecideCreativeIntentRequest(
    string UserId,
    string ProjectId,
    string IntentId,
    string Status,
    string DecisionReason,
    string ConflictStatus,
    bool MarkExecuted);

public sealed record QueryCreativeIntentsRequest(
    string UserId,
    string ProjectId,
    string Status,
    string TargetChapterId,
    int Limit);

public sealed class CreativeIntentQueryResult
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "all";

    [JsonPropertyName("targetChapterId")]
    public string TargetChapterId { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<CreativeIntentItem> Items { get; set; } = new();
}

public sealed class CreativeIntentExecutionResult
{
    [JsonPropertyName("executedCount")]
    public int ExecutedCount { get; set; }

    [JsonPropertyName("intentIds")]
    public List<string> IntentIds { get; set; } = new();
}

public sealed class CreativeIntentItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("rawContent")]
    public string RawContent { get; set; } = string.Empty;

    [JsonPropertyName("normalizedIntent")]
    public string NormalizedIntent { get; set; } = string.Empty;

    [JsonPropertyName("targetScope")]
    public string TargetScope { get; set; } = string.Empty;

    [JsonPropertyName("targetVolumeId")]
    public string TargetVolumeId { get; set; } = string.Empty;

    [JsonPropertyName("targetChapterId")]
    public string TargetChapterId { get; set; } = string.Empty;

    [JsonPropertyName("targetCharacterName")]
    public string TargetCharacterName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("impactLevel")]
    public string ImpactLevel { get; set; } = string.Empty;

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    [JsonPropertyName("conflictStatus")]
    public string ConflictStatus { get; set; } = string.Empty;

    [JsonPropertyName("decisionReason")]
    public string DecisionReason { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("decidedAt")]
    public DateTime? DecidedAt { get; set; }

    [JsonPropertyName("executedAt")]
    public DateTime? ExecutedAt { get; set; }
}
