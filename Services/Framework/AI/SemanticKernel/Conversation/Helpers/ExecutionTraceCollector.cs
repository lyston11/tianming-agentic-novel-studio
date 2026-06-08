using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers
{
    public class ExecutionTraceCollector : IDisposable
    {
        private readonly Dictionary<int, ToolCallRecord> _records = new();
        private readonly Action<ExecutionEvent> _eventHandler;
        private bool _isCollecting;
        private bool _disposed;
        private bool _isPolishFatal;

        public ExecutionTraceCollector()
        {
            _eventHandler = OnExecutionEvent;
        }

        public void Start()
        {
            if (_isCollecting) return;

            _records.Clear();
            _isPolishFatal = false;
            ExecutionEventHub.Published += _eventHandler;
            _isCollecting = true;
            TM.App.Log("[ExecutionTraceCollector] 开始收集执行轨迹");
        }

        public IReadOnlyList<ToolCallRecord> Stop()
        {
            if (!_isCollecting) return Array.Empty<ToolCallRecord>();

            ExecutionEventHub.Published -= _eventHandler;
            _isCollecting = false;

            var result = _records.Values.OrderBy(r => r.StepIndex).ToList();
            TM.App.Log($"[ExecutionTraceCollector] 停止收集，共 {result.Count} 条轨迹");
            return result;
        }

        public IReadOnlyList<ToolCallRecord> GetCurrentTrace()
        {
            return _records.Values.OrderBy(r => r.StepIndex).ToList();
        }

        public ExecutionTraceSummary GetSummary()
        {
            var records = _records.Values.ToList();

            var failedSummaries = records
                .Where(r => r.Status == ToolCallStatus.Failed)
                .OrderBy(r => r.StepIndex)
                .Select(FormatFailedStepSummary)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            return new ExecutionTraceSummary
            {
                TotalSteps = records.Count,
                CompletedSteps = records.Count(r => r.Status == ToolCallStatus.Completed),
                FailedSteps = records.Count(r => r.Status == ToolCallStatus.Failed),
                TotalDurationSeconds = records.Where(r => r.DurationSeconds.HasValue).Sum(r => r.DurationSeconds!.Value),
                FailedStepSummaries = failedSummaries,
                IsPolishFatal = _isPolishFatal
            };
        }

        private static string FormatFailedStepSummary(ToolCallRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            var title = record.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = string.IsNullOrWhiteSpace(record.PluginName) && string.IsNullOrWhiteSpace(record.FunctionName)
                    ? "未命名步骤"
                    : $"{record.PluginName}.{record.FunctionName}";
            }

            title = title
                .Replace("调用 ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("失败 ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("取消 ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            var reason = FormatUserFriendlyReason(record.ErrorMessage);
            return $"步骤 {record.StepIndex}「{title}」: {reason}";
        }

        private static string FormatUserFriendlyReason(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "未知错误";
            }

            var msg = raw.Trim();
            var msgShort = Truncate(msg, 160);

            if (msg.Contains("[用户取消", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("用户取消", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("已取消", StringComparison.OrdinalIgnoreCase))
            {
                return "用户取消";
            }

            if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("超时", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("The operation was canceled", StringComparison.OrdinalIgnoreCase))
            {
                return $"超时/取消: {msgShort}";
            }

            if (msg.Contains("429", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                return $"限流(请求过多): {msgShort}";
            }

            if (msg.Contains("401", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("403", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return $"鉴权失败: {msgShort}";
            }

            if (msg.Contains("json", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("解析", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("parse", StringComparison.OrdinalIgnoreCase))
            {
                return $"解析失败: {msgShort}";
            }

            if (msg.Contains("content policy", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("safety", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("违规", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("拒绝", StringComparison.OrdinalIgnoreCase))
            {
                return $"模型拒绝/安全策略: {msgShort}";
            }

            return msgShort;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0)
            {
                return string.Empty;
            }

            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private void OnExecutionEvent(ExecutionEvent evt)
        {
            if (evt.IsPolishFatal)
                _isPolishFatal = true;

            var stepIndex = evt.StepIndex ?? 0;
            if (stepIndex <= 0) return;

            switch (evt.EventType)
            {
                case ExecutionEventType.ToolCallStarted:
                    _records[stepIndex] = new ToolCallRecord
                    {
                        StepIndex = stepIndex,
                        PluginName = evt.PluginName ?? string.Empty,
                        FunctionName = evt.FunctionName ?? string.Empty,
                        Title = evt.Title ?? string.Empty,
                        Arguments = evt.Detail ?? string.Empty,
                        Status = ToolCallStatus.Running,
                        StartTime = DateTime.Now
                    };
                    break;

                case ExecutionEventType.ToolCallCompleted:
                    if (_records.TryGetValue(stepIndex, out var completedRecord))
                    {
                        completedRecord.Status = ToolCallStatus.Completed;
                        completedRecord.Result = evt.Detail ?? "完成";
                        completedRecord.EndTime = DateTime.Now;
                    }
                    break;

                case ExecutionEventType.ToolCallFailed:
                    if (_records.TryGetValue(stepIndex, out var failedRecord))
                    {
                        failedRecord.Status = ToolCallStatus.Failed;
                        failedRecord.ErrorMessage = evt.Detail;
                        failedRecord.EndTime = DateTime.Now;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_isCollecting)
            {
                ExecutionEventHub.Published -= _eventHandler;
                _isCollecting = false;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public class ExecutionTraceSummary
    {
        public int TotalSteps { get; init; }
        public int CompletedSteps { get; init; }
        public int FailedSteps { get; init; }
        public double TotalDurationSeconds { get; init; }

        public IReadOnlyList<string> FailedStepSummaries { get; init; } = Array.Empty<string>();

        public bool IsPolishFatal { get; init; }

        public string ToSummaryText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"共 {TotalSteps} 步");
            if (FailedSteps > 0) sb.Append($"（{FailedSteps} 失败）");
            if (TotalDurationSeconds > 0) sb.Append($"，耗时 {TotalDurationSeconds:F1}s");

            if (FailedSteps > 0 && FailedStepSummaries.Count > 0)
            {
                var max = 3;
                var shown = Math.Min(max, FailedStepSummaries.Count);
                sb.Append("\n失败原因：");
                for (var i = 0; i < shown; i++)
                {
                    sb.Append($"\n- {FailedStepSummaries[i]}");
                }

                if (FailedStepSummaries.Count > max)
                {
                    sb.Append($"\n- 还有 {FailedStepSummaries.Count - max} 个失败未展开");
                }
            }

            return sb.ToString();
        }

        public bool AllSucceeded => FailedSteps == 0 && CompletedSteps == TotalSteps;
    }
}
