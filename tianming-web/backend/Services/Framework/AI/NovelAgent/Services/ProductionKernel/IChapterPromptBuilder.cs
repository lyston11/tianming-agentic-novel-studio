using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterPromptBuilder
    {
        string BuildWritingSystemPrompt();

        string BuildChangesOnlyRepairSystemPrompt();

        string BuildWritingUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage);

        string BuildRepairUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report);

        string BuildChangesOnlyRepairUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report);
    }
}
