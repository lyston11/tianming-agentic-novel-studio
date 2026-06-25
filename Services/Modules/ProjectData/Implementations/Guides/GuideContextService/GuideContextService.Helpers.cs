using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region Helpers

        public async Task<ContentGuide> GetContentGuideAsync()
        {
            ContentGuide? cached;
            lock (_contentGuideCacheLock)
            {
                cached = _contentGuideCache;
            }
            if (cached != null) return cached;

            await _contentGuideLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_contentGuideCacheLock) { cached = _contentGuideCache; }
                if (cached != null) return cached;

                var epoch = Volatile.Read(ref _cacheEpoch);

                var merged = await LoadGuideAsync<ContentGuide>(GuideRuntimeDataKeys.ContentGuide).ConfigureAwait(false);

                if (!IsCacheEpochCurrent(epoch))
                    return new ContentGuide();

                lock (_contentGuideCacheLock)
                {
                    if (!IsCacheEpochCurrent(epoch))
                        return new ContentGuide();
                    _contentGuideCache ??= merged;
                    return _contentGuideCache;
                }
            }
            finally
            {
                _contentGuideLoadLock.Release();
            }
        }

        public void InvalidateContentGuideCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            lock (_contentGuideCacheLock)
            {
                _contentGuideCache = null;
            }
        }

        public async Task<T> LoadGuideAsync<T>(string guideKey) where T : new()
        {
            if (_runtimeDataSource == null)
            {
                TM.App.Log($"[GuideContextService] 生产指导数据源未配置: {guideKey}");
                return new T();
            }

            try
            {
                return await _runtimeDataSource.LoadGuideAsync<T>(guideKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载生产指导数据失败 [{guideKey}]: {ex.Message}");
                return new T();
            }
        }

        private async Task<List<T>> LoadPackagedAsync<T>(string dataKey)
        {
            if (_runtimeDataSource == null)
            {
                TM.App.Log($"[GuideContextService] 生产打包数据源未配置: {dataKey}");
                return new List<T>();
            }

            try
            {
                return (await _runtimeDataSource.LoadItemsAsync<T>(dataKey).ConfigureAwait(false)).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载生产打包数据失败 [{dataKey}]: {ex.Message}");
                return new List<T>();
            }
        }

        private bool IsCacheEpochCurrent(int epoch)
        {
            return epoch == Volatile.Read(ref _cacheEpoch);
        }

        public async Task<string> GetChapterSummaryAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return string.Empty;

            return await _summaryStore.GetSummaryAsync(chapterId).ConfigureAwait(false);
        }

        private async Task<string> LoadChapterContentAsync(string chapterId)
        {
            try
            {
                var chapterCatalog = ServiceLocator.TryGet<IChapterCatalogService>();
                if (chapterCatalog == null)
                    return string.Empty;

                return await chapterCatalog.GetChapterContentAsync(chapterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载章节内容失败 [{chapterId}]: {ex.Message}");
                return string.Empty;
            }
        }

        #endregion
    }
}
