using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.Core
{
    public static class ModelNameSanitizer
    {
        private static readonly Regex ModelNamePattern = new(
            @"(?i)\b(?:" +
                @"Chat\s*GPT(?:\s*[-‐‑‒–—]\s*(?:4o?|3\.5|[0-9]+))?" +
                @"|GPT\s*[-‐‑‒–—]?\s*(?:4o?|3\.5|[0-9]+(?:\.[0-9]+)?)\s*(?:turbo|mini|preview|plus)?" +
                @"|Claude\s*(?:[-‐‑‒–—]?\s*(?:[0-9]+(?:\.[0-9]+)?)\s*(?:opus|sonnet|haiku)?)?" +
                @"|Gemini\s*(?:[-‐‑‒–—]?\s*(?:[0-9]+(?:\.[0-9]+)?)\s*(?:pro|ultra|nano|flash)?)?" +
                @"|DeepSeek\s*(?:[-‐‑‒–—]?\s*(?:V[0-9]+|R[0-9]+|Chat|Coder|Reasoner))?" +
                @"|Llama\s*(?:[-‐‑‒–—]?\s*[0-9]+(?:\.[0-9]+)?)?" +
                @"|Mistral\s*(?:[-‐‑‒–—]?\s*(?:Large|Medium|Small|Nemo|[0-9]+))?" +
                @"|Moonshot\s*(?:[-‐‑‒–—]?\s*v[0-9]+)?" +
            @")\b" +
            @"|通义千问|文心一言|混元大模型|星火大模型|智谱清言|百川大模型",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LeakSentencePattern = new(
            @"(?:我(?:是|的底层|基于|使用(?:了)?|运行(?:在)?|来自)\s*)" +
            @"(?:(?:基于|使用|运行在|来自)\s*)?" +
            @"(?:OpenAI|Anthropic|Google|百度|阿里|腾讯|月之暗面|智谱|百川|Meta|Mistral\s*AI)" +
            @"[^。！？\n]{0,30}(?:[。！？]|\n|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex KimiContextPattern = new(
            @"(?:我是|我叫|名叫|名字是|名为)\s*Kimi\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex QwenContextPattern = new(
            @"(?:我是|我叫|名叫|名字是|基于|使用)\s*Qwen\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var result = text;
            var changed = false;

            if (ModelNamePattern.IsMatch(result))
            {
                result = ModelNamePattern.Replace(result, "天命");
                changed = true;
            }

            if (LeakSentencePattern.IsMatch(result))
            {
                result = LeakSentencePattern.Replace(result, "我是「天命」，具体技术细节不便透露。");
                changed = true;
            }

            if (KimiContextPattern.IsMatch(result))
            {
                result = KimiContextPattern.Replace(result, "我是「天命」");
                changed = true;
            }
            if (QwenContextPattern.IsMatch(result))
            {
                result = QwenContextPattern.Replace(result, "我是「天命」");
                changed = true;
            }

            if (changed)
            {
                TM.App.Log("[ModelNameSanitizer] 检测到并过滤了模型名泄露");
            }

            return result;
        }

        public static string SanitizeChunk(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk) || chunk.Length < 3)
                return chunk ?? string.Empty;

            if (ModelNamePattern.IsMatch(chunk))
            {
                return ModelNamePattern.Replace(chunk, "天命");
            }

            return chunk;
        }
    }
}
