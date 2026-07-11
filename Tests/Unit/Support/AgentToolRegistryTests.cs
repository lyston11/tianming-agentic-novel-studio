using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Support;
using Xunit;
using AgentRunEntity = TM.Web.NovelAgentWeb.Data.Entities.AgentRun;
using AgentToolExecutionEntity = TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution;
using ChapterEntity = TM.Web.NovelAgentWeb.Data.Entities.Chapter;
using ChapterVersion = TM.Web.NovelAgentWeb.Data.Entities.ChapterVersion;
using ContentChunk = TM.Web.NovelAgentWeb.Data.Entities.ContentChunk;
using ContentDocument = TM.Web.NovelAgentWeb.Data.Entities.ContentDocument;
using CreativeIntentEntity = TM.Web.NovelAgentWeb.Data.Entities.CreativeIntent;
using KnowledgeBaseEntity = TM.Web.NovelAgentWeb.Data.Entities.KnowledgeBase;
using KnowledgeClassificationEntity = TM.Web.NovelAgentWeb.Data.Entities.KnowledgeClassification;
using KnowledgeConflictReportEntity = TM.Web.NovelAgentWeb.Data.Entities.KnowledgeConflictReport;
using NovelProjectEntity = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;
using OutboxEventEntity = TM.Web.NovelAgentWeb.Data.Entities.OutboxEvent;
using ProductionEvent = TM.Web.NovelAgentWeb.Data.Entities.ProductionEvent;
using ProjectFactSnapshot = TM.Web.NovelAgentWeb.Data.Entities.ProjectFactSnapshot;
using ProjectKnowledgeUsageEntity = TM.Web.NovelAgentWeb.Data.Entities.ProjectKnowledgeUsage;
using RevisionPlanEntity = TM.Web.NovelAgentWeb.Data.Entities.RevisionPlan;
using TianmingPackage = TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage;
using UserEntity = TM.Web.NovelAgentWeb.Data.Entities.User;
using VolumeEntity = TM.Web.NovelAgentWeb.Data.Entities.Volume;

namespace Tests.Unit.Support;

public class AgentToolRegistryTests
{
    private static readonly InMemoryDatabaseRoot SettingsDatabaseRoot = new();

    private static ServiceProvider BuildToolServiceProvider(ServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IAgentToolExecutionLedger)))
            services.AddSingleton<IAgentToolExecutionLedger, RecordingAgentToolExecutionLedger>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(NovelAgentDbContext)))
            services.AddDbContext<NovelAgentDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IToolInputArtifactResolver)))
            services.AddScoped<IToolInputArtifactResolver, ToolInputArtifactResolver>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IAgentRuntimeEventService)))
            services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ILogger<OutboxChapterFactPostCommitScheduler>)))
            services.AddSingleton<ILogger<OutboxChapterFactPostCommitScheduler>>(
                NullLogger<OutboxChapterFactPostCommitScheduler>.Instance);

        return services.BuildServiceProvider();
    }

    private static async Task FinalizeChapterCommitMetadataOutboxAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var finalizer = new ChapterCommitPostCommitFinalizer(
            scope.ServiceProvider.GetRequiredService<IChapterCommitTruthRecorder>(),
            scope.ServiceProvider.GetRequiredService<ICreativeIntentService>(),
            scope.ServiceProvider.GetRequiredService<IProductionEventWriter>());

        var postCommitTypes = new[]
        {
            "finalize_chapter_commit_metadata",
            "extract_chapter_continuity_facts",
            "index_chapter_content"
        };

        for (var pass = 0; pass < 5; pass++)
        {
            var outboxes = await db.OutboxEvents
                .Where(e =>
                    postCommitTypes.Contains(e.EventType) &&
                    e.Status == "pending")
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            if (outboxes.Count == 0)
                break;

            foreach (var outbox in outboxes)
            {
                if (outbox.EventType == "finalize_chapter_commit_metadata" &&
                    outbox.AggregateType == "chapter")
                {
                    await finalizer.ProcessOutboxAsync(outbox);
                }

                outbox.Status = "completed";
                outbox.CompletedAt = DateTime.UtcNow;
                outbox.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
        }
    }

    private static bool JsonContainsText(string? json, string expected)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(expected))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonElementContainsText(document.RootElement, expected);
        }
        catch (JsonException)
        {
            return json.Contains(expected, StringComparison.Ordinal);
        }
    }

    private static bool JsonElementContainsText(JsonElement element, string expected)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Contains(expected, StringComparison.Ordinal) == true,
            JsonValueKind.Object => element.EnumerateObject().Any(property =>
                property.Name.Contains(expected, StringComparison.Ordinal) ||
                JsonElementContainsText(property.Value, expected)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => JsonElementContainsText(item, expected)),
            _ => element.ToString().Contains(expected, StringComparison.Ordinal)
        };
    }

    private static UserSettingsManager CreateDbBackedSettingsManager(
        string userId = "user-1",
        UserSettings? settings = null)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddSingleton<ILlmApiKeyProtector>(
            new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider()));
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, SettingsDatabaseRoot));
        var provider = services.BuildServiceProvider();
        var manager = new UserSettingsManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new HttpContextAccessor(),
            new FixedBackgroundUserContext(userId),
            provider.GetRequiredService<ILlmApiKeyProtector>());

        manager.SaveAsync(settings ?? new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://api.openai.com/v1",
            LlmModel = "gpt-4o",
            LlmApiKey = "test-key"
        }).GetAwaiter().GetResult();
        return manager;
    }

    private static AgentObservationContext ToolContext(params AgentToolDefinition[] tools) => new()
    {
        AvailableTools = tools.ToList()
    };

    [Fact]
    public void BuildNextPossibleTools_DoesNotSkipChapterCandidateSelectionAfterPlanChapter()
    {
        var method = typeof(AgentToolRegistry).GetMethod("BuildNextPossibleTools", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var next = Assert.IsType<List<string>>(method!.Invoke(null, new object[] { "PlanChapter", "planning" }));

        Assert.Contains("SelectChapterCandidate", next);
        Assert.DoesNotContain("ProduceChapter", next);
    }

    private static NovelAgentWorkspace WithTestProductionKernel(
        NovelAgentWorkspace workspace,
        ITianmingProductionKernel productionKernel)
    {
        var field = typeof(TM.Services.Framework.AI.NovelAgent.Services.NovelAgentOrchestrator)
            .GetField("_productionKernel", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(workspace.Orchestrator, productionKernel);
        return workspace;
    }

    private static NovelProjectCatalog CreateDbBackedCatalog(NovelAgentWorkspace workspace)
    {
        return new NovelProjectCatalog(workspace, workspace.ScopeFactory);
    }

    private static IServiceScopeFactory CreateWorkspaceScopeFactory(
        string userId = "user-1",
        string projectId = "project-1")
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        services.AddSingleton(connection);
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddMemoryCache();
        services.AddSingleton<IMemoryCacheService>(sp =>
            new MemoryCacheService(
                sp.GetRequiredService<IMemoryCache>(),
                NullLogger<MemoryCacheService>.Instance));
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new UserEntity
        {
            Id = userId,
            Username = userId,
            Email = $"{userId}@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        if (!string.IsNullOrWhiteSpace(projectId) &&
            !projectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
        {
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = projectId,
                UserId = userId,
                Title = projectId,
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        db.SaveChanges();

        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void ListToolSchemasRankedByPhaseHint_ExposesProjectResolutionTool()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var conversationTools = registry.ListToolSchemasRankedByPhaseHint(ConversationPhase.Conversation);
        var planningTools = registry.ListToolSchemasRankedByPhaseHint(ConversationPhase.Planning);

        Assert.Contains(conversationTools, tool => tool.Name == "ResolveNovelProject");
        Assert.Contains(planningTools, tool => tool.Name == "ResolveNovelProject");
    }

    [Fact]
    public void ResolveNovelProjectSchema_ExposesModelDecidedFoundationReadiness()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var tool = registry.ListToolSchemas().Single(schema => schema.Name == "ResolveNovelProject");

        Assert.Contains("foundationBriefReady", tool.Parameters.Keys);
        Assert.Contains("模型显式判断", tool.Description);
    }

    [Fact]
    public async Task ResolveNovelProject_UsesExplicitFoundationReadinessInsteadOfKeywordHeuristics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-resolve-ready-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        const string richSeed = "末世废土爽文，主角是底层幸存者，拥有系统和吞噬晶核升级能力，核心爽点是打怪升级、建基地、收伙伴，明确不要恋爱拉扯。";

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var sessionWithoutFlag = new AgentSession { UserId = "user-1", SessionId = "session-1" };
            var withoutFlag = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ResolveNovelProject",
                    Arguments = new Dictionary<string, string>
                    {
                        ["mode"] = "create_new",
                        ["title"] = "显式地基测试",
                        ["genre"] = "末世废土",
                        ["seed"] = richSeed
                    }
                },
                sessionWithoutFlag,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(withoutFlag.Success);
            Assert.Equal("awaiting_user_foundation", sessionWithoutFlag.Phase);
            Assert.Equal("needs_author_input", sessionWithoutFlag.WorkingMemory.Mission.Readiness);

            var sessionWithFlag = new AgentSession { UserId = "user-1", SessionId = "session-2" };
            var withFlag = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ResolveNovelProject",
                    Arguments = new Dictionary<string, string>
                    {
                        ["mode"] = "create_new",
                        ["title"] = "显式地基测试",
                        ["genre"] = "末世废土",
                        ["seed"] = richSeed,
                        ["foundationBriefReady"] = "true"
                    }
                },
                sessionWithFlag,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(withFlag.Success);
            Assert.Equal("foundation_ready", sessionWithFlag.Phase);
            Assert.Equal("ready", sessionWithFlag.WorkingMemory.Mission.Readiness);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanStoryFoundation_UsesExplicitGenreInsteadOfInferringFromSeed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-foundation-genre-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var missingDirections = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanStoryFoundation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["userSeed"] = "我想写一本科幻废土小说，但模型这轮还没有显式决定题材参数。"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.False(missingDirections.Success);
            Assert.Contains("candidateDirections", missingDirections.Message);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanStoryFoundation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["userSeed"] = "我想写一本科幻废土小说，但模型这轮还没有显式决定题材参数。",
                        ["candidateDirections"] = "废土邮路升级流,规则裂缝成长流,资源争夺推进流"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var run = Assert.IsType<NovelAgentRun>(result.Data);
            Assert.Equal("玄幻", run.StoryConstitution?.Genre);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanStoryFoundation_AcceptsStructuredCandidateDirectionObjectsWithoutJsonLeakingIntoTitles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-foundation-json-directions-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanStoryFoundation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["userSeed"] = "写一本废土邮路升级流，主角沈砚通过银蓝邮徽打通旧邮路。",
                        ["candidateDirections"] = """
                        [
                          {"id":"dir_upgrade_route","title":"废土邮路升级流","brief":"主线围绕打怪升级、资源争夺和邮路扩张推进"},
                          {"id":"dir_resource_war","direction":"资源点争夺成长流"},
                          {"id":"dir_system_limit","name":"邮徽能力边界成长流"}
                        ]
                        """
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var run = Assert.IsType<NovelAgentRun>(result.Data);
            Assert.NotEmpty(run.MacroCandidates);
            var visibleText = string.Join("\n", run.MacroCandidates.Select(c => $"{c.Title}\n{c.CoreHook}"));
            Assert.Contains("废土邮路升级流", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[{", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"id\"", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dir_upgrade_route", visibleText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanStoryFoundation_KeepsNaturalDirectionBlocksTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-foundation-block-directions-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanStoryFoundation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["userSeed"] = "沈砚获得银蓝邮徽，在废土中打通被篡改的旧邮路。",
                        ["candidateDirections"] = """
                        【方向A：废土邮路升级流】以沈砚逐步打通被篡改旧邮路为主线升级轴。每次成功辨认并修复一段邮路，获得资源补给和新线索。

                        【方向B：资源点争夺成长流】每个邮路节点都是稀缺资源点，邮路打通意味着资源流通权。

                        【方向C：邮徽能力边界成长流】银蓝邮徽只能辨认篡改，沈砚必须在能力边界内解决问题。
                        """
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var run = Assert.IsType<NovelAgentRun>(result.Data);
            Assert.Equal(3, run.MacroCandidates.Count);
            Assert.Contains(run.MacroCandidates, c => c.Title.Contains("废土邮路升级", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(run.MacroCandidates, c => c.Title.Contains("资源点争夺", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(run.MacroCandidates, c => c.Title.Contains("邮徽能力边界", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(run.MacroCandidates, c => c.Title.Contains("获得资源补给", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlanVolumeArcSchema_ExposesChapterCountAndRangeParameters()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var tool = registry.ListToolSchemas().Single(schema => schema.Name == "PlanVolumeArc");

        Assert.Contains("expectedChapterCount", tool.Parameters.Keys);
        Assert.Contains("startChapterId", tool.Parameters.Keys);
        Assert.Contains("endChapterId", tool.Parameters.Keys);
        Assert.Contains("每卷", tool.Description);
    }

    [Fact]
    public void CommitVolumeArcSchema_ExposesOverwriteParameter()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var tool = registry.ListToolSchemas().Single(schema => schema.Name == "CommitVolumeArc");

        Assert.Contains("overwrite", tool.Parameters.Keys);
        Assert.Contains("覆盖", tool.Description);
    }

    [Fact]
    public void ProduceChapterSchema_ExposesClosedLoopProductionTool()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var tool = registry.ListToolSchemas().Single(schema => schema.Name == "ProduceChapter");

        Assert.Equal("High", tool.Risk);
        Assert.True(tool.RequiresConfirmation);
        Assert.Contains("runId", tool.Parameters.Keys);
        Assert.Contains("revisionPlanId", tool.Parameters.Keys);
        Assert.Contains("maxRepairAttempts", tool.Parameters.Keys);
        Assert.Contains("commitPolicy", tool.Parameters.Keys);
        Assert.Contains("auto_commit", tool.Description);
        Assert.Contains("draft_only", tool.Description);
        Assert.Contains("require_user_review", tool.Description);
        Assert.Contains("chapter_versions", tool.SideEffects.WritesSqliteEntities);
        Assert.Contains("project_fact_snapshots", tool.SideEffects.WritesSqliteEntities);
        Assert.Contains("章节生产闭环", tool.Description);
        Assert.Contains("提交书城", tool.Description);
    }

    [Fact]
    public void ProcessKnowledgeFileSchema_AllowsAutoExecutionForUploadedTasks()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var tool = registry.ListToolSchemas().Single(schema => schema.Name == "ProcessKnowledgeFile");

        Assert.Equal("Medium", tool.Risk);
        Assert.False(tool.RequiresConfirmation);
        Assert.Contains("taskId", tool.Parameters.Keys);
        Assert.Contains("已上传", tool.Description);
        Assert.Contains("不覆盖正文", tool.Description);
        Assert.Contains("不删除数据", tool.Description);
    }

    [Fact]
    public void PublicToolCatalog_HidesLowLevelChapterProductionStagesBehindProduceChapter()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var publicToolNames = registry.ListToolSchemas()
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ProduceChapter", publicToolNames);
        Assert.DoesNotContain("BuildChapterContextPackage", publicToolNames);
        Assert.DoesNotContain("GenerateChapterWithChanges", publicToolNames);
        Assert.DoesNotContain("ValidateChapterDraft", publicToolNames);
        Assert.DoesNotContain("RepairChapterDraft", publicToolNames);
        Assert.DoesNotContain("CommitValidatedChapter", publicToolNames);
    }

    [Theory]
    [InlineData("BuildChapterContextPackage")]
    [InlineData("GenerateChapterWithChanges")]
    [InlineData("ValidateChapterDraft")]
    [InlineData("RepairChapterDraft")]
    [InlineData("CommitValidatedChapter")]
    public async Task LowLevelChapterProductionStages_AreNotRegisteredExecutableTools(string stageName)
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        Assert.Null(registry.Find(stageName));

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = stageName,
                Arguments = new Dictionary<string, string> { ["runId"] = "run-001" }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("未知工具", result.Message);
    }

    [Fact]
    public void ToolSemanticCards_ExposeSideEffectImpactAndFailureContract()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);

        Assert.All(schemas.Values, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.SideEffectLevel));
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.ImpactScope));
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.FailureContract));
        });

        Assert.Equal("production_final_write", schemas["ProduceChapter"].Semantic.SideEffectLevel);
        Assert.Contains("chapter", schemas["ProduceChapter"].Semantic.ImpactScope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failed_stage", schemas["ProduceChapter"].Semantic.FailureContract, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("read_only", schemas["QueryProjectContent"].Semantic.SideEffectLevel);
        Assert.Contains("current_project", schemas["QueryProjectContent"].Semantic.ImpactScope, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolSemanticCards_ExposeModelDecisionMapFields()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);

        Assert.All(schemas.Values, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.DisplayName), $"{tool.Name} must expose a display name.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.AverageDuration), $"{tool.Name} must expose average duration.");
            Assert.NotEmpty(tool.Semantic.ProgressEventContract);
            Assert.NotEmpty(tool.Semantic.NextPossibleTools);
        });

        var produce = schemas["ProduceChapter"].Semantic;
        Assert.True(produce.RequiresProject);
        Assert.False(produce.SupportsNoProjectSession);
        Assert.Contains("long", produce.AverageDuration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(produce.ProgressEventContract, item => item.Contains("chapter_context_package", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(produce.ProgressEventContract, item => item.Contains("kernel_gate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(produce.NextPossibleTools, tool => string.Equals(tool, "QueryProjectContent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(produce.NextPossibleTools, tool => string.Equals(tool, "QueryNovelProductionState", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(produce.NextPossibleTools, tool => string.Equals(tool, "ReviseCommittedChapter", StringComparison.OrdinalIgnoreCase));

        var workspace = schemas["QueryWorkspaceState"].Semantic;
        Assert.False(workspace.RequiresProject);
        Assert.True(workspace.SupportsNoProjectSession);
        Assert.Contains("short", workspace.AverageDuration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("知识", workspace.ResultSemantics, StringComparison.OrdinalIgnoreCase);

        var content = schemas["QueryProjectContent"].Semantic;
        Assert.True(content.RequiresProject);
        Assert.False(content.SupportsNoProjectSession);
        Assert.Contains(content.NextPossibleTools, tool => string.Equals(tool, "ProduceChapter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("知识", content.ResultSemantics, StringComparison.OrdinalIgnoreCase);

        var knowledgeBindings = schemas["QueryProjectKnowledgeBindings"];
        Assert.Contains("Story Bible Canon", knowledgeBindings.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("story_bible", knowledgeBindings.Semantic.ReadsFrom);
        Assert.Contains("canon_ledger", knowledgeBindings.Semantic.ReadsFrom);
        Assert.Contains("knowledge_conflict_reports", knowledgeBindings.Semantic.ReadsFrom);
        Assert.Contains("none_read_only", knowledgeBindings.Semantic.WritesTo);

        var attachKnowledge = schemas["AttachKnowledgeToProject"];
        Assert.Contains("绑定", attachKnowledge.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("knowledge_base", attachKnowledge.Semantic.ReadsFrom);
        Assert.Contains("project_knowledge_usages", attachKnowledge.Semantic.WritesTo);
        Assert.Contains("project_knowledge_binding", attachKnowledge.Semantic.OutputArtifacts);
        Assert.Contains("QueryProjectKnowledgeBindings", attachKnowledge.Semantic.NextPossibleTools);

        var bookValidation = schemas["RunBookValidation"];
        Assert.Contains("整书", bookValidation.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chapters", bookValidation.Semantic.ReadsFrom);
        Assert.Contains("project_fact_snapshots", bookValidation.Semantic.ReadsFrom);
        Assert.Contains("book_validation_report", bookValidation.Semantic.OutputArtifacts);
        Assert.Contains("CreateRevisionPlan", bookValidation.Semantic.NextPossibleTools);
    }

    [Fact]
    public void ToolSemanticCards_MarkReadOnlyQueryToolsWithoutBusinessWrites()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var readOnlyPrefixes = new[] { "Query", "Compare" };
        var readOnlyToolNames = registry.ListToolSchemas()
            .Where(tool =>
                readOnlyPrefixes.Any(prefix => tool.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(tool.Name, "AuditCommittedChapter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "RunBookValidation", StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Contains("QueryProductionOutbox", readOnlyToolNames);
        Assert.Contains("QueryNovelProductionState", readOnlyToolNames);
        Assert.Contains("CompareChapterVersions", readOnlyToolNames);
        Assert.Contains("AuditCommittedChapter", readOnlyToolNames);
        Assert.Contains("RunBookValidation", readOnlyToolNames);

        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var toolName in readOnlyToolNames)
        {
            var schema = schemas[toolName];
            Assert.True(schema.SideEffects.BusinessReadOnly, $"{toolName} must mark BusinessReadOnly=true.");
            Assert.Equal(new[] { "none_read_only" }, schema.Semantic.WritesTo);
        }
    }

    [Fact]
    public void ToolSemanticCards_MarkWriteToolsWithExplicitReadAndWriteSurfaces()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var writeToolNames = new[]
        {
            "ResolveNovelProject",
            "ProcessKnowledgeFile",
            "AttachKnowledgeToProject",
            "ClassifyProjectKnowledge",
            "DetectKnowledgeConflicts",
            "ResolveKnowledgeConflict",
            "CreateCreativeIntent",
            "DecideCreativeIntent",
            "CreateRevisionPlan",
            "InvalidateAffectedPackages",
            "ReviseCommittedChapter",
            "RollbackChapterVersion",
            "PlanStoryFoundation",
            "CommitStoryFoundation",
            "PlanVolumeArc",
            "CommitVolumeArc",
            "PlanChapter",
            "SelectChapterCandidate",
            "ProduceChapter",
            "RefreshProjectIndexes",
            "AnalyzeDependencyImpact",
            "ReviewChapter",
            "RetryProductionOutbox"
        };
        var operationalWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "agent_tool_execution_ledger",
            "recent_runtime_cache",
            "tool_search_cache",
            "agent_tool_search_snapshots",
            "none_read_only"
        };
        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in writeToolNames)
        {
            var schema = schemas[toolName];
            Assert.False(schema.SideEffects.BusinessReadOnly, $"{toolName} is a mutation/process tool and must not be marked read-only.");
            Assert.NotEmpty(schema.SideEffects.ReadsSqliteEntities);
            Assert.NotEmpty(schema.SideEffects.WritesSqliteEntities);
            Assert.Contains(schema.Semantic.WritesTo, target => !operationalWrites.Contains(target));
        }
    }

    [Fact]
    public void ToolSemanticCards_DoNotReportSearchKnowledgeReadsAsBusinessWrites()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var schema = registry.ListToolSchemas()
            .Single(tool => string.Equals(tool.Name, "SearchCreativeKnowledge", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("project_knowledge_usages", schema.SideEffects.ReadsSqliteEntities);
        Assert.DoesNotContain("project_knowledge_usages", schema.SideEffects.WritesSqliteEntities);
        Assert.DoesNotContain("project_knowledge_usages", schema.Semantic.WritesTo);
        Assert.Contains("project_knowledge_usages", schema.Semantic.ReadsFrom);
    }

    [Fact]
    public void ToolSemanticCards_ExposeArtifactContractsAndRecoveryPolicies()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        Assert.All(schemas.Values, tool =>
        {
            Assert.NotEmpty(tool.Semantic.InputArtifacts);
            Assert.NotEmpty(tool.Semantic.OutputArtifacts);
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.IdempotencyPolicy), $"{tool.Name} must expose idempotency policy.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Semantic.RollbackPolicy), $"{tool.Name} must expose rollback policy.");
        });

        var produce = schemas["ProduceChapter"].Semantic;
        Assert.Contains("chapter_plan_run", produce.InputArtifacts);
        Assert.Contains("continuity_pack", produce.InputArtifacts);
        Assert.Contains("knowledge_binding_snapshot", produce.InputArtifacts);
        Assert.Contains("chapter_commit", produce.OutputArtifacts);
        Assert.Contains("chapter_version", produce.OutputArtifacts);
        Assert.Contains("commitPolicy", produce.IdempotencyPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChapterVersion", produce.RollbackPolicy, StringComparison.OrdinalIgnoreCase);

        var queryContent = schemas["QueryProjectContent"].Semantic;
        Assert.Contains("project_content_query", queryContent.InputArtifacts);
        Assert.Contains("project_content_snapshot", queryContent.OutputArtifacts);
        Assert.Contains("read-only", queryContent.RollbackPolicy, StringComparison.OrdinalIgnoreCase);

        var queryKnowledgeBindings = schemas["QueryProjectKnowledgeBindings"].Semantic;
        Assert.Contains("project_knowledge_binding_status_summary", queryKnowledgeBindings.OutputArtifacts);
        Assert.Contains("project_knowledge_binding_snapshot", queryKnowledgeBindings.OutputArtifacts);
        Assert.Contains("story_bible_canon_ledger_snapshot", queryKnowledgeBindings.OutputArtifacts);
        Assert.Contains("knowledge_conflict_snapshot", queryKnowledgeBindings.OutputArtifacts);
        Assert.Contains("生产包", queryKnowledgeBindings.ResultSemantics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("门禁", queryKnowledgeBindings.ResultSemantics, StringComparison.OrdinalIgnoreCase);

        var creative = schemas["CreateCreativeIntent"].Semantic;
        Assert.Contains("creative_raw_intent", creative.InputArtifacts);
        Assert.Contains("creative_intent", creative.OutputArtifacts);
        Assert.Contains("intentId", creative.IdempotencyPolicy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreativeIntentTools_AreDiscoverableThroughUnifiedToolSearchRegistry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var toolNames = registry.ListToolSchemas().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("CreateCreativeIntent", toolNames);
        Assert.Contains("DecideCreativeIntent", toolNames);
        Assert.Contains("QueryCreativeIntents", toolNames);
        var schemas = registry.ListToolSchemas().ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        Assert.Contains("创意收件箱", schemas["CreateCreativeIntent"].Semantic.DomainSurface);
        Assert.Contains("creative_intents", schemas["CreateCreativeIntent"].Semantic.WritesTo);
        Assert.Contains("creative_intents", schemas["QueryCreativeIntents"].Semantic.ReadsFrom);
        Assert.Contains("none_read_only", schemas["QueryCreativeIntents"].Semantic.WritesTo);

        var searchResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "创意 收件箱 采纳 章节重写",
                    ["includeAll"] = "true"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(searchResult.Success);
        var serialized = JsonSerializer.Serialize(searchResult.Data);
        Assert.Contains("CreateCreativeIntent", serialized);
        Assert.Contains("DecideCreativeIntent", serialized);
        Assert.Contains("QueryCreativeIntents", serialized);
    }

    [Fact]
    public async Task PlanVolumeArc_UsesExpectedChapterCountArgument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-volume-count-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var missingDirections = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanVolumeArc",
                    Arguments = new Dictionary<string, string>
                    {
                        ["creativeBrief"] = "规划第一卷，每卷10章，主角在邮路废墟中建立第一条安全邮线。",
                        ["expectedChapterCount"] = "10",
                        ["volumeTitle"] = "第一卷：废墟邮线"
                    }
                },
                session,
                new StoryBibleDocument
                {
                    Constitution = new StoryCreativeConstitution
                    {
                        Genre = "废土邮差冒险",
                        MainPleasure = "邮路探索、谜团递进和硬事实连续性"
                    }
                },
                confirmed: true,
                CancellationToken.None);

            Assert.False(missingDirections.Success);
            Assert.Contains("candidateDirections", missingDirections.Message);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "PlanVolumeArc",
                    Arguments = new Dictionary<string, string>
                    {
                        ["creativeBrief"] = "规划第一卷，每卷10章，主角在邮路废墟中建立第一条安全邮线。",
                        ["expectedChapterCount"] = "10",
                        ["candidateDirections"] = "废墟邮线建立,投递秩序试运行,旧邮路危机升级"
                    }
                },
                session,
                new StoryBibleDocument
                {
                    Constitution = new StoryCreativeConstitution
                    {
                        Genre = "废土邮差冒险",
                        MainPleasure = "邮路探索、谜团递进和硬事实连续性"
                    }
                },
                confirmed: true,
                CancellationToken.None);

            var run = Assert.IsType<NovelAgentRun>(result.Data);
            Assert.True(result.Success, result.Message);
            Assert.NotNull(run.VolumeArcPlan);
            Assert.Equal("第一卷：废墟邮线建立", run.VolumeArcPlan!.Title);
            Assert.Equal(10, run.VolumeArcPlan!.ExpectedChapterCount);
            Assert.Equal(10, run.VolumeArcPlan.ChapterBeats.Count);
            Assert.Equal("chapter-010", run.VolumeArcPlan.EndChapterId);
            Assert.Equal("volume_arc_candidates", result.Artifact?.ArtifactType);
            Assert.Contains("章节节拍：10 个", result.Message);

            session.ActiveRunId = "runtime-run-not-a-story-bible-run";
            var commit = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "CommitVolumeArc",
                    Arguments = new Dictionary<string, string>()
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(commit.Success, commit.Message);
            Assert.Equal(run.RunId, commit.RunId);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CommitVolumeArc_UsesOverwriteArgumentToReplaceExistingPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-volume-overwrite-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var oldCommit = await workspace.StoryBibleService.CommitVolumeArcAsync(
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "第一卷：旧六章规划",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-006",
                    ExpectedChapterCount = 6,
                    ChapterBeats = Enumerable.Range(1, 6)
                        .Select(index => new VolumeChapterBeat { Index = index, Goal = $"旧节拍 {index}" })
                        .ToList()
                },
                sourceRunId: "seed-old-volume",
                overwrite: false,
                confirmed: true,
                ct: CancellationToken.None);
            Assert.True(oldCommit.Success);

            var newRun = await workspace.Orchestrator.PlanVolumeArcAsync(new VolumeArcPlanningRequest
            {
                UserGoal = "把第一卷重新规划为10章，覆盖旧的6章规划。",
                VolumeId = "volume-001",
                VolumeTitle = "第一卷：新版十章规划",
                StartChapterId = "chapter-001",
                EndChapterId = "chapter-010",
                ExpectedChapterCount = 10,
                CandidateDirections = { "新版十章卷节拍", "旧邮路危机升级", "卷末状态跃迁" }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "CommitVolumeArc",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = newRun.RunId,
                        ["overwrite"] = "true"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var bible = await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None);
            var volume = Assert.Single(bible.VolumeArcs);
            Assert.Equal("volume-001", volume.VolumeId);
            Assert.Equal(10, volume.ExpectedChapterCount);
            Assert.Equal("chapter-010", volume.EndChapterId);
            Assert.Equal(10, volume.ChapterBeats.Count);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchCreativeKnowledge_PreservesDatabaseHardFactCategory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-hardfact-search-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeService>(new FixedKnowledgeService(new KnowledgeSearchResult
        {
            Id = "hardfact-1",
            EntryType = "HardFact",
            Title = "主角与道具硬事实",
            Content = "主角必须叫沈砚；银蓝邮徽固定在右手腕，只能辨认被篡改邮路，不能攻击。",
            Score = 9
        }));
        var provider = BuildToolServiceProvider(services);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "SearchCreativeKnowledge",
                    Arguments = new Dictionary<string, string>
                    {
                        ["query"] = "主角 沈砚 银蓝邮徽"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, JsonSerializer.Serialize(new
            {
                result.Message,
                result.Phase,
                result.RecommendedToolName,
                result.Failure
            }));
            Assert.Contains("【HardFact】", result.Message);
            var data = Assert.IsType<CreativeKnowledgeRetrievalResult>(result.Data);
            Assert.Contains(data.Hits, hit => hit.Entry.Category.ToString() == "HardFact");
            Assert.Contains(data.HardFacts, fact => fact.Contains("沈砚") && fact.Contains("银蓝邮徽"));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchCreativeKnowledge_RequiresDatabaseKnowledgeServiceWhenProjectIsActive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-knowledge-required-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                registry.ExecuteAsync(
                    new AgentToolCall
                    {
                        Name = "SearchCreativeKnowledge",
                        Arguments = new Dictionary<string, string>
                        {
                            ["query"] = "主角 沈砚 银蓝邮徽"
                        }
                    },
                    new AgentSession
                    {
                        UserId = "user-1",
                        SessionId = "session-1",
                        ActiveProjectId = "project-1"
                    },
                    new StoryBibleDocument(),
                    confirmed: true,
                    CancellationToken.None));

            Assert.Contains(nameof(IKnowledgeService), ex.Message);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildChapterContextPackage_IncludesDatabaseHardFacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-hardfact-context-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter>(sp => new ProductionEventWriter(
            sp.GetRequiredService<IProductionTruthStore>(),
            sp.GetRequiredService<NovelAgentDbContext>()));
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new FixedKnowledgeService(new KnowledgeSearchResult
        {
            Id = "hardfact-context-1",
            EntryType = "HardFact",
            Title = "主角与邮徽硬事实",
            Content = "主角必须叫沈砚；银蓝邮徽固定在右手腕，只能辨认被篡改邮路，不能攻击。",
            Score = 9
        }));
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写沈砚带着银蓝邮徽进入废墟邮路的第一章。",
                CandidateDirections = { "邮路废墟探索与硬事实承接" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            if (!result.Success)
            {
                throw new InvalidOperationException(JsonSerializer.Serialize(new
                {
                    result.Message,
                    result.Phase,
                    result.RecommendedToolName,
                    result.Failure
                }));
            }
            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            Assert.NotNull(execution.ContextPackage);
            Assert.Contains(execution.ContextPackage!.HardContinuityFacts, fact => fact.Contains("沈砚") && fact.Contains("银蓝邮徽"));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildChapterContextPackage_IncludesRuntimeSoftRequirements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-soft-requirement-context-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<IAgentInterruptService, AgentInterruptService>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        var provider = BuildToolServiceProvider(services);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写沈砚进入废墟邮路的第一章。",
                CandidateDirections = { "废墟邮路探索" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索"
                }
            }, CancellationToken.None);

            var session = new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = run.RunId,
                RuntimeRunId = "runtime-run-1"
            };

            string softInterruptId;
            await using (var interruptScope = provider.CreateAsyncScope())
            {
                var interrupts = interruptScope.ServiceProvider.GetRequiredService<IAgentInterruptService>();
                var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
                    RuntimeRunId: "runtime-run-1",
                    UserId: "user-1",
                    SessionId: "session-1",
                    ProjectId: "project-1",
                    Kind: "soft_requirement",
                    Message: "这一章先不要发展恋爱关系，重点写打怪升级反馈。",
                    Priority: 80));
                softInterruptId = interrupt.Id;
            }

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            if (!result.Success)
            {
                throw new InvalidOperationException(JsonSerializer.Serialize(new
                {
                    result.Message,
                    result.Phase,
                    result.RecommendedToolName,
                    result.Failure
                }));
            }

            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            Assert.NotNull(execution.ContextPackage);
            Assert.Contains(execution.ContextPackage!.HardContinuityFacts,
                fact => fact.Contains("执行中补充要求", StringComparison.Ordinal) &&
                        fact.Contains("打怪升级反馈", StringComparison.Ordinal));
            Assert.Contains(execution.ContextPackage.Warnings,
                warning => warning.Contains(softInterruptId, StringComparison.Ordinal));
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var consumedInterrupt = await db.AgentInterrupts.SingleAsync(item => item.RuntimeRunId == "runtime-run-1");
            Assert.Equal(AgentInterruptStatus.Consumed, consumedInterrupt.Status);
            Assert.Contains("agent_tool_stage_boundary", consumedInterrupt.DecisionJson);
            var storedPackage = await db.TianmingPackages.SingleAsync(package => package.RuntimeRunId == run.RunId);
            using var packageJson = JsonDocument.Parse(storedPackage.InputJson);
            var persistedHardFacts = packageJson.RootElement
                .GetProperty("hardContinuityFacts")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToList();
            Assert.Contains(persistedHardFacts,
                fact => fact.Contains("执行中补充要求", StringComparison.Ordinal) &&
                        fact.Contains("打怪升级反馈", StringComparison.Ordinal));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_StopsAtToolBoundaryWhenDirectionChangeInterruptIsPending()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-direction-change-boundary-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<IAgentInterruptService, AgentInterruptService>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        var provider = BuildToolServiceProvider(services);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写沈砚进入废墟邮路的第一章。",
                CandidateDirections = { "废墟邮路探索" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索"
                }
            }, CancellationToken.None);

            var session = new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = run.RunId,
                RuntimeRunId = "runtime-run-direction-1"
            };

            string interruptId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var interrupts = scope.ServiceProvider.GetRequiredService<IAgentInterruptService>();
                var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
                    RuntimeRunId: "runtime-run-direction-1",
                    UserId: "user-1",
                    SessionId: "session-1",
                    ProjectId: "project-1",
                    Kind: "direction_change",
                    Message: "方向改一下，不要探索铺垫，直接改成怪物围攻和打怪升级。",
                    Priority: 90));
                interruptId = interrupt.Id;
            }

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.True(result.IsRepairable);
            Assert.Equal("runtime_direction_change", result.Phase);
            Assert.Equal("RUNTIME_DIRECTION_CHANGE", result.Failure?.Code);
            Assert.Contains("安全边界", result.Message);
            Assert.Contains(session.WorkingMemory.RuntimeInterrupts,
                item => item.InterruptId == interruptId &&
                        item.Kind == "direction_change" &&
                        item.Message.Contains("怪物围攻", StringComparison.Ordinal));

            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var consumedInterrupt = await db.AgentInterrupts.SingleAsync(item => item.Id == interruptId);
            Assert.Equal(AgentInterruptStatus.Consumed, consumedInterrupt.Status);
            Assert.Contains("agent_tool_stage_boundary", consumedInterrupt.DecisionJson);
            Assert.False(await db.TianmingPackages.AnyAsync(package => package.RuntimeRunId == run.RunId));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildChapterContextPackage_UsesBoundHardFactsWhenKnowledgeSearchMisses()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-sqlite-hardfact-context-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var services = new ServiceCollection();
        var dbRoot = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName, dbRoot));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "星渊邮差",
                Status = "Planning",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "sqlite-hardfact-1",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "结构与道具硬事实",
                Content = "必须为6卷60章，每卷10章，每章正文不少于3000字；主角必须叫沈砚；银蓝邮徽只能辨认被篡改邮路，不能攻击或治愈。",
                Weight = 10,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsageEntity
            {
                Id = "usage-sqlite-hardfact-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "sqlite-hardfact-1",
                Status = "referenced",
                SourceSessionId = "session-1",
                SourceRunId = "run-1",
                FirstSeenAt = DateTime.UtcNow.AddMinutes(-5),
                LastUsedAt = DateTime.UtcNow,
                UsageCount = 1,
                Note = "项目已绑定的硬事实"
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.KnowledgeBases.CountAsync(k =>
                k.UserId == "user-1" &&
                k.SourceProjectId == "project-1" &&
                k.EntryType == "HardFact" &&
                !k.IsArchived));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Equal(1, await db.KnowledgeBases.CountAsync(k =>
                k.UserId == "user-1" &&
                k.SourceProjectId == "project-1" &&
                k.EntryType == "HardFact" &&
                !k.IsArchived));
        }

        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-004",
                UserGoal = "写沈砚带着银蓝邮徽继续寻找第九枚空邮票线索。",
                CandidateDirections = { "空邮票线索追踪与邮徽边界承接" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            var session = new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = run.RunId
            };

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            Assert.Contains(execution.ContextPackage!.HardContinuityFacts,
                fact => fact.Contains("6卷60章") && fact.Contains("银蓝邮徽"));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildChapterContextPackage_PersistsTianmingPackageAndProductionEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-build-package-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "生产包测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章：银蓝邮徽",
                ChapterNumber = 1,
                WordCount = 3200,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "hardfact-1",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "邮徽边界",
                Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击。",
                Weight = 10,
                CreatedAt = DateTime.UtcNow
            });
            db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsageEntity
            {
                Id = "usage-hardfact-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                Status = "referenced",
                SourceSessionId = "session-1",
                SourceRunId = "run-1",
                FirstSeenAt = DateTime.UtcNow.AddMinutes(-5),
                LastUsedAt = DateTime.UtcNow,
                UsageCount = 1,
                Note = "项目已绑定的邮徽边界硬事实"
            });
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "snapshot-001",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-001",
                VersionNumber = 1,
                Source = "chapter_commit",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    chapterId = "project-1-chapter-001",
                    chapterTitle = "第一章：银蓝邮徽",
                    worldRules = new[] { "旧邮徽只能辨认被篡改邮路" },
                    characterStates = new[] { "陈默：维修站青年，左臂受伤" },
                    activeConflicts = new[] { "黑雨逼近维修站" },
                    nextChapterMustCarry = new[] { "必须承接维修站地下室出口被堵", "主角姓名保持陈默" }
                }),
                CreatedAt = DateTime.UtcNow
            });
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "snapshot-llm-001",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-001",
                VersionNumber = 2,
                Source = "chapter_fact_extraction",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    chapterId = "project-1-chapter-001",
                    chapterTitle = "第一章：银蓝邮徽",
                    protagonistName = "陈默",
                    protagonistIdentity = "维修站青年，刚被旧邮路选中",
                    protagonistStatus = "左臂受伤但仍能行动",
                    currentLocation = "维修站地下室出口",
                    systemState = "银蓝邮徽只能辨认旧邮路，不能攻击",
                    equipmentState = "银蓝邮徽发烫预警",
                    keyEvents = new[] { "陈默打开旧邮路入口" },
                    endingState = "维修站地下室出口被堵，黑雨逼近",
                    nextChapterMustCarry = new[] { "陈默必须从堵死的地下室出口脱身", "银蓝邮徽不能攻击" }
                }),
                CreatedAt = DateTime.UtcNow.AddMinutes(1)
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new AuditViolationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "构建第二章生产包。",
                CandidateDirections = { "第二章承接上一章结尾并推进主线压力" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            var evt = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_context_package_built");
            var savedRun = (await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None))
                .AgentRuns.Single(r => r.RunId == run.RunId);

            Assert.Equal("chapter_context_package", package.PackageKind);
            Assert.Equal("project-1", package.ProjectId);
            Assert.Equal("chapter-002", package.ChapterId);
            using var knowledgeJson = JsonDocument.Parse(package.KnowledgeSnapshotJson!);
            Assert.Contains(knowledgeJson.RootElement.GetProperty("hardContinuityFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("银蓝邮徽", StringComparison.Ordinal) == true);
            Assert.True(JsonContainsText(package.FactSnapshotJson, "snapshot-001"), package.FactSnapshotJson);
            Assert.True(JsonContainsText(package.FactSnapshotJson, "snapshot-llm-001"), package.FactSnapshotJson);
            Assert.True(JsonContainsText(package.FactSnapshotJson, "上一章主角：陈默"), package.FactSnapshotJson);
            Assert.True(JsonContainsText(package.FactSnapshotJson, "上一章系统状态：银蓝邮徽只能辨认旧邮路，不能攻击"), package.FactSnapshotJson);
            Assert.True(JsonContainsText(package.FactSnapshotJson, "下一章必须承接：陈默必须从堵死的地下室出口脱身"), package.FactSnapshotJson);
            using var inputJson = JsonDocument.Parse(package.InputJson);
            Assert.Contains(inputJson.RootElement.GetProperty("hardContinuityFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("必须承接维修站地下室出口被堵", StringComparison.Ordinal) == true);
            Assert.Contains(inputJson.RootElement.GetProperty("hardContinuityFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("上一章主角：陈默", StringComparison.Ordinal) == true);
            Assert.Contains(inputJson.RootElement.GetProperty("hardContinuityFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("银蓝邮徽只能辨认旧邮路，不能攻击", StringComparison.Ordinal) == true);
            Assert.Contains(inputJson.RootElement.GetProperty("hardContinuityFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("陈默必须从堵死的地下室出口脱身", StringComparison.Ordinal) == true);
            Assert.Equal(package.Id, savedRun.ContextPackage!.PackageId);
            Assert.Contains(savedRun.ContextPackage.HardContinuityFacts,
                fact => fact.Contains("主角姓名保持陈默", StringComparison.Ordinal));
            Assert.Contains(savedRun.ContextPackage.HardContinuityFacts,
                fact => fact.Contains("上一章主角：陈默", StringComparison.Ordinal));
            Assert.Contains(savedRun.ContextPackage.HardContinuityFacts,
                fact => fact.Contains("上一章系统状态：银蓝邮徽只能辨认旧邮路，不能攻击", StringComparison.Ordinal));
            Assert.Contains(savedRun.ContextPackage.HardContinuityFacts,
                fact => fact.Contains("下一章必须承接：陈默必须从堵死的地下室出口脱身", StringComparison.Ordinal));
            Assert.Contains(savedRun.ContextPackage.CharacterStates,
                state => state.Contains("左臂受伤但仍能行动", StringComparison.Ordinal));
            Assert.Contains(savedRun.ContextPackage.PreviousSummaries,
                summary => summary.Contains("第一章：银蓝邮徽", StringComparison.Ordinal));
            Assert.Equal("chapter_context_package_built", evt.EventType);
            Assert.Equal(NovelAgentProductionStages.PackageBuilt, evt.Stage);
            Assert.Equal("completed", evt.Status);
            Assert.Equal(package.Id, evt.PackageId);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateChapterDraft_PersistsGateEventOnExistingPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-gate-event-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "门禁事件测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "校验第一章草稿。",
                CandidateDirections = { "第一章草稿门禁校验准备" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var gateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(gateResult.Success);
            var packageId = ((NovelAgentExecutionResult)gateResult.Data!).ContextPackage!.PackageId;
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var events = await verifyDb.ProductionEvents
                .Where(e => e.RuntimeRunId == run.RunId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(events, e => e.EventType == "chapter_context_package_built" && e.PackageId == packageId);
            var gateEvent = Assert.Single(events, e => e.EventType == "chapter_gate_validated");
            Assert.Equal(packageId, gateEvent.PackageId);
            Assert.Equal(NovelAgentProductionStages.GateValidated, gateEvent.Stage);
            Assert.Equal("completed", gateEvent.Status);
            Assert.Contains("validated", gateEvent.DataJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateChapterWithChanges_PersistsBlockedEventOnExistingPackageWhenLlmMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-generate-event-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "生成事件测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成第一章正文。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var generateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(generateResult.Success);
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            var generateEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_draft_generated");

            Assert.Equal(package.Id, generateEvent.PackageId);
            Assert.Equal(NovelAgentProductionStages.DraftGenerated, generateEvent.Stage);
            Assert.Equal("blocked", generateEvent.Status);
            Assert.Contains("blocked_missing_llm_settings", generateEvent.DataJson);

            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.RuntimeRunId == run.RunId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            Assert.Contains(runtimeEvents, e =>
                e.Type == "production_progress" &&
                e.Stage == NovelAgentProductionStages.PackageBuilt &&
                e.Status == "completed" &&
                e.DisplaySurface == AgentRuntimeEventSurface.Workflow &&
                e.DisplayPolicy == AgentRuntimeEventDisplayPolicy.Timeline);
            Assert.Contains(runtimeEvents, e =>
                e.Type == "production_progress" &&
                e.Stage == NovelAgentProductionStages.DraftGenerated &&
                e.Status == "running" &&
                e.Message.Contains("正在生成章节正文", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(runtimeEvents, e =>
                e.Type == "production_progress" &&
                e.Stage == NovelAgentProductionStages.DraftGenerated &&
                e.Status == "blocked" &&
                e.DisplaySurface == AgentRuntimeEventSurface.Workflow &&
                e.DisplayPolicy == AgentRuntimeEventDisplayPolicy.Timeline &&
                e.Message.Contains("blocked_missing_llm_settings", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_EmitsDraftGenerationHeartbeatWhileWaitingForModel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-draft-heartbeat-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "草稿生成心跳测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new ShortDraftPassingKernel
        {
            DraftDelay = TimeSpan.FromMilliseconds(1300)
        };
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成第一章正文。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, kernel.GenerateDraftCallCount);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.RuntimeRunId == run.RunId &&
                            e.Type == "production_progress" &&
                            e.Stage == NovelAgentProductionStages.DraftGenerated)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(runtimeEvents, e =>
                e.Status == "running" &&
                e.Message.Contains("正在等待模型生成正文", StringComparison.Ordinal) &&
                e.Message.Contains("如有阶段性产物", StringComparison.Ordinal) &&
                e.DataJson.Contains("\"heartbeat\":true", StringComparison.Ordinal));
            Assert.Contains(runtimeEvents, e => e.Status == "completed");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_EmitsChapterCommitHeartbeatWhileWaitingForCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-commit-heartbeat-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "提交心跳测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new PassingReviewDraftKernel(provider.GetRequiredService<IGeneratedContentService>())
        {
            CommitDelay = TimeSpan.FromMilliseconds(1300)
        };
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成并提交第一章正文。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.RuntimeRunId == run.RunId &&
                            e.Type == "production_progress" &&
                            e.Stage == NovelAgentProductionStages.ChapterCommitted)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(runtimeEvents, e =>
                e.Status == "running" &&
                e.DataJson.Contains("\"heartbeat\":true", StringComparison.Ordinal));
            Assert.Contains(runtimeEvents, e => e.Status == "completed");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_EnqueuesPostCommitMetadataOutboxWithoutWaitingForTruthRecorder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-commit-early-completed-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var delayedRecorder = new DelayedChapterCommitTruthRecorder();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IChapterCommitTruthRecorder>(delayedRecorder);
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "提交快速完成反馈测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new PassingReviewDraftKernel(provider.GetRequiredService<IGeneratedContentService>());
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成并提交第一章正文，提交后处理可以继续后台推进。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                    new AgentToolCall
                    {
                        Name = "ProduceChapter",
                        Arguments = new Dictionary<string, string>
                        {
                            ["runId"] = run.RunId,
                            ["maxRepairAttempts"] = "0",
                            ["commitPolicy"] = "auto_commit"
                        }
                    },
                    session,
                    await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                    confirmed: true,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(result.Success, result.Message);
            Assert.False(delayedRecorder.HasStarted);

            await using (var verifyScope = provider.CreateAsyncScope())
            {
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
                Assert.True(await verifyDb.Chapters.AnyAsync(c => c.Id == "project-1-chapter-001"));
                Assert.True(await verifyDb.AgentRuntimeEvents.AnyAsync(e =>
                    e.RuntimeRunId == run.RunId &&
                    e.Type == "production_progress" &&
                    e.Stage == NovelAgentProductionStages.ChapterCommitted &&
                    e.Status == "completed"));
                var outbox = await verifyDb.OutboxEvents.SingleAsync(e =>
                    e.RuntimeRunId == run.RunId &&
                    e.EventType == "finalize_chapter_commit_metadata" &&
                    e.AggregateType == "chapter");
                var runOutboxes = await verifyDb.OutboxEvents
                    .Where(e => e.RuntimeRunId == run.RunId)
                    .OrderBy(e => e.EventType)
                    .ToListAsync();
                var queuedEvents = await verifyDb.ProductionEvents
                    .Where(e => e.RuntimeRunId == run.RunId &&
                                e.EventType == "outbox_queued")
                    .OrderBy(e => e.EventType)
                    .ToListAsync();
                var indexOutbox = await verifyDb.OutboxEvents.SingleAsync(e =>
                    e.EventType == "index_chapter_content" &&
                    e.AggregateType == "chapter_version");
                Assert.Equal("pending", outbox.Status);
                Assert.Contains("\"targetChapterId\":\"chapter-001\"", outbox.PayloadJson);
                Assert.NotEmpty(runOutboxes);
                Assert.Contains(runOutboxes, e => e.EventType == "finalize_chapter_commit_metadata");
                Assert.All(runOutboxes, runOutbox =>
                    Assert.Contains(queuedEvents, e =>
                        e.ArtifactType == "outbox_event" &&
                        e.ArtifactId == runOutbox.Id &&
                        e.Status == runOutbox.Status &&
                        e.DataJson?.Contains(runOutbox.EventType, StringComparison.Ordinal) == true));
                Assert.Null(indexOutbox.RuntimeRunId);
                Assert.DoesNotContain(queuedEvents, e =>
                    e.DataJson?.Contains("index_chapter_content", StringComparison.Ordinal) == true);
                Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                    e.RuntimeRunId == run.RunId &&
                    e.Stage == NovelAgentProductionStages.RunCompleted);
            }
        }
        finally
        {
            delayedRecorder.Release();
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_BlocksNextChapterWhenPreviousPostCommitOutboxIsPending()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-next-chapter-outbox-block-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "连续章节 outbox 依赖测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()),
            new TwoChapterContinuityKernel(provider.GetRequiredService<IGeneratedContentService>()));
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var firstRun = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写第一章并提交书城。",
                CandidateDirections = { "第一章：黑雨维修站与旧邮路开启" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "旧邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = firstRun.RunId;
            var firstResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = firstRun.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(firstResult.Success, firstResult.Message);
            await using (var pendingScope = provider.CreateAsyncScope())
            {
                var db = pendingScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
                Assert.Contains(await db.OutboxEvents.ToListAsync(), e =>
                    e.EventType == "finalize_chapter_commit_metadata" &&
                    e.Status == "pending");
            }

            var secondRun = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "写第二章并提交书城，必须承接第一章事实。",
                CandidateDirections = { "第二章：旧站台追击与代价延续" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "旧邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = secondRun.RunId;

            var secondResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = secondRun.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(secondResult.Success);
            Assert.True(secondResult.IsRepairable);
            Assert.Equal("previous_chapter_post_commit_outbox", secondResult.MissingPrerequisite);
            Assert.Equal("TOOL_INPUT_ARTIFACT_BLOCKED", secondResult.Failure?.Code);
            Assert.Equal("ProduceChapter", secondResult.RecommendedToolName);
            Assert.Contains("上一章提交后后台沉淀尚未完成", secondResult.Message);
            Assert.Contains("finalize_chapter_commit_metadata", secondResult.Message);
            Assert.Contains("post_commit_outbox", JsonSerializer.Serialize(secondResult.Data));
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.DoesNotContain(await verifyDb.Chapters.ToListAsync(), chapter => chapter.Id == "project-1-chapter-002");
            Assert.DoesNotContain(await verifyDb.AgentRuntimeEvents.ToListAsync(), e =>
                e.RuntimeRunId == secondRun.RunId);
            Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == secondRun.RunId &&
                e.EventType == "production_dependency_blocked");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_ReturnsBusyWhenSameChapterProductionLeaseIsHeld()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-lease-busy-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        var leaseService = new BusyChapterProductionLeaseService();
        services.AddSingleton<IChapterProductionLeaseService>(leaseService);
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);
        var kernel = new CountingProductionKernel();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1"), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        await using (var leaseSeedScope = provider.CreateAsyncScope())
        {
            var leaseDb = leaseSeedScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            leaseDb.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            leaseDb.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "锁测试项目",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await leaseDb.SaveChangesAsync();
        }

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "生产第二章。",
                CandidateDirections = { "承接第一章黑雨围城" }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["commitPolicy"] = "draft_only"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.True(result.IsRepairable);
            Assert.Equal("chapter_production_lock", result.MissingPrerequisite);
            Assert.Contains("同一章节正在生产", result.Message);
            Assert.Contains("QueryNovelProductionState", result.Suggestions);
            Assert.Equal("project-1", leaseService.LastProjectId);
            Assert.Equal("chapter-002", leaseService.LastChapterId);
            Assert.Equal(0, kernel.BuildContextPackageCalls);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_EmitsQualityReviewHeartbeatWhileWaitingForReview()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-review-heartbeat-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IAgentEditorialReviewModelClient>(new DelayedPassingEditorialReviewModelClient(
            TimeSpan.FromMilliseconds(1300)));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "评审心跳测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new PassingReviewDraftKernel(provider.GetRequiredService<IGeneratedContentService>());
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成并提交第一章正文，确认 Agent 总编评审阶段有进度反馈。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.RuntimeRunId == run.RunId &&
                            e.Type == "production_progress" &&
                            e.Stage == NovelAgentProductionStages.ReviewCompleted)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(runtimeEvents, e =>
                e.Status == "running" &&
                e.DataJson.Contains("\"heartbeat\":true", StringComparison.Ordinal));
            Assert.Contains(runtimeEvents, e => e.Status == "completed");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(NovelAgentProductionStages.PackageBuilt)]
    [InlineData(NovelAgentProductionStages.GateValidated)]
    public async Task ProduceChapter_EmitsHeartbeatWhileWaitingForEarlyProductionStage(string targetStage)
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-early-heartbeat-{targetStage}-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions { HeartbeatSeconds = 1 }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "早期阶段心跳测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new PassingReviewDraftKernel
        {
            ContextPackageDelay = targetStage == NovelAgentProductionStages.PackageBuilt
                ? TimeSpan.FromMilliseconds(2500)
                : TimeSpan.Zero,
            GateValidationDelay = targetStage == NovelAgentProductionStages.GateValidated
                ? TimeSpan.FromMilliseconds(2500)
                : TimeSpan.Zero
        };
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成第一章正文，验证早期生产阶段进度反馈。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.RuntimeRunId == run.RunId &&
                            e.Type == "production_progress" &&
                            e.Stage == targetStage)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(runtimeEvents, e =>
                e.Status == "running" &&
                e.DataJson.Contains("\"heartbeat\":true", StringComparison.Ordinal));
            Assert.Contains(runtimeEvents, e => e.Status == "completed");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionStageHeartbeat_CancelsWhenRuntimeRunRequestsCancel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IAgentRuntimeRunService, AgentRuntimeRunService>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions
            {
                HeartbeatSeconds = 1,
                StageTimeoutSeconds = 10,
                DraftGenerationTimeoutSeconds = 10
            }));
        var provider = BuildToolServiceProvider(services);

        string runtimeRunId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            var runs = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRunService>();
            var runtimeRun = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
                UserId: "user-1",
                SessionId: "session-1",
                ProjectId: "project-1",
                UserMessage: "写第一章",
                Mode: AgentRuntimeRunMode.Production));
            await runs.MarkRunningAsync(runtimeRun.Id);
            runtimeRunId = runtimeRun.Id;
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            RuntimeRunId = runtimeRunId
        };
        var method = typeof(AgentToolRegistry).GetMethod(
            "RunWithProductionStageHeartbeatAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var generic = method!.MakeGenericMethod(typeof(string));
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(200);
            await using var cancelScope = provider.CreateAsyncScope();
            var db = cancelScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var run = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == runtimeRunId);
            run.CancelRequested = true;
            run.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
        Func<CancellationToken, Task<string>> operation = async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return "finished";
        };

        var task = Assert.IsAssignableFrom<Task<string>>(generic.Invoke(
            registry,
            new object?[]
            {
                session,
                "chapter-run-1",
                NovelAgentProductionStages.DraftGeneration,
                "chapter_draft_artifact",
                null,
                operation,
                CancellationToken.None
            }));
        await cancelTask;
        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.True((await verifyDb.AgentRuntimeRuns.SingleAsync(r => r.Id == runtimeRunId)).CancelRequested);
        }
        var ex = await Assert.ThrowsAsync<AgentRuntimeRunCancelledException>(() => task);

        Assert.Equal(runtimeRunId, ex.RuntimeRunId);
        Assert.Equal(NovelAgentProductionStages.DraftGeneration, ex.Stage);
    }

    [Fact]
    public async Task ProduceChapter_RuntimeProgressEventsUseBackgroundRuntimeRunIdWhenPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-runtime-event-id-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "运行事件归属测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new ShortDraftPassingKernel();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            RuntimeRunId = "runtime-run-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生成第一章正文。",
                CandidateDirections = { "第一章邮路危机开场与能力边界展示" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var runtimeEvents = await verifyDb.AgentRuntimeEvents
                .Where(e => e.Type == "production_progress")
                .ToListAsync();

            Assert.NotEmpty(runtimeEvents);
            Assert.All(runtimeEvents, e => Assert.Equal("runtime-run-1", e.RuntimeRunId));
            Assert.Contains(runtimeEvents, e => e.DataJson.Contains(run.RunId, StringComparison.Ordinal));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_DoesNotStartReviewRewriteWhenInitialReviewCanCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-stage-timeout-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IOptions<AgentProductionStageProgressOptions>>(
            Options.Create(new AgentProductionStageProgressOptions
            {
                HeartbeatSeconds = 1,
                StageTimeoutSeconds = 1
            }));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "阶段预算测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new HangingSecondReviewRewriteKernel(provider.GetRequiredService<IGeneratedContentService>());
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生产第一章并提交书城，评审不合格时自动修订。",
                CandidateDirections = { "第一章完整生产并等待总编验收" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, kernel.GenerateDraftCallCount);
            Assert.Contains("提交书城", result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_agent_review_feedback_applied");
            Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_quality_review_warnings_accepted" &&
                e.Status == "completed");

            await FinalizeChapterCommitMetadataOutboxAsync(provider);
            var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            var version = await verifyDb.ChapterVersions
                .OrderByDescending(v => v.ContentDocumentId == chapter.CurrentDocumentId)
                .ThenByDescending(v => v.VersionNumber)
                .FirstAsync(v => v.ChapterId == chapter.Id);
            Assert.Contains("\"requiresRewrite\":false", version.AgentReviewJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsWarningOnlyAgentReview_ReturnsTrueForWarningsWithoutFailures()
    {
        var method = typeof(AgentToolRegistry).GetMethod(
            "IsWarningOnlyAgentReview",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(AgentToolExecutionResult) },
            modifiers: null);
        Assert.NotNull(method);
        var result = new AgentToolExecutionResult
        {
            Success = false,
            Data = new NovelAgentExecutionResult
            {
                Success = true,
                Run = new NovelAgentRun
                {
                    PostGenerationReview = new NovelAgentPostGenerationReview
                    {
                        RequiresRewrite = true,
                        OverallResult = "Warning",
                        Checks =
                        {
                            new NovelAgentReviewCheck
                            {
                                Key = "conflict_move_alignment",
                                Name = "冲突推进",
                                Status = NovelAgentReviewCheckStatus.Warning,
                                Message = "正文没有明显体现本章冲突推进。"
                            },
                            new NovelAgentReviewCheck
                            {
                                Key = "canon_ledger_coverage",
                                Name = "Canon Ledger 覆盖",
                                Status = NovelAgentReviewCheckStatus.Warning,
                                Message = "章节正文未明显引用已有 Canon 条目。"
                            }
                        }
                    }
                }
            }
        };

        var isWarningOnly = Assert.IsType<bool>(method!.Invoke(null, new object[] { result }));

        Assert.True(isWarningOnly);
    }

    [Fact]
    public async Task BuildChapterContextPackage_IncludesProjectKnowledgeUsageBindingsInPackageSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-build-package-knowledge-bindings-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "绑定知识生产包测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "global-hardfact-1",
                UserId = "user-1",
                SourceProjectId = null,
                EntryType = "HardFact",
                Title = "银蓝邮徽能力边界",
                Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈或升级。",
                Weight = 10,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsageEntity
            {
                Id = "usage-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "global-hardfact-1",
                Status = "referenced",
                SourceSessionId = "session-1",
                SourceRunId = "run-existing",
                FirstSeenAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                UsageCount = 3,
                Note = "用户确认该知识用于本项目",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 90,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                BoundVersion = "global-hardfact-1-v2",
                UsedByChaptersJson = "[\"project-1-chapter-001\"]"
            });
            db.KnowledgeClassifications.Add(new KnowledgeClassificationEntity
            {
                Id = "classification-global-hardfact-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "global-hardfact-1",
                Model = "fake-llm",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 90,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                Confidence = 0.93,
                ClassificationJson = """
                {
                  "rule": "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                  "targetEntities": ["银蓝邮徽"],
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true
                }
                """,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "构建第二章生产包，必须遵守银蓝邮徽能力边界。",
                CandidateDirections = { "第二章能力边界承接与追踪压力升级" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            if (!result.Success)
            {
                throw new InvalidOperationException(JsonSerializer.Serialize(new
                {
                    result.Message,
                    result.Phase,
                    result.RecommendedToolName,
                    result.Failure
                }));
            }
            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            Assert.Contains(execution.ContextPackage!.KnowledgeBindings,
                binding => binding.KnowledgeId == "global-hardfact-1" &&
                           binding.ProjectUsageStatus == "referenced" &&
                           binding.Title.Contains("银蓝邮徽", StringComparison.Ordinal) &&
                           binding.ConstraintLevel == "HardConstraint" &&
                           binding.PackagePolicy == "DefaultEveryChapter" &&
                           binding.ClassificationId == "classification-global-hardfact-1" &&
                           binding.ClassificationRule.Contains("不能攻击", StringComparison.Ordinal) &&
                           binding.TargetEntities.Contains("银蓝邮徽") &&
                           binding.ShouldEnterGate &&
                           binding.ShouldEnterBlueprint &&
                           binding.ShouldEnterFactSnapshot);
            Assert.Contains(execution.ContextPackage.HardContinuityFacts,
                fact => fact.Contains("银蓝邮徽能力边界", StringComparison.Ordinal) &&
                        fact.Contains("不能攻击", StringComparison.Ordinal));

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            using var knowledgeJson = JsonDocument.Parse(package.KnowledgeSnapshotJson!);
            var bindings = knowledgeJson.RootElement.GetProperty("knowledgeBindings").EnumerateArray().ToList();
            Assert.Contains(bindings, binding =>
                binding.GetProperty("knowledgeId").GetString() == "global-hardfact-1" &&
                binding.GetProperty("projectUsageStatus").GetString() == "referenced" &&
                binding.GetProperty("role").GetString() == "ItemRule" &&
                binding.GetProperty("priority").GetInt32() == 90 &&
                binding.GetProperty("constraintLevel").GetString() == "HardConstraint" &&
                binding.GetProperty("packagePolicy").GetString() == "DefaultEveryChapter" &&
                binding.GetProperty("boundVersion").GetString() == "global-hardfact-1-v2" &&
                binding.GetProperty("classificationId").GetString() == "classification-global-hardfact-1" &&
                binding.GetProperty("classificationRule").GetString()!.Contains("不能攻击", StringComparison.Ordinal) &&
                binding.GetProperty("shouldEnterGate").GetBoolean() &&
                binding.GetProperty("shouldEnterBlueprint").GetBoolean() &&
                binding.GetProperty("shouldEnterFactSnapshot").GetBoolean());
            var usage = await verifyDb.ProjectKnowledgeUsages.SingleAsync(x => x.Id == "usage-1");
            Assert.Equal(4, usage.UsageCount);
            Assert.NotNull(usage.LastUsedAt);
            using var usedByChapters = JsonDocument.Parse(usage.UsedByChaptersJson!);
            Assert.Contains(usedByChapters.RootElement.EnumerateArray(),
                chapter => chapter.GetString() == "chapter-002");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryProjectKnowledgeBindings_ReturnsProjectScopedBoundKnowledge()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-query-knowledge-bindings-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识绑定查询测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.AddRange(
                new KnowledgeBaseEntity
                {
                    Id = "hardfact-1",
                    UserId = "user-1",
                    SourceProjectId = null,
                    EntryType = "HardFact",
                    Title = "银蓝邮徽能力边界",
                    Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈或升级。",
                    Tags = "[\"邮徽\",\"硬事实\"]",
                    Weight = 10,
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                },
                new KnowledgeBaseEntity
                {
                    Id = "style-1",
                    UserId = "user-1",
                    SourceProjectId = null,
                    EntryType = "Style",
                    Title = "废土邮路风格",
                    Content = "描写要有冷硬废土质感。",
                    Weight = 5,
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                });
            db.ProjectKnowledgeUsages.AddRange(
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-hardfact",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    KnowledgeId = "hardfact-1",
                    Status = "referenced",
                    SourceSessionId = "session-1",
                    SourceRunId = "run-1",
                    FirstSeenAt = DateTime.UtcNow.AddMinutes(-3),
                    LastUsedAt = DateTime.UtcNow,
                    UsageCount = 4,
                    Note = "项目硬事实"
                },
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-style",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    KnowledgeId = "style-1",
                    Status = "imported",
                    SourceSessionId = "session-1",
                    FirstSeenAt = DateTime.UtcNow.AddMinutes(-2),
                    UsageCount = 1,
                    Note = "风格参考"
                });
            db.KnowledgeConflictReports.Add(new KnowledgeConflictReportEntity
            {
                Id = "conflict-hardfact",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "hardfact-1",
                ConflictingKnowledgeIdsJson = "[\"style-1\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "银蓝邮徽能力边界与另一条条目冲突。",
                RecommendedAction = "让用户确认保留哪条设定。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await new ContentDocumentService(db).SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "story_bible",
                "project-1",
                "aggregate_json",
                "Story Bible",
                JsonSerializer.Serialize(new StoryBibleDocument
                {
                    CanonLedger =
                    {
                        new CanonLedgerEntry
                        {
                            Id = "canon-boundary",
                            Type = CanonLedgerEntryType.Constraint,
                            Status = CanonLedgerEntryStatus.Canon,
                            Title = "邮徽能力边界",
                            Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
                            Rationale = "KnowledgeId=hardfact-1",
                            ConflictCheck = "clear"
                        }
                    }
                }),
                CancellationToken.None);
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryProjectKnowledgeBindings",
                    Arguments = new Dictionary<string, string>()
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("知识状态：已导入 1 条，已引用 1 条，已进入 CanonLedger 1 条，待分类 2 条，开放冲突 1 条", result.Message);
            Assert.Contains("银蓝邮徽能力边界", result.Message);
            Assert.Contains("referenced", result.Message);
            Assert.Contains("知识冲突报告", result.Message);
            Assert.Contains("conflict-hardfact", result.Message);
            Assert.Contains("Story Bible Canon", result.Message);
            Assert.Contains("canon-boundary", result.Message);
            var json = JsonSerializer.Serialize(result.Data);
            using var document = JsonDocument.Parse(json);
            Assert.Equal("project-1", document.RootElement.GetProperty("projectId").GetString());
            var summary = document.RootElement.GetProperty("statusSummary");
            Assert.Equal(1, summary.GetProperty("importedCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("referencedCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("canonLedgerCount").GetInt32());
            Assert.Equal(2, summary.GetProperty("pendingClassificationCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("openConflictCount").GetInt32());
            var bindings = document.RootElement.GetProperty("bindings").EnumerateArray().ToList();
            Assert.Contains(bindings, binding =>
                binding.GetProperty("knowledgeId").GetString() == "hardfact-1" &&
                binding.GetProperty("entryType").GetString() == "HardFact" &&
                binding.GetProperty("projectUsageStatus").GetString() == "referenced" &&
                binding.GetProperty("tags").EnumerateArray().Any(tag => tag.GetString() == "邮徽"));
            Assert.Contains(document.RootElement.GetProperty("hardFacts").EnumerateArray(),
                fact => fact.GetString()?.Contains("不能攻击", StringComparison.Ordinal) == true);
            var canonLedger = document.RootElement.GetProperty("canonLedger").EnumerateArray().ToList();
            Assert.Contains(canonLedger, canon =>
                canon.GetProperty("id").GetString() == "canon-boundary" &&
                canon.GetProperty("status").GetString() == "Canon" &&
                canon.GetProperty("content").GetString()!.Contains("不能攻击", StringComparison.Ordinal));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AttachKnowledgeToProject_BindsExistingKnowledgeThroughNormalToolRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-attach-knowledge-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton(Mock.Of<IAgentMemoryEventService>());
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        services.AddSingleton<ILogger<ProjectKnowledgeUsageService>>(NullLogger<ProjectKnowledgeUsageService>.Instance);
        services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识绑定工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-attach-1",
                UserId = "user-1",
                SourceProjectId = null,
                IdempotencyKey = "knowledge-attach-v1",
                EntryType = "HardFact",
                Title = "银蓝邮徽能力边界",
                Content = "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                Tags = "[\"邮徽\",\"硬事实\"]",
                Weight = 88,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "AttachKnowledgeToProject",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = "knowledge-attach-1",
                        ["status"] = "referenced"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("已绑定知识", result.Message);
            Assert.Contains("QueryProjectKnowledgeBindings", result.Message);
            Assert.Contains("ClassifyProjectKnowledge", result.Message);
            Assert.NotNull(result.Artifact);
            Assert.Contains("知识库", result.Artifact!.UserVisibleWhere);
            Assert.Contains("创作工作流", result.Artifact.UserVisibleWhere);

            var query = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryProjectKnowledgeBindings",
                    Arguments = new Dictionary<string, string>()
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(query.Success);
            Assert.Contains("银蓝邮徽能力边界", query.Message);
            Assert.Contains("referenced", query.Message);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var usage = await db.ProjectKnowledgeUsages.SingleAsync();
            Assert.Equal("user-1", usage.UserId);
            Assert.Equal("project-1", usage.ProjectId);
            Assert.Equal("knowledge-attach-1", usage.KnowledgeId);
            Assert.Equal("referenced", usage.Status);
            Assert.Equal("HardFact", usage.Role);
            Assert.Equal("HardConstraint", usage.ConstraintLevel);
            Assert.Equal("DefaultEveryChapter", usage.PackagePolicy);
            Assert.Equal("knowledge-attach-v1", usage.BoundVersion);
            Assert.Equal(1, usage.UsageCount);
            Assert.Equal("session-1", usage.SourceSessionId);
            Assert.Equal("run-1", usage.SourceRunId);
        }
    }

    [Fact]
    public async Task ClassifyProjectKnowledge_PersistsLlmClassificationThroughNormalToolRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-classify-knowledge-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IKnowledgeClassificationService, KnowledgeClassificationService>();
        services.AddSingleton<IKnowledgeClassificationModelClient>(new FixedKnowledgeClassificationModelClient());
        services.AddSingleton<ILogger<KnowledgeClassificationService>>(NullLogger<KnowledgeClassificationService>.Instance);
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识分类工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-1",
                UserId = "user-1",
                SourceProjectId = null,
                IdempotencyKey = "knowledge-v1",
                EntryType = "ReaderPromise",
                Title = "银蓝邮徽边界",
                Content = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                Weight = 7,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var search = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "tool_search",
                    Arguments = new Dictionary<string, string>
                    {
                        ["query"] = "知识 分类 生产语义 约束",
                        ["includeAll"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(search.Success);
            Assert.Contains("ClassifyProjectKnowledge", JsonSerializer.Serialize(search.Data));

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ClassifyProjectKnowledge",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = "knowledge-1"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("ItemRule", result.Message);
            Assert.Contains("HardConstraint", result.Message);
            Assert.Contains("ProduceChapter", result.Message);
            var data = Assert.IsType<KnowledgeClassificationToolResult>(result.Data);
            Assert.Equal("knowledge-1", data.KnowledgeId);
            Assert.Equal("ItemRule", data.Role);
            Assert.Equal("HardConstraint", data.ConstraintLevel);
            Assert.Equal("DefaultEveryChapter", data.PackagePolicy);
            Assert.True(data.ShouldEnterGate);
            Assert.True(data.ShouldEnterBlueprint);
            Assert.True(data.ShouldEnterFactSnapshot);
            Assert.Contains("QueryProjectKnowledgeBindings", data.NextRecommendedTools);
            Assert.Contains("DetectKnowledgeConflicts", data.NextRecommendedTools);
            Assert.Contains("ProduceChapter", data.NextRecommendedTools);
            Assert.NotNull(result.Artifact);
            Assert.Contains("知识库", result.Artifact!.UserVisibleWhere);
            Assert.Contains("创作工作流", result.Artifact.UserVisibleWhere);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var classification = await db.KnowledgeClassifications.SingleAsync();
            Assert.Equal("knowledge-1", classification.KnowledgeId);
            Assert.Equal("project-1", classification.ProjectId);
            Assert.Contains("shouldEnterGate", classification.ClassificationJson);

            var usage = await db.ProjectKnowledgeUsages.SingleAsync();
            Assert.Equal("ItemRule", usage.Role);
            Assert.Equal("HardConstraint", usage.ConstraintLevel);
            Assert.Equal("DefaultEveryChapter", usage.PackagePolicy);
            Assert.Equal("knowledge-v1", usage.BoundVersion);
        }
    }

    [Fact]
    public async Task DetectKnowledgeConflicts_PersistsConflictReportThroughNormalToolRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-detect-knowledge-conflict-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IKnowledgeConflictDetector, KnowledgeConflictDetector>();
        services.AddSingleton<IKnowledgeConflictModelClient>(new FixedKnowledgeConflictModelClient());
        services.AddSingleton<ILogger<KnowledgeConflictDetector>>(NullLogger<KnowledgeConflictDetector>.Instance);
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识冲突工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.AddRange(
                new KnowledgeBaseEntity
                {
                    Id = "knowledge-blue-flame",
                    UserId = "user-1",
                    EntryType = "ItemRule",
                    Title = "邮徽蓝焰攻击",
                    Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
                    Weight = 9,
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                },
                new KnowledgeBaseEntity
                {
                    Id = "knowledge-boundary",
                    UserId = "user-1",
                    EntryType = "HardFact",
                    Title = "邮徽能力边界",
                    Content = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                    Weight = 10,
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                });
            db.ProjectKnowledgeUsages.AddRange(
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-blue-flame",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    KnowledgeId = "knowledge-blue-flame",
                    Status = "imported",
                    Role = "ItemRule",
                    Scope = "ProjectWide",
                    Priority = 90,
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter",
                    FirstSeenAt = DateTime.UtcNow
                },
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-boundary",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    KnowledgeId = "knowledge-boundary",
                    Status = "referenced",
                    Role = "ItemRule",
                    Scope = "ProjectWide",
                    Priority = 95,
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter",
                    FirstSeenAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var search = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "tool_search",
                    Arguments = new Dictionary<string, string>
                    {
                        ["query"] = "知识 冲突 硬约束",
                        ["includeAll"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(search.Success);
            Assert.Contains("DetectKnowledgeConflicts", JsonSerializer.Serialize(search.Data));

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "DetectKnowledgeConflicts",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = "knowledge-blue-flame"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("硬冲突", result.Message);
            Assert.Contains("knowledge-boundary", JsonSerializer.Serialize(result.Data));
            var data = Assert.IsType<KnowledgeConflictDetectionToolResult>(result.Data);
            Assert.True(data.HasConflict);
            Assert.True(data.BlocksProduceChapter);
            Assert.True(data.RequiresUserDecision);
            Assert.Contains("knowledge-boundary", data.ConflictingKnowledgeIds);
            Assert.Contains("ResolveKnowledgeConflict", data.NextRecommendedTools);
            Assert.Contains("QueryProjectKnowledgeBindings", data.NextRecommendedTools);
            Assert.DoesNotContain("ProduceChapter", data.NextRecommendedTools);
            Assert.NotNull(result.Artifact);
            Assert.Contains("知识库", result.Artifact!.UserVisibleWhere);
            Assert.Contains("创作工作流", result.Artifact.UserVisibleWhere);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var report = await db.KnowledgeConflictReports.SingleAsync();
            Assert.Equal("knowledge-blue-flame", report.KnowledgeId);
            Assert.Equal("Hard", report.Severity);
            Assert.True(report.RequiresUserDecision);
        }
    }

    [Fact]
    public async Task ResolveKnowledgeConflict_UpdatesConflictReportThroughNormalToolRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-resolve-knowledge-conflict-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IKnowledgeConflictResolver, KnowledgeConflictResolver>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识冲突解决工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-blue-flame",
                UserId = "user-1",
                EntryType = "ItemRule",
                Title = "邮徽蓝焰攻击",
                Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
                Weight = 9,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            db.KnowledgeConflictReports.Add(new KnowledgeConflictReportEntity
            {
                Id = "conflict-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-blue-flame",
                ConflictingKnowledgeIdsJson = "[\"knowledge-boundary\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "银蓝邮徽不能攻击与蓝焰攻击冲突。",
                RecommendedAction = "询问用户选择。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var search = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "tool_search",
                    Arguments = new Dictionary<string, string>
                    {
                        ["query"] = "解决 知识 冲突 用户选择",
                        ["includeAll"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(search.Success);
            Assert.Contains("ResolveKnowledgeConflict", JsonSerializer.Serialize(search.Data));

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ResolveKnowledgeConflict",
                    Arguments = new Dictionary<string, string>
                    {
                        ["conflictId"] = "conflict-1",
                        ["decision"] = "resolved",
                        ["note"] = "用户确认保留能力边界，蓝焰改为环境现象。"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "run-1"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("resolved", result.Message);
            Assert.Equal("conflict-1", result.Artifact?.ArtifactId);
            Assert.Equal("ProduceChapter", result.RecommendedToolName);
            Assert.Contains("查询生产状态", result.Suggestions);
            Assert.Contains("继续章节生产", result.Suggestions);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var report = await db.KnowledgeConflictReports.SingleAsync();
            Assert.Equal("resolved", report.Status);
            Assert.Equal("session-1", report.ResolvedBySessionId);
            Assert.Contains("蓝焰改为环境现象", report.ResolutionNote);

            var productionEvent = await db.ProductionEvents.SingleAsync();
            Assert.Equal("knowledge_conflict_resolved", productionEvent.EventType);
            Assert.Equal("ready_for_rebuild", productionEvent.Status);
            Assert.Equal("knowledge_conflict_report", productionEvent.ArtifactType);
            Assert.Equal("conflict-1", productionEvent.ArtifactId);

            var creativeIntent = await db.CreativeIntents.SingleAsync();
            Assert.Equal("knowledge", creativeIntent.Source);
            Assert.Equal("accepted", creativeIntent.Status);
            Assert.Equal("world_rule_change", creativeIntent.ImpactLevel);
            Assert.Equal("resolved", creativeIntent.ConflictStatus);
            Assert.Contains("蓝焰改为环境现象", creativeIntent.NormalizedIntent);
            Assert.Contains("conflict-1", creativeIntent.MetadataJson);

            var revisionPlan = await db.RevisionPlans.SingleAsync();
            Assert.Equal("knowledge_conflict_resolution", revisionPlan.Source);
            Assert.Equal("accepted", revisionPlan.Status);
            Assert.Equal("world_rule_change", revisionPlan.PlanType);
            Assert.Equal(creativeIntent.Id, revisionPlan.CreativeIntentId);
            Assert.Equal("conflict-1", revisionPlan.KnowledgeConflictReportId);
            Assert.Contains("ProduceChapter", revisionPlan.Recommendation);

            var runtimeEvent = await db.AgentRuntimeEvents.SingleAsync(e =>
                e.Type == "production_progress" &&
                e.Status == "ready_for_rebuild");
            Assert.Equal("workflow", runtimeEvent.DisplaySurface);
            Assert.Equal("timeline", runtimeEvent.DisplayPolicy);
        }
    }

    [Fact]
    public async Task ProduceChapter_ReturnsStructuredBlockedResultAndPersistsEventsWhenGenerationIsBlocked()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-blocked-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "闭环生产测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new BlockedGenerationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "完整生产第一章并提交书城。",
                CandidateDirections = { "第一章完整生产并提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("High", result.Risk);
            Assert.Equal("chapter_production_blocked", result.Artifact?.ArtifactType);
            Assert.NotNull(result.Failure);
            Assert.Equal(NovelAgentProductionStages.GateValidationOrRepair, result.Failure!.FailedStage);
            Assert.Contains("查看门禁失败项", result.Failure.RecoverableActions);
            Assert.Equal("chapter_production_blocked", result.Failure.ProducedArtifacts.Single().ArtifactType);
            Assert.False(result.Failure.RequiresUserDecision);
            Assert.Contains("生成章节正文", result.Message);
            Assert.Contains("已执行阶段", result.Message);
            Assert.Contains("可继续动作", result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var events = await verifyDb.ProductionEvents
                .Where(e => e.RuntimeRunId == run.RunId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(events, e => e.EventType == "chapter_context_package_built");
            Assert.Contains(events, e => e.EventType == "chapter_draft_generated" && e.Status == "blocked");
            Assert.Single(await verifyDb.TianmingPackages.Where(p => p.RuntimeRunId == run.RunId).ToListAsync());
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_ReturnsUserDecisionFailureWhenOpenHardKnowledgeConflictBlocksPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-knowledge-conflict-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识冲突阻断测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-blue-flame",
                UserId = "user-1",
                EntryType = "ItemRule",
                Title = "邮徽蓝焰攻击",
                Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
                Weight = 9,
                CreatedAt = DateTime.UtcNow
            });
            db.KnowledgeConflictReports.Add(new KnowledgeConflictReportEntity
            {
                Id = "conflict-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-blue-flame",
                ConflictingKnowledgeIdsJson = "[\"knowledge-boundary\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "银蓝邮徽不能攻击与蓝焰攻击冲突。",
                RecommendedAction = "询问用户选择保留哪条设定。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "完整生产第一章并提交书城。",
                CandidateDirections = { "第一章完整生产并提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.NotNull(result.Failure);
            Assert.Equal("KNOWLEDGE_CONFLICT_BLOCKED", result.Failure!.Code);
            Assert.Equal(NovelAgentProductionStages.ContextPackage, result.Failure.FailedStage);
            Assert.True(result.Failure.RequiresUserDecision);
            Assert.False(result.IsRepairable);
            Assert.Equal("QueryProjectKnowledgeBindings", result.RecommendedToolName);
            Assert.Contains("知识冲突", result.Message);
            Assert.Contains("conflict-1", result.Message);
            Assert.Contains("向用户确认", result.Failure.RecoverableActions);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Empty(await verifyDb.TianmingPackages.Where(p => p.RuntimeRunId == run.RunId).ToListAsync());
            var evt = await verifyDb.ProductionEvents.SingleAsync(e => e.EventType == "knowledge_conflict_blocked");
            Assert.Equal("blocked", evt.Status);
            Assert.Contains("conflict-1", evt.DataJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_BlocksAtAgentReviewWhenReviewRequiresRewrite()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-review-blocked-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "总编验收阻断测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "完整生产第一章并提交书城，但总编验收不合格时必须停下。",
                CandidateDirections = { "第一章完整生产并等待总编验收" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("chapter_production_blocked", result.Artifact?.ArtifactType);
            Assert.NotNull(result.Failure);
            Assert.Equal("ReviewChapter", result.Failure!.FailedStage);
            Assert.Contains("按评审修订", result.Failure.RecoverableActions);
            Assert.Equal("chapter_production_blocked", result.Failure.ProducedArtifacts.Single().ArtifactType);
            Assert.Contains("Agent 质量评审", result.Message);
            Assert.Contains("质量评审", result.Message);
            Assert.DoesNotContain("CommitValidatedChapter", result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Empty(await verifyDb.Chapters.ToListAsync());
            var reviewEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_quality_reviewed");
            Assert.Equal("blocked", reviewEvent.Status);
            Assert.Contains("requiresRewrite", reviewEvent.DataJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_UsesAgentReviewFeedbackForAutomaticRewriteBeforeCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-review-rewrite-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "总编验收自动改写测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-review-rewrite",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                Source = "agent_review",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-001",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"按总编验收意见自动改写\"]",
                ContinuityRequirementsJson = "[]",
                ImpactAnalysisJson = "{}",
                AffectedChapterIdsJson = "[\"chapter-001\"]",
                InvalidatedPackageIdsJson = "[]",
                RiskLevel = "medium",
                Recommendation = "根据 AgentReview 反馈重写第一章。",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new ReviewFeedbackRewriteKernel(provider.GetRequiredService<IGeneratedContentService>());
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "完整生产第一章并提交书城；如果总编验收不合格，自动按评审意见修订一次。",
                CandidateDirections = { "第一章完整生产并提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["revisionPlanId"] = "revision-plan-review-rewrite",
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, kernel.GenerateDraftCallCount);
            Assert.Contains("Agent 质量评审#1", result.Message);
            Assert.Contains("正文已进入小说书城", result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await FinalizeChapterCommitMetadataOutboxAsync(provider);
            var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            var version = await verifyDb.ChapterVersions.SingleAsync(v => v.ChapterId == chapter.Id);
            Assert.Contains("\"requiresRewrite\":false", version.AgentReviewJson);
            Assert.Contains("\"qualityScore\":", version.AgentReviewJson);
            Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_agent_review_feedback_applied" &&
                e.Status == "completed" &&
                e.DataJson != null &&
                e.DataJson.Contains("核心创意落地", StringComparison.Ordinal));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_ReturnsRecoverableFailureWhenReviewRewriteModelRequestFails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-review-rewrite-http-failure-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "总编验收改写异常测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-review-rewrite",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                Source = "agent_review",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-001",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"按总编验收意见自动改写\"]",
                ContinuityRequirementsJson = "[]",
                ImpactAnalysisJson = "{}",
                AffectedChapterIdsJson = "[\"chapter-001\"]",
                InvalidatedPackageIdsJson = "[]",
                RiskLevel = "medium",
                Recommendation = "根据 AgentReview 反馈重写第一章。",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new ReviewRewriteHttpFailureKernel();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "完整生产第一章并提交书城；如果总编验收不合格，自动按评审意见修订一次。",
                CandidateDirections = { "第一章完整生产并提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["revisionPlanId"] = "revision-plan-review-rewrite",
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.True(result.IsRepairable);
            Assert.Equal("ProduceChapter", result.RecommendedToolName);
            Assert.Equal("ProduceChapter", result.Failure?.RecommendedAction);
            Assert.True(result.Failure?.Recoverable);
            Assert.Contains("HttpRequestException", result.Message);
            Assert.Contains("继续 ProduceChapter", result.Suggestions);
            Assert.Equal("revision-plan-review-rewrite", result.RecommendedArguments["revisionPlanId"]);
            Assert.Equal(2, kernel.GenerateDraftCallCount);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_CapsAutomaticAgentReviewRewritesAtTwo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-review-cap-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "总编验收预算测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new ShortDraftPassingKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生产第一章并提交书城，但质量评审一直不合格时不能无限改写。",
                CandidateDirections = { "第一章完整生产并等待总编验收" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "5",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Agent 质量评审#2", result.Message);
            Assert.DoesNotContain("Agent 质量评审#3", result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var feedbackEvents = await verifyDb.ProductionEvents
                .Where(e => e.RuntimeRunId == run.RunId && e.EventType == "chapter_agent_review_feedback_applied")
                .ToListAsync();
            Assert.Equal(2, feedbackEvents.Count);
            Assert.Empty(await verifyDb.Chapters.ToListAsync());
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_WithCancelledRuntimeRunStopsBeforeProductionStages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-cancel-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IAgentRuntimeRunService, AgentRuntimeRunService>();
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "取消边界测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-cancelled-1",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = AgentRuntimeRunStatus.Running,
                Mode = AgentRuntimeRunMode.Production,
                CurrentPhase = AgentRuntimeRunStatus.Running,
                UserMessage = "生产第一章",
                LastMessage = "已收到暂停/取消请求，Agent 会在安全边界处理。",
                CancelRequested = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var kernel = new ReviewFeedbackRewriteKernel(provider.GetRequiredService<IGeneratedContentService>());
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            RuntimeRunId = "runtime-cancelled-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生产第一章并提交书城。",
                CandidateDirections = { "第一章完整生产并提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;
            var bible = await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None);

            await Assert.ThrowsAsync<AgentRuntimeRunCancelledException>(() =>
                registry.ExecuteAsync(
                    new AgentToolCall
                    {
                        Name = "ProduceChapter",
                        Arguments = new Dictionary<string, string>
                        {
                            ["runId"] = run.RunId,
                            ["maxRepairAttempts"] = "2",
                            ["commitPolicy"] = "auto_commit"
                        }
                    },
                    session,
                    bible,
                    confirmed: true,
                    CancellationToken.None));

            Assert.Equal(0, kernel.GenerateDraftCallCount);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Empty(await verifyDb.ProductionEvents.ToListAsync());
            Assert.Empty(await verifyDb.Chapters.ToListAsync());
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_WithRequireUserReviewStopsBeforeCommitAfterPassingReview()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-user-review-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "用户审阅策略测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "生产第一章草稿，但提交前必须让我审阅。",
                CandidateDirections = { "第一章草稿生产后等待用户审阅" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "require_user_review"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("chapter_ready_for_user_review", result.Artifact?.ArtifactType);
            Assert.Contains("等待用户审阅", result.Message);
            Assert.Equal("ProduceChapter", result.RecommendedToolName);
            Assert.Equal("auto_commit", result.RecommendedArguments["commitPolicy"]);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Empty(await verifyDb.Chapters.ToListAsync());
            Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_quality_reviewed" &&
                e.Status == "completed");
            Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_committed");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_WithDraftOnlyStopsAfterGateWithoutReviewOrCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-produce-draft-only-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "草稿策略测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "只生成第一章草稿，不进入质量评审和提交。",
                CandidateDirections = { "第一章仅生成草稿" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("chapter_draft_ready", result.Artifact?.ArtifactType);
            Assert.Contains("draft_only", result.Message);
            Assert.Equal("ReviewChapter", result.RecommendedToolName);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            Assert.Empty(await verifyDb.Chapters.ToListAsync());
            var events = await verifyDb.ProductionEvents
                .Where(e => e.RuntimeRunId == run.RunId)
                .ToListAsync();
            Assert.Contains(events, e => e.EventType == "chapter_draft_generated");
            Assert.Contains(events, e => e.EventType == "chapter_gate_validated");
            Assert.Contains(events, e =>
                e.EventType == OutputArtifactRecorder.EventType &&
                e.ArtifactType == "chapter_draft_artifact" &&
                e.DataJson?.Contains("\"sourceEventType\":\"chapter_draft_generated\"", StringComparison.Ordinal) == true);
            Assert.Contains(events, e =>
                e.EventType == OutputArtifactRecorder.EventType &&
                e.ArtifactType == "generation_gate_report" &&
                e.DataJson?.Contains("\"sourceEventType\":\"chapter_gate_validated\"", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(events, e => e.EventType == "chapter_quality_reviewed");
            Assert.DoesNotContain(events, e => e.EventType == "chapter_committed");
            var draft = await verifyDb.ChapterDrafts.SingleAsync(item => item.RuntimeRunId == run.RunId);
            Assert.Equal("chapter-001", draft.ChapterId);
            Assert.Equal("draft_generated", draft.Status);
            Assert.True(draft.ContentLength > 0);
            Assert.True(draft.HasChanges);
            var gateReport = await verifyDb.GenerationGateReports.SingleAsync(item => item.RuntimeRunId == run.RunId);
            Assert.Equal("chapter-001", gateReport.ChapterId);
            Assert.Equal("validated", gateReport.Status);
            Assert.True(gateReport.ProtocolPassed);
            Assert.True(gateReport.ChangesDetected);

            var stateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryNovelProductionState",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["includeEvents"] = "true"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(stateResult.Success, stateResult.Message);
            Assert.Contains("章节草稿", stateResult.Message);
            var stateData = Assert.IsType<NovelProductionStateQueryResult>(stateResult.Data);
            var stateDraft = Assert.Single(stateData.ChapterDrafts);
            Assert.Equal(draft.ArtifactId, stateDraft.ArtifactId);
            Assert.Equal("draft_generated", stateDraft.Status);
            Assert.True(stateDraft.ContentLength > 0);
            var stateGateReport = Assert.Single(stateData.GenerationGateReports);
            Assert.Equal(gateReport.Id, stateGateReport.Id);
            Assert.Equal("validated", stateGateReport.Status);
            Assert.True(stateGateReport.ProtocolPassed);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepairChapterDraft_PersistsProductionEventOnExistingPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-repair-event-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "修复事件测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new RepairThenPassKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "修复第一章草稿。",
                CandidateDirections = { "第一章草稿按门禁失败项修复" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var repairResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "1",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(repairResult.Success, repairResult.Message);
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            var repairEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_draft_repaired");

            Assert.Equal(package.Id, repairEvent.PackageId);
            Assert.Equal(NovelAgentProductionStages.DraftRewritten, repairEvent.Stage);
            Assert.Contains(repairEvent.Status, new[] { "completed", "failed" });
            Assert.Contains("repairAttempt", repairEvent.DataJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReviewChapter_PersistsProductionEventOnExistingPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-review-event-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "评审事件测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "评审第一章草稿。",
                CandidateDirections = { "第一章草稿总编评审" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var reviewResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "require_user_review"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(reviewResult.Success, reviewResult.Message);
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            var reviewEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_quality_reviewed");

            Assert.Equal(package.Id, reviewEvent.PackageId);
            Assert.Equal(NovelAgentProductionStages.ReviewCompleted, reviewEvent.Stage);
            Assert.Equal("completed", reviewEvent.Status);
            Assert.Contains("qualityScore", reviewEvent.DataJson);
            var agentReview = await verifyDb.AgentReviews.SingleAsync(item => item.RuntimeRunId == run.RunId);
            Assert.Equal(package.Id, agentReview.PackageId);
            Assert.Equal("chapter-001", agentReview.ChapterId);
            Assert.Equal("Warning", agentReview.OverallResult);
            Assert.False(agentReview.RequiresRewrite);

            var stateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryNovelProductionState",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["includeEvents"] = "true"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(stateResult.Success, stateResult.Message);
            Assert.Contains("AgentReview", stateResult.Message);
            var stateData = Assert.IsType<NovelProductionStateQueryResult>(stateResult.Data);
            var stateReview = Assert.Single(stateData.AgentReviews);
            Assert.Equal(agentReview.Id, stateReview.Id);
            Assert.Equal("Warning", stateReview.OverallResult);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SelectChapterCandidate_AcceptsOrdinalPrefixFromModelArgument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-select-candidate-index-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "user-1",
            "project-1"
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-004",
                UserGoal = "第四章必须推进工会追踪者追捕、第二环门票和女子身份命运。",
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "打怪升级、邮路探索和硬事实连续性"
                },
                CandidateDirections =
                {
                    "工会追踪者追捕",
                    "第二环门票线索",
                    "女子身份命运"
                }
            }, CancellationToken.None);
            var expectedTitle = Assert.IsType<ChapterCreativeBrief>(run.ChapterBrief).Candidates[1].Title;

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "SelectChapterCandidate",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["candidateTitles"] = "【2】必须同时包含三条主线推进：工会追踪者追捕、第二环门票、女子身份命运"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            var selection = Assert.IsType<ChapterCandidateSelectionResult>(result.Data);
            Assert.Equal(expectedTitle, selection.Run!.ChapterBrief!.SelectedCandidateTitle);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToolPolicy_AllowsDiscoveredProjectResolutionTool()
    {
        var policy = new ToolPolicyEngine();
        var context = new AgentObservationContext
        {
            AvailableTools = new List<AgentToolDefinition>
            {
                new()
                {
                    Name = "ResolveNovelProject",
                    Risk = "Low"
                }
            }
        };

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ResolveNovelProject",
                Arguments = new Dictionary<string, string>
                {
                    ["mode"] = "bind_existing",
                    ["projectTitle"] = "天命"
                }
            },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            context,
            confirmed: false);

        Assert.True(result.AllowsExecution);
    }

    [Fact]
    public void ToolPolicy_AllowsKnowledgeProcessingTool()
    {
        var policy = new ToolPolicyEngine();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ProcessKnowledgeFile",
                Arguments = new Dictionary<string, string>
                {
                    ["taskId"] = "knowledge-task-1"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            ToolContext(new AgentToolDefinition
            {
                Name = "ProcessKnowledgeFile",
                Risk = "Medium",
                RequiresConfirmation = false
            }),
            confirmed: false);

        Assert.True(result.AllowsExecution);
        Assert.Equal("Medium", result.Risk);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public async Task ProcessKnowledgeFile_ReturnsStructuredKnowledgeIdsAndNextProductionTools()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-process-knowledge-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IKnowledgeProcessingService, RecordingKnowledgeProcessingService>();
        services.AddScoped<IKnowledgeClassificationService, KnowledgeClassificationService>();
        services.AddSingleton<IKnowledgeClassificationModelClient>(new ImportedKnowledgeClassificationModelClient());
        services.AddSingleton<ILogger<KnowledgeClassificationService>>(NullLogger<KnowledgeClassificationService>.Instance);
        services.AddScoped<IKnowledgeConflictDetector, KnowledgeConflictDetector>();
        services.AddSingleton<IKnowledgeConflictModelClient>(new NoConflictKnowledgeConflictModelClient());
        services.AddSingleton<ILogger<KnowledgeConflictDetector>>(NullLogger<KnowledgeConflictDetector>.Instance);
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "知识导入链路测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeProcessingTasks.Add(new TM.Web.NovelAgentWeb.Data.Entities.KnowledgeProcessingTask
            {
                Id = "knowledge-task-1",
                UserId = "user-1",
                ProjectId = "project-1",
                FileName = "旧邮路设定.md",
                FileSize = 1200,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel(provider.GetRequiredService<IGeneratedContentService>()));
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProcessKnowledgeFile",
                    Arguments = new Dictionary<string, string>
                    {
                        ["taskId"] = "knowledge-task-1"
                    }
                },
                session,
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains("提取 2 条知识", result.Message);
            Assert.Contains("ClassifyProjectKnowledge", result.Message);
            Assert.Contains("QueryProjectKnowledgeBindings", result.Message);
            Assert.Contains("DetectKnowledgeConflicts", result.Message);
            Assert.Contains("ProduceChapter", result.Message);
            var data = Assert.IsType<KnowledgeProcessingToolResult>(result.Data);
            Assert.Equal("knowledge-task-1", data.TaskId);
            Assert.Equal("project-1", data.ProjectId);
            Assert.Equal("completed", data.Status);
            Assert.Equal(2, data.ExtractedEntriesCount);
            Assert.Equal(new[] { "knowledge-route-cost", "knowledge-badge-boundary" }, data.KnowledgeIds);
            Assert.Equal(new[] { "knowledge-route-cost", "knowledge-badge-boundary" }, data.ImportedKnowledgeIds);
            Assert.Contains("ClassifyProjectKnowledge", data.NextRecommendedTools);
            Assert.Contains("QueryProjectKnowledgeBindings", data.NextRecommendedTools);
            Assert.Contains("DetectKnowledgeConflicts", data.NextRecommendedTools);
            Assert.Contains("ProduceChapter", data.NextRecommendedTools);
            Assert.Contains("分类项目知识", result.Suggestions);
            Assert.Contains("查询项目知识绑定", result.Suggestions);
            Assert.Contains("检查知识冲突", result.Suggestions);
            Assert.Contains("继续写章节", result.Suggestions);
            Assert.NotNull(result.Artifact);
            Assert.Equal("project-1", result.Artifact!.ProjectId);
            Assert.Contains("知识库", result.Artifact.UserVisibleWhere);
            Assert.Contains("创作工作流", result.Artifact.UserVisibleWhere);

            var firstKnowledgeId = data.KnowledgeIds[0];
            var classificationResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ClassifyProjectKnowledge",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = firstKnowledgeId
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(classificationResult.Success, classificationResult.Message);
            var classificationData = Assert.IsType<KnowledgeClassificationToolResult>(classificationResult.Data);
            Assert.Equal(firstKnowledgeId, classificationData.KnowledgeId);
            Assert.True(classificationData.ShouldEnterGate);
            Assert.True(classificationData.ShouldEnterBlueprint);
            Assert.True(classificationData.ShouldEnterFactSnapshot);

            var bindingsResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryProjectKnowledgeBindings"
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(bindingsResult.Success, bindingsResult.Message);
            Assert.Contains(firstKnowledgeId, bindingsResult.Message);
            Assert.Contains("Gate", bindingsResult.Message);
            Assert.Contains("FactSnapshot", bindingsResult.Message);

            var conflictResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "DetectKnowledgeConflicts",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = firstKnowledgeId
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(conflictResult.Success, conflictResult.Message);
            var conflictData = Assert.IsType<KnowledgeConflictDetectionToolResult>(conflictResult.Data);
            Assert.False(conflictData.HasConflict);
            Assert.False(conflictData.BlocksProduceChapter);
            Assert.Contains("ProduceChapter", conflictData.NextRecommendedTools);

            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写第一章并提交书城，必须使用知识库里的旧邮路记忆代价。",
                CandidateDirections = { "第一章：黑雨门槛与旧邮路代价" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "旧邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = run.RunId;

            var produceResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(produceResult.Success, produceResult.Message);
            await FinalizeChapterCommitMetadataOutboxAsync(provider);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var package = await verifyDb.TianmingPackages.SingleAsync(item => item.RuntimeRunId == run.RunId);
            Assert.True(JsonContainsText(package.KnowledgeSnapshotJson, firstKnowledgeId), package.KnowledgeSnapshotJson);
            Assert.True(JsonContainsText(package.KnowledgeSnapshotJson, "knowledgeBindingSummary"), package.KnowledgeSnapshotJson);
            var version = await verifyDb.ChapterVersions.SingleAsync(item => item.RuntimeRunId == run.RunId);
            var snapshot = await verifyDb.ProjectFactSnapshots.SingleAsync(item => item.ChapterVersionId == version.Id);
            Assert.True(JsonContainsText(snapshot.SnapshotJson, "knowledgeConstraintEvidence"), snapshot.SnapshotJson);
            Assert.True(JsonContainsText(snapshot.SnapshotJson, classificationData.ClassificationId), snapshot.SnapshotJson);
            var persistedChange = await verifyDb.ChapterChanges.SingleAsync(item => item.RuntimeRunId == run.RunId);
            Assert.True(persistedChange.AppliedToFactSnapshot);
            Assert.NotNull(persistedChange.AppliedAt);

            var contentResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryProjectContent",
                    Arguments = new Dictionary<string, string>
                    {
                        ["chapterNumber"] = "1",
                        ["includeBody"] = "false"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(contentResult.Success, contentResult.Message);
            Assert.Contains("CHANGES", contentResult.Message);
            var contentData = Assert.IsType<ProjectContentQueryResult>(contentResult.Data);
            var contentItem = Assert.Single(contentData.Items);
            var contentChange = Assert.Single(contentItem.ChapterChanges);
            Assert.Equal("parsed", contentChange.ParseStatus);
            Assert.True(contentChange.AppliedToFactSnapshot);
            Assert.Contains("NewPlotPoints", contentChange.CanonicalChangesJson);

            var stateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryNovelProductionState",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["includeEvents"] = "true"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(stateResult.Success, stateResult.Message);
            Assert.Contains("CHANGES", stateResult.Message);
            var stateData = Assert.IsType<NovelProductionStateQueryResult>(stateResult.Data);
            var stateChange = Assert.Single(stateData.ChapterChanges);
            Assert.Equal("parsed", stateChange.ParseStatus);
            Assert.True(stateChange.AppliedToFactSnapshot);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToolPolicy_AllowsProjectContentQueryTool()
    {
        var policy = new ToolPolicyEngine();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "QueryProjectContent",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "4",
                    ["includeBody"] = "true"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            ToolContext(new AgentToolDefinition { Name = "QueryProjectContent", Risk = "Low" }),
            confirmed: false);

        Assert.True(result.AllowsExecution);
        Assert.Equal("Low", result.Risk);
    }

    [Fact]
    public void ToolPolicy_AllowsNovelProductionStateQueryTool()
    {
        var policy = new ToolPolicyEngine();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "runtime-run-1"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            ToolContext(new AgentToolDefinition { Name = "QueryNovelProductionState", Risk = "Low" }),
            confirmed: false);

        Assert.True(result.AllowsExecution);
        Assert.Equal("Low", result.Risk);
    }

    [Fact]
    public void ToolPolicy_AllowsProduceChapterAsProductionClosedLoopTool()
    {
        var policy = new ToolPolicyEngine();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-1"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = "run-1"
            },
            new StoryBibleDocument(),
            ToolContext(new AgentToolDefinition
            {
                Name = "ProduceChapter",
                Risk = "High",
                RequiresConfirmation = true
            }),
            confirmed: false);

        Assert.True(result.AllowsExecution);
        Assert.Equal("High", result.Risk);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public void ToolPolicy_CommitWithoutQualityReviewRedirectsToReviewChapter()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveRunId = "run-1"
        };
        session.WorkingMemory.MissionPlan.BookTaskTree.Volumes.Add(new AgentVolumeTask
        {
            VolumeId = "volume-1",
            Chapters =
            {
                new AgentChapterTask
                {
                    ChapterId = "chapter-001",
                    RunId = "run-1",
                    Status = "pending_quality_review",
                    GateStatus = "validated",
                    QualityStatus = "pending_quality_review"
                }
            }
        });

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string> { ["runId"] = "run-1" }
            },
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = "run-1",
                        TargetChapterId = "chapter-001",
                        GateReport = new GenerationGateReport { Status = "validated" }
                    }
                }
            },
            new AgentObservationContext { MissionPlan = session.WorkingMemory.MissionPlan },
            confirmed: true);

        Assert.True(result.AllowsExecution);
        Assert.False(result.IsRepairable);
        Assert.Equal("High", result.Risk);
    }

    [Fact]
    public void ToolPolicy_BlocksCommittedChapterRegenerationWithoutRevisionIntent()
    {
        var policy = new ToolPolicyEngine();
        var session = BuildCommittedChapterPolicySession();
        var bible = BuildCommittedChapterPolicyBible();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string> { ["runId"] = "run-committed" }
            },
            session,
            bible,
            new AgentObservationContext
            {
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat },
                UserTurn = new UserTurnEnvelope { DialogueAct = DialogueAct.Chat },
                MissionPlan = session.WorkingMemory.MissionPlan
            },
            confirmed: true);

        Assert.False(result.AllowsExecution);
        Assert.Contains("已提交章节", result.Message);
    }

    [Fact]
    public void ToolPolicy_AllowsCommittedChapterRevisionWhenUserAskedToRevise()
    {
        var policy = new ToolPolicyEngine();
        var session = BuildCommittedChapterPolicySession();
        var bible = BuildCommittedChapterPolicyBible();

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterId"] = "chapter-006",
                    ["revisionGoal"] = "修掉银蓝邮徽越权问题",
                    ["auditRunId"] = "run-committed"
                }
            },
            session,
            bible,
            new AgentObservationContext
            {
                TurnIntent = new TurnIntent
                {
                    Type = TurnIntentType.FreeChat,
                    ReferencedChapterId = "chapter-006"
                },
                UserTurn = new UserTurnEnvelope
                {
                    DialogueAct = DialogueAct.Chat,
                    TargetArtifact = "chapter-006"
                },
                MissionPlan = session.WorkingMemory.MissionPlan
            },
            confirmed: true);

        Assert.True(result.AllowsExecution);
        Assert.Equal("High", result.Risk);
    }

    [Fact]
    public void ToolPolicy_AllowsCommittedChapterRevisionAfterModelReadsSameRunContent()
    {
        var policy = new ToolPolicyEngine();
        var session = BuildCommittedChapterPolicySession();
        var bible = BuildCommittedChapterPolicyBible();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat },
            UserTurn = new UserTurnEnvelope { DialogueAct = DialogueAct.Chat },
            MissionPlan = session.WorkingMemory.MissionPlan
        };
        context.RecentObservations.Add(new AgentRuntimeObservation
        {
            ToolName = "QueryProjectContent",
            Success = true,
            RunId = "run-committed",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_content_query",
                ArtifactId = "chapter-006",
                RunId = "run-committed",
                NextHints = new[]
                {
                    "基于该章节工作流 Run 重新生成修订稿",
                    "执行硬门禁校验",
                    "执行质量评审",
                    "重新提交书城"
                }
            }
        });

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterId"] = "chapter-006",
                    ["revisionGoal"] = "基于刚读取的正文重新修订",
                    ["auditRunId"] = "run-committed"
                }
            },
            session,
            bible,
            context,
            confirmed: true);

        Assert.True(result.AllowsExecution);
    }

    [Fact]
    public async Task ToolSearchAll_ReturnsUserSafeSummaryAndStructuredToolData()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["phase"] = "All"
                }
            },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("已准备", result.Message);
        Assert.Contains("创作能力", result.Message);
        Assert.DoesNotContain("QueryWorkspaceState", result.Message);
        Assert.Contains("ResolveNovelProject", JsonSerializer.Serialize(result.Data));
        Assert.DoesNotContain("StartNewNovelProject", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PassesToolSemanticArtifactContractToLedger()
    {
        var services = new ServiceCollection();
        var ledger = new RecordingAgentToolExecutionLedger();
        services.AddSingleton<IAgentToolExecutionLedger>(ledger);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "有哪些工具能查询生产状态"
                }
            },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        var start = Assert.Single(ledger.Starts);
        Assert.NotNull(start.SemanticContract);
        Assert.Equal("工具语义检索", start.SemanticContract!.DisplayName);
        Assert.Contains("tool_search_query", start.SemanticContract.InputArtifacts);
        Assert.Contains("tool_semantic_cards", start.SemanticContract.OutputArtifacts);
        Assert.Contains("tool_search", start.SemanticContract.IdempotencyPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("business", start.SemanticContract.RollbackPolicy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ProduceChapterBlocksWhenRequiredRunArtifactIsMissing()
    {
        var services = new ServiceCollection();
        var ledger = new RecordingAgentToolExecutionLedger();
        services.AddSingleton<IAgentToolExecutionLedger>(ledger);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>()
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("chapter_plan_run", result.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_MISSING", result.Failure?.Code);
        Assert.Contains("chapter_plan_run", result.Message);
        Assert.Equal("ProduceChapter", result.RecommendedToolName);
        Assert.Single(ledger.Starts);
    }

    [Fact]
    public async Task ExecuteAsync_ProduceChapterBlocksWhenRunPackageIsStaleWithoutRevisionPlan()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        var ledger = new RecordingAgentToolExecutionLedger();
        services.AddSingleton<IAgentToolExecutionLedger>(ledger);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddDbContext<NovelAgentDbContext>(options => options.UseSqlite(connection));
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "过期包测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.TianmingPackages.Add(new TianmingPackage
            {
                Id = "pkg-stale-1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-002",
                RuntimeRunId = "run-stale",
                PackageKind = "chapter_generation",
                Status = "stale",
                InputJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-stale"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = "run-stale",
                        Intent = NovelAgentIntent.PlanChapter,
                        TargetChapterId = "chapter-002"
                    }
                }
            },
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("revision_plan_optional", result.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_STALE", result.Failure?.Code);
        Assert.Contains("pkg-stale-1", result.Message);
        Assert.Contains("revisionPlanId", result.Message);
        Assert.Contains("stale", JsonSerializer.Serialize(result.Data));
        Assert.Equal("ProduceChapter", result.RecommendedToolName);
        Assert.Single(ledger.Starts);
    }

    [Fact]
    public async Task ExecuteAsync_ProduceChapterBlocksWhenPreviousPostCommitOutboxIsPending()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        var ledger = new RecordingAgentToolExecutionLedger();
        services.AddSingleton<IAgentToolExecutionLedger>(ledger);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddDbContext<NovelAgentDbContext>(options => options.UseSqlite(connection));
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "outbox 阻断测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.AddRange(
                new ChapterEntity
                {
                    Id = "project-1-chapter-001",
                    ProjectId = "project-1",
                    Title = "第一章",
                    ChapterNumber = 1,
                    Status = "committed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChapterEntity
                {
                    Id = "project-1-chapter-002",
                    ProjectId = "project-1",
                    Title = "第二章",
                    ChapterNumber = 2,
                    Status = "planned",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.OutboxEvents.Add(new OutboxEventEntity
            {
                Id = "outbox-finalize-001",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "run-001",
                EventType = "finalize_chapter_commit_metadata",
                AggregateType = "chapter",
                AggregateId = "project-1-chapter-001",
                Status = "pending",
                PayloadJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-next"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = "run-next",
                        Intent = NovelAgentIntent.PlanChapter,
                        TargetChapterId = "chapter-002"
                    }
                }
            },
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("previous_chapter_post_commit_outbox", result.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_BLOCKED", result.Failure?.Code);
        Assert.Contains("outbox-finalize-001", result.Message);
        Assert.Contains("post_commit_outbox", JsonSerializer.Serialize(result.Data));
        Assert.Equal("ProduceChapter", result.RecommendedToolName);
        Assert.Single(ledger.Starts);
    }

    [Theory]
    [InlineData("继续写第二章并使用知识库设定", "ProduceChapter", "SearchCreativeKnowledge")]
    [InlineData("现在执行到哪了，卡在哪个生产阶段", "QueryNovelProductionState", "QueryProjectContent")]
    public async Task ToolSearch_RanksChineseNaturalLanguageAgainstSemanticCards(
        string query,
        string expectedPrimaryTool,
        string expectedRelatedTool)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["limit"] = "8"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        var serialized = JsonSerializer.Serialize(result.Data);
        Assert.Contains(expectedPrimaryTool, serialized);
        Assert.Contains(expectedRelatedTool, serialized);
        Assert.Contains("Matches", serialized);
        Assert.Contains("RelevanceReason", serialized);
        using var document = JsonDocument.Parse(serialized);
        var matches = document.RootElement.GetProperty("Matches").EnumerateArray().ToList();
        Assert.NotEmpty(matches);
        Assert.All(matches, match =>
            Assert.False(string.IsNullOrWhiteSpace(match.GetProperty("RelevanceReason").GetString())));
    }

    [Fact]
    public async Task ToolSearch_PhaseHintDoesNotHideLongTailTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["phase"] = "Planning",
                    ["query"] = "读取第4章正文和所属卷标题",
                    ["limit"] = "8"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        var serialized = JsonSerializer.Serialize(result.Data);
        Assert.Contains("QueryProjectContent", serialized);
        Assert.Contains("QueryNovelProductionState", serialized);
    }

    [Fact]
    public async Task ToolSearch_DoesNotExposeRelatedToolsAsRequiredNextRoute()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(services),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "第二章重写并查看已有正文",
                    ["limit"] = "8"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        var serialized = JsonSerializer.Serialize(result.Data);
        using var document = JsonDocument.Parse(serialized);
        var toolDetails = string.Join("\n", document.RootElement
            .GetProperty("ToolDetails")
            .EnumerateArray()
            .Select(item => item.GetString()));
        var relevanceReasons = string.Join("\n", document.RootElement
            .GetProperty("Matches")
            .EnumerateArray()
            .Select(item => item.GetProperty("RelevanceReason").GetString()));
        Assert.DoesNotContain("后续=", toolDetails);
        Assert.DoesNotContain("后续可衔接", relevanceReasons);
        Assert.Contains("可搭配", toolDetails);
    }

    [Fact]
    public void ToolSearchScopeKey_UsesStableHashForPersistentCacheLookup()
    {
        var method = typeof(AgentToolRegistry).GetMethod(
            "BuildToolSearchScopeKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var key = Assert.IsType<string>(method!.Invoke(null, new object[]
        {
            "读取第4章正文",
            "",
            "",
            "Planning",
            false
        }));
        var normalized = "phaseHint=Planning|includeAll=False|query=读取第4章正文|intent=|context=";
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 8)
            .ToLowerInvariant();

        Assert.Equal($"global:Planning:{expectedHash}", key);
    }

    [Fact]
    public async Task QueryWorkspaceState_ProjectlessAdminReadsVisibleWorkspaceWithoutBindingProject()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IWorkspaceStateQueryService, WorkspaceStateQueryService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.AddRange(
                new UserEntity
                {
                    Id = "admin-1",
                    Username = "admin",
                    Email = "admin@example.com",
                    PasswordHash = "hash",
                    Role = "admin"
                },
                new UserEntity
                {
                    Id = "user-1",
                    Username = "author",
                    Email = "author@example.com",
                    PasswordHash = "hash",
                    Role = "author"
                });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "天命旧书",
                Genre = "玄幻",
                Status = "draft",
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new VolumeEntity
            {
                Id = "volume-1",
                ProjectId = "project-1",
                Title = "第一卷",
                VolumeNumber = 1
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed"
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-1",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "爽点节奏",
                Content = "三段式推进"
            });
            db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsageEntity
            {
                Id = "usage-knowledge-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-1",
                Status = "referenced",
                UsageCount = 2,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                Role = "PacingRule",
                Scope = "ProjectWide",
                Priority = 70,
                UsedByChaptersJson = "[\"chapter-1\"]",
                FirstSeenAt = DateTime.UtcNow.AddHours(-1),
                LastUsedAt = DateTime.UtcNow
            });
            db.KnowledgeConflictReports.Add(new KnowledgeConflictReportEntity
            {
                Id = "conflict-workspace-1",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-1",
                ConflictingKnowledgeIdsJson = "[]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "爽点节奏与慢热叙事要求冲突。",
                RecommendedAction = "询问用户选择节奏方向。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3)
            });
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "snapshot-knowledge-1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-1",
                VersionNumber = 1,
                Source = "chapter_commit",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                SnapshotJson = """
                {
                  "chapterId": "chapter-1",
                  "knowledgeConstraintEvidence": [
                    {
                      "knowledgeId": "knowledge-1",
                      "title": "爽点节奏",
                      "entryType": "HardFact",
                      "constraintLevel": "HardConstraint",
                      "packagePolicy": "DefaultEveryChapter",
                      "gateStatus": "validated",
                      "evidenceStatus": "satisfied"
                    }
                  ]
                }
                """
            });
            db.AgentRuns.Add(new AgentRunEntity
            {
                Id = "run-1",
                UserId = "user-1",
                ProjectId = "project-1",
                RunType = "chapter_generation",
                Status = "running",
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "admin-1",
            SessionId = "session-1",
            ActiveProjectId = string.Empty
        };
        session.WorkingMemory.AuthorMemory.DisplayName = "lyston";

        var result = await registry.ExecuteAsync(
            new AgentToolCall { Name = "QueryWorkspaceState" },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, session.ActiveProjectId);
        var state = Assert.IsType<AgentWorkspaceState>(result.Data);
        Assert.Equal(1, state.ProjectTotalCount);
        Assert.Equal(1, state.ProjectPreviewCount);
        var project = Assert.Single(state.VisibleProjects);
        Assert.Equal("天命旧书", project.Title);
        Assert.Equal("user-1", project.OwnerUserId);
        Assert.False(project.IsOwnedByCurrentUser);
        Assert.Equal(1, project.ChapterCount);
        Assert.Equal(1, project.CommittedChapterCount);
        Assert.Equal(1, state.KnowledgeBase.TotalCount);
        Assert.Contains(state.KnowledgeBase.RecentlyUsedBindings, binding => binding.KnowledgeId == "knowledge-1");
        Assert.Contains(state.KnowledgeBase.RecentConflictReports, report => report.ConflictId == "conflict-workspace-1");
        Assert.Equal(1, state.Workflow.ActiveRunCount);
        Assert.Equal("lyston", state.AuthorProfile.DisplayName);
        Assert.Contains("小说书城当前可见项目共 1 本", result.Message);
        Assert.Contains("最近实际用于章节的知识", result.Message);
        Assert.Contains("最近知识约束证据", result.Message);
        Assert.Contains("知识冲突报告", result.Message);
        Assert.Contains("conflict-workspace-1", result.Message);
        Assert.Contains("爽点节奏", result.Message);
        Assert.Contains("satisfied", result.Message);
        Assert.Contains("HardConstraint", result.Message);
        Assert.Contains("作者称呼：lyston", result.Message);
        Assert.Contains("不会自动绑定", string.Join("\n", state.Notes));
    }

    [Fact]
    public async Task QueryProjectContent_ReturnsVolumeChapterBodyAndContinuityFacts()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "连续性测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new VolumeEntity
            {
                Id = "volume-1",
                ProjectId = "project-1",
                Title = "第一卷：黑雨觉醒",
                VolumeNumber = 1
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-004",
                ProjectId = "project-1",
                VolumeId = "volume-1",
                Title = "第四章：地下室反击",
                ChapterNumber = 4,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var document = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-004",
                "chapter_body",
                "第四章：地下室反击",
                "第四章：地下室反击\n陈默握着旧扳手，在维修站地下室迎向黑潮异兽。",
                CancellationToken.None);
            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-004");
            chapter.CurrentDocumentId = document.Id;
            await db.SaveChangesAsync();

            var truthStore = new ProductionTruthStore(db);
            var package = await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
                Id: "pkg-chapter-004",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-004",
                RuntimeRunId: "run-chapter-004",
                PackageKind: "chapter_generation",
                InputJson: "{\"goal\":\"地下室反击\"}",
                DependencyVersionsJson: "{\"storyBible\":2,\"blueprint\":1}",
                KnowledgeSnapshotJson: """
                    {
                      "knowledgeBindings": [
                        {
                          "knowledgeId": "kb-black-rain",
                          "title": "黑雨规则",
                          "entryType": "HardFact",
                          "content": "黑雨会腐蚀暴露在外的记忆标签。",
                          "projectUsageStatus": "referenced",
                          "projectUsageCount": 3,
                          "role": "WorldRule",
                          "scope": "ProjectWide",
                          "priority": 80,
                          "constraintLevel": "HardConstraint",
                          "packagePolicy": "DefaultEveryChapter",
                          "boundVersion": "kb-black-rain-v1",
                          "usedByChapters": ["project-1-chapter-004"]
                        }
                      ],
                      "sourceRevisionPlans": [
                        {
                          "revisionPlanId": "revision-plan-004",
                          "planType": "chapter_rewrite",
                          "targetScope": "chapter",
                          "targetChapterId": "project-1-chapter-004",
                          "status": "ready_for_rebuild",
                          "riskLevel": "medium",
                          "recommendation": "把第四章改成地下室反击并保留黑雨硬约束。"
                        }
                      ],
                      "rebuiltFromPackageIds": ["pkg-chapter-004-old", "pkg-chapter-004-v1"]
                    }
                    """,
                FactSnapshotJson: "{\"state\":\"地下室攻防\"}",
                PromptVersion: "chapter-v1",
                KernelVersion: "tianming-kernel-v1"));
            var version = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-004",
                ContentDocumentId: document.Id,
                Title: "第四章：地下室反击",
                WordCount: 28,
                Status: "committed",
                RuntimeRunId: "run-chapter-004",
                PackageId: package.Id,
                GateReportJson: "{\"status\":\"validated\"}",
                AgentReviewJson: "{\"decision\":\"commit\"}"));
            await truthStore.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-004",
                ChapterVersionId: version.Id,
                SnapshotJson: """
                    {
                      "protagonist": "陈默",
                      "endingState": "陈默守住地下室入口",
                      "next": "继续承接地下室入口攻防",
                      "sourceRevisionPlans": [
                        {
                          "revisionPlanId": "revision-plan-004",
                          "planType": "chapter_rewrite",
                          "targetScope": "chapter",
                          "targetChapterId": "project-1-chapter-004",
                          "status": "executed",
                          "riskLevel": "medium",
                          "recommendation": "把第四章改成地下室反击并保留黑雨硬约束。"
                        }
                      ]
                    }
                    """,
                Source: "chapter_commit"));
            await truthStore.AppendEventAsync(new CreateProductionEventRequest(
                RuntimeRunId: "run-chapter-004",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-004",
                PackageId: package.Id,
                EventType: "kernel_gate",
                Stage: "KernelGate",
                Status: "completed",
                Message: "门禁通过",
                ArtifactType: "GateReport",
                ArtifactId: "gate-chapter-004",
                DataJson: "{\"score\":92}"));
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "run-chapter-004",
                    TargetChapterId = "chapter-004",
                    UpdatedAt = DateTime.UtcNow
                }
            },
            ContinuityFacts =
            {
                new ChapterContinuityFacts
                {
                    ChapterId = "chapter-004",
                    ProtagonistName = "陈默",
                    EndingState = "陈默守住地下室入口",
                    NextChapterMustCarry = { "继续承接地下室入口攻防" }
                }
            }
        };

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryProjectContent",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "4",
                    ["includeBody"] = "true"
                }
            },
            session,
            bible,
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("第四章：地下室反击", result.Message);
        Assert.Contains("第一卷：黑雨觉醒", result.Message);
        Assert.Contains("工作流 Run：run-chapter-004", result.Message);
        Assert.Contains("陈默握着旧扳手", result.Message);
        Assert.Contains("继续承接地下室入口攻防", result.Message);
        Assert.Contains("当前版本：v1", result.Message);
        Assert.Contains("生产包：pkg-chapter-004", result.Message);
        Assert.Contains("本章使用知识", result.Message);
        Assert.Contains("黑雨规则", result.Message);
        Assert.Contains("HardConstraint", result.Message);
        Assert.Contains("来源修订计划", result.Message);
        Assert.Contains("revision-plan-004", result.Message);
        Assert.Contains("把第四章改成地下室反击", result.Message);
        Assert.Contains("替代旧包", result.Message);
        Assert.Contains("pkg-chapter-004-old", result.Message);
        Assert.Contains("事实快照", result.Message);
        Assert.Contains("门禁通过", result.Message);
        Assert.Equal("run-chapter-004", result.Artifact?.RunId);

        var data = Assert.IsType<ProjectContentQueryResult>(result.Data);
        var item = Assert.Single(data.Items);
        Assert.Equal("run-chapter-004", item.SourceRunId);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.Equal("pkg-chapter-004", item.PackageId);
        Assert.Equal(new[] { "pkg-chapter-004-old", "pkg-chapter-004-v1" }, item.RebuiltFromPackageIds);
        Assert.Contains(item.KnowledgeBindings, binding => binding.KnowledgeId == "kb-black-rain");
        var sourceRevisionPlan = Assert.Single(item.SourceRevisionPlans);
        Assert.Equal("revision-plan-004", sourceRevisionPlan.RevisionPlanId);
        Assert.Equal("executed", sourceRevisionPlan.Status);
        Assert.Contains("陈默守住地下室入口", item.FactSnapshotJson);
        Assert.Contains(item.ProductionEvents, evt => evt.Stage == "KernelGate" && evt.Status == "completed");

        var canonicalIdResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryProjectContent",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterId"] = "chapter-004",
                    ["includeBody"] = "true"
                }
            },
            session,
            bible,
            confirmed: true,
            CancellationToken.None);

        Assert.True(canonicalIdResult.Success);
        Assert.Contains("第四章：地下室反击", canonicalIdResult.Message);

        var schema = registry.ListToolSchemas().Single(tool => tool.Name == "QueryProjectContent");
        Assert.Contains("chapters", schema.Semantic.ReadsFrom);
        Assert.Contains("chapter_versions", schema.Semantic.ReadsFrom);
        Assert.Contains("project_fact_snapshots", schema.Semantic.ReadsFrom);
        Assert.Contains("production_events", schema.Semantic.ReadsFrom);
        Assert.Contains("tianming_packages", schema.Semantic.ReadsFrom);
        Assert.Contains("none_read_only", schema.Semantic.WritesTo);
    }

    [Fact]
    public async Task QueryChapterVersions_IsDiscoverableAndReturnsVersionLineage()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionWorkflowBridge, ProductionWorkflowBridge>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<IChapterService, ChapterService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterService>>(NullLogger<ChapterService>.Instance);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            var truthStore = scope.ServiceProvider.GetRequiredService<IProductionTruthStore>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "版本测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：旧邮车重启",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-002");
            var firstDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                chapter.Id,
                "chapter_body",
                "第二章：旧邮车重启",
                "第二章：旧邮车重启\n沈砚第一次看见旧邮车亮灯。",
                CancellationToken.None);
            chapter.CurrentDocumentId = firstDocument.Id;
            chapter.WordCount = 22;
            await db.SaveChangesAsync();
            await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: chapter.Id,
                ContentDocumentId: firstDocument.Id,
                Title: "第二章：旧邮车重启",
                WordCount: 22,
                Status: "committed",
                RuntimeRunId: "run-chapter-002-v1",
                PackageId: "pkg-chapter-002-v1",
                GateReportJson: "{\"status\":\"passed\"}",
                AgentReviewJson: "{\"decision\":\"accept\"}"));

            var secondDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                chapter.Id,
                "chapter_body",
                "第二章：旧邮车重启",
                "第二章：旧邮车重启\n沈砚看见旧邮车重新亮灯，黑雨里的邮徽开始发热。",
                CancellationToken.None);
            chapter.CurrentDocumentId = secondDocument.Id;
            chapter.WordCount = 34;
            await db.SaveChangesAsync();
            db.TianmingPackages.Add(new TianmingPackage
            {
                Id = "pkg-chapter-002-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = chapter.Id,
                RuntimeRunId = "run-chapter-002-v2",
                Status = "completed",
                PackageKind = "chapter_generation",
                KernelVersion = "tianming-kernel-v1",
                PromptVersion = "chapter-v1",
                KnowledgeSnapshotJson = """
                    {
                      "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                    }
                    """
            });
            await db.SaveChangesAsync();
            await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: chapter.Id,
                ContentDocumentId: secondDocument.Id,
                Title: "第二章：旧邮车重启",
                WordCount: 34,
                Status: "committed",
                RuntimeRunId: "run-chapter-002-v2",
                PackageId: "pkg-chapter-002-v2",
                GateReportJson: "{\"status\":\"passed\"}",
                AgentReviewJson: "{\"decision\":\"accept\"}"));
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        var schema = registry.ListToolSchemas().Single(tool => tool.Name == "QueryChapterVersions");
        Assert.Equal("Low", schema.Risk);
        Assert.False(schema.RequiresConfirmation);
        Assert.Contains("chapterId", schema.Parameters.Keys);
        Assert.Contains("chapterNumber", schema.Parameters.Keys);
        Assert.Contains("chapter_versions", schema.Semantic.ReadsFrom);
        Assert.Contains("tianming_packages", schema.Semantic.ReadsFrom);
        Assert.Contains("none_read_only", schema.Semantic.WritesTo);

        var search = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "章节版本 回滚 旧包",
                    ["intent"] = "查看章节历史版本和生产来源",
                    ["limit"] = "8"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);
        Assert.True(search.Success);
        var searchTools = (string[])search.Data!.GetType().GetProperty("Tools")!.GetValue(search.Data)!;
        Assert.Contains("QueryChapterVersions", searchTools);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryChapterVersions",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "2"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("章节版本", result.Message);
        Assert.Contains("v2", result.Message);
        Assert.Contains("当前", result.Message);
        Assert.Contains("pkg-chapter-002-v2", result.Message);
        Assert.Contains("tianming-kernel-v1", result.Message);
        Assert.Contains("替代旧包", result.Message);
        Assert.Contains("pkg-chapter-002-v1", result.Message);
        Assert.Contains("旧邮车重新亮灯", result.Message);

        var versions = Assert.IsAssignableFrom<List<ChapterVersionResponse>>(result.Data);
        Assert.Equal(new[] { 2, 1 }, versions.Select(version => version.VersionNumber).ToArray());
        Assert.True(versions[0].IsCurrent);
        Assert.Equal("pkg-chapter-002-v2", versions[0].PackageId);
        Assert.Equal(new[] { "pkg-chapter-002-v1" }, versions[0].RebuiltFromPackageIds);
    }

    [Fact]
    public async Task CompareChapterVersions_IsDiscoverableAndReturnsDiff()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionWorkflowBridge, ProductionWorkflowBridge>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<IChapterService, ChapterService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterService>>(NullLogger<ChapterService>.Instance);
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);
        string leftVersionId;
        string rightVersionId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            var truthStore = scope.ServiceProvider.GetRequiredService<IProductionTruthStore>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "版本对比测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：旧邮车重启",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-002");
            var firstDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                chapter.Id,
                "chapter_body",
                "第二章：旧邮车重启",
                "第二章：旧邮车重启\n沈砚第一次看见旧邮车亮灯。\n黑雨刚刚落下。",
                CancellationToken.None);
            chapter.CurrentDocumentId = firstDocument.Id;
            await db.SaveChangesAsync();
            var firstVersion = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: chapter.Id,
                ContentDocumentId: firstDocument.Id,
                Title: "第二章：旧邮车重启",
                WordCount: 28,
                Status: "committed",
                RuntimeRunId: "run-chapter-002-v1",
                PackageId: "pkg-chapter-002-v1",
                GateReportJson: "{\"status\":\"passed\"}",
                AgentReviewJson: "{\"decision\":\"accept\"}"));

            var secondDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                chapter.Id,
                "chapter_body",
                "第二章：旧邮车重启",
                "第二章：旧邮车重启\n沈砚第一次看见旧邮车亮灯。\n黑雨里，邮徽开始发热。",
                CancellationToken.None);
            chapter.CurrentDocumentId = secondDocument.Id;
            await db.SaveChangesAsync();
            db.TianmingPackages.Add(new TianmingPackage
            {
                Id = "pkg-chapter-002-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = chapter.Id,
                RuntimeRunId = "run-chapter-002-v2",
                Status = "completed",
                PackageKind = "chapter_generation",
                KernelVersion = "tianming-kernel-v1",
                PromptVersion = "chapter-v1",
                KnowledgeSnapshotJson = """
                    {
                      "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                    }
                    """
            });
            await db.SaveChangesAsync();
            var secondVersion = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: chapter.Id,
                ContentDocumentId: secondDocument.Id,
                Title: "第二章：旧邮车重启",
                WordCount: 30,
                Status: "committed",
                RuntimeRunId: "run-chapter-002-v2",
                PackageId: "pkg-chapter-002-v2",
                GateReportJson: "{\"status\":\"passed\"}",
                AgentReviewJson: "{\"decision\":\"accept\"}"));
            leftVersionId = firstVersion.Id;
            rightVersionId = secondVersion.Id;
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        var schema = registry.ListToolSchemas().Single(tool => tool.Name == "CompareChapterVersions");
        Assert.Equal("Low", schema.Risk);
        Assert.False(schema.RequiresConfirmation);
        Assert.Contains("chapterId", schema.Parameters.Keys);
        Assert.Contains("leftVersionId", schema.Parameters.Keys);
        Assert.Contains("rightVersionId", schema.Parameters.Keys);
        Assert.Contains("chapter_versions", schema.Semantic.ReadsFrom);
        Assert.Contains("content_chunks", schema.Semantic.ReadsFrom);
        Assert.Contains("none_read_only", schema.Semantic.WritesTo);

        var search = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "对比 章节版本 差异",
                    ["intent"] = "比较两个章节版本正文变化",
                    ["limit"] = "8"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);
        Assert.True(search.Success);
        var searchTools = (string[])search.Data!.GetType().GetProperty("Tools")!.GetValue(search.Data)!;
        Assert.Contains("CompareChapterVersions", searchTools);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "CompareChapterVersions",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "2",
                    ["leftVersionId"] = leftVersionId,
                    ["rightVersionId"] = rightVersionId
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("版本差异", result.Message);
        Assert.Contains("v1 -> v2", result.Message);
        Assert.Contains("黑雨刚刚落下", result.Message);
        Assert.Contains("邮徽开始发热", result.Message);
        Assert.IsType<ChapterVersionCompareResponse>(result.Data);
    }

    [Fact]
    public async Task RollbackChapterVersion_RequiresConfirmationAndSwitchesCurrentVersion()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IChapterVersionRollbackService, ChapterVersionRollbackService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "回滚工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.AddRange(
                new ChapterEntity
                {
                    Id = "chapter-001",
                    ProjectId = "project-1",
                    Title = "第一章：旧邮徽重写版",
                    ChapterNumber = 1,
                    WordCount = 3600,
                    CurrentDocumentId = "doc-v2",
                    Status = "committed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChapterEntity
                {
                    Id = "chapter-002",
                    ProjectId = "project-1",
                    Title = "第二章：邮车亮灯",
                    ChapterNumber = 2,
                    Status = "committed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.ContentDocuments.AddRange(
                ChapterDocument("doc-v1", "archived", "第一版正文：沈砚捡起银蓝邮徽。"),
                ChapterDocument("doc-v2", "active", "第二版正文：沈砚直接开走旧邮车。"));
            db.ContentChunks.AddRange(
                ChapterChunk("doc-v1", "第一章：旧邮徽\n第一版正文：沈砚捡起银蓝邮徽。"),
                ChapterChunk("doc-v2", "第一章：旧邮徽\n第二版正文：沈砚直接开走旧邮车。"));
            db.ChapterVersions.AddRange(
                ChapterVersion("version-1", 1, "doc-v1", "pkg-chapter-001-v1", "run-chapter-001-v1"),
                ChapterVersion("version-2", 2, "doc-v2", "pkg-chapter-001-v2", "run-chapter-001-v2"));
            db.TianmingPackages.AddRange(
                Package("pkg-chapter-001-v1", "chapter-001", "run-chapter-001-v1"),
                Package("pkg-chapter-001-v2", "chapter-001", "run-chapter-001-v2"),
                Package("pkg-chapter-002-v1", "chapter-002", "run-chapter-002-v1"));
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        var schema = registry.ListToolSchemas().Single(tool => tool.Name == "RollbackChapterVersion");
        Assert.Equal("High", schema.Risk);
        Assert.True(schema.RequiresConfirmation);
        Assert.Contains("targetVersionId", schema.Parameters.Keys);
        Assert.Contains("chapter_versions", schema.Semantic.ReadsFrom);
        Assert.Contains("chapters", schema.Semantic.WritesTo);
        Assert.Contains("tianming_packages", schema.Semantic.WritesTo);

        var unconfirmed = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "RollbackChapterVersion",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterId"] = "chapter-001",
                    ["targetVersionId"] = "version-1",
                    ["reason"] = "用户想回到第一版。"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: false,
            CancellationToken.None);
        Assert.False(unconfirmed.Success);
        Assert.Contains("需要用户确认", unconfirmed.Message);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "RollbackChapterVersion",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterId"] = "chapter-001",
                    ["targetVersionId"] = "version-1",
                    ["reason"] = "用户想回到第一版。"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("已回滚", result.Message);
        var rollback = Assert.IsType<RollbackChapterVersionResult>(result.Data);
        Assert.Equal("version-1", rollback.CurrentVersionId);
        Assert.Contains("pkg-chapter-002-v1", rollback.InvalidatedPackageIds);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(item => item.Id == "chapter-001");
        Assert.Equal("doc-v1", chapter.CurrentDocumentId);
        Assert.Equal("stale", (await verifyDb.TianmingPackages.SingleAsync(package => package.Id == "pkg-chapter-001-v2")).Status);
        var outputArtifact = await verifyDb.ProductionEvents.SingleAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "chapter_version_rollback" &&
            evt.ArtifactId == "version-1");
        Assert.Contains("小说书城", outputArtifact.DataJson);
        Assert.Contains("RollbackChapterVersion", outputArtifact.DataJson);
    }

    private static ContentDocument ChapterDocument(string id, string status, string content) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        SourceType = "chapter",
        SourceId = "chapter-001",
        DocumentRole = "chapter_body",
        Title = "第一章：旧邮徽",
        MimeType = "text/plain",
        ContentHash = id,
        Version = id.EndsWith("v1", StringComparison.OrdinalIgnoreCase) ? 1 : 2,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ContentChunk ChapterChunk(string documentId, string content) => new()
    {
        Id = $"chunk-{documentId}",
        DocumentId = documentId,
        ChunkIndex = 0,
        ChunkText = content,
        TokenCount = content.Length,
        CharStart = 0,
        CharEnd = content.Length,
        ContentHash = $"chunk-{documentId}"
    };

    private static ChapterVersion ChapterVersion(
        string id,
        int versionNumber,
        string documentId,
        string packageId,
        string runId) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        ChapterId = "chapter-001",
        ContentDocumentId = documentId,
        VersionNumber = versionNumber,
        Title = versionNumber == 1 ? "第一章：旧邮徽" : "第一章：旧邮徽重写版",
        WordCount = versionNumber == 1 ? 3200 : 3600,
        Status = "committed",
        RuntimeRunId = runId,
        PackageId = packageId,
        GateReportJson = "{\"status\":\"passed\"}",
        AgentReviewJson = "{\"decision\":\"accept\"}",
        CreatedAt = DateTime.UtcNow.AddMinutes(versionNumber)
    };

    private static TianmingPackage Package(string id, string chapterId, string runtimeRunId) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        ChapterId = chapterId,
        RuntimeRunId = runtimeRunId,
        PackageKind = "chapter_generation",
        Status = "completed",
        InputJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task CreativeIntentTools_CreateDecideAndQueryProjectScopedIntents()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.AddRange(
                new NovelProjectEntity
                {
                    Id = "project-1",
                    UserId = "user-1",
                    Title = "创意收件箱测试书",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new NovelProjectEntity
                {
                    Id = "project-2",
                    UserId = "user-1",
                    Title = "隔离项目",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.CreativeIntents.Add(new CreativeIntentEntity
            {
                Id = "other-project-intent",
                UserId = "user-1",
                ProjectId = "project-2",
                SessionId = "session-other",
                Source = "chat",
                RawContent = "别的项目要写宫廷权谋",
                NormalizedIntent = "别的项目要写宫廷权谋",
                TargetScope = "project",
                Status = "accepted",
                ImpactLevel = "mainline_change",
                ConflictStatus = "none",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-chapter-002"
        };

        var createResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "CreateCreativeIntent",
                Arguments = new Dictionary<string, string>
                {
                    ["rawContent"] = "第二章别写情绪拉扯，改成怪物围攻和打怪升级。",
                    ["normalizedIntent"] = "第二章主冲突改为怪物围攻，男主通过银蓝邮徽识别逃生路线，不推进恋爱。",
                    ["source"] = "chat",
                    ["targetScope"] = "chapter",
                    ["targetChapterId"] = "chapter-002",
                    ["impactLevel"] = "chapter_rewrite",
                    ["conflictStatus"] = "none",
                    ["metadataJson"] = "{\"from\":\"unit-test\"}"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(createResult.Success);
        var created = Assert.IsType<CreativeIntentItem>(createResult.Data);
        Assert.Equal("candidate", created.Status);
        Assert.Equal("project-1", created.ProjectId);
        Assert.Equal("chapter-002", created.TargetChapterId);
        Assert.Equal("chapter_rewrite", created.ImpactLevel);

        var decideResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "DecideCreativeIntent",
                Arguments = new Dictionary<string, string>
                {
                    ["intentId"] = created.Id,
                    ["status"] = "accepted",
                    ["decisionReason"] = "符合用户当前章节重写目标",
                    ["conflictStatus"] = "none"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(decideResult.Success);
        var decided = Assert.IsType<CreativeIntentItem>(decideResult.Data);
        Assert.Equal("accepted", decided.Status);
        Assert.Contains("当前章节重写目标", decided.DecisionReason);
        Assert.NotNull(decided.DecidedAt);

        var queryResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryCreativeIntents",
                Arguments = new Dictionary<string, string>
                {
                    ["status"] = "accepted",
                    ["targetChapterId"] = "chapter-002"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(queryResult.Success);
        Assert.Contains("怪物围攻", queryResult.Message);
        Assert.DoesNotContain("宫廷权谋", queryResult.Message);
        var queried = Assert.IsType<CreativeIntentQueryResult>(queryResult.Data);
        var item = Assert.Single(queried.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal("project-1", item.ProjectId);

        session.ActiveProjectId = "project-2";
        var otherProjectQuery = await registry.ExecuteAsync(
            new AgentToolCall { Name = "QueryCreativeIntents" },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(otherProjectQuery.Success);
        var otherProject = Assert.IsType<CreativeIntentQueryResult>(otherProjectQuery.Data);
        var otherItem = Assert.Single(otherProject.Items);
        Assert.Equal("other-project-intent", otherItem.Id);
        Assert.DoesNotContain(created.Id, otherProjectQuery.Message);
    }

    [Fact]
    public async Task RevisionPlanTools_CreateAndQueryProjectScopedPlans()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.AddRange(
                new NovelProjectEntity
                {
                    Id = "project-1",
                    UserId = "user-1",
                    Title = "修订计划测试书",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new NovelProjectEntity
                {
                    Id = "project-2",
                    UserId = "user-1",
                    Title = "隔离项目",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "other-project-plan",
                UserId = "user-1",
                ProjectId = "project-2",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-009",
                Status = "accepted",
                RequirementsJson = "[\"别的项目修订\"]",
                ContinuityRequirementsJson = "[]",
                ImpactAnalysisJson = "{}",
                AffectedChapterIdsJson = "[\"chapter-009\"]",
                InvalidatedPackageIdsJson = "[]",
                RiskLevel = "medium",
                Recommendation = "别的项目继续重写。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-chapter-002"
        };

        var search = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "修订计划 影响分析 重建章节",
                    ["includeAll"] = "true"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(search.Success);
        var searchJson = JsonSerializer.Serialize(search.Data);
        Assert.Contains("CreateRevisionPlan", searchJson);
        Assert.Contains("QueryRevisionPlans", searchJson);

        var createResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "CreateRevisionPlan",
                Arguments = new Dictionary<string, string>
                {
                    ["source"] = "user_request",
                    ["planType"] = "chapter_rewrite",
                    ["targetScope"] = "chapter",
                    ["targetChapterId"] = "chapter-002",
                    ["status"] = "accepted",
                    ["requirementsJson"] = "[\"第二章加入怪物围攻\"]",
                    ["continuityRequirementsJson"] = "[\"承接第一章结尾\"]",
                    ["impactAnalysisJson"] = "{\"affectedChapterIds\":[\"chapter-002\"],\"invalidatedPackageIds\":[\"pkg-002\"]}",
                    ["affectedChapterIdsJson"] = "[\"chapter-002\"]",
                    ["invalidatedPackageIdsJson"] = "[\"pkg-002\"]",
                    ["riskLevel"] = "high",
                    ["recommendation"] = "重建第二章生产包后重新生成正文。"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(createResult.Success);
        var created = Assert.IsType<RevisionPlanItem>(createResult.Data);
        Assert.Equal("project-1", created.ProjectId);
        Assert.Equal("chapter-002", created.TargetChapterId);
        Assert.Equal("chapter_rewrite", created.PlanType);
        Assert.Contains("pkg-002", created.InvalidatedPackageIdsJson);

        var queryResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryRevisionPlans",
                Arguments = new Dictionary<string, string>
                {
                    ["status"] = "accepted",
                    ["targetChapterId"] = "chapter-002"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(queryResult.Success);
        Assert.Contains("怪物围攻", queryResult.Message);
        Assert.DoesNotContain("别的项目修订", queryResult.Message);
        var queried = Assert.IsType<RevisionPlanQueryResult>(queryResult.Data);
        var item = Assert.Single(queried.Items);
        Assert.Equal(created.Id, item.Id);

        session.ActiveProjectId = "project-2";
        var otherProjectQuery = await registry.ExecuteAsync(
            new AgentToolCall { Name = "QueryRevisionPlans" },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(otherProjectQuery.Success);
        var otherProject = Assert.IsType<RevisionPlanQueryResult>(otherProjectQuery.Data);
        var otherItem = Assert.Single(otherProject.Items);
        Assert.Equal("other-project-plan", otherItem.Id);
        Assert.DoesNotContain(created.Id, otherProjectQuery.Message);
    }

    [Fact]
    public async Task CreateRevisionPlan_AutoInvalidatesAffectedPackagesWhenImpactScopeIsKnown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IRevisionPlanPackageInvalidationService, RevisionPlanPackageInvalidationService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "修订失效联动测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.TianmingPackages.AddRange(
                Package("pkg-001", "chapter-001"),
                Package("pkg-002", "chapter-002"),
                Package("pkg-003", "chapter-003"));
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-revision-create"
        };

        var createResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "CreateRevisionPlan",
                Arguments = new Dictionary<string, string>
                {
                    ["source"] = "user_request",
                    ["planType"] = "chapter_rewrite",
                    ["targetScope"] = "chapter",
                    ["targetChapterId"] = "chapter-002",
                    ["status"] = "accepted",
                    ["requirementsJson"] = "[\"第二章改成怪物围攻\"]",
                    ["continuityRequirementsJson"] = "[\"第三章以后必须承接新版第二章\"]",
                    ["impactAnalysisJson"] = "{\"affectedChapterIds\":[\"chapter-002\"],\"impact\":\"downstream_must_rebuild\"}",
                    ["affectedChapterIdsJson"] = "[\"chapter-002\"]",
                    ["riskLevel"] = "high",
                    ["recommendation"] = "重写第二章，并让第二章及后续旧生产包过期。"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(createResult.Success, createResult.Message);
        Assert.Contains("旧生产包失效", createResult.Message);
        var created = Assert.IsType<RevisionPlanItem>(createResult.Data);
        Assert.Equal("ready_for_rebuild", created.Status);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        Assert.Equal("completed", (await verifyDb.TianmingPackages.SingleAsync(p => p.Id == "pkg-001")).Status);
        Assert.Equal("stale", (await verifyDb.TianmingPackages.SingleAsync(p => p.Id == "pkg-002")).Status);
        Assert.Equal("stale", (await verifyDb.TianmingPackages.SingleAsync(p => p.Id == "pkg-003")).Status);
        var plan = await verifyDb.RevisionPlans.SingleAsync(p => p.Id == created.Id);
        Assert.Contains("pkg-002", plan.InvalidatedPackageIdsJson);
        Assert.Contains("pkg-003", plan.InvalidatedPackageIdsJson);
        Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
            e.ArtifactId == created.Id &&
            e.EventType == "revision_plan_packages_invalidated");

        static TianmingPackage Package(string id, string chapterId) => new()
        {
            Id = id,
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapterId,
            RuntimeRunId = $"run-{id}",
            PackageKind = "chapter_generation",
            Status = "completed",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task CreateRevisionPlan_WithSameRequestDoesNotDuplicateAutoInvalidationSideEffects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IRevisionPlanPackageInvalidationService, RevisionPlanPackageInvalidationService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "修订失效幂等测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.TianmingPackages.AddRange(
                Package("pkg-001", "chapter-001"),
                Package("pkg-002", "chapter-002"),
                Package("pkg-003", "chapter-003"));
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-revision-create"
        };
        var call = new AgentToolCall
        {
            Name = "CreateRevisionPlan",
            Arguments = new Dictionary<string, string>
            {
                ["source"] = "user_request",
                ["planType"] = "chapter_rewrite",
                ["targetScope"] = "chapter",
                ["targetChapterId"] = "chapter-002",
                ["status"] = "accepted",
                ["requirementsJson"] = "[\"第二章改成怪物围攻\"]",
                ["continuityRequirementsJson"] = "[\"第三章以后必须承接新版第二章\"]",
                ["impactAnalysisJson"] = "{\"affectedChapterIds\":[\"chapter-002\"],\"impact\":\"downstream_must_rebuild\"}",
                ["affectedChapterIdsJson"] = "[\"chapter-002\"]",
                ["riskLevel"] = "high",
                ["recommendation"] = "重写第二章，并让第二章及后续旧生产包过期。"
            }
        };

        var first = await registry.ExecuteAsync(
            call,
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);
        var second = await registry.ExecuteAsync(
            call,
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        var firstPlan = Assert.IsType<RevisionPlanItem>(first.Data);
        var secondPlan = Assert.IsType<RevisionPlanItem>(second.Data);
        Assert.Equal(firstPlan.Id, secondPlan.Id);
        Assert.Equal("ready_for_rebuild", secondPlan.Status);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        Assert.Single(await verifyDb.RevisionPlans.ToListAsync());
        Assert.Single(await verifyDb.ProductionEvents
            .Where(e => e.EventType == "revision_plan_packages_invalidated" && e.ArtifactId == firstPlan.Id)
            .ToListAsync());
        Assert.Single(await verifyDb.ProductionEvents
            .Where(e => e.EventType == OutputArtifactRecorder.EventType &&
                        e.ArtifactType == "revision_plan_packages_invalidated" &&
                        e.ArtifactId == firstPlan.Id)
            .ToListAsync());
        Assert.Single(await verifyDb.AgentRuntimeEvents
            .Where(e => e.Type == "production_progress" &&
                        e.Stage == "packages_invalidated" &&
                        e.ArtifactId == firstPlan.Id)
            .ToListAsync());

        static TianmingPackage Package(string id, string chapterId) => new()
        {
            Id = id,
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapterId,
            RuntimeRunId = $"run-{id}",
            PackageKind = "chapter_generation",
            Status = "completed",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task InvalidateAffectedPackages_MarksRevisionPlanPackagesStaleAndEmitsEvents()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IRevisionPlanPackageInvalidationService, RevisionPlanPackageInvalidationService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.AddRange(
                new NovelProjectEntity
                {
                    Id = "project-1",
                    UserId = "user-1",
                    Title = "失效包测试书",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new NovelProjectEntity
                {
                    Id = "project-2",
                    UserId = "user-1",
                    Title = "不应受影响的书",
                    Status = "Writing",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.TianmingPackages.AddRange(
                new TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage
                {
                    Id = "pkg-002",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    ChapterId = "chapter-002",
                    RuntimeRunId = "run-chapter-002-old",
                    PackageKind = "chapter_generation",
                    Status = "completed",
                    InputJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
                },
                new TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage
                {
                    Id = "pkg-other-project",
                    UserId = "user-1",
                    ProjectId = "project-2",
                    ChapterId = "chapter-002",
                    RuntimeRunId = "run-other",
                    PackageKind = "chapter_generation",
                    Status = "completed",
                    InputJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
                });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "run-chapter-002",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "accepted",
                RequirementsJson = "[\"第二章改成打怪升级\"]",
                ContinuityRequirementsJson = "[\"承接第一章结尾\"]",
                ImpactAnalysisJson = "{\"reason\":\"用户要求改变第二章方向\"}",
                AffectedChapterIdsJson = "[\"chapter-002\"]",
                InvalidatedPackageIdsJson = "[\"pkg-002\"]",
                RiskLevel = "high",
                Recommendation = "使旧包失效，然后重新构建第二章生产包。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-chapter-002"
        };

        var search = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = "修订计划 失效 生产包 重建",
                    ["includeAll"] = "true"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(search.Success);
        var searchJson = JsonSerializer.Serialize(search.Data);
        Assert.Contains("InvalidateAffectedPackages", searchJson);

        var invalidateResult = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "InvalidateAffectedPackages",
                Arguments = new Dictionary<string, string>
                {
                    ["revisionPlanId"] = "revision-plan-1"
                }
            },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(invalidateResult.Success);
        Assert.Contains("pkg-002", invalidateResult.Message);
        Assert.Equal("QueryNovelProductionState", invalidateResult.RecommendedToolName);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var invalidatedPackage = await verifyDb.TianmingPackages.SingleAsync(p => p.Id == "pkg-002");
        var otherProjectPackage = await verifyDb.TianmingPackages.SingleAsync(p => p.Id == "pkg-other-project");
        var plan = await verifyDb.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-1");

        Assert.Equal("stale", invalidatedPackage.Status);
        Assert.Equal("completed", otherProjectPackage.Status);
        Assert.Equal("ready_for_rebuild", plan.Status);

        Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
            e.ProjectId == "project-1" &&
            e.EventType == "revision_plan_packages_invalidated" &&
            e.Stage == "packages_invalidated" &&
            e.Status == "stale" &&
            e.ArtifactType == "RevisionPlan" &&
            e.ArtifactId == "revision-plan-1" &&
            JsonContainsText(e.DataJson, "pkg-002"));

        Assert.Contains(await verifyDb.AgentRuntimeEvents.ToListAsync(), e =>
            e.ProjectId == "project-1" &&
            e.Type == "production_progress" &&
            e.Stage == "packages_invalidated" &&
            e.Status == "stale" &&
            e.ArtifactType == "RevisionPlan" &&
            e.ArtifactId == "revision-plan-1" &&
            e.DisplaySurface == AgentRuntimeEventSurface.Workflow &&
            e.DisplayPolicy == AgentRuntimeEventDisplayPolicy.Timeline &&
            JsonContainsText(e.DataJson, "pkg-002"));
    }

    [Fact]
    public async Task BuildChapterContextPackage_InjectsAcceptedCreativeIntentsIntoPackageSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-creative-intent-package-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "创意生产包测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.CreativeIntents.AddRange(
                new CreativeIntentEntity
                {
                    Id = "intent-project-mainline",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    SessionId = "session-1",
                    RuntimeRunId = "run-old",
                    Source = "agent_suggestion",
                    RawContent = "旧邮路成为第一卷主线",
                    NormalizedIntent = "旧邮路成为第一卷主线，每次开启都要付出记忆代价。",
                    TargetScope = "project",
                    Status = "accepted",
                    ImpactLevel = "mainline_change",
                    ConflictStatus = "none",
                    DecisionReason = "用户确认采纳",
                    MetadataJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-9),
                    DecidedAt = DateTime.UtcNow.AddMinutes(-9)
                },
                new CreativeIntentEntity
                {
                    Id = "intent-chapter-002",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    SessionId = "session-1",
                    RuntimeRunId = "run-old",
                    Source = "chat",
                    RawContent = "第二章改成怪物围攻",
                    NormalizedIntent = "第二章主冲突改为怪物围攻，男主用银蓝邮徽识别逃生路线。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    Status = "accepted",
                    ImpactLevel = "chapter_rewrite",
                    ConflictStatus = "none",
                    DecisionReason = "用户明确要求",
                    MetadataJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-8),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-7),
                    DecidedAt = DateTime.UtcNow.AddMinutes(-7)
                },
                new CreativeIntentEntity
                {
                    Id = "intent-chapter-003",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    SessionId = "session-1",
                    Source = "chat",
                    RawContent = "第三章增加邮差公会",
                    NormalizedIntent = "第三章再引入邮差公会。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-003",
                    Status = "accepted",
                    ImpactLevel = "future_carry",
                    ConflictStatus = "none",
                    MetadataJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-6),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new CreativeIntentEntity
                {
                    Id = "intent-candidate",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    SessionId = "session-1",
                    Source = "chat",
                    RawContent = "候选创意暂不采纳",
                    NormalizedIntent = "候选创意暂不采纳。",
                    TargetScope = "project",
                    Status = "candidate",
                    ImpactLevel = "minor_edit",
                    ConflictStatus = "unknown",
                    MetadataJson = "{}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-4),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
                });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-002",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "run-revision",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"第二章必须改成怪物围攻和打怪升级\"]",
                ContinuityRequirementsJson = "[\"承接第一章银蓝邮徽刚到手\"]",
                ImpactAnalysisJson = "{\"reason\":\"旧生产包已失效，需要按用户新方向重建\"}",
                AffectedChapterIdsJson = "[\"chapter-002\"]",
                InvalidatedPackageIdsJson = "[\"pkg-002-old\"]",
                RiskLevel = "high",
                Recommendation = "重建第二章生产包并重新生成正文。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-other",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "run-revision-other",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"第二章改成纯情感拉扯\"]",
                ContinuityRequirementsJson = "[\"暂不执行\"]",
                ImpactAnalysisJson = "{\"reason\":\"这是同章另一个待定计划，不应被本次 ProduceChapter 注入\"}",
                AffectedChapterIdsJson = "[\"chapter-002\"]",
                InvalidatedPackageIdsJson = "[\"pkg-other-old\"]",
                RiskLevel = "medium",
                Recommendation = "同章另一个待定修订计划。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "根据已采纳创意构建第二章生产包。",
                CandidateDirections = { "已采纳创意进入第二章生产包" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与打怪升级"
                }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["revisionPlanId"] = "revision-plan-002",
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "draft_only"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            if (!result.Success)
            {
                throw new InvalidOperationException(JsonSerializer.Serialize(new
                {
                    result.Message,
                    result.Phase,
                    result.RecommendedToolName,
                    result.Failure
                }));
            }
            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            var package = execution.ContextPackage!;
            Assert.Contains(package.AcceptedCreativeIntents, intent => intent.IntentId == "intent-project-mainline");
            Assert.Contains(package.AcceptedCreativeIntents, intent => intent.IntentId == "intent-chapter-002");
            Assert.DoesNotContain(package.AcceptedCreativeIntents, intent => intent.IntentId == "intent-chapter-003");
            Assert.DoesNotContain(package.AcceptedCreativeIntents, intent => intent.IntentId == "intent-candidate");
            Assert.Contains(package.HardContinuityFacts,
                fact => fact.Contains("已采纳创意", StringComparison.Ordinal) &&
                        fact.Contains("怪物围攻", StringComparison.Ordinal));

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var savedPackage = await verifyDb.TianmingPackages.SingleAsync(p => p.RuntimeRunId == run.RunId);
            using var inputJson = JsonDocument.Parse(savedPackage.InputJson);
            var inputIntents = inputJson.RootElement.GetProperty("acceptedCreativeIntents").EnumerateArray().ToList();
            Assert.Contains(inputIntents, item => item.GetProperty("intentId").GetString() == "intent-chapter-002");
            Assert.DoesNotContain(inputIntents, item => item.GetProperty("intentId").GetString() == "intent-chapter-003");
            var inputRevisionPlans = inputJson.RootElement.GetProperty("sourceRevisionPlans").EnumerateArray().ToList();
            Assert.Contains(inputRevisionPlans, item => item.GetProperty("revisionPlanId").GetString() == "revision-plan-002");
            Assert.DoesNotContain(inputRevisionPlans, item => item.GetProperty("revisionPlanId").GetString() == "revision-plan-other");
            Assert.Contains(inputRevisionPlans, item =>
                item.GetProperty("requirementsJson").GetString()?.Contains("怪物围攻", StringComparison.Ordinal) == true);

            using var knowledgeJson = JsonDocument.Parse(savedPackage.KnowledgeSnapshotJson!);
            var knowledgeIntents = knowledgeJson.RootElement.GetProperty("acceptedCreativeIntents").EnumerateArray().ToList();
            Assert.Contains(knowledgeIntents, item => item.GetProperty("intentId").GetString() == "intent-project-mainline");
            var knowledgeRevisionPlans = knowledgeJson.RootElement.GetProperty("sourceRevisionPlans").EnumerateArray().ToList();
            Assert.Contains(knowledgeRevisionPlans, item => item.GetProperty("revisionPlanId").GetString() == "revision-plan-002");
            Assert.DoesNotContain(knowledgeRevisionPlans, item => item.GetProperty("revisionPlanId").GetString() == "revision-plan-other");
            Assert.Contains(inputIntents, item =>
                item.GetProperty("normalizedIntent").GetString()?.Contains("怪物围攻", StringComparison.Ordinal) == true);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryNovelProductionState_ReturnsRuntimePackageEventsToolsAndOutbox()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "生产状态测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：黑雨邮路",
                ChapterNumber = 2,
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-run-1",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = "running",
                Mode = "production",
                CurrentPhase = "writing",
                CurrentStep = 3,
                ActiveTool = NovelAgentProductionStages.DraftGeneration,
                UserMessage = "继续写第二章",
                LastMessage = "正在生成正文",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent
            {
                Id = "runtime-event-1",
                RuntimeRunId = "runtime-run-1",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Type = "progress",
                Stage = NovelAgentProductionStages.DraftGeneration,
                Status = "running",
                Message = "已开始生成正文",
                DisplaySurface = "workflow",
                DisplayPolicy = "timeline",
                CreatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent
            {
                Id = "runtime-event-2",
                RuntimeRunId = "runtime-run-1",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Type = "production_progress",
                Stage = NovelAgentProductionStages.ContextPackage,
                Status = "completed",
                Message = "章节生产包已构建并持久化。",
                DisplaySurface = "workflow",
                DisplayPolicy = "timeline",
                CreatedAt = DateTime.UtcNow.AddSeconds(1)
            });
            db.AgentRuntimeEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeEvent
            {
                Id = "runtime-event-3",
                RuntimeRunId = "runtime-run-1",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Type = "production_progress",
                Stage = NovelAgentProductionStages.DraftGeneration,
                Status = "running",
                Message = "正在等待模型生成正文与修订记录。",
                DisplaySurface = "workflow",
                DisplayPolicy = "timeline",
                CreatedAt = DateTime.UtcNow.AddSeconds(2)
            });
            db.AgentToolExecutions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution
            {
                Id = "tool-exec-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RunId = "runtime-run-1",
                ToolName = NovelAgentProductionStages.DraftGeneration,
                Phase = "writing",
                Risk = "High",
                ArgumentsHash = "hash",
                SemanticContractJson = """
                {
                  "displayName":"章节正文生成",
                  "domainSurface":"创作工作流 / 小说书城",
                  "outputKind":"workflow_process_artifact",
                  "inputArtifacts":["chapter_plan_run","continuity_pack","knowledge_binding_snapshot"],
                  "outputArtifacts":["chapter_draft","kernel_gate_report","chapter_version"],
                  "idempotencyPolicy":"Uses runId + targetChapterId + commitPolicy.",
                  "rollbackPolicy":"Recover through ChapterVersion rollback.",
                  "userVisibleWhere":"创作工作流",
                  "resultSemantics":"生成章节正文并进入门禁"
                }
                """,
                Status = "running",
                StartedAt = DateTime.UtcNow.AddSeconds(-30)
            });
            db.AgentToolExecutions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution
            {
                Id = "tool-exec-2",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RunId = "runtime-run-1",
                ToolName = "ProduceChapter",
                Phase = "writing",
                Risk = "High",
                ArgumentsHash = "hash-2",
                Status = "failed",
                ResultPhase = "gate_failed",
                ResultMessage = "章节生产闭环在校验/修复章节草稿阶段停止。",
                ErrorType = "PRODUCE_CHAPTER_STAGE_FAILED",
                ErrorMessage = "主角状态与上一章不连续。",
                SemanticContractJson = """
                {
                  "displayName":"章节生产闭环",
                  "domainSurface":"创作工作流 / 小说书城",
                  "outputKind":"workflow_process_artifact",
                  "inputArtifacts":["chapter_plan_run","continuity_pack","knowledge_binding_snapshot"],
                  "outputArtifacts":["chapter_commit","chapter_version","post_commit_outbox"],
                  "idempotencyPolicy":"Uses runId + targetChapterId + commitPolicy.",
                  "rollbackPolicy":"Committed output is recoverable through ChapterVersion rollback.",
                  "userVisibleWhere":"创作工作流和小说书城",
                  "resultSemantics":"生产并提交章节"
                }
                """,
                FailureJson = JsonSerializer.Serialize(new
                {
                    code = "PRODUCE_CHAPTER_STAGE_FAILED",
                    failedStage = NovelAgentProductionStages.GateValidationOrRepair,
                    reason = "主角状态与上一章不连续。",
                    recoverable = true,
                    recommendedAction = "ProduceChapter",
                    recoverableActions = new[] { "查看门禁失败项", "重建章节蓝图" },
                    requiresUserDecision = false,
                    producedArtifacts = new[]
                    {
                        new
                        {
                            artifactType = "chapter_production_blocked",
                            artifactId = "runtime-run-1",
                            summary = "已保留阻断报告。"
                        }
                    }
                }),
                StartedAt = DateTime.UtcNow.AddSeconds(-20),
                CompletedAt = DateTime.UtcNow.AddSeconds(-5)
            });
            var truthStore = new ProductionTruthStore(db);
            await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
                Id: "pkg-chapter-002",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-002",
                RuntimeRunId: "runtime-run-1",
                PackageKind: "chapter_generation",
                InputJson: "{\"goal\":\"第二章\"}",
                DependencyVersionsJson: "{\"storyBible\":2}",
                KnowledgeSnapshotJson: "{\"bindings\":[\"kb-1\"],\"knowledgeBindingSummary\":{\"bindingCount\":2,\"shouldEnterGateCount\":1,\"shouldEnterBlueprintCount\":2,\"shouldEnterFactSnapshotCount\":1,\"hardConstraintCount\":1,\"referenceCount\":1,\"classifiedCount\":1,\"pendingClassificationCount\":1,\"importedCount\":1,\"referencedCount\":1}}",
                FactSnapshotJson: "{\"previous\":\"第一章结尾\"}",
                PromptVersion: "chapter-v1",
                KernelVersion: "tianming-kernel-v1"));
            await truthStore.AppendEventAsync(new CreateProductionEventRequest(
                RuntimeRunId: "runtime-run-1",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-002",
                PackageId: "pkg-chapter-002",
                EventType: "build_package",
                Stage: "BuildChapterPackage",
                Status: "completed",
                Message: "连续性包已构建",
                ArtifactType: "TianmingPackage",
                ArtifactId: "pkg-chapter-002",
                DataJson: "{}"));
            await truthStore.AppendEventAsync(new CreateProductionEventRequest(
                RuntimeRunId: "runtime-run-1",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-002",
                PackageId: "pkg-chapter-002",
                EventType: "outbox_failed",
                Stage: "index_outbox",
                Status: "retryable_failed",
                Message: "后台 outbox 处理失败，已排队重试。 index_chapter_content/chapter_version",
                ArtifactType: "outbox_event",
                ArtifactId: "outbox-1",
                DataJson: "{\"eventType\":\"index_chapter_content\",\"error\":\"Qdrant timeout\"}"));
            await truthStore.AppendEventAsync(new CreateProductionEventRequest(
                RuntimeRunId: "runtime-run-1",
                UserId: "user-1",
                ProjectId: "project-1",
                ChapterId: "project-1-chapter-002",
                PackageId: "pkg-chapter-002",
                EventType: "tool_output_artifact_recorded",
                Stage: NovelAgentProductionStages.DraftGeneration,
                Status: "completed",
                Message: "第二章草稿已生成。",
                ArtifactType: "chapter_draft",
                ArtifactId: "draft-chapter-002-v1",
                DataJson: """
                {
                  "toolName":"ProduceChapter",
                  "outputKind":"ProcessArtifact",
                  "visibleInWorkflow":true,
                  "visibleInLibrary":false,
                  "userVisibleWhere":["创作工作流"],
                  "sourceEventType":"chapter_draft_generated",
                  "sourceEventId":"evt-draft-002",
                  "summary":"第二章草稿已生成，等待门禁校验。"
                }
                """));
            await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
                UserId: "user-1",
                ProjectId: "project-1",
                RuntimeRunId: "runtime-run-1",
                EventType: "index_chapter",
                AggregateType: "chapter",
                AggregateId: "project-1-chapter-002",
                PayloadJson: "{}"));
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "fact-chapter-002-v1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                ChapterVersionId = "version-chapter-002-v1",
                VersionNumber = 3,
                Source = "chapter_fact_extraction",
                CreatedAt = DateTime.UtcNow.AddSeconds(5),
                SnapshotJson = """
                {
                  "chapterId": "project-1-chapter-002",
                  "chapterTitle": "第二章：黑雨邮路",
                  "protagonistName": "沈砚",
                  "protagonistStatus": "右手被邮徽灼伤，但仍能行动",
                  "endingState": "沈砚带着银蓝邮徽冲进旧邮路入口",
                  "nextChapterMustCarry": ["沈砚不能把银蓝邮徽当攻击武器", "旧邮路入口必须付出记忆代价"]
                }
                """
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "runtime-run-1"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("后台执行中", result.Message);
        Assert.Contains("当前生产进度：生成章节正文/running：正在等待模型生成正文与修订记录。", result.Message);
        Assert.Contains("最近失败：ProduceChapter 在校验/修复章节草稿停止：主角状态与上一章不连续。", result.Message);
        Assert.Contains("工具执行：章节正文生成", result.Message);
        Assert.Contains("输入=chapter_plan_run/continuity_pack/knowledge_binding_snapshot", result.Message);
        Assert.Contains("输出=chapter_draft/kernel_gate_report/chapter_version", result.Message);
        Assert.Contains("可恢复动作：查看门禁失败项、重建章节蓝图", result.Message);
        Assert.Contains("是否需要用户决定：否", result.Message);
        Assert.Contains(NovelAgentProductionStages.DraftGeneration, result.Message);
        Assert.Contains("pkg-chapter-002", result.Message);
        Assert.Contains("知识入口：绑定 2，Gate 1，蓝图 2，FactSnapshot 1，待分类 1", result.Message);
        Assert.Contains("连续性包已构建", result.Message);
        Assert.Contains("生产链路：chapter-002/runtime-run-1/blocked", result.Message);
        Assert.Contains("步骤：上下文包/completed -> 后台处理/retryable_failed", result.Message);
        Assert.Contains("后台索引：retryable_failed：后台 outbox 处理失败，已排队重试。 index_chapter_content/chapter_version", result.Message);
        Assert.Contains("已产出", result.Message);
        Assert.Contains("chapter_draft/draft-chapter-002-v1", result.Message);
        Assert.Contains("ProcessArtifact", result.Message);
        Assert.Contains("创作工作流", result.Message);
        Assert.Contains("chapter_draft_generated", result.Message);
        Assert.Contains("事实沉淀：第二章：黑雨邮路/v3/chapter_fact_extraction：主角 沈砚", result.Message);
        Assert.Contains("下一章必须承接：沈砚不能把银蓝邮徽当攻击武器、旧邮路入口必须付出记忆代价", result.Message);
        Assert.Contains("Qdrant timeout", result.Message);
        Assert.Contains("INDEX_FAILED", result.Message);
        Assert.Contains("推荐动作：等待后台重试", result.Message);
        Assert.Contains("待处理 outbox：1", result.Message);

        var state = Assert.IsType<NovelProductionStateQueryResult>(result.Data);
        Assert.Equal("runtime-run-1", state.RuntimeRun?.Id);
        Assert.Equal("pkg-chapter-002", Assert.Single(state.Packages).Id);
        Assert.Contains(state.RuntimeEvents, e => e.Type == "production_progress" && e.Stage == NovelAgentProductionStages.DraftGeneration);
        Assert.Contains(state.ProductionEvents, e => e.EventType == "build_package");
        Assert.Contains(state.ProductionEvents, e => e.EventType == "outbox_failed");
        Assert.Contains(state.ProductionEvents, e => e.EventType == "tool_output_artifact_recorded");
        var artifact = Assert.Single(state.OutputArtifacts);
        Assert.Equal("chapter_draft", artifact.ArtifactType);
        Assert.Equal("draft-chapter-002-v1", artifact.ArtifactId);
        Assert.Equal("ProcessArtifact", artifact.OutputKind);
        Assert.Contains("创作工作流", artifact.UserVisibleWhere);
        Assert.Equal("chapter_draft_generated", artifact.SourceEventType);
        Assert.Contains(state.ProductionEvents, e => e.Failure?.Code == "INDEX_FAILED");
        var chain = Assert.Single(state.ProductionChains);
        Assert.Equal("blocked", chain.Status);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Contains(chain.Steps, step => step.Key == "outbox" && step.OutboxEventId == "outbox-1");
        Assert.Equal(2, state.ToolExecutions.Count);
        var failedTool = state.ToolExecutions.Single(t => t.Id == "tool-exec-2");
        Assert.Equal("章节生产闭环", failedTool.SemanticContract.DisplayName);
        Assert.Contains("chapter_plan_run", failedTool.SemanticContract.InputArtifacts);
        Assert.Contains("chapter_version", failedTool.SemanticContract.OutputArtifacts);
        Assert.NotNull(failedTool.Failure);
        Assert.Equal(NovelAgentProductionStages.GateValidationOrRepair, failedTool.Failure!.FailedStage);
        Assert.Contains("重建章节蓝图", failedTool.Failure.RecoverableActions);
        Assert.Single(state.OutboxEvents);
        var snapshot = Assert.Single(state.FactSnapshots);
        Assert.Equal("沈砚", snapshot.ProtagonistName);
        Assert.Contains("旧邮路入口必须付出记忆代价", snapshot.NextChapterMustCarry);

        var schema = registry.ListToolSchemas().Single(tool => tool.Name == "QueryNovelProductionState");
        Assert.Contains("agent_runtime_runs", schema.Semantic.ReadsFrom);
        Assert.Contains("production_events", schema.Semantic.ReadsFrom);
        Assert.Contains("tianming_packages", schema.Semantic.ReadsFrom);
        Assert.Contains("outbox_events", schema.Semantic.ReadsFrom);
        Assert.Contains("project_fact_snapshots", schema.Semantic.ReadsFrom);
        Assert.True(schema.SideEffects.BusinessReadOnly);
        Assert.Contains("outbox_events", schema.SideEffects.ReadsSqliteEntities);
        Assert.Contains("none_read_only", schema.Semantic.WritesTo);
    }

    [Fact]
    public async Task QueryProductionOutbox_ReturnsCurrentProjectOutboxAndToolSchema()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IProductionOutboxAdminService, ProductionOutboxAdminService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddSingleton<IProductionOutboxDispatcher, NoopProductionOutboxDispatcher>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "Outbox 工具测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.OutboxEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.OutboxEvent
            {
                Id = "outbox-retry-1",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "runtime-run-1",
                EventType = "finalize_chapter_commit_metadata",
                AggregateType = "chapter",
                AggregateId = "chapter-001",
                Status = "retryable_failed",
                Attempts = 2,
                LastError = "LLM timeout",
                PayloadJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryProductionOutbox",
                Arguments = new Dictionary<string, string>
                {
                    ["status"] = "retryable_failed"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("outbox-retry-1/finalize_chapter_commit_metadata/chapter/retryable_failed", result.Message);
        Assert.Contains("LLM timeout", result.Message);
        Assert.Contains("重试失败 outbox", result.Suggestions);
        var data = Assert.IsType<OutboxAdminListResponse>(result.Data);
        Assert.Equal("outbox-retry-1", Assert.Single(data.Items).Id);
        var querySchema = registry.ListToolSchemas().Single(tool => tool.Name == "QueryProductionOutbox");
        Assert.Contains("outbox_events", querySchema.Semantic.ReadsFrom);
        Assert.True(querySchema.SideEffects.BusinessReadOnly);
        Assert.Contains("outbox_events", querySchema.SideEffects.ReadsSqliteEntities);
        Assert.Contains("none_read_only", querySchema.Semantic.WritesTo);
        var retrySchema = registry.ListToolSchemas().Single(tool => tool.Name == "RetryProductionOutbox");
        Assert.Contains("outbox_events", retrySchema.Semantic.ReadsFrom);
        Assert.Contains("outbox_events", retrySchema.SideEffects.ReadsSqliteEntities);
        Assert.Contains("outbox_events", retrySchema.Semantic.WritesTo);
    }

    [Fact]
    public async Task RetryProductionOutbox_ResetsCurrentProjectOutboxAndSuggestsStatusQuery()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IProductionOutboxAdminService, ProductionOutboxAdminService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IOutputArtifactRecorder, OutputArtifactRecorder>();
        services.AddSingleton<IProductionOutboxDispatcher, NoopProductionOutboxDispatcher>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);
        Assert.NotNull(provider.GetService<IOutputArtifactRecorder>());

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "Outbox 重试测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.OutboxEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.OutboxEvent
            {
                Id = "outbox-retry-1",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "runtime-run-1",
                EventType = "extract_chapter_continuity_facts",
                AggregateType = "chapter",
                AggregateId = "chapter-001",
                Status = "retryable_failed",
                Attempts = 3,
                LastError = "Fact extraction timeout",
                NextAttemptAt = DateTime.UtcNow.AddMinutes(10),
                PayloadJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "RetryProductionOutbox",
                Arguments = new Dictionary<string, string>
                {
                    ["eventId"] = "outbox-retry-1"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = "runtime-run-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("已重试后台 outbox outbox-retry-1", result.Message);
        Assert.Contains("当前状态 pending", result.Message);
        Assert.Contains("查询生产状态", result.Suggestions);
        var retry = Assert.IsType<OutboxAdminRetryResponse>(result.Data);
        Assert.Equal("pending", retry.Status);
        Assert.Equal(0, retry.Attempts);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var evt = await verifyDb.OutboxEvents.SingleAsync(item => item.Id == "outbox-retry-1");
        Assert.Equal("pending", evt.Status);
        Assert.Equal(0, evt.Attempts);
        Assert.Null(evt.LastError);
        Assert.Null(evt.NextAttemptAt);
        var outputArtifact = await verifyDb.ProductionEvents.SingleAsync(item =>
            item.EventType == OutputArtifactRecorder.EventType &&
            item.ArtifactType == "production_outbox_retry" &&
            item.ArtifactId == "outbox-retry-1");
        Assert.Equal("retry_production_outbox", outputArtifact.Stage);
        Assert.Equal("completed", outputArtifact.Status);
        Assert.Contains("已重试后台 outbox outbox-retry-1", outputArtifact.Message);
        Assert.Contains("RetryProductionOutbox", outputArtifact.DataJson);
    }

    [Fact]
    public async Task QueryNovelProductionState_IncludesStructuredRuntimeFailureInMessage()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "Runtime 失败测试",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-run-heartbeat",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = "failed",
                Mode = "production",
                CurrentPhase = "heartbeat_lost",
                UserMessage = "生成第一章",
                LastMessage = "后台运行心跳超时，已进入可恢复失败状态。",
                ErrorMessage = "Redis heartbeat missing; recovering from database truth.",
                FailureJson = JsonSerializer.Serialize(new
                {
                    code = "RUNTIME_HEARTBEAT_LOST",
                    stage = "heartbeat_lost",
                    message = "Redis heartbeat missing; recovering from database truth.",
                    recoverable = true,
                    recommendedAction = "QueryRuntimeRun 后按当前章节、工作流和工具结果决定恢复、重试或询问用户。",
                    artifactIds = new[] { "runtime-run-heartbeat" },
                    requiresUserDecision = false
                }),
                CreatedAt = DateTime.UtcNow.AddMinutes(-40),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-35),
                CompletedAt = DateTime.UtcNow.AddMinutes(-35)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "runtime-run-heartbeat"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("RUNTIME_HEARTBEAT_LOST", result.Message);
        Assert.Contains("heartbeat_lost", result.Message);
        Assert.Contains("可恢复：是", result.Message);
        Assert.Contains("推荐动作：QueryRuntimeRun 后按当前章节、工作流和工具结果决定恢复、重试或询问用户。", result.Message);
        Assert.Contains("是否需要用户决定：否", result.Message);
    }

    [Fact]
    public async Task QueryNovelProductionState_WithoutExplicitRunId_UsesLatestRuntimeRunInsteadOfSessionPlanningRun()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "状态查询测试",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-cancelled-latest",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = "cancelled",
                Mode = "production",
                CurrentPhase = "draft_repair",
                UserMessage = "继续写第二章",
                LastMessage = "后台执行已取消。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1),
                CompletedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>()
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1",
                ActiveRunId = "chapter-planning-run-not-runtime"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("当前状态：已取消", result.Message);
        var state = Assert.IsType<NovelProductionStateQueryResult>(result.Data);
        Assert.Equal("runtime-cancelled-latest", state.RuntimeRun?.Id);
    }

    [Fact]
    public async Task QueryNovelProductionState_ExplainsStalePackagesAndSuggestsRebuild()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "过期生产包测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：黑雨邮路",
                ChapterNumber = 2,
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-run-stale",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = "completed",
                Mode = "production",
                CurrentPhase = NovelAgentProductionStages.ContextPackage,
                UserMessage = "重写第二章",
                LastMessage = "旧生产包已被修订计划标记为过期。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow.AddMinutes(-5)
            });
            db.TianmingPackages.Add(new TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage
            {
                Id = "pkg-chapter-002",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                RuntimeRunId = "runtime-run-stale",
                PackageKind = "chapter_generation",
                Status = "stale",
                InputJson = "{\"goal\":\"第二章\"}",
                DependencyVersionsJson = "{\"storyBible\":2}",
                KnowledgeSnapshotJson = "{\"bindings\":[\"kb-1\"]}",
                FactSnapshotJson = "{\"previous\":\"第一章结尾\"}",
                PromptVersion = "chapter-v1",
                KernelVersion = "tianming-kernel-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-18),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-3)
            });
            db.TianmingPackages.Add(new TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage
            {
                Id = "pkg-chapter-002-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                RuntimeRunId = "runtime-run-stale",
                PackageKind = "chapter_context_package",
                Status = "pending",
                InputJson = "{\"rebuiltFromPackageIds\":[\"pkg-chapter-002\"]}",
                DependencyVersionsJson = "{\"storyBible\":3}",
                KnowledgeSnapshotJson = "{\"rebuiltFromPackageIds\":[\"pkg-chapter-002\"]}",
                FactSnapshotJson = "{\"previous\":\"重建第二章\"}",
                PromptVersion = "chapter-context-v1",
                KernelVersion = "agentic-tianming-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt = DateTime.UtcNow
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "runtime-run-stale",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "project-1-chapter-002",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"第二章重建为打怪升级\"]",
                ContinuityRequirementsJson = "[\"承接第一章结尾\"]",
                ImpactAnalysisJson = "{\"reason\":\"第二章方向变更，需要重建生产包\"}",
                AffectedChapterIdsJson = "[\"project-1-chapter-002\"]",
                InvalidatedPackageIdsJson = "[\"pkg-chapter-002\"]",
                RiskLevel = "high",
                Recommendation = "重建第二章生产包，并基于修订计划继续 ProduceChapter。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "runtime-run-stale"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("过期生产包", result.Message);
        Assert.Contains("pkg-chapter-002", result.Message);
        Assert.Contains("revision-plan-1", result.Message);
        Assert.Contains("ready_for_rebuild", result.Message);
        Assert.Contains("建议 ProduceChapter", result.Message);
        Assert.Contains("重建第二章生产包", result.Message);
        Assert.Contains("替代旧包 pkg-chapter-002", result.Message);
        Assert.Contains("重建链路：pkg-chapter-002（stale） -> pkg-chapter-002-v2（pending）", result.Message);
        Assert.Contains("重建过期章节生产包", result.Suggestions);

        var state = Assert.IsType<NovelProductionStateQueryResult>(result.Data);
        var rebuiltPackage = Assert.Single(state.Packages, package => package.Id == "pkg-chapter-002-v2");
        Assert.Equal(new[] { "pkg-chapter-002" }, rebuiltPackage.RebuiltFromPackageIds);
        var rebuildLink = Assert.Single(state.RebuildLinks);
        Assert.Equal("pkg-chapter-002", rebuildLink.OldPackageId);
        Assert.Equal("pkg-chapter-002-v2", rebuildLink.NewPackageId);
        var stale = Assert.Single(state.StalePackages);
        Assert.Equal("pkg-chapter-002", stale.PackageId);
        Assert.Equal("revision-plan-1", stale.RevisionPlanId);
        Assert.Equal("ProduceChapter", stale.RecommendedToolName);
        Assert.Equal("runtime-run-stale", stale.RecommendedArguments["runId"]);
        Assert.Equal("project-1-chapter-002", stale.RecommendedArguments["chapterId"]);
        Assert.Equal("revision-plan-1", stale.RecommendedArguments["revisionPlanId"]);
    }

    [Fact]
    public async Task QueryNovelProductionState_ShowsRevisionPlanExecutedByCurrentRunEvent()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "修订计划执行状态测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：黑雨邮路",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AgentRuntimeRuns.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
            {
                Id = "runtime-run-commit",
                UserId = "user-1",
                SessionId = "session-1",
                ProjectId = "project-1",
                Status = "completed",
                Mode = "production",
                CurrentPhase = NovelAgentProductionStages.ChapterCommit,
                UserMessage = "提交第二章",
                LastMessage = "章节已提交书城。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow.AddMinutes(-5)
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "revision-authoring-run",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "project-1-chapter-002",
                Status = "executed",
                RequirementsJson = "[\"第二章重建为打怪升级\"]",
                ContinuityRequirementsJson = "[\"承接第一章结尾\"]",
                ImpactAnalysisJson = "{\"reason\":\"第二章方向变更，需要重建生产包\"}",
                AffectedChapterIdsJson = "[\"project-1-chapter-002\"]",
                InvalidatedPackageIdsJson = "[\"pkg-chapter-002\"]",
                RiskLevel = "high",
                Recommendation = "重建第二章生产包，并基于修订计划继续 ProduceChapter。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-15),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
            db.ProductionEvents.Add(new TM.Web.NovelAgentWeb.Data.Entities.ProductionEvent
            {
                Id = "event-revision-plan-executed",
                RuntimeRunId = "runtime-run-commit",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                PackageId = "pkg-chapter-002-v2",
                EventType = "revision_plan_executed",
                Stage = NovelAgentProductionStages.ChapterCommit,
                Status = "executed",
                Message = "修订计划已随章节提交完成执行：重建第二章生产包。",
                ArtifactType = "RevisionPlan",
                ArtifactId = "revision-plan-1",
                DataJson = "{\"revisionPlanId\":\"revision-plan-1\",\"packageId\":\"pkg-chapter-002-v2\"}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3)
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "runtime-run-commit"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("revision-plan-1/executed/chapter_rewrite", result.Message);
        Assert.Contains("修订计划已随章节提交完成执行", result.Message);

        var state = Assert.IsType<NovelProductionStateQueryResult>(result.Data);
        var plan = Assert.Single(state.RevisionPlans, item => item.Id == "revision-plan-1");
        Assert.Equal("executed", plan.Status);
        Assert.Contains(state.ProductionEvents, evt =>
            evt.EventType == "revision_plan_executed" &&
            evt.ArtifactId == "revision-plan-1");
    }

    [Fact]
    public async Task CommitValidatedChapter_PersistsProductionEventAndLinksCommittedVersionToRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-commit-production-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "提交生产事件测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-silver-badge-boundary",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "银蓝邮徽能力边界",
                Content = "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                Tags = "[\"硬事实\",\"邮徽\"]",
                Weight = 10,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsageEntity
            {
                Id = "usage-silver-badge-boundary",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-silver-badge-boundary",
                Status = "referenced",
                SourceSessionId = "session-1",
                SourceRunId = "run-knowledge",
                UsageCount = 1,
                FirstSeenAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                Note = "第一章硬事实约束"
            });
            db.CreativeIntents.Add(new CreativeIntentEntity
            {
                Id = "intent-chapter-001-hook",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "chat",
                RawContent = "第一章必须让男主用银蓝邮徽从黑雨中脱身。",
                NormalizedIntent = "第一章必须让男主用银蓝邮徽从黑雨中脱身。",
                TargetScope = "chapter",
                TargetChapterId = "chapter-001",
                Status = "accepted",
                ImpactLevel = "chapter_rewrite",
                ConflictStatus = "none",
                DecisionReason = "用户确认第一章核心动作",
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DecidedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new PassingReviewDraftKernel(provider.GetRequiredService<IGeneratedContentService>()));
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var run = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "提交第一章到书城。",
                CandidateDirections = { "第一章成稿提交书城" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);

            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = run.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = run.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("正文已进入小说书城", result.Message);
            Assert.Contains("outbox 异步继续", result.Message);
            await FinalizeChapterCommitMetadataOutboxAsync(provider);
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            var version = await verifyDb.ChapterVersions.SingleAsync(v => v.ChapterId == chapter.Id);
            var evt = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_committed");
            var outbox = await verifyDb.OutboxEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "finalize_chapter_commit_metadata");
            var factSnapshot = await verifyDb.ProjectFactSnapshots.SingleAsync(s => s.ChapterVersionId == version.Id);
            var creativeIntent = await verifyDb.CreativeIntents.SingleAsync(i => i.Id == "intent-chapter-001-hook");
            var creativeEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "creative_intents_executed");
            var knowledgeEvents = await verifyDb.ProductionEvents
                .Where(e => e.RuntimeRunId == run.RunId &&
                            e.EventType == "knowledge_bindings_used")
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            Assert.Contains(knowledgeEvents,
                e => e.DataJson?.Contains("knowledge-silver-badge-boundary", StringComparison.Ordinal) == true);
            var knowledgeEvent = knowledgeEvents.First(e =>
                e.DataJson?.Contains("knowledge-silver-badge-boundary", StringComparison.Ordinal) == true);

            Assert.Equal(run.RunId, version.RuntimeRunId);
            Assert.Contains("validated", version.GateReportJson);
            Assert.Contains("Warning", version.AgentReviewJson);
            Assert.Contains("requiresRewrite\":false", version.AgentReviewJson);
            Assert.True(JsonContainsText(version.AgentReviewJson, "允许先提交"));
            var storedReview = await verifyDb.AgentReviews.SingleAsync(r =>
                r.RuntimeRunId == run.RunId &&
                r.ChapterId == chapter.Id);
            Assert.False(storedReview.RequiresRewrite);
            Assert.Contains("允许先提交", storedReview.Summary);
            Assert.Contains("requiresRewrite\":false", storedReview.ReviewJson);
            Assert.Equal("chapter_committed", evt.EventType);
            Assert.Equal(NovelAgentProductionStages.ChapterCommitted, evt.Stage);
            Assert.Equal("completed", evt.Status);
            Assert.Equal(version.Id, evt.ArtifactId);
            Assert.Equal("executed", creativeIntent.Status);
            Assert.NotNull(creativeIntent.ExecutedAt);
            Assert.Contains("已提交书城", creativeIntent.DecisionReason);
            Assert.Equal(NovelAgentProductionStages.FactsPersisted, creativeEvent.Stage);
            Assert.Equal("completed", creativeEvent.Status);
            Assert.Contains("intent-chapter-001-hook", creativeEvent.DataJson);
            Assert.Equal(NovelAgentProductionStages.FactsPersisted, knowledgeEvent.Stage);
            Assert.Equal("completed", knowledgeEvent.Status);
            Assert.Contains("knowledge-silver-badge-boundary", knowledgeEvent.DataJson);
            Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_production_completed");
            Assert.DoesNotContain(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.EventType == "chapter_commit_finished");
            Assert.Contains(await verifyDb.AgentRuntimeEvents.ToListAsync(), e =>
                e.RuntimeRunId == run.RunId &&
                e.Type == "production_progress" &&
                e.Stage == NovelAgentProductionStages.ChapterCommitted &&
                e.Status == "completed" &&
                e.DisplaySurface == AgentRuntimeEventSurface.Workflow &&
                e.DisplayPolicy == AgentRuntimeEventDisplayPolicy.Timeline);
            Assert.Equal(run.RunId, outbox.RuntimeRunId);
            Assert.Equal("chapter_commit", factSnapshot.Source);
            using var snapshotJson = JsonDocument.Parse(factSnapshot.SnapshotJson);
            Assert.Equal("project-1-chapter-001", snapshotJson.RootElement.GetProperty("chapterId").GetString());
            Assert.Contains(snapshotJson.RootElement.GetProperty("worldRules").EnumerateArray(),
                item => item.GetString()?.Contains("银蓝邮徽", StringComparison.Ordinal) == true);
            Assert.Contains(snapshotJson.RootElement.GetProperty("activeConflicts").EnumerateArray(),
                item => item.GetString()?.Contains("黑雨逼近", StringComparison.Ordinal) == true);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProduceChapter_TwoChapterE2E_UsesKnowledgeContinuityAndProjectContentQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-two-chapter-e2e-{Guid.NewGuid():N}");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton(Mock.Of<IAgentMemoryEventService>());
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IBookValidationService, BookValidationService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddScoped<IChapterCommitTruthRecorder, ChapterCommitTruthRecorder>();
        services.AddScoped<IProjectKnowledgeUsageService, ProjectKnowledgeUsageService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddScoped<IProductionDependencyGuard, ProductionDependencyGuard>();
        services.AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>();
        services.AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddSingleton<ILogger<ProjectKnowledgeUsageService>>(NullLogger<ProjectKnowledgeUsageService>.Instance);
        services.AddSingleton<IKnowledgeService>(new EmptyKnowledgeService());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        services.AddSingleton<IGeneratedContentService>(sp => new WebGeneratedContentService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ICurrentUserService>(),
            "project-1"));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "两章连续性测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new VolumeEntity
            {
                Id = "volume-1",
                ProjectId = "project-1",
                Title = "第一卷：黑雨旧邮路",
                VolumeNumber = 1
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-old-route-cost",
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "HardFact",
                Title = "旧邮路代价",
                Content = "旧邮路每次开启都必须付出一段真实记忆作为通行费。",
                Tags = "[\"硬事实\",\"旧邮路\"]",
                Weight = 10,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            });
            db.KnowledgeClassifications.Add(new KnowledgeClassificationEntity
            {
                Id = "classification-old-route-cost",
                UserId = "user-1",
                ProjectId = "project-1",
                KnowledgeId = "knowledge-old-route-cost",
                Model = "test-llm",
                ClassificationJson = """
                {
                  "rule": "旧邮路记忆代价是本书硬事实，必须进入章节门禁、章节蓝图和提交后的事实快照。",
                  "targetEntities": ["旧邮路", "银蓝邮徽", "沈砚"],
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true
                }
                """,
                Role = "HardConstraint",
                Scope = "ProjectWide",
                Priority = 100,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "AlwaysInclude",
                Confidence = 0.96,
                SourceSessionId = "session-1",
                SourceRunId = "knowledge-import-run",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1",
            vectorStore: null,
            embeddingService: null,
            currentUserService: null,
            memoryRepository: null,
            unifiedValidationService: new PassingUnifiedValidationService()), new TwoChapterContinuityKernel(provider.GetRequiredService<IGeneratedContentService>()));
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var attachKnowledge = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "AttachKnowledgeToProject",
                    Arguments = new Dictionary<string, string>
                    {
                        ["knowledgeId"] = "knowledge-old-route-cost",
                        ["status"] = "referenced"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "knowledge-binding-run"
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(attachKnowledge.Success, attachKnowledge.Message);
            Assert.Contains("已绑定知识", attachKnowledge.Message);

            var firstRun = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-001",
                UserGoal = "写第一章并提交书城，必须让沈砚拿到银蓝邮徽并付出旧邮路记忆代价。",
                CandidateDirections = { "第一章：黑雨维修站与旧邮路开启" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "旧邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = firstRun.RunId;
            var firstResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = firstRun.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(firstResult.Success, firstResult.Message);
            Assert.Contains("章节生产闭环已完成", firstResult.Message);
            await FinalizeChapterCommitMetadataOutboxAsync(provider);

            var secondRun = await workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = "chapter-002",
                UserGoal = "写第二章并提交书城，必须承接第一章结尾和旧邮路记忆代价。",
                CandidateDirections = { "第二章：旧站台追击与代价延续" },
                Constitution = new StoryCreativeConstitution
                {
                    Genre = "废土邮差冒险",
                    MainPleasure = "旧邮路探索与硬事实连续性"
                }
            }, CancellationToken.None);
            session.ActiveRunId = secondRun.RunId;
            var secondResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ProduceChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = secondRun.RunId,
                        ["maxRepairAttempts"] = "0",
                        ["commitPolicy"] = "auto_commit"
                    }
                },
                session,
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(secondResult.Success, secondResult.Message);
            await FinalizeChapterCommitMetadataOutboxAsync(provider);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapters = await verifyDb.Chapters
                .OrderBy(chapter => chapter.ChapterNumber)
                .ToListAsync();
            Assert.Equal(2, chapters.Count);
            Assert.All(chapters, chapter => Assert.Equal("volume-1", chapter.VolumeId));
            Assert.All(chapters, chapter => Assert.Equal("committed", chapter.Status));
            var versions = await verifyDb.ChapterVersions
                .OrderBy(version => version.ChapterId)
                .ToListAsync();
            Assert.Equal(2, versions.Count);
            Assert.All(versions, version => Assert.False(string.IsNullOrWhiteSpace(version.AgentReviewJson)));
            var snapshots = await verifyDb.ProjectFactSnapshots
                .OrderBy(snapshot => snapshot.ChapterId)
                .ToListAsync();
            Assert.Equal(2, snapshots.Count);
            Assert.True(JsonContainsText(snapshots[0].SnapshotJson, "真实记忆"), snapshots[0].SnapshotJson);
            Assert.True(JsonContainsText(snapshots[0].SnapshotJson, "knowledgeConstraintEvidence"), snapshots[0].SnapshotJson);
            Assert.True(JsonContainsText(snapshots[0].SnapshotJson, "classification-old-route-cost"), snapshots[0].SnapshotJson);
            using (var firstSnapshotJson = JsonDocument.Parse(snapshots[0].SnapshotJson))
            {
                var evidence = Assert.Single(firstSnapshotJson.RootElement.GetProperty("knowledgeConstraintEvidence").EnumerateArray());
                Assert.Equal("classification-old-route-cost", evidence.GetProperty("classificationId").GetString());
                Assert.True(evidence.GetProperty("shouldEnterGate").GetBoolean());
                Assert.True(evidence.GetProperty("shouldEnterBlueprint").GetBoolean());
                Assert.True(evidence.GetProperty("shouldEnterFactSnapshot").GetBoolean());
            }
            Assert.True(JsonContainsText(snapshots[1].SnapshotJson, "上一章已提交"), snapshots[1].SnapshotJson);

            var secondPackage = await verifyDb.TianmingPackages.SingleAsync(package => package.RuntimeRunId == secondRun.RunId);
            Assert.True(JsonContainsText(secondPackage.KnowledgeSnapshotJson, "旧邮路代价"), secondPackage.KnowledgeSnapshotJson);
            Assert.True(JsonContainsText(secondPackage.KnowledgeSnapshotJson, "knowledgeBindingSummary"), secondPackage.KnowledgeSnapshotJson);
            using (var secondKnowledgeJson = JsonDocument.Parse(secondPackage.KnowledgeSnapshotJson!))
            {
                var summary = secondKnowledgeJson.RootElement.GetProperty("knowledgeBindingSummary");
                Assert.Equal(1, summary.GetProperty("shouldEnterGateCount").GetInt32());
                Assert.Equal(1, summary.GetProperty("shouldEnterBlueprintCount").GetInt32());
                Assert.Equal(1, summary.GetProperty("shouldEnterFactSnapshotCount").GetInt32());
            }
            Assert.True(JsonContainsText(secondPackage.FactSnapshotJson, "上一章已提交"), secondPackage.FactSnapshotJson);
            Assert.True(JsonContainsText(secondPackage.FactSnapshotJson, "真实记忆"), secondPackage.FactSnapshotJson);
            Assert.True(JsonContainsText(secondPackage.FactSnapshotJson, "知识约束证据"), secondPackage.FactSnapshotJson);
            Assert.True(JsonContainsText(secondPackage.FactSnapshotJson, "旧邮路代价"), secondPackage.FactSnapshotJson);
            Assert.Contains(await verifyDb.ProductionEvents.ToListAsync(), e =>
                e.RuntimeRunId == secondRun.RunId &&
                e.EventType == "knowledge_bindings_used" &&
                e.DataJson?.Contains("knowledge-old-route-cost", StringComparison.Ordinal) == true);

            var contentResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryProjectContent",
                    Arguments = new Dictionary<string, string>
                    {
                        ["chapterNumber"] = "2",
                        ["includeBody"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = secondRun.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(contentResult.Success, contentResult.Message);
            Assert.Contains("第二章", contentResult.Message);
            Assert.Contains("第一卷：黑雨旧邮路", contentResult.Message);
            Assert.Contains("沈砚", contentResult.Message);
            Assert.Contains("真实记忆", contentResult.Message);
            Assert.Contains("事实快照", contentResult.Message);
            Assert.Contains("Gate", contentResult.Message);
            Assert.Contains("蓝图", contentResult.Message);
            Assert.Contains("FactSnapshot", contentResult.Message);
            var contentData = Assert.IsType<ProjectContentQueryResult>(contentResult.Data);
            var secondItem = Assert.Single(contentData.Items);
            Assert.Equal(secondRun.RunId, secondItem.SourceRunId);
            Assert.True(JsonContainsText(secondItem.FactSnapshotJson, "上一章已提交"), secondItem.FactSnapshotJson);
            var knowledgeBinding = Assert.Single(secondItem.KnowledgeBindings);
            Assert.Equal("classification-old-route-cost", knowledgeBinding.ClassificationId);
            Assert.True(knowledgeBinding.ShouldEnterGate);
            Assert.True(knowledgeBinding.ShouldEnterBlueprint);
            Assert.True(knowledgeBinding.ShouldEnterFactSnapshot);

            var stateResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "QueryNovelProductionState",
                    Arguments = new Dictionary<string, string>
                    {
                        ["runId"] = secondRun.RunId,
                        ["chapterNumber"] = "2",
                        ["includeEvents"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = secondRun.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(stateResult.Success, stateResult.Message);
            Assert.Contains("知识入口：绑定 1，Gate 1，蓝图 1，FactSnapshot 1", stateResult.Message);

            var validationResult = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "RunBookValidation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["startChapterNumber"] = "1",
                        ["endChapterNumber"] = "2",
                        ["includeBodyPreview"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = secondRun.RunId
                },
                await workspace.Orchestrator.GetStoryBibleAsync(CancellationToken.None),
                confirmed: true,
                CancellationToken.None);

            Assert.True(validationResult.Success, validationResult.Message);
            Assert.Contains("整书校验", validationResult.Message);
            var validationReport = Assert.IsType<BookValidationReport>(validationResult.Data);
            Assert.NotEqual("blocked", validationReport.OverallStatus);
            Assert.DoesNotContain(validationReport.Issues, issue => issue.Code == "protagonist_continuity_mismatch");
            Assert.DoesNotContain(validationReport.Issues, issue => issue.Code == "missing_chapter");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommittedChapterRevisionTools_AreDiscoverableWithClearRiskBoundaries()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var auditTool = registry.ListToolSchemas().Single(tool => tool.Name == "AuditCommittedChapter");
        var reviseTool = registry.ListToolSchemas().Single(tool => tool.Name == "ReviseCommittedChapter");

        Assert.False(auditTool.RequiresConfirmation);
        Assert.Equal("Medium", auditTool.Risk);
        Assert.Contains("已提交章节", auditTool.Description);
        Assert.Contains("chapters", auditTool.Semantic.ReadsFrom);
        Assert.Contains("content_documents", auditTool.Semantic.ReadsFrom);
        Assert.Contains("none_read_only", auditTool.Semantic.WritesTo);

        Assert.True(reviseTool.RequiresConfirmation);
        Assert.Equal("High", reviseTool.Risk);
        Assert.Contains("已提交章节", reviseTool.Description);
        Assert.Contains("chapters", reviseTool.Semantic.ReadsFrom);
        Assert.Contains("chapters", reviseTool.Semantic.WritesTo);
        Assert.Contains("content_documents", reviseTool.Semantic.WritesTo);
    }

    [Fact]
    public async Task RunBookValidation_ReadsCommittedChaptersAndReportsCrossChapterContinuity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-book-validation-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IBookValidationService, BookValidationService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "星渊邮差",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.AddRange(
                new ChapterEntity
                {
                    Id = "project-1-chapter-001",
                    ProjectId = "project-1",
                    Title = "第一章：银蓝邮徽",
                    ChapterNumber = 1,
                    Status = "committed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChapterEntity
                {
                    Id = "project-1-chapter-002",
                    ProjectId = "project-1",
                    Title = "第二章：黑雨围城",
                    ChapterNumber = 2,
                    Status = "committed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChapterEntity
                {
                    Id = "project-1-chapter-004",
                    ProjectId = "project-1",
                    Title = "第四章：断裂邮路",
                    ChapterNumber = 4,
                    Status = "planned",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var firstDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-001",
                "chapter_body",
                "第一章：银蓝邮徽",
                "第一章：银蓝邮徽\n沈砚在废弃邮局获得银蓝邮徽，结尾被迫带着银蓝邮徽冲进黑雨。",
                CancellationToken.None);
            var secondDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-002",
                "chapter_body",
                "第二章：黑雨围城",
                "第二章：黑雨围城\n林澈突然成为主角，并说自己从未见过银蓝邮徽。",
                CancellationToken.None);
            var first = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            first.CurrentDocumentId = firstDocument.Id;
            first.WordCount = 3200;
            var second = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-002");
            second.CurrentDocumentId = secondDocument.Id;
            second.WordCount = 2800;
            db.ProjectFactSnapshots.AddRange(
                new ProjectFactSnapshot
                {
                    Id = "fact-chapter-001",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    ChapterId = "project-1-chapter-001",
                    VersionNumber = 1,
                    Source = "llm_fact_writer",
                    SnapshotJson = JsonSerializer.Serialize(new
                    {
                        protagonistName = "沈砚",
                        endingState = "沈砚带着银蓝邮徽冲进黑雨",
                        nextChapterMustCarry = new[] { "沈砚必须仍持有银蓝邮徽" }
                    }),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new ProjectFactSnapshot
                {
                    Id = "fact-chapter-002",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    ChapterId = "project-1-chapter-002",
                    VersionNumber = 1,
                    Source = "llm_fact_writer",
                    SnapshotJson = JsonSerializer.Serialize(new
                    {
                        protagonistName = "林澈",
                        endingState = "林澈离开黑雨街区",
                        nextChapterMustCarry = Array.Empty<string>()
                    }),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        }

        var workspace = new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1");
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "RunBookValidation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["startChapterNumber"] = "1",
                        ["endChapterNumber"] = "4",
                        ["includeBodyPreview"] = "true"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("整书校验", result.Message);
            Assert.Contains("缺少第 3 章", result.Message);
            Assert.Contains("主角硬事实不一致", result.Message);
            var report = Assert.IsType<BookValidationReport>(result.Data);
            Assert.Equal("blocked", report.OverallStatus);
            Assert.Contains(report.Issues, issue => issue.Code == "missing_chapter" && issue.ChapterNumber == 3);
            Assert.Contains(report.Issues, issue => issue.Code == "protagonist_continuity_mismatch" && issue.ChapterNumber == 2);
            Assert.Contains("CreateRevisionPlan", result.Suggestions);
            Assert.Contains("QueryProjectContent", result.Suggestions);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunBookValidation_PersistsWorkflowAndRuntimeProgressEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-book-validation-events-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IBookValidationService, BookValidationService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IAgentRuntimeEventService, AgentRuntimeEventService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "整书校验工作流测试书",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章：银蓝邮徽",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 3200,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var document = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-001",
                "chapter_body",
                "第一章：银蓝邮徽",
                "第一章：银蓝邮徽\n沈砚在废弃邮局获得银蓝邮徽，并带着它冲进黑雨。",
                CancellationToken.None);
            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            chapter.CurrentDocumentId = document.Id;
            db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
            {
                Id = "fact-chapter-001",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-001",
                VersionNumber = 1,
                Source = "llm_fact_writer",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    protagonistName = "沈砚",
                    endingState = "沈砚带着银蓝邮徽冲进黑雨",
                    nextChapterMustCarry = new[] { "沈砚必须仍持有银蓝邮徽" }
                }),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var workspace = new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1");
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "RunBookValidation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["startChapterNumber"] = "1",
                        ["endChapterNumber"] = "1"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1",
                    ActiveRunId = "production-run-book-validation",
                    RuntimeRunId = "runtime-run-book-validation"
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var productionEvent = await verifyDb.ProductionEvents.SingleAsync(e =>
                e.RuntimeRunId == "production-run-book-validation" &&
                e.EventType == "book_validation_completed");
            Assert.Equal(NovelAgentProductionStages.ReviewCompleted, productionEvent.Stage);
            Assert.Equal("validated", productionEvent.Status);
            Assert.Equal("book_validation_report", productionEvent.ArtifactType);
            Assert.Equal("project-1", productionEvent.ArtifactId);
            Assert.Contains("整书校验", productionEvent.Message);
            Assert.Contains("overallStatus", productionEvent.DataJson);

            var runtimeEvent = await verifyDb.AgentRuntimeEvents.SingleAsync(e =>
                e.RuntimeRunId == "runtime-run-book-validation" &&
                e.Type == "production_progress" &&
                e.Stage == NovelAgentProductionStages.ReviewCompleted &&
                e.ArtifactType == "book_validation_report");
            Assert.Equal("validated", runtimeEvent.Status);
            Assert.Equal(AgentRuntimeEventSurface.Workflow, runtimeEvent.DisplaySurface);
            Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, runtimeEvent.DisplayPolicy);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunBookValidation_DefaultCallUsesFullBodyForCarryContinuity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-book-validation-full-body-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IBookValidationService, BookValidationService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "银蓝邮徽连续性测试",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.AddRange(
                new ChapterEntity
                {
                    Id = "project-1-chapter-001",
                    ProjectId = "project-1",
                    Title = "第一章：银蓝邮徽",
                    ChapterNumber = 1,
                    Status = "committed",
                    WordCount = 3200,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChapterEntity
                {
                    Id = "project-1-chapter-002",
                    ProjectId = "project-1",
                    Title = "第二章：黑雨分拣站",
                    ChapterNumber = 2,
                    Status = "committed",
                    WordCount = 3000,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var firstDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-001",
                "chapter_body",
                "第一章：银蓝邮徽",
                "第一章：银蓝邮徽\n沈砚在废弃邮局获得银蓝邮徽，结尾被迫带着银蓝邮徽冲进黑雨。",
                CancellationToken.None);
            var secondDocument = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-002",
                "chapter_body",
                "第二章：黑雨分拣站",
                "第二章：黑雨分拣站\n沈砚攥紧掌心的银蓝邮徽，确认银蓝邮徽仍在沈砚手中，才沿着旧邮路标记冲进废弃分拣站。",
                CancellationToken.None);
            var first = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
            first.CurrentDocumentId = firstDocument.Id;
            var second = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-002");
            second.CurrentDocumentId = secondDocument.Id;
            db.ProjectFactSnapshots.AddRange(
                new ProjectFactSnapshot
                {
                    Id = "fact-chapter-001",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    ChapterId = "project-1-chapter-001",
                    VersionNumber = 1,
                    Source = "llm_fact_writer",
                    SnapshotJson = JsonSerializer.Serialize(new
                    {
                        protagonistName = "沈砚",
                        endingState = "沈砚带着银蓝邮徽冲进黑雨",
                        nextChapterMustCarry = new[] { "银蓝邮徽仍在沈砚手中" }
                    }),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new ProjectFactSnapshot
                {
                    Id = "fact-chapter-002",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    ChapterId = "project-1-chapter-002",
                    VersionNumber = 1,
                    Source = "llm_fact_writer",
                    SnapshotJson = JsonSerializer.Serialize(new
                    {
                        protagonistName = "沈砚",
                        endingState = "沈砚抵达废弃分拣站，黑雨仍在身后逼近",
                        nextChapterMustCarry = Array.Empty<string>()
                    }),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        }

        var workspace = new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1");
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "RunBookValidation",
                    Arguments = new Dictionary<string, string>
                    {
                        ["startChapterNumber"] = "1",
                        ["endChapterNumber"] = "2"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var report = Assert.IsType<BookValidationReport>(result.Data);
            Assert.Equal("validated", report.OverallStatus);
            Assert.All(report.Chapters, chapter => Assert.Equal(string.Empty, chapter.BodyPreview));
            Assert.DoesNotContain(report.Issues, issue => issue.Code == "carry_not_reflected");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReviseCommittedChapter_RequiresSourceRevisionPlanId()
    {
        var registry = new AgentToolRegistry(
            CreateDbBackedSettingsManager(),
            BuildToolServiceProvider(new ServiceCollection()),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "6",
                    ["revisionGoal"] = "修掉银蓝邮徽越权问题"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("revisionPlanId", result.Message);
        Assert.Contains("CreateRevisionPlan", result.Suggestions);
        Assert.Contains("QueryRevisionPlans", result.Suggestions);
    }

    [Fact]
    public async Task ReviseCommittedChapter_BindsSourceRevisionPlanIntoContextPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-revise-committed-plan-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IRevisionPlanService, RevisionPlanService>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, NoopMemoryCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "星渊邮差",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-006",
                ProjectId = "project-1",
                Title = "第六章：潮信核心",
                ChapterNumber = 6,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.RevisionPlans.Add(new RevisionPlanEntity
            {
                Id = "revision-plan-006",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "run-revision-006",
                Source = "chapter_review",
                PlanType = "committed_chapter_revision",
                TargetScope = "chapter",
                TargetChapterId = "project-1-chapter-006",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"删除银蓝邮徽激活锈蚀功能\"]",
                ContinuityRequirementsJson = "[\"保留沈砚继续追踪空邮票\"]",
                AffectedChapterIdsJson = "[\"project-1-chapter-006\"]",
                InvalidatedPackageIdsJson = "[]",
                RiskLevel = "high",
                Recommendation = "重写第六章并覆盖书城正文。",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var document = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-006",
                "chapter_body",
                "第六章：潮信核心",
                "第六章：潮信核心\n沈砚在旧货场暴露了银蓝邮徽能激活锈蚀功能，随后继续追踪空邮票。",
                CancellationToken.None);
            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-006");
            chapter.CurrentDocumentId = document.Id;
            chapter.WordCount = 3000;
            await db.SaveChangesAsync();
        }

        var kernel = new RecordingCommittedRevisionKernel();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1"
            ), kernel);
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "ReviseCommittedChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["chapterNumber"] = "6",
                        ["revisionGoal"] = "修掉银蓝邮徽越权问题",
                        ["revisionPlanId"] = "revision-plan-006"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                new StoryBibleDocument(),
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(kernel.LastContextPackage);
            var plan = Assert.Single(kernel.LastContextPackage!.SourceRevisionPlans);
            Assert.Equal("revision-plan-006", plan.RevisionPlanId);
            Assert.Contains("删除银蓝邮徽激活锈蚀功能", plan.RequirementsJson);
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuditCommittedChapter_ReadsCommittedBodyAndReportsHardFactViolations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-tool-audit-committed-{Guid.NewGuid():N}");
        var settings = CreateDbBackedSettingsManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:StorageRoot"] = root,
                ["NovelAgent:ProjectName"] = "AgenticNovelStudio"
            })
            .Build();
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeIntentService, CreativeIntentService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProjectContentQueryService, ProjectContentQueryService>();
        services.AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>();
        services.AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChapterContextEnrichmentService>>(NullLogger<ChapterContextEnrichmentService>.Instance);
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddScoped<IProductionEventWriter, ProductionEventWriter>();
        services.AddScoped<IChapterContextPackageRecorder, ChapterContextPackageRecorder>();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = BuildToolServiceProvider(services);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            db.Users.Add(new UserEntity
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "星渊邮差",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "project-1-chapter-006",
                ProjectId = "project-1",
                Title = "第六章：潮信核心",
                ChapterNumber = 6,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var document = await contentDocuments.SaveOrReplaceTextAsync(
                "user-1",
                "project-1",
                "chapter",
                "project-1-chapter-006",
                "chapter_body",
                "第六章：潮信核心",
                "第六章：潮信核心\n沈砚在旧货场暴露了银蓝邮徽能激活锈蚀功能，随后继续追踪空邮票。",
                CancellationToken.None);
            var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-006");
            chapter.CurrentDocumentId = document.Id;
            chapter.WordCount = 3000;
            await db.SaveChangesAsync();
        }

        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(root),
            configuration,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1"
            ), new AuditViolationKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var registry = new AgentToolRegistry(
            settings,
            provider,
            NullLogger<AgentToolRegistry>.Instance);

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "AuditCommittedChapter",
                    Arguments = new Dictionary<string, string>
                    {
                        ["chapterNumber"] = "6",
                        ["focus"] = "检查硬事实边界和主角连续性"
                    }
                },
                new AgentSession
                {
                    UserId = "user-1",
                    SessionId = "session-1",
                    ActiveProjectId = "project-1"
                },
                new StoryBibleDocument
                {
                    Constitution = new StoryCreativeConstitution
                    {
                        WorldCoreRule = "银蓝邮徽只能辨认被篡改邮路，不能攻击或治愈，也不能激活锈蚀功能。"
                    },
                    ContinuityFacts =
                    {
                        new ChapterContinuityFacts
                        {
                            ChapterId = "chapter-005",
                            ProtagonistName = "沈砚",
                            EndingState = "沈砚带着银蓝邮徽抵达旧货场",
                            NextChapterMustCarry = { "继续追踪空邮票" }
                        }
                    }
                },
                confirmed: true,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("第六章：潮信核心", result.Message);
            Assert.Contains("知识库硬事实失败", result.Message);
            var execution = Assert.IsType<NovelAgentExecutionResult>(result.Data);
            Assert.Equal("gate_failed", execution.GateReport?.Status);
            Assert.Contains(execution.GateReport!.Issues, issue => issue.Contains("银蓝邮徽"));
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FormatCommittedChapterAudit_FiltersInternalRuntimeDiagnostics()
    {
        var method = typeof(AgentToolRegistry).GetMethod(
            "FormatCommittedChapterAudit",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var content = new ProjectContentQueryItem
        {
            ChapterTitle = "第六章：潮信核心"
        };
        var result = new NovelAgentExecutionResult
        {
            GateReport = new GenerationGateReport
            {
                Status = "gate_failed",
                Issues =
                {
                    "知识库硬事实失败：银蓝邮徽能力边界被改写，正文出现「银蓝邮徽能激活锈蚀」。"
                },
                RepairHints =
                {
                    "修订正文：银蓝邮徽只能辨认/溯源邮路。",
                    "Project business data is stored in SQLite, Redis, and Qdrant. Web runtime project filesystem paths are disabled."
                }
            }
        };

        var message = Assert.IsType<string>(method!.Invoke(null, new object[] { content, result }));

        Assert.Contains("银蓝邮徽能力边界", message);
        Assert.Contains("修订正文：银蓝邮徽只能辨认/溯源邮路。", message);
        Assert.DoesNotContain("Project business data", message);
        Assert.DoesNotContain("Web runtime", message);
    }

    private static AgentSession BuildCommittedChapterPolicySession()
    {
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveRunId = "run-committed"
        };
        session.WorkingMemory.MissionPlan.BookTaskTree.Volumes.Add(new AgentVolumeTask
        {
            VolumeId = "volume-1",
            Chapters =
            {
                new AgentChapterTask
                {
                    ChapterId = "chapter-006",
                    RunId = "run-committed",
                    Status = "committed",
                    GateStatus = "failed",
                    QualityStatus = "quality_failed"
                }
            }
        });
        return session;
    }

    private static StoryBibleDocument BuildCommittedChapterPolicyBible() => new()
    {
        AgentRuns =
        {
            new NovelAgentRun
            {
                RunId = "run-committed",
                TargetChapterId = "chapter-006",
                DraftArtifact = new ChapterDraftArtifact
                {
                    ChapterId = "chapter-006",
                    DraftContent = "第六章正文\n<chapter_changes>{}</chapter_changes>"
                },
                GateReport = new GenerationGateReport
                {
                    Status = "failed",
                    Issues = { "银蓝邮徽能力越界" }
                }
            }
        }
    };

    private sealed class UnsupportedProductionKernel : ITianmingProductionKernel
    {
        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            throw new NotSupportedException("This test does not execute the production kernel.");

        public string StripChanges(string content) => content;
    }

    private sealed class ShortDraftPassingKernel : ITianmingProductionKernel
    {
        public TimeSpan DraftDelay { get; init; } = TimeSpan.Zero;

        public TimeSpan CommitDelay { get; init; } = TimeSpan.Zero;

        public int GenerateDraftCallCount { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界。");
            return Task.FromResult(package);
        }

        public async Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default)
        {
            GenerateDraftCallCount++;
            if (DraftDelay > TimeSpan.Zero)
                await Task.Delay(DraftDelay, ct).ConfigureAwait(false);

            return new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = "<chapter_changes>{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近\"],\"foreshadows\":[\"银蓝邮徽\"]}</chapter_changes>",
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近\"],\"foreshadows\":[\"银蓝邮徽\"]}",
                HasChanges = true
            };
        }

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public async Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (CommitDelay > TimeSpan.Zero)
                await Task.Delay(CommitDelay, ct).ConfigureAwait(false);

            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核提交完成。"
            };
        }

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content)
        {
            return ChapterChangesText.StripChanges(content);
        }
    }

    private sealed class ReviewFeedbackRewriteKernel : ITianmingProductionKernel
    {
        private readonly IGeneratedContentService _generatedContentService;

        public ReviewFeedbackRewriteKernel(IGeneratedContentService generatedContentService)
        {
            _generatedContentService = generatedContentService;
        }

        public int GenerateDraftCallCount { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界，并沿旧邮路撤离。");
            return Task.FromResult(package);
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default)
        {
            GenerateDraftCallCount++;
            var hasReviewFeedback = package.Warnings.Any(w => w.Contains("AgentReview修订要求", StringComparison.Ordinal)) ||
                                    package.HardContinuityFacts.Any(f => f.Contains("AgentReview修订要求", StringComparison.Ordinal));
            var draft = hasReviewFeedback
                ? PassingReviewDraftKernel.BuildPassingDraft(run, package)
                : "<chapter_changes>{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近\"],\"foreshadows\":[\"银蓝邮徽\"]}</chapter_changes>";

            return Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = draft,
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"银蓝邮徽只能辨认旧邮路\"]}",
                HasChanges = true
            });
        }

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public async Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            await _generatedContentService.SaveChapterAsync(run.TargetChapterId, StripChanges(draft.DraftContent))
                .ConfigureAwait(false);
            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核已按 AgentReview 反馈修订并提交。"
            };
        }

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content) => ChapterChangesText.StripChanges(content);
    }

    private sealed class ReviewRewriteHttpFailureKernel : ITianmingProductionKernel
    {
        public int GenerateDraftCallCount { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界，并沿旧邮路撤离。");
            return Task.FromResult(package);
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default)
        {
            GenerateDraftCallCount++;
            if (GenerateDraftCallCount > 1)
                throw new System.Net.Http.HttpRequestException("simulated provider disconnect during review rewrite");

            return Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = "<chapter_changes>{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近\"],\"foreshadows\":[\"银蓝邮徽\"]}</chapter_changes>",
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"银蓝邮徽只能辨认旧邮路\"]}",
                HasChanges = true
            });
        }

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核提交完成。"
            });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content) => ChapterChangesText.StripChanges(content);
    }

    private sealed class HangingSecondReviewRewriteKernel : ITianmingProductionKernel
    {
        private readonly IGeneratedContentService _generatedContentService;

        public HangingSecondReviewRewriteKernel(IGeneratedContentService generatedContentService)
        {
            _generatedContentService = generatedContentService;
        }

        public int GenerateDraftCallCount { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界，并沿旧邮路撤离。");
            return Task.FromResult(package);
        }

        public async Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default)
        {
            GenerateDraftCallCount++;
            if (GenerateDraftCallCount > 1)
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

            var content = """
                陈默在旧邮路边缘醒来时，手臂的旧伤还在发烫。城里的规则已经变了，废弃邮站不再按白天黑夜开门，而是按银蓝邮徽上残留的温度决定通行。

                他没有试图用邮徽攻击任何东西，只把它贴近墙缝，确认那些被篡改过的邮路痕迹。每一次确认都让伤口更疼，也让他意识到后续选择会带来更大的代价。
                """;

            return new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = content + "\n<chapter_changes>{\"characters\":[\"陈默\"],\"conflicts\":[\"旧邮路规则改变\"],\"foreshadows\":[\"银蓝邮徽\"]}</chapter_changes>",
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"旧邮路规则改变\"],\"foreshadows\":[\"银蓝邮徽\"]}",
                HasChanges = true
            };
        }

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public async Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            await _generatedContentService.SaveChapterAsync(run.TargetChapterId, StripChanges(draft.DraftContent))
                .ConfigureAwait(false);
            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核提交完成。"
            };
        }

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content) => ChapterChangesText.StripChanges(content);
    }

    private sealed class BlockedGenerationKernel : ITianmingProductionKernel
    {
        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界。");
            return Task.FromResult(package);
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "blocked_missing_llm_settings",
                DraftContent = string.Empty,
                ChangesJson = string.Empty,
                HasChanges = false
            });

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "gate_failed",
                ProtocolPassed = false,
                ChangesDetected = false,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true,
                Issues = { "模型配置缺失，无法生成正文。" }
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport
            {
                Status = "blocked",
                Summary = "模型配置缺失，不能提交。"
            });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "gate_failed" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = false,
                Message = "模型配置缺失，不能修订。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "blocked",
                Summary = "模型配置缺失，未刷新索引。"
            };

        public string StripChanges(string content) => content;
    }

    private sealed class RepairThenPassKernel : ITianmingProductionKernel
    {
        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界，并沿旧邮路撤离。");
            return Task.FromResult(package);
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = "第一章：黑雨门槛\n陈默握紧旧邮徽。\n<chapter_changes>{}</chapter_changes>",
                ChangesJson = "{}",
                HasChanges = true
            });

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (string.Equals(draft.Status, "draft_repaired", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new GenerationGateReport
                {
                    Status = "validated",
                    ProtocolPassed = true,
                    ChangesDetected = true,
                    FactSnapshotPassed = true,
                    BlueprintPassed = true,
                    RagPassed = true
                });
            }

            var gate = new GenerationGateReport
            {
                Status = "gate_failed",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = false,
                BlueprintPassed = false,
                RagPassed = true
            };
            gate.Issues.Add("缺少蓝图承接：陈默确认邮徽边界");
            gate.RepairHints.Add("补入邮徽边界与黑雨冲突。");
            return Task.FromResult(gate);
        }

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_repaired",
                DraftContent = PassingReviewDraftKernel.BuildPassingDraft(run),
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"银蓝邮徽只能辨认旧邮路\"]}",
                HasChanges = true
            });

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport { Status = "clean", Summary = "测试提交完成。" });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult { Success = true, Message = "测试修订完成。" });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new() { Status = "clean", Summary = "测试索引刷新完成。" };

        public string StripChanges(string content) => ChapterChangesText.StripChanges(content);

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }
    }

    private sealed class AuditViolationKernel : ITianmingProductionKernel
    {
        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            });

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact { ChapterId = run.TargetChapterId, Status = "draft_generated" });

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport { Status = "clean", Summary = "测试提交完成。" });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default)
        {
            var report = new GenerationGateReport
            {
                Status = "gate_failed",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = false,
                BlueprintPassed = true,
                RagPassed = false
            };
            report.Issues.Add("知识库硬事实失败：银蓝邮徽不能激活锈蚀功能。");
            report.RepairHints.Add("修订章节，删除银蓝邮徽激活锈蚀功能的描述。");
            return Task.FromResult(report);
        }

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content) => content;
    }

    private sealed class RecordingCommittedRevisionKernel : ITianmingProductionKernel
    {
        public ChapterContextPackageSummary? LastContextPackage { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            });

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact { ChapterId = run.TargetChapterId, Status = "draft_generated" });

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport { Status = "clean", Summary = "测试提交完成。" });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default)
        {
            LastContextPackage = contextPackage;
            return Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                RiskLevel = NovelToolRiskLevel.High,
                Message = "测试修订完成。",
                ContextPackage = contextPackage,
                DraftArtifact = new ChapterDraftArtifact
                {
                    ChapterId = chapterId,
                    Status = "committed_revision",
                    CommittedContent = committedContent
                },
                GateReport = new GenerationGateReport { Status = "validated" },
                Run = new NovelAgentRun
                {
                    RunId = "run-recording-revision",
                    TargetChapterId = chapterId,
                    ContextPackage = contextPackage,
                    Status = NovelAgentRunStatus.Completed
                }
            });
        }

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new() { Status = "clean", Summary = "测试索引刷新完成。" };

        public string StripChanges(string content) => content;
    }

    private sealed class PassingUnifiedValidationService : IUnifiedValidationService
    {
        public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default) =>
            Task.FromResult(new ChapterValidationResult
            {
                ChapterId = chapterId,
                OverallResult = "通过"
            });

        public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default) =>
            Task.FromResult(new VolumeValidationResult
            {
                VolumeNumber = volumeNumber
            });

        public Task<bool> NeedsRepublishAsync() => Task.FromResult(false);
    }

    private sealed class PassingReviewDraftKernel : ITianmingProductionKernel
    {
        private readonly IGeneratedContentService? _generatedContentService;

        public TimeSpan ContextPackageDelay { get; init; } = TimeSpan.Zero;
        public TimeSpan GateValidationDelay { get; init; } = TimeSpan.Zero;
        public TimeSpan CommitDelay { get; init; } = TimeSpan.Zero;

        public PassingReviewDraftKernel(IGeneratedContentService? generatedContentService = null)
        {
            _generatedContentService = generatedContentService;
        }

        public async Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            if (ContextPackageDelay > TimeSpan.Zero)
                await Task.Delay(ContextPackageDelay, ct).ConfigureAwait(false);

            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认和溯源旧邮路，不能攻击。");
            package.CharacterStates.Add("陈默是废土邮差，刚拿到银蓝邮徽。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ChapterBlueprints.Add("陈默确认银蓝邮徽边界，并沿旧邮路撤离。");
            return package;
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = BuildPassingDraft(run, package),
                ChangesJson = "{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"银蓝邮徽只能辨认旧邮路\"]}",
                HasChanges = true
            });

        public async Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (GateValidationDelay > TimeSpan.Zero)
                await Task.Delay(GateValidationDelay, ct).ConfigureAwait(false);

            return new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            };
        }

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public async Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (CommitDelay > TimeSpan.Zero)
                await Task.Delay(CommitDelay, ct).ConfigureAwait(false);

            if (_generatedContentService != null)
                return await CommitWithGeneratedContentAsync(run, draft).ConfigureAwait(false);

            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核提交完成。"
            };
        }

        private async Task<DependencyImpactReport> CommitWithGeneratedContentAsync(
            NovelAgentRun run,
            ChapterDraftArtifact draft)
        {
            await _generatedContentService!.SaveChapterAsync(run.TargetChapterId, StripChanges(draft.DraftContent))
                .ConfigureAwait(false);
            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "测试内核已写入书城。"
            };
        }

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                Message = "测试修订完成。"
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Status = "clean",
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content)
        {
            return ChapterChangesText.StripChanges(content);
        }

        public static string BuildPassingDraft(NovelAgentRun run, ChapterContextPackageSummary? package = null)
        {
            var brief = run.ChapterBrief;
            var coreIdea = FirstNonEmpty(brief?.CoreIdea, "陈默确认银蓝邮徽只能辨认和溯源旧邮路，不能攻击");
            var conflictMove = FirstNonEmpty(brief?.ConflictMove, "黑雨逼近维修站，旧邮路被篡改");
            var characterChoice = FirstNonEmpty(brief?.CharacterChoice, "陈默选择承担肩伤代价，把银蓝邮徽贴近门缝辨认旧邮路");
            var cost = FirstNonEmpty(brief?.CostOrConsequence, "肩伤、暴露风险和后续债务");
            var foreshadow = FirstNonEmpty(brief?.ForeshadowingAction, "银蓝邮徽留下第九枚空邮票线索");
            var worldbuilding = FirstNonEmpty(brief?.WorldbuildingGap, "废土邮路规则要求每次破局都留下可追索印记");
            var knowledgeAnchor = package?.KnowledgeBindings
                .Where(binding => string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
                .Select(binding => FirstNonEmpty(binding.Content, binding.Title))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。";

            var body = string.Join("\n\n", new[]
            {
                $"第一章：黑雨门槛。{coreIdea}。维修站外的黑雨压低铁棚，{conflictMove}，陈默没有把邮徽当武器，而是先确认它只会显出旧邮路的痕迹。",
                $"他把这条知识边界压进每一次判断：{knowledgeAnchor}",
                $"{characterChoice}。这一次选择让他赢下小局：他找到了被刮掉的邮路编号，夺回撤离路线，也用证据证明追踪者改写过墙上的路标。",
                $"代价并不轻，{cost}。邮徽边缘的蓝磷骨光暴露了他的位置，追踪者记住了他的名字，维修站同伴也因他的选择产生信任与动摇。",
                $"{worldbuilding}。陈默由此明白，旧邮路不是普通地图，而是一套会记录选择、惩罚逃避者的组织规则；他必须在三日内把线索送出，否则黑雨会吞掉整条维修街。",
                $"{foreshadow}。门后传来低语，新的钥匙和异常痕迹同时出现，最后一枚空邮票上浮出一个问题：如果邮徽只能辨认旧路，谁正在替旧路改写终点？"
            });

            return body +
                "\n\n<chapter_changes>{\"characters\":[\"陈默\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"银蓝邮徽只能辨认旧邮路\"]}</chapter_changes>";
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed class DelayedPassingEditorialReviewModelClient : IAgentEditorialReviewModelClient
    {
        private readonly TimeSpan _delay;

        public DelayedPassingEditorialReviewModelClient(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task<AgentEditorialReviewDecision> ReviewAsync(
            AgentEditorialReviewRequest request,
            CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct).ConfigureAwait(false);

            return new AgentEditorialReviewDecision
            {
                MeetsUserIntent = true,
                MeetsRevisionPlan = true,
                MeetsProjectPromise = true,
                CreativeFit = "pass",
                Decision = "pass",
                Evidence = { "正文承接用户目标与作品承诺。" }
            };
        }
    }

    private sealed class DelayedChapterCommitTruthRecorder : IChapterCommitTruthRecorder
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowAfterRelease { get; init; }
        public bool HasStarted => _started.Task.IsCompleted;

        public async Task<ChapterCommitTruthRecord> RecordAsync(
            RecordChapterCommitTruthRequest request,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (ThrowAfterRelease)
                throw new InvalidOperationException("测试 post-commit 真相登记失败。");

            return new ChapterCommitTruthRecord(
                new ChapterEntity
                {
                    Id = request.TargetChapterId ?? "chapter-001",
                    ProjectId = request.ProjectId,
                    Title = request.TargetChapterId ?? "chapter-001"
                },
                new ChapterVersion
                {
                    Id = "delayed-version",
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    ChapterId = request.TargetChapterId ?? "chapter-001",
                    VersionNumber = 1,
                    Status = "committed"
                },
                new ProjectFactSnapshot
                {
                    Id = "delayed-snapshot",
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    ChapterId = request.TargetChapterId ?? "chapter-001",
                    VersionNumber = 1,
                    Source = "chapter_commit"
                },
                new ProductionEvent
                {
                    Id = "delayed-event",
                    RuntimeRunId = request.RuntimeRunId,
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    ChapterId = request.TargetChapterId ?? "chapter-001",
                    EventType = "chapter_committed",
                    Stage = NovelAgentProductionStages.ChapterCommit,
                    Status = "completed"
                });
        }

        public async Task WaitUntilStartedAsync(TimeSpan timeout)
        {
            await _started.Task.WaitAsync(timeout).ConfigureAwait(false);
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class BusyChapterProductionLeaseService : IChapterProductionLeaseService
    {
        public string LastProjectId { get; private set; } = string.Empty;
        public string LastChapterId { get; private set; } = string.Empty;

        public Task<ChapterProductionLease?> TryAcquireAsync(
            string userId,
            string projectId,
            string chapterId,
            string runtimeRunId,
            CancellationToken cancellationToken = default)
        {
            LastProjectId = projectId;
            LastChapterId = chapterId;
            return Task.FromResult<ChapterProductionLease?>(null);
        }
    }

    private sealed class CountingProductionKernel : ITianmingProductionKernel
    {
        public int BuildContextPackageCalls { get; private set; }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            BuildContextPackageCalls++;
            return Task.FromResult(new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            });
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                DraftContent = PassingReviewDraftKernel.BuildPassingDraft(run, contextPackage),
                ChangesJson = "{\"characters\":[\"陈默\"]}",
                HasChanges = true,
                Status = "draft_ready"
            });

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true,
                ChangesDetected = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new DependencyImpactReport
            {
                Summary = "测试提交完成。"
            });

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult
            {
                Success = true,
                DraftArtifact = new ChapterDraftArtifact
                {
                    ChapterId = chapterId,
                    DraftContent = committedContent,
                    Status = "revised"
                }
            });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new()
            {
                Summary = "测试索引刷新完成。"
            };

        public string StripChanges(string content) =>
            ChapterChangesText.StripChanges(content);
    }

    private sealed class TwoChapterContinuityKernel : ITianmingProductionKernel
    {
        private readonly IGeneratedContentService _generatedContentService;

        public TwoChapterContinuityKernel(IGeneratedContentService generatedContentService)
        {
            _generatedContentService = generatedContentService;
        }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "built",
                PackageId = $"pkg-{run.RunId}"
            };
            package.WorldRules.Add("银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。");
            package.CharacterStates.Add("沈砚：废土邮差，正在学习银蓝邮徽的真实边界。");
            package.ActiveConflicts.Add("黑雨逼近维修站，旧邮路被篡改。");
            package.ActiveForeshadowing.Add("空邮票终点被人改写。");
            package.ChapterBlueprints.Add("沈砚必须通过银蓝邮徽辨认旧邮路，而不是把邮徽当武器。");

            if (run.TargetChapterId.Contains("002", StringComparison.OrdinalIgnoreCase))
            {
                package.PreviousSummaries.Add("上一章已提交：第一章：黑雨门槛");
                package.HardContinuityFacts.Add("上一章结尾状态：沈砚在旧站台门后听见有人低声喊出他的名字。");
                package.HardContinuityFacts.Add("下一章必须承接：沈砚已经付出一段真实记忆作为旧邮路通行费。");
                package.CharacterStates.Add("沈砚：失去一段与母亲邮袋有关的真实记忆，但仍握着银蓝邮徽。");
                package.ActiveConflicts.Add("追踪者已经记住沈砚的名字。");
            }

            return Task.FromResult(package);
        }

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default)
        {
            var isSecond = run.TargetChapterId.Contains("002", StringComparison.OrdinalIgnoreCase);
            var brief = run.ChapterBrief;
            var coreIdea = FirstNonEmpty(brief?.CoreIdea, run.UserGoal);
            var conflictMove = FirstNonEmpty(brief?.ConflictMove, "旧邮路被篡改的冲突发生可追踪变化。");
            var characterChoice = FirstNonEmpty(brief?.CharacterChoice, "沈砚主动选择承担代价，用银蓝邮徽辨认旧邮路。");
            var cost = FirstNonEmpty(brief?.CostOrConsequence, "付出真实记忆，同时留下下一章必须处理的新压力。");
            var foreshadow = FirstNonEmpty(brief?.ForeshadowingAction, "空邮票终点被改写的伏笔继续强化。");
            var worldbuilding = FirstNonEmpty(brief?.WorldbuildingGap, "旧邮路规则要求每次通行都留下可追索代价。");
            var knowledge = package.KnowledgeBindings
                .Where(binding => string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
                .Select(binding => string.IsNullOrWhiteSpace(binding.Content) ? binding.Title : binding.Content)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?? "旧邮路每次开启都必须付出一段真实记忆作为通行费。";
            var carry = package.HardContinuityFacts
                .FirstOrDefault(fact => fact.Contains("上一章结尾状态", StringComparison.Ordinal))
                ?? "上一章结尾状态：沈砚在旧站台门后听见有人低声喊出他的名字。";
            var title = isSecond ? "第二章：旧站台回声" : "第一章：黑雨门槛";
            var body = isSecond
                ? string.Join("\n\n", new[]
                {
                    $"{title}。{carry}沈砚没有装作没听见，他把银蓝邮徽压在掌心，确认它只能辨认旧邮路，不能攻击、治愈或升级。",
                    $"本章核心创意落在这一场选择上：{coreIdea}。他记得上一章刚付出的代价：{knowledge}那段关于母亲邮袋的真实记忆已经缺了一角，越追问越空。",
                    $"冲突推进必须发生在现场：{conflictMove}追踪者从黑雨里逼近并喊出沈砚的名字，{characterChoice}",
                    $"沈砚没有让邮徽战斗，只让它照出被篡改的站台编号，夺回撤离路线，带着同伴沿旧邮路撤入废弃月台，终于赢下这一小局完成破局。",
                    $"代价继续发酵：{cost}他能说出邮袋的重量，却想不起母亲最后一次把邮袋递给他的表情。",
                    $"{worldbuilding}{foreshadow}门后低语再次响起，空邮票终点被改写的痕迹更清楚了。"
                })
                : string.Join("\n\n", new[]
                {
                    $"{title}。黑雨压住维修站棚顶，沈砚第一次握住银蓝邮徽，确认它只能辨认旧邮路，不能攻击、治愈或升级。",
                    $"本章核心创意落在这一场选择上：{coreIdea}。他开启旧邮路前读懂了硬规则：{knowledge}",
                    $"冲突推进必须发生在现场：{conflictMove}于是沈砚付出一段关于母亲邮袋的真实记忆，换来被篡改邮路的蓝磷痕迹。",
                    $"{characterChoice}他靠邮徽辨认出旧站台方向，夺回被黑雨切断的撤离路线，从异兽围堵里破局脱身，证明邮徽的价值不在攻击而在辨认真路。",
                    $"代价并没有结束：{cost}追踪者看见蓝光，第一次记住了他的名字。",
                    $"{worldbuilding}{foreshadow}门后传来低语，空邮票上浮出一行字：如果邮徽只能辨认旧路，谁正在替旧路改写终点？"
                });

            return Task.FromResult(new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = body +
                    "\n\n<chapter_changes>{\"characters\":[\"沈砚\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"空邮票终点被改写\"],\"memoryCosts\":[\"真实记忆作为通行费\"]}</chapter_changes>",
                ChangesJson = "{\"characters\":[\"沈砚\"],\"conflicts\":[\"黑雨逼近维修站，旧邮路被篡改\"],\"foreshadows\":[\"空邮票终点被改写\"],\"memoryCosts\":[\"真实记忆作为通行费\"]}",
                HasChanges = true
            });
        }

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport
            {
                Status = "validated",
                ProtocolPassed = true,
                ChangesDetected = true,
                FactSnapshotPassed = true,
                BlueprintPassed = true,
                RagPassed = true
            });

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            Task.FromResult(draft);

        public async Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            await _generatedContentService
                .SaveChapterAsync(run.TargetChapterId, StripChanges(draft.DraftContent))
                .ConfigureAwait(false);
            return new DependencyImpactReport
            {
                Status = "clean",
                Summary = "两章 E2E 测试内核已写入书城。"
            };
        }

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            Task.FromResult(new GenerationGateReport { Status = "validated" });

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            Task.FromResult(new NovelAgentExecutionResult { Success = true, Message = "测试修订完成。" });

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            new() { Status = "clean", Summary = "测试索引刷新完成。" };

        public string StripChanges(string content) => ChapterChangesText.StripChanges(content);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly string _userId;

        public FixedCurrentUserService(string userId)
        {
            _userId = userId;
        }

        public string GetUserId() => _userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => _userId;
    }

    private sealed class FixedBackgroundUserContext : IBackgroundUserContext
    {
        public FixedBackgroundUserContext(string userId)
        {
            Current = new BackgroundUserSnapshot(userId, "author", "author@example.com", "author");
        }

        public BackgroundUserSnapshot? Current { get; }

        public IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author") =>
            new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class RecordingToolSearchCacheService : IToolSearchCacheService
    {
        public Task<ToolSearchCacheLookup> GetAsync(
            AgentSession session,
            string phase,
            string toolCatalogSignature,
            CancellationToken ct = default) =>
            Task.FromResult(ToolSearchCacheLookup.Miss);

        public Task SaveAsync(
            AgentSession session,
            string phase,
            IReadOnlyList<ToolSchema> tools,
            string toolCatalogSignature,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAgentToolExecutionLedger : IAgentToolExecutionLedger
    {
        private readonly Dictionary<string, AgentToolExecutionEntity> _executions = new(StringComparer.OrdinalIgnoreCase);
        public List<AgentToolExecutionStart> Starts { get; } = new();

        public Task<AgentToolExecutionEntity> StartAsync(AgentToolExecutionStart start, CancellationToken ct = default)
        {
            Starts.Add(start);
            var execution = new AgentToolExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = start.UserId,
                ProjectId = start.ProjectId,
                SessionId = start.SessionId,
                RunId = start.RunId,
                ToolName = start.Call.Name,
                Phase = start.Phase,
                Risk = start.Risk,
                ArgumentsHash = Guid.NewGuid().ToString("N"),
                Status = "running",
                StartedAt = DateTime.UtcNow
            };
            _executions[execution.Id] = execution;
            return Task.FromResult(execution);
        }

        public Task RebindProjectAsync(string executionId, string projectId, CancellationToken ct = default)
        {
            if (_executions.TryGetValue(executionId, out var execution))
                execution.ProjectId = projectId;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(string executionId, AgentToolExecutionResult result, CancellationToken ct = default)
        {
            if (_executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = result.Success ? "completed" : "failed";
                execution.ResultPhase = result.Phase;
                execution.ResultMessage = result.Message;
                execution.RecommendedNextTool = result.RecommendedToolName;
                execution.MissingPrerequisite = result.MissingPrerequisite;
                execution.CompletedAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task<int> FailRunningForSessionAsync(
            string userId,
            string sessionId,
            string? projectId,
            string reason,
            CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> FailAllRunningAsync(string reason, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<AgentToolExecutionSnapshot>> GetRecentAsync(
            string userId,
            string sessionId,
            string? projectId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentToolExecutionSnapshot>>(Array.Empty<AgentToolExecutionSnapshot>());
    }

    private sealed class NoopDistributedCacheService : IDistributedCacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
            Task.FromResult<T?>(null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class NoopMemoryCacheService : IMemoryCacheService
    {
        public async Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            await factory().ConfigureAwait(false);

        public T? Get<T>(string key) => default;

        public void Set<T>(string key, T value, TimeSpan expiration) { }

        public void Remove(string key) { }

        public void RemoveByPrefix(string keyPrefix) { }
    }

    private sealed class FixedKnowledgeService : IKnowledgeService
    {
        private readonly KnowledgeSearchResult _result;

        public FixedKnowledgeService(KnowledgeSearchResult result)
        {
            _result = result;
        }

        public Task<KnowledgeResponse> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(CreateExtractedKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeDirectoryResponse>> ListKnowledgeDirectoriesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeDirectoryResponse> CreateKnowledgeDirectoryAsync(CreateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeDirectoryResponse> UpdateKnowledgeDirectoryAsync(string key, UpdateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteKnowledgeDirectoryAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> UpdateKnowledgeAsync(string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken ct = default) =>
            Task.FromResult(new List<KnowledgeSearchResult> { _result });

        public Task IncrementUsageAsync(
            string knowledgeId,
            string projectId,
            string? sessionId = null,
            string? runId = null,
            string? idempotencyKey = null,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyKnowledgeService : IKnowledgeService
    {
        public Task<KnowledgeResponse> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(CreateExtractedKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeDirectoryResponse>> ListKnowledgeDirectoriesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeDirectoryResponse> CreateKnowledgeDirectoryAsync(CreateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeDirectoryResponse> UpdateKnowledgeDirectoryAsync(string key, UpdateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteKnowledgeDirectoryAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<KnowledgeResponse> UpdateKnowledgeAsync(string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken ct = default) =>
            Task.FromResult(new List<KnowledgeSearchResult>());

        public Task IncrementUsageAsync(
            string knowledgeId,
            string projectId,
            string? sessionId = null,
            string? runId = null,
            string? idempotencyKey = null,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedKnowledgeClassificationModelClient : IKnowledgeClassificationModelClient
    {
        public Task<KnowledgeClassificationDecision> ClassifyAsync(
            KnowledgeClassificationPrompt prompt,
            CancellationToken ct = default)
        {
            Assert.Equal("user-1", prompt.UserId);
            Assert.Equal("project-1", prompt.ProjectId);
            Assert.Equal("knowledge-1", prompt.KnowledgeId);
            Assert.Contains("银蓝邮徽", prompt.KnowledgeContent);
            return Task.FromResult(new KnowledgeClassificationDecision
            {
                Model = "test-llm",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 91,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                TargetEntities = new List<string> { "银蓝邮徽" },
                Rule = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                ShouldEnterGate = true,
                ShouldEnterBlueprint = true,
                ShouldEnterFactSnapshot = true,
                Confidence = 0.9,
                RawJson = """
                {
                  "role": "ItemRule",
                  "scope": "ProjectWide",
                  "priority": 91,
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter",
                  "targetEntities": ["银蓝邮徽"],
                  "rule": "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true,
                  "confidence": 0.9
                }
                """
            });
        }
    }

    private sealed class RecordingKnowledgeProcessingService : IKnowledgeProcessingService
    {
        private readonly NovelAgentDbContext _db;

        public RecordingKnowledgeProcessingService(NovelAgentDbContext db)
        {
            _db = db;
        }

        public async Task<string> ProcessPendingFileAsync(
            string taskId,
            string userId,
            CancellationToken ct = default,
            KnowledgeProcessingProgressContext? progress = null)
        {
            var task = await _db.KnowledgeProcessingTasks.SingleAsync(item => item.Id == taskId && item.UserId == userId, ct);
            task.Status = "completed";
            task.Progress = 100;
            task.ExtractedEntriesCount = 2;
            task.CompletedAt = DateTime.UtcNow;

            _db.KnowledgeBases.AddRange(
                new KnowledgeBaseEntity
                {
                    Id = "knowledge-route-cost",
                    UserId = userId,
                    SourceProjectId = task.ProjectId,
                    EntryType = "HardFact",
                    Title = "旧邮路记忆代价",
                    Content = "每次开启旧邮路都要交出一段真实记忆。",
                    Tags = "[\"旧邮路\",\"代价\"]",
                    Weight = 10,
                    SourceType = "upload",
                    SourceUploadTaskId = task.Id,
                    ChunkIndex = 0,
                    CreatedAt = DateTime.UtcNow
                },
                new KnowledgeBaseEntity
                {
                    Id = "knowledge-badge-boundary",
                    UserId = userId,
                    SourceProjectId = task.ProjectId,
                    EntryType = "HardFact",
                    Title = "银蓝邮徽边界",
                    Content = "银蓝邮徽只能识路，不能攻击、治疗或升级。",
                    Tags = "[\"银蓝邮徽\",\"能力边界\"]",
                    Weight = 10,
                    SourceType = "upload",
                    SourceUploadTaskId = task.Id,
                    ChunkIndex = 1,
                    CreatedAt = DateTime.UtcNow
                });
            _db.ProjectKnowledgeUsages.AddRange(
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-route-cost",
                    UserId = userId,
                    ProjectId = task.ProjectId!,
                    KnowledgeId = "knowledge-route-cost",
                    Status = "imported",
                    SourceRunId = $"knowledge_upload:{task.Id}",
                    UsageCount = 0,
                    FirstSeenAt = DateTime.UtcNow
                },
                new ProjectKnowledgeUsageEntity
                {
                    Id = "usage-badge-boundary",
                    UserId = userId,
                    ProjectId = task.ProjectId!,
                    KnowledgeId = "knowledge-badge-boundary",
                    Status = "imported",
                    SourceRunId = $"knowledge_upload:{task.Id}",
                    UsageCount = 0,
                    FirstSeenAt = DateTime.UtcNow
                });

            await _db.SaveChangesAsync(ct);
            return "知识文件 旧邮路设定.md 已完成解析，提取 2 条知识。";
        }

        public Task<string> ProcessFileAsync(
            string taskId,
            CancellationToken ct = default,
            KnowledgeProcessingProgressContext? progress = null) =>
            ProcessPendingFileAsync(taskId, "user-1", ct, progress);
    }

    private sealed class ImportedKnowledgeClassificationModelClient : IKnowledgeClassificationModelClient
    {
        public Task<KnowledgeClassificationDecision> ClassifyAsync(
            KnowledgeClassificationPrompt prompt,
            CancellationToken ct = default)
        {
            Assert.Equal("user-1", prompt.UserId);
            Assert.Equal("project-1", prompt.ProjectId);
            Assert.Contains("knowledge-", prompt.KnowledgeId);
            return Task.FromResult(new KnowledgeClassificationDecision
            {
                Model = "test-import-classifier",
                Role = "HardFact",
                Scope = "ProjectWide",
                Priority = 96,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "AlwaysInclude",
                TargetEntities = new List<string> { "旧邮路", "银蓝邮徽" },
                Rule = $"{prompt.KnowledgeTitle} 必须进入章节生产包、门禁和事实快照。",
                ShouldEnterGate = true,
                ShouldEnterBlueprint = true,
                ShouldEnterFactSnapshot = true,
                Confidence = 0.94,
                RawJson = """
                {
                  "role": "HardFact",
                  "scope": "ProjectWide",
                  "priority": 96,
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "AlwaysInclude",
                  "targetEntities": ["旧邮路", "银蓝邮徽"],
                  "rule": "导入知识必须进入章节生产包、门禁和事实快照。",
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true,
                  "confidence": 0.94
                }
                """
            });
        }
    }

    private sealed class NoConflictKnowledgeConflictModelClient : IKnowledgeConflictModelClient
    {
        public Task<KnowledgeConflictDecision> DetectAsync(
            KnowledgeConflictPrompt prompt,
            CancellationToken ct = default)
        {
            Assert.Equal("user-1", prompt.UserId);
            Assert.Equal("project-1", prompt.ProjectId);
            Assert.False(string.IsNullOrWhiteSpace(prompt.Candidate.KnowledgeId));
            return Task.FromResult(new KnowledgeConflictDecision
            {
                Model = "test-no-conflict-detector",
                HasConflict = false,
                ConflictType = "None",
                Severity = "None",
                ImpactScope = "ProjectWide",
                ConflictingKnowledgeIds = new List<string>(),
                Explanation = "未发现与当前项目知识的硬冲突。",
                RecommendedAction = "可以继续构建章节生产包。",
                RequiresUserDecision = false,
                RawJson = """
                {
                  "hasConflict": false,
                  "conflictType": "None",
                  "severity": "None",
                  "impactScope": "ProjectWide",
                  "conflictingKnowledgeIds": [],
                  "explanation": "未发现与当前项目知识的硬冲突。",
                  "recommendedAction": "可以继续构建章节生产包。",
                  "requiresUserDecision": false
                }
                """
            });
        }
    }

    private sealed class FixedKnowledgeConflictModelClient : IKnowledgeConflictModelClient
    {
        public Task<KnowledgeConflictDecision> DetectAsync(
            KnowledgeConflictPrompt prompt,
            CancellationToken ct = default)
        {
            Assert.Equal("user-1", prompt.UserId);
            Assert.Equal("project-1", prompt.ProjectId);
            Assert.Equal("knowledge-blue-flame", prompt.Candidate.KnowledgeId);
            Assert.Contains(prompt.ExistingKnowledge, item => item.KnowledgeId == "knowledge-boundary");
            return Task.FromResult(new KnowledgeConflictDecision
            {
                Model = "test-conflict-llm",
                HasConflict = true,
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                ConflictingKnowledgeIds = new List<string> { "knowledge-boundary" },
                Explanation = "银蓝邮徽不能攻击与银蓝邮徽释放蓝焰攻击互相冲突。",
                RecommendedAction = "询问用户保留能力边界，还是修改新知识为非攻击表现。",
                RequiresUserDecision = true,
                RawJson = """
                {
                  "hasConflict": true,
                  "conflictType": "HardConstraintContradiction",
                  "severity": "Hard",
                  "impactScope": "ProjectWide",
                  "conflictingKnowledgeIds": ["knowledge-boundary"],
                  "explanation": "银蓝邮徽不能攻击与银蓝邮徽释放蓝焰攻击互相冲突。",
                  "recommendedAction": "询问用户保留能力边界，还是修改新知识为非攻击表现。",
                  "requiresUserDecision": true
                }
                """
            });
        }
    }

    private sealed class NoopProductionOutboxDispatcher : IProductionOutboxDispatcher
    {
        public Task<int> DispatchPendingAsync(
            int maxItems = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
