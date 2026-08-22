using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebStoryBibleDocumentStore : IStoryBibleDocumentStore
{
    private const string SourceType = "story_bible";
    private const string DocumentRole = "aggregate_json";
    private static readonly TimeSpan MemoryCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RedisCacheDuration = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public WebStoryBibleDocumentStore(IServiceScopeFactory scopeFactory, string userId, string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IDistributedCacheService>();
        var memory = scope.ServiceProvider.GetRequiredService<IMemoryCacheService>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var cacheKey = DocumentCacheKey(_userId, _projectId);

        return await memory.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var cached = await redis.GetAsync<StoryBibleDocument>(cacheKey, ct).ConfigureAwait(false);
                if (cached != null)
                    return cached;

                try
                {
                    var json = await contentDocuments.GetTextAsync(
                        _userId,
                        _projectId,
                        SourceType,
                        _projectId,
                        DocumentRole,
                        ct).ConfigureAwait(false);
                    var document = JsonSerializer.Deserialize<StoryBibleDocument>(json, JsonHelper.CnDefault)
                        ?? new StoryBibleDocument();
                    await redis.SetAsync(cacheKey, document, RedisCacheDuration, ct).ConfigureAwait(false);
                    return document;
                }
                catch (KeyNotFoundException)
                {
                    var empty = new StoryBibleDocument();
                    await redis.SetAsync(cacheKey, empty, RedisCacheDuration, ct).ConfigureAwait(false);
                    return empty;
                }
            },
            MemoryCacheDuration,
            ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IDistributedCacheService>();
        var memory = scope.ServiceProvider.GetRequiredService<IMemoryCacheService>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();

        var projectExists = await db.NovelProjects
            .AsNoTracking()
            .AnyAsync(p => p.Id == _projectId && p.UserId == _userId, ct)
            .ConfigureAwait(false);
        if (!projectExists)
            throw new InvalidOperationException($"Project {_projectId} not found for user {_userId}.");

        var json = JsonSerializer.Serialize(document, JsonHelper.CnDefault);
        await contentDocuments.SaveOrReplaceTextAsync(
            _userId,
            _projectId,
            SourceType,
            _projectId,
            DocumentRole,
            "Story Bible",
            json,
            ct).ConfigureAwait(false);

        await SyncConstitutionAsync(db, document.Constitution, ct).ConfigureAwait(false);
        await SyncVolumeArcsAsync(db, document.VolumeArcs, ct).ConfigureAwait(false);
        await SyncAgentRunsAsync(db, document.AgentRuns, contentDocuments, redis, memory, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var documentCacheKey = DocumentCacheKey(_userId, _projectId);
        memory.Set(documentCacheKey, document, MemoryCacheDuration);
        await redis.SetAsync(documentCacheKey, document, RedisCacheDuration, ct).ConfigureAwait(false);

        var aggregateKey = AggregateCacheKey(_userId, _projectId);
        memory.Remove(aggregateKey);
        await redis.RemoveAsync(aggregateKey, ct).ConfigureAwait(false);
    }

    private async Task SyncConstitutionAsync(
        NovelAgentDbContext db,
        StoryCreativeConstitution? source,
        CancellationToken ct)
    {
        if (source == null)
            return;

        var now = DateTime.UtcNow;
        var entity = await db.StoryConstitutions
            .FirstOrDefaultAsync(c => c.UserId == _userId && c.ProjectId == _projectId, ct)
            .ConfigureAwait(false);

        if (entity == null)
        {
            entity = new StoryConstitution
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = _userId,
                ProjectId = _projectId,
                CreatedAt = now
            };
            db.StoryConstitutions.Add(entity);
        }

        entity.Genre = source.Genre;
        entity.SubGenre = source.SubGenre;
        entity.CoreHook = source.CoreHook;
        entity.ReaderPromise = source.ReaderPromise;
        entity.GenreProfile = JsonSerializer.Serialize(source.GenreProfile, JsonHelper.CnDefault);
        entity.TargetAudience = source.MainPleasure;
        entity.Taboos = JsonSerializer.Serialize(source.ForbiddenDirections, JsonHelper.CnDefault);
        entity.UpdatedAt = now;
    }

    private async Task SyncVolumeArcsAsync(
        NovelAgentDbContext db,
        IReadOnlyList<VolumeArcPlan> source,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < source.Count; i++)
        {
            var plan = source[i];
            var number = ResolveVolumeNumber(plan, i + 1);
            var entity = await db.VolumeArcs
                .FirstOrDefaultAsync(v =>
                    v.UserId == _userId &&
                    v.ProjectId == _projectId &&
                    v.VolumeNumber == number,
                    ct)
                .ConfigureAwait(false);

            if (entity == null)
            {
                entity = new VolumeArc
                {
                    Id = string.IsNullOrWhiteSpace(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id,
                    UserId = _userId,
                    ProjectId = _projectId,
                    VolumeNumber = number,
                    CreatedAt = now
                };
                db.VolumeArcs.Add(entity);
            }

            entity.VolumeTitle = string.IsNullOrWhiteSpace(plan.Title) ? $"Volume {number}" : plan.Title;
            entity.VolumeTheme = plan.VolumePromise;
            entity.TargetChapters = plan.ExpectedChapterCount > 0 ? plan.ExpectedChapterCount : null;
            entity.CurrentChapters = 0;
            entity.Act1Setup = plan.EntryState;
            entity.Act2Confrontation = plan.MainConflictUpgrade;
            entity.Act3Climax = plan.Climax;
            entity.Act4Resolution = plan.ExitState;
            entity.KeyEvents = JsonSerializer.Serialize(plan.ChapterBeats, JsonHelper.CnDefault);
            entity.MajorConflict = plan.CoreQuestion;
            entity.ConflictEscalation = plan.MidpointReversal;
            entity.Status = plan.Status.ToString();
            entity.UpdatedAt = now;
        }
    }

    private async Task SyncAgentRunsAsync(
        NovelAgentDbContext db,
        IReadOnlyList<NovelAgentRun> source,
        IContentDocumentService contentDocuments,
        IDistributedCacheService redis,
        IMemoryCacheService memory,
        CancellationToken ct)
    {
        foreach (var run in source)
        {
            if (string.IsNullOrWhiteSpace(run.RunId))
                continue;

            var now = DateTime.UtcNow;
            var entity = await db.AgentRuns
                .FirstOrDefaultAsync(r => r.UserId == _userId && r.ProjectId == _projectId && r.Id == run.RunId, ct)
                .ConfigureAwait(false);

            if (entity == null)
            {
                entity = new AgentRun
                {
                    Id = run.RunId,
                    UserId = _userId,
                    ProjectId = _projectId,
                    CreatedAt = run.CreatedAt == default ? now : run.CreatedAt
                };
                db.AgentRuns.Add(entity);
            }

            entity.RunType = run.Intent.ToString();
            entity.TargetChapterId = string.IsNullOrWhiteSpace(run.TargetChapterId) ? null : run.TargetChapterId;
            entity.Status = run.Status.ToString();
            entity.InputParams = JsonSerializer.Serialize(new { run.UserGoal, run.Steps }, JsonHelper.CnDefault);
            var runOutputJson = JsonSerializer.Serialize(run, JsonHelper.CnDefault);
            var outputDocument = await contentDocuments.SaveOrReplaceTextAsync(
                    _userId,
                    _projectId,
                    "agent_run",
                    run.RunId,
                    "run_output",
                    $"Agent Run {run.RunId}",
                    runOutputJson,
                    ct)
                .ConfigureAwait(false);
            entity.OutputDocumentId = outputDocument.Id;
            entity.OutputData = JsonSerializer.Serialize(new
            {
                run.RunId,
                Intent = run.Intent.ToString(),
                Status = run.Status.ToString(),
                run.TargetChapterId,
                HasContextPackage = run.ContextPackage != null,
                HasDraftArtifact = run.DraftArtifact != null,
                HasGateReport = run.GateReport != null,
                HasDependencyImpact = run.DependencyImpact != null,
                HasPostGenerationReview = run.PostGenerationReview != null,
                run.UpdatedAt
            }, JsonHelper.CnDefault);
            entity.StartedAt = run.CreatedAt == default ? entity.CreatedAt : run.CreatedAt;
            entity.CompletedAt = IsTerminal(run.Status) ? run.UpdatedAt : null;
            entity.DurationMs = ResolveDurationMs(entity.StartedAt, entity.CompletedAt);
            entity.UpdatedAt = run.UpdatedAt == default ? now : run.UpdatedAt;

            var cacheKey = AgentRunCacheKey(_userId, _projectId, run.RunId);
            memory.Set(cacheKey, entity, MemoryCacheDuration);
            await redis.SetAsync(cacheKey, entity, RedisCacheDuration, ct).ConfigureAwait(false);
        }

        if (source.Any(run => run.Intent == NovelAgentIntent.PlanChapter))
            GuideContextService.RaiseCacheInvalidated();
    }

    private static int ResolveVolumeNumber(VolumeArcPlan plan, int fallback)
    {
        var source = string.IsNullOrWhiteSpace(plan.VolumeId) ? plan.Title : plan.VolumeId;
        var digits = new string((source ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) && number > 0 ? number : fallback;
    }

    private static int? ResolveDurationMs(DateTime startedAt, DateTime? completedAt)
    {
        if (completedAt == null || startedAt == default)
            return null;

        var duration = completedAt.Value - startedAt;
        if (duration.TotalMilliseconds <= 0)
            return null;

        return duration.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)duration.TotalMilliseconds;
    }

    private static bool IsTerminal(NovelAgentRunStatus status) =>
        status is NovelAgentRunStatus.Completed or NovelAgentRunStatus.Cancelled or NovelAgentRunStatus.Failed;

    private static string DocumentCacheKey(string userId, string projectId) =>
        $"storybible:document:{userId}:{projectId}";

    private static string AggregateCacheKey(string userId, string projectId) =>
        $"storybible:{userId}:{projectId}";

    private static string AgentRunCacheKey(string userId, string projectId, string runId) =>
        $"agentrun:{userId}:{projectId}:{runId}";
}
