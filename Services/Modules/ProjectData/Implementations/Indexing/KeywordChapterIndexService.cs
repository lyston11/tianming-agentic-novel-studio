using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class KeywordChapterIndexService
    {
        private Dictionary<string, HashSet<string>>? _index;
        private Dictionary<string, List<string>>? _indexOrder;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _dirty;
        private volatile bool _pendingInvalidation;

        public KeywordChapterIndexService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static int MaxChaptersPerKeyword => LayeredContextConfig.KeywordIndexMaxChaptersPerTerm;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false
        };

        #region 公开方法

        public async Task IndexChapterAsync(string chapterId, ChapterChanges changes)
        {
            if (string.IsNullOrEmpty(chapterId) || changes == null) return;

            var keywords = ExtractKeywords(changes);
            if (keywords.Count == 0) return;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);

                foreach (var kw in keywords)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!_index!.TryGetValue(key, out var chapters))
                    {
                        chapters = new HashSet<string>(StringComparer.Ordinal);
                        _index[key] = chapters;
                        _indexOrder![key] = new List<string>();
                    }

                    var order = _indexOrder![key];
                    if (chapters.Add(chapterId))
                    {
                        order.Add(chapterId);
                        while (order.Count > MaxChaptersPerKeyword)
                        {
                            chapters.Remove(order[0]);
                            order.RemoveAt(0);
                        }
                    }
                }

                _dirty = true;
                await SaveAsync().ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }

            TM.App.Log($"[KeywordIndex] 已索引 {chapterId}: {keywords.Count}个关键词");
        }

        public async Task<List<string>> SearchAsync(IEnumerable<string> keywords, int topK = 5)
        {
            var kwList = keywords?.ToList() ?? new List<string>();
            if (kwList.Count == 0) return new List<string>();

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);

                var hitCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kw in kwList)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!_index!.TryGetValue(key, out var chapters)) continue;

                    foreach (var chapId in chapters)
                    {
                        hitCount[chapId] = hitCount.GetValueOrDefault(chapId) + 1;
                    }
                }

                return hitCount
                    .OrderByDescending(kv => kv.Value)
                    .Take(topK)
                    .Select(kv => kv.Key)
                    .ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveChapterAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);

                var modified = false;
                foreach (var chapters in _index!.Values)
                {
                    if (chapters.Remove(chapterId))
                        modified = true;
                }

                if (modified)
                {
                    _dirty = true;
                    await SaveAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<HashSet<string>> GetIndexedChapterIdsAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var chapters in _index!.Values)
                    result.UnionWith(chapters);
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task IndexChapterFromKeywordsAsync(string chapterId, IEnumerable<string> keywords)
        {
            if (string.IsNullOrEmpty(chapterId) || keywords == null) return;

            var kwList = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            if (kwList.Count == 0) return;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);

                foreach (var kw in kwList)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!_index!.TryGetValue(key, out var chapters))
                    {
                        chapters = new HashSet<string>(StringComparer.Ordinal);
                        _index[key] = chapters;
                        _indexOrder![key] = new List<string>();
                    }

                    var order = _indexOrder![key];
                    if (chapters.Add(chapterId))
                    {
                        order.Add(chapterId);
                        while (order.Count > MaxChaptersPerKeyword)
                        {
                            chapters.Remove(order[0]);
                            order.RemoveAt(0);
                        }
                    }
                }

                _dirty = true;
                await SaveAsync().ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void InvalidateCache()
        {
            _pendingInvalidation = true;
            _dirty = false;
        }

        #endregion

        #region 私有方法

        private static List<string> ExtractKeywords(ChapterChanges changes)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in changes.CharacterStateChanges ?? new())
                if (!string.IsNullOrWhiteSpace(c.CharacterId))
                    result.Add(c.CharacterId);

            foreach (var p in changes.NewPlotPoints ?? new())
            {
                foreach (var kw in p.Keywords ?? new())
                    if (!string.IsNullOrWhiteSpace(kw))
                        result.Add(kw);
                foreach (var charId in p.InvolvedCharacters ?? new())
                    if (!string.IsNullOrWhiteSpace(charId))
                        result.Add(charId);
            }

            foreach (var f in changes.ForeshadowingActions ?? new())
                if (!string.IsNullOrWhiteSpace(f.ForeshadowId))
                    result.Add(f.ForeshadowId);

            foreach (var item in changes.ItemTransfers ?? new())
            {
                if (!string.IsNullOrWhiteSpace(item.ItemId))
                    result.Add(item.ItemId);
                if (!string.IsNullOrWhiteSpace(item.ItemName))
                    result.Add(item.ItemName);
            }

            foreach (var sr in changes.SecretRevealChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(sr.SecretId))
                    result.Add(sr.SecretId);
                if (!string.IsNullOrWhiteSpace(sr.SecretName))
                    result.Add(sr.SecretName);
            }

            foreach (var pc in changes.PledgeConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(pc.PledgeId))
                    result.Add(pc.PledgeId);
                if (!string.IsNullOrWhiteSpace(pc.PledgeName))
                    result.Add(pc.PledgeName);
            }

            foreach (var dc in changes.DeadlineConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(dc.DeadlineId))
                    result.Add(dc.DeadlineId);
                if (!string.IsNullOrWhiteSpace(dc.DeadlineName))
                    result.Add(dc.DeadlineName);
            }

            foreach (var loc in changes.LocationStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(loc.LocationId))
                    result.Add(loc.LocationId);
                if (!string.IsNullOrWhiteSpace(loc.LocationName))
                    result.Add(loc.LocationName);
            }

            foreach (var fac in changes.FactionStateChanges ?? new())
                if (!string.IsNullOrWhiteSpace(fac.FactionId))
                    result.Add(fac.FactionId);

            foreach (var conf in changes.ConflictProgress ?? new())
                if (!string.IsNullOrWhiteSpace(conf.ConflictId))
                    result.Add(conf.ConflictId);

            foreach (var mov in changes.CharacterMovements ?? new())
            {
                if (!string.IsNullOrWhiteSpace(mov.CharacterId))
                    result.Add(mov.CharacterId);
                if (!string.IsNullOrWhiteSpace(mov.ToLocationName))
                    result.Add(mov.ToLocationName);
            }

            return result.ToList();
        }

        private static string NormalizeKeyword(string kw)
        {
            return kw.Trim().ToLowerInvariant();
        }

        private async Task EnsureLoadedAsync()
        {
            if (_pendingInvalidation)
            {
                _index = null;
                _indexOrder = null;
                _pendingInvalidation = false;
            }
            if (_index != null) return;

            var path = GetIndexFilePath();
            if (!File.Exists(path))
            {
                _index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                _indexOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                var raw = await JsonSerializer.DeserializeAsync<Dictionary<string, List<string>>>(stream, _jsonOptions).ConfigureAwait(false);
                if (raw != null)
                {
                    _indexOrder = new Dictionary<string, List<string>>(
                        raw.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value)),
                        StringComparer.OrdinalIgnoreCase);
                    _index = new Dictionary<string, HashSet<string>>(
                        raw.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value, StringComparer.Ordinal)),
                        StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    _indexOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 加载失败，使用空索引: {ex.Message}");
                _index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                _indexOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task SaveAsync()
        {
            if (!_dirty) return;

            var dir = Path.GetDirectoryName(GetIndexFilePath())!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetIndexFilePath();
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (var stream = File.Create(tmpPath))
                {
                    await JsonSerializer.SerializeAsync(stream, _indexOrder ?? new(), _jsonOptions).ConfigureAwait(false);
                }
                File.Move(tmpPath, path, overwrite: true);
                _dirty = false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 保存失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        private static string GetIndexFilePath()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "keyword_index.json");
        }

        #endregion
    }
}
