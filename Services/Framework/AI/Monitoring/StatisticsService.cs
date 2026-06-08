using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.AI.Monitoring;

public class StatisticsService : IAIUsageStatisticsService, IDisposable
{

    public static event Action<ApiCallRecord>? CallRecorded;

    private readonly List<ApiCallRecord> _records = new();
    private readonly object _lock = new();
    private readonly string _storagePath;
    private static readonly TimeSpan SaveThrottleInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly int RetentionDays = 3;
    private Timer? _dailyTrimTimer;
    private readonly Timer _saveThrottleTimer;

    public StatisticsService()
    {
        _storagePath = StoragePathHelper.GetFilePath("Services", "AI/Monitoring", "api_statistics.json");
        _saveThrottleTimer = new Timer(OnSaveThrottleElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        System.Threading.Tasks.Task.Run(async () => { await LoadRecordsAsync().ConfigureAwait(false); TrimExpiredRecords(); })
            .SafeFireAndForget(ex => TM.App.Log($"[StatisticsService] 初始化失败: {ex.Message}"));
        StartDailyTrimTimer();
    }

    private static bool IsTianmingPrivateProvider(string? providerId)
        => TM.Services.Framework.AI.Core.TianmingProviderIdentity.IsTianmingPrivate(providerId);

    private void StartDailyTrimTimer()
    {
        var now = DateTime.Now;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;

        _dailyTrimTimer = new Timer(_ =>
        {
            TrimExpiredRecords();
        }, null, delay, TimeSpan.FromDays(1));
    }

    public void Dispose()
    {
        _saveThrottleTimer.Dispose();
        _dailyTrimTimer?.Dispose();
        _saveLock.Dispose();
    }

    private void TrimExpiredRecords()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        int removed;

        lock (_lock)
        {
            var before = _records.Count;
            _records.RemoveAll(r => r.Timestamp < cutoff);
            removed = before - _records.Count;
        }

        if (removed > 0)
        {
            _saveThrottleTimer.Change(SaveThrottleInterval, Timeout.InfiniteTimeSpan);
            TM.App.Log($"[StatisticsService] 每日裁剪: 移除了 {removed} 条超过 {RetentionDays} 天的记录");
        }
    }

    public void RecordCall(string modelName, string provider, bool success, int responseTimeMs,
                          int inputTokens = 0, int outputTokens = 0, string? errorMessage = null)
    {
        try
        {
            var record = new ApiCallRecord
            {
                Timestamp = DateTime.Now,
                ModelName = modelName,
                Provider = provider,
                Success = success,
                ResponseTimeMs = responseTimeMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ErrorMessage = errorMessage
            };

            lock (_lock)
            {
                _records.Add(record);
            }

            _saveThrottleTimer.Change(SaveThrottleInterval, Timeout.InfiniteTimeSpan);

            var displayName = IsTianmingPrivateProvider(provider) ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : modelName;
            TM.App.Log($"[StatisticsService] 记录API调用: {displayName} - {(success ? "成功" : "失败")} - {responseTimeMs}ms");

            RaiseCallRecorded(record);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 记录失败: {ex.Message}");
        }
    }

    public void RecordCall(ApiCallRecord record)
    {
        if (record == null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(record.Id))
                record.Id = ShortIdGenerator.New("D");
            if (record.Timestamp == default)
                record.Timestamp = DateTime.Now;

            lock (_lock)
            {
                _records.Add(record);
            }

            _saveThrottleTimer.Change(SaveThrottleInterval, Timeout.InfiniteTimeSpan);

            var displayName = IsTianmingPrivateProvider(record.Provider) ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : record.ModelName;
            TM.App.Log($"[StatisticsService] 记录API调用: {displayName} - {(record.Success ? "成功" : "失败")} - {record.ResponseTimeMs}ms, in/out={record.InputTokens}/{record.OutputTokens}, TTFB={record.FirstTokenMs}ms, TPS={record.TokensPerSecond:F1}");

            RaiseCallRecorded(record);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 记录失败: {ex.Message}");
        }
    }

    private static void RaiseCallRecorded(ApiCallRecord record)
    {
        try
        {
            CallRecorded?.Invoke(record);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] CallRecorded事件处理异常: {ex.Message}");
        }
    }

    public StatisticsSummary GetSummary()
    {
        List<ApiCallRecord> snapshot;
        lock (_lock) { snapshot = _records.ToList(); }

        if (snapshot.Count == 0)
        {
            return new StatisticsSummary();
        }

        int total = snapshot.Count, success = 0;
        long responseSum = 0, inputSum = 0, outputSum = 0;
        long ttfbSum = 0; int ttfbCount = 0;
        double tpsSum = 0; int tpsCount = 0;
        long thinkingSum = 0;
        int toolCallSum = 0;
        var minTime = DateTime.MaxValue;
        var maxTime = DateTime.MinValue;

        foreach (var r in snapshot)
        {
            if (r.Success) success++;
            responseSum += r.ResponseTimeMs;
            inputSum += r.InputTokens;
            outputSum += r.OutputTokens;
            if (r.FirstTokenMs > 0) { ttfbSum += r.FirstTokenMs; ttfbCount++; }
            if (r.TokensPerSecond > 0) { tpsSum += r.TokensPerSecond; tpsCount++; }
            thinkingSum += r.ThinkingMs;
            toolCallSum += r.ToolCallCount;
            if (r.Timestamp < minTime) minTime = r.Timestamp;
            if (r.Timestamp > maxTime) maxTime = r.Timestamp;
        }

        return new StatisticsSummary
        {
            TotalCalls = total,
            SuccessCalls = success,
            FailedCalls = total - success,
            AverageResponseTime = (double)responseSum / total,
            TotalInputTokens = (int)inputSum,
            TotalOutputTokens = (int)outputSum,
            FirstCallTime = minTime,
            LastCallTime = maxTime,
            AverageFirstTokenMs = ttfbCount > 0 ? (double)ttfbSum / ttfbCount : 0,
            AverageTokensPerSecond = tpsCount > 0 ? tpsSum / tpsCount : 0,
            TotalThinkingMs = thinkingSum,
            TotalToolCalls = toolCallSum
        };
    }

    public IReadOnlyList<DailyStatistics> GetDailyStatistics(int days = 7)
    {
        var startDate = DateTime.Now.Date.AddDays(-days);

        List<ApiCallRecord> snapshot;
        lock (_lock) { snapshot = _records.ToList(); }
        var records = snapshot.Where(r => r.Timestamp >= startDate).ToList();

        var dailyGroups = records.GroupBy(r => r.Timestamp.Date);

        return dailyGroups.Select(g =>
        {
            int total = 0, success = 0;
            long responseSum = 0;
            foreach (var r in g)
            {
                total++;
                if (r.Success) success++;
                responseSum += r.ResponseTimeMs;
            }
            return new DailyStatistics
            {
                Date = g.Key,
                TotalCalls = total,
                SuccessCalls = success,
                FailedCalls = total - success,
                AverageResponseTime = total > 0 ? (double)responseSum / total : 0
            };
        })
        .OrderBy(d => d.Date)
        .ToList();
    }

    public IReadOnlyDictionary<string, StatisticsSummary> GetStatisticsByModel()
    {
        List<ApiCallRecord> snapshot;
        lock (_lock) { snapshot = _records.ToList(); }

        var result = new Dictionary<string, StatisticsSummary>();

        var groups = snapshot.GroupBy(r => r.ModelName);

        foreach (var group in groups)
        {
            int total = 0, success = 0, responseSum = 0, inputSum = 0, outputSum = 0;
            long ttfbSum = 0; int ttfbCount = 0;
            double tpsSum = 0; int tpsCount = 0;
            long thinkingSum = 0;
            int toolCallSum = 0;
            var firstTime = DateTime.MaxValue;
            var lastTime = DateTime.MinValue;
            foreach (var r in group)
            {
                total++;
                if (r.Success) success++;
                responseSum += r.ResponseTimeMs;
                inputSum += r.InputTokens;
                outputSum += r.OutputTokens;
                if (r.FirstTokenMs > 0) { ttfbSum += r.FirstTokenMs; ttfbCount++; }
                if (r.TokensPerSecond > 0) { tpsSum += r.TokensPerSecond; tpsCount++; }
                thinkingSum += r.ThinkingMs;
                toolCallSum += r.ToolCallCount;
                if (r.Timestamp < firstTime) firstTime = r.Timestamp;
                if (r.Timestamp > lastTime) lastTime = r.Timestamp;
            }
            result[group.Key] = new StatisticsSummary
            {
                TotalCalls = total,
                SuccessCalls = success,
                FailedCalls = total - success,
                AverageResponseTime = total > 0 ? (double)responseSum / total : 0,
                TotalInputTokens = inputSum,
                TotalOutputTokens = outputSum,
                FirstCallTime = total > 0 ? firstTime : default,
                LastCallTime = total > 0 ? lastTime : default,
                AverageFirstTokenMs = ttfbCount > 0 ? (double)ttfbSum / ttfbCount : 0,
                AverageTokensPerSecond = tpsCount > 0 ? tpsSum / tpsCount : 0,
                TotalThinkingMs = thinkingSum,
                TotalToolCalls = toolCallSum
            };
        }

        return result;
    }

    public IReadOnlyList<ApiCallRecord> GetRecentRecords(int count = 50)
    {
        lock (_lock)
        {
            var skip = Math.Max(0, _records.Count - count);
            var result = new List<ApiCallRecord>(Math.Min(count, _records.Count));
            for (int i = _records.Count - 1; i >= skip; i--)
                result.Add(_records[i]);
            return result;
        }
    }

    public System.Collections.Generic.IReadOnlyList<ApiCallRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    public void ClearStatistics()
    {
        lock (_lock) { _records.Clear(); }
        _saveThrottleTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        TM.App.Log("[StatisticsService] 统计数据已清空");
    }

    private async System.Threading.Tasks.Task LoadRecordsAsync()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = await File.ReadAllTextAsync(_storagePath).ConfigureAwait(false);
                var records = JsonSerializer.Deserialize<List<ApiCallRecord>>(json);
                if (records != null)
                {
                    lock (_lock)
                    {
                        _records.Clear();
                        _records.AddRange(records);
                    }
                    TM.App.Log($"[StatisticsService] 加载了 {records.Count} 条统计记录");
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 加载统计数据失败: {ex.Message}");
        }
    }

    private async void OnSaveThrottleElapsed(object? state)
    {
        try
        {
            await SaveRecordsCore().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] SaveRecords异常: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveRecordsCore()
    {
        var acquired = false;
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<ApiCallRecord> snapshot;
            lock (_lock) { snapshot = _records.ToList(); }
            string json = JsonSerializer.Serialize(snapshot, JsonHelper.CnDefault);

            await _saveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            var tmp = _storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _storagePath, overwrite: true);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 保存统计数据失败: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _saveLock.Release();
        }
    }
}
