using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class SessionManager
    {

        private volatile string _sessionsDir;
        private string? _currentSessionId;
        private readonly ConcurrentDictionary<string, SessionInfo> _sessionIndex = new();
        private readonly object _indexSwapLock = new();
        private readonly object _writeQueueLock = new();
        private Task _writeQueue = Task.CompletedTask;

        public Task InitializationTask { get; }

        public SessionManager()
        {
            _sessionsDir = BuildSessionsDir();

            InitializationTask = Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(_sessionsDir);
                    await LoadSessionIndexAsync().ConfigureAwait(false);
                    CleanupEmptySessions();
                    RestoreLastSessionId();
                    TM.App.Log($"[SessionManager] 初始化完成，会话目录: {_sessionsDir}，当前会话: {_currentSessionId ?? "(无)"}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SessionManager] 初始化失败: {ex.Message}");
                }
            });

            StoragePathHelper.CurrentProjectChanged += OnProjectChanged;
        }

        private static string BuildSessionsDir()
            => Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "Sessions");

        private async void OnProjectChanged(string oldProject, string newProject)
        {
            try
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    var newDir = BuildSessionsDir();
                    Directory.CreateDirectory(newDir);

                    var newIndex = await LoadSessionIndexFromDirAsync(newDir).ConfigureAwait(false);

                    lock (_indexSwapLock)
                    {
                        foreach (var key in _sessionIndex.Keys)
                            _sessionIndex.TryRemove(key, out _);
                        foreach (var kv in newIndex)
                            _sessionIndex[kv.Key] = kv.Value;
                        _sessionsDir = newDir;
                        _currentSessionId = null;
                    }

                    TM.App.Log($"[SessionManager] 项目切换（{oldProject}→{newProject}），会话目录: {newDir}");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 项目切换失败: {ex.Message}");
            }
        }

        #region 会话管理

        public string CreateSession(string? title = null)
        {
            var sessionId = ShortIdGenerator.NewGuid().ToString("N")[..8];
            var info = new SessionInfo
            {
                Id = sessionId,
                Title = title ?? $"会话 {DateTime.Now:MM-dd HH:mm}",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                ContextChapterId = CurrentChapterTracker.CurrentChapterId
            };

            _sessionIndex[sessionId] = info;
            _currentSessionId = sessionId;

            SaveSessionIndex();
            SaveMessages(sessionId, Array.Empty<SerializedMessageRecord>());

            if (InfoLogDedup.ShouldLog($"SessionManager:Create:{sessionId}"))
                TM.App.Log($"[SessionManager] 创建会话: {sessionId}");
            return sessionId;
        }

        public void ResetCurrentSession()
        {
            _currentSessionId = null;
        }

        public ChatHistory SwitchSessionWithRecords(string sessionId, List<SerializedMessageRecord> records)
        {
            if (!_sessionIndex.ContainsKey(sessionId))
            {
                TM.App.Log($"[SessionManager] 会话不存在: {sessionId}");
                return new ChatHistory();
            }

            _currentSessionId = sessionId;
            if (_sessionIndex.TryGetValue(sessionId, out var switchedInfo))
            {
                switchedInfo.UpdatedAt = DateTime.Now;
                SaveSessionIndex();
            }
            return RebuildChatHistory(records);
        }

        public string GetCurrentSessionId()
        {
            if (string.IsNullOrEmpty(_currentSessionId))
            {
                _currentSessionId = CreateSession();
            }
            return _currentSessionId;
        }

        public string? GetCurrentSessionIdOrNull()
        {
            return _currentSessionId;
        }

        public bool HasCurrentSession => !string.IsNullOrEmpty(_currentSessionId);

        public List<SessionInfo> GetAllSessions()
        {
            return _sessionIndex.Values
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();
        }

        public void DeleteSession(string sessionId)
        {
            if (_sessionIndex.TryRemove(sessionId, out _))
            {
                var messagesPath = GetMessagesFilePath(sessionId);
                if (File.Exists(messagesPath))
                {
                    File.Delete(messagesPath);
                }

                SaveSessionIndex();

                if (_currentSessionId == sessionId)
                {
                    _currentSessionId = _sessionIndex.Keys.FirstOrDefault();
                }

                TM.App.Log($"[SessionManager] 删除会话: {sessionId}");
            }
        }

        public void RenameSession(string sessionId, string newTitle)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                info.Title = newTitle;
                info.UpdatedAt = DateTime.Now;
                SaveSessionIndex();
            }
        }

        public void UpdateSessionMode(string sessionId, string mode)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                info.Mode = mode;
                SaveSessionIndex();
                if (InfoLogDedup.ShouldLog($"SessionManager:Mode:{sessionId}"))
                    TM.App.Log($"[SessionManager] 更新会话模式: {sessionId} -> {mode}");
            }
        }

        public string GetSessionMode(string sessionId)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                return info.Mode ?? "0";
            }
            return "0";
        }

        #endregion

        #region 三层消息存储（新架构）

        public void SaveMessages(string sessionId, IEnumerable<SerializedMessageRecord> messages)
        {
            try
            {
                var list = messages?.ToList() ?? new List<SerializedMessageRecord>();

                if (_sessionIndex.TryGetValue(sessionId, out var info))
                {
                    info.UpdatedAt = DateTime.Now;
                    info.MessageCount = list.Count;
                    if (CurrentChapterTracker.HasCurrentChapter)
                        info.ContextChapterId = CurrentChapterTracker.CurrentChapterId;
                }

                var idxSnapshot = _sessionIndex.Values.ToList();
                var msgPath = GetMessagesFilePath(sessionId);
                var idxPath = GetIndexFilePath();
                var count = list.Count;

                EnqueueWrite(async () =>
                {
                    var msgJson = JsonSerializer.Serialize(list, JsonHelper.Default);
                    var tmp = msgPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmp, msgJson).ConfigureAwait(false);
                    File.Move(tmp, msgPath, overwrite: true);

                    var idxJson = JsonSerializer.Serialize(idxSnapshot, JsonHelper.Default);
                    var idxTmp = idxPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(idxTmp, idxJson).ConfigureAwait(false);
                    File.Move(idxTmp, idxPath, overwrite: true);

                    if (InfoLogDedup.ShouldLog($"SessionManager:Save:{sessionId}"))
                    {
                        TM.App.Log($"[SessionManager] 保存消息: {sessionId}, {count} 条");
                    }
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 保存消息失败: {ex.Message}");
            }
        }

        public async Task<List<SerializedMessageRecord>> LoadMessagesAsync(string sessionId)
        {
            var filePath = GetMessagesFilePath(sessionId);
            if (!File.Exists(filePath))
                return new List<SerializedMessageRecord>();

            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var records = JsonSerializer.Deserialize<List<SerializedMessageRecord>>(json);
                if (InfoLogDedup.ShouldLog($"SessionManager:Load:{sessionId}"))
                {
                    TM.App.Log($"[SessionManager] 加载消息: {sessionId}, {records?.Count ?? 0} 条");
                }
                return records ?? new List<SerializedMessageRecord>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 加载消息失败: {ex.Message}");
                return new List<SerializedMessageRecord>();
            }
        }

        public void SaveCurrentMessages(IEnumerable<SerializedMessageRecord> messages)
        {
            var list = messages?.ToList() ?? new List<SerializedMessageRecord>();

            if (string.IsNullOrEmpty(_currentSessionId) && list.Count == 0)
            {
                return;
            }

            var sessionId = string.IsNullOrEmpty(_currentSessionId)
                ? CreateSession()
                : _currentSessionId;

            SaveMessages(sessionId, list);
        }

        public ChatHistory RebuildChatHistory(IEnumerable<SerializedMessageRecord> messages)
        {
            var history = new ChatHistory();
            foreach (var msg in messages)
            {
                var role = msg.Role.ToLower() switch
                {
                    "system" => AuthorRole.System,
                    "user" => AuthorRole.User,
                    "assistant" => AuthorRole.Assistant,
                    _ => AuthorRole.User
                };
                history.Add(new ChatMessageContent(role, msg.Summary));
            }
            return history;
        }

        private string GetMessagesFilePath(string sessionId)
        {
            return Path.Combine(_sessionsDir, $"{sessionId}.messages.json");
        }

        #endregion

        #region 私有方法

        private string GetIndexFilePath()
        {
            return Path.Combine(_sessionsDir, "_index.json");
        }

        private void EnqueueWrite(Func<Task> writeAction)
        {
            lock (_writeQueueLock)
            {
                _writeQueue = _writeQueue.ContinueWith(async _ =>
                {
                    try
                    {
                        await writeAction().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SessionManager] 写入失败: {ex.Message}");
                    }
                }, TaskScheduler.Default).Unwrap();
            }
        }

        private static async System.Threading.Tasks.Task<Dictionary<string, SessionInfo>> LoadSessionIndexFromDirAsync(string dir)
        {
            var result = new Dictionary<string, SessionInfo>();
            var indexPath = Path.Combine(dir, "_index.json");
            if (!File.Exists(indexPath))
                indexPath = Path.Combine(dir, "session_index.json");
            if (!File.Exists(indexPath)) return result;
            try
            {
                var json = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
                var trimmed = json.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == "{}" || trimmed == "null") return result;
                List<SessionInfo>? sessions = null;
                if (trimmed.StartsWith('['))
                    sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionInfo>>(json, JsonHelper.Default);
                else if (trimmed.StartsWith('{'))
                {
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SessionInfo>>(json, JsonHelper.Default);
                    if (dict != null) sessions = dict.Values.ToList();
                }
                if (sessions != null)
                    foreach (var s in sessions)
                        if (!string.IsNullOrEmpty(s.Id))
                            result[s.Id] = s;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] LoadSessionIndexFromDirAsync 失败: {ex.Message}");
            }
            return result;
        }

        private void RestoreLastSessionId()
        {
            if (_sessionIndex.IsEmpty) return;

            var lastSession = _sessionIndex.Values
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefault();

            if (lastSession != null && !string.IsNullOrEmpty(lastSession.Id))
            {
                _currentSessionId = lastSession.Id;
            }
        }

        private async System.Threading.Tasks.Task LoadSessionIndexAsync()
        {
            var indexPath = GetIndexFilePath();
            if (!File.Exists(indexPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
                var trimmed = json.Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed == "{}" || trimmed == "null")
                {
                    TM.App.Log($"[SessionManager] 异步：索引文件为空或旧格式，已重置");
                    var tmpEmpty = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmpEmpty, "[]").ConfigureAwait(false);
                    File.Move(tmpEmpty, indexPath, overwrite: true);
                    return;
                }

                List<SessionInfo>? sessions = null;

                if (trimmed.StartsWith('['))
                {
                    sessions = JsonSerializer.Deserialize<List<SessionInfo>>(json, JsonHelper.Default);
                }
                else if (trimmed.StartsWith('{'))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, SessionInfo>>(json, JsonHelper.Default);
                    if (dict != null)
                    {
                        sessions = dict.Values.ToList();
                        var migratedJson = JsonSerializer.Serialize(sessions, JsonHelper.Default);
                        var tmpMigrated = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllTextAsync(tmpMigrated, migratedJson).ConfigureAwait(false);
                        File.Move(tmpMigrated, indexPath, overwrite: true);
                        TM.App.Log($"[SessionManager] 异步：旧格式索引已迁移: {sessions.Count} 条");
                    }
                }

                if (sessions != null)
                {
                    foreach (var s in sessions)
                    {
                        if (!string.IsNullOrEmpty(s.Id))
                            _sessionIndex[s.Id] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 异步加载索引失败（已重置）: {ex.Message}");
                try
                {
                    var tmpReset = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmpReset, "[]").ConfigureAwait(false);
                    File.Move(tmpReset, indexPath, overwrite: true);
                }
                catch { }
            }
        }

        public void ReloadIndex()
        {
            _sessionIndex.Clear();
            _currentSessionId = null;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await LoadSessionIndexAsync().ConfigureAwait(false);
                    CleanupEmptySessions();
                    RestoreLastSessionId();
                    TM.App.Log($"[SessionManager] 已重新加载会话索引，当前会话: {_currentSessionId ?? "(无)"}");
                }
                catch (Exception ex) { TM.App.Log($"[SessionManager] 重新加载索引失败: {ex.Message}"); }
            });
        }

        private void SaveSessionIndex()
        {
            try
            {
                var snapshot = _sessionIndex.Values.ToList();
                var path = GetIndexFilePath();

                EnqueueWrite(async () =>
                {
                    var json = JsonSerializer.Serialize(snapshot, JsonHelper.Default);
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 保存索引失败: {ex.Message}");
            }
        }

        private void CleanupEmptySessions()
        {
            try
            {
                var toDelete = new List<string>();

                foreach (var kv in _sessionIndex)
                {
                    var id = kv.Key;
                    var info = kv.Value;

                    if (info.MessageCount > 0)
                    {
                        continue;
                    }

                    var path = GetMessagesFilePath(id);
                    if (!File.Exists(path))
                    {
                        toDelete.Add(id);
                        continue;
                    }

                    try
                    {
                        var fileInfo = new FileInfo(path);
                        if (fileInfo.Length <= 4)
                        {
                            toDelete.Add(id);
                        }
                    }
                    catch
                    {
                        toDelete.Add(id);
                    }
                }

                foreach (var id in toDelete)
                {
                    DeleteSession(id);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] CleanupEmptySessions 失败: {ex.Message}");
            }
        }

        #endregion
    }

    public class SessionInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("MessageCount")] public int MessageCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Mode")] public string Mode { get; set; } = "0";
        [System.Text.Json.Serialization.JsonPropertyName("ContextChapterId")] public string? ContextChapterId { get; set; }
    }

    public class SerializedMessageRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("MessageId")] public string MessageId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Role")] public string Role { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Analysis")] public string? Analysis { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AnalysisKind")] public string? AnalysisKind { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DurationSeconds")] public double? DurationSeconds { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ChangesJson")] public string? ChangesJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ChangesDurationSeconds")] public double? ChangesDurationSeconds { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PayloadType")] public int PayloadType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PayloadJson")] public string? PayloadJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ReferencesJson")] public string? ReferencesJson { get; set; }
    }
}
