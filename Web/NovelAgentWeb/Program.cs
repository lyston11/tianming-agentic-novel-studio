using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Middleware;
using TM.Web.NovelAgentWeb.Services;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Health;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.DesignRules;
using TM.Web.NovelAgentWeb.Services.ChapterBlueprints;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Projects;
using TM.Web.NovelAgentWeb.Services.Repositories;
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

// Add DbContext with SQLite
builder.Services.AddDbContext<NovelAgentDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("NovelAgentDb")));

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");
var issuer = jwtSettings["Issuer"] ?? "NovelAgentWeb";
var audience = jwtSettings["Audience"] ?? "NovelAgentWeb";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
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

    // Add detailed logging for JWT authentication
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"JWT Authentication Failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine($"JWT Token Validated for user: {context.Principal?.Identity?.Name}");
            return Task.CompletedTask;
        },
        OnMessageReceived = context =>
        {
            // EventSource cannot send custom headers, so accept token from query string for SSE endpoints
            if (context.Request.Path.StartsWithSegments("/api/agent/sse"))
            {
                var token = context.Request.Query["token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
            }

            var authToken = context.Token ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            if (!string.IsNullOrEmpty(authToken))
            {
                Console.WriteLine($"JWT Token Received: {authToken.Substring(0, Math.Min(50, authToken.Length))}...");
            }
            return Task.CompletedTask;
        }
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
builder.Services.AddScoped<IToolSearchCacheService, ToolSearchCacheService>();
builder.Services.AddScoped<ChatHistoryCompressor>();

// Register Authentication Services
builder.Services.AddDataProtection();
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

// Register Knowledge Service
builder.Services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
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
builder.Services.AddScoped<IProductionTruthChapterIdentityMigrationService, ProductionTruthChapterIdentityMigrationService>();
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
builder.Services.AddScoped<IWorkspaceStateQueryService, WorkspaceStateQueryService>();

// Register Agent Session Service
builder.Services.AddScoped<IAgentSessionService, AgentSessionService>();
builder.Services.AddScoped<IAgentSessionResumeService, AgentSessionResumeService>();
builder.Services.AddScoped<IAgentToolExecutionLedger, AgentToolExecutionLedger>();
builder.Services.AddScoped<IToolInputArtifactResolver, ToolInputArtifactResolver>();
builder.Services.AddScoped<IAgentRuntimeRunService, AgentRuntimeRunService>();
builder.Services.AddScoped<IRuntimeRunToolExecutionStore, RuntimeRunToolExecutionStore>();
builder.Services.AddScoped<IAgentInterruptService, AgentInterruptService>();
builder.Services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
builder.Services.AddSingleton<IAgentRuntimeEventFanout, RedisAgentRuntimeEventFanout>();
builder.Services.AddSingleton<IAgentRuntimeEventStreamConsumer, RedisAgentRuntimeEventStreamConsumer>();
builder.Services.AddScoped<IAgentRuntimeEventStreamPump, AgentRuntimeEventStreamPump>();
builder.Services.AddSingleton<IAgentRuntimeRunLeaseService, AgentRuntimeRunLeaseService>();
builder.Services.AddSingleton<IAgentRuntimeQueue, AgentRuntimeQueue>();
builder.Services.AddScoped<IAgentBackgroundRunReadinessGate, LlmBackgroundRunReadinessGate>();

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

// Register StoryBible Repository
builder.Services.AddScoped<IStoryBibleRepository, StoryBibleRepository>();

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
builder.Services.AddScoped<AgentToolRegistry>();
builder.Services.AddSingleton<AgentToolGuardrails>();
builder.Services.AddSingleton<HttpClient>(sp =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    return http;
});
builder.Services.AddSingleton<ILlmToolCallingClient, ProviderToolCallingClient>();
builder.Services.AddScoped<AgentMemoryService>();
builder.Services.AddSingleton<AgentMissionTaskTreeService>();
builder.Services.AddSingleton<AgentTaskScheduler>();
builder.Services.AddSingleton<MissionBlackboardRecoveryService>();
builder.Services.AddSingleton<ConversationKernel>();
builder.Services.AddScoped<AgentObservationBuilder>();
builder.Services.AddSingleton<AgentPlanner>();
builder.Services.AddSingleton<ToolPolicyEngine>();
builder.Services.AddSingleton<AgentQualityReviewSuite>();
builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddScoped<PhaseContextBuilder>();
builder.Services.AddScoped<AgentRuntime>();
builder.Services.AddScoped<IAgentForegroundTurnRunner>(sp => sp.GetRequiredService<AgentRuntime>());
builder.Services.AddScoped<IAgentInterruptDecisionService>(sp => sp.GetRequiredService<AgentRuntime>());
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

builder.Services.AddSingleton<QdrantHealthCheck>();
builder.Services.AddSingleton<IQdrantHealthProbe>(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddHostedService<AgentRuntimeWorker>();
builder.Services.AddHostedService<RedisAgentRuntimeEventFanoutBridge>();
builder.Services.AddHostedService<AgentRuntimeEventStreamPumpHostedService>();
builder.Services.AddSingleton<RuntimeHealthService>();

var app = builder.Build();

var repositoryBoundaryGuard = app.Services.GetRequiredService<IRepositoryBoundaryGuard>();
var repositoryRoot = RepositoryBoundaryGuard.ResolveRepositoryRoot(app.Environment.ContentRootPath);
repositoryBoundaryGuard.EnsureClean(repositoryRoot);
var productionKernelAssemblyGuard = app.Services.GetRequiredService<IProductionKernelAssemblyGuard>();
productionKernelAssemblyGuard.EnsureCurrentRepositoryKernel(typeof(HardcoreWritingProductionKernel));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
        SqliteSchemaNormalizer.Normalize(db);
    }

    var chapterIdentityMigration = scope.ServiceProvider.GetRequiredService<IProductionTruthChapterIdentityMigrationService>();
    await chapterIdentityMigration.NormalizeAsync();
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
    Results.Ok(ApiEnvelope<object>.Ok(await health.CheckAsync(cancellationToken), httpContext.TraceIdentifier))).AllowAnonymous();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

// Make Program class accessible to tests - must be public for WebApplicationFactory
namespace TM.Web.NovelAgentWeb
{
    public partial class Program { }
}
