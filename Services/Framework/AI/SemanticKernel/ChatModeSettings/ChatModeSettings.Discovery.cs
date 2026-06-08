using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public static void RecordDiscoveredMaxOutput(string modelId, int maxOutput, string? endpoint = null, string? providerId = null, bool isAuthoritative = false)
            => RecordDiscoveredMaxOutput(modelId, maxOutput, endpoint, providerId,
                maxOutput < 0 ? DiscoverySource.ErrorParsed : (isAuthoritative ? DiscoverySource.ProbedExact : DiscoverySource.Family));

        public static void RecordDiscoveredMaxOutput(string modelId, int maxOutput, string? endpoint, string? providerId, DiscoverySource source)
        {
            if (string.IsNullOrEmpty(modelId) || maxOutput < -1 || maxOutput == 0) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);

            var written = true;
            if (source == DiscoverySource.ErrorParsed)
            {
                _maxOutputCache.ForceSet(key, maxOutput, source);
            }
            else
            {
                written = _maxOutputCache.Record(key, maxOutput, source);
            }
            if (!written)
            {
                TM.App.Log($"[ChatModeSettings] 跳过 MaxOutput 低可信写入: {MaskKeyEndpointForLog(key)}, value={maxOutput}, source={source}");
                return;
            }

            Interlocked.Increment(ref _cacheMutationVersion);
            TM.App.Log($"[ChatModeSettings] 已缓存模型输出上限: {MaskKeyEndpointForLog(key)} = {maxOutput} (source={source})");
            SaveDiscoveryCacheAsync();
        }

        public static int GetDiscoveredMaxOutput(string modelId, string? endpoint = null, string? providerId = null)
        {
            if (string.IsNullOrEmpty(modelId)) return 0;
            return TryGetMaxOutputWithFallback(modelId, endpoint, providerId, out var v) ? v : 0;
        }

        public static void RecordDiscoveredContextWindow(string modelId, int contextWindow, string? endpoint = null, string? providerId = null)
            => RecordDiscoveredContextWindow(modelId, contextWindow, endpoint, providerId, DiscoverySource.ProbedExact);

        public static void RecordDiscoveredContextWindow(string modelId, int contextWindow, string? endpoint, string? providerId, DiscoverySource source)
        {
            if (string.IsNullOrEmpty(modelId) || contextWindow <= 0) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);

            var written = true;
            if (source == DiscoverySource.ErrorParsed)
            {
                _contextWindowCache.ForceSet(key, contextWindow, source);
            }
            else
            {
                written = _contextWindowCache.Record(key, contextWindow, source);
            }
            if (!written)
            {
                TM.App.Log($"[ChatModeSettings] 跳过 ContextWindow 低可信写入: {MaskKeyEndpointForLog(key)}, value={contextWindow}, source={source}");
                return;
            }

            Interlocked.Increment(ref _cacheMutationVersion);
            TM.App.Log($"[ChatModeSettings] 已缓存模型上下文窗口: {MaskKeyEndpointForLog(key)} = {contextWindow} (source={source})");
            SaveDiscoveryCacheAsync();
        }

        public static void ClearDiscoveredLimits(string modelId, string? endpoint = null, string? providerId = null)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            var key = BuildCacheKey(providerId, endpoint, modelId);
            Interlocked.Increment(ref _cacheMutationVersion);
            _maxOutputCache.TryRemove(key);
            _contextWindowCache.TryRemove(key);

            var legacyKey = BuildLegacyCacheKey(endpoint, modelId);
            if (!string.Equals(legacyKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _maxOutputCache.TryRemove(legacyKey);
                _contextWindowCache.TryRemove(legacyKey);
            }

            _endpointUnsupportedParams.TryRemove(key, out _);

            if (string.IsNullOrWhiteSpace(providerId) && string.IsNullOrWhiteSpace(endpoint))
            {
                _maxOutputCache.TryRemove(modelId);
                _contextWindowCache.TryRemove(modelId);
                _endpointUnsupportedParams.TryRemove(modelId, out _);
            }
            TM.App.Log($"[ChatModeSettings] 已清除发现缓存: {MaskKeyEndpointForLog(key)}");
            SaveDiscoveryCacheAsync();
        }

        public static int GetDiscoveredContextWindow(string modelId, string? endpoint = null, string? providerId = null)
        {
            if (string.IsNullOrEmpty(modelId)) return 0;
            return TryGetContextWindowWithFallback(modelId, endpoint, providerId, out var v) ? v : 0;
        }

        public static bool IsFinishReasonTruncated(string? finishReason)
        {
            if (string.IsNullOrEmpty(finishReason)) return false;
            var fr = finishReason.ToLowerInvariant();
            return fr == "length"
                || fr == "max_tokens"
                || fr == "token_limit"
                || fr == "max_output_tokens"
                || fr == "output_tokens"
                || fr == "maximum_tokens";
        }

        public static int? GetUpgradeMaxTokens(int currentMax, string? modelId = null, string? endpoint = null, string? providerId = null)
        {
            if (currentMax <= 0) return null;

            int ceiling = !string.IsNullOrEmpty(modelId)
                ? BuildEffectiveMaxOutputCeiling(modelId, endpoint, providerId)
                : 0;

            int? upgraded;
            var idx = Array.IndexOf(_autoMaxTokensLadder, currentMax);
            if (idx == 0)
            {
                upgraded = (ceiling > currentMax) ? ceiling : (int?)null;
            }
            else if (idx > 0)
            {
                upgraded = _autoMaxTokensLadder[idx - 1];
            }
            else
            {
                upgraded = null;
                foreach (var v in _autoMaxTokensLadder)
                    if (v > currentMax) { upgraded = v; break; }
                if (!upgraded.HasValue && ceiling > currentMax) upgraded = ceiling;
            }

            if (!upgraded.HasValue) return null;

            if (ceiling > 0 && upgraded.Value > ceiling)
            {
                upgraded = ceiling > currentMax ? ceiling : (int?)null;
                TM.App.Log($"[ChatModeSettings] GetUpgradeMaxTokens 按 ceiling 封顶: {modelId} ceiling={ceiling}, upgraded={upgraded?.ToString() ?? "null(无法升级)"}");
            }

            return upgraded;
        }

        public static bool IsContextWindowError(Exception ex)
        {
            if (ex == null) return false;
            var msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
            var inner = ex.InnerException?.Message?.ToLowerInvariant() ?? string.Empty;
            var keywords = new[] { "context length", "context_length", "context window", "context limit", "too many tokens in your messages", "maximum context", "prompt is too long", "input is too long" };
            foreach (var kw in keywords)
            {
                if (msg.Contains(kw) || inner.Contains(kw))
                    return true;
            }
            if ((msg.Contains("500") || inner.Contains("500")) &&
                (msg.Contains("empty_stream") || inner.Contains("empty_stream")))
                return true;
            return false;
        }

        public static bool TryParseContextWindowLimit(Exception ex, out int contextWindow)
        {
            contextWindow = 0;
            if (ex == null) return false;
            var candidates = new[]
            {
                ex.Message,
                ex.InnerException?.Message,
                ex.InnerException?.InnerException?.Message
            };
            var patterns = new[]
            {
                @"(?:maximum context length|context length of|context window(?:\s+is)?|context limit(?:\s+is)?|max(?:imum)? context(?:\s+is)?)[^\d]{0,20}?(\d{4,8})",
                @"(?:supports up to|up to)[^\d]{0,10}?(\d{4,8})\s*tokens",
                @">\s*(\d{4,8})\s*maximum",
            };
            foreach (var src in candidates)
            {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (var pattern in patterns)
                {
                    var m = Regex.Match(src, pattern, RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed)
                        && parsed >= 1024 && parsed <= 10485760)
                    {
                        contextWindow = parsed;
                        TM.App.Log($"[ChatModeSettings] 从错误消息解析到 ContextWindow: {contextWindow} (src: {src})");
                        return true;
                    }
                }
            }
            return false;
        }

        public static int? GetAdaptiveMaxTokens(ChatHistory history, int? baseMaxTokens, UserConfiguration? overrideConfig = null)
        {
            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var config = overrideConfig ?? aiService.GetActiveConfiguration();
                if (config == null)
                {
                    return baseMaxTokens;
                }

                var ctxModelId = config.ModelId ?? string.Empty;
                var adaptCacheKey = BuildCacheKey(config.ProviderId, config.CustomEndpoint, ctxModelId);
                var model = aiService.GetModelById(config.ModelId!);
                var userCw = GetUserConfiguredContextWindow(config);
                var contextWindow = BuildEffectiveContextWindow(ctxModelId, config.CustomEndpoint, config.ProviderId, config);

                if (contextWindow > 0
                    && userCw <= 0
                    && (model == null || model.ContextWindow <= 0)
                    && TryMarkProbe(_modelContextWindowProbeTicks, adaptCacheKey))
                {
                    contextWindow = 0;
                }

                if (contextWindow <= 0)
                {
                    return baseMaxTokens;
                }

                var estimatedInputTokens = EstimateTokensFromHistory(history);
                var safetyMargin = Math.Max(768, (int)(contextWindow * 0.015));
                var allowedByWindow = contextWindow - estimatedInputTokens - safetyMargin;
                if (allowedByWindow < 256)
                {
                    allowedByWindow = 256;
                }

                var ceiling = BuildEffectiveMaxOutputCeiling(ctxModelId, config.CustomEndpoint, config.ProviderId);
                var effectiveBase = baseMaxTokens.HasValue && baseMaxTokens.Value > 0
                    ? (ceiling > 0 ? Math.Min(baseMaxTokens.Value, ceiling) : baseMaxTokens.Value)
                    : (ceiling > 0 ? ceiling : (int?)null);

                var adaptive = effectiveBase.HasValue && effectiveBase.Value > 0
                    ? Math.Min(effectiveBase.Value, allowedByWindow)
                    : allowedByWindow;

                return adaptive > 0 ? adaptive : (int?)null;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetAdaptiveMaxTokens), ex);
                return baseMaxTokens;
            }
        }

        private static int EstimateTokensFromHistory(ChatHistory history)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(history);

        public static void RecordSuccessObservation(UserConfiguration config, ChatHistory history, PromptExecutionSettings settings, string content)
        {
            try
            {
                if (config == null) return;
                if (string.IsNullOrEmpty(config.ModelId)) return;

                var modelId = config.ModelId ?? string.Empty;
                var cacheKey = BuildCacheKey(config.ProviderId, config.CustomEndpoint, modelId);
                var now = DateTime.UtcNow.Ticks;
                if (_modelSuccessUpgradeTicks.TryGetValue(cacheKey, out var last)
                    && now - last < _successUpgradeDebounce.Ticks)
                    return;

                _modelSuccessUpgradeTicks[cacheKey] = now;

                int usedMaxTokens = 0;
                if (settings is OpenAIPromptExecutionSettings oai && oai.MaxTokens is > 0)
                    usedMaxTokens = oai.MaxTokens.Value;
                else if (settings.ExtensionData?.TryGetValue("max_tokens", out var extMt) == true)
                    usedMaxTokens = extMt switch { int i => i, long l => (int)l, double d => (int)d, _ => 0 };

                if (usedMaxTokens > 0)
                {
                    var current = GetDiscoveredMaxOutput(modelId, config.CustomEndpoint, config.ProviderId);

                    if (_modelLastMaxOutputProbeRequested.TryGetValue(cacheKey, out var probed)
                        && probed > 0
                        && usedMaxTokens >= probed
                        && probed > current)
                    {
                        RecordDiscoveredMaxOutput(modelId, probed, config.CustomEndpoint, config.ProviderId, DiscoverySource.ProbedExact);
                    }
                    else if (usedMaxTokens > current)
                    {
                        RecordDiscoveredMaxOutput(modelId, usedMaxTokens, config.CustomEndpoint, config.ProviderId, DiscoverySource.ProbedBoundary);
                    }
                }

            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(RecordSuccessObservation), ex);
            }
        }

        public static bool RequiresFunctionConfirmation(ChatMode mode)
        {
            return mode == ChatMode.Plan;
        }

        public static bool IsMaxTokensError(Exception ex)
        {
            if (ex == null) return false;
            if (IsUnsupportedParameterError(ex)) return false;

            var msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
            var inner = ex.InnerException?.Message?.ToLowerInvariant() ?? string.Empty;

            var keywords = new[] { "max_tokens", "max output", "output_tokens", "token limit",
                "maximum_tokens", "max_completion_tokens", "output limit", "生成长度超过限制" };
            foreach (var kw in keywords)
            {
                if (msg.Contains(kw) || inner.Contains(kw))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryParseUnsupportedParamName(Exception ex, out string paramName)
        {
            paramName = string.Empty;
            if (ex == null) return false;
            var candidates = new[]
            {
                ex.Message,
                ex.InnerException?.Message,
                ex.InnerException?.InnerException?.Message
            };

            var patterns = new[]
            {
                @"(?:unsupported|unknown|unrecognized)\s+(?:parameter|argument|field)s?[:\s]+['""]?([\w\.\-]+)['""]?",
                "\"(?:param|parameter|argument|field|name)\"\\s*:\\s*\"([^\"]+)\"",
                "'(?:param|parameter|argument|field|name)'\\s*:\\s*'([^']+)'"
            };

            foreach (var src in candidates)
            {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (var p in patterns)
                {
                    var m = Regex.Match(src, p, RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    var raw = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    raw = raw.Trim().Trim('"', '\'', ' ', '\t', '\r', '\n');
                    var comma = raw.IndexOf(',');
                    if (comma > 0) raw = raw[..comma];
                    paramName = raw.Trim().ToLowerInvariant();
                    return true;
                }
            }

            return false;
        }

        public static bool IsUnsupportedParameterError(Exception ex)
            => TryParseUnsupportedParamName(ex, out _);

        public static event Action<string?, string?, string, string>? UnsupportedParamMarked;

        public static void MarkUnsupportedParam(string? providerId, string? endpoint, string modelId, string paramName)
        {
            if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(paramName)) return;
            paramName = paramName.Trim().Trim('"', '\'', ' ', '\t', '\r', '\n').TrimEnd(':', ',').ToLowerInvariant();
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var set = _endpointUnsupportedParams.GetOrAdd(key, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            bool added;
            lock (set) { added = set.Add(paramName); }
            TM.App.Log($"[ChatModeSettings] 已标记端点不支持参数: {MaskKeyEndpointForLog(key)} → {paramName}");
            if (paramName.Contains("max_tokens") || paramName.Contains("max_output") || paramName.Contains("max_completion"))
            {
                RecordDiscoveredMaxOutput(modelId, -1, endpoint, providerId, DiscoverySource.ErrorParsed);
            }
            else if (paramName == "long_context")
            {
                var hasExistingCw = TryGetContextWindowWithFallback(modelId, endpoint, providerId, out _);
                if (!hasExistingCw)
                {
                    var familyCw = SKChatService.GetFamilyContextWindow(modelId);
                    if (familyCw > 0)
                    {
                        RecordDiscoveredContextWindow(modelId, familyCw, endpoint, providerId, DiscoverySource.Family);
                        TM.App.Log($"[ChatModeSettings] 1M 被拒，写入家族基线 cw 兜底: {modelId} → {familyCw}");
                    }
                    else { SaveDiscoveryCacheAsync(); }
                }
                else { SaveDiscoveryCacheAsync(); }
            }
            else
                SaveDiscoveryCacheAsync();

            if (added)
            {
                try { UnsupportedParamMarked?.Invoke(providerId, endpoint, modelId, paramName); }
                catch (Exception ex) { TM.App.Log($"[ChatModeSettings] UnsupportedParamMarked 回调异常: {ex.Message}"); }
            }
        }

        public static void ClearUnsupportedParam(string? providerId, string? endpoint, string modelId, string paramName)
        {
            if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(paramName)) return;
            paramName = paramName.Trim().Trim('"', '\'', ' ', '\t', '\r', '\n').TrimEnd(':', ',').ToLowerInvariant();
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var legacyKey = BuildLegacyCacheKey(endpoint, modelId);
            var keys = new[] { key, legacyKey, modelId }
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var changed = false;
            foreach (var k in keys)
            {
                if (!_endpointUnsupportedParams.TryGetValue(k, out var set)) continue;
                lock (set)
                {
                    changed |= set.Remove(paramName);
                    if (set.Count == 0)
                        _endpointUnsupportedParams.TryRemove(k, out _);
                }
            }

            if (!changed) return;
            Interlocked.Increment(ref _cacheMutationVersion);
            TM.App.Log($"[ChatModeSettings] 已清除端点不支持参数: {MaskKeyEndpointForLog(key)} → {paramName}");
            SaveDiscoveryCacheAsync();
        }

        public static bool IsLongContextRejectedError(Exception ex, UserConfiguration? config)
        {
            if (ex == null || config == null) return false;
            if (config.EnableLongContext != true) return false;

            var sources = new[]
            {
                ex.Message ?? string.Empty,
                ex.InnerException?.Message ?? string.Empty,
                ex.InnerException?.InnerException?.Message ?? string.Empty
            };

            foreach (var raw in sources)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var src = raw.ToLowerInvariant();

                if (src.Contains("context-1m") || src.Contains("anthropic-beta")
                    || src.Contains("1m context") || src.Contains("not eligible for")
                    || (src.Contains("beta") && src.Contains("not supported")))
                    return true;

                bool hasModelNotFound = src.Contains("model not found")
                    || src.Contains("model_not_found")
                    || src.Contains("invalid model")
                    || src.Contains("unknown model")
                    || src.Contains("unsupported model")
                    || src.Contains("no such model")
                    || src.Contains("does not exist");
                if (hasModelNotFound && (src.Contains("[1m]") || src.Contains(":extended"))) return true;
                if (hasModelNotFound
                    && !string.IsNullOrEmpty(config.ModelId)
                    && !config.ModelId.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase)
                    && !config.ModelId.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (src.Contains("input_tokens") && (src.Contains("exceed") || src.Contains("too long") || src.Contains("limit")))
                    return true;
                if (src.Contains("prompt is too long") || src.Contains("context length exceed"))
                    return true;
            }

            return false;
        }

        public static int GetEffectiveContextWindow(UserConfiguration? config)
        {
            if (config == null) return 0;
            return BuildEffectiveContextWindow(config.ModelId, config.CustomEndpoint, config.ProviderId, config);
        }

        private static int GetUserConfiguredContextWindow(UserConfiguration config)
        {
            if (config.EnableLongContext == true
                && config.SupportsLongContext
                && config.LongContextWindow > 0
                && !IsUnsupportedParam(config.ProviderId, config.CustomEndpoint, config.ModelId, "long_context"))
            {
                return config.LongContextWindow;
            }
            return config.ContextWindow;
        }

        public static bool IsAnyThinkingParamUnsupported(string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return false;
            for (int i = 0; i < _thinkingFamilyParamKeys.Length; i++)
            {
                if (IsUnsupportedParam(providerId, endpoint, modelId, _thinkingFamilyParamKeys[i]))
                    return true;
            }
            return false;
        }

        private static readonly string[] _thinkingFamilyParamKeys =
        {
            "reasoning_effort",
            "reasoning",
            "thinking",
            "enable_thinking",
            "thinkingConfig",
            "effort",
            "thinking_budget",
        };

        public static bool IsUnsupportedParam(string? providerId, string? endpoint, string? modelId, string paramName)
        {
            if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(paramName)) return false;
            paramName = paramName.Trim().Trim('"', '\'', ' ', '\t', '\r', '\n').TrimEnd(':', ',').ToLowerInvariant();
            var key = BuildCacheKey(providerId, endpoint, modelId);
            var legacyKey = BuildLegacyCacheKey(endpoint, modelId);
            var keys = new[] { key, legacyKey, modelId }
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var k in keys)
            {
                if (!_endpointUnsupportedParams.TryGetValue(k, out var set)) continue;
                lock (set)
                {
                    if (set.Contains(paramName)) return true;
                }
            }

            return false;
        }

        public static void StripUnsupportedParams(PromptExecutionSettings settings, string? providerId, string? endpoint, string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            var key = BuildCacheKey(providerId, endpoint, modelId!);
            if (!_endpointUnsupportedParams.TryGetValue(key, out var set) || set.Count == 0) return;
            if (settings is not OpenAIPromptExecutionSettings oai) return;

            string[] snapshot;
            lock (set) { snapshot = set.ToArray(); }
            foreach (var param in snapshot)
            {
                switch (param)
                {
                    case "temperature": oai.Temperature = null; break;
                    case "max_tokens":
                    case "max_output_tokens":
                    case "max_completion_tokens": oai.MaxTokens = null; break;
                    case "frequency_penalty": oai.FrequencyPenalty = null; break;
                    case "presence_penalty": oai.PresencePenalty = null; break;
                    case "top_p": oai.TopP = null; break;

                    case "reasoning_effort":
                    case "reasoning.effort":
                        oai.ReasoningEffort = null;
                        oai.ExtensionData?.Remove("reasoning_effort");
                        break;
                    case "reasoning":
                    case "include_reasoning":
                    case "reasoning_max_tokens":
                        oai.ExtensionData?.Remove("reasoning");
                        oai.ExtensionData?.Remove("include_reasoning");
                        oai.ExtensionData?.Remove("reasoning_max_tokens");
                        break;
                    case "effort":
                        oai.ExtensionData?.Remove("effort");
                        break;

                    case "thinking":
                    case "thinking.type":
                    case "thinking.budget_tokens":
                    case "budget_tokens":
                    case "extended_thinking":
                        oai.ExtensionData?.Remove("thinking");
                        oai.ExtensionData?.Remove("extended_thinking");
                        break;

                    case "thinkingconfig":
                    case "thinkingconfig.thinkingbudget":
                    case "thinking_config":
                    case "thinking_config.thinking_budget":
                    case "thinkingbudget":
                        oai.ExtensionData?.Remove("thinkingConfig");
                        oai.ExtensionData?.Remove("thinking_config");
                        break;

                    case "enable_thinking":
                        oai.ExtensionData?.Remove("enable_thinking");
                        oai.ExtensionData?.Remove("thinking_budget");
                        break;
                    case "thinking_budget":
                        oai.ExtensionData?.Remove("thinking_budget");
                        break;

                    case "tools":
                    case "tool_choice":
                    case "function_call":
                    case "functions": oai.FunctionChoiceBehavior = null; break;

                    case "max_input_tokens":
                        oai.ExtensionData?.Remove("max_input_tokens");
                        break;
                }

                if (oai.ExtensionData != null && oai.ExtensionData.Count > 0)
                {
                    var keys = oai.ExtensionData.Keys.ToArray();
                    foreach (var k in keys)
                        if (string.Equals(k, param, StringComparison.OrdinalIgnoreCase))
                            oai.ExtensionData.Remove(k);
                }
            }
            InfoLogWhenChanged(ref _lastStripSummary, $"[ChatModeSettings] 已剥离不支持的参数: {string.Join(", ", snapshot)}");
        }

        public static void InjectLongContextParameters(PromptExecutionSettings settings, UserConfiguration config)
        {
            if (settings == null || config == null) return;
            if (config.EnableLongContext != true || !config.SupportsLongContext) return;
            if (IsUnsupportedParam(config.ProviderId, config.CustomEndpoint, config.ModelId, "long_context")) return;
            if (settings is not OpenAIPromptExecutionSettings oai) return;

            var modelId = (config.ModelId ?? string.Empty).ToLowerInvariant();
            bool isQwenLongContextModel = modelId.Contains("qwen")
                && (modelId.Contains("turbo") || modelId.Contains("long")
                    || modelId.Contains("plus") || modelId.Contains("flash"));

            if (isQwenLongContextModel)
            {
                oai.ExtensionData ??= new Dictionary<string, object>();
                oai.ExtensionData["max_input_tokens"] = 1_000_000;
                TM.App.Log($"[ChatModeSettings] Qwen 1M 上下文注入: max_input_tokens=1000000, model={config.ModelId}");
            }
        }

        public static bool TryParseMaxTokensLimit(Exception ex, out int limit)
        {
            limit = 0;
            if (ex == null) return false;

            var candidates = new[]
            {
                ex.Message,
                ex.InnerException?.Message,
                ex.InnerException?.InnerException?.Message
            };

            var patterns = new[]
            {
                @"max_tokens:\s*\d+\s*>\s*(\d{3,7})\s*maximum",
                @"(?:max_tokens|maximum|output.{0,20}tokens?|token.{0,10}limit|supports at most|at most)\D{0,30}?(\d{3,7})",
                @"(?:<=|<=|not exceed|is limited to|be at most)\s*(\d{3,7})",
                @"(\d{3,7})\s*(?:tokens?|max|maximum|output)",
            };

            foreach (var src in candidates)
            {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (var pattern in patterns)
                {
                    var m = Regex.Match(src, pattern, RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed)
                        && parsed >= 1024 && parsed <= 2097152)
                    {
                        limit = parsed;
                        TM.App.Log($"[ChatModeSettings] 从错误消息解析到 max_tokens 上限: {limit} (src: {src})");
                        return true;
                    }
                }
            }
            return false;
        }

        public static int GetFallbackMaxTokens(int current)
        {
            if (current <= 0) return 32768;
            for (int i = 0; i < _autoMaxTokensLadder.Length; i++)
            {
                var v = _autoMaxTokensLadder[i];
                if (current > v)
                {
                    return v;
                }
                if (current == v)
                {
                    return i + 1 < _autoMaxTokensLadder.Length ? _autoMaxTokensLadder[i + 1] : 4096;
                }
            }
            return 4096;
        }

        private enum ProbeResult { Success, MaxTokensRejected, FatalError }

        public static async Task<int?> ProbeMaxTokensConcurrentAsync(
            Func<int, CancellationToken, Task> tryRequest,
            string modelId, string? endpoint = null, string? providerId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(modelId)) return null;

            var ceiling = BuildEffectiveMaxOutputCeiling(modelId, endpoint, providerId);
            var family = SKChatService.GetFamilyMaxOutput(modelId);
            var probeTop = Math.Max(ceiling, family);
            var probeValues = BuildProbeCandidates(probeTop);
            TM.App.Log($"[ChatModeSettings] 并发探测 max_tokens: [{string.Join(",", probeValues)}], ceiling={ceiling}, family={family}, probeTop={probeTop}");

            var tasks = probeValues.Select(v => Task.Run(async () =>
            {
                try
                {
                    await tryRequest(v, ct).ConfigureAwait(false);
                    return (Value: v, Status: ProbeResult.Success, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    if (IsMaxTokensError(ex) || IsContextWindowError(ex))
                        return (Value: v, Status: ProbeResult.MaxTokensRejected, Error: ex);
                    return (Value: v, Status: ProbeResult.FatalError, Error: ex);
                }
            }, ct)).ToArray();

            try
            {
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                var fatal = results.FirstOrDefault(r => r.Status == ProbeResult.FatalError);
                if (fatal.Status == ProbeResult.FatalError)
                {
                    if (IsTransientError(fatal.Error))
                    {
                        TM.App.Log($"[ChatModeSettings] 探测瞬时失败，不污染缓存: {fatal.Error?.Message}");
                        return null;
                    }
                    return TryFallbackToFamily(modelId, endpoint, providerId, "持久错误");
                }

                var successes = results.Where(r => r.Status == ProbeResult.Success)
                                      .Select(r => r.Value).OrderByDescending(v => v).ToList();
                var rejected = results.Where(r => r.Status == ProbeResult.MaxTokensRejected)
                                      .Select(r => r.Value).OrderBy(v => v).ToList();

                TM.App.Log($"[ChatModeSettings] 并发探测结果: 成功=[{string.Join(",", successes)}], 拒绝=[{string.Join(",", rejected)}]");

                if (successes.Count > 0)
                {
                    var best = successes[0];
                    var source = best >= probeTop ? DiscoverySource.ProbedExact : DiscoverySource.ProbedBoundary;

                    int knownBad = rejected.FirstOrDefault(v => v > best);
                    if (knownBad > 0 && best < probeTop
                        && (knownBad - best) > Math.Max(4096, best / 32))
                    {
                        var refined = await RefineByBinarySearchAsync(
                            tryRequest, best, knownBad, maxIterations: 3, ct).ConfigureAwait(false);
                        if (refined > best)
                        {
                            TM.App.Log($"[ChatModeSettings] 二分收敛: {best} → {refined}");
                            best = refined;
                            source = DiscoverySource.ProbedExact;
                        }
                    }

                    RecordDiscoveredMaxOutput(modelId, best, endpoint, providerId, source);
                    return best;
                }

                return TryFallbackToFamily(modelId, endpoint, providerId, "全部拒绝");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TM.App.Log($"[ChatModeSettings] 并发探测异常: {ex.Message}");
            }

            return null;
        }

        private static int[] BuildProbeCandidates(int ceiling)
        {
            var values = new HashSet<int> { 4096 };

            if (ceiling >= 8192) values.Add(8192);
            if (ceiling >= 16384) values.Add(16384);
            if (ceiling >= 65536) values.Add(65536);
            if (ceiling >= 131072) values.Add(131072);
            if (ceiling >= 262144) values.Add(262144);

            if (ceiling > 0)
            {
                values.Add(ceiling);

                if (ceiling > 16384)
                    values.Add((int)(ceiling * 0.85));
            }

            return values.OrderBy(v => v).ToArray();
        }

        private static async Task<int> RefineByBinarySearchAsync(
            Func<int, CancellationToken, Task> tryRequest,
            int knownGood, int knownBad,
            int maxIterations,
            CancellationToken ct)
        {
            int lo = knownGood, hi = knownBad;
            int best = knownGood;

            for (int i = 0; i < maxIterations; i++)
            {
                if (hi - lo <= Math.Max(4096, lo / 32)) break;
                int mid = lo + (hi - lo) / 2;

                try
                {
                    await tryRequest(mid, ct).ConfigureAwait(false);
                    best = mid; lo = mid;
                }
                catch (Exception ex) when (IsMaxTokensError(ex) || IsContextWindowError(ex))
                {
                    hi = mid;
                }
                catch
                {
                    break;
                }
            }
            return best;
        }

        private static int? TryFallbackToFamily(string modelId, string? endpoint, string? providerId, string reason)
        {
            var familyMo = SKChatService.GetFamilyMaxOutput(modelId);
            if (familyMo > 0)
            {
                RecordDiscoveredMaxOutput(modelId, familyMo, endpoint, providerId, DiscoverySource.Family);
                TM.App.Log($"[ChatModeSettings] 探测{reason}，家族兜底: {modelId}={familyMo}");
                return familyMo;
            }
            return null;
        }

        private static bool IsTransientError(Exception? ex)
        {
            if (ex == null) return false;
            var msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
            var inner = ex.InnerException?.Message?.ToLowerInvariant() ?? string.Empty;
            return msg.Contains("429") || inner.Contains("429")
                || msg.Contains("rate limit") || inner.Contains("rate limit")
                || msg.Contains("timeout") || msg.Contains("timed out") || inner.Contains("timeout") || inner.Contains("timed out")
                || msg.Contains("503") || inner.Contains("503")
                || msg.Contains("502") || inner.Contains("502")
                || msg.Contains("connection refused") || inner.Contains("connection refused")
                || msg.Contains("connection reset") || inner.Contains("connection reset")
                || msg.Contains("network") || inner.Contains("network")
                || msg.Contains("socket") || inner.Contains("socket");
        }

        internal static void SyncLastUsedMaxTokens(int value)
        {
            LastUsedMaxTokens = value > 0 ? value : 0;
        }

        internal static void SyncLastFinishReason(string? value, UserConfiguration? config = null)
        {
            LastFinishReason = value;

            if (config != null && !string.IsNullOrEmpty(config.ModelId))
                TrackFinishReasonStreak(value, config);
        }

        private static readonly ConcurrentDictionary<string, int> _lengthFinishStreak =
            new(StringComparer.OrdinalIgnoreCase);

        private const int LengthFinishStreakThreshold = 3;

        private static void TrackFinishReasonStreak(string? finishReason, UserConfiguration config)
        {
            var modelId = config.ModelId!;
            var key = BuildCacheKey(config.ProviderId, config.CustomEndpoint, modelId);

            if (IsFinishReasonTruncated(finishReason))
            {
                var streak = _lengthFinishStreak.AddOrUpdate(key, 1, (_, v) => v + 1);
                if (streak >= LengthFinishStreakThreshold)
                {
                    _maxOutputCache.TryRemove(key);
                    _lengthFinishStreak[key] = 0;
                    Interlocked.Increment(ref _cacheMutationVersion);
                    TM.App.Log($"[ChatModeSettings] finish_reason=length 连续 {streak} 次，清除 MaxOutput 缓存触发重探: {MaskKeyEndpointForLog(key)}");
                    SaveDiscoveryCacheAsync();
                }
            }
            else if (!string.IsNullOrEmpty(finishReason))
            {
                _lengthFinishStreak.TryRemove(key, out _);
            }
        }

    }
}

