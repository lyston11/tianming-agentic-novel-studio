using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.AI.Core;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class UserConfiguration : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("ProviderId")] public string ProviderId { get; set; } = string.Empty;
    [JsonPropertyName("ModelId")] public string ModelId { get; set; } = string.Empty;
    [JsonIgnore] public string ApiKey { get; set; } = string.Empty;
    [JsonPropertyName("CustomEndpoint")] public string? CustomEndpoint { get; set; }
    [JsonPropertyName("Temperature")] public double Temperature { get; set; } = 0.7;
    [JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 0;
    [JsonPropertyName("FrequencyPenalty")] public double FrequencyPenalty { get; set; } = 0.1;
    [JsonPropertyName("BatchTier")] public string BatchTier { get; set; } = "64K";
    [JsonPropertyName("ContextWindow")] public int ContextWindow { get; set; }
    [JsonPropertyName("IsActive")] public bool IsActive { get; set; }
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.Now;

    private string _reasoningEffort = string.Empty;
    private bool? _thinkingEnabled;

    [JsonPropertyName("ReasoningEffort")]
    public string ReasoningEffort
    {
        get => _reasoningEffort;
        set { if (_reasoningEffort == value) return; _reasoningEffort = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    [JsonPropertyName("ThinkingEnabled")]
    public bool? ThinkingEnabled
    {
        get => _thinkingEnabled;
        set { if (_thinkingEnabled == value) return; _thinkingEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    [JsonIgnore]
    public string ReasoningLabel
    {
        get
        {
            var familyLabel = GetUiFamilyThinkingLabel();

            var parts = new System.Collections.Generic.List<string>(3);

            if (_thinkingEnabled == true)
            {
                var rawEffort = string.IsNullOrEmpty(_reasoningEffort) ? string.Empty
                    : _reasoningEffort.Trim() is { Length: > 0 } e
                        ? (e.Equals("xhigh", StringComparison.OrdinalIgnoreCase) ? "XHigh"
                           : char.ToUpperInvariant(e[0]) + e.Substring(1))
                        : string.Empty;
                if (rawEffort.Length > 0) parts.Add(rawEffort);
                if (familyLabel.Length > 0) parts.Add(familyLabel);
            }

            if (_enableLongContext == true && _supportsLongContext) parts.Add("1M");

            return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
        }
    }

    private string GetUiFamilyThinkingLabel()
    {
        var m = (ModelId ?? string.Empty).ToLowerInvariant();
        var p = (ProviderId ?? string.Empty).ToLowerInvariant();

        if (m.Contains("deepseek")) return "Reasoner";

        if (System.Text.RegularExpressions.Regex.IsMatch(m, @"gpt-[5-9]")) return "Reasoning";
        if (System.Text.RegularExpressions.Regex.IsMatch(m, @"^o[1-9](?:-|$)")) return "Reasoning";
        if (m.Contains("gpt-oss")) return "Reasoning";
        if (m.Contains("llama")) return "Reasoning";
        if (m.Contains("grok") && (m.Contains("mini") || m.Contains("reason"))
            && !m.Contains("-thinking") && !m.Contains("-think")) return "Reasoning";
        if (m.StartsWith("step", System.StringComparison.Ordinal)
            && (m.Contains("-r") || System.Text.RegularExpressions.Regex.IsMatch(m, @"step-[2-9]"))) return "Reasoning";

        return "Thinking";
    }

    [JsonPropertyName("DeveloperMessage")] public string? DeveloperMessage { get; set; }

    [JsonPropertyName("TopP")] public double TopP { get; set; } = 1.0;

    [JsonPropertyName("PresencePenalty")] public double PresencePenalty { get; set; } = 0.0;

    [JsonPropertyName("Seed")] public string Seed { get; set; } = string.Empty;

    [JsonPropertyName("StopSequences")] public string StopSequences { get; set; } = string.Empty;

    [JsonPropertyName("RateLimitRPM")] public int RateLimitRPM { get; set; } = 0;

    [JsonPropertyName("RateLimitTPM")] public int RateLimitTPM { get; set; } = 0;

    [JsonPropertyName("MaxConcurrency")] public int MaxConcurrency { get; set; } = 5;

    [JsonPropertyName("RetryCount")] public int RetryCount { get; set; } = 3;

    [JsonPropertyName("TimeoutSeconds")] public int TimeoutSeconds { get; set; } = 120;

    [JsonPropertyName("AutoDisabledBySystem")] public bool AutoDisabledBySystem { get; set; }

    [JsonPropertyName("ThinkingPassthrough")] public bool? ThinkingPassthrough { get; set; }

    private bool _supportsThinking;
    [JsonPropertyName("SupportsThinking")]
    public bool SupportsThinking
    {
        get => _supportsThinking;
        set { if (_supportsThinking == value) return; _supportsThinking = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    private bool _supportsReasoningEffort;
    [JsonPropertyName("SupportsReasoningEffort")]
    public bool SupportsReasoningEffort
    {
        get => _supportsReasoningEffort;
        set { if (_supportsReasoningEffort == value) return; _supportsReasoningEffort = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    private List<string>? _supportedEffortLevels;
    [JsonPropertyName("SupportedEffortLevels")]
    public List<string>? SupportedEffortLevels
    {
        get => _supportedEffortLevels;
        set { if (ReferenceEquals(_supportedEffortLevels, value)) return; _supportedEffortLevels = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    private bool _supportsLongContext;
    [JsonPropertyName("SupportsLongContext")]
    public bool SupportsLongContext
    {
        get => _supportsLongContext;
        set { if (_supportsLongContext == value) return; _supportsLongContext = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveContextWindow)); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    private bool? _enableLongContext;
    [JsonPropertyName("EnableLongContext")]
    public bool? EnableLongContext
    {
        get => _enableLongContext;
        set { if (_enableLongContext == value) return; _enableLongContext = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveContextWindow)); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    [JsonPropertyName("LongContextWindow")] public int LongContextWindow { get; set; } = 1_000_000;

    [JsonIgnore]
    public int EffectiveContextWindow
        => (_supportsLongContext && _enableLongContext == true && LongContextWindow > 0)
            ? LongContextWindow
            : ContextWindow;

    private bool _capabilitiesDetected;
    [JsonPropertyName("CapabilitiesDetected")]
    public bool CapabilitiesDetected
    {
        get => _capabilitiesDetected;
        set { if (_capabilitiesDetected == value) return; _capabilitiesDetected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningLabel)); }
    }

    [JsonIgnore]
    public string CategoryPrefix { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayNameWithPrefix =>
        string.IsNullOrEmpty(CategoryPrefix) ? Name : $"{CategoryPrefix}-{Name}";

    public string GetDisplayName()
    {
        return string.IsNullOrEmpty(Name) ? $"{ProviderId}/{ModelId}" : Name;
    }
}
