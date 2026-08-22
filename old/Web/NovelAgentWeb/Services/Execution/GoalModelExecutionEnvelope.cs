using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Models;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public interface IGoalModelExecutionEnvelope
{
    Task<WritingModelCompletionResult> ExecuteAsync(
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        Func<CancellationToken, Task<WritingModelCompletionResult>> providerCall,
        CancellationToken cancellationToken = default);
}

public sealed class GoalModelExecutionEnvelope : IGoalModelExecutionEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGoalBudgetService _budget;
    private readonly IKernelModelExecutionScopeAccessor _scopes;

    public GoalModelExecutionEnvelope(
        IGoalBudgetService budget,
        IKernelModelExecutionScopeAccessor scopes)
    {
        _budget = budget;
        _scopes = scopes;
    }

    public async Task<WritingModelCompletionResult> ExecuteAsync(
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        Func<CancellationToken, Task<WritingModelCompletionResult>> providerCall,
        CancellationToken cancellationToken = default)
    {
        var scope = _scopes.Current
            ?? throw new InvalidOperationException("Goal 模型执行信封只能在 Kernel execution scope 内使用。");
        var operationKey = Hash(string.Join('\u001f', scope.KernelName, system, user));
        var modelConfigVersionId = configuration.Version.HasValue
            ? $"{configuration.KernelName}:v{configuration.Version.Value}"
            : $"{configuration.KernelName}:system";
        var reusable = await _budget.FindReusableResultAsync(
            scope.UserId,
            scope.TaskId,
            scope.KernelName,
            modelConfigVersionId,
            operationKey,
            cancellationToken);
        if (reusable != null)
        {
            if (reusable.RequiresSettlement)
                await _budget.SettleAsync(scope.UserId, reusable.ExecutionId, cancellationToken);
            return JsonSerializer.Deserialize<WritingModelCompletionResult>(reusable.ResultJson, JsonOptions)
                ?? throw new InvalidOperationException("已持久化模型结果无法解析。");
        }

        var estimatedInputTokens = Encoding.UTF8.GetByteCount(system) + Encoding.UTF8.GetByteCount(user);
        var worstCaseCost = CalculateCost(
            estimatedInputTokens,
            configuration.MaxOutputTokens,
            configuration.InputPricePerMillion,
            configuration.OutputPricePerMillion);
        worstCaseCost += configuration.Fallbacks.Sum(fallback => CalculateCost(
            estimatedInputTokens,
            configuration.MaxOutputTokens,
            fallback.InputPricePerMillion,
            fallback.OutputPricePerMillion));
        var reservation = await _budget.ReserveAsync(new GoalBudgetReservationRequest(
            scope.UserId,
            scope.ProjectId,
            scope.GoalId,
            scope.TaskId,
            scope.KernelName,
            modelConfigVersionId,
            configuration.Provider,
            configuration.Model,
            worstCaseCost,
            Math.Clamp(scope.Attempt, 1, 2),
            operationKey), cancellationToken);
        if (!reservation.Reserved || reservation.ExecutionId == null)
            throw new InvalidOperationException("Goal 预算不足，模型调用未发送，Goal 已进入 budget_exceeded。");

        var providerLeaseOwner = Guid.NewGuid().ToString("N");
        await _budget.MarkProviderCallStartedAsync(
            scope.UserId,
            reservation.ExecutionId,
            providerLeaseOwner,
            TimeSpan.FromSeconds(configuration.TimeoutSeconds * (configuration.Fallbacks.Count + 1)),
            cancellationToken);
        WritingModelCompletionResult result;
        try
        {
            result = await providerCall(cancellationToken);
        }
        catch (ModelCallKnownFailureException exception)
        {
            await _budget.FailKnownAsync(
                scope.UserId,
                reservation.ExecutionId,
                providerLeaseOwner,
                exception.Message,
                cancellationToken);
            throw;
        }
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);
        var actualCost = result.UsageReported
            ? CalculateCost(
                result.InputTokens,
                result.OutputTokens,
                result.InputPricePerMillion,
                result.OutputPricePerMillion)
            : reservation.ReservedCost;
        await _budget.RecordResultAsync(new ModelExecutionResultRecord(
            scope.UserId,
            reservation.ExecutionId,
            resultJson,
            actualCost,
            result.InputTokens,
            result.OutputTokens,
            result.ProviderRequestId,
            Hash(resultJson),
            result.Provider,
            result.Model,
            providerLeaseOwner), cancellationToken);
        await _budget.SettleAsync(scope.UserId, reservation.ExecutionId, cancellationToken);
        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static decimal CalculateCost(
        int inputTokens,
        int outputTokens,
        decimal inputPricePerMillion,
        decimal outputPricePerMillion) =>
        decimal.Round(
            (inputTokens * inputPricePerMillion / 1_000_000m) +
            (outputTokens * outputPricePerMillion / 1_000_000m),
            6,
            MidpointRounding.ToPositiveInfinity);
}
