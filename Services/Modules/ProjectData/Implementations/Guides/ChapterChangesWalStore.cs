using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations.Guides
{
    public class ChapterChangesWalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public async Task WriteAsync(string chapterId, ChapterChanges changes)
        {
            try
            {
                var dir = GetWalDir();
                Directory.CreateDirectory(dir);
                var path = GetFilePath(dir, chapterId);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(changes, JsonOptions)).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangesWal] 写入失败 {chapterId}: {ex.Message}");
            }
        }

        public async Task<ChapterChanges?> TryReadAsync(string chapterId)
        {
            try
            {
                var path = GetFilePath(GetWalDir(), chapterId);
                if (!File.Exists(path)) return null;
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<ChapterChanges>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangesWal] 读取失败 {chapterId}: {ex.Message}");
                return null;
            }
        }

        public void Delete(string chapterId)
        {
            try
            {
                var path = GetFilePath(GetWalDir(), chapterId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangesWal] 删除失败 {chapterId}: {ex.Message}");
            }
        }

        public IReadOnlyList<string> GetAllChapterIds()
        {
            try
            {
                var dir = GetWalDir();
                if (!Directory.Exists(dir)) return Array.Empty<string>();
                return Directory.GetFiles(dir, "*.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f)!)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void CleanupOrphanTmp()
        {
            try
            {
                var dir = GetWalDir();
                if (!Directory.Exists(dir)) return;
                foreach (var tmp in Directory.GetFiles(dir, "*.tmp"))
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
            catch { }
        }

        private static string GetWalDir()
            => Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "changes_wal");

        private static string GetFilePath(string dir, string chapterId)
            => Path.Combine(dir, $"{chapterId}.json");
    }
}
