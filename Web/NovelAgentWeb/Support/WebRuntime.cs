using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Guides;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM
{
    public static class App
    {
        public static bool IsDebugMode { get; set; }
        public static List<string> Logs { get; } = new();

        public static void Log(string message)
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (Logs.Count > 1000) Logs.RemoveRange(0, Logs.Count - 1000);
        }
    }
}

namespace TM.Framework.Common.Helpers
{
    public static class JsonHelper
    {
        public static JsonSerializerOptions Default { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static JsonSerializerOptions CnDefault { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public static class InfoLogDedup
    {
        private static readonly ConcurrentDictionary<string, byte> Keys = new(StringComparer.Ordinal);

        public static bool ShouldLog(string key) => Keys.TryAdd(key, 0);

        public static void DebugLogOnce(string key, Exception ex, string tag = "")
        {
            if (!ShouldLog($"{tag}:{key}")) return;
            TM.App.Log($"[{tag}] {key}: {ex.Message}");
        }
    }
}

namespace TM.Framework.Common.Helpers.Numerics
{
    public static class VectorMath
    {
        public static float DotProduct(float[] a, float[] b) => DotProduct(a.AsSpan(), b.AsSpan());

        public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            var len = Math.Min(a.Length, b.Length);
            var sum = 0f;
            for (var i = 0; i < len; i++) sum += a[i] * b[i];
            return sum;
        }

        public static bool L2NormalizeInPlace(float[] vector) => L2NormalizeInPlace(vector.AsSpan());

        public static bool L2NormalizeInPlace(Span<float> vector)
        {
            if (vector.Length == 0) return false;
            var normSquared = 0f;
            for (var i = 0; i < vector.Length; i++)
                normSquared += vector[i] * vector[i];
            var norm = MathF.Sqrt(normSquared);
            if (norm <= 1e-12f) return false;
            for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
            return true;
        }

        public static (sbyte[] Quantized, float Scale) QuantizeInt8(ReadOnlySpan<float> vector)
        {
            var quantized = new sbyte[vector.Length];
            QuantizeInt8(vector, quantized, out var scale);
            return (quantized, scale);
        }

        public static void QuantizeInt8(ReadOnlySpan<float> vector, Span<sbyte> quantized, out float scale)
        {
            if (vector.Length != quantized.Length)
                throw new ArgumentException($"量化维度不匹配: v={vector.Length} q={quantized.Length}");

            var maxAbs = 0f;
            for (var i = 0; i < vector.Length; i++)
                maxAbs = MathF.Max(maxAbs, MathF.Abs(vector[i]));

            if (maxAbs <= 1e-12f)
            {
                scale = 0f;
                quantized.Clear();
                return;
            }

            scale = maxAbs / 127f;
            var inv = 1f / scale;
            for (var i = 0; i < vector.Length; i++)
            {
                var value = (int)MathF.Round(vector[i] * inv);
                value = Math.Clamp(value, -127, 127);
                quantized[i] = (sbyte)value;
            }
        }
    }
}

namespace TM.Framework.Common.Helpers.Storage
{
    public static class StoragePathHelper
    {
        // AsyncLocal for per-request workspace isolation
        private static readonly AsyncLocal<string?> _asyncStorageRoot = new();
        private static readonly AsyncLocal<string?> _asyncProjectName = new();

        // Fallback static values (for non-request contexts like startup)
        private static string _fallbackProjectName = "AgenticNovelStudio";
        private static string _fallbackStorageRoot = Path.Combine(AppContext.BaseDirectory, "App_Data");

        public static event Action<string, string>? CurrentProjectChanged;

        public static string WebStorageRoot
        {
            get => _asyncStorageRoot.Value ?? _fallbackStorageRoot;
            set
            {
                _fallbackStorageRoot = value;
                Directory.CreateDirectory(value);
            }
        }

        public static string CurrentProjectName
        {
            get => _asyncProjectName.Value ?? _fallbackProjectName;
            set
            {
                var trimmed = value?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(CurrentProjectName, trimmed, StringComparison.Ordinal))
                    return;

                var old = CurrentProjectName;
                if (_asyncProjectName.Value != null)
                    _asyncProjectName.Value = trimmed;
                else
                    _fallbackProjectName = trimmed;
                EnsureProjectDirectories();
                CurrentProjectChanged?.Invoke(old, trimmed);
            }
        }

        public static void Configure(string storageRoot, string projectName)
        {
            _fallbackStorageRoot = storageRoot;
            Directory.CreateDirectory(storageRoot);
            CurrentProjectName = projectName;
            EnsureProjectDirectories();
        }

        /// <summary>
        /// Set per-request storage context (called by AgentRuntime after acquiring workspace).
        /// </summary>
        internal static void SetRequestContext(string storageRoot, string projectName)
        {
            _asyncStorageRoot.Value = storageRoot;
            _asyncProjectName.Value = projectName;
            Directory.CreateDirectory(storageRoot);
            EnsureProjectDirectories();
        }

        /// <summary>
        /// Clear per-request storage context (called by AgentRuntime in finally block).
        /// </summary>
        internal static void ClearRequestContext()
        {
            _asyncStorageRoot.Value = null;
            _asyncProjectName.Value = null;
        }

        public static string GetStorageRoot()
        {
            Directory.CreateDirectory(WebStorageRoot);
            return WebStorageRoot;
        }

        public static string GetCurrentProjectPath()
        {
            var path = Path.Combine(WebStorageRoot, "Projects", CurrentProjectName);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetProjectConfigPath() => EnsureDirectory(Path.Combine(GetCurrentProjectPath(), "Config"));

        public static string GetProjectConfigPath(string subPath) =>
            EnsureDirectory(Path.Combine(GetProjectConfigPath(), NormalizeSubPath(subPath)));

        public static string GetProjectChaptersPath() => EnsureDirectory(Path.Combine(GetCurrentProjectPath(), "Chapters"));

        public static string GetProjectHistoryPath() => EnsureDirectory(Path.Combine(GetCurrentProjectPath(), "History"));

        public static string GetProjectValidationPath() => EnsureDirectory(Path.Combine(GetCurrentProjectPath(), "Validation"));

        public static string GetServicesStoragePath(string subPath) =>
            EnsureDirectory(Path.Combine(WebStorageRoot, "Services", NormalizeSubPath(subPath)));

        public static string GetModulesStoragePath(string modulePath) =>
            EnsureDirectory(Path.Combine(WebStorageRoot, "Modules", NormalizeSubPath(modulePath)));

        public static string GetFilePath(string layer, string subPath, string fileName)
        {
            var path = EnsureDirectory(Path.Combine(WebStorageRoot, "Projects", CurrentProjectName, layer, NormalizeSubPath(subPath)));
            return Path.Combine(path, fileName);
        }

        public static void EnsureDirectoryExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
        }

        public static void NotifyModuleDataIsEnabledChanged(string dirPath, bool enabled)
        {
            TM.App.Log($"[StoragePathHelper] module enabled changed: {dirPath} => {enabled}");
        }

        private static void EnsureProjectDirectories()
        {
            _ = GetProjectConfigPath();
            _ = GetProjectChaptersPath();
            _ = GetProjectHistoryPath();
            _ = GetProjectValidationPath();
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string NormalizeSubPath(string subPath)
        {
            return string.IsNullOrWhiteSpace(subPath)
                ? string.Empty
                : subPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}

namespace TM.Framework.Common.Services
{
    public static class ServiceLocator
    {
        // AsyncLocal for per-request service isolation
        private static readonly AsyncLocal<ConcurrentDictionary<Type, object>?> _asyncServices = new();

        // Fallback static dictionary (for non-request contexts)
        private static readonly ConcurrentDictionary<Type, object> _fallbackServices = new();

        private static ConcurrentDictionary<Type, object> Services =>
            _asyncServices.Value ?? _fallbackServices;

        public static bool IsInitialized => !Services.IsEmpty;

        public static void Clear() => Services.Clear();

        public static void Register<T>(T instance) where T : class => Services[typeof(T)] = instance;

        public static void Register(Type type, object instance) => Services[type] = instance;

        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException($"Web ServiceLocator 未注册服务：{typeof(T).FullName}");
        }

        public static T? TryGet<T>() where T : class =>
            Services.TryGetValue(typeof(T), out var service) ? (T)service : null;

        public static object? GetOrDefault(Type type) =>
            Services.TryGetValue(type, out var service) ? service : null;

        /// <summary>
        /// Create a new per-request service container (called by AgentRuntime after acquiring workspace).
        /// </summary>
        internal static void SetRequestContext()
        {
            _asyncServices.Value = new ConcurrentDictionary<Type, object>();
        }

        /// <summary>
        /// Clear per-request service container (called by AgentRuntime in finally block).
        /// </summary>
        internal static void ClearRequestContext()
        {
            _asyncServices.Value = null;
        }
    }
}

namespace System.Threading.Tasks
{
    public static class TaskExtensions
    {
        public static void SafeFireAndForget(this Task task, Action<Exception>? onException = null)
        {
            _ = ObserveAsync(task, onException);
        }

        private static async Task ObserveAsync(Task task, Action<Exception>? onException)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { onException?.Invoke(ex); }
        }
    }
}

namespace TM.Web.NovelAgentWeb.Support
{
    public sealed class WebEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 64;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            var vector = new float[Dimension];
            foreach (var ch in text ?? string.Empty)
                vector[ch % Dimension] += 1f;
            TM.Framework.Common.Helpers.Numerics.VectorMath.L2NormalizeInPlace(vector);
            return Task.FromResult(vector);
        }

        public async Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            var result = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
                result[i] = await EncodeAsync(texts[i], mode, ct).ConfigureAwait(false);
            return result;
        }

        public bool IsModelReady() => true;

        public void ReleaseSession() { }
    }

    public sealed class NovelAgentWorkspace
    {
        public string UserId { get; internal set; } = "default";
        public string ProjectName { get; }
        public string StorageRoot { get; }
        public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
        public StoryBibleService StoryBibleService { get; }
        public CreativeKnowledgeBaseService CreativeKnowledgeBaseService { get; }
        public NovelAgentOrchestrator Orchestrator { get; }

        // Per-workspace service registrations (populated in constructor, applied per-request)
        private readonly List<(Type type, object instance)> _serviceRegistrations = new();

        public NovelAgentWorkspace(IWebHostEnvironment environment, IConfiguration configuration, UserSettingsManager settingsManager)
        {
            ProjectName = configuration["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
            StorageRoot = configuration["NovelAgent:StorageRoot"]
                ?? Path.Combine(environment.ContentRootPath, "App_Data");

            // Do NOT call StoragePathHelper.Configure() here — global state mutation.
            // Context is set per-request via SetRequestContext().

            StoryBibleService = new StoryBibleService();
            CreativeKnowledgeBaseService = new CreativeKnowledgeBaseService();

            var guideManager = new GuideManager();
            var summaryStore = new ChapterSummaryStore();
            var milestoneStore = new ChapterMilestoneStore();
            var factSnapshotExtractor = new FactSnapshotExtractor(guideManager);
            var guideContextService = new GuideContextService(factSnapshotExtractor, summaryStore, milestoneStore);
            var contentChunkSearch = new ContentChunkSearchService();
            var chapterEmbeddingIndex = new ChapterEmbeddingIndex();
            var chunkEmbeddingIndex = new ChunkEmbeddingIndex();
            var embeddingService = new WebEmbeddingService();
            var generationGate = new GenerationGate(
                new LedgerConsistencyChecker(),
                new LedgerRuleSetProvider(),
                new EntityOmissionDetector(guideManager));
            var generatedContentService = new GeneratedContentService();
            var versionTracking = new WebVersionTrackingService();

            RegisterProjectDataServices(
                guideManager,
                summaryStore,
                milestoneStore,
                factSnapshotExtractor,
                guideContextService,
                contentChunkSearch,
                chapterEmbeddingIndex,
                chunkEmbeddingIndex,
                embeddingService,
                generationGate,
                generatedContentService,
                versionTracking);

            var storyStateSnapshotService = new StoryStateSnapshotService(
                guideContextService,
                contentChunkSearch,
                StoryBibleService,
                chapterEmbeddingIndex,
                chunkEmbeddingIndex,
                embeddingService);

            var hardcoreEngine = new HardcoreWritingEngine(
                storyStateSnapshotService,
                guideContextService,
                generationGate,
                generatedContentService,
                contentChunkSearch,
                chapterEmbeddingIndex,
                chunkEmbeddingIndex,
                embeddingService,
                versionTracking,
                settingsManager);

            Orchestrator = new NovelAgentOrchestrator(
                new BookConceptDesigner(new GenreDirectionPlanner()),
                new VolumeArcPlanner(),
                new ChapterNoveltyPlanner(),
                StoryBibleService,
                storyStateSnapshotService,
                new ChapterPostGenerationReviewer(),
                new NovelAgentRewriteLoopService(),
                new CanonMaintenanceService(StoryBibleService),
                new ForeshadowLedgerService(StoryBibleService),
                new CharacterLedgerService(StoryBibleService),
                CreativeKnowledgeBaseService,
                hardcoreEngine);
        }

        private void RegisterProjectDataServices(
            GuideManager guideManager,
            ChapterSummaryStore summaryStore,
            ChapterMilestoneStore milestoneStore,
            FactSnapshotExtractor factSnapshotExtractor,
            GuideContextService guideContextService,
            ContentChunkSearchService contentChunkSearch,
            ChapterEmbeddingIndex chapterEmbeddingIndex,
            ChunkEmbeddingIndex chunkEmbeddingIndex,
            IMicroEmbeddingService embeddingService,
            GenerationGate generationGate,
            GeneratedContentService generatedContentService,
            WebVersionTrackingService versionTracking)
        {
            // Register on the workspace's own list (applied per-request via SetRequestContext)
            Register(guideManager);
            Register(summaryStore);
            Register(milestoneStore);
            Register(new VolumeFactArchiveStore());
            Register(new ChapterKeyEventStore());
            Register(new ChapterChangesWalStore());
            Register(factSnapshotExtractor);
            Register<IGuideContextService>(guideContextService);
            Register(guideContextService);
            Register(contentChunkSearch);
            Register(chapterEmbeddingIndex);
            Register(chunkEmbeddingIndex);
            Register<IChunkEmbeddingIndex>(chunkEmbeddingIndex);
            Register(embeddingService);
            Register(generationGate);
            Register(generatedContentService);
            Register(new KeywordChapterIndexService());
            Register(versionTracking);
            Register(new CharacterStateService(guideManager));
            Register(new ConflictProgressService(guideManager));
            Register(new ForeshadowingStatusService(guideManager));
            Register(new LocationStateService(guideManager));
            Register(new FactionStateService(guideManager));
            Register(new TimelineService(guideManager));
            Register(new ItemStateService(guideManager));
            Register(new SecretRevealService(guideManager));
            Register(new PledgeConstraintService(guideManager));
            Register(new DeadlineConstraintService(guideManager));
            Register(new RelationStrengthService());
            Register(new PlotPointsIndexService());
            Register(new EntityFirstChapterIndex(embeddingService, chunkEmbeddingIndex));
            Register(new LedgerTrimService(guideManager));
        }

        private void Register<T>(T instance) where T : class
        {
            _serviceRegistrations.Add((typeof(T), instance));
        }

        private void Register(Type type, object instance)
        {
            _serviceRegistrations.Add((type, instance));
        }

        /// <summary>
        /// Set per-request context: StoragePathHelper + ServiceLocator for this workspace.
        /// Called by AgentRuntime after acquiring workspace.
        /// </summary>
        internal void SetRequestContext()
        {
            StoragePathHelper.SetRequestContext(StorageRoot, ProjectName);
            ServiceLocator.SetRequestContext();
            foreach (var (type, instance) in _serviceRegistrations)
                ServiceLocator.Register(type, instance);
        }

        /// <summary>
        /// Clear per-request context. Called by AgentRuntime in finally block.
        /// </summary>
        internal void ClearRequestContext()
        {
            StoragePathHelper.ClearRequestContext();
            ServiceLocator.ClearRequestContext();
        }
    }

    public sealed class WebVersionTrackingService
    {
        private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);

        public int IncrementModuleVersion(string moduleName)
        {
            if (!_versions.ContainsKey(moduleName)) _versions[moduleName] = 0;
            _versions[moduleName]++;
            return _versions[moduleName];
        }

        public IReadOnlyList<string> GetDownstreamModules(string moduleName) =>
            TM.Services.Modules.VersionTracking.DependencyConfig.GetDownstreamModules(moduleName);
    }
}
