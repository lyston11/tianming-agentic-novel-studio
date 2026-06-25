using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterChangesRecorder
    {
        Task RecordAsync(
            NovelAgentRun run,
            string chapterId,
            ChapterChanges changes,
            string? changesJson,
            CancellationToken cancellationToken = default);
    }
}
