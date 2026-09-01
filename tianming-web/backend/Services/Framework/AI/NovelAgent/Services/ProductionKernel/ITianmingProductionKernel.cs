using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface ITianmingProductionKernel
    {
        Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default);

        Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct = default);

        Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default);

        Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            CancellationToken ct = default);

        Task<DependencyImpactReport> CommitChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            ChapterDraftArtifact draft,
            CancellationToken ct = default);

        Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default);

        Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default);

        DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft);

        string StripChanges(string content);
    }
}
