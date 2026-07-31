using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Interceptors;
using TM.Web.NovelAgentWeb.DataMigration;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Middleware;
using TM.Web.NovelAgentWeb.Services;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Health;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.DesignRules;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.ChapterBlueprints;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Models;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Quality;
using TM.Web.NovelAgentWeb.Services.Projects;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.Rework;
using TM.Web.NovelAgentWeb.Services.Settings;
using TM.Web.NovelAgentWeb.Services.StoryBible;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using TM.Web.NovelAgentWeb.Services.Workflow;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using Qdrant.Client;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ApiEnvelopeResultFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpClientFactory for Qdrant health check
builder.Services.AddHttpClient();

// PostgreSQL is the sole authoritative production database.
builder.Services.AddScoped<UserScopeConnectionInterceptor>();
builder.Services.AddDbContext<PostgresNovelAgentDbContext>((sp, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("NovelAgentDb"))
        .AddInterceptors(sp.GetRequiredService<UserScopeConnectionInterceptor>()));
builder.Services.AddScoped<NovelAgentDbContext>(sp => sp.GetRequiredService<PostgresNovelAgentDbContext>());
builder.Services.AddSingleton<IBackgroundClaimConnectionFactory, BackgroundClaimConnectionFactory>();
builder.Services.AddSingleton<IBackgroundClaimDatabasePreflight, BackgroundClaimDatabasePreflight>();

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secretKey = jwtSettings["SecretKey"]
        ?? throw new InvalidOperationException("JWT SecretKey is not configured");
    var issuer = jwtSettings["Issuer"] ?? "NovelAgentWeb";
    var audience = jwtSettings["Audience"] ?? "NovelAgentWeb";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

});

builder.Services.AddAuthorization();

// Register Memory Cache
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMemoryCacheService, MemoryCacheService>();

builder.Services.AddNovelAgentDistributedCache(builder.Configuration);
builder.Services.AddSingleton<IDistributedCacheService, RedisCacheService>();
builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();

// Agent memory repository with three-tier caching
builder.Services.AddScoped<IAgentMemoryRepository, AgentMemoryRepository>();
builder.Services.AddScoped<IAgentMemoryVersionService, AgentMemoryVersionService>();
builder.Services.AddScoped<IAgentMemoryEventService, AgentMemoryEventService>();
builder.Services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
builder.Services.AddScoped<IAgentMemoryContextService, AgentMemoryContextService>();
builder.Services.AddScoped<ICollaborationMemoryService, CollaborationMemoryService>();
builder.Services.AddScoped<ChatHistoryCompressor>();

// Register Authentication Services
var dataProtectionKeysDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data",
    "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysDirectory);
builder.Services.AddDataProtection()
    .SetApplicationName("NovelAgentWeb")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));
builder.Services.AddSingleton<ILlmApiKeyProtector, DataProtectionLlmApiKeyProtector>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<ILlmConnectionHealthService, LlmConnectionHealthService>();

// Register Current User Service (for authorization and data isolation)
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IBackgroundUserContext, BackgroundUserContext>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Register Project Service
builder.Services.AddScoped<IProjectService, ProjectService>();

// Register Chapter Service
builder.Services.AddScoped<IChapterService, ChapterService>();

// Register Material Service
builder.Services.AddScoped<IMaterialService, MaterialService>();

// Register Content Document Service
builder.Services.AddScoped<IContentDocumentService, ContentDocumentService>();
builder.Services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
builder.Services.AddScoped<ICreativeIntentService, CreativeIntentService>();
builder.Services.AddScoped<IRevisionPlanService, RevisionPlanService>();
builder.Services.AddScoped<IRevisionPlanPackageInvalidationService, RevisionPlanPackageInvalidationService>();
builder.Services.AddScoped<ICommitmentAssessmentModelClient, DefaultCommitmentAssessmentModelClient>();
builder.Services.AddScoped<ICommitmentAssessmentService, CommitmentAssessmentService>();
builder.Services.AddScoped<TargetArchitectureDirector>();
builder.Services.AddScoped<IGoalBaselineProvider, GoalBaselineProvider>();
builder.Services.AddScoped<ICreativeGoalService, CreativeGoalService>();
builder.Services.AddSingleton<TaskGraphValidator>();
builder.Services.AddScoped<IGoalCompiler, GoalCompiler>();
builder.Services.AddScoped<IKernelTaskScheduler, PostgresKernelTaskScheduler>();
builder.Services.AddScoped<IGoalBudgetService, GoalBudgetService>();
builder.Services.AddScoped<IGoalControlService, GoalControlService>();
builder.Services.AddScoped<IGoalProgressEventPublisher, GoalProgressEventPublisher>();
builder.Services.AddScoped<IModelExecutionRecoveryService, ModelExecutionRecoveryService>();
builder.Services.AddSingleton<IModelExecutionOutcomeResolver, UnsupportedModelExecutionOutcomeResolver>();
builder.Services.AddScoped<DomainContractValidator>();
builder.Services.AddScoped<IKernelArtifactStore, KernelArtifactStore>();
builder.Services.AddScoped<IDomainReducer, DomainReducer>();
builder.Services.AddScoped<ICanonBranchService, CanonBranchService>();
builder.Services.AddScoped<IPrefixMergeService, PrefixMergeService>();
builder.Services.AddScoped<ICanonMergeConflictModelClient, DefaultCanonMergeConflictModelClient>();
builder.Services.AddScoped<CanonChangeExtractor>();
builder.Services.AddScoped<ILightweightCanonExtractionModelClient, DefaultLightweightCanonExtractionModelClient>();
builder.Services.AddScoped<ILightweightCanonSemanticReviewModelClient, DefaultLightweightCanonSemanticReviewModelClient>();
builder.Services.AddScoped<IContinuitySummaryExtractor, ContinuitySummaryExtractor>();
builder.Services.AddScoped<IReworkIntentModelClient, DefaultReworkIntentModelClient>();
builder.Services.AddScoped<IReworkIntentService, ReworkIntentService>();
builder.Services.AddSingleton<ReworkBudgetPolicy>();
builder.Services.AddScoped<IKernelStructuredModelClient, DefaultKernelStructuredModelClient>();
builder.Services.AddScoped<IKernelModelConfigurationService, KernelModelConfigurationService>();
builder.Services.AddSingleton<IKernelModelExecutionScopeAccessor, KernelModelExecutionScopeAccessor>();
builder.Services.AddScoped<IGoalModelExecutionEnvelope, GoalModelExecutionEnvelope>();
builder.Services.AddScoped<IGoalEmbeddingExecutionEnvelope, GoalEmbeddingExecutionEnvelope>();
builder.Services.AddSingleton<IKernelPromptAssembler, KernelPromptAssembler>();
builder.Services.AddScoped<IKernel, SettingKernel>();
builder.Services.AddScoped<IKernel, NarrativePlanningKernel>();
builder.Services.AddScoped<IChapterTargetResolver, ChapterTargetResolver>();
builder.Services.AddScoped<IKernel, KnowledgeRetrievalKernel>();
builder.Services.AddScoped<IKernel, TianmingWritingKernel>();
builder.Services.AddSingleton<IPotentialViolationDetector, PotentialViolationDetector>();
builder.Services.AddScoped<IContinuityReviewModelClient, DefaultContinuityReviewModelClient>();
builder.Services.AddScoped<ILiteraryReviewModelClient, DefaultLiteraryReviewModelClient>();
builder.Services.AddScoped<IKernel, ContinuityReviewKernel>();
builder.Services.AddScoped<IKernel, LiteraryReviewKernel>();
builder.Services.AddScoped<KernelRegistry>();
builder.Services.AddScoped<IKernelTaskExecutor, KernelTaskExecutionRouter>();

// Register Knowledge Service
builder.Services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
builder.Services.AddScoped<IKnowledgeStructureModelClient, DefaultKnowledgeStructureModelClient>();
builder.Services.AddScoped<IKnowledgeDocumentIngestionService, KnowledgeDocumentIngestionService>();
builder.Services.AddScoped<IKnowledgeCitationService, KnowledgeCitationService>();
builder.Services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
builder.Services.AddScoped<IKnowledgeClassificationModelClient, DefaultKnowledgeClassificationModelClient>();
builder.Services.AddScoped<IKnowledgeStoryBiblePromotionService, KnowledgeStoryBiblePromotionService>();
builder.Services.AddScoped<IKnowledgeClassificationService, KnowledgeClassificationService>();
builder.Services.AddScoped<IKnowledgeConflictModelClient, DefaultKnowledgeConflictModelClient>();
builder.Services.AddScoped<IKnowledgeCanonConflictStatusService, KnowledgeCanonConflictStatusService>();
builder.Services.AddScoped<IKnowledgeConflictDetector, KnowledgeConflictDetector>();
builder.Services.AddScoped<IKnowledgeConflictResolver, KnowledgeConflictResolver>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
builder.Services.AddScoped<IKnowledgeProcessingService, KnowledgeProcessingService>();
builder.Services.AddScoped<IKnowledgeProcessingTaskClaimer, PostgresKnowledgeProcessingTaskClaimer>();
builder.Services.AddScoped<KnowledgeProcessingTaskRunner>();
builder.Services.AddScoped<IDesignRuleAggregationService, DesignRuleAggregationService>();
builder.Services.AddScoped<IChapterBlueprintService, ChapterBlueprintService>();

// Register StoryBible Service
builder.Services.AddScoped<IStoryBibleService, TM.Web.NovelAgentWeb.Services.StoryBible.StoryBibleService>();

// Register Tianming production kernel from current repository services.
builder.Services.AddScoped<GuideManager>();
builder.Services.AddScoped<FactSnapshotExtractor>();
builder.Services.AddTianmingProductionKernelServices();

// Register Workflow Service
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
builder.Services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
builder.Services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
builder.Services.AddScoped<IChapterVersionRollbackService, ChapterVersionRollbackService>();
builder.Services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
builder.Services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
builder.Services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
builder.Services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
builder.Services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
builder.Services.AddScoped<IProductionWorkflowBridge, ProductionWorkflowBridge>();
builder.Services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
builder.Services.AddScoped<IBookValidationService, BookValidationService>();
builder.Services.AddScoped<IChapterProductionLeaseService, ChapterProductionLeaseService>();
builder.Services.AddScoped<IWritingModelCompletionService, DefaultWritingModelCompletionService>();
builder.Services.AddScoped<IAgentEditorialReviewModelClient, DefaultAgentEditorialReviewModelClient>();
builder.Services.AddScoped<IChapterContinuityFactPersister, StoryBibleChapterContinuityFactPersister>();
builder.Services.AddScoped<IChapterFactSnapshotUpdater, ChapterFactSnapshotUpdater>();
builder.Services.AddScoped<IChapterFactOutboxProcessor, ChapterFactOutboxProcessor>();
builder.Services.AddScoped<IChapterCommitPostCommitFinalizer, ChapterCommitPostCommitFinalizer>();
builder.Services.AddScoped<IProductionOutboxDispatcher, ProductionOutboxDispatcher>();
builder.Services.AddScoped<IProductionOutboxAdminService, ProductionOutboxAdminService>();
builder.Services.AddHostedService<ProductionOutboxHostedService>();

// Register Workspace Service
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Register Agent Session Service
builder.Services.AddScoped<IAgentSessionService, AgentSessionService>();
builder.Services.AddScoped<IAgentSessionResumeService, AgentSessionResumeService>();
builder.Services.AddScoped<ILegacyRuntimeAuditReader, LegacyRuntimeAuditReader>();
builder.Services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
builder.Services.AddSingleton<IAgentRuntimeEventFanout, RedisAgentRuntimeEventFanout>();
builder.Services.AddSingleton<IAgentRuntimeEventStreamConsumer, RedisAgentRuntimeEventStreamConsumer>();

// Register the required BGE embedding service; unsupported providers fail during startup.
builder.Services.AddNovelAgentEmbedding(builder.Configuration, builder.Environment);

// Register WorkspaceFactory with options
builder.Services.Configure<WorkspaceFactoryOptions>(
    builder.Configuration.GetSection("WorkspaceFactory"));
builder.Services.Configure<TM.Web.NovelAgentWeb.Services.Workspace.Models.WorkspaceAuditOptions>(
    builder.Configuration.GetSection("WorkspaceAudit"));
builder.Services.AddSingleton<IWorkspaceFactory, WorkspaceFactory>();
builder.Services.AddSingleton<IViolationAlertService, LoggingViolationAlertService>();
builder.Services.AddSingleton<IWorkspaceViolationTracker, WorkspaceViolationTracker>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Dev", policy =>
        policy.WithOrigins(
                "http://localhost:3002",
                "http://127.0.0.1:3002")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// NovelAgentWorkspace and NovelProjectCatalog are now provided dynamically via WorkspaceFactory
builder.Services.AddSingleton<ProjectScopedExecutor>();
builder.Services.AddSingleton<AgentSseEventBus>();
builder.Services.AddScoped<AgentSessionManager>();
builder.Services.AddSingleton<UserSettingsManager>(sp =>
{
    return new UserSettingsManager(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpContextAccessor>(),
        sp.GetRequiredService<IBackgroundUserContext>(),
        sp.GetRequiredService<ILlmApiKeyProtector>());
});
builder.Services.AddSingleton<HttpClient>(sp =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    return http;
});
builder.Services.AddSingleton<AgentMissionTaskTreeService>();
builder.Services.AddSingleton<MissionBlackboardRecoveryService>();
builder.Services.AddScoped<IAgentForegroundTurnRunner>(sp => sp.GetRequiredService<TargetArchitectureDirector>());
builder.Services.AddScoped<AgentTurnCoordinator>();
builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();

// Register QdrantClient for vectorization services
builder.Services.AddSingleton<QdrantClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var qdrantHost = config["Qdrant:Host"] ?? "localhost";
    var qdrantPort = config.GetValue<int>("Qdrant:Port", 6334);
    return new QdrantClient(host: qdrantHost, port: qdrantPort, https: false);
});

// Register vectorization services
builder.Services.AddScoped<IMaterialChunker, MaterialChunker>();
builder.Services.AddScoped<IQdrantCollectionManager, QdrantCollectionManager>();
builder.Services.AddScoped<IOutboxMaterialVectorIndexingService, OutboxMaterialVectorIndexingService>();
builder.Services.AddScoped<SemanticSearchService>();
builder.Services.AddScoped<IMultiScaleVectorIndexer, MultiScaleVectorIndexer>();
builder.Services.AddScoped<IAuthoritativeStoryVectorIndexer, AuthoritativeStoryVectorIndexer>();
builder.Services.AddScoped<ILegacyVectorSourceRebuilder, LegacyVectorSourceRebuilder>();
builder.Services.AddScoped<IVectorIndexRebuilder, VectorIndexRebuilder>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<KnowledgeProcessingWorker>();
builder.Services.AddScoped<IRagQueryPlanningModelClient, DefaultRagQueryPlanningModelClient>();
builder.Services.AddScoped<IQueryPlanner, QueryPlanner>();
builder.Services.AddScoped<IPostgresFullTextRetriever, PostgresFullTextRetriever>();
builder.Services.AddScoped<IEntityDependencyRetriever, EntityDependencyRetriever>();
builder.Services.AddSingleton<ReciprocalRankFusion>();
builder.Services.AddScoped<EvidenceBundleCompiler>();
builder.Services.AddScoped<IHybridRetriever, HybridRetriever>();
builder.Services.AddScoped<SqliteToPostgresImporter>();
builder.Services.AddScoped<MigrationVerifier>();
builder.Services.AddScoped<TargetArchitectureCutoverService>();

builder.Services.AddSingleton<QdrantHealthCheck>();
builder.Services.AddSingleton<IQdrantHealthProbe>(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddHostedService<RedisAgentRuntimeEventFanoutBridge>();
builder.Services.AddHostedService<KernelTaskWorker>();
builder.Services.AddHostedService<ModelExecutionRecoveryWorker>();
builder.Services.AddSingleton<IPostgresHealthProbe, PostgresHealthProbe>();
builder.Services.AddSingleton<RuntimeHealthService>();

var app = builder.Build();

// Resolve once during startup so a missing secret fails before the server accepts traffic.
_ = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);

var repositoryBoundaryGuard = app.Services.GetRequiredService<IRepositoryBoundaryGuard>();
var repositoryRoot = RepositoryBoundaryGuard.ResolveRepositoryRoot(app.Environment.ContentRootPath);
repositoryBoundaryGuard.EnsureClean(repositoryRoot);
var productionKernelAssemblyGuard = app.Services.GetRequiredService<IProductionKernelAssemblyGuard>();
productionKernelAssemblyGuard.EnsureCurrentRepositoryKernel(typeof(HardcoreWritingProductionKernel));

if (app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
    // The test host uses an isolated SQLite connection and materializes the current model.
    db.Database.EnsureCreated();
}
else
{
    var workerPreflight = app.Services.GetRequiredService<IBackgroundClaimDatabasePreflight>();
    await workerPreflight.VerifyRoleAsync();
    var migrationConnectionString = builder.Configuration.GetConnectionString("NovelAgentMigrationDb")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:NovelAgentMigrationDb 未配置，数据库迁移不得使用 HTTP 应用角色。");
    var migrationOptions = new DbContextOptionsBuilder<PostgresNovelAgentDbContext>()
        .UseNpgsql(migrationConnectionString)
        .Options;
    await using var migrationDb = new PostgresNovelAgentDbContext(migrationOptions);
    await migrationDb.Database.MigrateAsync();
    await workerPreflight.VerifyPermissionsAsync();
}

var targetMigrationMode = CommandLineValue(args, "--target-migration-mode");
if (!string.IsNullOrWhiteSpace(targetMigrationMode))
{
    var sourcePath = CommandLineValue(args, "--target-migration-source")
        ?? throw new InvalidOperationException("目标架构迁移必须提供 --target-migration-source=<sqlite-path>。");
    var requestedUserId = CommandLineValue(args, "--target-migration-user");
    var rebuildVectors = string.Equals(targetMigrationMode, "cutover", StringComparison.OrdinalIgnoreCase);
    if (targetMigrationMode is not ("rehearse" or "verify" or "cutover"))
        throw new InvalidOperationException("--target-migration-mode 只允许 rehearse、verify 或 cutover。");

    var userIds = string.IsNullOrWhiteSpace(requestedUserId)
        ? await SqliteToPostgresImporter.ListSourceUsersAsync(sourcePath)
        : [requestedUserId];
    var reports = new List<object>();
    foreach (var userId in userIds)
    {
        await using var migrationScope = app.Services.CreateAsyncScope();
        if (!string.Equals(targetMigrationMode, "verify", StringComparison.OrdinalIgnoreCase))
        {
            var importer = migrationScope.ServiceProvider.GetRequiredService<SqliteToPostgresImporter>();
            reports.Add(await importer.ImportUserAsync(sourcePath, userId));
        }

        if (string.Equals(targetMigrationMode, "cutover", StringComparison.OrdinalIgnoreCase))
        {
            var cutover = migrationScope.ServiceProvider.GetRequiredService<TargetArchitectureCutoverService>();
            reports.Add(await cutover.PreflightAndRebuildAsync(sourcePath, userId, rebuildVectors));
        }
        else
        {
            var verifier = migrationScope.ServiceProvider.GetRequiredService<MigrationVerifier>();
            reports.Add(await verifier.VerifyUserAsync(sourcePath, userId));
        }
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        reports,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Dev");
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>(); // Extract user context from JWT after authentication
app.UseMiddleware<WorkspaceUsageAuditMiddleware>();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", async (RuntimeHealthService health, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    var report = await health.CheckAsync(cancellationToken);
    var envelope = ApiEnvelope<object>.Ok(report, httpContext.TraceIdentifier);
    return report.Status == RuntimeHealthStatuses.Healthy
        ? Results.Ok(envelope)
        : Results.Json(envelope, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

static string? CommandLineValue(string[] commandLineArgs, string name)
{
    var prefix = name + "=";
    return commandLineArgs
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
        ?[prefix.Length..];
}

// Make Program class accessible to tests - must be public for WebApplicationFactory
namespace TM.Web.NovelAgentWeb
{
    public partial class Program { }
}
