using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GeneratedContentService : IGeneratedContentService
    {
        private string ChaptersDirectory => StoragePathHelper.GetProjectChaptersPath();

        private static readonly Dictionary<string, (ChapterInfo Info, DateTime Modified)> _metaCache = new();
        private static readonly object _metaCacheLock = new();

        private static string[] _cachedFiles = Array.Empty<string>();
        private static DateTime _cachedFilesAt = DateTime.MinValue;
        private static string _cachedFilesDir = string.Empty;
        private static readonly SemaphoreSlim _metaIndexSaveLock = new(1, 1);
        private static int _metaIndexSaveVersion;

        private const string MetaIndexFileName = "_meta_index_v2.json";
        private const string LegacyMetaIndexFileNameV1 = "_meta_index.json";

        public GeneratedContentService()
        {
            _ = ChaptersDirectory;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateStaticCaches();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public void InvalidateStaticCaches()
        {
            lock (_metaCacheLock)
            {
                _metaCache.Clear();
                _cachedFiles = Array.Empty<string>();
                _cachedFilesAt = DateTime.MinValue;
                _cachedFilesDir = string.Empty;
            }
        }

        public Task SaveChapterAsync(string chapterId, string content)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new ArgumentException("章节ID不能为空", nameof(chapterId));

            var callback = ServiceLocator.Get<ContentGenerationCallback>();
            return callback.OnExternalContentSavedAsync(chapterId, content);
        }

        public async Task<string?> GetChapterAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return null;

            var filePath = GetChapterPath(chapterId);

            if (!File.Exists(filePath))
                return null;

            return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }

        public async Task<List<ChapterInfo>> GetGeneratedChaptersAsync()
        {
            var chapters = new List<ChapterInfo>();

            if (!Directory.Exists(ChaptersDirectory))
                return chapters;

            var dir = ChaptersDirectory;
            var dirModified = Directory.Exists(dir)
                ? Directory.GetLastWriteTimeUtc(dir)
                : DateTime.MinValue;
            string[] files;
            lock (_metaCacheLock)
            {
                if (_cachedFilesDir == dir && _cachedFilesAt == dirModified && _cachedFiles.Length > 0)
                    files = _cachedFiles;
                else
                    files = null!;
            }
            if (files == null)
            {
                files = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly);
                lock (_metaCacheLock)
                {
                    _cachedFiles = files;
                    _cachedFilesAt = dirModified;
                    _cachedFilesDir = dir;
                }
            }

            var needsResolve = new List<(string file, FileInfo info, string id, DateTime modified)>();

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var chapterId = Path.GetFileNameWithoutExtension(file);
                var modified = fileInfo.LastWriteTime;

                ChapterInfo? cached = null;
                lock (_metaCacheLock)
                {
                    if (_metaCache.TryGetValue(file, out var entry) && entry.Modified == modified)
                        cached = entry.Info;
                }
                if (cached != null) { chapters.Add(cached); continue; }

                needsResolve.Add((file, fileInfo, chapterId, modified));
            }

            if (needsResolve.Count > 0)
            {
                var index = await LoadMetaIndexFromDiskAsync().ConfigureAwait(false);
                var needsRead = new List<(string file, FileInfo info, string id, DateTime modified)>();

                foreach (var item in needsResolve)
                {
                    if (index.TryGetValue(item.id, out var ie) && ie.ModifiedTicks == item.modified.Ticks)
                    {
                        var (vn, cn) = ParseChapterId(item.id);
                        var info = new ChapterInfo
                        {
                            Id = item.id,
                            Title = ie.Title,
                            VolumeNumber = vn,
                            ChapterNumber = cn,
                            WordCount = ie.WordCount,
                            CreatedTime = item.info.CreationTime,
                            ModifiedTime = item.modified,
                            FilePath = item.file
                        };
                        lock (_metaCacheLock) { _metaCache[item.file] = (info, item.modified); }
                        chapters.Add(info);
                    }
                    else
                    {
                        needsRead.Add(item);
                    }
                }

                var indexDirty = needsRead.Count > 0;
                if (needsRead.Count > 0)
                {
                    var parallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
                    using var semaphore = new SemaphoreSlim(parallelism, parallelism);

                    var readTasks = needsRead.Select(async rd =>
                    {
                        await semaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            var (title, wordCount) = await ReadChapterMetaAsync(rd.file).ConfigureAwait(false);
                            var (vn, cn) = ParseChapterId(rd.id);
                            return (rd.id, rd.file, rd.modified,
                                Info: new ChapterInfo
                                {
                                    Id = rd.id,
                                    Title = title,
                                    VolumeNumber = vn,
                                    ChapterNumber = cn,
                                    WordCount = wordCount,
                                    CreatedTime = rd.info.CreationTime,
                                    ModifiedTime = rd.modified,
                                    FilePath = rd.file
                                },
                                Meta: new ChapterMetaEntry { Title = title, WordCount = wordCount, ModifiedTicks = rd.modified.Ticks });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    foreach (var r in await Task.WhenAll(readTasks).ConfigureAwait(false))
                    {
                        chapters.Add(r.Info);
                        lock (_metaCacheLock) { _metaCache[r.file] = (r.Info, r.modified); }
                        index[r.id] = r.Meta;
                    }
                }

                var existingIds = new HashSet<string>(files.Select(f => Path.GetFileNameWithoutExtension(f)));
                var staleKeys = index.Keys.Where(k => !existingIds.Contains(k)).ToList();
                if (staleKeys.Count > 0)
                {
                    foreach (var key in staleKeys) index.Remove(key);
                    indexDirty = true;
                }

                if (indexDirty)
                {
                    var saveVersion = Interlocked.Increment(ref _metaIndexSaveVersion);
                    _ = SaveMetaIndexAsync(new Dictionary<string, ChapterMetaEntry>(index), saveVersion);
                }
            }

            return chapters
                .OrderBy(c => c.VolumeNumber)
                .ThenBy(c => c.ChapterNumber)
                .ToList();
        }

        public async Task<bool> DeleteChapterAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return false;

            var filePath = GetChapterPath(chapterId);

            if (!File.Exists(filePath))
                return false;

            try
            {
                string? tempPath = null;

                try
                {
                    tempPath = filePath + $".deleting_{Guid.NewGuid():N}.tmp";
                    File.Move(filePath, tempPath);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GeneratedContentService] 章节文件无法移动（可能被占用），已中止级联清理: {chapterId}, {ex.Message}");
                    return false;
                }

                try
                {
                    await ServiceLocator.Get<ContentGenerationCallback>().OnChapterDeletedAsync(chapterId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GeneratedContentService] 章节清理失败（非致命，继续删除MD）: {chapterId}, {ex.Message}");
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GeneratedContentService] 删除章节失败: {chapterId}, {ex.Message}");
                    return false;
                }

                InvalidateStaticCaches();
                TM.App.Log($"[GeneratedContentService] 删除章节: {chapterId}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 删除章节失败: {chapterId}, {ex.Message}");
                return false;
            }
        }

        public bool ChapterExists(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return false;

            return File.Exists(GetChapterPath(chapterId));
        }

        public string GetChapterPath(string chapterId)
        {
            return Path.Combine(ChaptersDirectory, $"{chapterId}.md");
        }

        private static (int volumeNumber, int chapterNumber) ParseChapterId(string chapterId)
        {
            return ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
        }

        private static string NormalizeChapterTitle(string title)
        {
            return ChapterParserHelper.NormalizeChapterTitle(title);
        }

        private static async Task<(string Title, int WordCount)> ReadChapterMetaAsync(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? firstNonEmptyLine = null;
                string? title = null;

                var totalCount = 0;
                var lineIndex = 0;

                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                        break;

                    var trimmed = line.Trim();
                    if (firstNonEmptyLine == null && !string.IsNullOrWhiteSpace(trimmed))
                        firstNonEmptyLine = trimmed;

                    if (title == null && lineIndex < 10)
                    {
                        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                            title = NormalizeChapterTitle(trimmed.Substring(2).Trim());
                        else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                            title = NormalizeChapterTitle(trimmed.Substring(3).Trim());
                    }

                    totalCount += WordCountHelper.CountRaw(line);
                    lineIndex++;
                }

                if (string.IsNullOrEmpty(title))
                {
                    if (!string.IsNullOrEmpty(firstNonEmptyLine))
                    {
                        title = firstNonEmptyLine.Length > 50
                            ? firstNonEmptyLine.Substring(0, 50) + "..."
                            : firstNonEmptyLine;
                    }
                    else
                    {
                        title = "未命名章节";
                    }
                }

                return (title, totalCount);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 读取章节元数据失败 [{Path.GetFileName(filePath)}]: {ex.Message}");
                return ("未命名章节", 0);
            }
        }

        #region 持久化元数据索引

        private string MetaIndexPath => Path.Combine(ChaptersDirectory, MetaIndexFileName);

        private async Task<Dictionary<string, ChapterMetaEntry>> LoadMetaIndexFromDiskAsync()
        {
            try
            {
                var legacyV1 = Path.Combine(ChaptersDirectory, LegacyMetaIndexFileNameV1);
                if (File.Exists(legacyV1))
                {
                    File.Delete(legacyV1);
                    TM.App.Log("[GeneratedContentService] 已删除 v1 旧口径字数索引，首次加载将按统一口径重算。");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 清理 v1 索引失败（忽略）: {ex.Message}");
            }

            try
            {
                var path = MetaIndexPath;
                if (!File.Exists(path)) return new();

                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<Dictionary<string, ChapterMetaEntry>>(stream).ConfigureAwait(false) ?? new();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 加载元数据索引失败（将重建）: {ex.Message}");
                return new();
            }
        }

        private async Task SaveMetaIndexAsync(Dictionary<string, ChapterMetaEntry> snapshot, int saveVersion)
        {
            await _metaIndexSaveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _metaIndexSaveVersion))
                    return;
                var path = MetaIndexPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(snapshot);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 保存元数据索引失败: {ex.Message}");
            }
            finally
            {
                _metaIndexSaveLock.Release();
            }
        }

        private class ChapterMetaEntry
        {
            public string Title { get; set; } = "";
            public int WordCount { get; set; }
            public long ModifiedTicks { get; set; }
        }

        #endregion

        #region 分类（卷）管理

        public async Task<bool> VolumeExistsAsync(int volumeNumber)
        {
            var volumeService = ServiceLocator.Get<VolumeDesignService>();
            await volumeService.InitializeAsync().ConfigureAwait(false);
            return volumeService.GetAllVolumeDesigns().Any(v => v.VolumeNumber == volumeNumber);
        }

        public async Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId)
        {
            if (string.IsNullOrWhiteSpace(sourceChapterId))
                throw new ArgumentException("章节ID不能为空", nameof(sourceChapterId));

            var parsed = ChapterParserHelper.ParseChapterId(sourceChapterId);
            if (parsed == null)
                throw new ArgumentException($"章节ID格式无效: {sourceChapterId}", nameof(sourceChapterId));

            var (volumeNumber, chapterNumber) = parsed.Value;

            if (!await VolumeExistsAsync(volumeNumber).ConfigureAwait(false))
                throw new InvalidOperationException($"卷 {volumeNumber} 不存在");

            if (!ChapterExists(sourceChapterId))
                throw new InvalidOperationException($"来源章节 {sourceChapterId} 不存在");

            var sameVolumeNextId = ChapterParserHelper.BuildChapterId(volumeNumber, chapterNumber + 1);
            var targetChapterId = sameVolumeNextId;

            var volumeService = ServiceLocator.Get<VolumeDesignService>();
            await volumeService.InitializeAsync().ConfigureAwait(false);
            var designs = volumeService.GetAllVolumeDesigns();

            var currentDesign = designs.FirstOrDefault(v => v.VolumeNumber == volumeNumber);
            if (currentDesign != null)
            {
                var effectiveEndChapter = currentDesign.EndChapter;

                if (effectiveEndChapter <= 0)
                    effectiveEndChapter = await ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber).ConfigureAwait(false);

                if (effectiveEndChapter > 0 && chapterNumber >= effectiveEndChapter)
                {
                    var nextDesign = designs
                        .Where(v => v.VolumeNumber > volumeNumber)
                        .OrderBy(v => v.VolumeNumber)
                        .FirstOrDefault();

                    if (nextDesign != null)
                    {
                        var nextStart = nextDesign.StartChapter > 0 ? nextDesign.StartChapter : 1;
                        targetChapterId = ChapterParserHelper.BuildChapterId(nextDesign.VolumeNumber, nextStart);
                        TM.App.Log($"[GeneratedContentService] 跨卷续写: {sourceChapterId} → {targetChapterId}（第{volumeNumber}卷末→第{nextDesign.VolumeNumber}卷首）");
                    }
                }
            }

            if (ChapterExists(targetChapterId))
                throw new InvalidOperationException($"目标章节 {targetChapterId} 已存在，请使用 @重写:{targetChapterId} 指令");

            TM.App.Log($"[GeneratedContentService] 从 {sourceChapterId} 生成下一章ID: {targetChapterId}");
            return targetChapterId;
        }

        #endregion
    }
}
