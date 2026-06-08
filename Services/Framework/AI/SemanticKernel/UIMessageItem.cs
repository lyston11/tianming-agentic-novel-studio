using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows.Threading;
using TM.Framework.Common.Helpers.Id;
using System.Windows.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        private string _content = string.Empty;
        private string _thinkingContent = string.Empty;
        private bool _isThinking;
        private bool _isStreaming;
        private int _thinkingChapterCount;
        private readonly StringBuilder _contentBuilder = new();
        private readonly StringBuilder _thinkingBuilder = new();
        private DateTime _thinkingStartTime;

        private Queue<char>? _charQueue;
        private readonly StringBuilder _displayedBuilder = new();
        private DispatcherTimer? _smoothTimer;
        private int _streamingTokenEstimate;
        private int _lastTokenEstimateLength;

        private DispatcherTimer? _thinkingFlushTimer;
        private bool _thinkingDirty;

        private bool _isError;
        private bool _isStarred;
        private string? _statusText;
        private bool _isThinkingExpanded;
        private IReadOnlyList<ThinkingBlock> _thinkingBlocks = Array.Empty<ThinkingBlock>();
        private string? _analysisSummary;
        private double? _analysisDurationSeconds;
        private string _analysisKind = "Thinking";

        private string? _changesJson;
        private IReadOnlyList<ThinkingBlock> _changesBlocks = Array.Empty<ThinkingBlock>();
        private bool _isChangesProcessing;
        private bool _isChangesExpanded;
        private string? _changesSummary;
        private double? _changesDurationSeconds;

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "UIMessageItem");

        public UIMessageItem()
        {
            ThinkingPanelBindings = new ThinkingPanelBindingsAdapter(this);
            ChangesPanelBindings = new ChangesPanelBindingsAdapter(this);
        }

        public string MessageId { get; set; } = ShortIdGenerator.NewGuid().ToString("N");

        public string Id => MessageId.Length >= 8 ? MessageId[..8] : MessageId;

        public AuthorRole Role { get; set; }

        public bool IsUser => Role == AuthorRole.User;

        public bool IsAssistant => Role == AuthorRole.Assistant;

        public bool IsSystem => Role == AuthorRole.System;

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTypingPlaceholder));
            }
        }

        public string? ChangesJson
        {
            get => _changesJson;
            set
            {
                if (_changesJson != value)
                {
                    _changesJson = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChanges));
                    UpdateChangesBlocks();

                    if (!string.IsNullOrWhiteSpace(_changesJson) && string.IsNullOrWhiteSpace(_changesSummary))
                    {
                        ChangesSummary = "View CHANGES";
                    }
                }
            }
        }

        public bool HasChanges => !string.IsNullOrWhiteSpace(_changesJson);

        public IReadOnlyList<ThinkingBlock> ChangesBlocks
        {
            get => _changesBlocks;
            private set
            {
                _changesBlocks = value;
                OnPropertyChanged();
            }
        }

        public bool IsChangesProcessing
        {
            get => _isChangesProcessing;
            set
            {
                if (_isChangesProcessing != value)
                {
                    _isChangesProcessing = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsChangesExpanded
        {
            get => _isChangesExpanded;
            set
            {
                if (_isChangesExpanded != value)
                {
                    _isChangesExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private ICommand? _toggleChangesExpandedCommand;
        public ICommand ToggleChangesExpandedCommand =>
            _toggleChangesExpandedCommand ??= new RelayCommand(() => IsChangesExpanded = !IsChangesExpanded);

        public string? ChangesSummary
        {
            get => _changesSummary;
            set
            {
                if (_changesSummary != value)
                {
                    _changesSummary = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChangesSummary));
                }
            }
        }

        public bool HasChangesSummary => !string.IsNullOrWhiteSpace(_changesSummary);

        public double? ChangesDurationSeconds
        {
            get => _changesDurationSeconds;
            set
            {
                if (_changesDurationSeconds != value)
                {
                    _changesDurationSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public CollapsiblePanelBindings ThinkingPanelBindings { get; }

        public CollapsiblePanelBindings ChangesPanelBindings { get; }

        public string ThinkingContent
        {
            get => _thinkingContent;
            set
            {
                _thinkingContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThinking));
                UpdateThinkingBlocks();
            }
        }

        public bool HasThinking => !string.IsNullOrEmpty(_thinkingContent);

        public string? AnalysisSummary
        {
            get => _analysisSummary;
            set
            {
                if (_analysisSummary != value)
                {
                    _analysisSummary = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasAnalysisSummary));
                }
            }
        }

        public bool HasAnalysisSummary => !string.IsNullOrWhiteSpace(_analysisSummary);

        public string AnalysisKind
        {
            get => _analysisKind;
            set
            {
                var normalized = NormalizeAnalysisKind(value);
                if (_analysisKind != normalized)
                {
                    _analysisKind = normalized;
                    OnPropertyChanged();
                }
            }
        }

        public double? AnalysisDurationSeconds
        {
            get => _analysisDurationSeconds;
            set
            {
                if (_analysisDurationSeconds != value)
                {
                    _analysisDurationSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public static string NormalizeAnalysisKind(string? value)
        {
            if (string.Equals(value, "Reasoner", StringComparison.OrdinalIgnoreCase)) return "Reasoner";
            if (string.Equals(value, "Reasoning", StringComparison.OrdinalIgnoreCase)) return "Reasoning";
            if (string.Equals(value, "Analysis", StringComparison.OrdinalIgnoreCase)) return "Analysis";
            if (string.Equals(value, "Thought", StringComparison.OrdinalIgnoreCase)) return "Thought";
            if (string.Equals(value, "SeedThink", StringComparison.OrdinalIgnoreCase)) return "SeedThink";
            return "Thinking";
        }

        public static string FormatAnalysisSummary(string? kind, double seconds)
            => $"{NormalizeAnalysisKind(kind)} for {seconds:F1} s";

        public IReadOnlyList<ThinkingBlock> ThinkingBlocks
        {
            get => _thinkingBlocks;
            private set
            {
                _thinkingBlocks = value;
                OnPropertyChanged();
            }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                _isThinking = value;
                OnPropertyChanged();

                if (value)
                {
                    IsThinkingExpanded = true;
                    StartThinkingTimer();
                }
                else
                {
                    IsThinkingExpanded = false;
                    StopThinkingTimer();
                }
            }
        }

        public DateTime ThinkingStartTime => _thinkingStartTime;

        private void StartThinkingTimer()
        {
            _thinkingStartTime = DateTime.Now;
        }

        private void TruncateThinkingToLastLines(int keepLines)
        {
            var content = _thinkingBuilder.ToString();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > keepLines)
            {
                _thinkingBuilder.Clear();
                var start = lines.Length - keepLines;
                for (var i = start; i < lines.Length; i++)
                {
                    _thinkingBuilder.Append(lines[i]);
                    _thinkingBuilder.Append('\n');
                }
            }
        }

        private void StopThinkingTimer()
        {
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTypingPlaceholder));
            }
        }

        public bool IsError
        {
            get => _isError;
            set { _isError = value; OnPropertyChanged(); }
        }
        public string? StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasStatusText));
                }
            }
        }
        public bool HasStatusText => !string.IsNullOrEmpty(_statusText);

        public bool IsStarred
        {
            get => _isStarred;
            set { _isStarred = value; OnPropertyChanged(); }
        }

        public bool IsTypingPlaceholder => IsAssistant && IsStreaming && string.IsNullOrWhiteSpace(Content);

        public bool IsThinkingExpanded
        {
            get => _isThinkingExpanded;
            set
            {
                if (_isThinkingExpanded != value)
                {
                    _isThinkingExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private ICommand? _toggleThinkingExpandedCommand;
        public ICommand ToggleThinkingExpandedCommand =>
            _toggleThinkingExpandedCommand ??= new RelayCommand(() => IsThinkingExpanded = !IsThinkingExpanded);

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string? ModelName { get; set; }

        public int TokenCount { get; set; }

        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }

        public IReadOnlyList<SearchResult>? References { get; set; }

        public bool HasReferences => References != null && References.Count > 0;

        public int StreamingTokenEstimate
        {
            get => _streamingTokenEstimate;
            private set
            {
                if (_streamingTokenEstimate != value)
                {
                    _streamingTokenEstimate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StreamingTokenEstimateText));
                }
            }
        }

        public string StreamingTokenEstimateText =>
            _streamingTokenEstimate > 0 ? $"~{_streamingTokenEstimate} tokens" : string.Empty;

        public Guid RunId { get; set; }

    }
}
