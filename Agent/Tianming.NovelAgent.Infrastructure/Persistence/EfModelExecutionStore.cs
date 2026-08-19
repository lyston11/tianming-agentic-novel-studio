using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Models;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class EfModelExecutionStore(
    AgentControlDbContext db,
    IIdGenerator ids,
    IClock clock) : IModelExecutionStore
{
    public async Task<ModelExecutionReservation> ReserveAsync(
        ModelRequest request,
        CancellationToken cancellationToken)
    {
        Require(request.UserId, nameof(request.UserId));
        Require(request.ProjectId, nameof(request.ProjectId));
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey));
        if (request.MaximumCost < 0)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumCost));

        var existing = await db.ModelExecutions.SingleOrDefaultAsync(
            x => x.UserId == request.UserId && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == "completed" && existing.ResultJson is not null)
            {
                return new ModelExecutionReservation(
                    existing.Id,
                    AgentJson.Deserialize<ModelResult>(existing.ResultJson));
            }
            throw new InvalidOperationException(
                $"Model execution {existing.Id} is already {existing.Status}; provider invocation will not be repeated.");
        }

        CreativeGoalRecord? goal = null;
        if (!string.IsNullOrWhiteSpace(request.GoalId))
        {
            goal = await db.CreativeGoals.SingleOrDefaultAsync(
                x => x.UserId == request.UserId && x.Id == request.GoalId,
                cancellationToken) ?? throw new KeyNotFoundException($"Goal {request.GoalId} was not found.");
            if (goal.ReservedCost + goal.ActualCost + request.MaximumCost > goal.TotalCostLimit)
                throw new InvalidOperationException("The model execution would exceed the confirmed goal budget.");
            goal.ReservedCost += request.MaximumCost;
        }

        var execution = new ModelExecutionRecord
        {
            Id = ids.NewId(),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            GoalId = request.GoalId,
            TaskId = request.TaskId,
            KernelName = request.Purpose,
            ModelConfigVersionId = request.PromptVersion,
            Provider = request.Provider,
            Model = request.Model,
            IdempotencyKey = request.IdempotencyKey,
            OperationKey = request.CorrelationId,
            Status = "reserved",
            ReservedCost = request.MaximumCost,
            CreatedAt = clock.UtcNow
        };
        db.ModelExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        return new ModelExecutionReservation(execution.Id, null);
    }

    public async Task MarkStartedAsync(string executionId, CancellationToken cancellationToken)
    {
        var execution = await RequireExecutionAsync(executionId, cancellationToken);
        if (execution.Status != "reserved")
            throw new InvalidOperationException($"Model execution {executionId} cannot start from {execution.Status}.");
        execution.Status = "running";
        execution.StartedAt = clock.UtcNow;
        execution.Attempt++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        string executionId,
        ModelResult result,
        CancellationToken cancellationToken)
    {
        var execution = await RequireExecutionAsync(executionId, cancellationToken);
        if (execution.Status != "running")
            throw new InvalidOperationException($"Model execution {executionId} cannot complete from {execution.Status}.");

        if (!string.IsNullOrWhiteSpace(execution.GoalId))
        {
            var goal = await db.CreativeGoals.SingleAsync(
                x => x.UserId == execution.UserId && x.Id == execution.GoalId,
                cancellationToken);
            goal.ReservedCost = Math.Max(0, goal.ReservedCost - execution.ReservedCost);
            goal.ActualCost += result.Usage.ActualCost;
        }

        execution.Status = "completed";
        execution.ActualCost = result.Usage.ActualCost;
        execution.Currency = result.Usage.Currency;
        execution.InputTokens = result.Usage.InputTokens;
        execution.OutputTokens = result.Usage.OutputTokens;
        execution.ProviderRequestId = result.ProviderRequestId;
        execution.ResultJson = AgentJson.Serialize(result);
        execution.ResultContentHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Content)));
        execution.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkOutcomeUnknownAsync(
        string executionId,
        string error,
        CancellationToken cancellationToken)
    {
        var execution = await RequireExecutionAsync(executionId, cancellationToken);
        if (execution.Status == "completed")
            return;
        execution.Status = "outcome_unknown";
        execution.ErrorMessage = error;
        execution.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ModelExecutionRecord> RequireExecutionAsync(
        string executionId,
        CancellationToken cancellationToken) =>
        await db.ModelExecutions.SingleOrDefaultAsync(x => x.Id == executionId, cancellationToken)
        ?? throw new KeyNotFoundException($"Model execution {executionId} was not found.");

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }
}
