using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Threading;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        #region 流式更新

        public void AppendContent(string chunk)
        {
            if (_contentBuilder.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                {
                    return;
                }
                chunk = chunk.TrimStart('\r', '\n');
                if (chunk.Length == 0)
                {
                    return;
                }
            }

            if (IsThinking && _contentBuilder.Length == 0)
            {
                if (HasThinking)
                {
                    var elapsed = Math.Max(0.1, (DateTime.Now - _thinkingStartTime).TotalSeconds);
                    AnalysisDurationSeconds = elapsed;
                    AnalysisSummary = FormatAnalysisSummary(AnalysisKind, elapsed);
                }
                IsThinking = false;
            }

            _contentBuilder.Append(chunk);

            if (_contentBuilder.Length - _lastTokenEstimateLength >= 200)
            {
                _lastTokenEstimateLength = _contentBuilder.Length;
                StreamingTokenEstimate = EstimateTokensFromLength(_contentBuilder.Length);
            }

            _charQueue ??= new Queue<char>();
            foreach (var c in chunk)
                _charQueue.Enqueue(c);
            EnsureSmoothTimerRunning();
        }

        private bool _smoothTimerRunning;

        private void EnsureSmoothTimerRunning()
        {
            if (_smoothTimer == null)
            {
                _smoothTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(32)
                };
                _smoothTimer.Tick += OnSmoothTimerTick;
            }
            if (!_smoothTimerRunning) { _smoothTimer.Start(); _smoothTimerRunning = true; }
        }

        private void OnSmoothTimerTick(object? sender, EventArgs e)
        {
            if (_charQueue == null || _charQueue.Count == 0)
            {
                _smoothTimer?.Stop();
                _smoothTimerRunning = false;
                return;
            }

            var backlog = _charQueue.Count;
            var targetPerFrame = Math.Clamp(backlog / 15, 30, 600);
            var count = Math.Min(backlog, targetPerFrame);
            for (int i = 0; i < count && _charQueue.Count > 0; i++)
                _displayedBuilder.Append(_charQueue.Dequeue());

            if (count > 0)
            {
                Content = _displayedBuilder.ToString();
            }
        }

        private void FlushSmoothQueue()
        {
            _smoothTimer?.Stop();
            _smoothTimer = null;
            _smoothTimerRunning = false;

            if (_charQueue != null && _charQueue.Count > 0)
            {
                Content = _contentBuilder.ToString();
            }
            _charQueue = null;
            _displayedBuilder.Clear();
        }

        public void FlushThinkingImmediately()
        {
            _thinkingFlushTimer?.Stop();
            _thinkingDirty = false;
            FlushThinkingToUI();
        }

        private void EnsureThinkingFlushTimerRunning()
        {
            if (_thinkingFlushTimer == null)
            {
                _thinkingFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(80)
                };
                _thinkingFlushTimer.Tick += OnThinkingFlushTimerTick;
            }
            if (!_thinkingFlushTimer.IsEnabled)
                _thinkingFlushTimer.Start();
        }

        private void OnThinkingFlushTimerTick(object? sender, EventArgs e)
        {
            if (!_thinkingDirty)
            {
                _thinkingFlushTimer?.Stop();
                return;
            }
            _thinkingDirty = false;
            FlushThinkingToUI();
        }

        private void FlushThinkingToUI()
        {
            _thinkingContent = _thinkingBuilder.ToString().TrimEnd('\n', '\r', ' ', '\t');
            OnPropertyChanged(nameof(ThinkingContent));
            OnPropertyChanged(nameof(HasThinking));
        }

        private void FlushThinkingQueue()
        {
            _thinkingFlushTimer?.Stop();
            _thinkingFlushTimer = null;
            if (_thinkingDirty)
            {
                _thinkingDirty = false;
                FlushThinkingToUI();
            }
        }

        public void FinishStreaming()
        {
            FlushSmoothQueue();

            FlushThinkingQueue();

            UpdateThinkingBlocks();

            IsStreaming = false;
            _thinking?.Deactivate();
            IsThinking = false;

            Timestamp = DateTime.Now;
            OnPropertyChanged(nameof(Timestamp));

            if (!string.IsNullOrWhiteSpace(Content))
            {
                TokenCount = TM.Framework.Common.Helpers.TokenEstimator.CountTokens(Content);
            }
            else if (_streamingTokenEstimate > 0)
            {
                TokenCount = _streamingTokenEstimate;
            }

            _contentBuilder.Clear();
            _thinkingBuilder.Clear();
            _thinkingChapterCount = 0;
        }

        #endregion
    }
}
