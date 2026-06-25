using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class WorkspaceProductionRuntimeBuilder : IWorkspaceProductionRuntimeBuilder
{
    public WorkspaceProductionRuntime Build(WorkspaceProductionRuntimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.ScopeFactory);
        ArgumentNullException.ThrowIfNull(request.UnifiedValidationService);

        var registrations = new List<WorkspaceServiceRegistration>();
        var guideManager = new GuideManager();
        IChapterSummaryService summaryService = new ProductionChapterSummaryService(request.ScopeFactory, request.UserId, request.ProjectId);
        IChapterMilestoneService milestoneService = new ProductionChapterMilestoneService(summaryService);
        IChapterChangesRecorder chapterChangesRecorder = new ProductionChapterChangesRecorder(request.ScopeFactory, request.UserId, request.ProjectId);
        IPlotPointRecallService plotPointRecall = new ProductionPlotPointRecallService(request.ScopeFactory, request.ProjectId);
        IVolumeFactArchiveService volumeFactArchiveService = new ProductionVolumeFactArchiveService(request.ScopeFactory, request.UserId, request.ProjectId);
        IDesignElementLookupService designElementLookup = new ProductionDesignElementLookupService(request.ScopeFactory, request.UserId, request.ProjectId);
        IRelationStrengthSourceService relationStrengthSource = new ProductionRelationStrengthSourceService(request.ScopeFactory, request.UserId, request.ProjectId);
        IGuideRuntimeDataSource guideRuntimeDataSource = new ProductionGuideRuntimeDataSource(request.ScopeFactory, request.UserId, request.ProjectId);
        var factSnapshotExtractor = new FactSnapshotExtractor(guideManager);
        var guideContextService = request.GuideContextService
            ?? new GuideContextService(
                factSnapshotExtractor,
                summaryService,
                milestoneService,
                guideRuntimeDataSource,
                relationStrengthSource);
        IContentChunkSearchService contentChunkSearch = request.ContentChunkSearchService
            ?? new WebContentChunkSearchService(request.ScopeFactory, request.UserId, request.ProjectId, request.VectorStore, request.EmbeddingService);
        var generationGate = new GenerationGate(
            new LedgerConsistencyChecker(),
            new LedgerRuleSetProvider(),
            new EntityOmissionDetector(guideManager));
        IChapterCatalogService chapterCatalog = new WebChapterCatalogService(request.ScopeFactory, request.UserId, request.ProjectId);
        IGeneratedContentService generatedContentService = request.GeneratedContentService
            ?? new WebGeneratedContentService(request.ScopeFactory, request.CurrentUserService, request.ProjectId);
        var versionTracking = new WebVersionTrackingService();
        var validationService = request.UnifiedValidationService;
        using var serviceScope = request.ScopeFactory.CreateScope();
        var editorialReviewModel = serviceScope.ServiceProvider.GetService<IAgentEditorialReviewModelClient>();
        var chapterFactPostCommitScheduler = new OutboxChapterFactPostCommitScheduler(
            request.ScopeFactory,
            request.UserId,
            request.ProjectId,
            serviceScope.ServiceProvider.GetService<ILogger<OutboxChapterFactPostCommitScheduler>>()
                ?? NullLogger<OutboxChapterFactPostCommitScheduler>.Instance);

        RegisterProjectDataServices(
            registrations,
            guideManager,
            summaryService,
            milestoneService,
            factSnapshotExtractor,
            guideContextService,
            contentChunkSearch,
            request.EmbeddingService,
            generationGate,
            chapterCatalog,
            generatedContentService,
            versionTracking,
            chapterChangesRecorder,
            plotPointRecall,
            volumeFactArchiveService,
            designElementLookup,
            relationStrengthSource,
            guideRuntimeDataSource);

        var storyStateSnapshotService = new StoryStateSnapshotService(
            guideContextService,
            contentChunkSearch,
            request.StoryBibleService,
            chapterEmbeddingIndex: null,
            chunkEmbeddingIndex: null,
            embeddingService: request.EmbeddingService);
        var productionEngine = new HardcoreWritingEngine(
            storyStateSnapshotService,
            guideContextService,
            generationGate,
            generatedContentService,
            contentChunkSearch,
            settingsManager: request.SettingsManager,
            chapterSummaryService: summaryService,
            chapterFactPostCommitScheduler: chapterFactPostCommitScheduler,
            writingCompletion: (system, user, ct) => CompleteWritingWithCurrentUserAsync(request, system, user, ct));
        var productionKernel = new HardcoreWritingProductionKernel(productionEngine);
        new ProductionKernelAssemblyGuard().EnsureCurrentRepositoryKernel(productionKernel.GetType());

        var orchestrator = new NovelAgentOrchestrator(
            new BookConceptDesigner(new GenreDirectionPlanner()),
            new VolumeArcPlanner(),
            new ChapterNoveltyPlanner(),
            request.StoryBibleService,
            storyStateSnapshotService,
            new ChapterPostGenerationReviewer(
                generatedContentService,
                validationService,
                request.StoryBibleService,
                storyStateSnapshotService,
                editorialReviewModel: editorialReviewModel,
                userId: request.UserId),
            new CanonMaintenanceService(request.StoryBibleService),
            new ForeshadowLedgerService(request.StoryBibleService),
            new CharacterLedgerService(request.StoryBibleService),
            request.CreativeKnowledgeBaseService,
            productionKernel);

        return new WorkspaceProductionRuntime(orchestrator, registrations);
    }

    private static async Task<string> CompleteWritingWithCurrentUserAsync(
        WorkspaceProductionRuntimeRequest request,
        string system,
        string user,
        CancellationToken ct)
    {
        using var scope = request.ScopeFactory.CreateScope();
        var completion = scope.ServiceProvider.GetRequiredService<IWritingModelCompletionService>();
        return await completion.CompleteAsync(request.UserId, system, user, ct).ConfigureAwait(false);
    }

    private static void RegisterProjectDataServices(
        ICollection<WorkspaceServiceRegistration> registrations,
        GuideManager guideManager,
        IChapterSummaryService summaryService,
        IChapterMilestoneService milestoneService,
        FactSnapshotExtractor factSnapshotExtractor,
        IGuideContextService guideContextService,
        IContentChunkSearchService contentChunkSearch,
        IMicroEmbeddingService? embeddingService,
        GenerationGate generationGate,
        IChapterCatalogService? chapterCatalog,
        IGeneratedContentService generatedContentService,
        WebVersionTrackingService versionTracking,
        IChapterChangesRecorder? chapterChangesRecorder,
        IPlotPointRecallService? plotPointRecall,
        IVolumeFactArchiveService volumeFactArchiveService,
        IDesignElementLookupService designElementLookup,
        IRelationStrengthSourceService relationStrengthSource,
        IGuideRuntimeDataSource? guideRuntimeDataSource)
    {
        Register(registrations, guideManager);
        Register<IChapterSummaryService>(registrations, summaryService);
        Register<IChapterMilestoneService>(registrations, milestoneService);
        Register<IVolumeFactArchiveService>(registrations, volumeFactArchiveService);
        Register<IDesignElementLookupService>(registrations, designElementLookup);
        Register<IRelationStrengthSourceService>(registrations, relationStrengthSource);
        if (guideRuntimeDataSource != null)
            Register<IGuideRuntimeDataSource>(registrations, guideRuntimeDataSource);
        Register(registrations, factSnapshotExtractor);
        Register<IGuideContextService>(registrations, guideContextService);
        if (guideContextService is GuideContextService concreteGuideContextService)
            Register(registrations, concreteGuideContextService);
        else
            Register(registrations, guideContextService.GetType(), guideContextService);
        Register<IContentChunkSearchService>(registrations, contentChunkSearch);
        if (embeddingService != null)
            Register(registrations, embeddingService);
        Register(registrations, generationGate);
        if (chapterCatalog != null)
            Register<IChapterCatalogService>(registrations, chapterCatalog);
        Register(registrations, generatedContentService);
        if (chapterChangesRecorder != null)
            Register<IChapterChangesRecorder>(registrations, chapterChangesRecorder);
        if (plotPointRecall != null)
            Register<IPlotPointRecallService>(registrations, plotPointRecall);
        Register(registrations, versionTracking);
        Register(registrations, new CharacterStateService(guideManager, designElementLookup));
        Register(registrations, new ConflictProgressService(guideManager, designElementLookup));
        Register(registrations, new ForeshadowingStatusService(guideManager));
        Register(registrations, new LocationStateService(guideManager, designElementLookup));
        Register(registrations, new FactionStateService(guideManager, designElementLookup));
        Register(registrations, new TimelineService(guideManager));
        Register(registrations, new ItemStateService(guideManager));
        Register(registrations, new SecretRevealService(guideManager));
        Register(registrations, new PledgeConstraintService(guideManager));
        Register(registrations, new DeadlineConstraintService(guideManager));
        Register(registrations, new RelationStrengthService(relationStrengthSource));
        Register(registrations, new LedgerTrimService(guideManager));
    }

    private static void Register<T>(ICollection<WorkspaceServiceRegistration> registrations, T instance)
        where T : class =>
        registrations.Add(new WorkspaceServiceRegistration(typeof(T), instance));

    private static void Register(ICollection<WorkspaceServiceRegistration> registrations, Type type, object instance) =>
        registrations.Add(new WorkspaceServiceRegistration(type, instance));
}

public sealed class WebVersionTrackingService
{
    private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);

    public int IncrementModuleVersion(string moduleName)
    {
        if (!_versions.ContainsKey(moduleName))
            _versions[moduleName] = 0;
        _versions[moduleName]++;
        return _versions[moduleName];
    }

    public IReadOnlyList<string> GetDownstreamModules(string moduleName) =>
        TM.Services.Modules.VersionTracking.DependencyConfig.GetDownstreamModules(moduleName);
}
