using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;

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
        var ready = await (
            from production in db.BookProductions.AsNoTracking()
            join batch in db.ProductionBatches.AsNoTracking()
                on new { production.UserId, ProductionId = production.Id, Number = production.CurrentBatchNumber }
                equals new { batch.UserId, ProductionId = batch.BookProductionId, Number = batch.BatchNumber }
            join task in db.KernelTasks.AsNoTracking()
                on new { production.UserId, production.GoalId, GraphId = batch.TaskGraphVersionId, TaskType = "UserAcceptance" }
                equals new { task.UserId, task.GoalId, GraphId = (string?)task.TaskGraphVersionId, task.TaskType }
            where production.ExecutionStrategy == BookExecutionStrategies.FullAuto &&
                  production.Status == "running" &&
                  batch.Status == "running" &&
                  task.Status == "awaiting_user"
            orderby batch.UpdatedAt
            select new { production.UserId, production.GoalId, BatchId = batch.Id, batch.CanonBranchId })
            .FirstOrDefaultAsync(cancellationToken);
        if (ready == null || string.IsNullOrWhiteSpace(ready.CanonBranchId))
            return false;

        var claimed = await db.ProductionBatches
            .Where(item => item.Id == ready.BatchId && item.UserId == ready.UserId && item.Status == "running")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "accepting")
                .SetProperty(item => item.AcceptanceActor, "agent")
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
        if (claimed != 1)
            return true;

        var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        using var userScope = backgroundUser.Push(ready.UserId);
        try
        {
            var candidates = await db.CandidateChapters.AsNoTracking()
                .Where(item => item.UserId == ready.UserId && item.GoalId == ready.GoalId && item.BranchId == ready.CanonBranchId)
                .OrderBy(item => item.ChapterNumber)
                .ThenByDescending(item => item.Version)
                .ToListAsync(cancellationToken);
            var latest = candidates.GroupBy(item => item.ChapterNumber).Select(group => group.First()).ToArray();
            var batch = await db.ProductionBatches.AsNoTracking().SingleAsync(item => item.Id == ready.BatchId, cancellationToken);
            if (latest.Length != batch.EndChapterNumber - batch.StartChapterNumber + 1)
                throw new InvalidOperationException("自动验收时批次候选章节不完整。");

            var branches = scope.ServiceProvider.GetRequiredService<ICanonBranchService>();
            foreach (var candidate in latest)
                await branches.AcceptAsync(candidate.Id, candidate.Version, "agent", cancellationToken);
            var progress = scope.ServiceProvider.GetRequiredService<IGoalProgressEventPublisher>();
            await progress.PublishAsync(new GoalProgressEventRequest(
                ready.UserId,
                ready.GoalId,
                AgentSseEventType.GoalCandidateAccepted,
                $"第 {batch.StartChapterNumber}-{batch.EndChapterNumber} 章已由 Agent 完成工作流验收。",
                BranchId: ready.CanonBranchId,
                Action: "agent_accepted"), cancellationToken);

            var prefixMerge = scope.ServiceProvider.GetRequiredService<IPrefixMergeService>();
            var merge = await prefixMerge.MergeAcceptedPrefixAsync(ready.CanonBranchId, cancellationToken);
            if (merge.EndChapterNumber != batch.EndChapterNumber)
                throw new InvalidOperationException("自动验收只能合并完整批次前缀。");

            var productions = scope.ServiceProvider.GetRequiredService<IBookProductionService>();
            var advance = await productions.FinalizeMergedBatchAsync(ready.GoalId, ready.CanonBranchId, cancellationToken);
            await progress.PublishAsync(new GoalProgressEventRequest(
                ready.UserId,
                ready.GoalId,
                AgentSseEventType.GoalPrefixMerged,
                $"第 {merge.StartChapterNumber}-{merge.EndChapterNumber} 章已自动合并到正史。",
                BranchId: ready.CanonBranchId,
                Action: advance.Production.Status), cancellationToken);
            if (advance.ShouldCompileNextBatch)
            {
                var compiler = scope.ServiceProvider.GetRequiredService<IGoalCompiler>();
                await compiler.CompileAsync(ready.GoalId, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Full-auto book production stopped at batch {BatchId}.", ready.BatchId);
            var production = await db.BookProductions.SingleAsync(item => item.UserId == ready.UserId && item.GoalId == ready.GoalId, cancellationToken);
            var batch = await db.ProductionBatches.SingleAsync(item => item.Id == ready.BatchId, cancellationToken);
            var goal = await db.CreativeGoals.SingleAsync(item => item.UserId == ready.UserId && item.Id == ready.GoalId, cancellationToken);
            production.Status = "blocked";
            production.UpdatedAt = DateTime.UtcNow;
            production.AggregateVersion++;
            batch.Status = "blocked";
            batch.UpdatedAt = DateTime.UtcNow;
            goal.Status = "awaiting_decision";
            goal.AggregateVersion++;
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }
}
