using Microsoft.Extensions.Hosting;

namespace TM.Web.NovelAgentWeb.Services.Embedding;

public sealed class EmbeddingModeReporter : IHostedService
{
    private readonly EmbeddingRuntimeStatus _status;
    private readonly ILogger<EmbeddingModeReporter> _logger;

    public EmbeddingModeReporter(EmbeddingRuntimeStatus status, ILogger<EmbeddingModeReporter> logger)
    {
        _status = status;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_status.IsDegraded)
        {
            _logger.LogWarning(
                "Embedding provider {Provider} is running in degraded semantic mode: {Warning}",
                _status.Provider,
                _status.Warning);
        }
        else
        {
            _logger.LogInformation(
                "Embedding provider {Provider} using model {Model} is ready with {Quality} semantic quality.",
                _status.Provider,
                _status.Model,
                _status.SemanticQuality);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
