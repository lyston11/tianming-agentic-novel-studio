using System;
using System.Collections.Generic;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel.Chunk;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public sealed class ThinkingProgressBridge : IDisposable
    {
        private readonly UIMessageItem _message;
        private readonly Func<Guid> _runIdProvider;
        private readonly ChatMode _mode;
        private readonly Action<ProgressInfo> _onProgress;
        private readonly Action<IStreamChunk> _onChunk;
        private bool _disposed;

        private static readonly Dictionary<string, string> ToolDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PreviewChange"] = "预览变更",
            ["ConfirmChange"] = "确认变更",
            ["RollbackChange"] = "回滚变更",
            ["ListEntityFields"] = "查询字段",
            ["SearchTextInAllEntities"] = "全文搜索",
            ["FindEntityReferences"] = "查找引用",
            ["FindRelatedEntities"] = "查找关联",
            ["ReconcileAllData"] = "数据对账",
            ["GetEntityList"] = "获取列表",
            ["GetEntityDetail"] = "获取详情",
            ["GetContentGuideChapter"] = "获取导引",
            ["GetProjectInfo"] = "获取项目信息",
            ["ListDirectory"] = "浏览目录",
            ["SearchFiles"] = "搜索文件",
            ["GrepInFiles"] = "内容检索",
            ["ReadFileLines"] = "读取文件",
            ["ReplaceInFile"] = "替换预览",
            ["ConfirmFileEdit"] = "确认文件操作",
            ["RollbackFileEdit"] = "取消文件操作",
            ["ExecuteReplaceInFile"] = "执行替换",
            ["PreviewWriteFile"] = "写入预览",
            ["ExecuteWriteFile"] = "执行写入",
            ["CreateFolder"] = "创建目录",
            ["CreateFile"] = "创建文件",
            ["PreviewDeleteFile"] = "删除预览",
            ["ExecuteDeleteFile"] = "执行删除",
            ["PreviewRenameFile"] = "重命名预览",
            ["ExecuteRenameFile"] = "执行重命名",
        };

        public ThinkingProgressBridge(UIMessageItem message, Guid runId, ChatMode mode)
            : this(message, () => runId, mode) { }

        public ThinkingProgressBridge(UIMessageItem message, Func<Guid> runIdProvider, ChatMode mode)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
            _mode = mode;

            _onProgress = OnProgressReported;
            _onChunk = OnAIChunk;

            GenerationProgressHub.ProgressReported += _onProgress;
            AIChunkBus.Published += _onChunk;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GenerationProgressHub.ProgressReported -= _onProgress;
            AIChunkBus.Published -= _onChunk;
        }

        private void OnProgressReported(ProgressInfo info)
        {
            var currentRunId = _runIdProvider();
            if (currentRunId == Guid.Empty) return;
            if (info.RunId == null) return;
            if (info.RunId.Value != currentRunId) return;

            DispatchToUI(() =>
            {
                if (!string.IsNullOrEmpty(info.Phase))
                {
                    _message.Thinking.EnterPhase(info.Phase, string.IsNullOrEmpty(info.Message) ? null : info.Message);
                }
                else if (!string.IsNullOrEmpty(info.Message))
                {
                    _message.Thinking.WriteStatus(info.Message);
                }
            });
        }

        private void OnAIChunk(IStreamChunk chunk)
        {
            var currentRunId = _runIdProvider();
            if (currentRunId == Guid.Empty || chunk.RunId != currentRunId) return;

            DispatchToUI(() =>
            {
                switch (_mode)
                {
                    case ChatMode.Edit:
                        HandleEditChunk(chunk);
                        break;
                    case ChatMode.Agent:
                    case ChatMode.Plan:
                        HandleExecutionChunk(chunk);
                        break;
                    default:
                        if (chunk is ToolCallStartChunk start)
                            _message.Thinking.WriteToolStart(GetToolDisplayName(start.FunctionName));
                        break;
                }
            });
        }

        private static void DispatchToUI(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.InvokeAsync(action);
            }
        }

        private void HandleEditChunk(IStreamChunk chunk)
        {
            switch (chunk)
            {
                case ToolCallStartChunk started:
                    _message.Thinking.WriteToolStart(GetToolDisplayName(started.FunctionName));
                    break;

                case ToolCallCompletedChunk completed:
                    _message.Thinking.WriteToolDone(
                        GetToolDisplayName(completed.FunctionName),
                        completed.Succeeded);
                    break;
            }
        }

        private void HandleExecutionChunk(IStreamChunk chunk)
        {
            if (chunk is not ToolCallStartChunk started) return;
            if (string.Equals(started.Arguments, "等待执行", StringComparison.Ordinal)) return;

            var title = string.IsNullOrWhiteSpace(started.FunctionName) ? "工具调用" : GetToolDisplayName(started.FunctionName);
            _message.Thinking.WriteStepMarker(started.StepIndex, title);
        }

        private static string GetToolDisplayName(string? functionName)
        {
            if (string.IsNullOrEmpty(functionName)) return "工具调用";
            return ToolDisplayNames.TryGetValue(functionName, out var display) ? display : functionName;
        }
    }
}
