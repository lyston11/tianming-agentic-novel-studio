using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

                var guidesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
                var shardFiles = Directory.Exists(guidesDir)
                    ? Directory.GetFiles(guidesDir, "content_guide_vol*.json")
                        .Select(f =>
                        {
                            var stem = Path.GetFileNameWithoutExtension(f);
                            const string prefix = "content_guide_vol";
                            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (Path: f, Vol: -1);
                            var suffix = stem.Substring(prefix.Length);
                            return int.TryParse(suffix, out var num) && num > 0 ? (Path: f, Vol: num) : (Path: f, Vol: -1);
                        })
                        .Where(x => x.Vol > 0)
                        .OrderBy(x => x.Vol)
                        .Select(x => x.Path)
                        .ToArray()
                    : Array.Empty<string>();

                ContentGuide merged;
                if (shardFiles.Length > 0)
                {
                    merged = new ContentGuide();
                    var shardTasks = shardFiles.Select(async sf =>
                    {
                        try
                        {
                            await using var sfStream = File.OpenRead(sf);
                            return await JsonSerializer.DeserializeAsync<ContentGuide>(sfStream, JsonOptions).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[GuideContextService] 加载分片失败 {Path.GetFileName(sf)}: {ex.Message}");
                            return null;
                        }
                    });
                    var shards = await Task.WhenAll(shardTasks).ConfigureAwait(false);
                    if (!IsCacheEpochCurrent(epoch))
                        return new ContentGuide();
                    foreach (var shard in shards)
                    {
                        if (shard == null) continue;
                        foreach (var (k, v) in shard.Chapters)
                            merged.Chapters[k] = v;
                        foreach (var (k, v) in shard.ChapterSummaries)
                            merged.ChapterSummaries[k] = v;
                    }

                    TM.App.Log($"[GuideContextService] content_guide 聚合 {shardFiles.Length} 个分片，共 {merged.Chapters.Count} 章");
                }
                else
                {
                    merged = await LoadGuideAsync<ContentGuide>("content_guide.json").ConfigureAwait(false);
                }

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

        public async Task<T> LoadGuideAsync<T>(string fileName) where T : new()
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", fileName);

            if (!File.Exists(guidesPath))
            {
                TM.App.Log($"[GuideContextService] 指导文件不存在: {fileName}");
                return new T();
            }

            try
            {
                T guide;
                await using (var fs = new FileStream(guidesPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, useAsync: true))
                {
                    var sgInfo = GuideSerializerContext.Default.GetTypeInfo(typeof(T)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>;
                    guide = (sgInfo != null
                        ? await JsonSerializer.DeserializeAsync(fs, sgInfo).ConfigureAwait(false)
                        : await JsonSerializer.DeserializeAsync<T>(fs, JsonOptions).ConfigureAwait(false)) ?? new T();
                }

                return guide;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载指导文件失败 [{fileName}]: {ex.Message}");
                return new T();
            }
        }

        private async Task<List<T>> LoadPackagedAsync<T>(string relativePath, string dataKey)
        {
            var filePath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), relativePath);
            var items = new List<T>();

            if (!File.Exists(filePath))
            {
                TM.App.Log($"[GuideContextService] 打包文件不存在: {relativePath}");
                return items;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    if (!dataProp.TryGetProperty(dataKey, out var keyProp))
                    {
                        if (dataKey.Length > 0 && (dataKey[^1] == 's' || dataKey[^1] == 'S'))
                        {
                            var alt = dataKey.TrimEnd('s');
                            dataProp.TryGetProperty(alt, out keyProp);
                        }
                        else
                        {
                            var alt = dataKey + "s";
                            dataProp.TryGetProperty(alt, out keyProp);
                        }
                    }

                    if (keyProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var fileProp in keyProp.EnumerateObject())
                        {
                            if (string.Equals(fileProp.Name, "categories", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (fileProp.Value.ValueKind == JsonValueKind.Array)
                            {
                                var arrayJson = fileProp.Value.GetRawText();
                                var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                                if (arrayItems != null) items.AddRange(arrayItems);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载打包数据失败 [{relativePath}]: {ex.Message}");
            }

            return items;
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
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");

            if (!File.Exists(chapterFile))
            {
                TM.App.Log($"[GuideContextService] 章节文件不存在: {chapterId}");
                return string.Empty;
            }

            try
            {
                return await File.ReadAllTextAsync(chapterFile).ConfigureAwait(false);
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
