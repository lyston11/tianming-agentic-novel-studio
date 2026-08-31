using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterPackageBuilder
    {
        ChapterContextPackageSummary Build(ChapterPackageBuildRequest request);
    }

    public sealed class ChapterPackageBuildRequest
    {
        public NovelAgentRun Run { get; set; } = new();

        public StoryBibleDocument Document { get; set; } = new();

        public StoryStateSnapshot StoryState { get; set; } = new();

        public ContentTaskContext? ContentContext { get; set; }
    }
}
