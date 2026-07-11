using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workflow;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Interfaces;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Tests.Unit.Architecture;

public class ApiContractPurityTests
{
    [Fact]
    public void AuthAndProxyLoggingContract_DoesNotLogTokensOrTokenBearingUrls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var authControllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "AuthController.cs"));
        var agentControllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "AgentController.cs"));
        var programSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Program.cs"));
        var viteConfigSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "vite.config.ts"));
        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));

        Assert.DoesNotContain("Response JSON", authControllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize(response", authControllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JWT Token Received", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("authToken.Substring", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMessageReceived", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Query[\"token\"]", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery] string? token", agentControllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("req.url", viteConfigSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new EventSource", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("params.set('token'", frontendApiSource, StringComparison.Ordinal);
        Assert.Contains("Authorization", frontendApiSource, StringComparison.Ordinal);
        Assert.Contains("fetch(url", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectResponse_DoesNotExposeFilesystemStorageName()
    {
        Assert.DoesNotContain(
            typeof(ProjectResponse).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "StorageProjectName", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "ProjectController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(NovelProject));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(NovelProject.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(NovelProject.UserId),
                nameof(NovelProject.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('novel-project'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<NovelProjectInfo>('/projects'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StoryConstitutionWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "StoryBibleController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StoryConstitution));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(StoryConstitution.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(StoryConstitution.ProjectId),
                nameof(StoryConstitution.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('story-constitution'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<StoryConstitutionResponse>('/storybible/constitution'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentSessionCreateContract_UsesActionScopedIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "AgentController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(TM.Web.NovelAgentWeb.Data.Entities.AgentSession));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty("IdempotencyKey"));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                "UserId",
                "IdempotencyKey"
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildActionIdempotencyKey('agent-session'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<AgentSessionInfo>(`/agent/session", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("buildStableIdempotencyKey('agent-session'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentChatContract_UsesActionScopedIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "AgentController.cs"));

        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Matches(
            @"HandleAsync\(\s*sessionId,\s*request\.Message \?\? """"\s*,\s*ct,\s*idempotencyKey,\s*request\.ClientMessageId\s*\)",
            controllerSource);

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildActionIdempotencyKey('agent-chat'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<AgentChatResponse>('/agent/chat'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("buildStableIdempotencyKey('agent-chat'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "StoryBibleController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Character));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(Character.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Character.ProjectId),
                nameof(Character.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('character'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<CharacterResponse>('/storybible/characters'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelProjectEntity_DoesNotKeepFilesystemStorageIdentity()
    {
        Assert.DoesNotContain(
            typeof(NovelProject).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "StorageProjectName", StringComparison.Ordinal));
    }

    [Fact]
    public void DbContext_DoesNotMapFilesystemStorageIdentity()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(NovelProject));

        Assert.NotNull(entityType);
        Assert.Null(entityType!.FindProperty("StorageProjectName"));
        Assert.DoesNotContain(
            entityType.GetIndexes(),
            index => index.Properties.Any(p => string.Equals(p.Name, "StorageProjectName", StringComparison.Ordinal)));
    }

    [Fact]
    public void AgentSessionService_DoesNotExposeRawSessionDataWriter()
    {
        Assert.DoesNotContain(
            typeof(IAgentSessionService).GetMethods().Select(m => m.Name),
            name => string.Equals(name, "SaveSessionStateAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionMemory_DoesNotTrackUploadedKnowledgeAsChatMemory()
    {
        Assert.DoesNotContain(
            typeof(SessionMemory).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "RecentUploadedKnowledgeIds", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateKnowledgeRequest_DoesNotAllowClientProvidedSourceFileIdentity()
    {
        var properties = typeof(CreateKnowledgeRequest)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SourceFileId", properties);
        Assert.DoesNotContain("SourceType", properties);
        Assert.DoesNotContain("ChunkIndex", properties);
        Assert.DoesNotContain("ExtractionContext", properties);
    }

    [Fact]
    public void KnowledgeBaseEntity_UsesUploadTaskIdentityNotSourceFileIdentity()
    {
        var properties = typeof(KnowledgeBase)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SourceFileId", properties);
        Assert.Contains("SourceUploadTaskId", properties);

        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(KnowledgeBase));

        Assert.NotNull(entityType);
        Assert.Null(entityType!.FindProperty("SourceFileId"));

        var uploadTaskProperty = entityType.FindProperty("SourceUploadTaskId");
        Assert.NotNull(uploadTaskProperty);
        Assert.Equal("source_upload_task_id", uploadTaskProperty!.GetColumnName());
    }

    [Fact]
    public void KnowledgeResponse_SeparatesUsageContextFromSourceProject()
    {
        var properties = typeof(KnowledgeResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ProjectId", properties);
        Assert.Contains("UsageProjectId", properties);
        Assert.Contains("SourceProjectId", properties);
    }

    [Fact]
    public void ProjectController_ExposesOnlyCanonicalProjectsRoute()
    {
        var routes = typeof(ProjectController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToList();

        Assert.Contains("api/projects", routes);
        Assert.DoesNotContain("api/project", routes);
    }

    [Fact]
    public void PublicRequestDtos_DoNotKeepObsoleteWorkflowOrMaterialRequests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var requestsSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "DTOs", "Requests.cs"));
        var frontendTypes = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "types.ts"));
        var forbiddenDtos = new[]
        {
            "CommitStoryFoundationRequest",
            "ConfirmRequest",
            "ConfirmOnlyRequest",
            "SelectChapterCandidateRequest",
            "EntryConfirmRequest",
            "CreativeKnowledgeQueryRequest",
            "UsedPatternRequest",
            "MaterialIngestRequest",
            "MaterialUpdateRequest"
        };

        var offenders = forbiddenDtos.SelectMany(dto =>
            new[]
            {
                requestsSource.Contains(dto, StringComparison.Ordinal) ? $"Requests.cs:{dto}" : "",
                frontendTypes.Contains(dto, StringComparison.Ordinal) ? $"types.ts:{dto}" : ""
            }.Where(value => !string.IsNullOrEmpty(value))).ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PublicResponseDtos_DoNotKeepObsoleteAgentSessionMirrors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var responsesSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "DTOs", "Responses.cs"));
        var forbiddenDtos = new[]
        {
            "AgentConversationTurnView",
            "AgentSessionSummary",
            "AgentSessionDetail"
        };

        var offenders = forbiddenDtos
            .Where(dto => responsesSource.Contains(dto, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentStepRollbackSurface_IsRemovedWithObsoleteStepDetailPanel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedSources = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "AgentController.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "DTOs", "Requests.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "index.ts")
        };
        var forbiddenFragments = new[]
        {
            "RollbackStep",
            "RollbackStepRequest",
            "agent/step/",
            "rollbackStep"
        };

        var offenders = checkedSources
            .Select(path => new { Path = Path.GetRelativePath(repositoryRoot, path), Source = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Source.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{file.Path}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
        Assert.False(File.Exists(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "components",
            "agent",
            "StepDetail.tsx")));
    }

    [Fact]
    public void CreativeIntentApi_IsExposedAsProductBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "CreativeController.cs");
        var frontendApiPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "index.ts");
        var controllerSource = File.Exists(controllerPath) ? File.ReadAllText(controllerPath) : string.Empty;
        var frontendApiSource = File.ReadAllText(frontendApiPath);

        Assert.True(File.Exists(controllerPath));
        Assert.Contains("[Route(\"api/creative/intents\")]", controllerSource, StringComparison.Ordinal);
        Assert.Contains("ICreativeIntentService", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", controllerSource, StringComparison.Ordinal);
        Assert.Contains("listCreativeIntents", frontendApiSource, StringComparison.Ordinal);
        Assert.Contains("createCreativeIntent", frontendApiSource, StringComparison.Ordinal);
        Assert.Contains("decideCreativeIntent", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UserSettings_DoesNotExposeObsoleteStepDetailsPreference()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedSources = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "UserSettings.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "components", "Settings", "UITab.tsx")
        };
        var forbiddenFragments = new[] { "ShowStepDetails", "showStepDetails" };

        var offenders = checkedSources
            .Select(path => new { Path = Path.GetRelativePath(repositoryRoot, path), Source = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Source.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{file.Path}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void UserSettingsApi_DoesNotPersistLlmApiKeyAsPlaintext()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "SettingsController.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "UserSettings.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Auth", "AuthService.cs")
        };
        var forbiddenFragments = new[]
        {
            "TODO: Encrypt",
            "LlmApiKeyEncrypted = dto.LlmApiKey",
            "LlmApiKeyEncrypted = settings.LlmApiKey",
            "LlmApiKey = entity.LlmApiKeyEncrypted"
        };

        var offenders = checkedFiles
            .Select(path => new { Path = Path.GetRelativePath(repositoryRoot, path), Source = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Source.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{file.Path}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void UserSettingsWriteContract_UsesPutAndReturnsSettingsPayload()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "SettingsController.cs"));
        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));

        Assert.Contains("[HttpPut(\"settings\")]", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost(\"settings\")]", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("return Ok(new { success = true, message = \"设置已保存\" });", controllerSource, StringComparison.Ordinal);
        Assert.Contains("return Ok(ToClientSettings(existing));", controllerSource, StringComparison.Ordinal);

        Assert.Contains("api<UserSettings>('/settings'", frontendApiSource, StringComparison.Ordinal);
        Assert.Contains("method: 'PUT'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<{ success: boolean; message: string }>('/settings'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UserSettingsManager_DoesNotKeepInMemoryFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Support",
            "UserSettings.cs"));

        Assert.DoesNotContain("_fallbackSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return null;", ExtractMethodSource(source, "TryLoadDatabaseSettingsAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("return false;", ExtractMethodSource(source, "TrySaveDatabaseSettingsAsync"), StringComparison.Ordinal);
        Assert.Contains("database-backed user settings", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserSettingsManager_DoesNotExposeLegacyFileSettingsConstructor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Support",
            "UserSettings.cs"));

        Assert.DoesNotContain("string storageRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string projectName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UserSettingsManager_RequiresDatabaseBackedDependencies()
    {
        var constructors = typeof(UserSettingsManager).GetConstructors();

        Assert.Single(constructors);
        var parameters = constructors.Single().GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.DoesNotContain(parameters, parameter => parameter.HasDefaultValue);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepUnusedOwnershipFilterOrLegacyFileParsers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Attributes", "ValidateUserOwnershipAttribute.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "FileParsers.cs")
        };

        var offenders = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void WorkspaceMonitoring_DoesNotExposeNoopInvalidateEndpoint()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "WorkspaceMonitoringController.cs"));

        Assert.DoesNotContain("InvalidateWorkspace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/{projectId}/invalidate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("not yet implemented", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workspace invalidation logged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("evicted naturally based on LRU policy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceCode_DoesNotRuntimeDependOnOriginalTianmingProjectPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "tianming-novel-ai-writer",
            "PycharmProjects/tianming-novel-ai-writer",
            "/Users/lyston/PycharmProjects/tianming-novel-ai-writer"
        };

        var sourceRoots = new[] { "Web", "Services", "Tests" }
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists);

        var checkedFiles = sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => IsSourceFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(ApiContractPurityTests) + ".cs", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentRuntime_DoesNotContainRuleBasedProductionRetryLoop()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs"));
        var forbiddenFragments = new[]
        {
            "ShouldRetryNoToolPlanning",
            "ShouldRetryUserFacingProductionReply",
            "ShouldDeferUserFacingReplyToScheduler",
            "planner_user_facing_retry",
            "planner_no_action_retry",
            "no_tool_fallback",
            "runtime_no_tool",
            "BuildFallbackReplyAction",
            "retry_planning_for_production_turn",
            "retry_planning_after_reading_project_content"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentChatEntryPoint_UsesTurnCoordinatorInsteadOfRouterNaming()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs"));
        var controllerSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "AgentController.cs"));
        var supportPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support");

        Assert.False(File.Exists(Path.Combine(supportPath, "ProjectRouter.cs")));
        Assert.False(File.Exists(Path.Combine(supportPath, "AgentRouter.cs")));
        Assert.True(File.Exists(Path.Combine(supportPath, "AgentTurnCoordinator.cs")));

        Assert.Contains("AgentTurnCoordinator", programSource, StringComparison.Ordinal);
        Assert.Contains("AgentTurnCoordinator", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectRouter", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectRouter", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentRouter", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentRouter", controllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationKernelContract_DoesNotExposeObsoleteRuleRoutedIntentTypes()
    {
        var allowedIntentTypes = new[]
        {
            nameof(TurnIntentType.FreeChat),
            nameof(TurnIntentType.CandidateSelection)
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var allowedDialogueActs = new[]
        {
            nameof(DialogueAct.Chat),
            nameof(DialogueAct.SelectCandidate)
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            allowedIntentTypes,
            Enum.GetNames<TurnIntentType>().OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            allowedDialogueActs,
            Enum.GetNames<DialogueAct>().OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void LibraryPage_DoesNotKeepObsoleteChapterApiTodoOrShowNonLibraryChapters()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "pages",
            "LibraryPage.tsx"));

        Assert.DoesNotContain("Once chapter API is available", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const filteredVolumes = useMemo(() => volumeViews", source, StringComparison.Ordinal);
        Assert.Contains("chapter.visibleInLibrary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreativeIntentWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "CreativeController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CreativeIntent));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(CreativeIntent.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(CreativeIntent.UserId),
                nameof(CreativeIntent.ProjectId),
                nameof(CreativeIntent.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('creative-intent'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<CreativeIntentItem>('/creative/intents'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeEntryWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "KnowledgeController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(KnowledgeBase));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(KnowledgeBase.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(KnowledgeBase.UserId),
                nameof(KnowledgeBase.SourceProjectId),
                nameof(KnowledgeBase.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('knowledge-entry'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<KnowledgeResponse>('/knowledge'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeDirectoryWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "KnowledgeController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(KnowledgeDirectory));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(KnowledgeDirectory.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(KnowledgeDirectory.UserId),
                nameof(KnowledgeDirectory.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('knowledge-directory'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<KnowledgeDirectoryResponse>('/knowledge/directories'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendApiClient_DoesNotAcceptRawNonEnvelopeResponses()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "client.ts"));

        Assert.DoesNotContain("return value as T", source, StringComparison.Ordinal);
        Assert.Contains("API 响应缺少统一信封", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeDirectoryContract_DoesNotKeepLegacySystemKeyCompatibilityScenario()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Tests",
            "Unit",
            "Services",
            "Knowledge",
            "KnowledgeServiceTests.cs"));

        Assert.DoesNotContain("LegacyCustomDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-system-key", source, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy custom", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentCore_DoesNotInferCreativeStateFromUserMessageKeywords()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentCore.cs"));
        var forbiddenFragments = new[]
        {
            "LooksCreative",
            "HasSufficientFoundationBrief",
            "TextContainsAny",
            "userMessage.Contains(\"正文\"",
            "userMessage.Contains(\"修复\"",
            "userMessage.Contains(\"提交\"",
            "userMessage.Contains(\"复盘\"",
            "userMessage.Contains(\"章节\"",
            "userMessage.Contains(\"下一章\""
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentPlanner_DoesNotKeepRuleBasedActionFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentCore.cs"));
        var toolCallingSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolCallingClient.cs"));
        var forbiddenFragments = new[]
        {
            "BuildActionRuleFallback",
            "planner_error_fallback",
            "rate_limit_fallback",
            "Minimal fallback",
            "resume_pending_tool",
            "cancel_pending_confirmation",
            "IsExplicitConfirmation",
            "JsonActionFallbackClient",
            "规则兜底"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal) ||
                               toolCallingSource.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentRuntimePendingConfirmation_DoesNotUseColloquialKeywordContains()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs"));
        var forbiddenFragments = new[]
        {
            "Confirmation detection — enhanced with colloquial Chinese",
            "IsExplicitConfirmation",
            "IsCancel(",
            "normalized.Contains(\"确认\"",
            "normalized.Contains(\"允许\"",
            "normalized.Contains(\"同意\"",
            "normalized.Contains(\"执行吧\"",
            "msg.Contains(\"取消\"",
            "msg.Contains(\"不要\"",
            "msg.Contains(\"先不\"",
            "msg.Contains(\"算了\""
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentToolRegistry_DoesNotInferFoundationReadinessFromKeywords()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs"));
        var forbiddenFragments = new[]
        {
            "HasSufficientFoundationBrief",
            "foundationBriefText",
            "signalCount",
            "ContainsAny(seed,",
            "ExtractGenre("
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void BookConceptDesigner_DoesNotInjectFallbackMacroDirections()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "BookConceptDesigner.cs"));
        var forbiddenFragments = new[]
        {
            "BuildFallbackDirections",
            "foreach (var fallback",
            "打怪升级爽点循环流",
            "BuildBriefText",
            "directions.Add",
            "ContainsAny(brief",
            "foreach (var text in new[]",
            "request.TargetReader })"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ChapterNoveltyPlanner_DoesNotInferCandidateDirectionsFromUserGoal()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "ChapterNoveltyPlanner.cs"));
        var forbiddenFragments = new[]
        {
            "BuildSignalText",
            "HasKnowledgeSignal",
            "directions.Add",
            "目标推进并留下新压力",
            "request.UserGoal.Split",
            "StartsWith(\"不要",
            "UsedPlotPatterns.Where"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void VolumeArcPlanner_DoesNotInferVolumeDirectionsFromRawBrief()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "VolumeArcPlanner.cs"));
        var forbiddenFragments = new[]
        {
            "BuildBriefText",
            "request.UserGoal.Split",
            "怪物压力升级",
            "资源点争夺",
            "境界突破门槛",
            "高阶怪物痕迹",
            "关键限制显形",
            "人物立场裂痕"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GenreDirectionPlanner_DoesNotInferProfileFromRawKeywordBuckets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "GenreDirectionPlanner.cs"));
        var forbiddenFragments = new[]
        {
            "ContainsAny(text",
            "profile.PleasureStrength = Math.Max",
            "profile.MysteryStrength = Math.Max",
            "profile.EnsembleStrength = Math.Max",
            "profile.EmotionStrength = Math.Max"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void BookConceptDesigner_DoesNotRouteCandidateDirectionsByKeywordTemplates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "BookConceptDesigner.cs"));
        var forbiddenFragments = new[]
        {
            "isPowerFantasy",
            "isMecha",
            "isEnsemble",
            "ContainsAny(cleanDirection",
            "打怪获取资源",
            "怪物、资源点",
            "核心爽点循环：遭遇压迫"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void StoryFoundationContract_DoesNotExposeLegacyReaderOrDirectionFields()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modelSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Models",
            "CreativePlanningModels.cs"));
        var registrySource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs"));
        var offenders = new[]
            {
                (File: "CreativePlanningModels.cs", Source: modelSource, Fragment: "TargetReader"),
                (File: "CreativePlanningModels.cs", Source: modelSource, Fragment: "DesiredDirection"),
                (File: "AgentToolRegistry.cs", Source: registrySource, Fragment: "\"targetReader\""),
                (File: "AgentToolRegistry.cs", Source: registrySource, Fragment: "\"desiredDirection\"")
            }
            .Where(item => item.Source.Contains(item.Fragment, StringComparison.Ordinal))
            .Select(item => $"{item.File}:{item.Fragment}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepLegacyNovelAgentSemanticKernelPlugin()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pluginPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Plugins",
            "NovelAgentPlugin.cs");
        var skChatPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "SemanticKernel",
            "SKChatService",
            "SKChatService.KernelManagement.cs");
        var skChatSource = File.Exists(skChatPath) ? File.ReadAllText(skChatPath) : string.Empty;

        Assert.False(File.Exists(pluginPath));
        Assert.DoesNotContain("NovelAgentPlugin", skChatSource, StringComparison.Ordinal);
        Assert.DoesNotContain("kernel.Plugins.AddFromObject(ServiceLocator.Get<TM.Services.Framework.AI.NovelAgent.Plugins", skChatSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacySemanticKernelPluginPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "SemanticKernel",
            "Plugins");
        var skChatPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "SemanticKernel",
            "SKChatService",
            "SKChatService.KernelManagement.cs");
        var skChatSource = File.Exists(skChatPath) ? File.ReadAllText(skChatPath) : string.Empty;
        var forbiddenRegistrations = new[]
        {
            "WriterPlugin",
            "AutoRewriteEngine",
            "DataLookupPlugin",
            "DataEditPlugin",
            "ContentEditPlugin",
            "WorkspacePlugin",
            "LayeredPromptBuilder"
        };

        Assert.False(
            Directory.Exists(pluginDirectory),
            "旧 SemanticKernel 插件管线不应继续留在 Agentic Novel Studio；写作能力必须通过 tool registry + Tianming production kernel 暴露。");

        var offenders = forbiddenRegistrations
            .Where(fragment => skChatSource.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacySemanticKernelChatStack()
    {
        var repositoryRoot = FindRepositoryRoot();
        var semanticKernelDirectory = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "SemanticKernel");
        var webProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "NovelAgentWeb.csproj"));

        Assert.False(
            Directory.Exists(semanticKernelDirectory),
            "当前 Web Agent 通过 ProviderToolCallingClient + Tianming production kernel 调用模型；旧 SemanticKernel/SKChatService 聊天栈不能继续作为仓库内备用实现。");
        Assert.DoesNotContain("Services\\Framework\\AI\\SemanticKernel", webProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Services/Framework/AI/SemanticKernel", webProjectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacyDesktopAiAndSystemIntegrationStacks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyPaths = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Core"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Middleware"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Interfaces"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Mlm"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "SemanticGuard"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "RateLimiting"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "WritingConfig"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Monitoring"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Capabilities"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "Library"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "BuiltInConfigSyncService.cs"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "EntityPropagationService.cs"),
            Path.Combine(repositoryRoot, "Services", "Framework", "SystemIntegration"),
            Path.Combine(repositoryRoot, "Services", "Framework", "Notification", "NotificationSoundService.cs")
        };

        var offenders = legacyPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacyAiBackedValidationImplementations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyPaths = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Validation", "UnifiedValidationService.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Validation", "UnifiedValidationService"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Guides", "MilestoneCondenser.cs")
        };

        var offenders = legacyPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepLegacyRunAutopilotEntrypoints()
    {
        var repositoryRoot = FindRepositoryRoot();
        var orchestratorPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "NovelAgentOrchestrator.cs");
        var modelPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Models",
            "NovelAgentRun.cs");
        var requestsPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "DTOs", "Requests.cs");
        var frontendTypesPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts");

        var orchestratorSource = File.ReadAllText(orchestratorPath);
        var modelSource = File.ReadAllText(modelPath);
        var requestsSource = File.ReadAllText(requestsPath);
        var frontendTypesSource = File.ReadAllText(frontendTypesPath);

        Assert.DoesNotContain("ContinueAgentRunAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteChapterFromBriefAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<NovelAgentExecutionResult> BuildChapterContextPackageAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<NovelAgentExecutionResult> GenerateChapterWithChangesAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<NovelAgentExecutionResult> ValidateChapterDraftAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<NovelAgentExecutionResult> RepairChapterDraftAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<NovelAgentExecutionResult> CommitValidatedChapterAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentAutoContinueResult", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ContinueRunRequest", requestsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ContinueRunRequest", frontendTypesSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ChapterNoveltyPlanner_DoesNotRouteCandidateDirectionsByKeywordTemplates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "ChapterNoveltyPlanner.cs"));
        var forbiddenFragments = new[]
        {
            "BuildPowerProgressionCandidate",
            "BuildResourceCandidate",
            "ContainsAny(direction,",
            "通过连续战斗推进",
            "争夺一处能让主角升级的资源点"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void VolumeArcPlanner_DoesNotRouteBeatsByKeywordTemplates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "VolumeArcPlanner.cs"));
        var forbiddenFragments = new[]
        {
            "isPowerProgression",
            "ContainsAny(directionText",
            "BuildBeatGoal(string role, string promise, bool",
            "BuildBeatTurn(string role, string conflict, bool",
            "BuildBeatCost(string role, bool",
            "怪潮资源与境界突破"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RuntimeMemory_DoesNotExtractAuthorOrSessionPreferencesFromMessageKeywords()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs"));
        var memorySource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentMemoryService.cs"));
        var compressorSource = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "ChatHistoryCompressor.cs"));

        var offenders = new[]
            {
                (File: "AgentRuntime.cs", Source: runtimeSource, Fragment: "UserPreferences.Add(preference)"),
                (File: "AgentMemoryService.cs", Source: memorySource, Fragment: "preference.Contains(\"不要\")"),
                (File: "AgentMemoryService.cs", Source: memorySource, Fragment: "preference.Contains(\"避免\")"),
                (File: "ChatHistoryCompressor.cs", Source: compressorSource, Fragment: "var keywords = new[]")
            }
            .Where(item => item.Source.Contains(item.Fragment, StringComparison.Ordinal))
            .Select(item => $"{item.File}:{item.Fragment}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepStubMaterialAnalysisPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "StubMaterialAnalysisService",
            "IMaterialAnalysisService",
            "MaterialAnalysisResult",
            "MaterialAnalysisProgress"
        };

        var checkedFiles = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "Web"), "*", SearchOption.AllDirectories)
            .Where(path => IsSourceFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepDeterministicEmbeddingStubs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "StubEmbeddingService",
            "WebEmbeddingService",
            "Embedding:Provider=stub",
            "UsesDeterministicStub = true",
            "deterministic hash vectors"
        };

        var checkedFiles = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "Web"), "*", SearchOption.AllDirectories)
            .Where(path => IsSourceFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EmbeddingHealthContract_DoesNotExposeStubCompatibilityFields()
    {
        Assert.DoesNotContain(
            typeof(EmbeddingRuntimeStatus).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "UsesDeterministicStub", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(EmbeddingHealthResponse).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "DeterministicStub", StringComparison.Ordinal));
    }

    [Fact]
    public void Repository_DoesNotKeepLegacyFileBackedGenerationPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyPaths = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Generation", "GeneratedContentService.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Generation", "ContentPolisher.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Generation", "ContentGenerationCallback.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Generation", "ContentGenerationCallback")
        };

        var offenders = legacyPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacyQueryRoutingPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "QueryRouting");

        Assert.False(
            Directory.Exists(legacyPath),
            "旧 QueryRouting 是关键词/服务定位式检索管线，不能和 Agent 自主 tool_search + 内容查询模型并存。");
    }

    [Fact]
    public void RuntimeLogConfiguration_DoesNotKeepRemovedGenerationPluginFilters()
    {
        var repositoryRoot = FindRepositoryRoot();
        var logManagerPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "Settings",
            "LogManager.cs");
        var source = File.Exists(logManagerPath) ? File.ReadAllText(logManagerPath) : string.Empty;
        var forbiddenFragments = new[]
        {
            "WriterPlugin",
            "AutoRewriteEngine",
            "DataEditPlugin",
            "DataLookupPlugin",
            "WorkspacePlugin",
            "ContentEditPlugin",
            "LayeredPromptBuilder",
            "ContentPolisher",
            "ContentCallback|",
            "GeneratedContentService",
            "QueryRoutingService",
            "AIService|",
            "SKChatService|",
            "SKConversationViewModel|",
            "GlobalCleanupService|",
            "BusinessCleanupService|",
            "SystemIntegration|",
            "ModelManagement|",
            "EndpointTestService|",
            "ThinkingRouter|",
            "PlanModeMapper|",
            "PlanPayloadPublisher|",
            "PlanViewModel|",
            "TodoExecutionService|",
            "BuiltInConfigSyncService",
            "已刷新服务: AIService"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionSource_DoesNotReferenceRemovedGenerationImplementations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "Services"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb")
        };
        var forbiddenFragments = new[]
        {
            "ContentGenerationCallback",
            "TM.Services.Modules.ProjectData.Implementations.Generation.GeneratedContentService",
            "ServiceLocator.Get<GeneratedContentService>",
            "ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>"
        };

        var offenders = sourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !path.EndsWith(Path.Combine("Architecture", "ApiContractPurityTests.cs"), StringComparison.Ordinal))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Source.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ChapterService_DoesNotWriteChapterVectorsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Chapters",
            "ChapterService.cs"));
        var interfaceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Chapters",
            "IChapterService.cs"));

        var forbiddenFragments = new[]
        {
            "IVectorStore",
            "IMicroEmbeddingService",
            "GenerateAndStoreEmbeddingsAsync",
            "DeleteChapterVectorsAsync",
            "CollectionExistsAsync",
            "InitializeUserCollectionAsync",
            "UpsertVectorsAsync",
            "DeleteVectorsByFilterAsync",
            "Qdrant vectors",
            "Qdrant vectors.",
            "Qdrant if content changed",
            "Generate and store embeddings"
        };

        var offenders = forbiddenFragments
            .Where(fragment =>
                source.Contains(fragment, StringComparison.Ordinal) ||
                interfaceSource.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ChapterWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "ChaptersController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Chapter));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(Chapter.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Chapter.ProjectId),
                nameof(Chapter.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('chapter'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VolumeArcWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "WorkflowController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(VolumeArc));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(VolumeArc.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(VolumeArc.ProjectId),
                nameof(VolumeArc.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('volume-arc'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<VolumeArcResponse>('/workflow/volumes'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialWriteContract_UsesIdempotencyKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Controllers",
            "MaterialsController.cs"));
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Material));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.FindProperty(nameof(Material.IdempotencyKey)));
        Assert.Contains("Idempotency-Key", controllerSource, StringComparison.Ordinal);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Material.ProjectId),
                nameof(Material.IdempotencyKey)
            }));

        var frontendApiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb.Frontend",
            "src",
            "api",
            "index.ts"));
        Assert.Contains("buildStableIdempotencyKey('material'", frontendApiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("post<MaterialResponse>('/materials'", frontendApiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectService_DoesNotManageQdrantCollectionsOrVectorsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Projects",
            "ProjectService.cs"));

        var forbiddenFragments = new[]
        {
            "IVectorStore",
            "InitializeUserCollectionAsync",
            "DeleteVectorsByFilterAsync",
            "Qdrant collection",
            "Qdrant vectors"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MaterialService_DoesNotVectorizeOrDeleteVectorsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Materials",
            "MaterialService.cs"));

        var forbiddenFragments = new[]
        {
            "IMaterialVectorizationService",
            "IVectorStore",
            "TryVectorizeMaterialAsync",
            "TryDeleteMaterialVectorsAsync",
            "VectorizeMaterialAsync",
            "DeleteVectorsByFilterAsync"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MaterialVectorIndexing_IsOutboxExecutorNotBusinessService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var vectorizationDir = Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Vectorization");
        var checkedFiles = Directory.EnumerateFiles(vectorizationDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToList();
        var offenders = checkedFiles
            .Select(path => new { Path = Path.GetRelativePath(repositoryRoot, path), Source = File.ReadAllText(path) })
            .SelectMany(file => new[]
                {
                    "IMaterialVectorizationService",
                    "MaterialVectorizationService",
                    "VectorizeAllMaterialsAsync"
                }
                .Where(fragment => file.Source.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{file.Path}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void KnowledgeService_DoesNotWriteKnowledgeVectorsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Knowledge",
            "KnowledgeService.cs"));

        var forbiddenFragments = new[]
        {
            "private readonly IVectorStore",
            "private readonly IMicroEmbeddingService",
            "TryUpsertKnowledgeVectorAsync",
            "TryDeleteKnowledgeVectorsAsync",
            "UpsertVectorsAsync",
            "DeleteVectorsByFilterAsync",
            "InitializeUserCollectionAsync"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentMemoryRepository_DoesNotWriteMemoryVectorsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Memory",
            "AgentMemoryRepository.cs"));

        var forbiddenFragments = new[]
        {
            "private readonly IVectorStore",
            "private readonly IMicroEmbeddingService",
            "IVectorStore vectorStore",
            "IMicroEmbeddingService embedding",
            "VectorizeProjectMemoryFieldsAsync",
            "UpsertVectorsAsync",
            "new VectorData",
            "EmbeddingMode.Passage"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
        Assert.Contains("private readonly IProductionTruthStore _truthStore", source, StringComparison.Ordinal);
        Assert.Contains("EnqueueOutboxAsync(", source, StringComparison.Ordinal);
        Assert.Contains("index_memory_content", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QdrantVectorStore_RoundTripsVectorMetadataPayload()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "VectorStore",
            "QdrantVectorStore.cs"));

        Assert.Contains("AddMetadataPayload(point.Payload, v.Metadata)", source, StringComparison.Ordinal);
        Assert.Contains("Metadata = GetPayloadMetadata(r.Payload)", source, StringComparison.Ordinal);
        Assert.Contains("CorePayloadKeys.Contains(key)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepUnusedQdrantSearchServiceBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenPath = Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "VectorStore",
            "QdrantSearchService.cs");
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs"));

        Assert.False(File.Exists(forbiddenPath));
        Assert.DoesNotContain("QdrantSearchService", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepHybridVectorSearchCompatibilityBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Modules",
            "ProjectData",
            "Implementations",
            "Indexing",
            "HybridVectorSearchService.cs");

        Assert.False(File.Exists(forbiddenPath));
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedContentChunkSearchService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "Services",
            "Modules",
            "ProjectData",
            "Implementations",
            "Indexing",
            "ContentChunkSearchService.cs");

        Assert.False(File.Exists(sourcePath));
    }

    [Fact]
    public void HardcoreWritingEngine_DoesNotFallbackWhenGenerationGateIsUnavailable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var enginePath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "HardcoreWritingEngine.cs");
        var source = File.ReadAllText(enginePath);
        var forbiddenFragments = new[]
        {
            "BuildFallbackGateReport",
            "Web runtime fallback",
            "fallback 校验",
            "真实 GenerationGate 当前不可用"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HardcoreWritingEngine_DoesNotKeepInlinePostCommitRefreshDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var enginePath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "HardcoreWritingEngine.cs");
        var source = File.ReadAllText(enginePath);

        Assert.DoesNotContain("ServiceLocator", source, StringComparison.Ordinal);
        Assert.Contains("IChapterSummaryService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChapterSummaryStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KeywordChapterIndexService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChapterChangesRecorder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChapterChangesWalStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChapterFactWriter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChapterEmbeddingIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChunkEmbeddingIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IMicroEmbeddingService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IncrementModuleVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardcoreWritingEngine_DoesNotUseFireAndForgetPostCommitRefresh()
    {
        var repositoryRoot = FindRepositoryRoot();
        var enginePath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "HardcoreWritingEngine.cs");
        var source = File.ReadAllText(enginePath);

        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPostCommitRefresh", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepEntityDriftFallbackPatcher()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "EntityDriftFallbackPatcher",
            "EntityDriftFallbackResult",
            "driftFallbackPatcher",
            "漂移兜底",
            "兜底补录"
        };

        var checkedFiles = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(ApiContractPurityTests) + ".cs", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GenerationGate_DoesNotAutoPatchOmittedChanges()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "AutoPatchAction",
            "DriftStrategy.AutoPatch",
            "漏报自动补录",
            "ChangesXmlOpenRegex",
            "ChangesSeparatorLineRegex",
            "MdChangesHeaderRegex",
            "TrailingJsonOnly",
            "XmlOpenOnly",
            "TryIdentifyTrailingJson",
            "TryValidateTrailingJsonCandidate",
            "兼容旧格式",
            "末尾 JSON"
        };

        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Generation", "GenerationGate", "GenerationGate.PublicMethods.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "EntityDrift", "EntityDimensionDescriptor.cs"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "EntityDrift", "EntityDimensionRegistry.cs")
        };

        var offenders = checkedFiles
            .Where(File.Exists)
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionRuntime_DoesNotExposeLegacyChangesProtocol()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "---CHANGES---",
            "## CHANGES",
            "ChangesSeparator",
            "<changes>",
            "</changes>"
        };

        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent"),
            Path.Combine(repositoryRoot, "Services", "Modules", "ProjectData"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb")
        }.Where(Directory.Exists);

        var checkedFiles = sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(ApiContractPurityTests) + ".cs", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_UsesProductionStageIdsInsteadOfObsoleteChapterToolNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "BuildChapterContextPackage",
            "GenerateChapterWithChanges",
            "ValidateChapterDraft",
            "RepairChapterDraft",
            "CommitValidatedChapter"
        };

        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb")
        }.Where(Directory.Exists);

        var checkedFiles = sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Text.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(repositoryRoot, file.Path)}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotUseAutopilotRuntimeVocabulary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support")
        }.Where(Directory.Exists);

        var checkedFiles = sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => file.Text.Contains("Autopilot", StringComparison.Ordinal) ||
                           file.Text.Contains("autopilot", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SettingsContract_DoesNotExposeLegacyAutoContinueFields()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Data", "Entities", "UserSettings.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Data", "NovelAgentDbContext.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "SettingsController.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Auth", "AuthService.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "UserSettings.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "components", "Settings", "CreativeTab.tsx")
        };
        var forbiddenFragments = new[]
        {
            "AgentAutoContinue",
            "AgentMaxAutoSteps",
            "agentAutoContinue",
            "agentMaxAutoSteps",
            "agent_auto_continue",
            "agent_max_auto_steps"
        };

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Text.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(repositoryRoot, file.Path)}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void WebProject_IncludesBgeEmbeddingRuntimeAndModelResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("BgeSmallZhEmbeddingService.cs", project, StringComparison.Ordinal);
        Assert.Contains("Embedding\\Resources\\bge-small-zh-v1.5\\**\\*", project, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory", project, StringComparison.Ordinal);
    }

    [Fact]
    public void WebAppSettings_DefaultEmbeddingConfigurationUsesRealBge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSettingsPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "appsettings.json");
        var json = File.ReadAllText(appSettingsPath);

        Assert.DoesNotContain("\"Provider\": \"stub\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stub-hash-v1", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Provider\": \"bge-small-zh\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"RequireRealEmbeddings\": true", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectReferences_StayInsideCurrentRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => IsProjectMetadataFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToList();

        var offenders = new List<string>();
        foreach (var projectFile in projectFiles)
        {
            var document = XDocument.Load(projectFile);
            var projectReferences = document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();

            foreach (var include in projectReferences)
            {
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include));
                if (!IsInsideDirectory(fullPath, repositoryRoot))
                {
                    offenders.Add($"{Path.GetRelativePath(repositoryRoot, projectFile)} -> {include}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void MsBuildLocalIncludes_StayInsideCurrentRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => IsProjectMetadataFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToList();

        var offenders = projectFiles
            .SelectMany(projectFile => FindExternalMsBuildLocalIncludes(projectFile, repositoryRoot))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MsBuildLocalIncludeScanner_FlagsRepositoryExternalCompileIncludes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "tm-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectFile = Path.Combine(tempRoot, "BoundaryFixture.csproj");
        File.WriteAllText(projectFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="../../tianming-novel-ai-writer/RuntimeBorrow.cs" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var offenders = FindExternalMsBuildLocalIncludes(projectFile, repositoryRoot).ToList();

            Assert.Contains(offenders, offender =>
                offender.Contains("BoundaryFixture.csproj", StringComparison.Ordinal)
                && offender.Contains("Compile", StringComparison.Ordinal)
                && offender.Contains("RuntimeBorrow.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RuntimeSource_DoesNotBorrowProductionKernelThroughDynamicLoadingOrSubprocess()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbiddenFragments = new[]
        {
            "ProcessStartInfo",
            "System.Diagnostics.Process.Start",
            "Assembly.Load",
            "Assembly.LoadFrom",
            "Assembly.LoadFile",
            "AssemblyLoadContext",
            "dotnet run --project",
            "\"python\"",
            "\"python3\"",
            "\"node\"",
            "\"bash\"",
            "\"zsh\"",
            "'python'",
            "'python3'",
            "'node'",
            "'bash'",
            "'zsh'"
        };

        var sourceRoots = new[] { "Web", "Services" }
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists);

        var checkedFiles = sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => IsSourceFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToList();

        var offenders = checkedFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Program_RunsRepositoryBoundaryGuardAtStartup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs"));

        Assert.Contains("IRepositoryBoundaryGuard", program, StringComparison.Ordinal);
        Assert.Contains("ResolveRepositoryRoot", program, StringComparison.Ordinal);
        Assert.Contains("EnsureClean", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_RunsProductionKernelAssemblyGuardAtStartup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs"));

        Assert.Contains("IProductionKernelAssemblyGuard", program, StringComparison.Ordinal);
        Assert.Contains("EnsureCurrentRepositoryKernel", program, StringComparison.Ordinal);
        Assert.Contains("HardcoreWritingProductionKernel", program, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelAgentOrchestrator_UsesProductionKernelContractForProductionPipeline()
    {
        var fields = typeof(NovelAgentOrchestrator)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, field => field.FieldType, StringComparer.Ordinal);

        Assert.Contains("_productionKernel", fields.Keys);
        Assert.Equal(typeof(ITianmingProductionKernel), fields["_productionKernel"]);
        Assert.DoesNotContain(fields, field =>
            field.Value == typeof(HardcoreWritingEngine)
            || field.Value.Name.Contains("HardcoreWritingEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void NovelAgentOrchestrator_DoesNotCreateProductionKernelFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var orchestratorPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "NovelAgentOrchestrator.cs");
        var source = File.ReadAllText(orchestratorPath);

        Assert.DoesNotContain("new HardcoreWritingProductionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HardcoreWritingEngine? hardcoreWritingEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("productionKernel ??", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelAgentRegressionWorkspaceFactory_DoesNotInjectDefaultProductionKernelStub()
    {
        var repositoryRoot = FindRepositoryRoot();
        var factoryPath = Path.Combine(
            repositoryRoot,
            "Tests",
            "NovelAgentRegression",
            "TestNovelAgentWorkspaceFactory.cs");
        var source = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("RegressionProductionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("kernelOverride:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelAgentRegressionProgram_DoesNotManuallyAssembleProductionKernel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(
            repositoryRoot,
            "Tests",
            "NovelAgentRegression",
            "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.DoesNotContain("new HardcoreWritingEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new HardcoreWritingProductionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new GenerationGate", source, StringComparison.Ordinal);
        Assert.Contains("IWorkspaceProductionRuntimeBuilder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionKernelRuntimeCode_LivesInsideCurrentRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedRoot = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "ProductionKernel");

        var kernelTypes = new[]
        {
            typeof(ITianmingProductionKernel),
            typeof(HardcoreWritingProductionKernel),
            typeof(IChapterDirectiveBuilder),
            typeof(ChapterDirectiveBuilder),
            typeof(IChapterPromptBuilder),
            typeof(ChapterPromptBuilder),
            typeof(IChapterGatekeeper),
            typeof(ChapterGatekeeper),
            typeof(IChapterFactWriter),
            typeof(ChapterFactWriter),
            typeof(IChapterRewriter),
            typeof(ChapterRewriter),
            typeof(IChapterPackageBuilder),
            typeof(ChapterPackageBuilder),
            typeof(ChapterChangesText),
            typeof(AcceptedCreativeIntentPolicy)
        };

        foreach (var type in kernelTypes)
        {
            var assemblyLocation = type.Assembly.Location;
            Assert.True(IsInsideDirectory(assemblyLocation, repositoryRoot), $"{type.FullName} assembly is outside repository: {assemblyLocation}");
        }

        Assert.True(Directory.Exists(expectedRoot), $"Production kernel source directory is missing: {expectedRoot}");
        Assert.Contains(Path.Combine(expectedRoot, "ITianmingProductionKernel.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "HardcoreWritingProductionKernel.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterDirectiveBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterDirectiveBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterPromptBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterPromptBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterGatekeeper.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterGatekeeper.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterFactWriter.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterFactWriter.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterRewriter.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterRewriter.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "IChapterPackageBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterPackageBuilder.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "ChapterChangesText.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
        Assert.Contains(Path.Combine(expectedRoot, "AcceptedCreativeIntentPolicy.cs"), Directory.EnumerateFiles(expectedRoot, "*.cs"));
    }

    [Fact]
    public void HardcoreWritingEngine_DoesNotExposeStoryStateOnlyCompatibilityConstructor()
    {
        var constructors = typeof(HardcoreWritingEngine).GetConstructors();

        Assert.DoesNotContain(constructors, ctor =>
        {
            var parameters = ctor.GetParameters();
            return parameters.Length >= 1
                   && parameters[0].ParameterType == typeof(StoryStateSnapshotService)
                   && !parameters.Any(p => p.ParameterType == typeof(IGuideContextService));
        });
    }

    [Fact]
    public void ProductionKernel_DoesNotConstructWritingEngineOutsideDi()
    {
        var repositoryRoot = FindRepositoryRoot();
        var kernelPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "ProductionKernel",
            "HardcoreWritingProductionKernel.cs");
        var source = File.ReadAllText(kernelPath);

        Assert.DoesNotContain("new HardcoreWritingEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoryStateSnapshotService storyStateSnapshotService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelAgentWorkspace_DoesNotExposeProductionKernelOverride()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var source = File.ReadAllText(webRuntimePath);

        Assert.DoesNotContain("kernelOverride", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ITianmingProductionKernel?", source, StringComparison.Ordinal);
        Assert.DoesNotContain("projectProductionKernel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_RegistersProductionKernelServicesWithoutRootScopedRuntimeKernel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.Contains("AddTianmingProductionKernelServices", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UnavailableContentChunkSearchService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<StoryStateSnapshotService>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<HardcoreWritingEngine>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<ITianmingProductionKernel>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("tianming-novel-ai-writer", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebRuntime_DoesNotKeepDisabledHostedSchedulerCompatibilityPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        var schedulerPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentSchedulerHostedService.cs");

        Assert.False(File.Exists(schedulerPath));
        Assert.DoesNotContain("AgentSchedulerHostedService", program, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRuntime_DoesNotKeepNoopContentChunkSearchFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebContentChunkSearchService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("UnavailableContentChunkSearchService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult(new List<ContentChunkHit>())", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult<IReadOnlyList<ContentChunkHit>>(Array.Empty<ContentChunkHit>())", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeRequiredContentChunkSearchService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRuntime_DoesNotKeepNoopGeneratedContentFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var deletedFallbackPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebValidationServices.cs");
        var validationPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Production", "ProductionUnifiedValidationService.cs");
        var source = File.ReadAllText(validationPath);
        var runtimePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.False(File.Exists(deletedFallbackPath));
        Assert.DoesNotContain("UnavailableGeneratedContentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UnavailableGeneratedContentService", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeRequiredGeneratedContentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult<string?>(null)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult(false)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult(\"chapter-001\")", source, StringComparison.Ordinal);
        Assert.Contains("ProductionUnifiedValidationService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRuntime_DoesNotUseFakeUnifiedValidationPassThrough()
    {
        var repositoryRoot = FindRepositoryRoot();
        var validationPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Production", "ProductionUnifiedValidationService.cs");
        var runtimePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var validationSource = File.ReadAllText(validationPath);
        var runtimeSource = File.ReadAllText(runtimePath);

        Assert.DoesNotContain("WebUnifiedValidationService", validationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WebUnifiedValidationService", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeRequiredUnifiedValidationService", validationSource, StringComparison.Ordinal);
        Assert.Contains("GetChapterAsync", validationSource, StringComparison.Ordinal);
        Assert.Contains("IssuesByModule", validationSource, StringComparison.Ordinal);
        Assert.Contains("ProductionUnifiedValidationService", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WebProject_UsesCanonicalPostGenerationReviewer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var projectSource = File.ReadAllText(projectPath);
        var webReviewerPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebNovelAgentReviewServices.cs");

        Assert.Contains("Services\\Framework\\AI\\NovelAgent\\Services\\ChapterPostGenerationReviewer.cs", projectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Remove=\"..\\..\\Services\\Framework\\AI\\NovelAgent\\Services\\ChapterPostGenerationReviewer.cs\"", projectSource, StringComparison.Ordinal);
        Assert.False(File.Exists(webReviewerPath));
    }

    [Fact]
    public void WebProject_DoesNotUseCompileRemoveToHideProductionSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var projectSource = File.ReadAllText(projectPath);

        Assert.DoesNotContain("<Compile Remove=", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntime_DoesNotAllowRedisInMemoryFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Caching", "RedisCacheServiceCollectionExtensions.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Caching", "IDistributedCacheService.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "appsettings.json"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "appsettings.Development.json")
        };

        var offenders = checkedFiles
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains("AllowInMemoryFallback", StringComparison.Ordinal)
                           || File.ReadAllText(path).Contains("local fallback", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Repository_DoesNotKeepLegacyMigrationCompatibilityScripts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationPath = Path.Combine(repositoryRoot, "Scripts", "Migration");

        Assert.False(
            Directory.Exists(migrationPath),
            "Legacy JSON/vector migration scripts must be removed after the production-kernel refactor; runtime state now lives in canonical DB, Redis, Qdrant, and production_event flows.");
    }

    [Fact]
    public void ActiveDocumentation_DoesNotPointToRemovedLegacyMigrationTools()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeDocs = new[]
        {
            Path.Combine(repositoryRoot, "README.md"),
            Path.Combine(repositoryRoot, "Docs", "DEPLOYMENT.md"),
            Path.Combine(repositoryRoot, "Docs", "ARCHITECTURE.md")
        };

        var offenders = activeDocs
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains("Scripts/Migration", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ActiveDocumentation_DoesNotAdvertiseDeterministicEmbeddingStubs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeDocs = new[]
        {
            Path.Combine(repositoryRoot, "README.md"),
            Path.Combine(repositoryRoot, "Docs", "DEPLOYMENT.md")
        };
        var forbiddenFragments = new[]
        {
            "Embedding:Provider=stub",
            "\"Provider\": \"stub\"",
            "stub-hash-v1",
            "deterministicStub",
            "默认 embedding 为 degraded stub",
            "supports `stub` only"
        };

        var offenders = activeDocs
            .Where(File.Exists)
            .SelectMany(path => forbiddenFragments
                .Where(fragment => File.ReadAllText(path).Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(fragment => $"{Path.GetRelativePath(repositoryRoot, path)} contains {fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ActiveAgentGuidance_DoesNotTellModelsToSkipRealStatusTools()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeDocs = new[]
        {
            Path.Combine(repositoryRoot, "CLAUDE.md"),
            Path.Combine(repositoryRoot, "Docs", "工程质量", "NovelAgent架构与写作流程说明.md")
        };
        var forbiddenFragments = new[]
        {
            "状态查询等用 `chat_reply`",
            "不要调用 `QueryProjectStatus`",
            "问候、状态查询等用 `chat_reply`"
        };

        var offenders = activeDocs
            .Where(File.Exists)
            .SelectMany(path => forbiddenFragments
                .Where(fragment => File.ReadAllText(path).Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(fragment => $"{Path.GetRelativePath(repositoryRoot, path)} contains {fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiControllers_DoNotHandWrapAgentOrRuntimeEnvelopes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "AgentController.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers", "RuntimeController.cs")
        };
        var forbiddenFragments = new[]
        {
            "OkEnvelope(",
            "NotFoundEnvelope(",
            "BadRequestEnvelope(",
            "ApiEnvelope<"
        };

        var offenders = checkedFiles
            .SelectMany(path => forbiddenFragments
                .Where(fragment => File.ReadAllText(path).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(repositoryRoot, path)} contains {fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Program_RegistersUnifiedApiEnvelopeResultFilter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("options.Filters.Add<ApiEnvelopeResultFilter>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_MinimalHealthEndpoint_UsesUnifiedApiEnvelope()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/health\"", source, StringComparison.Ordinal);
        Assert.Contains("ApiEnvelope<object>.Ok", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Ok(await health.CheckAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiErrorContract_PreservesRequiresUserDecisionAcrossBackendAndFrontend()
    {
        var repositoryRoot = FindRepositoryRoot();
        var envelopePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "DTOs", "ApiEnvelope.cs");
        var filterPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Filters", "ApiEnvelopeResultFilter.cs");
        var frontendClientPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "client.ts");

        var envelopeSource = File.ReadAllText(envelopePath);
        var filterSource = File.ReadAllText(filterPath);
        var frontendSource = File.ReadAllText(frontendClientPath);

        Assert.Contains("public bool RequiresUserDecision", envelopeSource, StringComparison.Ordinal);
        Assert.Contains("requiresUserDecision", filterSource, StringComparison.Ordinal);
        Assert.Contains("requiresUserDecision?: boolean", frontendSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiControllers_DoNotReturnAnonymousErrorObjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllersRoot = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Controllers");
        var anonymousErrorPattern = new Regex(
            @"return\s+(?:NotFound|BadRequest|StatusCode|Conflict|Unauthorized)\s*\([^;]*new\s*\{\s*(?:error|message|success)\s*=",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var offenders = Directory.EnumerateFiles(controllersRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path)
            })
            .Where(item => anonymousErrorPattern.IsMatch(item.Source))
            .Select(item => Path.GetRelativePath(repositoryRoot, item.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotExposeTitleBasedStoryFoundationSelection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedRoots = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src")
        };

        var offenders = checkedRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => IsSourceFile(path) || IsFrontendSourceFile(path))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("selectedMacroCandidateTitle", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionCode_DoesNotKeepUncompiledRewriteLoopWithMissingDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var projectSource = File.ReadAllText(projectPath);
        var deadRewritePath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "NovelAgentRewriteLoopService.cs");

        Assert.False(File.Exists(deadRewritePath));
        Assert.DoesNotContain("<Compile Remove=\"..\\..\\Services\\Framework\\AI\\NovelAgent\\Services\\NovelAgentRewriteLoopService.cs\"", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCode_DoesNotExposeLegacyRewriteLoopTool()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebNovelAgentRewriteLoopService.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent", "Services", "NovelAgentOrchestrator.cs"),
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent", "Plugins", "NovelAgentPlugin.cs")
        };

        var forbiddenFragments = new[]
        {
            "NovelAgentRewriteLoopService",
            "RewriteChapterFromReview",
            "NovelAgent.RewriteChapterFromReview"
        };

        var offenders = checkedFiles
            .Where(File.Exists)
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => forbiddenFragments.Any(fragment => file.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RunContracts_DoNotExposeLegacyRewriteAttemptList()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyModelPath = Path.Combine(
            repositoryRoot,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Models",
            "RewriteLoopModels.cs");
        var checkedFiles = new[]
        {
            Path.Combine(repositoryRoot, "Services", "Framework", "AI", "NovelAgent", "Models", "NovelAgentRun.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "NovelLibrary.cs"),
            Path.Combine(repositoryRoot, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts")
        };
        var forbiddenFragments = new[]
        {
            "NovelAgentRewriteAttempt",
            "RewriteAttempts",
            "rewriteAttempts"
        };

        Assert.False(File.Exists(legacyModelPath));

        var offenders = checkedFiles
            .Where(File.Exists)
            .Select(path => new { Path = Path.GetRelativePath(repositoryRoot, path), Source = File.ReadAllText(path) })
            .SelectMany(file => forbiddenFragments
                .Where(fragment => file.Source.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{file.Path}:{fragment}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ToolSearchCacheContract_RequiresCurrentToolCatalogSignature()
    {
        var methods = typeof(IToolSearchCacheService)
            .GetMethods()
            .Where(method => method.Name is "GetAsync" or "SaveAsync")
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.Contains(method.GetParameters(), parameter =>
                parameter.ParameterType == typeof(string) &&
                string.Equals(parameter.Name, "toolCatalogSignature", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(methods, method =>
            method.Name == "GetAsync" &&
            !method.GetParameters().Any(parameter => string.Equals(parameter.Name, "toolCatalogSignature", StringComparison.Ordinal)));
        Assert.DoesNotContain(methods, method =>
            method.Name == "SaveAsync" &&
            !method.GetParameters().Any(parameter => string.Equals(parameter.Name, "toolCatalogSignature", StringComparison.Ordinal)));
    }

    [Fact]
    public void ToolSearchCacheService_DoesNotAllowCatalogAgnosticVersionKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "AgentTools",
            "ToolSearchCacheService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("tool_catalog=", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeToolCatalogSignature", source, StringComparison.Ordinal);
        Assert.Contains("Tool search cache requires a current tool catalog signature.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildToolSearchVersionAsync(session, ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolSearchCacheVersion = memoryVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowService_UsesProductionWorkflowBridgeForProductionEvents()
    {
        var fields = typeof(WorkflowService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, field => field.FieldType, StringComparer.Ordinal);

        Assert.Contains("_productionWorkflowBridge", fields.Keys);
        Assert.Equal(typeof(IProductionWorkflowBridge), fields["_productionWorkflowBridge"]);

        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Workflow", "WorkflowService.cs");
        var workflowSource = File.ReadAllText(workflowPath);
        Assert.DoesNotContain("_db.ProductionEvents", workflowSource, StringComparison.Ordinal);

        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IProductionWorkflowBridge, ProductionWorkflowBridge>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_WritesChapterProductionEventsThroughProductionEventWriter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IProductionEventWriter, ProductionEventWriter>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "private async Task AppendChapterProductionEventAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "AppendChapterProductionEventAsync method should exist.");

        var methodEnd = registrySource.IndexOf(
            "private async Task<AgentToolExecutionResult> RunDraftRepairStageAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "AppendChapterProductionEventAsync method boundary should be discoverable.");

        var helperSource = registrySource[methodStart..methodEnd];
        Assert.Contains("GetRequiredService<IProductionEventWriter>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IProductionEventWriter>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IProductionTruthStore>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new CreateProductionEventRequest", helperSource, StringComparison.Ordinal);

        Assert.DoesNotContain(".AppendEventAsync(", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new CreateProductionEventRequest", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new CreateTianmingPackageRequest", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreatePackageAsync(", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AttachLatestChapterVersionToRunAsync(", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SaveProjectFactSnapshotRequest", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".SaveFactSnapshotAsync(", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildCommittedChapterFactSnapshotJson", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<AgentToolExecutionResult> BuildChapterContextPackageAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<AgentToolExecutionResult> GenerateChapterWithChangesAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<AgentToolExecutionResult> ValidateChapterDraftAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<AgentToolExecutionResult> RepairChapterDraftAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task<AgentToolExecutionResult> CommitValidatedChapterAsync", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_RequiresProductionTruthRecordersForClosedLoopWrites()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var packageMethod = ExtractMethodSource(registrySource, "PersistChapterContextPackageAsync");
        var commitMethod = ExtractMethodSource(registrySource, "PersistChapterCommitProductionTruthAsync");

        Assert.Contains("GetRequiredService<IChapterContextPackageRecorder>", packageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IChapterContextPackageRecorder>", packageMethod, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IChapterCommitTruthRecorder>", commitMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IChapterCommitTruthRecorder>", commitMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("createdPackage == null", packageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("recorder == null", packageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("recorder == null", commitMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesNovelProductionStateQueryToProductionService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<INovelProductionStateQueryService, NovelProductionStateQueryService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "private async Task<AgentToolExecutionResult> QueryNovelProductionStateAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "QueryNovelProductionStateAsync method should exist.");

        var methodEnd = registrySource.IndexOf(
            "private async Task<AgentToolExecutionResult> AuditCommittedChapterAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "QueryNovelProductionStateAsync method boundary should be discoverable.");

        var helperSource = registrySource[methodStart..methodEnd];
        Assert.Contains("GetRequiredService<INovelProductionStateQueryService>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentRuntimeRuns", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianmingPackages", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductionEvents", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutboxEvents", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentToolExecutions", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionChainProjection_IsSharedByWorkflowLibraryAndAgentState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IProductionChainProjectionService, ProductionChainProjectionService>", program, StringComparison.Ordinal);

        var workflowService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Workflow",
            "WorkflowService.cs"));
        var chapterService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Chapters",
            "ChapterService.cs"));
        var productionStateService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Production",
            "NovelProductionStateQueryService.cs"));
        var projectWorkflow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Web",
            "NovelAgentWeb",
            "Support",
            "ProjectWorkflow.cs"));

        Assert.Contains("IProductionChainProjectionService", workflowService, StringComparison.Ordinal);
        Assert.Contains("BuildWorkflowChains(productionEvents)", workflowService, StringComparison.Ordinal);
        Assert.Contains("IProductionChainProjectionService", chapterService, StringComparison.Ordinal);
        Assert.Contains("BuildWorkflowChains(productionEvents)", chapterService, StringComparison.Ordinal);
        Assert.Contains("IProductionChainProjectionService", productionStateService, StringComparison.Ordinal);
        Assert.Contains("BuildNovelChains(", productionStateService, StringComparison.Ordinal);
        Assert.Contains("ProductionChainProjectionService.BuildWorkflowChainsCore", projectWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("private static WorkflowProductionChain BuildProductionChain", projectWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("private static List<NovelProductionChainState> BuildProductionChains", productionStateService, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesWorkspaceStateQueriesToWorkspaceService()
    {
        Assert.True(typeof(IWorkspaceStateQueryService).IsInterface);

        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IWorkspaceStateQueryService, WorkspaceStateQueryService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "private async Task<AgentToolExecutionResult> QueryWorkspaceStateAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "QueryWorkspaceStateAsync method should exist.");

        var methodEnd = registrySource.IndexOf(
            "private static string BuildWorkspaceStateMessage",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "QueryWorkspaceStateAsync method boundary should be discoverable.");

        var helperSource = registrySource[methodStart..methodEnd];
        Assert.Contains("GetRequiredService<IWorkspaceStateQueryService>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.Users", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.NovelProjects", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.KnowledgeBases", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.AgentRuns", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AgentWorkspaceState", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesProjectKnowledgeBindingQueriesToKnowledgeService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IProjectKnowledgeBindingQueryService, ProjectKnowledgeBindingQueryService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "private async Task<AgentToolExecutionResult> QueryProjectKnowledgeBindingsAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "QueryProjectKnowledgeBindingsAsync method should exist.");

        var methodEnd = registrySource.IndexOf(
            "private async Task<AgentToolExecutionResult> AttachKnowledgeToProjectAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "QueryProjectKnowledgeBindingsAsync method boundary should be discoverable.");

        var helperSource = registrySource[methodStart..methodEnd];
        Assert.Contains("GetRequiredService<IProjectKnowledgeBindingQueryService>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectKnowledgeUsages", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KnowledgeBases", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProjectKnowledgeBindingsAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProjectKnowledgeHardFactsAsync", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesProjectContentQueriesToContentService()
    {
        Assert.True(typeof(IProjectContentQueryService).IsInterface);

        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IProjectContentQueryService, ProjectContentQueryService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "private async Task<AgentToolExecutionResult> QueryProjectContentAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "QueryProjectContentAsync method should exist.");

        var methodEnd = registrySource.IndexOf(
            "private async Task<AgentToolExecutionResult> AuditCommittedChapterAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "QueryProjectContentAsync method boundary should be discoverable.");

        var helperSource = registrySource[methodStart..methodEnd];
        Assert.Contains("GetRequiredService<IProjectContentQueryService>", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.Chapters", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.ChapterVersions", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.TianmingPackages", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.ProductionEvents", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.ProjectFactSnapshots", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.ContentChunks", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class ProjectContentQueryResult", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class ProjectContentQueryItem", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class ProjectContentProductionEvent", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesCreativeIntentManagementToCreativeService()
    {
        Assert.True(typeof(ICreativeIntentService).IsInterface);

        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<ICreativeIntentService, CreativeIntentService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        foreach (var methodName in new[]
                 {
                     "CreateCreativeIntentAsync",
                     "DecideCreativeIntentAsync",
                     "QueryCreativeIntentsAsync",
                     "InjectAcceptedCreativeIntentsAsync"
                 })
        {
            var helperSource = ExtractMethodSource(registrySource, methodName);
            Assert.Contains("GetRequiredService<ICreativeIntentService>", helperSource, StringComparison.Ordinal);
            Assert.DoesNotContain("NovelAgentDbContext", helperSource, StringComparison.Ordinal);
            Assert.DoesNotContain("db.CreativeIntents", helperSource, StringComparison.Ordinal);
            Assert.DoesNotContain("new CreativeIntent", helperSource, StringComparison.Ordinal);
            Assert.DoesNotContain("GetService<NovelAgentDbContext>", helperSource, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("public sealed class CreativeIntentQueryResult", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class CreativeIntentItem", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeCreativeIntentSource", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreativeIntentAppliesToPackage", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesChapterContextEnrichmentToProductionService()
    {
        Assert.True(typeof(IChapterContextEnrichmentService).IsInterface);

        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Program.cs");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddScoped<IChapterContextEnrichmentService, ChapterContextEnrichmentService>", program, StringComparison.Ordinal);

        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var methodSource = ExtractMethodSource(registrySource, "InjectDatabaseHardFactsAsync");

        Assert.Contains("GetRequiredService<IChapterContextEnrichmentService>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchDatabaseKnowledgeAsync", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchSqliteHardFactLinesAsync", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProjectFactSnapshotLinesAsync", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryBoundProjectKnowledgeAsync", methodSource, StringComparison.Ordinal);

        Assert.DoesNotContain("ProjectFactSnapshotLines", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchSqliteHardFactLinesAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProjectFactSnapshotLinesAsync", registrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSnapshotLines", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesChapterContextPackageExistenceChecksToRecorder()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var methodSource = ExtractMethodSource(registrySource, "IsChapterContextPackagePersistedAsync");

        Assert.Contains("GetRequiredService<IChapterContextPackageRecorder>", methodSource, StringComparison.Ordinal);
        Assert.Contains(".ExistsAsync(", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianmingPackages", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesKnowledgeFileProcessingToKnowledgeProcessingService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var methodSource = ExtractMethodSource(registrySource, "ProcessKnowledgeFileAsync");

        Assert.Contains("GetRequiredService<IKnowledgeProcessingService>", methodSource, StringComparison.Ordinal);
        Assert.Contains(".ProcessPendingFileAsync(", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KnowledgeProcessingTasks", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_RequiresKnowledgeServiceForProjectKnowledgeSearch()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var methodSource = ExtractMethodSource(registrySource, "SearchDatabaseKnowledgeAsync");

        Assert.Contains("GetRequiredService<IKnowledgeService>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IKnowledgeService>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge search unavailable", methodSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IKnowledgeService is unavailable", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceFactory_RequiresCoreRuntimeServicesForProjectWorkspaces()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Workspace", "WorkspaceFactory.cs");
        var source = File.ReadAllText(sourcePath);
        var methodSource = ExtractMethodSource(source, "CreateWorkspaceAsync");

        Assert.Contains("GetRequiredService<IVectorStore>", methodSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IMicroEmbeddingService>", methodSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ICurrentUserService>", methodSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IAgentMemoryRepository>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IVectorStore>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IMicroEmbeddingService>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<ICurrentUserService>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IAgentMemoryRepository>", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WebGeneratedContentService_RequiresProductionTruthStoreForChapterVersions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebGeneratedContentService.cs");
        var source = File.ReadAllText(sourcePath);
        var methodSource = ExtractMethodSource(source, "PersistProductionTruthAsync");

        Assert.Contains("GetRequiredService<IProductionTruthStore>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IProductionTruthStore>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("truthStore == null", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WebGeneratedContentService_DoesNotKeepDirectChapterVectorSync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebGeneratedContentService.cs");
        var source = File.ReadAllText(sourcePath);
        var forbiddenFragments = new[]
        {
            "IVectorStore",
            "IMicroEmbeddingService",
            "UpsertChapterVectorsAsync",
            "ResolveChapterVectorId",
            "MarkChapterVectorPointsCompleted",
            "MarkChapterVectorPointsFailedAsync",
            "ChapterVectorChunk",
            "UpsertVectorsAsync",
            "DeleteVectorsByFilterAsync",
            "InitializeUserCollectionAsync",
            "CollectionExistsAsync"
        };

        var offenders = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AgentToolRegistry_RequiresExecutionLedgerForToolRuns()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        const string methodMarker = "public async Task<AgentToolExecutionResult> ExecuteAsync";
        var methodStart = registrySource.IndexOf(methodMarker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ExecuteAsync method should exist.");
        var methodEnd = registrySource.IndexOf("private static string? ResolveRunId", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "ExecuteAsync method boundary should be discoverable.");
        var methodSource = registrySource[methodStart..methodEnd];

        Assert.Contains("GetRequiredService<IServiceScopeFactory>", methodSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IAgentToolExecutionLedger>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IServiceScopeFactory>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IAgentToolExecutionLedger>", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ledger == null", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DelegatesProjectMaterialCountsToMaterialService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);
        var methodSource = ExtractMethodSource(registrySource, "CountProjectMaterialsAsync");

        Assert.Contains("GetRequiredService<IMaterialService>", methodSource, StringComparison.Ordinal);
        Assert.Contains(".CountProjectMaterialsAsync(", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.Materials", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_DoesNotSilentlyIgnoreMissingNovelAgentDbContext()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var registrySource = File.ReadAllText(registryPath);

        Assert.DoesNotContain("GetService<NovelAgentDbContext>", registrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceFactory_DoesNotResolveProductionKernelFromRootScope()
    {
        var repositoryRoot = FindRepositoryRoot();
        var factoryPath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Services", "Workspace", "WorkspaceFactory.cs");
        var factory = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("GetRequiredService<ITianmingProductionKernel>", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<ITianmingProductionKernel>", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("productionKernel", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRuntimeWorkspace_DelegatesProductionKernelAssemblyToProductionService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var source = File.ReadAllText(webRuntimePath);

        Assert.DoesNotContain("ITianmingProductionKernel productionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ITianmingProductionKernel? productionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("productionKernel = null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("productionKernel ??", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new HardcoreWritingEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new HardcoreWritingProductionKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new GenerationGate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new GuideManager", source, StringComparison.Ordinal);
        Assert.Contains("IWorkspaceProductionRuntimeBuilder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRuntime_DoesNotNameRootContextAsFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(repositoryRoot, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var source = File.ReadAllText(webRuntimePath);

        Assert.DoesNotContain("_fallbackProjectName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_fallbackStorageRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_fallbackServices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fallback static", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RegressionTests_DoNotCompileProductionSourcesIntoTestAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var regressionProject = Path.Combine(
            repositoryRoot,
            "Tests",
            "NovelAgentRegression",
            "NovelAgentRegression.csproj");
        var document = XDocument.Load(regressionProject);
        var projectDirectory = Path.GetDirectoryName(regressionProject)!;
        var productionRoots = new[]
        {
            Path.Combine(repositoryRoot, "Services"),
            Path.Combine(repositoryRoot, "Web")
        };

        var offenders = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(include => LooksLikeLocalPath(include))
            .Select(include => new
            {
                Include = include,
                FullPath = Path.GetFullPath(Path.Combine(projectDirectory, NormalizeMsBuildPath(include).Replace('*', '_')))
            })
            .Where(item => productionRoots.Any(root => IsInsideDirectory(item.FullPath, root)))
            .Select(item => $"Tests/NovelAgentRegression/NovelAgentRegression.csproj Compile.Include -> {item.Include}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !IsGitRepositoryRoot(directory.FullName))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Cannot find repository root.");
    }

    private static bool IsGitRepositoryRoot(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static bool IsSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".props" or ".targets";
    }

    private static bool IsFrontendSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".ts" or ".tsx";
    }

    private static bool IsProjectMetadataFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".csproj" or ".props" or ".targets";
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var marker = methodName + "(";
        var searchStart = 0;
        var methodNameIndex = -1;
        var methodStart = -1;
        while (searchStart < source.Length)
        {
            methodNameIndex = source.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (methodNameIndex < 0)
                break;

            methodStart = source.LastIndexOf('\n', methodNameIndex);
            methodStart = methodStart < 0 ? 0 : methodStart + 1;
            var declarationLine = source[methodStart..methodNameIndex];
            if (declarationLine.Contains("private ", StringComparison.Ordinal))
                break;

            searchStart = methodNameIndex + marker.Length;
            methodNameIndex = -1;
            methodStart = -1;
        }

        Assert.True(methodNameIndex >= 0 && methodStart >= 0, $"{methodName} method should exist.");

        var bodyStart = source.IndexOf('{', methodNameIndex);
        Assert.True(bodyStart > methodNameIndex, $"{methodName} method body should be discoverable.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[methodStart..(index + 1)];
            }
        }

        throw new InvalidOperationException($"{methodName} method body was not closed.");
    }

    private static IEnumerable<string> FindExternalMsBuildLocalIncludes(string projectFile, string repositoryRoot)
    {
        var document = XDocument.Load(projectFile);
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        foreach (var element in document.Descendants())
        {
            var itemName = element.Name.LocalName;
            if (!IsLocalBuildItem(itemName))
            {
                continue;
            }

            foreach (var attributeName in new[] { "Include", "Update" })
            {
                var value = element.Attribute(attributeName)?.Value;
                if (string.IsNullOrWhiteSpace(value) || !LooksLikeLocalPath(value))
                {
                    continue;
                }

                foreach (var include in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!LooksLikeLocalPath(include) || include.Contains('*', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
                    if (!IsInsideDirectory(fullPath, repositoryRoot))
                    {
                        yield return $"{SafeRelativePath(repositoryRoot, projectFile)} {itemName}.{attributeName} -> {include}";
                    }
                }
            }
        }
    }

    private static bool IsLocalBuildItem(string itemName) =>
        itemName is "Compile"
            or "Content"
            or "None"
            or "EmbeddedResource"
            or "AdditionalFiles"
            or "Analyzer";

    private static bool LooksLikeLocalPath(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0
            && !trimmed.StartsWith("$(", StringComparison.Ordinal)
            && !trimmed.StartsWith("@(", StringComparison.Ordinal)
            && !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMsBuildPath(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string SafeRelativePath(string root, string path) =>
        IsInsideDirectory(path, root) ? Path.GetRelativePath(root, path) : path;

    private static bool IsInsideDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.Ordinal);
    }

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/.git/", StringComparison.Ordinal)
            || normalized.Contains("/node_modules/", StringComparison.Ordinal)
            || normalized.Contains("/wwwroot/assets/", StringComparison.Ordinal);
    }
}
