using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeProcessingWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundUserContext _backgroundUser;
    private readonly ILogger<KnowledgeProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public KnowledgeProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IBackgroundUserContext backgroundUser,
        ILogger<KnowledgeProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _backgroundUser = backgroundUser;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            KnowledgeProcessingTaskClaim? claim;
            await using (var claimScope = _scopeFactory.CreateAsyncScope())
            {
                var claimer = claimScope.ServiceProvider.GetRequiredService<IKnowledgeProcessingTaskClaimer>();
                claim = await claimer.ClaimNextAsync(_workerId, TimeSpan.FromMinutes(5), stoppingToken);
            }

            if (claim == null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            using var userScope = _backgroundUser.Push(claim.UserId);
            await using var processingScope = _scopeFactory.CreateAsyncScope();
            var runner = processingScope.ServiceProvider.GetRequiredService<KnowledgeProcessingTaskRunner>();
            try
            {
                await runner.RunAsync(claim, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Knowledge processing task failed. TaskId={TaskId} UserId={UserId} Stage={Stage} Attempt={Attempt}",
                    claim.TaskId,
                    claim.UserId,
                    claim.ProcessingStage,
                    claim.Attempt);
            }
        }
    }
}
