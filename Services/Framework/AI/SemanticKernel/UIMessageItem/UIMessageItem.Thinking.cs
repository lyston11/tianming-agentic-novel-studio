using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        private string? _currentPhase;
        private DateTime _phaseEnterTime;

        public string? CurrentPhase
        {
            get => _currentPhase;
            private set
            {
                if (_currentPhase != value)
                {
                    _currentPhase = value;
                    OnPropertyChanged();
                }
            }
        }

        private ThinkingScope? _thinking;

        public ThinkingScope Thinking => _thinking ??= new ThinkingScope(this);

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        public sealed class ThinkingScope
        {
            private const int SoftCharLimit = 32 * 1024;
            private const int SoftCharTailKeep = 16 * 1024;
            private const int HardCharLimit = 64 * 1024;
            private const int HardCharTailKeep = 32 * 1024;
            private const int ChapterMarkerLineKeep = 50;
            private const int ChapterMarkerEvery = 3;

            private readonly UIMessageItem _owner;
            private string? _lastStatus;
            private bool _isActive;

            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingRawQueue
                = new System.Collections.Concurrent.ConcurrentQueue<string>();
            private int _drainFlag;

            internal ThinkingScope(UIMessageItem owner)
            {
                _owner = owner;
            }

            public bool IsActive => _isActive;

            #region 状态写入(语义化分类)

            public void WriteStatus(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (string.Equals(text, _lastStatus, StringComparison.Ordinal)) return;
                _lastStatus = text;
                EnsureActive();
                AppendLine($"⚠ {text}");
            }

            public void WriteWarning(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                _lastStatus = null;
                EnsureActive();
                AppendLine($"⚠ {text}");
            }

            public void WriteCompletion(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                _lastStatus = null;
                EnsureActive();
                AppendLine($"✓ {text}");
            }

            #endregion

            #region 阶段语义

            public void EnterPhase(string phase, string? optionalText = null)
            {
                if (string.IsNullOrEmpty(phase)) return;
                EnsureActive();

                if (string.Equals(_owner.CurrentPhase, phase, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(optionalText))
                        AppendLine(optionalText);
                    return;
                }

                _owner._phaseEnterTime = DateTime.Now;
                _owner.CurrentPhase = phase;
                _lastStatus = null;

                AppendLine($"── 进入阶段:{phase} ──");
                if (!string.IsNullOrEmpty(optionalText))
                {
                    AppendLine(optionalText);
                }
            }

            public void MarkPhaseDone(string phase)
            {
                if (string.IsNullOrEmpty(phase)) return;
                EnsureActive();
                _lastStatus = null;
                AppendLine($"✓ {phase} 完成");
            }

            #endregion

            #region 工具事件

            public void WriteToolStart(string displayName)
            {
                if (string.IsNullOrEmpty(displayName)) return;
                EnsureActive();
                _lastStatus = null;
                AppendLine($"[工具] {displayName}...");
            }

            public void WriteToolDone(string displayName, bool succeeded)
            {
                if (string.IsNullOrEmpty(displayName)) return;
                EnsureActive();
                _lastStatus = null;
                AppendLine(succeeded ? $"✓ {displayName} 完成" : $"✗ {displayName} 失败");
            }

            public void WriteStepMarker(int stepIndex, string title)
            {
                EnsureActive();
                _lastStatus = null;
                AppendLine($"\n── 步骤 {stepIndex}: {title} ──");
            }

            #endregion

            #region 异步队列(对外)

            public void EnqueueRaw(string text)
            {
                if (string.IsNullOrEmpty(text)) return;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    EnqueueRawCore(text);
                    return;
                }

                _pendingRawQueue.Enqueue(text);
                if (System.Threading.Interlocked.CompareExchange(ref _drainFlag, 1, 0) == 0)
                {
                    dispatcher.InvokeAsync(DrainPendingRawOnUI, DispatcherPriority.Background);
                }
            }

            private void EnqueueRawCore(string text)
            {
                EnsureActive();
                _lastStatus = null;
                AppendInternal(text);
            }

            private void DrainPendingRawOnUI()
            {
                var sb = new StringBuilder();
                while (sb.Length < 8192 && _pendingRawQueue.TryDequeue(out var t))
                    sb.Append(t);

                if (sb.Length > 0)
                    EnqueueRawCore(sb.ToString());

                System.Threading.Interlocked.Exchange(ref _drainFlag, 0);
                if (!_pendingRawQueue.IsEmpty
                    && System.Threading.Interlocked.CompareExchange(ref _drainFlag, 1, 0) == 0)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        DrainPendingRawOnUI, DispatcherPriority.Background);
                }
            }

            public void FlushPending()
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(FlushPending);
                    return;
                }

                var sb = new StringBuilder();
                while (_pendingRawQueue.TryDequeue(out var t))
                    sb.Append(t);
                if (sb.Length > 0)
                    EnqueueRawCore(sb.ToString());
                System.Threading.Interlocked.Exchange(ref _drainFlag, 0);

                _owner.FlushThinkingImmediately();
            }

            #endregion

            #region 收尾

            public void Complete(string? finalPhase = null)
            {
                FlushPending();

                if (!_owner.AnalysisDurationSeconds.HasValue)
                {
                    var seconds = Math.Max(0.1, (DateTime.Now - _owner.ThinkingStartTime).TotalSeconds);
                    _owner.AnalysisDurationSeconds = seconds;

                    if (!string.IsNullOrEmpty(finalPhase))
                    {
                        _owner.CurrentPhase = finalPhase;
                        _owner.AnalysisSummary = $"{finalPhase} for {seconds:F1} s";
                    }
                    else
                    {
                        _owner.AnalysisSummary = FormatAnalysisSummary(_owner.AnalysisKind, seconds);
                    }
                }
                else if (!string.IsNullOrEmpty(finalPhase))
                {
                    _owner.CurrentPhase = finalPhase;
                    _owner.AnalysisSummary = $"{finalPhase} for {_owner.AnalysisDurationSeconds.Value:F1} s";
                }

                SetActive(false);
            }

            public void Reset()
            {
                _lastStatus = null;
                SetActive(false);
                _owner.CurrentPhase = null;
                while (_pendingRawQueue.TryDequeue(out _)) { }
                System.Threading.Interlocked.Exchange(ref _drainFlag, 0);
            }

            internal void Deactivate()
            {
                SetActive(false);
            }

            #endregion

            #region 内部实现

            private void EnsureActive()
            {
                SetActive(true);
            }

            private void SetActive(bool value)
            {
                if (_isActive == value) return;
                _isActive = value;
                _owner.OnPropertyChanged(nameof(IsThinking));
            }

            private void AppendLine(string text)
            {
                AppendInternal(text + "\n");
            }

            private void AppendInternal(string text)
            {
                _owner._thinkingBuilder.Append(text);

                if (text.Contains("✓ 章节 ", StringComparison.Ordinal)
                    && text.Contains(" 生成完成", StringComparison.Ordinal)
                    && ++_owner._thinkingChapterCount % ChapterMarkerEvery == 0)
                {
                    _owner.TruncateThinkingToLastLines(ChapterMarkerLineKeep);
                }

                var len = _owner._thinkingBuilder.Length;
                if (len > HardCharLimit)
                {
                    TruncateBuilderToLastChars(HardCharTailKeep);
                }
                else if (len > SoftCharLimit)
                {
                    TruncateBuilderToLastChars(SoftCharTailKeep);
                }

                _owner._thinkingDirty = true;
                _owner.EnsureThinkingFlushTimerRunning();
            }

            private void TruncateBuilderToLastChars(int keepChars)
            {
                var builder = _owner._thinkingBuilder;
                if (builder.Length <= keepChars) return;

                var tail = builder.ToString(builder.Length - keepChars, keepChars);
                builder.Clear();
                builder.Append("[...已截断]\n");
                builder.Append(tail);

                if (_owner._thinkingChapterCount > ChapterMarkerEvery)
                    _owner._thinkingChapterCount = 0;
            }

            #endregion
        }
    }
}
