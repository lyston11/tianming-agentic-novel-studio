using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed record GoalProgressEventRequest(
    string UserId,
    string GoalId,
    string Type,
    string Message,
    string? TaskId = null,
    string? CandidateChapterId = null,
    int? CandidateVersion = null,
    int? ChapterNumber = null,
    string? BranchId = null,
    IReadOnlyList<string>? ArtifactIds = null,
    string? Action = null,
    string? Error = null);

public sealed record GoalProgressEventData(
    string GoalId,
    string ProjectId,
    string GoalStatus,
    string? TaskId,
    string? TaskType,
    string? KernelName,
    string? TaskStatus,
    int? Attempt,
    string? CandidateChapterId,
    int? CandidateVersion,
    int? ChapterNumber,
    string? BranchId,
    IReadOnlyList<string> ArtifactIds,
    IReadOnlyList<string> ArtifactTypes,
    string? Action,
    string? Error);

public interface IGoalProgressEventPublisher
{
    Task PublishAsync(GoalProgressEventRequest request, CancellationToken cancellationToken = default);
}

public sealed class GoalProgressEventPublisher : IGoalProgressEventPublisher
{
    private readonly NovelAgentDbContext _db;
    private readonly AgentSseEventBus _localEvents;
    private readonly IAgentRuntimeEventFanout _fanout;
    private readonly ILogger<GoalProgressEventPublisher> _logger;

    public GoalProgressEventPublisher(
        NovelAgentDbContext db,
        AgentSseEventBus localEvents,
        IAgentRuntimeEventFanout fanout,
        ILogger<GoalProgressEventPublisher> logger)
    {
        _db = db;
        _localEvents = localEvents;
        _fanout = fanout;
        _logger = logger;
    }

    public async Task PublishAsync(
        GoalProgressEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var goal = await _db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == request.GoalId && item.UserId == request.UserId,
                cancellationToken);
            if (goal == null)
            {
                _logger.LogWarning(
                    "Goal progress event was dropped because the owned Goal was not found. GoalId={GoalId} UserId={UserId}",
                    request.GoalId,
                    request.UserId);
                return;
            }

            Data.Entities.KernelTask? task = null;
            if (!string.IsNullOrWhiteSpace(request.TaskId))
            {
                task = await _db.KernelTasks.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == request.TaskId &&
                    item.GoalId == goal.Id &&
                    item.ProjectId == goal.ProjectId &&
                    item.UserId == goal.UserId,
                    cancellationToken);
            }

            var artifactIds = request.ArtifactIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [];
            var artifactTypes = artifactIds.Length == 0
                ? []
                : await _db.KernelArtifacts.AsNoTracking()
                    .Where(item =>
                        item.UserId == goal.UserId &&
                        item.ProjectId == goal.ProjectId &&
                        item.GoalId == goal.Id &&
                        artifactIds.Contains(item.Id))
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => item.ArtifactType)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);

            var data = new GoalProgressEventData(
                goal.Id,
                goal.ProjectId,
                goal.Status,
                task?.Id ?? request.TaskId,
                task?.TaskType,
                task?.KernelName,
                task?.Status,
                task?.Attempt,
                request.CandidateChapterId,
                request.CandidateVersion,
                request.ChapterNumber,
                request.BranchId ?? task?.BranchId,
                artifactIds,
                artifactTypes,
                request.Action,
                request.Error);
            var evt = new AgentSseEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Type = request.Type,
                StepId = task?.Id ?? request.TaskId,
                Stage = task?.TaskType ?? string.Empty,
                Status = task?.Status ?? goal.Status,
                ArtifactType = artifactTypes.Length == 1 ? artifactTypes[0] : string.Empty,
                ArtifactId = artifactIds.Length == 1 ? artifactIds[0] : string.Empty,
                DisplaySurface = AgentRuntimeEventSurface.Workflow,
                DisplayPolicy = AgentRuntimeEventDisplayPolicy.Timeline,
                Message = request.Message,
                Data = data
            };

            await _localEvents.SendAsync(goal.UserId, goal.SourceSessionId, evt, cancellationToken);
            await _fanout.PublishAsync(goal.UserId, goal.SourceSessionId, evt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Goal progress event publish failed. GoalId={GoalId} Type={Type}",
                request.GoalId,
                request.Type);
        }
    }
}
