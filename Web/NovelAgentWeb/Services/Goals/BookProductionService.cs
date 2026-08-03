using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public static class BookExecutionStrategies
{
    public const string FullAuto = "full_auto";
    public const string InteractiveBatch = "interactive_batch";

    public static string RequireValid(string value) => value switch
    {
        FullAuto => FullAuto,
        InteractiveBatch => InteractiveBatch,
        _ => throw new ArgumentException("执行策略只能是 full_auto 或 interactive_batch。", nameof(value))
    };
}

public sealed record BookProductionAdvanceResult(
    BookProduction Production,
    ProductionBatch CompletedBatch,
    ProductionBatch? NextBatch,
    bool ShouldCompileNextBatch);

public interface IBookProductionService
{
    Task<BookProduction> InitializeAsync(CreativeGoal goal, CancellationToken cancellationToken = default);
    Task<ProductionBatch> GetCurrentBatchAsync(string goalId, CancellationToken cancellationToken = default);
    Task BindCompiledBatchAsync(string goalId, string graphId, string branchId, CancellationToken cancellationToken = default);
    Task<BookProduction> ChangeStrategyAsync(string goalId, string executionStrategy, CancellationToken cancellationToken = default);
    Task<BookProductionAdvanceResult> CompleteBatchAsync(string goalId, string branchId, CancellationToken cancellationToken = default);
    Task<BookProductionAdvanceResult> FinalizeMergedBatchAsync(string goalId, string branchId, CancellationToken cancellationToken = default);
    Task<ProductionBatch> ContinueInteractiveAsync(string goalId, CancellationToken cancellationToken = default);
}

public sealed class BookProductionService : IBookProductionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IBookValidationService _bookValidation;

    public BookProductionService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IBookValidationService bookValidation)
    {
        _db = db;
        _currentUser = currentUser;
        _bookValidation = bookValidation;
    }

    public async Task<BookProduction> InitializeAsync(
        CreativeGoal goal,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == goal.UserId && item.GoalId == goal.Id,
            cancellationToken);
        if (existing != null)
            return existing;

        var range = ParseBookRange(goal.TargetChapterRangeJson);
        var plan = ParseBookPlan(goal.BookPlanJson);
        var strategy = BookExecutionStrategies.RequireValid(goal.ExecutionStrategy);
        var batchSize = plan.BatchSize is >= 1 and <= 20 ? plan.BatchSize : 5;
        var production = new BookProduction
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = goal.UserId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            ExecutionStrategy = strategy,
            Status = "running",
            TargetStartChapterNumber = range.Start,
            TargetEndChapterNumber = range.End,
            NextChapterNumber = range.Start,
            BatchSize = batchSize,
            CurrentBatchNumber = 1,
            CompletionCriteriaJson = goal.BookPlanJson,
            PausePolicyJson = goal.AcceptancePolicyJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var batch = CreateBatch(production, range.Start, 1);
        _db.BookProductions.Add(production);
        _db.ProductionBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);
        return production;
    }

    public async Task<ProductionBatch> GetCurrentBatchAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var production = await _db.BookProductions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken) ?? throw new InvalidOperationException("Goal 缺少整书生产状态。");
        return await _db.ProductionBatches.SingleOrDefaultAsync(item =>
            item.UserId == userId &&
            item.BookProductionId == production.Id &&
            item.BatchNumber == production.CurrentBatchNumber,
            cancellationToken) ?? throw new InvalidOperationException("整书生产缺少当前批次。");
    }

    public async Task BindCompiledBatchAsync(
        string goalId,
        string graphId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await GetCurrentBatchAsync(goalId, cancellationToken);
        batch.TaskGraphVersionId = graphId;
        batch.CanonBranchId = branchId;
        batch.Status = "running";
        batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<BookProduction> ChangeStrategyAsync(
        string goalId,
        string executionStrategy,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var strategy = BookExecutionStrategies.RequireValid(executionStrategy);
        var production = await _db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken) ?? throw new KeyNotFoundException("整书生产不存在或不属于当前用户。");
        var hasRunningTasks = await _db.KernelTasks.AsNoTracking().AnyAsync(item =>
            item.UserId == userId && item.GoalId == goalId && item.Status == "running",
            cancellationToken);
        if (hasRunningTasks)
            throw new InvalidOperationException("执行策略只能在批次边界切换。");

        production.ExecutionStrategy = strategy;
        production.UpdatedAt = DateTime.UtcNow;
        production.AggregateVersion++;
        var goal = await _db.CreativeGoals.SingleAsync(item => item.UserId == userId && item.Id == goalId, cancellationToken);
        goal.ExecutionStrategy = strategy;
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return production;
    }

    public async Task<BookProductionAdvanceResult> CompleteBatchAsync(
        string goalId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var production = await _db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken) ?? throw new InvalidOperationException("Goal 缺少整书生产状态。");
        var batch = await _db.ProductionBatches.SingleOrDefaultAsync(item =>
            item.UserId == userId &&
            item.BookProductionId == production.Id &&
            item.BatchNumber == production.CurrentBatchNumber &&
            item.CanonBranchId == branchId,
            cancellationToken) ?? throw new InvalidOperationException("合并分支不属于当前生产批次。");
        if (batch.Status == "completed")
            return new BookProductionAdvanceResult(production, batch, null, false);

        var now = DateTime.UtcNow;
        batch.Status = "completed";
        batch.CompletedAt = now;
        batch.UpdatedAt = now;
        production.NextChapterNumber = batch.EndChapterNumber + 1;
        production.UpdatedAt = now;
        production.AggregateVersion++;
        var goal = await _db.CreativeGoals.SingleAsync(item => item.UserId == userId && item.Id == goalId, cancellationToken);

        if (production.NextChapterNumber > production.TargetEndChapterNumber)
        {
            var validation = await _bookValidation.ValidateAsync(new BookValidationRequest(
                userId,
                production.ProjectId,
                production.TargetStartChapterNumber,
                production.TargetEndChapterNumber), cancellationToken);
            var prefixTask = await _db.KernelTasks.AsNoTracking()
                .Where(item => item.UserId == userId && item.GoalId == goalId && item.BranchId == branchId && item.TaskType == "PrefixMerge")
                .OrderByDescending(item => item.CreatedAt)
                .FirstAsync(cancellationToken);
            var reportJson = JsonSerializer.Serialize(validation, JsonOptions);
            _db.KernelArtifacts.Add(new KernelArtifact
            {
                Id = $"book-validation:{production.Id}:{production.AggregateVersion}",
                UserId = userId,
                ProjectId = production.ProjectId,
                GoalId = goalId,
                TaskId = prefixTask.Id,
                BranchId = branchId,
                ArtifactType = "BookValidationReport",
                SchemaVersion = 1,
                ContentJson = reportJson,
                ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reportJson))).ToLowerInvariant(),
                Status = validation.OverallStatus == "blocked" ? "unadopted" : "adopted",
                Authorship = "system",
                IsProtected = true,
                CreatedAt = now
            });
            if (validation.OverallStatus == "blocked")
            {
                production.Status = "blocked";
                goal.Status = "awaiting_decision";
                goal.AggregateVersion++;
                await _db.SaveChangesAsync(cancellationToken);
                return new BookProductionAdvanceResult(production, batch, null, false);
            }
            production.Status = "completed";
            production.CompletedAt = now;
            goal.Status = "completed";
            goal.AggregateVersion++;
            var project = await _db.NovelProjects.SingleAsync(item =>
                item.Id == production.ProjectId && item.UserId == userId,
                cancellationToken);
            project.Status = "completed";
            project.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            return new BookProductionAdvanceResult(production, batch, null, false);
        }

        production.CurrentBatchNumber++;
        var next = CreateBatch(production, production.NextChapterNumber, production.CurrentBatchNumber);
        _db.ProductionBatches.Add(next);
        var auto = production.ExecutionStrategy == BookExecutionStrategies.FullAuto;
        production.Status = auto ? "running" : "awaiting_user";
        goal.Status = auto ? "running" : "awaiting_next_batch";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return new BookProductionAdvanceResult(production, batch, next, auto);
    }

    public async Task<BookProductionAdvanceResult> FinalizeMergedBatchAsync(
        string goalId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var batch = await GetCurrentBatchAsync(goalId, cancellationToken);
        if (batch.CanonBranchId != branchId || string.IsNullOrWhiteSpace(batch.TaskGraphVersionId))
            throw new InvalidOperationException("合并分支未绑定当前生产批次任务图。");
        var graphId = batch.TaskGraphVersionId;
        var tasks = await _db.KernelTasks.Where(task =>
            task.UserId == userId &&
            task.GoalId == goalId &&
            task.TaskGraphVersionId == graphId &&
            (task.TaskType == BookProductionWorkflow.AcceptanceGate || task.TaskType == BookProductionWorkflow.PrefixMerge))
            .ToListAsync(cancellationToken);
        if (tasks.Count != 2)
            throw new InvalidOperationException("批次验收或前缀合并任务缺失。");
        var taskIds = tasks.Select(task => task.Id).ToArray();
        var artifacts = await _db.KernelArtifacts.AsNoTracking()
            .Where(artifact =>
                artifact.UserId == userId &&
                artifact.GoalId == goalId &&
                artifact.BranchId == branchId &&
                taskIds.Contains(artifact.TaskId) &&
                (artifact.ArtifactType == "AcceptanceDecision" || artifact.ArtifactType == "MergeRecord"))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var task in tasks)
        {
            var expected = task.TaskType == "PrefixMerge" ? "MergeRecord" : "AcceptanceDecision";
            var ids = artifacts.Where(item => item.TaskId == task.Id && item.ArtifactType == expected)
                .Select(item => item.Id).ToArray();
            if (ids.Length == 0)
                throw new InvalidOperationException($"批次 {task.TaskType} 任务缺少权威 Artifact 输出。");
            task.Status = "completed";
            task.OutputArtifactIdsJson = JsonSerializer.Serialize(ids);
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
            task.CompletedAt = now;
            task.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return await CompleteBatchAsync(goalId, branchId, cancellationToken);
    }

    public async Task<ProductionBatch> ContinueInteractiveAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var production = await _db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken) ?? throw new KeyNotFoundException("整书生产不存在或不属于当前用户。");
        if (production.ExecutionStrategy != BookExecutionStrategies.InteractiveBatch || production.Status != "awaiting_user")
            throw new InvalidOperationException("当前整书生产不在等待下一批的交互状态。");
        var batch = await _db.ProductionBatches.SingleAsync(item =>
            item.UserId == userId && item.BookProductionId == production.Id && item.BatchNumber == production.CurrentBatchNumber,
            cancellationToken);
        production.Status = "running";
        production.UpdatedAt = DateTime.UtcNow;
        production.AggregateVersion++;
        var goal = await _db.CreativeGoals.SingleAsync(item => item.UserId == userId && item.Id == goalId, cancellationToken);
        goal.Status = "running";
        goal.AggregateVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    private static ProductionBatch CreateBatch(BookProduction production, int start, int number)
    {
        var end = Math.Min(production.TargetEndChapterNumber, start + production.BatchSize - 1);
        return new ProductionBatch
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = production.UserId,
            ProjectId = production.ProjectId,
            GoalId = production.GoalId,
            BookProductionId = production.Id,
            BatchNumber = number,
            StartChapterNumber = start,
            EndChapterNumber = end,
            Status = "planned",
            AcceptanceActor = production.ExecutionStrategy == BookExecutionStrategies.FullAuto
                ? BookProductionWorkflow.AgentPolicyActor
                : BookProductionWorkflow.UserActor,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static BookRange ParseBookRange(string json)
    {
        var range = JsonSerializer.Deserialize<BookRange>(json, JsonOptions)
            ?? throw new InvalidOperationException("整书合同缺少章节范围。");
        if (range.Start <= 0 || range.End < range.Start)
            throw new InvalidOperationException("整书章节范围无效。");
        return range;
    }

    private static BookPlan ParseBookPlan(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return new BookPlan(5);
        return JsonSerializer.Deserialize<BookPlan>(json, JsonOptions)
            ?? throw new InvalidOperationException("整书计划不能为空。");
    }

    private sealed record BookRange(int Start, int End);
    private sealed record BookPlan(int BatchSize = 5);
}
