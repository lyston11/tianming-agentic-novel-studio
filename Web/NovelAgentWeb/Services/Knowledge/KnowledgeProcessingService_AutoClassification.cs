using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Auto-classification partial for KnowledgeProcessingService.
/// Triggers knowledge classification after file processing completes.
/// </summary>
public partial class KnowledgeProcessingService
{
    /// <summary>
    /// Triggers automatic classification for newly created knowledge entries.
    /// Called after knowledge file processing completes successfully.
    /// </summary>
    private async Task TriggerAutoClassificationAsync(
        Data.Entities.KnowledgeProcessingTask task,
        IReadOnlyList<string> knowledgeIds,
        CancellationToken ct)
    {
        if (_classificationService == null)
        {
            _logger.LogWarning(
                "KnowledgeClassificationService not available, skipping auto-classification for task {TaskId}",
                task.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.ProjectId))
        {
            _logger.LogWarning(
                "Task {TaskId} has no ProjectId, skipping auto-classification",
                task.Id);
            return;
        }

        _logger.LogInformation(
            "Auto-classifying {Count} knowledge entries for project {ProjectId}",
            knowledgeIds.Count,
            task.ProjectId);

        var successCount = 0;
        var failureCount = 0;

        foreach (var knowledgeId in knowledgeIds)
        {
            try
            {
                var request = new KnowledgeClassificationRequest(
                    UserId: task.UserId,
                    ProjectId: task.ProjectId,
                    KnowledgeId: knowledgeId,
                    SessionId: null,
                    RunId: null
                );

                await _classificationService.ClassifyAndApplyAsync(request, ct)
                    .ConfigureAwait(false);

                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to auto-classify knowledge {KnowledgeId} in task {TaskId}",
                    knowledgeId,
                    task.Id);
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Auto-classification completed for task {TaskId}: {SuccessCount} succeeded, {FailureCount} failed",
            task.Id,
            successCount,
            failureCount);
    }
}
