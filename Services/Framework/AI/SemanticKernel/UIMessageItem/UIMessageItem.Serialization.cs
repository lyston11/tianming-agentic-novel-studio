using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        #region 三层序列化

        public SerializedMessageRecord ToSerializedRecord()
        {
            string? refsJson = null;
            if (HasReferences)
            {
                try { refsJson = JsonSerializer.Serialize(References, JsonHelper.Compact); }
                catch { }
            }

            return new SerializedMessageRecord
            {
                MessageId = MessageId,
                Role = Role.Label,
                Summary = Content,
                Analysis = string.IsNullOrWhiteSpace(ThinkingContent) ? null : ThinkingContent,
                AnalysisKind = AnalysisKind,
                DurationSeconds = AnalysisDurationSeconds,
                ChangesJson = string.IsNullOrWhiteSpace(ChangesJson) ? null : ChangesJson,
                ChangesDurationSeconds = ChangesDurationSeconds,
                PayloadType = (int)PayloadType,
                PayloadJson = PayloadJson,
                Timestamp = Timestamp,
                ReferencesJson = refsJson
            };
        }

        public static UIMessageItem FromSerializedRecord(SerializedMessageRecord record)
        {
            var role = record.Role.ToLower() switch
            {
                "system" => AuthorRole.System,
                "user" => AuthorRole.User,
                "assistant" => AuthorRole.Assistant,
                _ => AuthorRole.User
            };

            var item = new UIMessageItem
            {
                MessageId = record.MessageId,
                Role = role,
                Content = record.Summary,
                ThinkingContent = record.Analysis ?? string.Empty,
                AnalysisKind = NormalizeAnalysisKind(record.AnalysisKind),
                ChangesJson = record.ChangesJson,
                Timestamp = record.Timestamp,
                PayloadType = (PayloadType)record.PayloadType,
                PayloadJson = record.PayloadJson
            };

            if (record.DurationSeconds.HasValue)
            {
                item.AnalysisDurationSeconds = record.DurationSeconds.Value;
                item.AnalysisSummary = FormatAnalysisSummary(item.AnalysisKind, record.DurationSeconds.Value);
            }
            else if (!string.IsNullOrEmpty(record.Analysis))
            {
                item.AnalysisSummary = "View analysis";
            }

            if (record.ChangesDurationSeconds.HasValue)
            {
                item.ChangesDurationSeconds = record.ChangesDurationSeconds.Value;
                item.ChangesSummary = $"CHANGES for {record.ChangesDurationSeconds.Value:F1} s";
            }
            else if (!string.IsNullOrWhiteSpace(record.ChangesJson))
            {
                item.ChangesSummary = "View CHANGES";
            }

            if (!string.IsNullOrWhiteSpace(record.ReferencesJson))
            {
                try
                {
                    var refs = JsonSerializer.Deserialize<List<SearchResult>>(record.ReferencesJson);
                    if (refs != null && refs.Count > 0)
                        item.References = refs;
                }
                catch { }
            }

            return item;
        }

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        public abstract class CollapsiblePanelBindings : INotifyPropertyChanged
        {
            protected UIMessageItem Owner { get; }

            protected CollapsiblePanelBindings(UIMessageItem owner)
            {
                Owner = owner;
                Owner.PropertyChanged += OnOwnerPropertyChanged;
            }

            public abstract string? Summary { get; }

            public abstract bool IsProcessing { get; }

            public abstract bool IsExpanded { get; set; }

            public abstract IReadOnlyList<ThinkingBlock> Blocks { get; }

            public virtual string AnalysisRaw => string.Empty;

            public virtual string? Phase => null;

            public abstract ICommand ToggleCommand { get; }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void RaisePropertyChanged([CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            protected abstract void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e);
        }

        public sealed class ThinkingPanelBindingsAdapter : CollapsiblePanelBindings
        {
            public ThinkingPanelBindingsAdapter(UIMessageItem owner) : base(owner)
            {
            }

            public override string? Summary => Owner.AnalysisSummary;

            public override bool IsProcessing => Owner.IsThinking || (Owner._thinking?.IsActive ?? false);

            public override bool IsExpanded
            {
                get => Owner.IsThinkingExpanded;
                set => Owner.IsThinkingExpanded = value;
            }

            public override IReadOnlyList<ThinkingBlock> Blocks => Owner.ThinkingBlocks;

            public override string AnalysisRaw => Owner.ThinkingContent ?? string.Empty;

            public override string? Phase => Owner.CurrentPhase;

            public override ICommand ToggleCommand => Owner.ToggleThinkingExpandedCommand;

            protected override void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(UIMessageItem.AnalysisSummary))
                {
                    RaisePropertyChanged(nameof(Summary));
                    RaisePropertyChanged(nameof(IsProcessing));
                }
                if (e.PropertyName == nameof(UIMessageItem.IsThinking)) RaisePropertyChanged(nameof(IsProcessing));
                if (e.PropertyName == nameof(UIMessageItem.IsThinkingExpanded)) RaisePropertyChanged(nameof(IsExpanded));
                if (e.PropertyName == nameof(UIMessageItem.ThinkingBlocks)) RaisePropertyChanged(nameof(Blocks));
                if (e.PropertyName == nameof(UIMessageItem.ThinkingContent))
                {
                    RaisePropertyChanged(nameof(AnalysisRaw));
                    RaisePropertyChanged(nameof(IsProcessing));
                }
                if (e.PropertyName == nameof(UIMessageItem.CurrentPhase))
                {
                    RaisePropertyChanged(nameof(Phase));
                    RaisePropertyChanged(nameof(IsProcessing));
                }
            }
        }

        public sealed class ChangesPanelBindingsAdapter : CollapsiblePanelBindings
        {
            public ChangesPanelBindingsAdapter(UIMessageItem owner) : base(owner)
            {
            }

            public override string? Summary => Owner.ChangesSummary;

            public override bool IsProcessing => Owner.IsChangesProcessing;

            public override bool IsExpanded
            {
                get => Owner.IsChangesExpanded;
                set => Owner.IsChangesExpanded = value;
            }

            public override IReadOnlyList<ThinkingBlock> Blocks => Owner.ChangesBlocks;

            public override ICommand ToggleCommand => Owner.ToggleChangesExpandedCommand;

            protected override void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(UIMessageItem.ChangesSummary)) RaisePropertyChanged(nameof(Summary));
                if (e.PropertyName == nameof(UIMessageItem.IsChangesProcessing)) RaisePropertyChanged(nameof(IsProcessing));
                if (e.PropertyName == nameof(UIMessageItem.IsChangesExpanded)) RaisePropertyChanged(nameof(IsExpanded));
                if (e.PropertyName == nameof(UIMessageItem.ChangesBlocks)) RaisePropertyChanged(nameof(Blocks));
            }
        }

        #endregion
    }
}
