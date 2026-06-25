using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;

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

namespace TM.Framework.Common.Services
{
    public static class ServiceLocator
    {
        // AsyncLocal for per-request service isolation
        private static readonly AsyncLocal<ConcurrentDictionary<Type, object>?> _asyncServices = new();

        private static ConcurrentDictionary<Type, object> RequiredServices =>
            _asyncServices.Value
            ?? throw new InvalidOperationException("Web ServiceLocator 只能在 workspace request context 内使用。");

        public static bool IsInitialized => _asyncServices.Value is { IsEmpty: false };

        public static void Clear() => _asyncServices.Value?.Clear();

        public static void Register<T>(T instance) where T : class => RequiredServices[typeof(T)] = instance;

        public static void Register(Type type, object instance) => RequiredServices[type] = instance;

        public static T Get<T>() where T : class
        {
            if (RequiredServices.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException($"Web ServiceLocator 未注册服务：{typeof(T).FullName}");
        }

        public static T? TryGet<T>() where T : class =>
            _asyncServices.Value is { } services && services.TryGetValue(typeof(T), out var service) ? (T)service : null;

        public static object? GetOrDefault(Type type) =>
            _asyncServices.Value is { } services && services.TryGetValue(type, out var service) ? service : null;

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
    public sealed class NovelAgentWorkspace
    {
        public string UserId { get; internal set; } = "default";
        public string ProjectId { get; }
        public string ProjectName { get; }
        public string StorageRoot { get; }
        public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
        public StoryBibleService StoryBibleService { get; }
        public CreativeKnowledgeBaseService CreativeKnowledgeBaseService { get; }
        public NovelAgentOrchestrator Orchestrator { get; }
        internal IServiceScopeFactory ScopeFactory { get; }

        // Per-workspace service registrations (populated in constructor, applied per-request)
        private readonly List<(Type type, object instance)> _serviceRegistrations = new();

        public NovelAgentWorkspace(
            IWebHostEnvironment environment,
            IConfiguration configuration,
            UserSettingsManager settingsManager,
            IWorkspaceProductionRuntimeBuilder productionRuntimeBuilder,
            IServiceScopeFactory scopeFactory,
            string userId = "default",
            string projectId = "",
            IVectorStore? vectorStore = null,
            IMicroEmbeddingService? embeddingService = null,
            ICurrentUserService? currentUserService = null,
            IAgentMemoryRepository? memoryRepository = null,
            IUnifiedValidationService? unifiedValidationService = null)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ScopeFactory = scopeFactory;
            UserId = string.IsNullOrWhiteSpace(userId) ? "default" : userId;
            ProjectId = projectId ?? string.Empty;
            ProjectName = configuration["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
            var baseStorageRoot = configuration["NovelAgent:StorageRoot"]
                ?? Path.Combine(environment.ContentRootPath, "App_Data");

            // User-isolated storage paths
            StorageRoot = !string.IsNullOrWhiteSpace(UserId) && UserId != "default"
                ? Path.Combine(baseStorageRoot, "Users", UserId)
                : Path.Combine(baseStorageRoot, "System");

            Directory.CreateDirectory(StorageRoot);

            StoryBibleService = new StoryBibleService(new WebStoryBibleDocumentStore(scopeFactory, UserId, ProjectId));
            CreativeKnowledgeBaseService = new CreativeKnowledgeBaseService(
                vectorStore,
                embeddingService,
                currentUserService,
                memoryRepository,
                ProjectId);

            var generatedContentService = new WebGeneratedContentService(scopeFactory, currentUserService, ProjectId);
            var validationService = unifiedValidationService
                ?? new ProductionUnifiedValidationService(scopeFactory, generatedContentService, UserId, ProjectId);
            var productionRuntime = productionRuntimeBuilder.Build(new WorkspaceProductionRuntimeRequest(
                UserId,
                ProjectId,
                StoryBibleService,
                CreativeKnowledgeBaseService,
                settingsManager,
                vectorStore,
                embeddingService,
                currentUserService,
                memoryRepository,
                scopeFactory,
                validationService,
                GeneratedContentService: generatedContentService));
            Orchestrator = productionRuntime.Orchestrator;
            _serviceRegistrations.AddRange(
                productionRuntime.ServiceRegistrations.Select(item => (item.Type, item.Instance)));
        }

        /// <summary>
        /// Set per-request service context for this workspace.
        /// Called by AgentRuntime after acquiring workspace.
        /// </summary>
        internal void SetRequestContext()
        {
            ServiceLocator.SetRequestContext();
            foreach (var (type, instance) in _serviceRegistrations)
                ServiceLocator.Register(type, instance);
        }

        /// <summary>
        /// Clear per-request context. Called by AgentRuntime in finally block.
        /// </summary>
        internal void ClearRequestContext()
        {
            ServiceLocator.ClearRequestContext();
        }
    }

}
