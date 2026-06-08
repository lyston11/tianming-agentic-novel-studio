using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel.Discovery;

#pragma warning disable SKEXP0001

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static partial class ChatModeSettings
    {
        public static int LastUsedMaxTokens { get; private set; } = 0;

        public static string? LastFinishReason { get; private set; } = null;

        private static readonly int[] _autoMaxTokensLadder = { 262144, 131072, 65536, 32768, 16384, 8192, 4096 };

        public static int MaxTokensLadderTop => _autoMaxTokensLadder[0];

        private static readonly DiscoveryCache<int> _maxOutputCache = new();
        private static readonly DiscoveryCache<int> _contextWindowCache = new();

        private static readonly TimeSpan _cacheTtl = TimeSpan.FromDays(30);

        private static readonly object _cacheSaveLock = new();
        private static bool _cacheDirty = false;
        private static int _cacheMutationVersion;

        private static readonly TimeSpan _probeInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan _successUpgradeDebounce = TimeSpan.FromMinutes(30);
        private static readonly ConcurrentDictionary<string, long> _modelMaxOutputProbeTicks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, long> _modelContextWindowProbeTicks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, long> _modelSuccessUpgradeTicks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, int> _modelLastMaxOutputProbeRequested = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, HashSet<string>> _endpointUnsupportedParams =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool TryMarkProbe(ConcurrentDictionary<string, long> dict, string key)
        {
            var now = DateTime.UtcNow.Ticks;
            if (dict.TryGetValue(key, out var last)
                && now - last < _probeInterval.Ticks)
                return false;
            dict[key] = now;
            return true;
        }

        private static readonly ConcurrentDictionary<string, long> _modelsEndpointProbeTicks =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _modelsEndpointProbeInterval = TimeSpan.FromHours(6);

        public static bool ShouldProbeModelsEndpoint(string modelId, string? endpoint, string? providerId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var now = DateTime.UtcNow.Ticks;
            if (_modelsEndpointProbeTicks.TryGetValue(key, out var last)
                && now - last < _modelsEndpointProbeInterval.Ticks)
                return false;
            _modelsEndpointProbeTicks[key] = now;
            return true;
        }

        private static int? GetNextLargerFromLadder(int current)
        {
            if (current <= 0) return null;
            for (int i = _autoMaxTokensLadder.Length - 1; i >= 0; i--)
            {
                var v = _autoMaxTokensLadder[i];
                if (v > current) return v;
            }
            return null;
        }

        static ChatModeSettings()
        {
            System.Threading.Tasks.Task.Run(async () => await LoadDiscoveryCacheAsync().ConfigureAwait(false))
                .SafeFireAndForget(ex => TM.App.Log($"[ChatModeSettings] 缓存加载失败: {ex.Message}"));
        }

        private static string BuildCacheKey(string? providerId, string? endpoint, string modelId)
        {
            var pid = string.IsNullOrWhiteSpace(providerId) ? string.Empty : providerId.Trim().ToLowerInvariant();
            var ep = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.TrimEnd('/').ToLowerInvariant();
            if (string.IsNullOrEmpty(pid) && string.IsNullOrEmpty(ep))
                return modelId;
            if (string.IsNullOrEmpty(pid))
                return $"{ep}::{modelId}";
            if (string.IsNullOrEmpty(ep))
                return $"{pid}::{modelId}";
            return $"{pid}::{ep}::{modelId}";
        }

        private static string BuildLegacyCacheKey(string? endpoint, string modelId)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return modelId;
            var ep = endpoint.TrimEnd('/').ToLowerInvariant();
            return $"{ep}::{modelId}";
        }

        internal static string MaskKeyEndpointForLog(string? key)
        {
            if (string.IsNullOrEmpty(key)) return key ?? string.Empty;
            var parts = key!.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length >= 3 && TianmingProviderIdentity.IsTianmingPrivate(parts[0]))
            {
                parts[1] = TianmingProviderIdentity.MaskedEndpointLabel;
                return string.Join("::", parts);
            }
            return key!;
        }

        private static bool TryGetDiscoveryWithFallback(DiscoveryCache<int> cache, string modelId, string? endpoint, string? providerId, [NotNullWhen(true)] out DiscoveryRecord<int>? record)
        {
            record = null;
            if (string.IsNullOrEmpty(modelId)) return false;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            if (cache.TryGet(key, out record)) return true;

            var legacyKey = BuildLegacyCacheKey(endpoint, modelId);
            if (!string.Equals(legacyKey, key, StringComparison.OrdinalIgnoreCase)
                && cache.TryGet(legacyKey, out record))
                return true;

            if (string.IsNullOrWhiteSpace(providerId)
                && string.IsNullOrWhiteSpace(endpoint)
                && !string.Equals(modelId, key, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(modelId, legacyKey, StringComparison.OrdinalIgnoreCase)
                && cache.TryGet(modelId, out record))
                return true;

            record = null;
            return false;
        }

        private static bool TryGetMaxOutputWithFallback(string modelId, string? endpoint, string? providerId, out int value)
        {
            value = 0;
            if (TryGetDiscoveryWithFallback(_maxOutputCache, modelId, endpoint, providerId, out var record)
                && record.Value > 0)
            {
                value = record.Value;
                return true;
            }
            return false;
        }

        private static bool TryGetContextWindowWithFallback(string modelId, string? endpoint, string? providerId, out int value)
        {
            value = 0;
            if (TryGetDiscoveryWithFallback(_contextWindowCache, modelId, endpoint, providerId, out var record)
                && record.Value > 0)
            {
                value = record.Value;
                return true;
            }
            return false;
        }

        private static bool IsMaxTokensUnsupported(string modelId, string? endpoint, string? providerId)
            => TryGetDiscoveryWithFallback(_maxOutputCache, modelId, endpoint, providerId, out var record)
                && record.Value == -1;

        private static bool TryReadDiscoverySource(JsonElement element, out DiscoverySource source)
        {
            if (element.ValueKind == JsonValueKind.String
                && Enum.TryParse(element.GetString(), true, out source))
                return true;
            if (element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var raw)
                && Enum.IsDefined(typeof(DiscoverySource), raw))
            {
                source = (DiscoverySource)raw;
                return true;
            }
            source = DiscoverySource.Unknown;
            return false;
        }

        private static string GetCacheFilePath()
        {
            try
            {
                return TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetFilePath(
                    "Services",
                    "AI/SemanticKernel",
                    "model_discovery_cache.json");
            }
            catch { return string.Empty; }
        }

        private static async System.Threading.Tasks.Task LoadDiscoveryCacheAsync()
        {
            var loadVersion = Volatile.Read(ref _cacheMutationVersion);
            try
            {
                var path = GetCacheFilePath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var loadedMaxOutput = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var loadedContextWindow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var moTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var cwTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("MaxOutputTimestamps", out var moTs))
                    foreach (var kv in moTs.EnumerateObject())
                        if (kv.Value.TryGetInt64(out var t)) moTicks[kv.Name] = t;
                if (root.TryGetProperty("ContextWindowTimestamps", out var cwTs))
                    foreach (var kv in cwTs.EnumerateObject())
                        if (kv.Value.TryGetInt64(out var t)) cwTicks[kv.Name] = t;

                var now = DateTime.UtcNow.Ticks;
                if (root.TryGetProperty("MaxOutput", out var mo))
                    foreach (var kv in mo.EnumerateObject())
                    {
                        if (!kv.Value.TryGetInt32(out var v) || v == 0) continue;
                        if (moTicks.TryGetValue(kv.Name, out var t) && now - t > _cacheTtl.Ticks) continue;
                        loadedMaxOutput[kv.Name] = v;
                    }

                if (root.TryGetProperty("ContextWindow", out var cw))
                    foreach (var kv in cw.EnumerateObject())
                    {
                        if (!kv.Value.TryGetInt32(out var v) || v <= 0) continue;
                        if (cwTicks.TryGetValue(kv.Name, out var t) && now - t > _cacheTtl.Ticks) continue;
                        if (v > 4096 && v < 8192) continue;
                        loadedContextWindow[kv.Name] = v;
                    }

                if (root.TryGetProperty("UnsupportedParams", out var unsupSection))
                    foreach (var kv in unsupSection.EnumerateObject())
                    {
                        var paramSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in kv.Value.EnumerateArray())
                        {
                            var pn = item.GetString();
                            if (!string.IsNullOrEmpty(pn)) paramSet.Add(pn);
                        }
                        if (paramSet.Count > 0)
                            _endpointUnsupportedParams[kv.Name] = paramSet;
                    }

                if (loadVersion != Volatile.Read(ref _cacheMutationVersion))
                    return;

                LoadReasoningCapsFromJson(root, now);

                var moAuthoritative = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("MaxOutputAuthoritative", out var moAuth))
                    foreach (var kv in moAuth.EnumerateObject())
                        if (kv.Value.ValueKind == JsonValueKind.True || kv.Value.ValueKind == JsonValueKind.False)
                            moAuthoritative[kv.Name] = kv.Value.GetBoolean();

                var moSources = new Dictionary<string, DiscoverySource>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("MaxOutputSources", out var moSrc))
                    foreach (var kv in moSrc.EnumerateObject())
                        if (TryReadDiscoverySource(kv.Value, out var src) && src != DiscoverySource.Unknown)
                            moSources[kv.Name] = src;

                var cwSources = new Dictionary<string, DiscoverySource>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("ContextWindowSources", out var cwSrc))
                    foreach (var kv in cwSrc.EnumerateObject())
                        if (TryReadDiscoverySource(kv.Value, out var src) && src != DiscoverySource.Unknown)
                            cwSources[kv.Name] = src;

                foreach (var kv in loadedMaxOutput)
                {
                    var source = moSources.TryGetValue(kv.Key, out var src)
                        ? src
                        : (kv.Value < 0
                            ? DiscoverySource.ErrorParsed
                            : (moAuthoritative.TryGetValue(kv.Key, out var auth) && auth
                                ? DiscoverySource.ProbedExact
                                : DiscoverySource.Family));
                    _maxOutputCache.ForceSet(kv.Key, kv.Value, source);
                }

                foreach (var kv in loadedContextWindow)
                {
                    var source = cwSources.TryGetValue(kv.Key, out var src)
                        ? src
                        : DiscoverySource.Declared;
                    _contextWindowCache.ForceSet(kv.Key, kv.Value, source);
                }

                TM.App.Log($"[ChatModeSettings] 已加载探测缓存: MaxOutput={_maxOutputCache.Count}, ContextWindow={_contextWindowCache.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChatModeSettings] 加载探测缓存失败: {ex.Message}");
            }
        }

        private static void SaveDiscoveryCacheAsync()
        {
            lock (_cacheSaveLock) { _cacheDirty = true; }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(500).ConfigureAwait(false);
                    bool shouldSave;
                    lock (_cacheSaveLock) { shouldSave = _cacheDirty; _cacheDirty = false; }
                    if (!shouldSave) return;

                    var path = GetCacheFilePath();
                    if (string.IsNullOrEmpty(path)) return;

                    var unsupDict = new Dictionary<string, string[]>();
                    foreach (var kvp in _endpointUnsupportedParams)
                    {
                        string[] arr;
                        lock (kvp.Value) { arr = kvp.Value.ToArray(); }
                        if (arr.Length > 0) unsupDict[kvp.Key] = arr;
                    }

                    var maxOutputSnapshot = _maxOutputCache.Snapshot();
                    var contextWindowSnapshot = _contextWindowCache.Snapshot();

                    var obj = new
                    {
                        MaxOutput = maxOutputSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase),
                        ContextWindow = contextWindowSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase),
                        MaxOutputTimestamps = maxOutputSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Timestamp.Ticks, StringComparer.OrdinalIgnoreCase),
                        ContextWindowTimestamps = contextWindowSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Timestamp.Ticks, StringComparer.OrdinalIgnoreCase),
                        MaxOutputSources = maxOutputSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Source.ToString(), StringComparer.OrdinalIgnoreCase),
                        ContextWindowSources = contextWindowSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Source.ToString(), StringComparer.OrdinalIgnoreCase),
                        MaxOutputAuthoritative = maxOutputSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Source >= DiscoverySource.ProbedExact || kv.Value.Source == DiscoverySource.ErrorParsed, StringComparer.OrdinalIgnoreCase),
                        UnsupportedParams = unsupDict,
                        ReasoningCaps = ExportReasoningCaps()
                    };
                    var json = JsonSerializer.Serialize(obj, JsonHelper.Default);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ChatModeSettings] 保存探测缓存失败: {ex.Message}");
                }
            }).SafeFireAndForget(ex => TM.App.Log($"[ChatModeSettings] 保存异常: {ex.Message}"));
        }

        private static readonly object _infoLogLock = new();
        private static string? _lastInfoSummary;
        private static string? _lastAdaptiveSummary;
        private static string? _lastTokenDecisionSummary;
        private static string? _lastStripSummary;

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "ChatModeSettings");

        private static void InfoLogWhenChanged(ref string? lastValue, string newValue)
        {
            if (TM.App.IsDebugMode)
            {
                TM.App.Log(newValue);
                return;
            }

            lock (_infoLogLock)
            {
                if (string.Equals(lastValue, newValue, StringComparison.Ordinal))
                {
                    return;
                }

                lastValue = newValue;
            }

            TM.App.Log(newValue);
        }

        public static PromptExecutionSettings GetExecutionSettings(ChatMode mode, double temperature = 0.7, int? overrideMaxTokens = null, UserConfiguration? overrideConfig = null)
        {
            int? maxTokens = null;
            double frequencyPenalty = 0.1;
            double? topP = null;
            double? presencePenalty = null;
            string? seedRaw = null;
            string? stopSequencesRaw = null;

            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var config = overrideConfig ?? aiService.GetActiveConfiguration();
                if (config != null)
                {
                    if (config.MaxTokens > 0)
                    {
                        maxTokens = config.MaxTokens;
                    }

                    if (maxTokens == null)
                    {
                        InfoLogWhenChanged(ref _lastTokenDecisionSummary, $"[ChatModeSettings] MaxTokens=AUTO");
                    }

                    if (config.Temperature >= 0)
                    {
                        temperature = config.Temperature;
                    }

                    frequencyPenalty = Math.Clamp(config.FrequencyPenalty, -2.0, 2.0);

                    if (config.TopP > 0 && config.TopP < 1.0)
                    {
                        topP = Math.Clamp(config.TopP, 0.01, 1.0);
                    }
                    if (Math.Abs(config.PresencePenalty) > 0.0001)
                    {
                        presencePenalty = Math.Clamp(config.PresencePenalty, -2.0, 2.0);
                    }
                    seedRaw = config.Seed;
                    stopSequencesRaw = config.StopSequences;

                    var modelIdForCheck = config.ModelId ?? string.Empty;
                    var effectiveSafeLimit = BuildEffectiveMaxOutputCeiling(modelIdForCheck, config.CustomEndpoint, config.ProviderId);
                    if (effectiveSafeLimit > 0 && maxTokens.HasValue && maxTokens.Value > effectiveSafeLimit)
                    {
                        maxTokens = effectiveSafeLimit;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChatModeSettings] 读取配置失败: {ex.Message}");
            }

            if (overrideMaxTokens.HasValue && overrideMaxTokens.Value > 0)
            {
                maxTokens = overrideMaxTokens.Value;
                InfoLogWhenChanged(ref _lastTokenDecisionSummary, $"[ChatModeSettings] override MaxTokens={maxTokens}");
            }

            LastUsedMaxTokens = maxTokens ?? 0;

            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = temperature,
                MaxTokens = maxTokens,
                FrequencyPenalty = frequencyPenalty
            };

            if (topP.HasValue) settings.TopP = topP.Value;
            if (presencePenalty.HasValue) settings.PresencePenalty = presencePenalty.Value;

            if (!string.IsNullOrWhiteSpace(stopSequencesRaw))
            {
                var stopList = stopSequencesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
                if (stopList.Count > 0)
                {
                    settings.StopSequences = stopList;
                }
            }

            if (!string.IsNullOrWhiteSpace(seedRaw) && int.TryParse(seedRaw.Trim(), out var seedValue))
            {
                settings.ExtensionData ??= new System.Collections.Generic.Dictionary<string, object>();
                settings.ExtensionData["seed"] = seedValue;
            }

            settings.ExtensionData ??= new System.Collections.Generic.Dictionary<string, object>();
            if (!settings.ExtensionData.ContainsKey("stream_options"))
            {
                settings.ExtensionData["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true
                };
            }

            switch (mode)
            {
                case ChatMode.Edit:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                case ChatMode.Agent:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                case ChatMode.Plan:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                case ChatMode.Channel:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                case ChatMode.Business:
                    settings.FunctionChoiceBehavior = null;
                    break;

                default:
                    settings.FunctionChoiceBehavior = null;
                    break;
            }

            InfoLogWhenChanged(
                ref _lastInfoSummary,
                $"[ChatModeSettings] Mode={mode}, MaxTokens={maxTokens?.ToString() ?? "null(MAX)"}, Temperature={temperature}, FrequencyPenalty={frequencyPenalty}, FunctionChoice={(settings.FunctionChoiceBehavior != null ? "Auto" : "None")}");

            try
            {
                var stripConfig = overrideConfig ?? ServiceLocator.Get<AIService>()?.GetActiveConfiguration();
                if (stripConfig != null)
                    StripUnsupportedParams(settings, stripConfig.ProviderId, stripConfig.CustomEndpoint, stripConfig.ModelId);
            }
            catch { }

            return settings;
        }

        public static PromptExecutionSettings GetExecutionSettings(ChatMode mode, ChatHistory history, double temperature = 0.7, int? overrideMaxTokens = null, UserConfiguration? overrideConfig = null)
        {
            var baseSettings = GetExecutionSettings(mode, temperature, overrideMaxTokens, overrideConfig);

            int? baseMaxTokens = (baseSettings is OpenAIPromptExecutionSettings oaiBase)
                ? oaiBase.MaxTokens
                : null;

            int? adaptive;
            if (!baseMaxTokens.HasValue)
            {
                adaptive = GetAutoMaxTokens(history, overrideConfig);
            }
            else
            {
                adaptive = GetAdaptiveMaxTokens(history, baseMaxTokens, overrideConfig);
            }

            if (baseSettings is OpenAIPromptExecutionSettings oaiAdaptive)
            {
                oaiAdaptive.MaxTokens = adaptive;
            }
            else
            {
                baseSettings.ExtensionData ??= new Dictionary<string, object>();
                if (adaptive.HasValue)
                    baseSettings.ExtensionData["max_tokens"] = adaptive.Value;
            }

            LastUsedMaxTokens = adaptive ?? 0;
            InfoLogWhenChanged(
                ref _lastAdaptiveSummary,
                $"[ChatModeSettings] Adaptive max_tokens: base={baseMaxTokens?.ToString() ?? "null(MAX)"}, adaptive={adaptive?.ToString() ?? "null(MAX)"}");

            return baseSettings;
        }

        internal static int BuildEffectiveMaxOutputCeiling(
            string modelId, string? endpoint, string? providerId, int contextWindow)
        {
            if (string.IsNullOrEmpty(modelId)) return 0;

            var candidates = new List<(int Value, DiscoverySource Source)>();

            if (TryGetDiscoveryWithFallback(_maxOutputCache, modelId, endpoint, providerId, out var discovered)
                && discovered.Value > 0)
                candidates.Add((discovered.Value, discovered.Source));

            var family = SKChatService.GetFamilyMaxOutput(modelId);
            if (family > 0)
                candidates.Add((family, DiscoverySource.Family));

            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var model = aiService?.GetModelById(modelId);
                if (model?.MaxOutputTokens > 0)
                {
                    candidates.Add((model.MaxOutputTokens, DiscoverySource.Family));
                }
            }
            catch { }

            int ceiling;
            if (candidates.Count == 0)
                ceiling = 8192;
            else
            {
                var topSource = candidates.Max(c => c.Source);
                ceiling = candidates.Where(c => c.Source == topSource).Max(c => c.Value);
            }

            if (contextWindow > 0)
            {
                var safetyMargin = Math.Max(768, (int)(contextWindow * 0.015));
                var bounded = Math.Max(256, contextWindow - safetyMargin);
                if (ceiling > bounded) ceiling = bounded;
            }

            return ceiling;
        }

        internal static int BuildEffectiveMaxOutputCeiling(string modelId, string? endpoint, string? providerId)
            => BuildEffectiveMaxOutputCeiling(modelId, endpoint, providerId, 0);

        internal static int BuildEffectiveContextWindow(
            string modelId, string? endpoint, string? providerId, UserConfiguration? config = null)
        {
            if (string.IsNullOrEmpty(modelId)) return 0;

            var candidates = new List<(int Value, DiscoverySource Source)>();

            if (config != null)
            {
                var userCw = GetUserConfiguredContextWindow(config);
                if (userCw > 0)
                    candidates.Add((userCw, DiscoverySource.Declared));
            }

            if (TryGetDiscoveryWithFallback(_contextWindowCache, modelId, endpoint, providerId, out var discovered)
                && discovered.Value > 0)
                candidates.Add((discovered.Value, discovered.Source));

            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var model = aiService?.GetModelById(modelId);
                if (model?.ContextWindow > 0)
                {
                    candidates.Add((model.ContextWindow, DiscoverySource.Family));
                }
            }
            catch { }

            var family = SKChatService.GetFamilyContextWindow(modelId);
            if (family > 0)
                candidates.Add((family, DiscoverySource.Family));

            if (candidates.Count == 0) return 0;

            var topSource = candidates.Max(c => c.Source);
            return candidates.Where(c => c.Source == topSource).Max(c => c.Value);
        }

        private static int? GetAutoMaxTokens(ChatHistory history, UserConfiguration? overrideConfig = null)
        {
            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var config = overrideConfig ?? aiService.GetActiveConfiguration();
                if (config == null) return null;

                var modelId = config.ModelId ?? string.Empty;
                var endpoint = config.CustomEndpoint;
                var providerId = config.ProviderId;

                var cacheKey = BuildCacheKey(providerId, endpoint, modelId);

                if (IsMaxTokensUnsupported(modelId, endpoint, providerId))
                {
                    InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                        $"[ChatModeSettings] AUTO=null（端点不支持 max_tokens 参数）");
                    return null;
                }

                if (!string.IsNullOrEmpty(modelId)
                    && TryGetMaxOutputWithFallback(modelId, endpoint, providerId, out var discovered)
                    && discovered > 0)
                {
                    if (TryMarkProbe(_modelMaxOutputProbeTicks, cacheKey))
                    {
                        var probe = GetNextLargerFromLadder(discovered);
                        if (probe.HasValue)
                        {
                            discovered = probe.Value;
                            _modelLastMaxOutputProbeRequested[cacheKey] = discovered;
                        }
                    }

                    var model0 = aiService.GetModelById(config.ModelId!);
                    var userCw0 = GetUserConfiguredContextWindow(config);
                    var cw0 = BuildEffectiveContextWindow(modelId, endpoint, providerId, config);

                    if (cw0 > 0
                        && userCw0 <= 0
                        && (model0 == null || model0.ContextWindow <= 0)
                        && TryMarkProbe(_modelContextWindowProbeTicks, cacheKey))
                    {
                        cw0 = 0;
                    }
                    if (cw0 > 0)
                    {
                        var input0 = EstimateTokensFromHistory(history);
                        var avail0 = Math.Max(256, cw0 - input0 - 768);
                        var capped = Math.Min(discovered, avail0);
                        InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                            $"[ChatModeSettings] AUTO使用已探测输出上限(含窗口裁剪): {MaskKeyEndpointForLog(cacheKey)} discovered={discovered}, cw={cw0}, input={input0}, result={capped}");
                        return capped;
                    }
                    InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                        $"[ChatModeSettings] AUTO使用已探测到的输出上限: {MaskKeyEndpointForLog(cacheKey)} = {discovered}");
                    return discovered;
                }

                var model = aiService.GetModelById(config.ModelId!);
                var userCw = GetUserConfiguredContextWindow(config);
                var contextWindow = BuildEffectiveContextWindow(modelId, endpoint, providerId, config);

                if (contextWindow > 0
                    && userCw <= 0
                    && (model == null || model.ContextWindow <= 0)
                    && TryMarkProbe(_modelContextWindowProbeTicks, cacheKey))
                {
                    contextWindow = 0;
                }

                if (contextWindow <= 0)
                {
                    var fallbackDiscoveredMax = TryGetMaxOutputWithFallback(modelId, endpoint, providerId, out var fdm) && fdm > 0 ? fdm : 0;
                    if (fallbackDiscoveredMax > 0)
                    {
                        InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                            $"[ChatModeSettings] AUTO=已探测输出上限: {fallbackDiscoveredMax}（CW未知但MaxOutput已知）");
                        return fallbackDiscoveredMax;
                    }
                    const int universalFallbackMaxOutput = 131072;
                    InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                        $"[ChatModeSettings] AUTO=兜底输出上限: {universalFallbackMaxOutput}（CW未知，主动探测满血输出）");
                    return universalFallbackMaxOutput;
                }

                var estimatedInputTokens = EstimateTokensFromHistory(history);
                var safetyMargin = Math.Max(768, (int)(contextWindow * 0.015));
                var allowedByWindow = contextWindow - estimatedInputTokens - safetyMargin;
                if (allowedByWindow < 256) allowedByWindow = 256;

                var ceiling = BuildEffectiveMaxOutputCeiling(modelId, endpoint, providerId);
                var result = Math.Min(ceiling, allowedByWindow);
                InfoLogWhenChanged(ref _lastTokenDecisionSummary,
                    $"[ChatModeSettings] AUTO吃满可用空间: contextWindow={contextWindow}, input={estimatedInputTokens}, ceiling={ceiling}, maxOut={result}");
                return result;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetAutoMaxTokens), ex);
                return null;
            }
        }

    }
}

