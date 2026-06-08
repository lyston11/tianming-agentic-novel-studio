using System.Text.Json.Serialization;
using TM.Web.NovelAgentWeb.Middleware;
using TM.Web.NovelAgentWeb.Services;
using TM.Web.NovelAgentWeb.Support;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Dev", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddSingleton<NovelAgentWorkspace>();
builder.Services.AddSingleton<NovelProjectCatalog>();
builder.Services.AddSingleton<ProjectScopedExecutor>();
builder.Services.AddSingleton<IMaterialAnalysisService, StubMaterialAnalysisService>();
builder.Services.AddSingleton<AgentSessionManager>();
builder.Services.AddSingleton<UserSettingsManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var storageRoot = config["NovelAgent:StorageRoot"] ?? Path.Combine(env.ContentRootPath, "App_Data");
    var projectName = config["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
    return new UserSettingsManager(storageRoot, projectName);
});
builder.Services.AddSingleton<AgentToolRegistry>();
builder.Services.AddSingleton<AgentToolGuardrails>();
builder.Services.AddSingleton<HttpClient>(sp =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    return http;
});
builder.Services.AddSingleton<ILlmToolCallingClient, ProviderToolCallingClient>();
builder.Services.AddSingleton<AgentMemoryService>();
builder.Services.AddSingleton<AgentMissionTaskTreeService>();
builder.Services.AddSingleton<AgentTaskScheduler>();
builder.Services.AddSingleton<MissionBlackboardRecoveryService>();
builder.Services.AddSingleton<ConversationKernel>();
builder.Services.AddSingleton<AgentObservationBuilder>();
builder.Services.AddSingleton<AgentPlanner>();
builder.Services.AddSingleton<ToolPolicyEngine>();
builder.Services.AddSingleton<AgentQualityReviewSuite>();
builder.Services.AddSingleton<ReflectionEngine>();
builder.Services.AddSingleton<PhaseInference>();
builder.Services.AddSingleton<PhaseContextBuilder>();
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<AgentRouter>();
builder.Services.AddHostedService<AgentSchedulerHostedService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Dev");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
