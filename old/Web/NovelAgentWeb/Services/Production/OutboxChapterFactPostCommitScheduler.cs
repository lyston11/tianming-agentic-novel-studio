using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class OutboxChapterFactPostCommitScheduler : IChapterFactPostCommitScheduler
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProductionTruthStore? _truthStore;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;
    private readonly ILogger<OutboxChapterFactPostCommitScheduler> _logger;

    public OutboxChapterFactPostCommitScheduler(
        IProductionTruthStore truthStore,
        string userId,
        string projectId,
        ILogger<OutboxChapterFactPostCommitScheduler> logger)
    {
        _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
        _userId = userId;
        _projectId = projectId;
        _logger = logger;
    }

    public OutboxChapterFactPostCommitScheduler(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId,
        ILogger<OutboxChapterFactPostCommitScheduler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
        _logger = logger;
    }

    public async Task ScheduleAsync(
        ChapterFactWriteRequest request,
        Action<ChapterFactWriteResult>? onCompleted = null,
        Action<Exception>? onFailed = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var truthStore = ResolveTruthStore(out var scope);
            await using var asyncScope = scope;
            var run = request.Run;
            var chapterId = FirstNonEmpty(run.TargetChapterId, request.ContextPackage.ChapterId);
            await truthStore.EnqueueOutboxAsync(
                    new EnqueueOutboxEventRequest(
                        UserId: _userId,
                        ProjectId: _projectId,
                        RuntimeRunId: run.RunId,
                        EventType: "extract_chapter_continuity_facts",
                        AggregateType: "chapter",
                        AggregateId: chapterId,
                        PayloadJson: JsonSerializer.Serialize(new
                        {
                            run,
                            request.ContextPackage,
                            request.CommittedContent
                        }, PayloadJsonOptions)),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onFailed?.Invoke(ex);
            _logger.LogWarning(
                ex,
                "Failed to enqueue chapter fact extraction outbox for run {RunId}, chapter {ChapterId}",
                request.Run.RunId,
                request.Run.TargetChapterId);
        }
    }

    private IProductionTruthStore ResolveTruthStore(out AsyncServiceScope? scope)
    {
        if (_truthStore != null)
        {
            scope = null;
            return _truthStore;
        }

        if (_scopeFactory == null)
            throw new InvalidOperationException("Outbox scheduler requires either a truth store or scope factory.");

        var created = _scopeFactory.CreateAsyncScope();
        scope = created;
        return created.ServiceProvider.GetRequiredService<IProductionTruthStore>();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
