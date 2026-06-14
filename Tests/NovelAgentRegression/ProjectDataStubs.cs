using TM.Services.Framework.AI.Embedding;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    // Note: This stub ContentTaskContext uses FactSnapshot from Tracking namespace (via alias below)
    // to match the real implementation
    using FactSnapshot = TM.Services.Modules.ProjectData.Models.Tracking.FactSnapshot;
    public sealed class ContentTaskContext
    {
        public string ChapterId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public ChapterPlanStub? ChapterPlan { get; set; }
        public string PreviousChapterId { get; set; } = string.Empty;
        public string PreviousChapterSummary { get; set; } = string.Empty;
        public List<ChapterSummaryEntry> PreviousChapterSummaries { get; set; } = new();
        public List<ChapterSummaryEntry> MdPreviousChapterSummaries { get; set; } = new();
        public FactSnapshot? FactSnapshot { get; set; }
        public List<WorldRuleStub> WorldRules { get; set; } = new();
        public List<EntityStub> Characters { get; set; } = new();
        public List<EntityStub> Factions { get; set; } = new();
        public List<EntityStub> Locations { get; set; } = new();
        public List<EntityStub> PlotRules { get; set; } = new();
        public List<BlueprintStub> Blueprints { get; set; } = new();
        public List<SceneStub> Scenes { get; set; } = new();
        public List<LongDistanceRecallFragment> LongDistanceRecallFragments { get; set; } = new();
        public List<string> StateDivergenceWarnings { get; set; } = new();
        public TM.Services.Modules.ProjectData.Models.Guides.ContextIdCollection ContextIds { get; set; } = new();
    }

    public sealed class ChapterPlanStub
    {
        public string ChapterTitle { get; set; } = string.Empty;
        public string MainGoal { get; set; } = string.Empty;
        public string KeyTurn { get; set; } = string.Empty;
        public string Hook { get; set; } = string.Empty;
        public string ReaderExperienceGoal { get; set; } = string.Empty;
    }

    public sealed class ChapterSummaryEntry
    {
        public string ChapterId { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    // Note: FactSnapshot and related snapshot classes are imported from Tracking namespace via alias
    // They are not defined here to avoid conflicts with the real implementation

    public sealed class WorldRuleStub
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string OneLineSummary { get; set; } = string.Empty;
        public string HardRules { get; set; } = string.Empty;
        public string PowerSystem { get; set; } = string.Empty;
        public string GetCoreSummary() => OneLineSummary;
    }

    public sealed class BlueprintStub
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string OneLineStructure { get; set; } = string.Empty;
        public string SceneTitle { get; set; } = string.Empty;
        public string Turning { get; set; } = string.Empty;
        public string GetCoreSummary() => OneLineStructure;
    }

    public sealed class EntityStub
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string GetCoreSummary() => Summary;
    }

    public sealed class SceneStub
    {
        public string Title { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string Opening { get; set; } = string.Empty;
        public string Development { get; set; } = string.Empty;
        public string Turning { get; set; } = string.Empty;
        public string Ending { get; set; } = string.Empty;
    }

    public sealed class LongDistanceRecallFragment
    {
        public string ChapterId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
        public string? Category { get; set; }
    }
}

namespace TM.Services.Modules.ProjectData.Interfaces
{
    using TM.Services.Modules.ProjectData.Models.TaskContexts;
    using TM.Services.Modules.ProjectData.Implementations.Generation;

    public interface IGuideContextService
    {
        Task<ContentTaskContext?> BuildContentContextAsync(string chapterId, CancellationToken ct = default);
    }

    public record VectorSearchHit(string Key, float Score);

    public sealed record ContentChunkHit(string ChapterId, int Position, string Content, double Score);

    public interface IContentChunkSearchService
    {
        Task<List<ContentChunkHit>> SearchAsync(string query, int topK = 5);
        Task<List<ContentChunkHit>> SearchByChapterAsync(string chapterId, int topK = 2);
        Task InvalidateChapterAsync(string chapterId);
        Task<List<ContentChunkHit>> SearchByChapterPositionAsync(
            string chapterId,
            int startPosition,
            int windowSize = 1,
            CancellationToken ct = default);
        Task<IReadOnlyList<ContentChunkHit>> GetChunksAsync(string chapterId, CancellationToken ct = default);
        void InvalidateCache();
    }

    public interface IVectorIndex
    {
        int Count { get; }
        Task<bool> UpsertAsync(string key, float[] vector, CancellationToken ct = default) => Task.FromResult(true);
        Task<bool> UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default) => Task.FromResult(true);
        Task<bool> RemoveAsync(string key, CancellationToken ct = default) => Task.FromResult(true);
        Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default);
        Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        IReadOnlyCollection<string> GetAllKeys() => Array.Empty<string>();
        Task LoadAsync(CancellationToken ct = default);
        Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        void InvalidateCache() { }
    }

    public static class ChunkKey
    {
        public static string Format(string chapterId, int position) => $"{chapterId}#{position}";

        public static bool TryParse(string key, out string chapterId, out int position)
        {
            chapterId = string.Empty;
            position = -1;
            if (string.IsNullOrEmpty(key)) return false;
            var idx = key.LastIndexOf('#');
            if (idx <= 0 || idx == key.Length - 1) return false;
            if (!int.TryParse(key.AsSpan(idx + 1), out position)) return false;
            chapterId = key[..idx];
            return true;
        }
    }

    public interface IChunkEmbeddingIndex : IVectorIndex
    {
        Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
            float[] queryVector,
            IReadOnlySet<string> chapterIds,
            int topK,
            CancellationToken ct = default);
        Task RemoveByChapterAsync(string chapterId, CancellationToken ct = default) => Task.CompletedTask;
        Task UpsertBatchAsync(IReadOnlyList<(string Key, float[] Vector)> items, CancellationToken ct = default) => Task.CompletedTask;
        Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public interface IGeneratedContentService
    {
        Task SaveChapterAsync(string chapterId, string content);
    }
}

namespace TM.Services.Framework.AI.Embedding
{
    public enum EmbeddingMode
    {
        Query = 0,
        Passage = 1
    }

    public interface IMicroEmbeddingService
    {
        Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default);
        Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default);
        void ReleaseSession();
        bool IsModelReady();
        int Dimension { get; }
    }
}

namespace TM.Services.Modules.ProjectData.Implementations
{
    using TM.Services.Modules.ProjectData.Interfaces;

    public class ContentChunkSearchService : IContentChunkSearchService
    {
        private readonly Dictionary<(string ChapterId, int Position), string> _chunks = new();

        public void AddChunk(string chapterId, int position, string content)
        {
            _chunks[(chapterId, position)] = content;
        }

        public Task<List<ContentChunkHit>> SearchAsync(string query, int topK = 5)
        {
            var hits = _chunks
                .Where(kv => kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => kv.Value.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Take(topK)
                .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task<List<ContentChunkHit>> SearchByChapterAsync(string chapterId, int topK = 2)
        {
            var hits = _chunks
                .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key.Position)
                .Take(topK)
                .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task InvalidateChapterAsync(string chapterId) => Task.CompletedTask;

        public Task<List<ContentChunkHit>> SearchByChapterPositionAsync(
            string chapterId,
            int startPosition,
            int windowSize = 1,
            CancellationToken ct = default)
        {
            var hits = _chunks
                .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
                    && kv.Key.Position >= startPosition)
                .OrderBy(kv => kv.Key.Position)
                .Take(windowSize)
                .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task<IReadOnlyList<ContentChunkHit>> GetChunksAsync(string chapterId, CancellationToken ct = default)
        {
            IReadOnlyList<ContentChunkHit> hits = _chunks
                .Where(kv => string.Equals(kv.Key.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key.Position)
                .Select(kv => new ContentChunkHit(kv.Key.ChapterId, kv.Key.Position, kv.Value, 1))
                .ToList();
            return Task.FromResult(hits);
        }

        public void InvalidateCache()
        {
        }
    }
}

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public sealed class ContextIdCollection
    {
    }
}

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    // Commented out stub to avoid type conflicts with real implementation
    // The test project references the actual ChapterChanges class from the main codebase
    /*
    public sealed class ChapterChanges
    {
        public const string ChangesSeparator = "---CHANGES---";
        public static IReadOnlyList<string> TopLevelFieldNames { get; } = new[] { "characters", "conflicts", "foreshadowing", "worldRules" };
    }
    */

    public sealed class DesignElementNames
    {
        public List<string> CharacterNames { get; set; } = new();
        public List<string> FactionNames { get; set; } = new();
        public List<string> LocationNames { get; set; } = new();
        public List<string> PlotKeyNames { get; set; } = new();
        public List<string> PovCharacterNames { get; set; } = new();
    }
}

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    using TM.Services.Modules.ProjectData.Models.Guides;
    using TM.Services.Modules.ProjectData.Models.TaskContexts;
    using TM.Services.Modules.ProjectData.Models.Tracking;
    // Use types from Tracking namespace to match HardcoreWritingEngine expectations
    using GateResult = TM.Services.Modules.ProjectData.Models.Tracking.GateResult;
    using GateFailure = TM.Services.Modules.ProjectData.Models.Tracking.GateFailure;
    using FailureType = TM.Services.Modules.ProjectData.Models.Tracking.FailureType;

    // FailureType, GateFailure, and GateResult are aliased to Tracking namespace types above
    // The stub definitions are commented out to avoid conflicts
    /*
    public enum FailureType
    {
        Protocol,
        Entity,
        Fact,
        Blueprint
    }

    public sealed class GateFailure
    {
        public FailureType Type { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class GateResult
    {
        public bool Success { get; set; }
        public ChapterChanges? ParsedChanges { get; set; }
        public string ContentWithoutChanges { get; set; } = string.Empty;
        public List<GateFailure> Failures { get; set; } = new();
        public List<string> GetHumanReadableFailures(int max) => Failures.Select(f => f.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Take(max).ToList();
        public List<string> GetAllFailures() => GetHumanReadableFailures(100);
    }
    */

    public sealed class GenerationGate
    {
        public static bool HasChangesRegion(string content) =>
            content?.Contains(ChapterChanges.ChangesSeparator, StringComparison.Ordinal) == true;

        public Task<GateResult> ValidateAsync(
            string chapterId,
            string content,
            TM.Services.Modules.ProjectData.Models.Tracking.FactSnapshot snapshot,
            DesignElementNames design,
            ContextIdCollection contextIds)
        {
            var success = HasChangesRegion(content);
            return Task.FromResult(new GateResult
            {
                Success = success,
                ParsedChanges = success ? new ChapterChanges() : null,
                ContentWithoutChanges = content ?? string.Empty,
                Failures = success ? new List<GateFailure>() : new List<GateFailure>
                {
                    new GateFailure { Type = FailureType.Protocol, Errors = new List<string> { "缺少 CHANGES 区域。" } }
                }
            });
        }
    }
}

namespace TM.Services.Modules.ProjectData.Implementations
{
    using TM.Services.Modules.ProjectData.Models.Guides;
    using TM.Services.Modules.ProjectData.Models.TaskContexts;
    using TM.Services.Modules.ProjectData.Models.Tracking;
    // Use types from Tracking namespace to match HardcoreWritingEngine expectations
    using GateResult = TM.Services.Modules.ProjectData.Models.Tracking.GateResult;
    using GateFailure = TM.Services.Modules.ProjectData.Models.Tracking.GateFailure;
    using FailureType = TM.Services.Modules.ProjectData.Models.Tracking.FailureType;

    // FailureType, GateFailure, and GateResult are aliased to Tracking namespace types above
    // The stub definitions are commented out to avoid conflicts
    /*
    public enum FailureType
    {
        Protocol,
        Entity,
        Fact,
        Blueprint
    }

    public sealed class GateFailure
    {
        public FailureType Type { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class GateResult
    {
        public bool Success { get; set; }
        public ChapterChanges? ParsedChanges { get; set; }
        public string ContentWithoutChanges { get; set; } = string.Empty;
        public List<GateFailure> Failures { get; set; } = new();
        public List<string> GetHumanReadableFailures(int max) => Failures.Select(f => f.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Take(max).ToList();
        public List<string> GetAllFailures() => GetHumanReadableFailures(100);
    }
    */

    public sealed class GenerationGate
    {
        public static bool HasChangesRegion(string content) =>
            content?.Contains(ChapterChanges.ChangesSeparator, StringComparison.Ordinal) == true;

        public Task<GateResult> ValidateAsync(
            string chapterId,
            string content,
            TM.Services.Modules.ProjectData.Models.Tracking.FactSnapshot snapshot,
            DesignElementNames design,
            ContextIdCollection contextIds)
        {
            var success = HasChangesRegion(content);
            return Task.FromResult(new GateResult
            {
                Success = success,
                ParsedChanges = success ? new ChapterChanges() : null,
                ContentWithoutChanges = content ?? string.Empty,
                Failures = success ? new List<GateFailure>() : new List<GateFailure>
                {
                    new GateFailure { Type = FailureType.Protocol, Errors = new List<string> { "缺少 CHANGES 区域。" } }
                }
            });
        }
    }
}

namespace TM.Services.Modules.ProjectData.Implementations.Guides
{
    using TM.Services.Modules.ProjectData.Models.Tracking;

    public sealed class ChapterSummaryStore
    {
        public Task SetSummaryAsync(string chapterId, string summary) => Task.CompletedTask;
    }

    public sealed class ChapterChangesWalStore
    {
        public Task WriteAsync(string chapterId, ChapterChanges changes) => Task.CompletedTask;
    }
}

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    using TM.Services.Modules.ProjectData.Interfaces;
    using TM.Services.Modules.ProjectData.Models.Tracking;

    public class ChapterEmbeddingIndex : IVectorIndex
    {
        private readonly List<VectorSearchHit> _hits = new();

        public int Count => _hits.Count;

        public void AddHit(string chapterId, float score)
        {
            _hits.Add(new VectorSearchHit(chapterId, score));
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(_hits
                .OrderByDescending(h => h.Score)
                .Take(topK)
                .ToList());
        }

        public Task<bool> UpsertAsync(string chapterId, float[] vector, CancellationToken ct = default)
        {
            AddHit(chapterId, vector.Length);
            return Task.FromResult(true);
        }

        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public class FakeChunkEmbeddingIndex : IChunkEmbeddingIndex
    {
        private readonly List<VectorSearchHit> _hits = new();

        public int Count => _hits.Count;

        public void AddHit(string chapterId, int position, float score)
        {
            _hits.Add(new VectorSearchHit(ChunkKey.Format(chapterId, position), score));
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(_hits
                .OrderByDescending(h => h.Score)
                .Take(topK)
                .ToList());
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
            float[] queryVector,
            IReadOnlySet<string> chapterIds,
            int topK,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(_hits
                .Where(h => ChunkKey.TryParse(h.Key, out var chapterId, out _) && chapterIds.Contains(chapterId))
                .OrderByDescending(h => h.Score)
                .Take(topK)
                .ToList());
        }

        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public sealed class KeywordChapterIndexService
    {
        public Task IndexChapterAsync(string chapterId, ChapterChanges changes) => Task.CompletedTask;
    }
}
