using System;
using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public static class ProxyProviderResolver
    {

        private static readonly (string DomainKeyword, string ProxyName)[] KnownProxyDomains =
        {
            ("openrouter.ai",     "OpenRouter"),
            ("aihubmix.com",      "AIHubMix"),
            ("aihubmix",          "AIHubMix"),
            ("cherryin",          "CherryIn"),
            ("new-api",           "NewAPI"),
            ("oneapi",            "OneAPI"),
        };

        private static readonly (string Prefix, string ProviderId)[] OpenRouterStylePrefixes =
        {
            ("anthropic/",   "Anthropic"),
            ("openai/",      "OpenAI"),
            ("google/",      "Google"),
            ("deepseek/",    "DeepSeek"),
            ("qwen/",        "Qwen"),
            ("alibaba/",     "Qwen"),
            ("meta-llama/",  "Meta"),
            ("meta/",        "Meta"),
            ("mistralai/",   "Mistral"),
            ("x-ai/",        "xAI"),
            ("xai/",         "xAI"),
            ("moonshotai/",  "Moonshot"),
            ("zai/",         "Zhipu"),
            ("zhipu/",       "Zhipu"),
            ("doubao/",      "Doubao"),
            ("bytedance/",   "Doubao"),
            ("01-ai/",       "Yi"),
            ("01.ai/",       "Yi"),
        };

        private static readonly (Regex Pattern, string ProviderId)[] NameFeaturePatterns =
        {
            (new Regex(@"\bclaude\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Anthropic"),
            (new Regex(@"\bgemini\b|\bgemma\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Google"),
            (new Regex(@"\bdeepseek\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "DeepSeek"),
            (new Regex(@"\bgpt-|\bo1\b|\bo3\b|\bo4\b|\bo5\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "OpenAI"),
            (new Regex(@"\bqwen|qwq|qvq", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Qwen"),
            (new Regex(@"\bllama\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Meta"),
            (new Regex(@"\bgrok\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "xAI"),
            (new Regex(@"\bglm\b|\bchatglm\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Zhipu"),
            (new Regex(@"\bkimi\b|\bmoonshot\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Moonshot"),
            (new Regex(@"\bdoubao\b|\bseed\b|\bark\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Doubao"),
            (new Regex(@"\bhunyuan\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Tencent"),
            (new Regex(@"\bmistral\b|\bmixtral\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Mistral"),
            (new Regex(@"\byi-\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Yi"),
        };

        public static ProxyProviderHint? Resolve(string? endpointUrl, string? modelId)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
                return null;

            var lowerUrl = endpointUrl.ToLowerInvariant();
            string? matchedProxyName = null;
            foreach (var (domain, name) in KnownProxyDomains)
            {
                if (lowerUrl.Contains(domain, StringComparison.OrdinalIgnoreCase))
                {
                    matchedProxyName = name;
                    break;
                }
            }

            if (matchedProxyName == null)
                return null;

            var underlying = InferUnderlyingProvider(modelId);
            if (underlying == null)
            {
                return new ProxyProviderHint
                {
                    ProxyDomain = matchedProxyName,
                    UnderlyingProviderId = "OpenAI",
                    UnderlyingKind = ProviderEndpointKind.OpenAICompat,
                    Reason = $"已知代理 {matchedProxyName}，modelId 无明显特征，默认 OpenAI-compat",
                };
            }

            return new ProxyProviderHint
            {
                ProxyDomain = matchedProxyName,
                UnderlyingProviderId = underlying,
                UnderlyingKind = MapUnderlyingKind(underlying),
                Reason = $"已知代理 {matchedProxyName}，按 modelId 推断底层 = {underlying}",
            };
        }

        public static bool IsKnownProxyDomain(string? endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
                return false;

            var lowerUrl = endpointUrl.ToLowerInvariant();
            foreach (var (domain, _) in KnownProxyDomains)
            {
                if (lowerUrl.Contains(domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string? InferUnderlyingProvider(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return null;

            var lower = modelId.ToLowerInvariant();

            foreach (var (prefix, providerId) in OpenRouterStylePrefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                    return providerId;
            }

            foreach (var (pattern, providerId) in NameFeaturePatterns)
            {
                if (pattern.IsMatch(lower))
                    return providerId;
            }

            return null;
        }

        private static ProviderEndpointKind MapUnderlyingKind(string underlyingProviderId)
        {
            return ProviderEndpointKind.OpenAICompat;
        }
    }

    public sealed record ProxyProviderHint
    {
        public required string ProxyDomain { get; init; }

        public required string UnderlyingProviderId { get; init; }

        public ProviderEndpointKind UnderlyingKind { get; init; } = ProviderEndpointKind.OpenAICompat;

        public string? Reason { get; init; }
    }
}
