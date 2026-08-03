using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class BookProductionWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BookProductionWorker> _logger;

    public BookProductionWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BookProductionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_configuration.GetValue("TargetArchitecture:ExecutionEnabled", false))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            var processed = await TryProcessOneAsync(stoppingToken);
            if (!processed)
                await Task.Delay(IdleDelay, stoppingToken);
        }
    }

    private async Task<bool> TryProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var staleAcceptanceBefore = DateTime.UtcNow.AddMinutes(-5);
        var ready = await (
            from production in db.BookProductions.AsNoTracking()
            join batch in db.ProductionBatches.AsNoTracking()
                on new { production.UserId, ProductionId = production.Id, Number = production.CurrentBatchNumber }
                equals new { batch.UserId, ProductionId = batch.BookProductionId, Number = batch.BatchNumber }
            join task in db.KernelTasks.AsNoTracking()
                on new { production.UserId, production.GoalId, GraphId = batch.TaskGraphVersionId, TaskType = BookProductionWorkflow.AcceptanceGate }
                equals new { task.UserId, task.GoalId, GraphId = (string?)task.TaskGraphVersionId, task.TaskType }
            where production.ExecutionStrategy == BookExecutionStrategies.FullAuto &&
                  production.Status == "running" &&
                  (batch.Status == "running" ||
                   (batch.Status == "accepting" && batch.UpdatedAt < staleAcceptanceBefore)) &&
                  task.Status == "awaiting_user"
            orderby batch.UpdatedAt
            select new { production.UserId, production.GoalId, BatchId = batch.Id, batch.CanonBranchId })
            .FirstOrDefaultAsync(cancellationToken);
        if (ready == null || string.IsNullOrWhiteSpace(ready.CanonBranchId))
            return false;

        var claimed = await db.ProductionBatches
            .Where(item =>
                item.Id == ready.BatchId &&
                item.UserId == ready.UserId &&
                (item.Status == "running" ||
                 (item.Status == "accepting" && item.UpdatedAt < staleAcceptanceBefore)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "accepting")
                .SetProperty(item => item.AcceptanceActor, BookProductionWorkflow.AgentPolicyActor)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
        if (claimed != 1)
            return true;

        var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        using var userScope = backgroundUser.Push(ready.UserId);
        try
        {
            var transitions = scope.ServiceProvider.GetRequiredService<IBookProductionTransitionService>();
            await transitions.AdvanceAfterAcceptanceAsync(
                ready.GoalId,
                ready.CanonBranchId,
                BookProductionWorkflow.AgentPolicyActor,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Full-auto book production stopped at batch {BatchId}.", ready.BatchId);
            try
            {
                var transitions = scope.ServiceProvider.GetRequiredService<IBookProductionTransitionService>();
                await transitions.BlockAsync(ready.GoalId, ready.BatchId, cancellationToken);
            }
            catch (Exception blockException)
            {
                _logger.LogError(blockException, "Failed to block book production batch {BatchId} after transition failure.", ready.BatchId);
            }
        }
        return true;
    }
}
