using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TM.Services.Framework.AI.Core.Capabilities;

namespace TM.Services.Framework.AI.Core;

public static class ModelFamilyClassifier
{
    private static readonly Regex OSeriesRegex =
        new(@"(?:^|[/\-])o\d+(?:$|[/\-])", RegexOptions.Compiled);

    private static readonly Regex PhiSeriesRegex =
        new(@"^phi[\d\-]", RegexOptions.Compiled);

    private static readonly Regex DeepSeekV31OrLaterRegex =
        new(@"v(?:3\.(?:[1-9]\d*)|(?:[4-9]|[1-9]\d+)(?:\.\d+)?)(?:\D|$)", RegexOptions.Compiled);

    private static readonly Regex LongContextNameRegex = new(
        @"claude-(?:sonnet-4|opus-4|mythos)"
        + @"|deepseek-v[4-9]"
        + @"|gpt-(?:4\.1|5\.[4-9]|[6-9])"
        + @"|gemini-(?:1[\.\-]5|2(?:[\.\-]\d+)?|3(?:[\.\-]\d+)?|[4-9])"
        + @"|qwen-(?:turbo|long|plus|flash)"
        + @"|qwen3?\.[5-9]-(?:plus|flash)"
        + @"|llama-4-scout"
        + @"|grok-(?:4(?:[\.-]\d+)?-fast|[5-9])"
        + @"|minimax-(?:m1|01|text-01)"
        + @"|mimo-v[2-9](?:[\.\-]\d+)?(?:-pro)?(?![\.\-]?(?:flash|omni|tts))"
        + @"|hailuo",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsGeminiThinkingFamily(string modelId)
    {
        if (!modelId.Contains("gemini")) return false;
        if (modelId.Contains("think")) return true;
        return Regex.IsMatch(modelId, @"gemini-(?:2[\.\-]5|3(?:[\.\-]\d+)?|[4-9])");
    }

    public static bool IsReasoningEffortModel(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        var p = (providerId ?? string.Empty).ToLowerInvariant();
        if (OSeriesRegex.IsMatch(m)) return true;
        if (m.Contains("grok") && m.Contains("mini")) return true;
        if (m.Contains("gpt-") && !m.Contains("gpt-4") && !m.Contains("gpt-3") && !m.Contains("gpt-oss")) return true;
        if (!p.Contains("google") && !p.Contains("gemini") && IsGeminiThinkingFamily(m)) return true;
        return false;
    }

    public static bool IsOpenRouterReasoningModel(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        var p = (providerId ?? string.Empty).ToLowerInvariant();

        if (IsReasoningEffortModel(modelId, providerId)) return true;

        if (m.Contains("claude") || p.Contains("anthropic")) return true;
        if (IsGeminiThinkingFamily(m)) return true;
        if (m.Contains("qwen3") || m.Contains("qwq") || m.Contains("qvq")) return true;
        if (m.Contains("deepseek-r") || m.Contains("deepseek-reasoner")) return true;
        if (m.Contains("deepseek") && DeepSeekV31OrLaterRegex.IsMatch(m)) return true;
        if (m.Contains("grok")
            && (Regex.IsMatch(m, @"grok-?[3-9]")
                || m.Contains("mini") || m.Contains("reason") || m.Contains("think"))) return true;
        if ((m.Contains("doubao") || m.Contains("ark") || m.Contains("seed"))
            && (m.Contains("think") || Regex.IsMatch(m, @"1\.[6-9]|-?[2-9]"))) return true;
        if (m.Contains("hunyuan")
            && (Regex.IsMatch(m, @"t\d") || m.Contains("think") || m.Contains("turbo"))) return true;
        if (m.Contains("kimi") || (m.Contains("moonshot") && m.Contains("think"))) return true;
        if (m.Contains("glm")
            && (Regex.IsMatch(m, @"glm-?5|glm-4\.[5-9]") || m.Contains("think"))) return true;
        if (m.Contains("step") && (m.Contains("think") || m.Contains("-r") || Regex.IsMatch(m, @"step-[2-9]"))) return true;
        if (m.Contains("mimo") && !m.Contains("tts")) return true;
        if (m.Contains("minimax") && m.Contains("-m")) return true;
        if (Regex.IsMatch(m, @"[-_](thinking|reasoner|reason|think)([-_]|$)")) return true;

        return false;
    }

    public static bool IsThinkingModel(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        var p = (providerId ?? string.Empty).ToLowerInvariant();

        if (m.Contains("claude") || p.Contains("anthropic")) return true;
        if (IsGeminiThinkingFamily(m)) return true;
        if (m.Contains("qwen3") || m.Contains("qwq") || m.Contains("qvq")) return true;
        if (m.Contains("deepseek-r") || m.Contains("deepseek-reasoner")) return true;
        if (m.Contains("deepseek") && DeepSeekV31OrLaterRegex.IsMatch(m)) return true;
        if (m.Contains("grok")
            && (Regex.IsMatch(m, @"grok-?[3-9]")
                || m.Contains("mini") || m.Contains("reason") || m.Contains("think"))) return true;
        if ((m.Contains("doubao") || m.Contains("ark"))
            && (m.Contains("think") || Regex.IsMatch(m, @"1\.[6-9]|-?[2-9]"))) return true;
        if (m.Contains("hunyuan")
            && (Regex.IsMatch(m, @"t\d") || m.Contains("think") || m.Contains("turbo"))) return true;
        if (m.Contains("kimi") || (m.Contains("moonshot") && m.Contains("think"))) return true;
        if (m.Contains("glm")
            && (Regex.IsMatch(m, @"glm-?5|glm-4\.[5-9]") || m.Contains("think"))) return true;
        if (m.Contains("step") && (m.Contains("think") || m.Contains("-r") || Regex.IsMatch(m, @"step-[2-9]"))) return true;
        if (m.Contains("seed")
            && (m.Contains("think") || Regex.IsMatch(m, @"1\.[6-9]|-?[2-9]"))) return true;
        if (m.Contains("mimo") && !m.Contains("tts")) return true;
        if (m.Contains("minimax") && m.Contains("-m")) return true;
        if (m.Contains("qwen") && m.Contains("think")) return true;
        if (m.Contains("gpt-") && !m.Contains("gpt-4") && !m.Contains("gpt-3") && !m.Contains("gpt-oss"))
            return true;
        if (Regex.IsMatch(m, @"[-_](thinking|reasoner|reason|think)([-_]|$)")) return true;

        return false;
    }

    public static bool IsNeitherParamModel(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();

        if (m.Contains("gpt-4") || m.Contains("gpt-3") || m.Contains("gpt-oss")) return true;

        if ((m.StartsWith("deepseek", StringComparison.Ordinal) || m.Contains("/deepseek"))
            && !m.Contains("-r1") && !m.Contains("/r1") && !m.Contains("deepseek-r") && !m.Contains("reasoner")
            && !DeepSeekV31OrLaterRegex.IsMatch(m))
            return true;

        if ((m.StartsWith("glm", StringComparison.Ordinal) || m.Contains("glm-") || m.StartsWith("chatglm", StringComparison.Ordinal))
            && !m.Contains("think")
            && !Regex.IsMatch(m, @"glm-?5|glm-4\.[5-9]"))
            return true;

        if (m.StartsWith("qwen", StringComparison.Ordinal) &&
            !m.Contains("qwq") && !m.Contains("qvq") && !m.Contains("qwen3") && !m.Contains("think")) return true;

        if (m.Contains("llama") || m.Contains("mistral") || m.Contains("mixtral")) return true;

        if (PhiSeriesRegex.IsMatch(m) || m.StartsWith("falcon", StringComparison.Ordinal)) return true;

        if (m.StartsWith("moonshot", StringComparison.Ordinal) && !m.Contains("think") && !m.Contains("kimi")) return true;

        if (m.StartsWith("minicpm", StringComparison.Ordinal)) return true;
        if (m.StartsWith("minimax", StringComparison.Ordinal) && !m.Contains("-m")) return true;
        if (m.StartsWith("abab", StringComparison.Ordinal)) return true;

        if (m.StartsWith("baichuan", StringComparison.Ordinal)) return true;
        if (m.StartsWith("yi-", StringComparison.Ordinal)) return true;

        if (m.Contains("gemini") && !IsGeminiThinkingFamily(m)) return true;

        if (m.StartsWith("gemma", StringComparison.Ordinal) || m.StartsWith("command", StringComparison.Ordinal)) return true;

        if (m.StartsWith("internlm", StringComparison.Ordinal) && !m.Contains("think")) return true;

        if (m.StartsWith("spark", StringComparison.Ordinal) || m.StartsWith("xunfei", StringComparison.Ordinal)) return true;

        if (m.StartsWith("ernie", StringComparison.Ordinal) || m.StartsWith("wenxin", StringComparison.Ordinal) || m.Contains("ernie-")) return true;

        return false;
    }

    public static bool IsLongContextModel(string? modelId, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        return LongContextNameRegex.IsMatch(modelId);
    }

    public static bool IsDefaultLongContextModel(string? modelId, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        return DefaultLongContextNameRegex.IsMatch(modelId);
    }

    private static readonly Regex DefaultLongContextNameRegex = new(
        @"deepseek-v[4-9]"
        + @"|gpt-(?:4\.1|5\.[4-9]|[6-9])"
        + @"|gemini-(?:1[\.\-]5|2(?:[\.\-]\d+)?|3(?:[\.\-]\d+)?|[4-9])"
        + @"|grok-(?:4(?:[\.-]\d+)?-fast|[5-9])"
        + @"|llama-4-scout"
        + @"|minimax-(?:m1|01|text-01)"
        + @"|mimo-v[2-9](?:[\.\-]\d+)?(?:-pro)?(?![\.\-]?(?:flash|omni|tts))"
        + @"|hailuo",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> GetSupportedEffortLevels(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();

        if (Regex.IsMatch(m, @"gpt-[5-9]\.[4-9]-pro"))
            return new[] { EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh };

        if (Regex.IsMatch(m, @"gpt-[5-9](?:\.\d+)?-pro"))
            return new[] { EffortConstants.High };

        if (Regex.IsMatch(m, @"gpt-[5-9](?:\.\d+)?-codex-max"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh };

        if (Regex.IsMatch(m, @"gpt-[5-9](?:\.\d+)?-codex(?:-mini)?(?!-max)"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh };

        if (Regex.IsMatch(m, @"gpt-[5-9]\.[1-9]"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh };

        if (Regex.IsMatch(m, @"claude-opus-(?:4-[7-9]|[5-9])"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh, EffortConstants.Max };

        if (Regex.IsMatch(m, @"claude-opus-4-6"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.Max };

        if (Regex.IsMatch(m, @"claude-sonnet-(?:4-[6-9]|[5-9])"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High };

        if (Regex.IsMatch(m, @"claude-opus-4-5"))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High };

        if (Regex.IsMatch(m, @"grok-?[3-9](?:\.\d+)?-mini"))
            return new[] { EffortConstants.Low, EffortConstants.High };

        if (Regex.IsMatch(m, @"deepseek-v[4-9]"))
            return new[] { EffortConstants.High, EffortConstants.Max };

        if (m.Contains("deepseek") && DeepSeekV31OrLaterRegex.IsMatch(m))
            return Array.Empty<string>();

        if (Regex.IsMatch(m, @"^glm-?[4-9]"))
            return Array.Empty<string>();

        if (m.Contains("hunyuan")
            && (Regex.IsMatch(m, @"t\d") || m.Contains("think") || m.Contains("turbo")))
            return Array.Empty<string>();

        if (m.StartsWith("step", StringComparison.Ordinal)
            && (m.Contains("think") || m.Contains("-r") || Regex.IsMatch(m, @"step-[2-9]")))
            return Array.Empty<string>();

        if (m.Contains("minimax") && m.Contains("-m"))
            return Array.Empty<string>();

        if (m.Contains("kimi"))
            return Array.Empty<string>();

        if (m.Contains("doubao") || m.Contains("ark") || m.Contains("seed"))
            return Array.Empty<string>();

        if (m.Contains("grok") && !m.Contains("mini") && !m.Contains("-thinking") && !m.Contains("-think")
            && Regex.IsMatch(m, @"grok-?[4-9]"))
            return Array.Empty<string>();

        if (Regex.IsMatch(m, @"gpt-[5-9](?!\.\d)(?!-codex)"))
            return new[] { EffortConstants.Minimal, EffortConstants.Low, EffortConstants.Medium, EffortConstants.High, EffortConstants.XHigh };

        if (OSeriesRegex.IsMatch(m))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High };

        if (IsGeminiThinkingFamily(m))
            return new[] { EffortConstants.Minimal, EffortConstants.Low, EffortConstants.Medium, EffortConstants.High };

        if (IsReasoningEffortModel(modelId, providerId) || IsThinkingModel(modelId, providerId))
            return new[] { EffortConstants.Low, EffortConstants.Medium, EffortConstants.High };

        return Array.Empty<string>();
    }

    public static string? GetDefaultEffort(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();

        if (Regex.IsMatch(m, @"gpt-[5-9]\.[4-9]-pro")) return EffortConstants.Medium;

        if (Regex.IsMatch(m, @"gpt-[5-9](?:\.\d+)?-pro")) return EffortConstants.High;

        if (Regex.IsMatch(m, @"gpt-[5-9]\.\d+-codex-max")) return EffortConstants.Medium;

        if (Regex.IsMatch(m, @"gpt-[5-9]\.[1-9]")) return EffortConstants.Low;

        if (Regex.IsMatch(m, @"claude-opus-(?:4-[7-9]|[5-9])")) return EffortConstants.XHigh;

        if (Regex.IsMatch(m, @"claude-opus-4-6")) return EffortConstants.High;
        if (Regex.IsMatch(m, @"claude-sonnet-(?:4-[6-9]|[5-9])")) return EffortConstants.Medium;
        if (Regex.IsMatch(m, @"claude-opus-4-5")) return EffortConstants.High;

        if (Regex.IsMatch(m, @"deepseek-v[4-9]")) return EffortConstants.High;

        if (Regex.IsMatch(m, @"grok-?[3-9](?:\.\d+)?-mini")) return EffortConstants.Low;

        if (IsReasoningEffortModel(modelId, providerId) || IsThinkingModel(modelId, providerId))
            return EffortConstants.Medium;

        return null;
    }

    public static string GetThinkingDisplayKind(string? modelId, string? providerId)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        var p = (providerId ?? string.Empty).ToLowerInvariant();

        if (m.Contains("claude") || p.Contains("anthropic")) return "Analysis";
        if (m.Contains("gemini") || p.Contains("google") || p.Contains("gemini")) return "Thought";
        if (m.Contains("deepseek")) return "Reasoner";
        if (m.Contains("qwen") || m.Contains("qwq") || m.Contains("qvq")) return "Reasoning";
        if (m.Contains("llama") || m.Contains("gpt-oss")) return "Reasoning";
        if (m.Contains("grok") && (m.Contains("mini") || m.Contains("reason") || m.Contains("think"))) return "Reasoning";
        if (m.Contains("seed")) return "SeedThink";
        if (m.Contains("glm") || m.Contains("kimi") || m.Contains("hunyuan") || m.Contains("doubao")
            || m.Contains("ark") || m.Contains("step")
            || (m.Contains("mimo") && !m.Contains("tts"))
            || (m.Contains("minimax") && m.Contains("-m"))) return "Reasoning";

        return "Thinking";
    }

    public static RequestParameterMode GetRequestParameterMode(string? modelId, string? providerId, string? endpoint = null)
    {
        var m = (modelId ?? string.Empty).ToLowerInvariant();
        var p = (providerId ?? string.Empty).ToLowerInvariant();
        bool isOpenRouter = !string.IsNullOrEmpty(endpoint)
            && endpoint!.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);

        if (isOpenRouter && IsOpenRouterReasoningModel(modelId, providerId))
            return RequestParameterMode.OpenRouterReasoning;

        if (p.Contains("anthropic")) return RequestParameterMode.AnthropicThinking;

        if ((p.Contains("google") || p.Contains("gemini")) && IsGeminiThinkingFamily(m))
            return RequestParameterMode.GoogleThinkingConfig;

        if (m.Contains("claude")) return RequestParameterMode.AnthropicThinking;

        if (IsGeminiThinkingFamily(m))
            return RequestParameterMode.GoogleThinkingConfig;

        if (m.Contains("qwen3") || m.Contains("qwq") || m.Contains("qvq"))
            return RequestParameterMode.QwenEnableThinking;

        if (m.StartsWith("deepseek-r", StringComparison.Ordinal)
            || m.Contains("deepseek-r1")
            || m.Contains("deepseek-reasoner"))
            return RequestParameterMode.None;

        if ((Regex.IsMatch(m, @"deepseek-v[4-9]"))
            || (m.Contains("deepseek") && DeepSeekV31OrLaterRegex.IsMatch(m)))
            return RequestParameterMode.DeepSeekV4Thinking;

        if (m.Contains("grok"))
        {
            if (m.Contains("-thinking") || m.Contains("-think"))
                return RequestParameterMode.AnthropicThinking;
            if (m.Contains("mini"))
                return RequestParameterMode.OpenAIReasoningEffort;
        }

        if (m.Contains("doubao") || m.Contains("ark")
            || (m.Contains("seed") && (m.Contains("think") || Regex.IsMatch(m, @"1\.[6-9]|-?[2-9]"))))
            return RequestParameterMode.DeepSeekV4Thinking;

        if (m.Contains("glm")
            && (Regex.IsMatch(m, @"^glm-?5|^glm-4\.[5-9]") || m.Contains("think")))
            return RequestParameterMode.DeepSeekV4Thinking;

        if (Regex.IsMatch(m, @"^kimi-?k[2-9]"))
            return RequestParameterMode.DeepSeekV4Thinking;

        if (m.Contains("hunyuan")
            && (Regex.IsMatch(m, @"t\d") || m.Contains("think") || m.Contains("turbo")))
            return RequestParameterMode.None;

        if (m.StartsWith("step", StringComparison.Ordinal)
            && (m.Contains("think") || m.Contains("-r") || Regex.IsMatch(m, @"step-[2-9]")))
            return RequestParameterMode.None;

        if (m.Contains("minimax") && m.Contains("-m"))
            return RequestParameterMode.None;

        if (m.StartsWith("mimo", StringComparison.Ordinal) && !m.Contains("tts"))
            return RequestParameterMode.EnableThinkingFlag;

        if (IsReasoningEffortModel(modelId, providerId))
            return RequestParameterMode.OpenAIReasoningEffort;

        if (!m.Contains("deepseek") && !m.Contains("qwen")
            && (Regex.IsMatch(m, @"[-_](thinking|reasoner|reason|think)([-_]|$)")
                || m.EndsWith("-thinking", StringComparison.Ordinal)
                || m.EndsWith("-reasoner", StringComparison.Ordinal)
                || m.EndsWith("-think", StringComparison.Ordinal)))
            return RequestParameterMode.EnableThinkingFlag;

        return RequestParameterMode.None;
    }
}
