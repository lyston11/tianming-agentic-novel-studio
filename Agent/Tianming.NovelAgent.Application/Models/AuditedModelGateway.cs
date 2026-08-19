using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Models;

namespace Tianming.NovelAgent.Application.Models;

public sealed class AuditedModelGateway(
    IEnumerable<IModelProviderAdapter> providers,
    IModelExecutionStore executions) : IModelGateway
{
    private readonly IReadOnlyDictionary<string, IModelProviderAdapter> _providers = providers
        .ToDictionary(x => x.Provider, StringComparer.OrdinalIgnoreCase);

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(request.Provider, out var provider))
            throw new InvalidOperationException($"No model provider adapter is registered for '{request.Provider}'.");
        if (request.JsonSchema is not null && !provider.Capabilities.StructuredOutput)
            throw new InvalidOperationException($"Provider '{request.Provider}' does not support structured output.");
        if (request.Stream && !provider.Capabilities.Streaming)
            throw new InvalidOperationException($"Provider '{request.Provider}' does not support streaming.");

        var reservation = await executions.ReserveAsync(request, cancellationToken);
        if (reservation.CompletedResult is not null)
            return reservation.CompletedResult;

        await executions.MarkStartedAsync(reservation.ExecutionId, cancellationToken);
        try
        {
            var result = await provider.CompleteAsync(request, cancellationToken);
            await executions.CompleteAsync(reservation.ExecutionId, result, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await executions.MarkOutcomeUnknownAsync(
                reservation.ExecutionId,
                exception.Message,
                CancellationToken.None);
            throw;
        }
    }
}
