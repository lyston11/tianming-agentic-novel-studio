using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Middleware;
using TM.Web.NovelAgentWeb.Scripts;
using TM.Web.NovelAgentWeb.Services;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Health;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Projects;
using TM.Web.NovelAgentWeb.Services.Repositories;
using TM.Web.NovelAgentWeb.Services.StoryBible;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using TM.Web.NovelAgentWeb.Services.Workflow;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddControllers()
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

// Agent memory repository with three-tier caching
builder.Services.AddScoped<IAgentMemoryRepository, AgentMemoryRepository>();
builder.Services.AddScoped<IAgentMemoryVersionService, AgentMemoryVersionService>();
builder.Services.AddScoped<IAgentMemoryEventService, AgentMemoryEventService>();
builder.Services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
builder.Services.AddScoped<IAgentMemoryContextService, AgentMemoryContextService>();
builder.Services.AddScoped<IToolSearchCacheService, ToolSearchCacheService>();
builder.Services.AddScoped<ChatHistoryCompressor>();

// Register Authentication Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<JwtTokenGenerator>();

// Register Current User Service (for authorization and data isolation)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Register Project Service
builder.Services.AddScoped<IProjectService, ProjectService>();

// Register Chapter Service
builder.Services.AddScoped<IChapterService, ChapterService>();

// Register Material Service
builder.Services.AddScoped<IMaterialService, MaterialService>();

// Register Content Document Service
builder.Services.AddScoped<IContentDocumentService, ContentDocumentService>();

// Register Knowledge Service
builder.Services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
builder.Services.AddScoped<IKnowledgeProcessingService, KnowledgeProcessingService>();

// Register StoryBible Service
builder.Services.AddScoped<IStoryBibleService, StoryBibleService>();

// Register Workflow Service
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

// Register Workspace Service
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Register Agent Session Service
builder.Services.AddScoped<IAgentSessionService, AgentSessionService>();

// Register Embedding Service. Stub mode is explicit and reported by /health until a real provider is added.
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
        policy.WithOrigins("http://localhost:3002")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// NovelAgentWorkspace and NovelProjectCatalog are now provided dynamically via WorkspaceFactory
builder.Services.AddSingleton<ProjectScopedExecutor>();
builder.Services.AddSingleton<IMaterialAnalysisService, StubMaterialAnalysisService>();
builder.Services.AddScoped<AgentSessionManager>();
builder.Services.AddSingleton<UserSettingsManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var storageRoot = config["NovelAgent:StorageRoot"] ?? Path.Combine(env.ContentRootPath, "App_Data");
    var projectName = config["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
    return new UserSettingsManager(
        storageRoot,
        projectName,
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpContextAccessor>());
});
builder.Services.AddSingleton<AgentToolRegistry>();
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
builder.Services.AddScoped<AgentRouter>();
builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
builder.Services.AddSingleton<QdrantSearchService>(); // Vector search service with user isolation

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
builder.Services.AddScoped<IMaterialVectorizationService, MaterialVectorizationService>();
builder.Services.AddScoped<SemanticSearchService>();

// TODO: AgentSchedulerHostedService needs refactoring for multi-user workspace isolation
// builder.Services.AddHostedService<AgentSchedulerHostedService>();
builder.Services.AddSingleton<QdrantHealthCheck>();
builder.Services.AddSingleton<IQdrantHealthProbe>(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<QdrantHealthCheck>());
builder.Services.AddSingleton<RuntimeHealthService>();

var app = builder.Build();

// Handle CLI commands
if (args.Contains("--migrate-memory"))
{
    await TM.Web.NovelAgentWeb.Scripts.MigrateMemoryToSqlite.RunAsync(app.Services);
    return;
}

if (args.Contains("--migrate-legacy-content", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
    var content = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MigrateLegacyContentToSqlite");
    var storageRoot = app.Configuration["NovelAgent:StorageRoot"] ?? "App_Data";
    await MigrateLegacyContentToSqlite.RunAsync(db, content, storageRoot, logger);
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
app.MapGet("/health", async (RuntimeHealthService health, CancellationToken cancellationToken) =>
    Results.Ok(await health.CheckAsync(cancellationToken))).AllowAnonymous();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

// Make Program class accessible to tests - must be public for WebApplicationFactory
namespace TM.Web.NovelAgentWeb
{
    public partial class Program { }
}
