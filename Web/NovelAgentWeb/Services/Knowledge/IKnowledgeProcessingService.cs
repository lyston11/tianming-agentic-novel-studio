namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service interface for processing uploaded knowledge files.
/// </summary>
public interface IKnowledgeProcessingService
{
    /// <summary>
    /// Processes a knowledge file (short or long) and extracts knowledge entries.
    /// </summary>
    /// <param name="taskId">The ID of the KnowledgeProcessingTask to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success message with the number of extracted entries.</returns>
    Task<string> ProcessFileAsync(string taskId, CancellationToken ct = default);
}
