using Xunit;

namespace Tests.Unit.Architecture;

public class RuntimePurityTests
{
    [Fact]
    public void RuntimeCode_DoesNotExposeLegacyContentPathContracts()
    {
        var root = FindRepositoryRoot();
        var runtimeFiles = EnumerateRuntimeFiles(root).ToList();

        var forbiddenTokens = new[]
        {
            "ContentPath",
            "FilePath",
            "ContextPackagePath",
            "GateReportPath",
            "content_path",
            "file_path",
            "context_package_path",
            "gate_report_path",
            "--migrate-legacy-content",
            "--migrate-memory",
            "MigrateLegacyContentToSqlite",
            "MigrateMemoryToSqlite",
            "LegacyContentMigration"
        };

        var violations = runtimeFiles
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(root, file)} contains {token}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void FrontendApiTypes_DoesNotExposeLegacyContentPathContracts()
    {
        var root = FindRepositoryRoot();
        var apiTypesPath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts");
        var text = File.ReadAllText(apiTypesPath);

        var forbiddenFields = new[]
        {
            "filePath",
            "contentPath",
            "storyBiblePath",
            "creativeKnowledgePath",
            "storagePath"
        };

        var violations = forbiddenFields
            .Where(field => text.Contains(field, StringComparison.Ordinal))
            .Select(field => $"{Path.GetRelativePath(root, apiTypesPath)} contains {field}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void FrontendAgentResume_UsesBackendResumeWithoutPersistingBusinessTruth()
    {
        var root = FindRepositoryRoot();
        var apiIndexPath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "api", "index.ts");
        var apiTypesPath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts");
        var agentStorePath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "stores", "useAgentStore.ts");
        var agentPagePath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "pages", "AgentPage.tsx");

        var apiIndex = File.ReadAllText(apiIndexPath);
        var apiTypes = File.ReadAllText(apiTypesPath);
        var agentStore = File.ReadAllText(agentStorePath);
        var agentPage = File.ReadAllText(agentPagePath);

        Assert.Contains("AgentSessionResumeResponse", apiTypes, StringComparison.Ordinal);
        Assert.Contains("resumeAgentSession", apiIndex, StringComparison.Ordinal);
        Assert.Contains("/agent/sessions/${encodeURIComponent(sessionId)}/resume", apiIndex, StringComparison.Ordinal);
        Assert.Contains("resumeAgentSession", agentPage, StringComparison.Ordinal);
        Assert.Contains("setResumeState", agentStore, StringComparison.Ordinal);

        var partializeStart = agentStore.IndexOf("partialize:", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Agent store must explicitly partialize persisted state.");
        var persistedSlice = agentStore[partializeStart..Math.Min(agentStore.Length, partializeStart + 240)];
        Assert.Contains("sessionId", persistedSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingConfirmation", persistedSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("discoveredTools", persistedSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("toolSearch", persistedSlice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCode_DoesNotUseBusinessFilesystemLegacyPaths()
    {
        var root = FindRepositoryRoot();
        var runtimeFiles = EnumerateRuntimeFiles(root).ToList();

        var forbiddenTokens = new[]
        {
            "Design\", \"elements.json",
            "Design\", \"globalsettings.json",
            "Generate\", \"elements.json",
            "StoragePathHelper.GetProjectConfigPath",
            "MaterialLibrary",
            "LoadLegacyCatalog",
            "SaveLegacyCatalog",
            "SyncLegacyProject",
            "RemoveLegacyProject",
            "GetLegacyCatalogPath",
            "GetProjectChaptersPath",
            "user_settings.json",
            "legacy JSON",
            "DB知识优先，legacy JSON 作为回退"
        };

        var violations = runtimeFiles
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(root, file)} contains {token}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void CommonRuntime_DoesNotLoadBusinessDataFromFilesystem()
    {
        var root = FindRepositoryRoot();
        var commonFiles = EnumerateRuntimeFiles(root)
            .Where(file => Path.GetRelativePath(root, file)
                .StartsWith(Path.Combine("Web", "NovelAgentWeb", "Common"), StringComparison.Ordinal))
            .ToList();

        var forbiddenTokens = new[]
        {
            "File.",
            "Directory.",
            "Path.",
            "StoragePathHelper",
            "JsonDocument",
            "Design/elements.json",
            "elements.json",
            "globalsettings.json",
            "generate_elements.json",
            "guides"
        };

        var violations = commonFiles
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(root, file)} contains {token}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void WebRuntime_DoesNotWireFileBackedSearchOrVectorIndexes()
    {
        var root = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var text = File.ReadAllText(webRuntimePath);

        var forbiddenTokens = new[]
        {
            "new ContentChunkSearchService(",
            "new ChapterEmbeddingIndex(",
            "new ChunkEmbeddingIndex(",
            "Register(contentChunkSearch)",
            "Register(chapterEmbeddingIndex)",
            "Register(chunkEmbeddingIndex)"
        };

        var violations = forbiddenTokens
            .Where(token => text.Contains(token, StringComparison.Ordinal))
            .Select(token => $"{Path.GetRelativePath(root, webRuntimePath)} contains {token}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void WebRuntime_DisablesProjectBusinessFilesystemPathHelpers()
    {
        var root = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var text = File.ReadAllText(webRuntimePath);

        Assert.Contains("Project business data is stored in SQLite, Redis, and Qdrant", text, StringComparison.Ordinal);
        Assert.Contains("public static string GetCurrentProjectPath()", text, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureProjectDirectories();", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(WebStorageRoot, \"Projects\", CurrentProjectName)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WebProject_DoesNotCompileFileBackedSearchOrVectorIndexes()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var text = File.ReadAllText(projectPath);

        var forbiddenTokens = new[]
        {
            "FileBasedVectorIndex.cs",
            "ChapterEmbeddingIndex.cs",
            "ChunkEmbeddingIndex.cs",
            "EntityFirstChapterIndex.cs",
            "ContentChunkSearchService.cs"
        };

        var violations = forbiddenTokens
            .Where(token => text.Contains(token, StringComparison.Ordinal))
            .Select(token => $"{Path.GetRelativePath(root, projectPath)} contains {token}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void NovelProjectCatalog_DoesNotUseLegacyFilesystemCatalogOrGlobalProjectSwitch()
    {
        var root = FindRepositoryRoot();
        var catalogPath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "NovelProjectCatalog.cs");
        var text = File.ReadAllText(catalogPath);

        var forbiddenTokens = new[]
        {
            "projects.json",
            "File.",
            "Directory.",
            "StoragePathHelper.CurrentProjectName"
        };

        var violations = forbiddenTokens
            .Where(token => text.Contains(token, StringComparison.Ordinal))
            .Select(token => $"{Path.GetRelativePath(root, catalogPath)} contains {token}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void AgentRuntime_RebindsWorkspaceAndRequestContextAfterProjectChanges()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var text = File.ReadAllText(runtimePath);

        Assert.Contains("SetWorkspaceContext", text, StringComparison.Ordinal);
        Assert.Contains("workspace.SetRequestContext()", text, StringComparison.Ordinal);
        Assert.Contains("ProjectScopedExecutor.SetCatalog", text, StringComparison.Ordinal);
        Assert.Contains("EnsureWorkspaceForSessionProjectAsync", text, StringComparison.Ordinal);
        Assert.Contains("_workspaceFactory.AcquireAsync(userId, session.ActiveProjectId", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntime_HydratesLayeredMemoryBeforeProjectObservation()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var text = File.ReadAllText(runtimePath);

        Assert.Contains("await _memoryService.HydrateAsync(session, project, bible, ct)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntime_PersistsSessionMemoryBeforePureTextReplySave()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var text = File.ReadAllText(runtimePath);
        var methodStart = text.IndexOf("private async Task<AgentChatResponse> FinishTextResponse", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "FinishTextResponse must exist.");
        var methodEnd = text.IndexOf("private MemoryUpdateTrigger DetermineUpdateTrigger", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "FinishTextResponse body boundary must be findable.");
        var methodBody = text[methodStart..methodEnd];
        var persistIndex = methodBody.IndexOf("PersistTextSessionMemoryAsync", StringComparison.Ordinal);
        var saveIndex = methodBody.IndexOf("_sessionManager.SaveSessionAsync", StringComparison.Ordinal);

        Assert.True(persistIndex >= 0, "Pure text responses must persist session memory before session_data is saved.");
        Assert.True(saveIndex > persistIndex, "Session memory persistence must happen before SaveSessionAsync.");
    }

    [Fact]
    public void AgentRuntime_AllowsProjectlessMetaToolsBeforeProjectResolution()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var text = File.ReadAllText(runtimePath);

        Assert.Contains("CanExecuteWithoutProject", text, StringComparison.Ordinal);
        Assert.Contains("\"tool_search\"", text, StringComparison.Ordinal);
        Assert.Contains("\"ResolveNovelProject\"", text, StringComparison.Ordinal);
        Assert.Contains("BuildProjectlessReflectContext", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceUsageAudit_DoesNotRequireProjectIdForAgentManagedEndpoints()
    {
        var root = FindRepositoryRoot();
        var middlewarePath = Path.Combine(root, "Web", "NovelAgentWeb", "Middleware", "WorkspaceUsageAuditMiddleware.cs");
        var text = File.ReadAllText(middlewarePath);

        Assert.Contains("IsAgentManagedWorkspaceEndpoint", text, StringComparison.Ordinal);
        Assert.Contains("path.StartsWith(\"/api/agent\"", text, StringComparison.Ordinal);

        var methodStart = text.IndexOf("private bool IsWorkspaceEndpoint", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "IsWorkspaceEndpoint must exist.");
        var methodEnd = text.IndexOf("private async Task<string?> ExtractProjectIdAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "IsWorkspaceEndpoint body boundary must be findable.");
        var methodBody = text[methodStart..methodEnd];

        Assert.Contains("IsAgentManagedWorkspaceEndpoint(path)", methodBody, StringComparison.Ordinal);
        Assert.Contains("return false", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectScopedExecutor_DoesNotFallbackSessionScopeToActiveProject()
    {
        var root = FindRepositoryRoot();
        var executorPath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "ProjectScopedExecutor.cs");
        var text = File.ReadAllText(executorPath);
        var runSessionStart = text.IndexOf("public async Task<T> RunSessionAsync", StringComparison.Ordinal);
        Assert.True(runSessionStart >= 0, "RunSessionAsync must exist.");
        var runSessionBody = text[runSessionStart..];

        Assert.Contains("No active project in session", runSessionBody, StringComparison.Ordinal);
        Assert.DoesNotContain("project ??= await _catalog.GetActiveAsync", runSessionBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticKernelPlugins_DoNotResolveFileBackedGeneratedContentService()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            "Services/Framework/AI/SemanticKernel",
            "Services/Framework/AI/EntityPropagationService.cs"
        };

        var files = paths.SelectMany(path =>
        {
            var fullPath = Path.Combine(root, path);
            return File.Exists(fullPath)
                ? new[] { fullPath }
                : Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories);
        });

        var forbiddenTokens = new[]
        {
            "ServiceLocator.Get<GeneratedContentService>",
            "ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>"
        };

        var violations = files
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(root, file)} contains {token}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> EnumerateRuntimeFiles(string root)
    {
        var paths = new[]
        {
            "Web/NovelAgentWeb/Common",
            "Web/NovelAgentWeb/Controllers",
            "Web/NovelAgentWeb/Data/Entities",
            "Web/NovelAgentWeb/DTOs",
            "Web/NovelAgentWeb/Models",
            "Web/NovelAgentWeb/Services",
            "Web/NovelAgentWeb/Support",
            "Web/NovelAgentWeb/Program.cs"
        };

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(root, path);
            if (File.Exists(fullPath))
            {
                yield return fullPath;
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                         .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Legacy{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                yield return file;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Web", "NovelAgentWeb")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Tests", "Unit")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
