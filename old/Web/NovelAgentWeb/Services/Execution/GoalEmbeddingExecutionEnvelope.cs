using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Services.Embedding;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public interface IGoalEmbeddingExecutionEnvelope
{
    Task<float[]> ExecuteAsync(
        string text,
        EmbeddingMode mode,
        Func<CancellationToken, Task<float[]>> providerCall,
        CancellationToken cancellationToken = default);
}

public sealed class GoalEmbeddingExecutionEnvelope : IGoalEmbeddingExecutionEnvelope
{
    private const string ExecutionKernelName = "embedding";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGoalBudgetService _budget;
    private readonly IKernelModelExecutionScopeAccessor _scopes;
    private readonly EmbeddingRuntimeStatus _runtime;

    public GoalEmbeddingExecutionEnvelope(
        IGoalBudgetService budget,
        IKernelModelExecutionScopeAccessor scopes,
        EmbeddingRuntimeStatus runtime)
    {
        _budget = budget;
        _scopes = scopes;
        _runtime = runtime;
    }

    public async Task<float[]> ExecuteAsync(
        string text,
        EmbeddingMode mode,
        Func<CancellationToken, Task<float[]>> providerCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var scope = _scopes.Current;
        if (scope == null)
            return ValidateVector(await providerCall(cancellationToken));

        var modelConfigVersionId = $"embedding:{_runtime.Provider}:{_runtime.Model}";
        var operationKey = Hash($"{mode}\u001f{text}");
        var reusable = await _budget.FindReusableResultAsync(
            scope.UserId,
            scope.TaskId,
            ExecutionKernelName,
            modelConfigVersionId,
            operationKey,
            cancellationToken);
        if (reusable != null)
        {
            if (reusable.RequiresSettlement)
                await _budget.SettleAsync(scope.UserId, reusable.ExecutionId, cancellationToken);
            var persisted = JsonSerializer.Deserialize<EmbeddingExecutionResult>(reusable.ResultJson, JsonOptions)
                ?? throw new InvalidOperationException("已持久化的 Embedding 结果无法解析。");
            if (persisted.Mode != mode)
                throw new InvalidOperationException("已持久化的 Embedding 模式与当前查询不一致。");
            return ValidateVector(persisted.Vector);
        }

        var reservation = await _budget.ReserveAsync(new GoalBudgetReservationRequest(
            scope.UserId,
            scope.ProjectId,
            scope.GoalId,
            scope.TaskId,
            ExecutionKernelName,
            modelConfigVersionId,
            _runtime.Provider,
            _runtime.Model,
            0,
            Math.Clamp(scope.Attempt, 1, 2),
            operationKey), cancellationToken);
        if (!reservation.Reserved || reservation.ExecutionId == null)
            throw new InvalidOperationException("Goal 当前状态不允许执行查询 Embedding。");

        var providerLeaseOwner = Guid.NewGuid().ToString("N");
        await _budget.MarkProviderCallStartedAsync(
            scope.UserId,
            reservation.ExecutionId,
            providerLeaseOwner,
            TimeSpan.FromSeconds(120),
            cancellationToken);
        var vector = ValidateVector(await providerCall(cancellationToken));
        var resultJson = JsonSerializer.Serialize(new EmbeddingExecutionResult(mode, vector), JsonOptions);
        var estimatedInputTokens = Math.Max(1, (Encoding.UTF8.GetByteCount(text) + 3) / 4);
        await _budget.RecordResultAsync(new ModelExecutionResultRecord(
            scope.UserId,
            reservation.ExecutionId,
            resultJson,
            0,
            estimatedInputTokens,
            0,
            null,
            Hash(resultJson),
            _runtime.Provider,
            _runtime.Model,
            providerLeaseOwner), cancellationToken);
        await _budget.SettleAsync(scope.UserId, reservation.ExecutionId, cancellationToken);
        return vector;
    }

    private float[] ValidateVector(float[] vector)
    {
        if (vector.Length != _runtime.Dimension)
        {
            throw new InvalidOperationException(
                $"Embedding 向量维度 {vector.Length} 与运行时声明 {_runtime.Dimension} 不一致。");
        }
        return vector;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record EmbeddingExecutionResult(EmbeddingMode Mode, float[] Vector);
}
