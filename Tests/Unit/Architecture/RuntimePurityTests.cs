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
    public void FrontendAgentProgressPanel_RendersProductionProgressAsStageTimeline()
    {
        var root = FindRepositoryRoot();
        var apiTypesPath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "api", "types.ts");
        var agentPagePath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "pages", "AgentPage.tsx");

        var apiTypes = File.ReadAllText(apiTypesPath);
        var agentPage = File.ReadAllText(agentPagePath);

        Assert.Contains("stage?: string", apiTypes, StringComparison.Ordinal);
        Assert.Contains("status?: string", apiTypes, StringComparison.Ordinal);
        Assert.Contains("displaySurface?: string", apiTypes, StringComparison.Ordinal);
        Assert.Contains("production_progress", agentPage, StringComparison.Ordinal);
        Assert.Contains("productionStageLabel", agentPage, StringComparison.Ordinal);
        Assert.Contains("productionStatusLabel", agentPage, StringComparison.Ordinal);
        Assert.Contains("构建章节上下文包", agentPage, StringComparison.Ordinal);
        Assert.Contains("生成章节正文", agentPage, StringComparison.Ordinal);
        Assert.Contains("提交书城", agentPage, StringComparison.Ordinal);
        Assert.Contains("'production_progress'", agentPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendWorkflowProductionChains_DoNotTruncateCanonicalProductionSteps()
    {
        var root = FindRepositoryRoot();
        var workflowPagePath = Path.Combine(root, "Web", "NovelAgentWeb.Frontend", "src", "pages", "WorkflowPage.tsx");
        var workflowPage = File.ReadAllText(workflowPagePath);

        Assert.DoesNotContain("chain.steps.slice(0,", workflowPage, StringComparison.Ordinal);
        Assert.Contains("chain.steps.map", workflowPage, StringComparison.Ordinal);
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
    public void CreativeKnowledgeRuntime_DoesNotKeepFileBackedKnowledgeDocumentAdapter()
    {
        var root = FindRepositoryRoot();
        var servicePath = Path.Combine(
            root,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "CreativeKnowledgeBaseService.cs");
        var orchestratorPath = Path.Combine(
            root,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "NovelAgentOrchestrator.cs");
        var regressionProgramPath = Path.Combine(root, "Tests", "NovelAgentRegression", "Program.cs");

        var serviceSource = File.Exists(servicePath) ? File.ReadAllText(servicePath) : string.Empty;
        var orchestratorSource = File.Exists(orchestratorPath) ? File.ReadAllText(orchestratorPath) : string.Empty;
        var regressionProgramSource = File.Exists(regressionProgramPath) ? File.ReadAllText(regressionProgramPath) : string.Empty;
        var forbiddenServiceFragments = new[]
        {
            "creative_knowledge_base.json",
            "StorageSubPath",
            "KnowledgeFileName",
            "StoragePathHelper",
            "GetStoragePath",
            "LoadAsync(",
            "LoadWithoutLockAsync",
            "SaveWithoutLockAsync",
            "AddEntryAsync",
            "UpdateEntryAsync",
            "DeleteEntryAsync",
            "RecordUsedPatternAsync"
        };
        var forbiddenOrchestratorFragments = new[]
        {
            "AddCreativeKnowledgeEntryAsync",
            "RecordUsedPlotPatternAsync"
        };
        var forbiddenRegressionFragments = new[]
        {
            "RecordUsedPatternAsync",
            "CreativeKnowledgeBaseDocument",
            "CreativeKnowledgeMutationResult"
        };

        var violations = forbiddenServiceFragments
            .Where(fragment => serviceSource.Contains(fragment, StringComparison.Ordinal))
            .Select(fragment => $"{Path.GetRelativePath(root, servicePath)} contains {fragment}")
            .Concat(forbiddenOrchestratorFragments
                .Where(fragment => orchestratorSource.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, orchestratorPath)} contains {fragment}"))
            .Concat(forbiddenRegressionFragments
                .Where(fragment => regressionProgramSource.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, regressionProgramPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void StoryBibleRuntime_DoesNotExposeStoragePathCompatibilityContract()
    {
        var root = FindRepositoryRoot();
        var servicePath = Path.Combine(
            root,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "StoryBibleService.cs");
        var modelPath = Path.Combine(
            root,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Models",
            "StoryBibleModels.cs");
        var orchestratorPath = Path.Combine(
            root,
            "Services",
            "Framework",
            "AI",
            "NovelAgent",
            "Services",
            "NovelAgentOrchestrator.cs");
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");

        var serviceSource = File.ReadAllText(servicePath);
        var modelSource = File.ReadAllText(modelPath);
        var orchestratorSource = File.ReadAllText(orchestratorPath);
        var webRuntimeSource = File.ReadAllText(webRuntimePath);

        var forbiddenServiceFragments = new[]
        {
            "StorageSubPath",
            "StoryBibleFileName",
            "StoragePathHelper",
            "GetStoragePath",
            "_storageIdentity",
            "sqlite-redis://story-bible"
        };
        var forbiddenModelFragments = new[]
        {
            "StoragePath",
            "storagePath"
        };
        var forbiddenOrchestratorFragments = new[]
        {
            "StoragePath = _storyBibleService.GetStoragePath"
        };
        var forbiddenWebRuntimeFragments = new[]
        {
            "sqlite-redis://story-bible"
        };

        var violations = forbiddenServiceFragments
            .Where(fragment => serviceSource.Contains(fragment, StringComparison.Ordinal))
            .Select(fragment => $"{Path.GetRelativePath(root, servicePath)} contains {fragment}")
            .Concat(forbiddenModelFragments
                .Where(fragment => modelSource.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, modelPath)} contains {fragment}"))
            .Concat(forbiddenOrchestratorFragments
                .Where(fragment => orchestratorSource.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, orchestratorPath)} contains {fragment}"))
            .Concat(forbiddenWebRuntimeFragments
                .Where(fragment => webRuntimeSource.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, webRuntimePath)} contains {fragment}"))
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
    public void WebRuntimeServiceLocator_DoesNotKeepRootStaticFallbackContainer()
    {
        var root = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var text = File.ReadAllText(webRuntimePath);
        var serviceLocatorStart = text.IndexOf("public static class ServiceLocator", StringComparison.Ordinal);
        Assert.True(serviceLocatorStart >= 0, "WebRuntime ServiceLocator adapter should be discoverable.");
        var serviceLocatorEnd = text.IndexOf("namespace System.Threading.Tasks", serviceLocatorStart, StringComparison.Ordinal);
        Assert.True(serviceLocatorEnd > serviceLocatorStart, "WebRuntime ServiceLocator adapter boundary should be discoverable.");
        var serviceLocatorSource = text[serviceLocatorStart..serviceLocatorEnd];

        Assert.Contains("AsyncLocal<ConcurrentDictionary<Type, object>?>", serviceLocatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_rootServices", serviceLocatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("?? _rootServices", serviceLocatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Root static dictionary", serviceLocatorSource, StringComparison.Ordinal);
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
    public void WebRuntime_RemovesProjectBusinessFilesystemPathHelperCompatibilityShell()
    {
        var root = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var text = File.ReadAllText(webRuntimePath);

        Assert.DoesNotContain("StoragePathHelper", text, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace TM.Framework.Common.Helpers.Storage", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCurrentProjectPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProjectConfigPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProjectHistoryPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProjectValidationPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetServicesStoragePath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetModulesStoragePath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureProjectDirectories();", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(WebStorageRoot", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NovelAgentWorkspace_RequiresDatabaseScopeAtCompileTime()
    {
        var root = FindRepositoryRoot();
        var webRuntimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "WebRuntime.cs");
        var text = File.ReadAllText(webRuntimePath);

        Assert.DoesNotContain("IServiceScopeFactory? scopeFactory = null", text, StringComparison.Ordinal);
        Assert.DoesNotContain("if (scopeFactory == null)", text, StringComparison.Ordinal);
        Assert.Contains("IServiceScopeFactory scopeFactory", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceProductionRuntimeRequest_RequiresRealProjectRuntimeDependenciesAtCompileTime()
    {
        var root = FindRepositoryRoot();
        var runtimeBuilderContractPath = Path.Combine(
            root,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Production",
            "IWorkspaceProductionRuntimeBuilder.cs");
        var text = File.ReadAllText(runtimeBuilderContractPath);

        Assert.Contains("IServiceScopeFactory ScopeFactory", text, StringComparison.Ordinal);
        Assert.Contains("IUnifiedValidationService UnifiedValidationService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceScopeFactory? ScopeFactory", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IUnifiedValidationService? UnifiedValidationService", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceProductionRuntimeBuilder_DoesNotKeepMissingScopeCompatibilityBranches()
    {
        var root = FindRepositoryRoot();
        var builderPath = Path.Combine(
            root,
            "Web",
            "NovelAgentWeb",
            "Services",
            "Production",
            "WorkspaceProductionRuntimeBuilder.cs");
        var text = File.ReadAllText(builderPath);

        Assert.DoesNotContain("request.ScopeFactory != null", text, StringComparison.Ordinal);
        Assert.DoesNotContain("request.ScopeFactory == null", text, StringComparison.Ordinal);
        Assert.DoesNotContain("null!", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntimeCode_DoesNotKeepScopeRequiredCompatibilityServices()
    {
        var root = FindRepositoryRoot();
        var productionRoots = new[]
        {
            Path.Combine(root, "Web", "NovelAgentWeb", "Services", "Production"),
            Path.Combine(root, "Web", "NovelAgentWeb", "Support")
        };

        var violations = productionRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("ScopeRequired", StringComparison.Ordinal)
                    ? new[] { Path.GetRelativePath(root, path) }
                    : Array.Empty<string>();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void RuntimeCode_DoesNotKeepGlobalProjectChangedCompatibilityEvent()
    {
        var root = FindRepositoryRoot();
        var runtimeFiles = EnumerateRuntimeFiles(root).ToList();

        var violations = runtimeFiles
            .Where(file => File.ReadAllText(file).Contains("CurrentProjectChanged", StringComparison.Ordinal))
            .Select(file => $"{Path.GetRelativePath(root, file)} still references CurrentProjectChanged")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedVersionTrackingRegistry()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "VersionTracking", "VersionTrackingService.cs"),
            Path.Combine(root, "Services", "Modules", "VersionTracking", "Models", "VersionRegistry.cs")
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepUnusedFileBackedGuideStores()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "ChapterKeyEventStore.cs")
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedChapterChangesWalStore()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "ChapterChangesWalStore.cs")
        };
        var forbiddenFragments = new[]
        {
            "ChapterChangesWalStore",
            "changes_wal"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedKeywordChapterIndexService()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "KeywordChapterIndexService.cs")
        };
        var forbiddenFragments = new[]
        {
            "KeywordChapterIndexService",
            "keyword_index.json"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedPlotPointsIndexService()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "PlotPointsIndexService.cs")
        };
        var forbiddenFragments = new[]
        {
            "PlotPointsIndexService",
            "guides\", \"plot_points",
            "plot_points"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedChapterSummaryStore()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "ChapterSummaryStore.cs")
        };
        var forbiddenFragments = new[]
        {
            "ChapterSummaryStore",
            "guides\", \"summaries",
            "guides/summaries"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedChapterMilestoneStore()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "ChapterMilestoneStore.cs")
        };
        var forbiddenFragments = new[]
        {
            "ChapterMilestoneStore",
            "guides\", \"milestones",
            "guides/milestones"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepFileBackedVolumeFactArchiveStore()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "Web", "NovelAgentWeb", "NovelAgentWeb.csproj");
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "VolumeFactArchiveStore.cs")
        };
        var forbiddenFragments = new[]
        {
            "VolumeFactArchiveStore",
            "guides\", \"fact_archives",
            "guides/fact_archives"
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .Concat(forbiddenFragments
                .Where(fragment => File.ReadAllText(projectPath).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(root, projectPath)} contains {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void TrackingServices_DoNotReadDesignElementsJsonForDisplayNames()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "CharacterStateService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "LocationStateService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "FactionStateService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "ConflictProgressService.cs")
        };
        var forbiddenFragments = new[]
        {
            "StoragePathHelper.GetProjectConfigPath",
            "\"Design\", \"elements.json\"",
            "File.ReadAllTextAsync(elementsPath)",
            "characterrules",
            "locationrules",
            "factionrules",
            "plotrules"
        };

        var violations = files
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file);
                return forbiddenFragments
                    .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
                    .Select(fragment => $"{Path.GetRelativePath(root, file)} contains {fragment}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void RelationStrengthService_DoesNotReadFileBackedIndexesOrModuleDirectories()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Tracking", "RelationStrengthService.cs");
        var source = File.ReadAllText(file);
        var forbiddenFragments = new[]
        {
            "relation_strength_index.json",
            "StoragePathHelper.GetProjectConfigPath",
            "StoragePathHelper.GetStorageRoot",
            "Modules\", \"Design",
            "Modules\", \"Generate",
            "File.OpenRead",
            "Directory.GetFiles"
        };

        var violations = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .Select(fragment => $"{Path.GetRelativePath(root, file)} contains {fragment}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void GuideRuntime_DoesNotReadLegacyProjectGuidesOrModuleDirectories()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "GuideContextService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "GuideContextService"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "GuideIndexBuilder.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "GuideIndexBuilder"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Guides", "GuideManager.cs")
        };
        var forbiddenFragments = new[]
        {
            "StoragePathHelper.GetProjectConfigPath",
            "StoragePathHelper.GetStorageRoot",
            "\"Projects\", projectName, \"Config\", \"guides\"",
            "\"Modules\", relativePath",
            "\"Modules\", \"Design\"",
            "\"Design\", \"elements.json\"",
            "\"Design\", \"globalsettings.json\"",
            "\"Generate\", \"elements.json\"",
            "\"Generate\", \"globalsettings.json\"",
            "content_guide_vol",
            "content_guide.json",
            "relationships.json",
            "File.OpenRead",
            "File.ReadAllTextAsync",
            "FileStream(",
            "Directory.GetFiles"
        };

        var sourceFiles = files
            .SelectMany(path =>
                File.Exists(path)
                    ? new[] { path }
                    : Directory.Exists(path)
                        ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                        : Array.Empty<string>())
            .ToList();

        var violations = sourceFiles
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file);
                return forbiddenFragments
                    .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
                    .Select(fragment => $"{Path.GetRelativePath(root, file)} contains {fragment}");
            })
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void GuideManager_DoesNotKeepLegacyFileFlushOrRecoveryApi()
    {
        var root = FindRepositoryRoot();
        var guideManagerPath = Path.Combine(
            root,
            "Services",
            "Modules",
            "ProjectData",
            "Implementations",
            "Guides",
            "GuideManager.cs");
        var source = File.ReadAllText(guideManagerPath);

        var forbiddenFragments = new[]
        {
            "FlushAllAsync",
            "RecoverPendingFlush",
            "FlushOnExitAsync",
            "ShouldFlush",
            "DiscardDirtyAndEvict",
            "CleanupExpiredCache",
            "EntryChanged",
            "RaiseEntryChanged"
        };

        var violations = forbiddenFragments
            .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
            .Select(fragment => $"{Path.GetRelativePath(root, guideManagerPath)} contains legacy guide API {fragment}")
            .ToList();

        Assert.Empty(violations);
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
    public void Repository_DoesNotKeepFileBackedSearchOrVectorIndexImplementations()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "FileBasedVectorIndex.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "ChapterEmbeddingIndex.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "ChunkEmbeddingIndex.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "EntityFirstChapterIndex.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IEntityFirstChapterIndex.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "ContentChunkSearchService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Generation", "EntityRebuildSubscription.cs")
        };

        var violations = forbiddenPaths
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepUncompiledLegacyProjectDataFilePipeline()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Generation", "HumanizeRules"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Generation", "GenerationStatisticsService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Summary"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Validation", "ConsistencyReconciler.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Validation", "ConsistencyReconciler"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Validation", "ValidationSummaryService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "ChangeDetection"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "DataIndexService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Indexing", "IndexService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Context"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "PackagingAllowlist.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Implementations", "Packaging"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Helpers", "NavigationConfigParser.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IChangeDetectionService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IContextService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IFocusContextService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IModuleEnabledService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IPackageHistoryService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IPublishService.cs"),
            Path.Combine(root, "Services", "Modules", "ProjectData", "Interfaces", "IValidationSummaryService.cs")
        };

        var violations = forbiddenPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_DoesNotKeepUncompiledLegacyFrameworkSettingsPipeline()
    {
        var root = FindRepositoryRoot();
        var forbiddenPaths = new[]
        {
            Path.Combine(root, "Services", "Framework", "Settings", "SettingsManager.cs"),
            Path.Combine(root, "Services", "Framework", "Settings", "LogManager.cs"),
            Path.Combine(root, "Services", "Framework", "Settings", "LogManager")
        };

        var violations = forbiddenPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionSource_DoesNotReferenceRemovedFileBackedVectorIndexClasses()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "Services"),
            Path.Combine(root, "Web", "NovelAgentWeb")
        };
        var forbiddenFragments = new[]
        {
            "new ChapterEmbeddingIndex",
            "new ChunkEmbeddingIndex",
            "new EntityFirstChapterIndex",
            "ServiceLocator.Get<ChapterEmbeddingIndex",
            "ServiceLocator.Get<ChunkEmbeddingIndex",
            "ServiceLocator.Get<EntityFirstChapterIndex",
            "ServiceLocator.Get<Indexing.ChapterEmbeddingIndex",
            "ServiceLocator.Get<Indexing.ChunkEmbeddingIndex",
            "ServiceLocator.Get<Indexing.EntityFirstChapterIndex",
            "IEntityFirstChapterIndex",
            "ChapterEmbeddingIndex chapterIdx",
            "ChunkEmbeddingIndex chunkIdx",
            "EntityFirstChapterIndex firstIdx",
            "FileBasedVectorIndex"
        };

        var violations = sourceRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return forbiddenFragments
                    .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
                    .Select(fragment => $"{Path.GetRelativePath(root, path)} contains {fragment}");
            })
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
        Assert.Contains("AgentObservationBuilder.SetWorkspace", text, StringComparison.Ordinal);
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
        Assert.Contains("_toolRegistry.Find", text, StringComparison.Ordinal);
        Assert.Contains("ImpactScope", text, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(toolName, \"QueryWorkspaceState\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(toolName, \"SearchCreativeKnowledge\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(toolName, \"ResolveNovelProject\"", text, StringComparison.Ordinal);
        Assert.Contains("BuildProjectlessReflectContext", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentPlanner_ReceivesProductSpaceMemoryAndToolSemanticsWithoutSuppressingToolUse()
    {
        var root = FindRepositoryRoot();
        var corePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentCore.cs");
        var text = File.ReadAllText(corePath);

        Assert.Contains("ProductSpace", text, StringComparison.Ordinal);
        Assert.Contains("WorkspaceState", text, StringComparison.Ordinal);
        Assert.Contains("memory_layers", text, StringComparison.Ordinal);
        Assert.Contains("memory_context", text, StringComparison.Ordinal);
        Assert.Contains("product_space", text, StringComparison.Ordinal);
        Assert.Contains("workspace_state", text, StringComparison.Ordinal);
        Assert.Contains("DomainSurface", text, StringComparison.Ordinal);
        Assert.Contains("OutputKind", text, StringComparison.Ordinal);
        Assert.Contains("ReadsFrom", text, StringComparison.Ordinal);
        Assert.Contains("WritesTo", text, StringComparison.Ordinal);
        Assert.Contains("UserVisibleWhere", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT QueryProjectStatus tool", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Use chat_reply for greetings, questions, status queries", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentObservationBuilder_UsesRealWorkspaceStateInsteadOfOnlyHint()
    {
        var root = FindRepositoryRoot();
        var corePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentCore.cs");
        var text = File.ReadAllText(corePath);

        Assert.Contains("BuildWorkspaceStateAsync", text, StringComparison.Ordinal);
        Assert.Contains("IWorkspaceStateQueryService", text, StringComparison.Ordinal);
        Assert.Contains("WorkspaceStateQueryRequest", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceState = AgentWorkspaceState.Hint(session),", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentPlanner_DoesNotPromoteSpecificBusinessToolsInInstructions()
    {
        var root = FindRepositoryRoot();
        var coreText = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentCore.cs"));
        var toolCallingText = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentToolCallingClient.cs"));

        Assert.DoesNotContain("Call ResolveNovelProject", coreText, StringComparison.Ordinal);
        Assert.DoesNotContain("Use SearchCreativeKnowledge", coreText, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryWorkspaceState when", coreText, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryProjectStatus", toolCallingText, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryWorkspaceState", toolCallingText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolPolicy_DoesNotSpecialCaseWorkspaceKnowledgeOrProjectTools()
    {
        var root = FindRepositoryRoot();
        var kernelText = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentKernel.cs"));

        Assert.DoesNotContain("\"QueryWorkspaceState\" or \"QueryProjectStatus\"", kernelText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SearchCreativeKnowledge\" or \"ResolveNovelProject\"", kernelText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"QueryProjectContent\" or \"QueryNovelProductionState\"", kernelText, StringComparison.Ordinal);
        Assert.Contains("AllowKnownNonCreativeTool", kernelText, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentPlannerInstructions_DoNotAdvertiseInternalChapterProductionStages()
    {
        var root = FindRepositoryRoot();
        var coreText = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentCore.cs"));

        Assert.DoesNotContain("工作流自带校验机制（如ValidateChapterDraft）", coreText, StringComparison.Ordinal);
        Assert.DoesNotContain("不要说 pending_quality_review 或 CommitValidatedChapter", coreText, StringComparison.Ordinal);
        Assert.Contains("ProduceChapter", coreText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSpaceCatalog_DescribesCapabilitiesNotPrimaryTools()
    {
        var root = FindRepositoryRoot();
        var catalogText = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentProductSpaceCatalog.cs"));

        Assert.DoesNotContain("PrimaryTools", catalogText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ResolveNovelProject\"", catalogText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"QueryWorkspaceState\"", catalogText, StringComparison.Ordinal);
        Assert.Contains("Capabilities", catalogText, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentToolRegistry_ExposesWorkspaceStateToolAndSemanticContracts()
    {
        var root = FindRepositoryRoot();
        var registryPath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentToolRegistry.cs");
        var text = File.ReadAllText(registryPath);

        Assert.Contains("\"QueryWorkspaceState\"", text, StringComparison.Ordinal);
        Assert.Contains("QueryWorkspaceStateAsync", text, StringComparison.Ordinal);
        Assert.Contains("DomainSurface", text, StringComparison.Ordinal);
        Assert.Contains("OutputKind", text, StringComparison.Ordinal);
        Assert.Contains("ReadsFrom", text, StringComparison.Ordinal);
        Assert.Contains("WritesTo", text, StringComparison.Ordinal);
        Assert.Contains("UserVisibleWhere", text, StringComparison.Ordinal);
        Assert.Contains("novel_projects", text, StringComparison.Ordinal);
        Assert.Contains("knowledge_base", text, StringComparison.Ordinal);
        Assert.Contains("agent_runs", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentMemoryService_HydratesAndPersistsProjectlessMemory()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var memoryPath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentMemoryService.cs");
        var runtime = File.ReadAllText(runtimePath);
        var memory = File.ReadAllText(memoryPath);

        Assert.Contains("HydrateProjectlessAsync", memory, StringComparison.Ordinal);
        Assert.Contains("PersistProjectlessAsync", memory, StringComparison.Ordinal);
        Assert.Contains("await _memoryService.HydrateProjectlessAsync(session, ct)", runtime, StringComparison.Ordinal);
        Assert.Contains("PersistProjectlessAsync(session", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntime_ProjectlessObservationUsesDiscoveredToolCache()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentRuntime.cs");
        var text = File.ReadAllText(runtimePath);
        var methodStart = text.IndexOf("private static AgentObservationContext BuildProjectlessReflectContext", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "BuildProjectlessReflectContext must exist.");
        var methodEnd = text.IndexOf("// ═══════════════════════════════════════════════════════════════", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "BuildProjectlessReflectContext body boundary must be findable.");
        var methodBody = text[methodStart..methodEnd];

        Assert.Contains("session.DiscoveredTools.Count > 0", methodBody, StringComparison.Ordinal);
        Assert.Contains("ToAgentToolDefinition", methodBody, StringComparison.Ordinal);
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
                : Directory.Exists(fullPath)
                    ? Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                    : Array.Empty<string>();
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
