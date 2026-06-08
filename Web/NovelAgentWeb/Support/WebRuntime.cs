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
        private static string _currentProjectName = "AgenticNovelStudio";

        public static event Action<string, string>? CurrentProjectChanged;

        public static string WebStorageRoot { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "App_Data");

        public static string CurrentProjectName
        {
            get => _currentProjectName;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(_currentProjectName, value, StringComparison.Ordinal))
                    return;

                var old = _currentProjectName;
                _currentProjectName = value.Trim();
                EnsureProjectDirectories();
                CurrentProjectChanged?.Invoke(old, _currentProjectName);
            }
        }

        public static void Configure(string storageRoot, string projectName)
        {
            WebStorageRoot = storageRoot;
            Directory.CreateDirectory(WebStorageRoot);
            CurrentProjectName = projectName;
            EnsureProjectDirectories();
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
        private static readonly ConcurrentDictionary<Type, object> Services = new();

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
        public string ProjectName { get; }
        public string StorageRoot { get; }
        public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
        public StoryBibleService StoryBibleService { get; }
        public CreativeKnowledgeBaseService CreativeKnowledgeBaseService { get; }
        public NovelAgentOrchestrator Orchestrator { get; }

        public NovelAgentWorkspace(IWebHostEnvironment environment, IConfiguration configuration, UserSettingsManager settingsManager)
        {
            ProjectName = configuration["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
            StorageRoot = configuration["NovelAgent:StorageRoot"]
                ?? Path.Combine(environment.ContentRootPath, "App_Data");

            StoragePathHelper.Configure(StorageRoot, ProjectName);

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

        private static void RegisterProjectDataServices(
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
            ServiceLocator.Clear();
            ServiceLocator.Register(guideManager);
            ServiceLocator.Register(summaryStore);
            ServiceLocator.Register(milestoneStore);
            ServiceLocator.Register(new VolumeFactArchiveStore());
            ServiceLocator.Register(new ChapterKeyEventStore());
            ServiceLocator.Register(new ChapterChangesWalStore());
            ServiceLocator.Register(factSnapshotExtractor);
            ServiceLocator.Register<IGuideContextService>(guideContextService);
            ServiceLocator.Register(guideContextService);
            ServiceLocator.Register(contentChunkSearch);
            ServiceLocator.Register(chapterEmbeddingIndex);
            ServiceLocator.Register(chunkEmbeddingIndex);
            ServiceLocator.Register<IChunkEmbeddingIndex>(chunkEmbeddingIndex);
            ServiceLocator.Register(embeddingService);
            ServiceLocator.Register(generationGate);
            ServiceLocator.Register(generatedContentService);
            ServiceLocator.Register(new KeywordChapterIndexService());
            ServiceLocator.Register(versionTracking);
            ServiceLocator.Register(new CharacterStateService(guideManager));
            ServiceLocator.Register(new ConflictProgressService(guideManager));
            ServiceLocator.Register(new ForeshadowingStatusService(guideManager));
            ServiceLocator.Register(new LocationStateService(guideManager));
            ServiceLocator.Register(new FactionStateService(guideManager));
            ServiceLocator.Register(new TimelineService(guideManager));
            ServiceLocator.Register(new ItemStateService(guideManager));
            ServiceLocator.Register(new SecretRevealService(guideManager));
            ServiceLocator.Register(new PledgeConstraintService(guideManager));
            ServiceLocator.Register(new DeadlineConstraintService(guideManager));
            ServiceLocator.Register(new RelationStrengthService());
            ServiceLocator.Register(new PlotPointsIndexService());
            ServiceLocator.Register(new EntityFirstChapterIndex(embeddingService, chunkEmbeddingIndex));
            ServiceLocator.Register(new LedgerTrimService(guideManager));
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
