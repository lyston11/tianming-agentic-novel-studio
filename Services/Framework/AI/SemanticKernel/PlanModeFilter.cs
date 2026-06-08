using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class PlanModeFilter : IFunctionInvocationFilter
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<Guid, int> _runStepCounters = new();

        private static readonly Dictionary<Guid, int> _consecutiveFailures = new();
        private const int MaxConsecutiveFailures = 3;

        public static event Func<FunctionInvocationContext, Task<bool>>? OnFunctionConfirmation;

        public static bool IsEnabled { get; set; } = false;

        private static int GetNextStepIndex(Guid runId)
        {
            lock (_lock)
            {
                if (!_runStepCounters.TryGetValue(runId, out var current))
                {
                    current = 0;
                }

                current++;
                _runStepCounters[runId] = current;
                return current;
            }
        }

        public static int GetToolCallCount(Guid runId)
        {
            lock (_lock)
            {
                return _runStepCounters.TryGetValue(runId, out var n) ? n : 0;
            }
        }

        public static void ResetRun(Guid runId)
        {
            lock (_lock)
            {
                _runStepCounters.Remove(runId);
                _consecutiveFailures.Remove(runId);
            }
        }

        public static bool IsRunCircuitBroken(Guid runId)
        {
            return IsCircuitBroken(runId);
        }

        public static int GetConsecutiveFailures(Guid runId)
        {
            lock (_lock)
            {
                return _consecutiveFailures.TryGetValue(runId, out var count) ? count : 0;
            }
        }

        public static int CircuitBreakerThreshold => MaxConsecutiveFailures;

        public static bool IsToolAllowedForMode(
            TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode mode,
            string? pluginName,
            string? funcName)
        {
            pluginName ??= string.Empty;
            funcName ??= string.Empty;

            var isDataLookup = string.Equals(pluginName, "DataLookup", StringComparison.OrdinalIgnoreCase);
            var isDataEdit = string.Equals(pluginName, "DataEdit", StringComparison.OrdinalIgnoreCase);
            var isContentEdit = string.Equals(pluginName, "ContentEdit", StringComparison.OrdinalIgnoreCase);
            var isWorkspace = string.Equals(pluginName, "Workspace", StringComparison.OrdinalIgnoreCase);
            var isWorkspaceReadOnly = isWorkspace && (string.Equals(funcName, "ListDirectory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "SearchFiles", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "GrepInFiles", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ReadFileLines", StringComparison.OrdinalIgnoreCase));
            var isWorkspaceExecute = isWorkspace && (string.Equals(funcName, "ExecuteReplaceInFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteMultiReplaceInFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteWriteFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteDeleteFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteRenameFile", StringComparison.OrdinalIgnoreCase));

            if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Edit)
            {
                var isAllowed = isDataLookup || isDataEdit || isContentEdit
                    || (isWorkspace && !isWorkspaceExecute)
                    || (string.Equals(pluginName, "System", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(funcName, "NotifyUser", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(funcName, "GetProjectInfo", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(funcName, "GetCurrentTime", StringComparison.OrdinalIgnoreCase)));

                if (isDataEdit && string.Equals(funcName, "ExecuteChange", StringComparison.OrdinalIgnoreCase))
                    isAllowed = false;
                if (isContentEdit && string.Equals(funcName, "ExecuteContentEdit", StringComparison.OrdinalIgnoreCase))
                    isAllowed = false;
                if (isWorkspace && (string.Equals(funcName, "ConfirmFileEdit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(funcName, "RollbackFileEdit", StringComparison.OrdinalIgnoreCase)))
                    isAllowed = false;

                return isAllowed;
            }

            if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Channel)
            {
                return isDataLookup || isWorkspaceReadOnly;
            }

            return true;
        }

        private static void RecordToolSuccess(Guid runId)
        {
            lock (_lock) { _consecutiveFailures[runId] = 0; }
        }

        private static void RecordToolFailure(Guid runId)
        {
            lock (_lock)
            {
                _consecutiveFailures.TryGetValue(runId, out var count);
                _consecutiveFailures[runId] = count + 1;
            }
        }

        private static bool IsCircuitBroken(Guid runId)
        {
            lock (_lock)
            {
                return _consecutiveFailures.TryGetValue(runId, out var count) && count >= MaxConsecutiveFailures;
            }
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            TM.App.Log($"[PlanModeFilter] 拦截函数调用: {context.Function.Name}");

            var chatService = ServiceLocator.Get<SKChatService>();
            var runId = chatService.LastRunId;
            var mode = chatService.CurrentMode;

            if (IsCircuitBroken(runId))
            {
                TM.App.Log($"[PlanModeFilter] 连续失败熔断：已连续 {MaxConsecutiveFailures} 次工具调用失败，拒绝后续调用: {context.Function.Name}");
                context.Result = new FunctionResult(context.Function, $"[工具调用已暂停：连续 {MaxConsecutiveFailures} 次调用失败，请检查参数后重试]");
                return;
            }

            var stepIndex = GetNextStepIndex(runId);

            var runType = chatService.CurrentRunType;
            ExecutionEventHub.Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                RunType = runType,
                EventType = ExecutionEventType.ToolCallStarted,
                PluginName = context.Function.PluginName,
                FunctionName = context.Function.Name,
                StepIndex = stepIndex,
                Title = $"调用 {context.Function.PluginName}.{context.Function.Name}",
                Detail = context.Arguments?.ToString()
            });

            var pluginName = context.Function.PluginName ?? string.Empty;
            var funcName = context.Function.Name ?? string.Empty;
            var isDataLookup = string.Equals(pluginName, "DataLookup", StringComparison.OrdinalIgnoreCase);
            var isDataEdit = string.Equals(pluginName, "DataEdit", StringComparison.OrdinalIgnoreCase);
            var isContentEdit = string.Equals(pluginName, "ContentEdit", StringComparison.OrdinalIgnoreCase);
            var isWorkspace = string.Equals(pluginName, "Workspace", StringComparison.OrdinalIgnoreCase);
            var isWorkspaceReadOnly = isWorkspace && (string.Equals(funcName, "ListDirectory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "SearchFiles", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "GrepInFiles", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ReadFileLines", StringComparison.OrdinalIgnoreCase));
            var isWorkspaceExecute = isWorkspace && (string.Equals(funcName, "ExecuteReplaceInFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteMultiReplaceInFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteWriteFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteDeleteFile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(funcName, "ExecuteRenameFile", StringComparison.OrdinalIgnoreCase));

            if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Edit)
            {
                if (!IsToolAllowedForMode(mode, pluginName, funcName))
                {
                    RecordToolFailure(runId);
                    TM.App.Log($"[PlanModeFilter] Edit模式禁止函数调用: {pluginName}.{funcName}");
                    context.Result = new FunctionResult(context.Function, $"[Edit模式禁止执行 {pluginName}.{funcName}]");

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallFailed,
                        PluginName = pluginName,
                        FunctionName = funcName,
                        StepIndex = stepIndex,
                        Title = $"拒绝 {pluginName}.{funcName}",
                        Detail = "[Edit模式禁止调用]",
                        Succeeded = false
                    });
                    return;
                }
            }

            if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Channel)
            {
                if (!IsToolAllowedForMode(mode, pluginName, funcName))
                {
                    RecordToolFailure(runId);
                    TM.App.Log($"[PlanModeFilter] Channel 总通道禁止函数调用: {pluginName}.{funcName}");
                    context.Result = new FunctionResult(context.Function, $"[总通道禁止执行 {pluginName}.{funcName}]");

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallFailed,
                        PluginName = pluginName,
                        FunctionName = funcName,
                        StepIndex = stepIndex,
                        Title = $"拒绝 {pluginName}.{funcName}",
                        Detail = "[总通道禁止调用]",
                        Succeeded = false
                    });
                    return;
                }
            }

            if (isDataLookup || isDataEdit || isContentEdit || isWorkspace)
            {
                try
                {
                    await next(context).ConfigureAwait(false);
                    RecordToolSuccess(runId);

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallCompleted,
                        PluginName = pluginName,
                        FunctionName = funcName,
                        StepIndex = stepIndex,
                        Title = $"完成 {pluginName}.{funcName}",
                        Detail = context.Result?.ToString(),
                        Succeeded = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    RecordToolFailure(runId);
                    TM.App.Log($"[PlanModeFilter] 工具调用异常: {ex.Message}");
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallFailed,
                        PluginName = pluginName,
                        FunctionName = funcName,
                        StepIndex = stepIndex,
                        Title = $"失败 {pluginName}.{funcName}",
                        Detail = ex.Message,
                        Succeeded = false
                    });
                    throw;
                }
            }

            bool confirmed = true;
            if (IsEnabled && OnFunctionConfirmation != null)
            {
                try
                {
                    confirmed = await OnFunctionConfirmation(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlanModeFilter] 确认回调异常: {ex.Message}");
                    confirmed = false;
                }
            }

            if (confirmed)
            {
                TM.App.Log($"[PlanModeFilter] 用户确认执行: {context.Function.Name}");
                try
                {
                    await next(context).ConfigureAwait(false);
                    RecordToolSuccess(runId);

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallCompleted,
                        PluginName = context.Function.PluginName,
                        FunctionName = context.Function.Name,
                        StepIndex = stepIndex,
                        Title = $"完成 {context.Function.PluginName}.{context.Function.Name}",
                        Detail = context.Result?.ToString(),
                        Succeeded = true
                    });
                }
                catch (Exception ex)
                {
                    RecordToolFailure(runId);
                    TM.App.Log($"[PlanModeFilter] 工具调用异常: {ex.Message}");
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        RunType = runType,
                        EventType = ExecutionEventType.ToolCallFailed,
                        PluginName = context.Function.PluginName,
                        FunctionName = context.Function.Name,
                        StepIndex = stepIndex,
                        Title = $"失败 {context.Function.PluginName}.{context.Function.Name}",
                        Detail = ex.Message,
                        Succeeded = false
                    });
                    throw;
                }
            }
            else
            {
                TM.App.Log($"[PlanModeFilter] 用户取消执行: {context.Function.Name}");
                context.Result = new FunctionResult(context.Function, "[用户取消了此操作]");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    RunType = runType,
                    EventType = ExecutionEventType.ToolCallFailed,
                    PluginName = context.Function.PluginName,
                    FunctionName = context.Function.Name,
                    StepIndex = stepIndex,
                    Title = $"取消 {context.Function.PluginName}.{context.Function.Name}",
                    Detail = "[用户取消了此操作]",
                    Succeeded = false
                });
            }
        }
    }
}
