using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Implementations.Indexing;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public sealed class EntityRebuildSubscription
    {
        private readonly GuideManager _guideManager;
        private readonly EntityFirstChapterIndex _firstChapterIndex;

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _entityLocks
            = new(StringComparer.OrdinalIgnoreCase);

        public EntityRebuildSubscription(GuideManager guideManager, EntityFirstChapterIndex firstChapterIndex)
        {
            _guideManager = guideManager;
            _firstChapterIndex = firstChapterIndex;
            _guideManager.EntryChanged += OnEntryChanged;
        }

        private void OnEntryChanged(string entityId, string entityName, string entityDescription)
        {
            if (string.IsNullOrEmpty(entityId)) return;

            _ = Task.Run(async () =>
            {
                var sem = _entityLocks.GetOrAdd(entityId, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    await _firstChapterIndex.LoadAsync().ConfigureAwait(false);

                    if (!_firstChapterIndex.Contains(entityId))
                    {
                        return;
                    }
                    var ok = await _firstChapterIndex.RebuildAsync(entityId, entityName, entityDescription).ConfigureAwait(false);
                    await _firstChapterIndex.SaveAsync().ConfigureAwait(false);
                    TM.App.Log($"[EntityRebuildSubscription] {entityId} Rebuild done (ok={ok})");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityRebuildSubscription] {entityId} Rebuild 异常（已吞）: {ex.Message}");
                }
                finally
                {
                    sem.Release();
                }
            });
        }
    }
}
