using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Agent 架构修复性能监控指标收集器
/// </summary>
public sealed class AgentArchitectureMetrics
{
    private static readonly object _lock = new();
    private static AgentArchitectureMetrics? _instance;

    // #25 智能终止指标
    public long EarlyTerminationsByNoProgress { get; private set; }
    public long EarlyTerminationsByConsecutiveFailures { get; private set; }
    public long TokensSavedByEarlyTermination { get; private set; }

    // #30 ChatHistory 压缩指标
    public long ToolExecutionHistoryOverflowCount { get; private set; }
    public long ToolExecutionHistoryMaxSize { get; private set; }
    public long ToolExecutionHistoryCurrentSize { get; private set; }

    // #31 中断优先级指标
    public Dictionary<string, long> InterruptPriorityDistribution { get; } = new();

    // #33 Memory 同步指标
    public long MemoryRefreshCount { get; private set; }
    public long MemoryRefreshFailureCount { get; private set; }
    public TimeSpan TotalMemoryRefreshDuration { get; private set; }

    // #28 工具去重指标
    public long ToolDeduplicationHits { get; private set; }
    public long ToolDeduplicationMisses { get; private set; }

    // #36 向量检索性能指标
    public long VectorRetrievalCount { get; private set; }
    public long VectorRetrievalFailureCount { get; private set; }
    public TimeSpan TotalVectorRetrievalDuration { get; private set; }
    public long VectorRetrievalSlowCount { get; private set; } // >100ms

    public static AgentArchitectureMetrics Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AgentArchitectureMetrics();
                }
            }
            return _instance;
        }
    }

    private AgentArchitectureMetrics()
    {
        InterruptPriorityDistribution["stop"] = 0;
        InterruptPriorityDistribution["direction_change"] = 0;
        InterruptPriorityDistribution["supplement"] = 0;
        InterruptPriorityDistribution["status"] = 0;
        InterruptPriorityDistribution["freeform"] = 0;
    }

    /// <summary>
    /// 记录智能终止（无进展）
    /// </summary>
    public void RecordEarlyTerminationByNoProgress(int stepsSkipped)
    {
        lock (_lock)
        {
            EarlyTerminationsByNoProgress++;
            // 假设每步平均消耗 500 tokens
            TokensSavedByEarlyTermination += stepsSkipped * 500;
        }
    }

    /// <summary>
    /// 记录智能终止（连续失败）
    /// </summary>
    public void RecordEarlyTerminationByFailures(int stepsSkipped)
    {
        lock (_lock)
        {
            EarlyTerminationsByConsecutiveFailures++;
            TokensSavedByEarlyTermination += stepsSkipped * 500;
        }
    }

    /// <summary>
    /// 记录 ToolExecutionHistory 溢出
    /// </summary>
    public void RecordToolExecutionHistoryOverflow(int currentSize)
    {
        lock (_lock)
        {
            ToolExecutionHistoryOverflowCount++;
            ToolExecutionHistoryCurrentSize = currentSize;
            if (currentSize > ToolExecutionHistoryMaxSize)
                ToolExecutionHistoryMaxSize = currentSize;
        }
    }

    /// <summary>
    /// 记录中断优先级分配
    /// </summary>
    public void RecordInterruptPriority(string kind)
    {
        lock (_lock)
        {
            var normalizedKind = kind?.ToLowerInvariant() ?? "freeform";
            if (InterruptPriorityDistribution.ContainsKey(normalizedKind))
                InterruptPriorityDistribution[normalizedKind]++;
            else
                InterruptPriorityDistribution["freeform"]++;
        }
    }

    /// <summary>
    /// 记录 Memory 刷新
    /// </summary>
    public void RecordMemoryRefresh(TimeSpan duration, bool success)
    {
        lock (_lock)
        {
            if (success)
            {
                MemoryRefreshCount++;
                TotalMemoryRefreshDuration += duration;
            }
            else
            {
                MemoryRefreshFailureCount++;
            }
        }
    }

    /// <summary>
    /// 记录工具去重命中/未命中
    /// </summary>
    public void RecordToolDeduplication(bool hit)
    {
        lock (_lock)
        {
            if (hit)
                ToolDeduplicationHits++;
            else
                ToolDeduplicationMisses++;
        }
    }

    /// <summary>
    /// 记录向量检索性能
    /// </summary>
    public void RecordVectorRetrieval(TimeSpan duration, bool success)
    {
        lock (_lock)
        {
            if (success)
            {
                VectorRetrievalCount++;
                TotalVectorRetrievalDuration += duration;
                if (duration.TotalMilliseconds > 100)
                    VectorRetrievalSlowCount++;
            }
            else
            {
                VectorRetrievalFailureCount++;
            }
        }
    }

    /// <summary>
    /// 获取性能报告
    /// </summary>
    public string GetPerformanceReport()
    {
        lock (_lock)
        {
            var avgMemoryRefreshMs = MemoryRefreshCount > 0
                ? TotalMemoryRefreshDuration.TotalMilliseconds / MemoryRefreshCount
                : 0;

            var deduplicationHitRate = (ToolDeduplicationHits + ToolDeduplicationMisses) > 0
                ? (double)ToolDeduplicationHits / (ToolDeduplicationHits + ToolDeduplicationMisses) * 100
                : 0;

            var avgVectorRetrievalMs = VectorRetrievalCount > 0
                ? TotalVectorRetrievalDuration.TotalMilliseconds / VectorRetrievalCount
                : 0;

            var vectorSlowRate = VectorRetrievalCount > 0
                ? (double)VectorRetrievalSlowCount / VectorRetrievalCount * 100
                : 0;

            return $@"
=== Agent 架构修复性能指标 ===

[#25 智能终止]
  无进展提前终止: {EarlyTerminationsByNoProgress}
  连续失败提前终止: {EarlyTerminationsByConsecutiveFailures}
  节省 Token 估算: ~{TokensSavedByEarlyTermination:N0}

[#30 ChatHistory 压缩]
  ToolExecutionHistory 溢出次数: {ToolExecutionHistoryOverflowCount}
  历史队列最大长度: {ToolExecutionHistoryMaxSize}
  历史队列当前长度: {ToolExecutionHistoryCurrentSize}

[#31 中断优先级]
  stop: {InterruptPriorityDistribution["stop"]}
  direction_change: {InterruptPriorityDistribution["direction_change"]}
  supplement: {InterruptPriorityDistribution["supplement"]}
  status: {InterruptPriorityDistribution["status"]}
  freeform: {InterruptPriorityDistribution["freeform"]}

[#33 Memory 同步]
  刷新次数: {MemoryRefreshCount}
  刷新失败: {MemoryRefreshFailureCount}
  平均耗时: {avgMemoryRefreshMs:F2} ms

[#28 工具去重]
  去重命中: {ToolDeduplicationHits}
  去重未命中: {ToolDeduplicationMisses}
  命中率: {deduplicationHitRate:F1}%

[#36 向量检索性能]
  检索次数: {VectorRetrievalCount}
  检索失败: {VectorRetrievalFailureCount}
  平均耗时: {avgVectorRetrievalMs:F2} ms
  慢查询(>100ms): {VectorRetrievalSlowCount} ({vectorSlowRate:F1}%)
";
        }
    }

    /// <summary>
    /// 重置所有指标（仅用于测试）
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            EarlyTerminationsByNoProgress = 0;
            EarlyTerminationsByConsecutiveFailures = 0;
            TokensSavedByEarlyTermination = 0;
            ToolExecutionHistoryOverflowCount = 0;
            ToolExecutionHistoryMaxSize = 0;
            ToolExecutionHistoryCurrentSize = 0;
            MemoryRefreshCount = 0;
            MemoryRefreshFailureCount = 0;
            TotalMemoryRefreshDuration = TimeSpan.Zero;
            ToolDeduplicationHits = 0;
            ToolDeduplicationMisses = 0;
            VectorRetrievalCount = 0;
            VectorRetrievalFailureCount = 0;
            TotalVectorRetrievalDuration = TimeSpan.Zero;
            VectorRetrievalSlowCount = 0;
            foreach (var key in InterruptPriorityDistribution.Keys.ToList())
            {
                InterruptPriorityDistribution[key] = 0;
            }
        }
    }
}
