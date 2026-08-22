using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterGatekeeper
    {
        void ApplyHardGates(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft);
    }
}
