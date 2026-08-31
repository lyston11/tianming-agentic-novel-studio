using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Workflow;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class EfWorkflowReadModel(AgentControlDbContext db) : IWorkflowReadModel
{
    public async Task<WorkflowProjectView> GetProjectAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var proposals = await db.GoalProposals.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new WorkflowProposalView(
                x.Id,
                x.SourceSessionId,
                x.Status,
                x.ContractHash,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
        var goals = await db.CreativeGoals.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new WorkflowGoalView(
                x.Id,
                x.Status,
                x.HumanReadableObjective,
                x.CurrentRevisionId,
                x.AggregateVersion,
                x.TotalCostLimit,
                x.ReservedCost,
                x.ActualCost))
            .ToListAsync(cancellationToken);
        var productions = await db.BookProductions.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new WorkflowProductionView(
                x.Id,
                x.GoalId,
                x.GoalRevisionId,
                x.ExecutionStrategy,
                x.Status,
                x.TaskGraphVersionId,
                x.AggregateVersion,
                x.TerminalReason))
            .ToListAsync(cancellationToken);
        var goalIds = goals.Select(x => x.Id).ToArray();
        var tasks = await db.KernelTasks.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId && goalIds.Contains(x.GoalId))
            .OrderBy(x => x.Priority)
            .Select(x => new WorkflowTaskView(
                x.Id,
                x.GoalId,
                x.TaskType,
                x.KernelName,
                x.Status,
                x.Attempt,
                x.MaxAttempts,
                x.Priority))
            .ToListAsync(cancellationToken);
        var lastSequence = await db.StreamEvents.AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId && x.StreamKind == "workflow")
            .Select(x => (long?)x.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        return new WorkflowProjectView(projectId, proposals, goals, productions, tasks, lastSequence);
    }
}
