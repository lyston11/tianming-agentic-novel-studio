namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionOutboxHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductionOutboxHostedService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    public ProductionOutboxHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductionOutboxHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DispatchOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IProductionOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(maxItems: 20, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Production outbox dispatch loop failed.");
        }
    }
}
