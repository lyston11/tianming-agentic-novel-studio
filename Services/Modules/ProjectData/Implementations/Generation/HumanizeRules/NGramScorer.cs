using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static class NGramScorer
    {
        private static readonly FrozenDictionary<string, int> Unigrams = LoadEmbedded("unigrams");
        private static readonly FrozenDictionary<string, int> Bigrams = LoadEmbedded("bigrams");
        private static readonly FrozenDictionary<string, int> Trigrams = LoadEmbedded("trigrams");

        private static readonly int VocabSize = Math.Max(Unigrams.Count, 1000);

        private const double SmoothingK = 0.01;
        private const double TrigramLambda = 0.6;
        private const double UnseenFloor = -20.0;

        private const char ChineseCharLow = '\u4e00';
        private const char ChineseCharHigh = '\u9fff';

        public static double ComputePerplexity(ReadOnlySpan<char> text)
        {
            var buffer = ArrayPool<char>.Shared.Rent(text.Length > 0 ? text.Length : 1);
            try
            {
                int chineseCount = 0;
                foreach (var c in text)
                {
                    if (c >= ChineseCharLow && c <= ChineseCharHigh)
                        buffer[chineseCount++] = c;
                }

                if (chineseCount < 5) return 0.0;

                double sumLogProb = 0.0;
                int count = 0;
                for (int i = 2; i < chineseCount; i++)
                {
                    sumLogProb += TrigramLogProb(buffer[i - 2], buffer[i - 1], buffer[i]);
                    count++;
                }

                if (count == 0) return 0.0;
                double avgLp = sumLogProb / count;
                return Math.Pow(2, -avgLp);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private static double TrigramLogProb(char c1, char c2, char c3)
        {
            Span<char> triBuf = stackalloc char[3] { c1, c2, c3 };
            string triKey = new string(triBuf);

            Span<char> biBuf = stackalloc char[2] { c1, c2 };
            string biContextKey = new string(biBuf);

            int triCount = Trigrams.GetValueOrDefault(triKey, 0);
            int biContextCount = Bigrams.GetValueOrDefault(biContextKey, 0);
            double pTri = (triCount + SmoothingK) / (biContextCount + SmoothingK * VocabSize);

            double pBiLog = BigramLogProb(c2, c3);
            double pBi = Math.Pow(2, pBiLog);

            double pInterp = TrigramLambda * pTri + (1 - TrigramLambda) * pBi;
            return pInterp > 0 ? Math.Log2(pInterp) : UnseenFloor;
        }

        private static double BigramLogProb(char c1, char c2)
        {
            Span<char> biBuf = stackalloc char[2] { c1, c2 };
            string biKey = new string(biBuf);

            Span<char> uniBuf = stackalloc char[1] { c1 };
            string uniKey = new string(uniBuf);

            int biCount = Bigrams.GetValueOrDefault(biKey, 0);
            int uniCount = Unigrams.GetValueOrDefault(uniKey, 0);
            double prob = (biCount + SmoothingK) / (uniCount + SmoothingK * VocabSize);
            return prob > 0 ? Math.Log2(prob) : UnseenFloor;
        }

        private static FrozenDictionary<string, int> LoadEmbedded(string sectionKey)
        {
            var asm = typeof(NGramScorer).Assembly;

            string? resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("NGramFreqCn.json", StringComparison.Ordinal));

            if (resourceName == null)
                throw new InvalidOperationException(
                    "未找到嵌入资源 NGramFreqCn.json。请确认 Services.props 中 EmbeddedResource 通配符规则已注册。");

            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"无法打开嵌入资源流: {resourceName}");

            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty(sectionKey, out var section))
                throw new InvalidOperationException(
                    $"NGramFreqCn.json 缺少必需段: {sectionKey}（期望 unigrams/bigrams/trigrams）。");

            var dict = new Dictionary<string, int>(section.EnumerateObject().Count());
            foreach (var prop in section.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetInt32();
            }
            return dict.ToFrozenDictionary();
        }
    }
}
