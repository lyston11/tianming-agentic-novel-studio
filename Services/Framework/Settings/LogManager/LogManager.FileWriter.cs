using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.Logging.LogLevel;
using TM.Framework.SystemSettings.Logging.LogRotation;

namespace TM.Services.Framework.Settings
{
    public partial class LogManager
    {
        private void WriteToFile(LogLevelEnum level, string formatted)
        {
            var entry = new LogEntry(level, formatted);
            if (level >= LogLevelEnum.Warning)
            {
                _highPriorityChannel.Writer.TryWrite(entry);
                return;
            }

            if (!_lowPriorityChannel.Writer.TryWrite(entry))
            {
                Interlocked.Increment(ref _droppedLowPriority);
            }
        }

        private async Task BackgroundWriterLoopAsync()
        {
            StreamWriter? writer = null;
            string? currentFilePath = null;
            var pendingLines = 0;

            try
            {
                while (true)
                {
                    while (_highPriorityChannel.Reader.TryRead(out var hp))
                    {
                        if (!TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            continue;
                        writer!.WriteLine(hp.Formatted);
                        FlushAndMaybeRotate(ref writer, ref currentFilePath, ref pendingLines);
                    }

                    while (_lowPriorityChannel.Reader.TryRead(out var lp))
                    {
                        if (!TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            continue;
                        writer!.WriteLine(lp.Formatted);
                        FlushAndMaybeRotate(ref writer, ref currentFilePath, ref pendingLines);
                    }

                    var dropped = Interlocked.Exchange(ref _droppedLowPriority, 0);
                    if (dropped > 0)
                    {
                        var now = DateTime.Now;
                        if ((now - _lastDropReport).TotalSeconds >= 5)
                        {
                            _lastDropReport = now;
                            if (TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            {
                                var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [WRN] [LogManager] 低优先级日志队列已满，已丢弃 {dropped} 条日志";
                                writer!.WriteLine(line);
                                FlushAndMaybeRotate(ref writer, ref currentFilePath, ref pendingLines);
                            }
                        }
                        else
                        {
                            Interlocked.Add(ref _droppedLowPriority, dropped);
                        }
                    }

                    if (_highPriorityChannel.Reader.Completion.IsCompleted
                        && _lowPriorityChannel.Reader.Completion.IsCompleted
                        && !_highPriorityChannel.Reader.TryPeek(out _)
                        && !_lowPriorityChannel.Reader.TryPeek(out _))
                    {
                        break;
                    }

                    var hpWait = _highPriorityChannel.Reader.WaitToReadAsync(_writerCts.Token).AsTask();
                    var lpWait = _lowPriorityChannel.Reader.WaitToReadAsync(_writerCts.Token).AsTask();
                    var idleFlush = Task.Delay(500, _writerCts.Token);
                    var completed = await Task.WhenAny(hpWait, lpWait, idleFlush).ConfigureAwait(false);

                    if (completed == idleFlush)
                    {
                        if (_writerCts.IsCancellationRequested) break;
                        if (writer != null && pendingLines > 0)
                        {
                            try { writer.Flush(); pendingLines = 0; } catch { }
                        }
                    }
                    else
                    {
                        try { await completed.ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                try { writer?.Flush(); writer?.Dispose(); } catch { }
            }
        }

        private bool TryEnsureWriter(ref StreamWriter? writer, ref string? currentFilePath, ref int pendingLines)
        {
            try
            {
                var filePath = ResolveLogFilePath();
                if (filePath == null) return false;

                if (filePath != currentFilePath)
                {
                    writer?.Flush();
                    writer?.Dispose();
                    writer = null;
                    currentFilePath = filePath;
                    pendingLines = 0;
                }

                if (writer != null) return true;

                bool firstSeen = false;
                lock (_clearedFilesThisSession)
                {
                    firstSeen = _clearedFilesThisSession.Add(filePath);
                }
                if (firstSeen)
                {
                    try
                    {
                        var fi = new FileInfo(filePath);
                        var maxBytes = Math.Max(1, _rotationSettings.MaxFileSizeMB) * 1024L * 1024L;
                        if (fi.Exists && fi.Length > maxBytes)
                        {
                            try
                            {
                                var rotated = GetRotatedPath(filePath);
                                File.Move(filePath, rotated);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                writer = CreateBufferedWriter(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static StreamWriter CreateBufferedWriter(string filePath)
        {
            var fs = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 16 * 1024);
            return new StreamWriter(fs, Encoding.UTF8, bufferSize: 4096) { AutoFlush = false };
        }

        private static string GetRotatedPath(string originalPath)
        {
            var dir = Path.GetDirectoryName(originalPath) ?? string.Empty;
            var nameNoExt = Path.GetFileNameWithoutExtension(originalPath);
            var ext = Path.GetExtension(originalPath);

            for (int seq = 1; seq <= 999; seq++)
            {
                var candidate = Path.Combine(dir, $"{nameNoExt}.{seq:D3}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, $"{nameNoExt}.{DateTime.Now:HHmmssfff}{ext}");
        }

        private void FlushAndMaybeRotate(ref StreamWriter? writer, ref string? currentFilePath, ref int pendingLines)
        {
            pendingLines++;
            if (pendingLines < 32 || writer == null) return;

            try { writer.Flush(); } catch { }
            pendingLines = 0;

            if (!_rotationSettings.EnableSizeRotation
                || _rotationSettings.MaxFileSizeMB <= 0
                || currentFilePath == null
                || writer.BaseStream is not FileStream fs)
                return;

            var threshold = _rotationSettings.MaxFileSizeMB * 1024L * 1024L;
            if (fs.Position < threshold) return;

            try
            {
                writer.Dispose();
                writer = null;
                var rotated = GetRotatedPath(currentFilePath);
                File.Move(currentFilePath, rotated);
            }
            catch
            {
            }
        }

        private string? ResolveLogFilePath()
        {
            try
            {
                if (_resolvedLogDir == null)
                {
                    var configuredPath = _outputSettings.FileOutputPath;
                    if (string.IsNullOrWhiteSpace(configuredPath))
                        configuredPath = "Logs/application.log";

                    var dir = Path.GetDirectoryName(configuredPath);
                    if (string.IsNullOrWhiteSpace(dir))
                        dir = "Logs";

                    if (!Path.IsPathRooted(dir))
                        dir = Path.Combine(StoragePathHelper.GetStorageRoot(), dir);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    _resolvedLogDir = dir;
                }

                var pattern = _outputSettings.FileNamingPattern;
                if (string.IsNullOrWhiteSpace(pattern))
                    pattern = "{date}_{appname}.log";

                var fileName = pattern
                    .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                    .Replace("{appname}", "天命");

                return Path.Combine(_resolvedLogDir, fileName);
            }
            catch
            {
                return null;
            }
        }

        private bool ShouldWrite(LogLevelEnum level, string? module)
        {
            var threshold = _levelSettings.GlobalLevel;

            if (!string.IsNullOrWhiteSpace(module) && _levelSettings.ModuleLevels.TryGetValue(module, out var moduleLevel))
            {
                threshold = moduleLevel;
            }

            if (threshold < _levelSettings.MinimumLevel)
            {
                threshold = _levelSettings.MinimumLevel;
            }

            return level >= threshold;
        }

        private string Format(LogLevelEnum level, string? module, string message, Exception? ex)
        {
            if (_useFastFormat)
                return FormatFast(level, module, message, ex);

            var template = string.IsNullOrWhiteSpace(_formatSettings.FormatTemplate)
                ? "[{timestamp}] [{level}] [{caller}] {message}"
                : _formatSettings.FormatTemplate;

            var now = DateTime.Now;

            var tokens = _activeFormatTokens;

            if (tokens.Contains("timestamp"))
                template = TimestampRegex().Replace(template, m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : _formatSettings.TimestampFormat;
                    if (string.IsNullOrWhiteSpace(fmt))
                        fmt = "yyyy-MM-dd HH:mm:ss.fff";
                    return now.ToString(fmt);
                });

            if (tokens.Contains("level"))
                template = LevelRegex().Replace(template, m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
                    return string.Equals(fmt, "short", StringComparison.OrdinalIgnoreCase)
                        ? ToShortLevel(level)
                        : level.ToString().ToUpperInvariant();
                });

            if (tokens.Contains("message")) template = MessageRegex().Replace(template, _ => message ?? string.Empty);
            if (tokens.Contains("caller"))
            {
                if (!string.IsNullOrWhiteSpace(module))
                    template = CallerRegex().Replace(template, _ => module);
                else
                    template = template.Replace("[{caller}] ", "").Replace("[{caller}]", "");
            }
            if (tokens.Contains("threadid")) template = ThreadIdRegex().Replace(template, _ => Environment.CurrentManagedThreadId.ToString());
            if (tokens.Contains("processid")) template = ProcessIdRegex().Replace(template, _ => Environment.ProcessId.ToString());
            if (tokens.Contains("exception")) template = ExceptionRegex().Replace(template, _ => ex?.ToString() ?? string.Empty);

            return template;
        }

        [ThreadStatic] private static StringBuilder? t_fmtBuf;

        private string FormatFast(LogLevelEnum level, string? module, string message, Exception? ex)
        {
            var sb = t_fmtBuf ??= new StringBuilder(256);
            sb.Clear();

            var now = DateTime.Now;
            var totalMinutes = (int)now.TimeOfDay.TotalMinutes;
            var second = now.Second;
            var prevMin = _prevTotalMinutes;
            var prevSec = _prevSecond;
            var prevTicks = Interlocked.Read(ref _prevLogTimeTicks);
            var gapSec = prevTicks > 0 ? (now.Ticks - prevTicks) / TimeSpan.TicksPerSecond : long.MaxValue;

            if (prevMin < 0 || totalMinutes != prevMin || gapSec > 60)
            {
                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            else if (second != prevSec)
            {
                sb.Append(':');
                sb.Append(second.ToString("D2"));
                sb.Append('.');
                sb.Append(now.Millisecond.ToString("D3"));
            }
            else
            {
                sb.Append('.');
                sb.Append(now.Millisecond.ToString("D3"));
            }

            _prevTotalMinutes = totalMinutes;
            _prevSecond = second;
            Interlocked.Exchange(ref _prevLogTimeTicks, now.Ticks);

            sb.Append(' ');
            var li = (int)level;
            sb.Append(li >= 0 && li < _levelChars.Length ? _levelChars[li] : '?');
            sb.Append(' ');

            if (!string.IsNullOrWhiteSpace(module))
            {
                sb.Append(module);
                sb.Append("| ");
            }

            sb.Append(message);
            if (ex != null)
            {
                sb.AppendLine();
                sb.Append(ex);
            }

            var result = sb.ToString();

            if (sb.Capacity > 4096)
            {
                sb.Capacity = 512;
            }

            return result;
        }

        private static string ToShortLevel(LogLevelEnum level)
        {
            return level switch
            {
                LogLevelEnum.Trace => "TRC",
                LogLevelEnum.Debug => "DBG",
                LogLevelEnum.Info => "INF",
                LogLevelEnum.Warning => "WRN",
                LogLevelEnum.Error => "ERR",
                LogLevelEnum.Fatal => "FTL",
                _ => level.ToString().ToUpperInvariant()
            };
        }

        private static (string? Module, string Message) TryParseModule(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return (null, string.Empty);
            }

            if (message[0] != '[')
            {
                return (null, message);
            }

            int end = message.IndexOf(']', 1);
            if (end < 2)
            {
                return (null, message);
            }

            var module = message.Substring(1, end - 1).Trim();
            if (string.IsNullOrEmpty(module))
            {
                return (null, message);
            }

            int msgStart = end + 1;
            while (msgStart < message.Length && char.IsWhiteSpace(message[msgStart]))
                msgStart++;

            var msg = msgStart >= message.Length ? string.Empty : message.Substring(msgStart);
            return (module, msg);
        }

        private static LogLevelEnum GuessLevel(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("Fatal", StringComparison.OrdinalIgnoreCase) || message.Contains("致命", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Fatal;
            }

            if (FailureZeroRegex().IsMatch(message)
                || SuccessFailureCountRegex().IsMatch(message))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("TTS不可用", StringComparison.OrdinalIgnoreCase)
                || message.Contains("No voice installed", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("quality-warn:", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("warn:", StringComparison.OrdinalIgnoreCase)
                    && message.Contains(", pass)", StringComparison.OrdinalIgnoreCase)))
            {
                return LogLevelEnum.Info;
            }

            if (message.StartsWith("归一化补丁", StringComparison.Ordinal)
                || message.StartsWith("归一化歧义", StringComparison.Ordinal)
                || message.StartsWith("已添加情节索引", StringComparison.Ordinal)
                || message.StartsWith("已更新 ", StringComparison.Ordinal)
                || message.Contains("向量召回", StringComparison.Ordinal) && message.Contains("片段", StringComparison.Ordinal))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("警告", StringComparison.Ordinal)
                && message.Contains("0 条", StringComparison.Ordinal)
                && !message.Contains("1 条", StringComparison.Ordinal)
                && !message.Contains("2 条", StringComparison.Ordinal)
                && !message.Contains("3 条", StringComparison.Ordinal))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("心跳失败", StringComparison.Ordinal))
            {
                return LogLevelEnum.Warning;
            }

            if (message.Contains("回退到", StringComparison.Ordinal)
                || message.Contains("已回退", StringComparison.Ordinal)
                || message.Contains("回退至", StringComparison.Ordinal))
            {
                return LogLevelEnum.Warning;
            }

            if (message.Contains("失败", StringComparison.OrdinalIgnoreCase) || message.Contains("错误", StringComparison.OrdinalIgnoreCase) || message.Contains("Error", StringComparison.OrdinalIgnoreCase) || message.Contains("异常", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Error;
            }

            if (message.Contains("Warn", StringComparison.OrdinalIgnoreCase) || message.Contains("警告", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Warning;
            }

            if (ContainsAnyKeyword(message, _debugKeywordsIgnoreCase, StringComparison.OrdinalIgnoreCase)
                || ContainsAnyKeyword(message, _debugKeywordsOrdinal, StringComparison.Ordinal)
                || (message.Contains("已注册 ", StringComparison.OrdinalIgnoreCase) && message.Contains(" 个 Plugin", StringComparison.OrdinalIgnoreCase))
                || (message.Contains("加载成功:", StringComparison.Ordinal) && message.Contains("章,", StringComparison.Ordinal))
                || (message.Contains("找到 ", StringComparison.Ordinal) && message.Contains("个输出设备", StringComparison.Ordinal))
                || (message.Contains("找到 ", StringComparison.Ordinal) && message.Contains("个输入设备", StringComparison.Ordinal))
                || (message.Contains("已加载 ", StringComparison.Ordinal) && message.Contains(" 个系统字体", StringComparison.Ordinal))
                || (message.Contains("筛选完成:", StringComparison.Ordinal) && message.Contains("个字体", StringComparison.Ordinal))
                || (message.Contains("检测到 ", StringComparison.Ordinal) && message.Contains(" 个显示器", StringComparison.Ordinal))
                || (message.Contains("加载", StringComparison.Ordinal) && message.Contains("条历史消息（三层架构）", StringComparison.Ordinal))
                || (message.Contains("已加载 ", StringComparison.Ordinal) && message.Contains(" 个活动会话", StringComparison.Ordinal))
                || (message.Contains("已加载 ", StringComparison.Ordinal) && message.Contains(" 个主题", StringComparison.Ordinal))
                || (message.Contains("发现 ", StringComparison.Ordinal) && message.Contains(" 个自定义主题", StringComparison.Ordinal))
                || (message.Contains("加载了 ", StringComparison.Ordinal) && message.Contains(" 条通知历史", StringComparison.Ordinal))
                || (message.Contains("加载了 ", StringComparison.Ordinal) && message.Contains(" 条登录记录", StringComparison.Ordinal))
                || (message.Contains("获取到 ", StringComparison.Ordinal) && message.Contains(" 条激活历史", StringComparison.Ordinal))
                || (message.Contains("从服务器获取到 ", StringComparison.Ordinal) && message.Contains(" 个绑定", StringComparison.Ordinal))
                || (message.Contains("服务器记录=", StringComparison.Ordinal) && message.Contains("本地记录=", StringComparison.Ordinal)))
            {
                return LogLevelEnum.Debug;
            }

            if ((message.Contains("-> 200", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("-> 201", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("-> 204", StringComparison.OrdinalIgnoreCase))
                && (message.Contains("GET ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("POST ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("PUT ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)))
            {
                return LogLevelEnum.Debug;
            }

            if (message.Contains("Debug", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Debug;
            }

            if (message.Contains("Trace", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Trace;
            }

            return LogLevelEnum.Info;
        }

        private string? ResolveLogDir()
        {
            try
            {
                var configuredPath = _outputSettings.FileOutputPath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                    configuredPath = "Logs/application.log";

                var dir = Path.GetDirectoryName(configuredPath);
                if (string.IsNullOrWhiteSpace(dir)) dir = "Logs";
                if (!Path.IsPathRooted(dir))
                    dir = Path.Combine(StoragePathHelper.GetStorageRoot(), dir);

                return dir;
            }
            catch
            {
                return null;
            }
        }

        private string ResolveArchiveDir()
        {
            var archive = _rotationSettings.ArchivePath;
            if (string.IsNullOrWhiteSpace(archive))
                archive = "Logs/Archive";

            if (Path.IsPathRooted(archive))
                return archive;

            return Path.Combine(StoragePathHelper.GetStorageRoot(), archive);
        }

        private void CompressOldLogFiles()
        {
            try
            {
                if (!_rotationSettings.EnableCompression) return;
                if (_rotationSettings.CompressionType == CompressionType.None) return;

                var dir = ResolveLogDir();
                if (dir == null || !Directory.Exists(dir)) return;

                var archiveDir = ResolveArchiveDir();
                var compressAfterDays = _rotationSettings.CompressAfterDays > 0 ? _rotationSettings.CompressAfterDays : 1;
                var cutoff = DateTime.Now.AddDays(-compressAfterDays);

                var todayPath = ResolveLogFilePath();
                var candidates = Directory.GetFiles(dir, "*.log")
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTime < cutoff
                                && !string.Equals(f.FullName, todayPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (candidates.Count == 0) return;

                try { Directory.CreateDirectory(archiveDir); } catch { }

                int compressed = 0;
                foreach (var f in candidates)
                {
                    try
                    {
                        if (TryCompressFile(f, archiveDir, _rotationSettings.CompressionType))
                        {
                            try { f.Delete(); compressed++; }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce($"CompressFile:{f.Name}", ex);
                    }
                }

                if (compressed > 0)
                    Debug.WriteLine($"[LogManager] 启动压缩：已压缩 {compressed} 个旧日志文件（类型={_rotationSettings.CompressionType}, 阈值={compressAfterDays}天）");
            }
            catch (Exception ex)
            {
                DebugLogOnce("CompressOldLogs", ex);
            }
        }

        private static bool TryCompressFile(FileInfo source, string archiveDir, CompressionType type)
        {
            if (type == CompressionType.None) return false;

            var ext = type == CompressionType.ZIP ? ".zip" : ".gz";
            var target = Path.Combine(archiveDir, source.Name + ext);

            if (File.Exists(target))
            {
                return true;
            }

            var tempTarget = target + ".tmp";
            try
            {
                if (type == CompressionType.ZIP)
                {
                    using (var zipStream = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        var entry = archive.CreateEntry(source.Name, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        using var sourceStream = source.OpenRead();
                        sourceStream.CopyTo(entryStream);
                    }
                }
                else
                {
                    using var sourceStream = source.OpenRead();
                    using var targetStream = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var gzipStream = new GZipStream(targetStream, CompressionLevel.Optimal);
                    sourceStream.CopyTo(gzipStream);
                }

                File.Move(tempTarget, target);
                return true;
            }
            catch
            {
                try { if (File.Exists(tempTarget)) File.Delete(tempTarget); } catch { }
                return false;
            }
        }

        private static IEnumerable<FileInfo> SafeEnumerateFiles(string? directory, string pattern, Func<FileInfo, bool>? filter)
        {
            if (string.IsNullOrWhiteSpace(directory)) return Enumerable.Empty<FileInfo>();
            try
            {
                if (!Directory.Exists(directory)) return Enumerable.Empty<FileInfo>();
                var query = Directory.GetFiles(directory, pattern).Select(f => new FileInfo(f));
                if (filter != null) query = query.Where(filter);
                return query.ToList();
            }
            catch
            {
                return Enumerable.Empty<FileInfo>();
            }
        }

        private void CleanupOldLogFiles()
        {
            try
            {
                if (!_rotationSettings.EnableAutoCleanup) return;

                var dir = ResolveLogDir();
                if (dir == null) return;

                var archiveDir = ResolveArchiveDir();

                var logFiles = SafeEnumerateFiles(dir, "*.log", null);
                var archiveFiles = SafeEnumerateFiles(archiveDir, "*.*",
                    f => string.Equals(f.Extension, ".zip", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(f.Extension, ".gz", StringComparison.OrdinalIgnoreCase));

                var todayPath = ResolveLogFilePath();
                var allFiles = logFiles.Concat(archiveFiles)
                    .Where(f => !string.Equals(f.FullName, todayPath, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                if (allFiles.Count == 0) return;

                var maxDays = _rotationSettings.MaxRetainDays > 0 ? _rotationSettings.MaxRetainDays : 1;
                var maxCount = _rotationSettings.MaxRetainCount > 0 ? _rotationSettings.MaxRetainCount : 1;
                var maxSizeBytes = _rotationSettings.MaxRetainSizeMB > 0
                    ? _rotationSettings.MaxRetainSizeMB * 1024L * 1024L
                    : long.MaxValue;
                var cutoff = DateTime.Now.AddDays(-maxDays);

                long cumulative = 0;
                int deleted = 0;
                for (int i = 0; i < allFiles.Count; i++)
                {
                    var f = allFiles[i];
                    cumulative += f.Length;

                    bool shouldDelete = _rotationSettings.CleanupStrategy switch
                    {
                        CleanupStrategy.ByTime => f.LastWriteTime < cutoff,
                        CleanupStrategy.ByCount => i >= maxCount,
                        // BySize 策略：至少保留 1 份最新历史（i >= 1），其余按累积容量裁剪
                        CleanupStrategy.BySize => i >= 1 && cumulative > maxSizeBytes,
                        _ => f.LastWriteTime < cutoff
                    };

                    if (shouldDelete)
                    {
                        try { f.Delete(); deleted++; }
                        catch { }
                    }
                }

                if (deleted > 0)
                    Debug.WriteLine($"[LogManager] 启动清理：删除 {deleted} 个旧日志文件（策略={_rotationSettings.CleanupStrategy}, 保留{maxDays}天/{maxCount}个/{_rotationSettings.MaxRetainSizeMB}MB）");
            }
            catch (Exception ex)
            {
                DebugLogOnce("CleanupOldLogs", ex);
            }
        }

        private static async System.Threading.Tasks.Task<T> LoadJsonOrDefaultAsync<T>(string path, T defaultValue) where T : class
        {
            try
            {
                if (!File.Exists(path)) return defaultValue;
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
            }
            catch (Exception ex)
            {
                DebugLogOnce($"LoadConfig:{path}", ex);
                return defaultValue;
            }
        }
    }
}

