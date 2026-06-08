#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        #region 上下文用量估算

        public (int EstimatedTokens, int ContextWindow, double UsagePercent) GetContextUsage(string? additionalText = null, int? overrideContextWindow = null)
        {
            var config = AI.GetActiveConfiguration();
            if (config == null || string.IsNullOrEmpty(config.ModelId))
            {
                return (0, 0, 0);
            }

            if (overrideContextWindow.HasValue && overrideContextWindow.Value > 0)
            {
                return _compression.GetContextUsage(_chatHistory, config.ModelId, additionalText, overrideContextWindow);
            }

            var rawWindow = GetModelContextWindow(config.ModelId);
            if (rawWindow <= 0)
            {
                return (0, 0, 0);
            }

            return _compression.GetContextUsage(_chatHistory, config.ModelId, additionalText, rawWindow);
        }

        public bool IsContextWindowReal()
        {
            var config = AI.GetActiveConfiguration();
            if (config == null || string.IsNullOrEmpty(config.ModelId)) return false;

            var rawWindow = GetModelContextWindow(config.ModelId);
            if (rawWindow <= 0) return false;

            var model = _ai.GetModelById(config.ModelId);
            if (model != null && model.ContextWindow > 0) return true;
            if (ChatModeSettings.GetEffectiveContextWindow(config) > 0) return true;
            return ChatModeSettings.GetDiscoveredContextWindow(config.ModelId, config.CustomEndpoint, config.ProviderId) > 0;
        }

        private async Task EnsureCompressionIfNeededAsync(string? upcomingText, CancellationToken cancellationToken, int? overrideContextWindow = null)
        {
            var config = AI.GetActiveConfiguration();
            if (config == null || string.IsNullOrEmpty(config.ModelId))
            {
                return;
            }

            int? effectiveInputBudget = overrideContextWindow;
            if (!effectiveInputBudget.HasValue || effectiveInputBudget.Value <= 0)
            {
                var rawWindow = GetModelContextWindow(config.ModelId);
                var lastMaxOut = ChatModeSettings.LastUsedMaxTokens;
                effectiveInputBudget = (rawWindow > 0 && lastMaxOut > 0 && lastMaxOut < rawWindow)
                    ? rawWindow - lastMaxOut
                    : (rawWindow > 0 ? rawWindow : (int?)null);
            }
            if (!effectiveInputBudget.HasValue || effectiveInputBudget.Value <= 0)
            {
                return;
            }
            var (_, contextWindow, usagePercent) = _compression.GetContextUsage(_chatHistory, config.ModelId, upcomingText, effectiveInputBudget);
            bool willCompress = usagePercent >= 95;
            if (!willCompress)
            {
                return;
            }

            if (willCompress)
            {
                GlobalToast.Info("上下文压缩", $"正在压缩对话历史（用量{usagePercent:F0}%）...", 2000);
            }

            var (compressedHistory, compressed) = await _compression.EnsureCompressionIfNeededAsync(
                _chatHistory,
                config.ModelId,
                upcomingText,
                cancellationToken,
                effectiveInputBudget).ConfigureAwait(false);

            if (!compressed)
            {
                return;
            }

            _chatHistory = compressedHistory;
            _isSessionCompressed = true;
            TM.App.Log($"[SKChatService] 会话已压缩，剩余消息数: {_chatHistory.Count}");
            GlobalToast.Success("压缩完成", $"保留{_chatHistory.Count}条消息", 2000);
        }

        private int GetModelContextWindow(string modelId)
        {
            var config = _ai.GetActiveConfiguration();
            var effectiveCw = ChatModeSettings.GetEffectiveContextWindow(config);
            if (effectiveCw > 0)
                return effectiveCw;

            var model = _ai.GetModelById(modelId);
            if (model != null && model.ContextWindow > 0)
                return model.ContextWindow;

            var discovered = ChatModeSettings.GetDiscoveredContextWindow(modelId, config?.CustomEndpoint, config?.ProviderId);
            if (discovered > 0)
                return discovered;

            var familyCw = GetFamilyContextWindow(modelId);
            if (familyCw > 0)
                return familyCw;

            return 0;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _familyCwCache = new();

        internal static int GetFamilyContextWindow(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return 0;

            if (_familyCwCache.TryGetValue(modelId, out var cached))
                return cached;

            var id = NormalizeModelId(modelId);
            var seg = SplitModelSegments(id);
            int cw = GetFamilyContextWindowCore(id, seg);

            _familyCwCache[modelId] = cw;
            if (cw > 0)
                TM.App.Log($"[SKChatService] FamilyContextWindow fallback: cw={cw}");
            return cw;
        }

        private static int GetFamilyContextWindowCore(string id, HashSet<string> seg)
        {
            if (StartsWithAnySegment(id, seg, "o1", "o3", "o4"))
                return 200000;

            if (HasToken(seg, "gpt") && Regex.IsMatch(id, @"gpt-5\.[4-9]"))
                return 1048576;
            if (HasToken(seg, "gpt") && id.Contains("gpt-5"))
                return 400000;
            if (HasToken(seg, "gpt") && id.Contains("gpt-4.1"))
                return 1000000;
            if (HasToken(seg, "chatgpt") || HasToken(seg, "gpt"))
                return 128000;

            if (HasToken(seg, "claude")
                && (Regex.IsMatch(id, @"(?:sonnet|opus)-(?:4-[5-9]|[5-9])")
                    || id.Contains("mythos")))
                return 1000000;
            if (HasToken(seg, "claude"))
                return 200000;

            if (HasToken(seg, "gemini") && HasToken(seg, "pro")
                && (id.Contains("gemini-1.5") || id.Contains("gemini-1-5")
                    || id.Contains("gemini-2.0") || id.Contains("gemini-2-0")))
                return 2000000;
            if (HasToken(seg, "gemini") && HasToken(seg, "pro"))
                return 1000000;
            if (HasToken(seg, "gemini"))
                return 1000000;
            if (HasToken(seg, "gemma") && Regex.IsMatch(id, @"gemma-?[4-9]"))
                return 262144;
            if (HasToken(seg, "gemma"))
                return 128000;

            if (HasToken(seg, "llama") && HasToken(seg, "4") && HasToken(seg, "scout"))
                return 10000000;
            if (HasToken(seg, "llama") && HasToken(seg, "4"))
                return 1000000;
            if (HasToken(seg, "llama"))
                return 128000;
            if (HasToken(seg, "nemotron") && (HasToken(seg, "nano") || Regex.IsMatch(id, @"nemotron-?[3-9]")))
                return 256000;
            if (HasToken(seg, "nemotron"))
                return 128000;

            if (HasToken(seg, "codestral")) return 256000;
            if (HasToken(seg, "devstral")) return 256000;
            if (HasToken(seg, "pixtral")) return 128000;
            if (HasToken(seg, "ministral")) return 128000;
            if (HasToken(seg, "magistral")) return 128000;
            if (HasToken(seg, "mixtral")) return 64000;
            if (HasToken(seg, "mistral") || HasToken(seg, "mistralai"))
                return 128000;

            if (HasToken(seg, "deepseek") && Regex.IsMatch(id, @"v[4-9]"))
                return 1048576;
            if (HasToken(seg, "deepseek"))
                return 128000;

            bool isQwen = StartsWithAnySegment(id, seg, "qwen");
            if (isQwen && HasToken(seg, "long"))
                return 10000000;
            if (isQwen
                && (HasToken(seg, "turbo") || HasToken(seg, "plus") || HasToken(seg, "flash")))
                return 1000000;
            if (isQwen && HasToken(seg, "max"))
                return 262144;
            if (isQwen || HasToken(seg, "qwq") || HasToken(seg, "qvq"))
                return 128000;

            if (HasToken(seg, "mimo") && HasToken(seg, "tts"))
                return 8192;
            if (HasToken(seg, "mimo") && (HasToken(seg, "flash") || HasToken(seg, "omni")))
                return 262144;
            if (HasToken(seg, "mimo"))
                return 1000000;

            if (HasToken(seg, "doubao"))
                return 262144;

            if (HasToken(seg, "kimi") && HasToken(seg, "long"))
                return 1000000;
            if (HasToken(seg, "kimi") && Regex.IsMatch(id, @"k-?2"))
                return 262144;
            if (HasToken(seg, "moonshot") || HasToken(seg, "kimi"))
                return 200000;

            if (HasToken(seg, "glm") && Regex.IsMatch(id, @"glm-?5"))
                return 202752;
            if (HasToken(seg, "glm") && (id.Contains("4.6") || id.Contains("4-6") || id.Contains("4.7") || id.Contains("4-7")))
                return 204800;
            if (HasToken(seg, "glm") || HasToken(seg, "zhipu"))
                return 128000;

            if (StartsWithAnySegment(id, seg, "internlm") || HasToken(seg, "intern"))
                return 1000000;

            if (HasToken(seg, "longcat"))
                return 128000;

            if (HasToken(seg, "kwaiyii") || HasToken(seg, "keye") || HasToken(seg, "kwai"))
                return 32000;

            if (HasToken(seg, "chatjd") || HasToken(seg, "yanxi") || HasToken(seg, "jdgpt"))
                return 32000;

            if (HasToken(seg, "ziyan") || HasToken(seg, "yodao") || HasToken(seg, "ziyue"))
                return 32000;

            if (HasToken(seg, "bailing") || HasToken(seg, "antgroup") || HasToken(seg, "antbailing"))
                return 32000;

            if (HasToken(seg, "ernie"))
                return 128000;

            if (HasToken(seg, "pangu"))
                return 64000;

            if (HasToken(seg, "spark"))
                return 128000;

            if (HasToken(seg, "telechat"))
                return 262144;

            if (HasToken(seg, "hunyuan"))
                return 262144;

            if (HasToken(seg, "sensenova") || (HasToken(seg, "sense") && HasToken(seg, "nova")))
                return 200000;

            if (HasToken(seg, "minimax") && Regex.IsMatch(id, @"m2(?:[\.\-]\d+)?"))
                return 196608;
            if (HasToken(seg, "minimax") && (Regex.IsMatch(id, @"m1(?:[\.\-]\d+)?") || id.Contains("text-01") || id.Contains("minimax-01")))
                return 1000192;
            if (HasToken(seg, "abab") && (HasToken(seg, "7") || id.Contains("text-01")))
                return 1000000;
            if (HasToken(seg, "minimax") || HasToken(seg, "abab"))
                return 245000;

            if (HasToken(seg, "yi"))
                return 200000;

            if (HasToken(seg, "aquila"))
                return 32000;

            if (HasToken(seg, "taichu"))
                return 32000;

            if (HasToken(seg, "stepfun") || HasToken(seg, "step"))
                return 262144;

            if (HasToken(seg, "command") || HasToken(seg, "cohere"))
                return 128000;

            if (HasToken(seg, "jamba"))
                return 256000;

            if (HasToken(seg, "grok")
                && Regex.IsMatch(id, @"grok-4(?:[\.\-]\d+)?-fast"))
                return 2000000;
            if (HasToken(seg, "grok") && (Regex.IsMatch(id, @"grok-?4")))
                return 262144;
            if (HasToken(seg, "grok") && (Regex.IsMatch(id, @"grok-?3")))
                return 131072;
            if (HasToken(seg, "grok"))
                return 128000;

            if ((HasToken(seg, "nova") && HasToken(seg, "amazon")) || StartsWithAnySegment(id, seg, "nova"))
                return 300000;
            if (HasToken(seg, "titan"))
                return 32000;

            if (HasToken(seg, "phi"))
                return 128000;

            if (HasToken(seg, "granite"))
                return 128000;

            if (HasToken(seg, "sonar"))
                return 127072;

            if (HasToken(seg, "palmyra"))
                return 32000;

            if (HasToken(seg, "inflection"))
                return 32000;

            if (HasToken(seg, "falcon") && HasToken(seg, "3"))
                return 32000;
            if (HasToken(seg, "falcon"))
                return 8192;

            if (HasToken(seg, "dbrx"))
                return 32768;

            if (HasToken(seg, "arctic") || HasToken(seg, "snowflake"))
                return 32000;

            if (HasToken(seg, "stablelm") || HasToken(seg, "stable-code") || HasToken(seg, "stability"))
                return 16000;

            if (HasToken(seg, "reka"))
                return 128000;

            if (HasToken(seg, "aya"))
                return 128000;

            if (HasToken(seg, "baichuan"))
                return 32000;

            if (HasToken(seg, "skywork") || HasToken(seg, "tiangong"))
                return 128000;

            if (HasToken(seg, "minicpm"))
                return 128000;

            if (HasToken(seg, "milm") || HasToken(seg, "xiaomi"))
                return 32000;

            if (HasToken(seg, "bluelm") || HasToken(seg, "vivo"))
                return 32000;

            if (HasToken(seg, "yuan2") || HasToken(seg, "yuan2.0") || HasToken(seg, "ieityuan"))
                return 32000;

            if (HasToken(seg, "360gpt"))
                return 32000;

            if (HasToken(seg, "hailuo"))
                return 1000000;

            if (HasToken(seg, "apriel"))
                return 32000;
            if (HasToken(seg, "aria"))
                return 64000;

            return 128000;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _familyMoCache = new();

        internal static int GetFamilyMaxOutput(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return 0;
            if (_familyMoCache.TryGetValue(modelId, out var cached))
                return cached;

            var id = NormalizeModelId(modelId);
            var seg = SplitModelSegments(id);
            int mo = GetFamilyMaxOutputCore(id, seg);

            _familyMoCache[modelId] = mo;
            if (mo > 0)
                TM.App.Log($"[SKChatService] FamilyMaxOutput fallback: mo={mo}");
            return mo;
        }

        private static int GetFamilyMaxOutputCore(string id, HashSet<string> seg)
        {
            if (StartsWithAnySegment(id, seg, "o1") && HasToken(seg, "mini")) return 65536;
            if (StartsWithAnySegment(id, seg, "o1", "o3", "o4")) return 100000;

            if (HasToken(seg, "gpt") && id.Contains("gpt-5")) return 128000;
            if (HasToken(seg, "gpt") && id.Contains("gpt-4.1")) return 32768;
            if (HasToken(seg, "gpt") && (id.Contains("gpt-4o") || HasToken(seg, "4o"))) return 16384;
            if (HasToken(seg, "gpt") && id.Contains("gpt-4-turbo")) return 4096;
            if (HasToken(seg, "gpt") && id.Contains("gpt-3.5")) return 4096;
            if (HasToken(seg, "gpt-oss") || HasToken(seg, "gpt") || HasToken(seg, "chatgpt")) return 8192;

            if (HasToken(seg, "claude")
                && (System.Text.RegularExpressions.Regex.IsMatch(id, @"(?:sonnet|opus)-(?:4-[5-9]|[5-9])")
                    || id.Contains("mythos")))
                return 128000;
            if (HasToken(seg, "claude")
                && (System.Text.RegularExpressions.Regex.IsMatch(id, @"(?:sonnet|opus)-4(?:[^-\d]|$)")
                    || System.Text.RegularExpressions.Regex.IsMatch(id, @"haiku-(?:4-[5-9]|[5-9])")
                    || id.Contains("haiku-latest")
                    || id.Contains("3-7-sonnet") || id.Contains("3.7-sonnet")))
                return 64000;
            if (HasToken(seg, "claude") && (id.Contains("3-5") || id.Contains("3.5"))) return 8192;
            if (HasToken(seg, "claude") && (id.Contains("haiku") || id.Contains("opus") || id.Contains("sonnet"))) return 4096;
            if (HasToken(seg, "claude")) return 8192;

            if (HasToken(seg, "gemini")
                && (id.Contains("2.5") || id.Contains("2-5")
                    || Regex.IsMatch(id, @"gemini-(?:3(?:[\.\-]\d+)?|[4-9])")))
                return 65536;
            if (HasToken(seg, "gemma") && Regex.IsMatch(id, @"gemma-?[4-9]")) return 16384;
            if (HasToken(seg, "gemini") || HasToken(seg, "gemma")) return 8192;

            if (HasToken(seg, "deepseek") && System.Text.RegularExpressions.Regex.IsMatch(id, @"v[4-9]")) return 384000;
            if (HasToken(seg, "deepseek")) return 8192;

            bool isQwenMo = StartsWithAnySegment(id, seg, "qwen");
            if (isQwenMo && HasToken(seg, "long")) return 32768;
            if (isQwenMo && HasToken(seg, "max")
                && (id.Contains("qwen3") || id.Contains("qwen-3"))) return 65536;
            if (isQwenMo
                && (id.Contains("qwen3.5-plus") || id.Contains("qwen3.6-plus")
                    || id.Contains("qwen3.5-flash") || id.Contains("qwen3.6-flash")))
                return 65536;
            if (isQwenMo && HasToken(seg, "flash")) return 32768;
            if (isQwenMo && HasToken(seg, "turbo")) return 16384;
            if (isQwenMo && HasToken(seg, "plus")) return 32768;
            if (isQwenMo && HasToken(seg, "max")) return 8192;
            if (isQwenMo && (id.Contains("qwen3") || id.Contains("qwen-3"))) return 65536;
            if (isQwenMo && id.Contains("qwen2.5")) return 32768;
            if (isQwenMo) return 16384;
            if (HasToken(seg, "qwq") || HasToken(seg, "qvq")) return 8192;

            if (HasToken(seg, "mimo") && HasToken(seg, "tts")) return 4096;
            if (HasToken(seg, "mimo") && (HasToken(seg, "flash") || HasToken(seg, "omni"))) return 16384;
            if (HasToken(seg, "mimo")) return 131072;

            if (HasToken(seg, "doubao")) return 16384;

            if (HasToken(seg, "kimi") && System.Text.RegularExpressions.Regex.IsMatch(id, @"k-?2")) return 262142;
            if (HasToken(seg, "kimi") && HasToken(seg, "long")) return 64000;
            if (HasToken(seg, "kimi") || HasToken(seg, "moonshot")) return 16384;

            if (HasToken(seg, "glm") && (id.Contains("4.6") || id.Contains("4-6") || id.Contains("4.7") || id.Contains("4-7"))) return 128000;
            if (HasToken(seg, "glm") && (id.Contains("4.5") || id.Contains("4-5"))) return 96000;
            if (HasToken(seg, "glm") && System.Text.RegularExpressions.Regex.IsMatch(id, @"glm-?5")) return 65535;
            if (HasToken(seg, "glm") && (id.Contains("flash") || id.Contains("air"))) return 4096;
            if (HasToken(seg, "glm") || HasToken(seg, "zhipu") || HasToken(seg, "chatglm")) return 8192;

            if (HasToken(seg, "grok") && id.Contains("4-fast")) return 32768;
            if (HasToken(seg, "grok") && System.Text.RegularExpressions.Regex.IsMatch(id, @"grok-?4")) return 32768;
            if (HasToken(seg, "grok") && System.Text.RegularExpressions.Regex.IsMatch(id, @"grok-?3")) return 8192;
            if (HasToken(seg, "grok")) return 4096;

            if (HasToken(seg, "hunyuan") && (id.Contains("t1") || id.Contains("-t-1"))) return 64000;
            if (HasToken(seg, "hunyuan") && (id.Contains("lite") || id.Contains("standard"))) return 4096;
            if (HasToken(seg, "hunyuan")) return 16384;

            if (HasToken(seg, "llama") && HasToken(seg, "4")) return 65536;
            if (HasToken(seg, "nemotron") && System.Text.RegularExpressions.Regex.IsMatch(id, @"nemotron-?[3-9]|nano")) return 65536;
            if (HasToken(seg, "llama") || HasToken(seg, "nemotron")) return 16384;

            if (HasToken(seg, "codestral") || HasToken(seg, "devstral")) return 8192;
            if (HasToken(seg, "mistral") || HasToken(seg, "mixtral") || HasToken(seg, "pixtral")
                || HasToken(seg, "ministral") || HasToken(seg, "magistral")) return 8192;

            if (HasToken(seg, "ernie") && id.Contains("4.5")) return 8192;
            if (HasToken(seg, "ernie")) return 4096;

            if (HasToken(seg, "sensenova") || (HasToken(seg, "sense") && HasToken(seg, "nova"))) return 8192;

            if (HasToken(seg, "minimax") && Regex.IsMatch(id, @"m2(?:[\.\-]\d+)?")) return 131072;
            if (HasToken(seg, "minimax") && (Regex.IsMatch(id, @"m1(?:[\.\-]\d+)?") || id.Contains("text-01") || id.Contains("minimax-01"))) return 80000;
            if (HasToken(seg, "minimax") || HasToken(seg, "abab") || HasToken(seg, "hailuo")) return 8192;

            if (HasToken(seg, "yi")) return 4096;

            if (HasToken(seg, "stepfun") || HasToken(seg, "step")) return 8192;

            if (HasToken(seg, "internlm") || HasToken(seg, "intern")) return 8192;

            if (HasToken(seg, "longcat")) return 8192;

            if (HasToken(seg, "spark")) return 8192;

            if (HasToken(seg, "telechat")) return 8192;

            if (HasToken(seg, "baichuan")) return 4096;

            if (HasToken(seg, "skywork") || HasToken(seg, "tiangong")) return 8192;

            if (HasToken(seg, "minicpm")) return 8192;

            if (HasToken(seg, "milm") || HasToken(seg, "xiaomi")
                || HasToken(seg, "bluelm") || HasToken(seg, "vivo")
                || HasToken(seg, "yuan2") || HasToken(seg, "ieityuan")
                || HasToken(seg, "360gpt") || HasToken(seg, "pangu")
                || HasToken(seg, "chatjd") || HasToken(seg, "yanxi") || HasToken(seg, "jdgpt")
                || HasToken(seg, "ziyan") || HasToken(seg, "yodao") || HasToken(seg, "ziyue")
                || HasToken(seg, "bailing") || HasToken(seg, "antgroup")
                || HasToken(seg, "kwaiyii") || HasToken(seg, "keye") || HasToken(seg, "kwai")
                || HasToken(seg, "taichu") || HasToken(seg, "aquila")
                || HasToken(seg, "palmyra") || HasToken(seg, "inflection"))
                return 4096;

            if (HasToken(seg, "command") || HasToken(seg, "cohere") || HasToken(seg, "aya")) return 4096;

            if (HasToken(seg, "nova") && HasToken(seg, "amazon")) return 5120;

            if (HasToken(seg, "granite") || HasToken(seg, "phi") || HasToken(seg, "jamba")
                || HasToken(seg, "sonar") || HasToken(seg, "falcon")
                || HasToken(seg, "titan") || HasToken(seg, "apriel") || HasToken(seg, "aria")) return 8192;

            if (HasToken(seg, "dbrx") || HasToken(seg, "arctic") || HasToken(seg, "snowflake")
                || HasToken(seg, "stablelm") || HasToken(seg, "stable-code") || HasToken(seg, "reka")) return 4096;

            return 8192;
        }

        private static string NormalizeModelId(string modelId)
        {
            var id = modelId.Trim().ToLowerInvariant();
            id = id.Replace('\\', '/').Replace(':', '/').Replace('|', '/');
            id = id.Replace("__", "_");
            while (id.Contains("//")) id = id.Replace("//", "/");
            return id;
        }

        private static HashSet<string> SplitModelSegments(string modelId)
        {
            var parts = modelId.Split(
                new[] { '/', '-', '_', '.', ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasToken(HashSet<string> segments, string token)
        {
            return !string.IsNullOrWhiteSpace(token) && segments.Contains(token);
        }

        private static bool StartsWithAnySegment(string modelId, HashSet<string> segments, params string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix)) continue;
                if (modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var seg in segments)
                    if (seg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion
    }
}
