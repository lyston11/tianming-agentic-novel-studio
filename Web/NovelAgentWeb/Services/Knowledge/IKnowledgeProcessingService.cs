namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeProcessingProgressContext(
    string RuntimeRunId,
    string UserId,
    string SessionId,
    string? ProjectId,
    Func<KnowledgeProcessingProgressEvent, CancellationToken, Task> PublishAsync);

public sealed record KnowledgeProcessingProgressEvent(
    string Stage,
    string Message,
    int Progress,
    object? Data = null);

/// <summary>
/// Service interface for processing uploaded knowledge files.
/// </summary>
public interface IKnowledgeProcessingService
{
    Task<string> ProcessPendingFileAsync(
        string taskId,
        string userId,
        CancellationToken ct = default,
        KnowledgeProcessingProgressContext? progress = null);

    /// <summary>
    /// Processes a knowledge file (short or long) and extracts knowledge entries.
    /// </summary>
    /// <param name="taskId">The ID of the KnowledgeProcessingTask to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success message with the number of extracted entries.</returns>
    Task<string> ProcessFileAsync(
        string taskId,
        CancellationToken ct = default,
        KnowledgeProcessingProgressContext? progress = null);
}
