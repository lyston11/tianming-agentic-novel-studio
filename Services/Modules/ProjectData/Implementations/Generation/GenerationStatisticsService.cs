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
    public class GenerationStatisticsService
    {
        #region 构造函数

        public GenerationStatisticsService()
        {
            LoadStatisticsAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationStats] 初始化失败: {ex.Message}"));
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    Interlocked.Increment(ref _statisticsEpoch);
                    ResetInMemoryStatistics();
                    LoadStatisticsAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationStats] 项目切换重载失败: {ex.Message}"));
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationStats] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        #endregion

        #region 字段

        private GenerationStatistics _statistics = new();
        private readonly List<GenerationRecord> _recentRecords = new();
        private readonly object _statisticsLock = new();
        private readonly object _recordsLock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _statisticsEpoch;
        private const int MaxRecentRecords = 100;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        #endregion

        #region 公开方法

        public void RecordGeneration(GenerationResult result)
        {
            var rewriteCount = result.TotalAttempts - 1;

            var record = new GenerationRecord
            {
                ChapterId = result.ChapterId,
                Success = result.Success,
                TotalAttempts = result.TotalAttempts,
                RewriteCount = rewriteCount,
                RequiresManualIntervention = result.RequiresManualIntervention,
                FailureReasons = result.GetLastFailureReasons()
            };

            foreach (var attempt in result.Attempts)
            {
                var attemptRecord = new AttemptRecord
                {
                    AttemptNumber = attempt.AttemptNumber,
                    Success = attempt.Success,
                    FailureReasons = attempt.FailureReasons
                };

                if (!attempt.Success && attempt.FailureReasons.Count > 0)
                {
                    var firstReason = attempt.FailureReasons.FirstOrDefault() ?? "";
                    if (firstReason.Contains("[Protocol]"))
                        attemptRecord.FailureType = "Protocol";
                    else if (firstReason.Contains("[Consistency]"))
                        attemptRecord.FailureType = "Consistency";

                    if (!string.IsNullOrEmpty(attemptRecord.FailureType))
                        record.FailureStages.Add($"Attempt{attempt.AttemptNumber}:{attemptRecord.FailureType}");
                }

                record.Attempts.Add(attemptRecord);
            }

            string logSummary;
            lock (_statisticsLock)
            {
                _statistics.TotalGenerations++;

                if (result.Success)
                {
                    if (result.TotalAttempts == 1)
                        _statistics.FirstPassCount++;
                    else
                        _statistics.RewritePassCount++;
                }
                else if (result.RequiresManualIntervention)
                {
                    _statistics.FinalFailureCount++;
                }

                if (!_statistics.RewriteDistribution.ContainsKey(rewriteCount))
                    _statistics.RewriteDistribution[rewriteCount] = 0;
                _statistics.RewriteDistribution[rewriteCount]++;

                foreach (var attempt in result.Attempts)
                {
                    if (!attempt.Success && attempt.FailureReasons.Count > 0)
                    {
                        var firstReason = attempt.FailureReasons.FirstOrDefault() ?? "";
                        if (firstReason.Contains("[Protocol]"))
                            _statistics.ProtocolFailureCount++;
                        else if (firstReason.Contains("[Consistency]"))
                        {
                            _statistics.ConsistencyFailureCount++;
                            ParseConsistencyIssues(attempt.FailureReasons);
                        }
                    }
                }

                _statistics.EndTime = DateTime.Now;
                logSummary = $"成功={result.Success}, 尝试={result.TotalAttempts}, 首次通过率={_statistics.FirstPassRate:F1}%, 最终通过率={_statistics.FinalPassRate:F1}%";
            }

            lock (_recordsLock)
            {
                _recentRecords.Add(record);
                if (_recentRecords.Count > MaxRecentRecords)
                    _recentRecords.RemoveRange(0, _recentRecords.Count - MaxRecentRecords);
            }

            TM.App.Log($"[GenerationStats] 记录生成: {result.ChapterId}, {logSummary}");

            _ = SaveStatisticsAsync();
        }

        public void RecordConsistencyIssue(string issueType)
        {
            lock (_statisticsLock)
            {
                switch (issueType)
                {
                    case "CharacterStateConflict":
                        _statistics.ConsistencyIssues.CharacterStateConflict++;
                        break;
                    case "ForeshadowingEarlyPayoff":
                    case "PayoffBeforeSetup":
                        _statistics.ConsistencyIssues.ForeshadowingEarlyPayoff++;
                        break;
                    case "ForeshadowingRollback":
                        _statistics.ConsistencyIssues.ForeshadowingRollback++;
                        break;
                    case "ConflictStatusSkip":
                        _statistics.ConsistencyIssues.ConflictStatusSkip++;
                        break;
                    case "CharacterNotInvolved":
                        _statistics.ConsistencyIssues.CharacterNotInvolved++;
                        break;
                }
            }
        }

        public GenerationStatistics GetStatistics() { lock (_statisticsLock) { return _statistics; } }

        public List<GenerationRecord> GetRecentRecords(int count = 10)
        {
            lock (_recordsLock)
            {
                return _recentRecords.TakeLast(count).ToList();
            }
        }

        public void ResetStatistics()
        {
            lock (_statisticsLock)
            {
                _statistics = new GenerationStatistics();
            }
            lock (_recordsLock)
            {
                _recentRecords.Clear();
            }
            TM.App.Log("[GenerationStats] 统计数据已重置");
            _ = SaveStatisticsAsync();
        }

        public string GetStatisticsSummary()
        {
            lock (_statisticsLock)
            {
                return $"生成统计: 总数={_statistics.TotalGenerations}, " +
                    $"首次通过={_statistics.FirstPassCount}({_statistics.FirstPassRate:F1}%), " +
                    $"重写通过={_statistics.RewritePassCount}, " +
                    $"最终失败={_statistics.FinalFailureCount}, " +
                    $"协议失败={_statistics.ProtocolFailureCount}, " +
                    $"一致性失败={_statistics.ConsistencyFailureCount}";
            }
        }

        #endregion

        #region 私有方法

        private void ParseConsistencyIssues(List<string> reasons)
        {
            foreach (var reason in reasons)
            {
                if (reason.Contains("PayoffBeforeSetup") || reason.Contains("提前揭示"))
                {
                    _statistics.ConsistencyIssues.ForeshadowingEarlyPayoff++;
                }
                else if (reason.Contains("ForeshadowingRollback") || reason.Contains("回退"))
                {
                    _statistics.ConsistencyIssues.ForeshadowingRollback++;
                }
                else if (reason.Contains("ConflictStatusSkip") || reason.Contains("跳级"))
                {
                    _statistics.ConsistencyIssues.ConflictStatusSkip++;
                }
                else if (reason.Contains("CharacterNotInvolved") || reason.Contains("未登记"))
                {
                    _statistics.ConsistencyIssues.CharacterNotInvolved++;
                }
                else if (reason.Contains("CharacterState") || reason.Contains("角色状态"))
                {
                    _statistics.ConsistencyIssues.CharacterStateConflict++;
                }
            }
        }

        private async Task LoadStatisticsAsync()
        {
            var acquired = false;
            var epoch = Volatile.Read(ref _statisticsEpoch);
            try
            {
                await _saveLock.WaitAsync().ConfigureAwait(false);
                acquired = true;

                var path = GetStatisticsFilePath();
                GenerationStatistics loaded;
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    loaded = JsonSerializer.Deserialize<GenerationStatistics>(json, JsonOptions)
                        ?? new GenerationStatistics();
                }
                else
                {
                    loaded = new GenerationStatistics();
                }

                if (epoch == Volatile.Read(ref _statisticsEpoch))
                {
                    lock (_statisticsLock) { _statistics = loaded; }
                    TM.App.Log($"[GenerationStats] 已加载统计数据: {GetStatisticsSummary()}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationStats] 加载统计数据失败: {ex.Message}");
                if (epoch == Volatile.Read(ref _statisticsEpoch))
                    ResetInMemoryStatistics();
            }
            finally
            {
                if (acquired)
                    _saveLock.Release();
            }
        }

        private void ResetInMemoryStatistics()
        {
            lock (_statisticsLock)
            {
                _statistics = new GenerationStatistics();
            }

            lock (_recordsLock)
            {
                _recentRecords.Clear();
            }
        }

        private async Task SaveStatisticsAsync()
        {
            var acquired = false;
            try
            {
                await _saveLock.WaitAsync().ConfigureAwait(false);
                acquired = true;

                var epoch = Volatile.Read(ref _statisticsEpoch);
                var path = GetStatisticsFilePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json;
                lock (_statisticsLock)
                {
                    json = JsonSerializer.Serialize(_statistics, JsonOptions);
                }

                if (epoch != Volatile.Read(ref _statisticsEpoch))
                    return;

                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmp))
                {
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(json).ConfigureAwait(false);
                }
                if (epoch == Volatile.Read(ref _statisticsEpoch))
                    File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationStats] 保存统计数据失败: {ex.Message}");
            }
            finally
            {
                if (acquired)
                    _saveLock.Release();
            }
        }

        private string GetStatisticsFilePath()
        {
            return Path.Combine(
                StoragePathHelper.GetProjectConfigPath(),
                "generation_statistics.json");
        }

        #endregion
    }
}
