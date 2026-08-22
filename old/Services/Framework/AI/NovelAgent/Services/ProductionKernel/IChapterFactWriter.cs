using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterFactWriter
    {
        Task<ChapterFactWriteResult> ExtractAndPersistAsync(
            ChapterFactWriteRequest request,
            CancellationToken ct = default);
    }

    public sealed class ChapterFactWriteRequest
    {
        public NovelAgentRun Run { get; set; } = new();

        public ChapterContextPackageSummary ContextPackage { get; set; } = new();

        public string CommittedContent { get; set; } = string.Empty;

        public Func<string, string, CancellationToken, Task<string>>? CompleteAsync { get; set; }

        public Func<ChapterContinuityFacts, CancellationToken, Task<StoryBibleCommitResult>>? PersistAsync { get; set; }
    }

    public sealed class ChapterFactWriteResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public ChapterContinuityFacts? Facts { get; set; }

        public StoryBibleCommitResult? CommitResult { get; set; }
    }
}
