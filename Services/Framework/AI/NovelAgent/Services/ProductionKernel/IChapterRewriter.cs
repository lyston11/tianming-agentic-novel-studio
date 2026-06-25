using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterRewriter
    {
        Task<ChapterDraftArtifact> RepairAsync(
            ChapterRewriteRequest request,
            CancellationToken ct = default);
    }

    public sealed class ChapterRewriteRequest
    {
        public NovelAgentRun Run { get; set; } = new();

        public ChapterContextPackageSummary ContextPackage { get; set; } = new();

        public ChapterDraftArtifact Draft { get; set; } = new();

        public GenerationGateReport GateReport { get; set; } = new();

        public Func<string, string, CancellationToken, Task<string>>? CompleteAsync { get; set; }
    }
}
