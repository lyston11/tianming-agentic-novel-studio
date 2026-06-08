using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public enum ReasoningFamily
    {
        None,
        Level,
        Budget,
        Bool
    }
}

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static partial class ChatModeSettings
    {
        private static readonly TimeSpan ReasoningCapTtl = TimeSpan.FromHours(24);

        private static readonly Dictionary<string, int> _effortLevels = new(StringComparer.OrdinalIgnoreCase)
        {
            { "",        0 },
            { "none",    0 },
            { "minimal", 1 },
            { "low",     2 },
            { "medium",  3 },
            { "high",    4 },
            { "xhigh",   5 },
            { "max",     6 }
        };

        private static readonly ConcurrentDictionary<string, (string Effort, long Ticks)> _effortCap =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, long> _thinkingDisabledCap =
            new(StringComparer.OrdinalIgnoreCase);

        public static string? GetEffortCap(string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return null;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            if (!_effortCap.TryGetValue(key, out var entry)) return null;
            if (DateTime.UtcNow.Ticks - entry.Ticks > ReasoningCapTtl.Ticks)
            {
                _effortCap.TryRemove(key, out _);
                return null;
            }
            return entry.Effort;
        }

        public static bool IsThinkingDisabledByCap(string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return false;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            if (!_thinkingDisabledCap.TryGetValue(key, out var ticks)) return false;
            if (DateTime.UtcNow.Ticks - ticks > ReasoningCapTtl.Ticks)
            {
                _thinkingDisabledCap.TryRemove(key, out _);
                return false;
            }
            return true;
        }

        private static Dictionary<string, object> ExportReasoningCaps()
        {
            var effort = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _effortCap)
            {
                if (DateTime.UtcNow.Ticks - kv.Value.Ticks <= ReasoningCapTtl.Ticks)
                    effort[kv.Key] = new { value = kv.Value.Effort, ticks = kv.Value.Ticks };
            }

            var disabled = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _thinkingDisabledCap)
            {
                if (DateTime.UtcNow.Ticks - kv.Value <= ReasoningCapTtl.Ticks)
                    disabled[kv.Key] = kv.Value;
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Effort"] = effort,
                ["Disabled"] = disabled
            };
        }

        private static void LoadReasoningCapsFromJson(JsonElement root, long nowTicks)
        {
            if (!root.TryGetProperty("ReasoningCaps", out var caps)) return;

            if (caps.TryGetProperty("Effort", out var effort))
            {
                foreach (var kv in effort.EnumerateObject())
                {
                    if (kv.Value.ValueKind != JsonValueKind.Object) continue;
                    var value = kv.Value.TryGetProperty("value", out var ve) ? ve.GetString() ?? string.Empty : string.Empty;
                    var ticks = kv.Value.TryGetProperty("ticks", out var te) && te.TryGetInt64(out var t) ? t : nowTicks;
                    if (nowTicks - ticks <= ReasoningCapTtl.Ticks)
                        _effortCap[kv.Name] = (value, ticks);
                }
            }

            if (caps.TryGetProperty("Disabled", out var disabled))
            {
                foreach (var kv in disabled.EnumerateObject())
                {
                    if (!kv.Value.TryGetInt64(out var ticks)) continue;
                    if (nowTicks - ticks <= ReasoningCapTtl.Ticks)
                        _thinkingDisabledCap[kv.Name] = ticks;
                }
            }
        }

        public static void RecordEffortCap(string? providerId, string? endpoint, string? modelId, string newCap)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var newLevel = GetEffortLevel(newCap);
            if (_effortCap.TryGetValue(key, out var existing))
            {
                if (DateTime.UtcNow.Ticks - existing.Ticks <= ReasoningCapTtl.Ticks
                    && GetEffortLevel(existing.Effort) <= newLevel)
                {
                    return;
                }
            }
            var normalized = (newCap ?? string.Empty).Trim().ToLowerInvariant();
            _effortCap[key] = (normalized, DateTime.UtcNow.Ticks);
            Interlocked.Increment(ref _cacheMutationVersion);
            TM.App.Log($"[ChatModeSettings] 推理等级 cap 已记录: {MaskKeyEndpointForLog(key)} → {(string.IsNullOrEmpty(normalized) ? "(关闭)" : normalized)}");
            SaveDiscoveryCacheAsync();
        }

        public static void RecordThinkingDisabled(string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            _thinkingDisabledCap[key] = DateTime.UtcNow.Ticks;
            Interlocked.Increment(ref _cacheMutationVersion);
            TM.App.Log($"[ChatModeSettings] 思考开关 cap 已记录: {MaskKeyEndpointForLog(key)} → 关闭");
            SaveDiscoveryCacheAsync();
        }

        public static void ClearReasoningCaps(string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var legacyKey = BuildLegacyCacheKey(endpoint, modelId);
            var keys = new[] { key, legacyKey, modelId }
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var any = false;
            foreach (var k in keys)
            {
                any |= _effortCap.TryRemove(k, out _);
                any |= _thinkingDisabledCap.TryRemove(k, out _);
            }
            if (any)
            {
                Interlocked.Increment(ref _cacheMutationVersion);
                TM.App.Log($"[ChatModeSettings] 推理 cap 已清除: {MaskKeyEndpointForLog(key)}");
                SaveDiscoveryCacheAsync();
            }
        }

        public static string ApplyEffortCap(string userEffort, string? cap)
        {
            if (cap == null) return userEffort ?? string.Empty;
            var u = GetEffortLevel(userEffort);
            var c = GetEffortLevel(cap);
            return c < u ? (cap ?? string.Empty) : (userEffort ?? string.Empty);
        }

        public static string GetLowerEffort(string current)
        {
            return (current ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "max" => "xhigh",
                "xhigh" => "high",
                "high" => "medium",
                "medium" => "low",
                "low" => "minimal",
                "minimal" => string.Empty,
                "none" => string.Empty,
                _ => string.Empty
            };
        }

        public static string FormatEffort(string effort)
        {
            effort = (effort ?? string.Empty).Trim().ToLowerInvariant();
            return effort switch
            {
                "max" => "Max",
                "xhigh" => "XHigh",
                "high" => "High",
                "medium" => "Medium",
                "low" => "Low",
                "minimal" => "Minimal",
                "none" => "不注入字段",
                _ => string.IsNullOrEmpty(effort) ? "不注入字段" : effort
            };
        }

        private static int GetEffortLevel(string? effort)
        {
            if (string.IsNullOrWhiteSpace(effort)) return 0;
            var key = effort.Trim().ToLowerInvariant();
            return _effortLevels.TryGetValue(key, out var v) ? v : 0;
        }

        public static ReasoningFamily ClassifyReasoningFamily(UserConfiguration? config)
        {
            if (config == null) return ReasoningFamily.None;
            var modelId = (config.ModelId ?? string.Empty).ToLowerInvariant();
            var endpoint = config.CustomEndpoint ?? string.Empty;

            if (endpoint.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
                return ReasoningFamily.Level;

            if (modelId.StartsWith("deepseek-r", StringComparison.Ordinal)
                || modelId.Contains("deepseek-r1")
                || modelId.Contains("deepseek-reasoner"))
                return ReasoningFamily.None;

            if (modelId.Contains("claude"))
                return ReasoningFamily.Budget;

            if (modelId.Contains("gemini")
                && (modelId.Contains("think") || modelId.Contains("flash")
                    || (Regex.IsMatch(modelId, @"gemini-[2-9][^0-9]|gemini-[2-9]$") && modelId.Contains("pro"))
                    || Regex.IsMatch(modelId, @"gemini-[3-9]")))
                return ReasoningFamily.Budget;

            if (modelId.Contains("qwen3") || modelId.Contains("qwq") || modelId.Contains("qvq"))
                return ReasoningFamily.Bool;
            if (modelId.Contains("deepseek") && Regex.IsMatch(modelId, @"v(?:3\.(?:[1-9]\d*)|(?:[4-9]|[1-9]\d+)(?:\.\d+)?)(?:\D|$)"))
                return ReasoningFamily.Bool;
            if (modelId.Contains("doubao") || modelId.Contains("ark"))
                return ReasoningFamily.Bool;
            if (modelId.Contains("hunyuan")
                && (Regex.IsMatch(modelId, @"t\d") || modelId.Contains("think") || modelId.Contains("turbo")))
                return ReasoningFamily.Bool;
            if (modelId.StartsWith("step", StringComparison.Ordinal)
                && (modelId.Contains("think") || modelId.Contains("-r") || Regex.IsMatch(modelId, @"step-[2-9]")))
                return ReasoningFamily.Bool;
            if (modelId.Contains("seed"))
                return ReasoningFamily.Bool;
            if (modelId.StartsWith("mimo", StringComparison.Ordinal))
                return ReasoningFamily.Bool;
            if (Regex.IsMatch(modelId, @"^kimi-?k[2-9]"))
                return ReasoningFamily.Bool;
            if (modelId.Contains("glm")
                && (Regex.IsMatch(modelId, @"^glm-?5|^glm-4\.[5-9]") || modelId.Contains("think")))
                return ReasoningFamily.Bool;
            if (Regex.IsMatch(modelId, @"[-_](thinking|reasoner|reason|think)([-_]|$)")
                || modelId.EndsWith("-thinking", StringComparison.Ordinal)
                || modelId.EndsWith("-reasoner", StringComparison.Ordinal)
                || modelId.EndsWith("-think", StringComparison.Ordinal))
                return ReasoningFamily.Bool;

            if (modelId.Contains("grok"))
                return (modelId.Contains("think") || modelId.Contains("reason"))
                    ? ReasoningFamily.Budget
                    : ReasoningFamily.Level;

            return ReasoningFamily.Level;
        }

        public static bool TryRecordReasoningCapForFailure(UserConfiguration? config, out ReasoningFamily family, out string fromDesc, out string toDesc)
        {
            family = ClassifyReasoningFamily(config);
            fromDesc = string.Empty;
            toDesc = string.Empty;
            if (config == null || string.IsNullOrEmpty(config.ModelId)) return false;

            var providerId = config.ProviderId;
            var endpoint = config.CustomEndpoint;
            var modelId = config.ModelId;

            switch (family)
            {
                case ReasoningFamily.Level:
                    {
                        var raw = EffortConstants.Normalize(config.ReasoningEffort);
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            raw = EffortConstants.Normalize(ModelFamilyClassifier.GetDefaultEffort(modelId, providerId));
                            if (string.IsNullOrWhiteSpace(raw))
                            {
                                raw = EffortConstants.Medium;
                            }
                        }
                        var existingCap = GetEffortCap(providerId, endpoint, modelId);
                        var effective = ApplyEffortCap(raw, existingCap);
                        if (string.IsNullOrEmpty(effective)) return false;
                        var next = GetLowerEffort(effective);
                        if (string.Equals(effective, next, StringComparison.OrdinalIgnoreCase)) return false;
                        RecordEffortCap(providerId, endpoint, modelId, next);
                        fromDesc = FormatEffort(effective);
                        toDesc = FormatEffort(next);
                        return true;
                    }

                case ReasoningFamily.Budget:
                    {
                        if (IsThinkingDisabledByCap(providerId, endpoint, modelId)) return false;
                        RecordThinkingDisabled(providerId, endpoint, modelId);
                        fromDesc = "默认预算";
                        toDesc = "停止注入思考字段";
                        return true;
                    }

                case ReasoningFamily.Bool:
                    {
                        if (IsThinkingDisabledByCap(providerId, endpoint, modelId)) return false;
                        RecordThinkingDisabled(providerId, endpoint, modelId);
                        fromDesc = "启用思考";
                        toDesc = "停止注入思考字段";
                        return true;
                    }

                default:
                    return false;
            }
        }

        public static bool ShouldAttemptReasoningFallback(Exception? ex, TM.Services.Framework.AI.Core.KeyUseResult result)
        {
            if (ex == null) return false;

            if (result is TM.Services.Framework.AI.Core.KeyUseResult.RateLimited
                or TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                or TM.Services.Framework.AI.Core.KeyUseResult.NetworkError
                or TM.Services.Framework.AI.Core.KeyUseResult.ModelNotFound
                or TM.Services.Framework.AI.Core.KeyUseResult.ContentFiltered)
                return false;

            if (IsMaxTokensError(ex) || IsContextWindowError(ex)) return false;

            if (TryParseUnsupportedParamName(ex, out var paramName))
            {
                var p = (paramName ?? string.Empty).Trim().ToLowerInvariant();
                if (p is "reasoning_effort" or "reasoning" or "reasoning.effort"
                    or "include_reasoning" or "reasoning_max_tokens"
                    or "thinking" or "thinking_budget" or "enable_thinking"
                    or "thinkingconfig" or "thinkingbudget" or "budget_tokens"
                    or "extended_thinking")
                    return true;
            }

            var lowerMsg = (ex.Message ?? string.Empty).ToLowerInvariant() + " " + (ex.InnerException?.Message ?? string.Empty).ToLowerInvariant();
            if (lowerMsg.Contains("reasoning") || lowerMsg.Contains("effort")
                || lowerMsg.Contains("thinking") || lowerMsg.Contains("budget_tokens")
                || lowerMsg.Contains("enable_thinking"))
                return true;

            return false;
        }
    }
}
