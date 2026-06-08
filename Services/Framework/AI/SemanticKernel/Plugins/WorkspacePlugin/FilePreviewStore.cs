using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public static class FilePreviewStore
    {
        private static readonly ConcurrentDictionary<string, FilePreviewEntry> _store = new();
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public static string CreateReplacePreview(
            string relativePath,
            string fullPath,
            string originalContent,
            string newContent,
            string diffSummary,
            string originalHash)
        {
            CleanExpired();
            var previewId = $"fp_{Guid.NewGuid():N}";
            var entry = new FilePreviewEntry
            {
                PreviewId = previewId,
                OperationType = FileOperationType.Replace,
                RelativePath = relativePath,
                FullPath = fullPath,
                OriginalContent = originalContent,
                NewContent = newContent,
                DiffSummary = diffSummary,
                OriginalHash = originalHash,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(DefaultTtl)
            };
            _store[previewId] = entry;
            TM.App.Log($"[FilePreviewStore] 创建替换预览 {previewId}: {relativePath}");
            return previewId;
        }

        public static string CreateWritePreview(
            string relativePath,
            string fullPath,
            string originalContent,
            string newContent,
            string diffSummary,
            string originalHash)
        {
            CleanExpired();
            var previewId = $"fp_{Guid.NewGuid():N}";
            var entry = new FilePreviewEntry
            {
                PreviewId = previewId,
                OperationType = FileOperationType.Write,
                RelativePath = relativePath,
                FullPath = fullPath,
                OriginalContent = originalContent,
                NewContent = newContent,
                DiffSummary = diffSummary,
                OriginalHash = originalHash,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(DefaultTtl)
            };
            _store[previewId] = entry;
            TM.App.Log($"[FilePreviewStore] 创建写入预览 {previewId}: {relativePath}");
            return previewId;
        }

        public static string CreateDeletePreview(
            string relativePath,
            string fullPath,
            string originalContent,
            string originalHash)
        {
            CleanExpired();
            var previewId = $"fp_{Guid.NewGuid():N}";
            var entry = new FilePreviewEntry
            {
                PreviewId = previewId,
                OperationType = FileOperationType.Delete,
                RelativePath = relativePath,
                FullPath = fullPath,
                OriginalContent = originalContent,
                NewContent = string.Empty,
                DiffSummary = $"删除文件: {relativePath}（{originalContent.Length} 字符）",
                OriginalHash = originalHash,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(DefaultTtl)
            };
            _store[previewId] = entry;
            TM.App.Log($"[FilePreviewStore] 创建删除预览 {previewId}: {relativePath}");
            return previewId;
        }

        public static string CreateRenamePreview(
            string relativePath,
            string fullPath,
            string newRelativePath,
            string newFullPath,
            string originalHash)
        {
            CleanExpired();
            var previewId = $"fp_{Guid.NewGuid():N}";
            var entry = new FilePreviewEntry
            {
                PreviewId = previewId,
                OperationType = FileOperationType.Rename,
                RelativePath = relativePath,
                FullPath = fullPath,
                NewRelativePath = newRelativePath,
                NewFullPath = newFullPath,
                DiffSummary = $"重命名: {relativePath} → {newRelativePath}",
                OriginalHash = originalHash,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(DefaultTtl)
            };
            _store[previewId] = entry;
            TM.App.Log($"[FilePreviewStore] 创建重命名预览 {previewId}: {relativePath} → {newRelativePath}");
            return previewId;
        }

        public static FilePreviewEntry? GetPreview(string previewId)
        {
            if (string.IsNullOrEmpty(previewId)) return null;
            if (!_store.TryGetValue(previewId, out var entry)) return null;
            if (DateTime.Now > entry.ExpiresAt)
            {
                _store.TryRemove(previewId, out _);
                return null;
            }
            return entry;
        }

        public static bool Remove(string previewId)
        {
            return _store.TryRemove(previewId, out _);
        }

        private static void CleanExpired()
        {
            var now = DateTime.Now;
            var expired = _store.Where(kv => now > kv.Value.ExpiresAt).Select(kv => kv.Key).ToList();
            foreach (var key in expired)
            {
                _store.TryRemove(key, out _);
            }
        }

        public static string ComputeHash(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes);
        }

        public static int Count => _store.Count;
    }

    public enum FileOperationType
    {
        Replace,
        Write,
        Delete,
        Rename
    }

    public class FilePreviewEntry
    {
        public string PreviewId { get; set; } = string.Empty;
        public FileOperationType OperationType { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string? NewRelativePath { get; set; }
        public string? NewFullPath { get; set; }
        public string OriginalContent { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
        public string DiffSummary { get; set; } = string.Empty;
        public string OriginalHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public static class FileEditHistoryLog
    {
        private static readonly object _writeLock = new();

        public static void Append(FilePreviewEntry entry, string resultSummary)
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "AI/EditHistory", "file_edit_history.jsonl");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var record = new
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    entry.PreviewId,
                    Operation = entry.OperationType.ToString(),
                    Path = entry.RelativePath,
                    NewPath = entry.NewRelativePath,
                    Result = resultSummary.Length > 500 ? resultSummary[..500] : resultSummary
                };

                var json = JsonSerializer.Serialize(record, JsonHelper.Compact);

                lock (_writeLock)
                {
                    File.AppendAllText(filePath, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FileEditHistoryLog] 追加历史记录失败: {ex.Message}");
            }
        }
    }
}
