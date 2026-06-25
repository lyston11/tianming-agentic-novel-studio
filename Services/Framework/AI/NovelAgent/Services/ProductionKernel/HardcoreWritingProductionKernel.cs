using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class HardcoreWritingProductionKernel : ITianmingProductionKernel
    {
        private readonly HardcoreWritingEngine _engine;

        public HardcoreWritingProductionKernel(HardcoreWritingEngine engine)
        {
            _engine = engine;
        }

        public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default) =>
            _engine.BuildContextPackageAsync(run, document, ct);

        public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default) =>
            _engine.GenerateDraftWithChangesAsync(run, package, ct);

        public Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            _engine.ValidateDraftAsync(run, package, draft, ct);

        public Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default) =>
            _engine.RepairDraftAsync(run, package, draft, gate, ct);

        public Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default) =>
            _engine.CommitChapterAsync(run, package, draft, ct);

        public Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default) =>
            _engine.AuditCommittedChapterAsync(chapterId, committedContent, contextPackage, ct);

        public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default) =>
            _engine.ReviseCommittedChapterAsync(chapterId, committedContent, contextPackage, revisionGoal, ct);

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft) =>
            _engine.RefreshIndexesAndAnalyzeImpact(run, draft);

        public string StripChanges(string content) =>
            _engine.StripChanges(content);
    }
}
