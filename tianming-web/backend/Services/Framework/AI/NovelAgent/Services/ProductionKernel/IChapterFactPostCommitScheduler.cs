using System;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public interface IChapterFactPostCommitScheduler
    {
        Task ScheduleAsync(
            ChapterFactWriteRequest request,
            Action<ChapterFactWriteResult>? onCompleted = null,
            Action<Exception>? onFailed = null,
            CancellationToken ct = default);
    }

    public sealed class InlineChapterFactPostCommitScheduler : IChapterFactPostCommitScheduler
    {
        private readonly IChapterFactWriter _writer;

        public InlineChapterFactPostCommitScheduler(IChapterFactWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public async Task ScheduleAsync(
            ChapterFactWriteRequest request,
            Action<ChapterFactWriteResult>? onCompleted = null,
            Action<Exception>? onFailed = null,
            CancellationToken ct = default)
        {
            try
            {
                var result = await _writer.ExtractAndPersistAsync(request, ct)
                    .ConfigureAwait(false);
                onCompleted?.Invoke(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onFailed?.Invoke(ex);
            }
        }
    }
}
