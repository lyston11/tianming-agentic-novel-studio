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

/// <summary>
/// Read-side access to the legacy production aggregate. All control-plane and
/// batch-transition mutations moved to Application commands
/// (ILegacyControlPlaneCommands); the write methods of this legacy service had
/// no remaining callers and were removed.
/// </summary>
public interface IBookProductionService
{
    Task<ProductionBatch> GetCurrentBatchAsync(string goalId, CancellationToken cancellationToken = default);
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

    private sealed record BookRange(int Start, int End);
    private sealed record BookPlan(int BatchSize = 5);
}
