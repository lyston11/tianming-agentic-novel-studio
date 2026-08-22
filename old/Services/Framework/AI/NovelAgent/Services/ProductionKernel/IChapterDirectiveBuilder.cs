using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterDirectiveBuilder
    {
        ChapterDirective Build(NovelAgentRun run, ChapterContextPackageSummary contextPackage);
    }
}
