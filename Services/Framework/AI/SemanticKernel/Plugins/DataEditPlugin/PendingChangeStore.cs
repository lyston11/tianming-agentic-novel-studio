using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public static class PendingChangeStore
    {
        private static readonly ConcurrentDictionary<string, PendingChangeEntry> _store = new();
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public static string CreatePreview(List<EntityChangeOperation> operations, Dictionary<string, string> snapshots)
        {
            CleanExpired();
            var previewId = $"pv_{Guid.NewGuid():N}";
            var entry = new PendingChangeEntry
            {
                PreviewId = previewId,
                Operations = operations,
                Snapshots = snapshots,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(DefaultTtl)
            };
            _store[previewId] = entry;
            TM.App.Log($"[PendingChangeStore] 创建预览 {previewId}，包含 {operations.Count} 个操作");
            return previewId;
        }

        public static PendingChangeEntry? GetPreview(string previewId)
        {
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

        public static int Count => _store.Count;
    }

    public class PendingChangeEntry
    {
        public string PreviewId { get; set; } = string.Empty;
        public List<EntityChangeOperation> Operations { get; set; } = new();

        public Dictionary<string, string> Snapshots { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class EntityChangeOperation
    {
        [JsonPropertyName("type")]
        public string EntityType { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string EntityId { get; set; } = string.Empty;

        [JsonPropertyName("op")]
        public string Op { get; set; } = string.Empty;

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }
    }

    public static class EditHistoryLog
    {
        private static readonly object _writeLock = new();

        public static void Append(PendingChangeEntry entry, string resultSummary)
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "AI/EditHistory", "edit_history.jsonl");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var record = new
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    PreviewId = entry.PreviewId,
                    OperationCount = entry.Operations.Count,
                    Operations = entry.Operations.Select(op => new
                    {
                        op.EntityType,
                        op.EntityId,
                        op.Op,
                        Payload = op.Payload.ToString()
                    }),
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
                TM.App.Log($"[EditHistoryLog] 追加历史记录失败: {ex.Message}");
            }
        }
    }
}
