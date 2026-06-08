using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Mlm;
using TM.Services.Framework.AI.SemanticGuard;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal sealed class PickerScorers
    {
        public IMicroMlmService? Mlm { get; init; }
        public ISemanticGuard? Guard { get; init; }
        public PickerOptions Options { get; init; } = new PickerOptions();
    }

    internal sealed class PickerOptions
    {
        public int GuardWindowChars { get; init; } = 50;

        public float GuardCosineThreshold { get; init; } = 0.85f;
    }

    internal static class CandidatePicker
    {
        public static async Task<string> PickBestAsync(
            string text,
            int anchorIdx,
            string key,
            IReadOnlyList<string> avail,
            PickerScorers? scorers,
            CancellationToken ct = default)
        {
            if (avail.Count == 0) return key;
            if (avail.Count == 1) return avail[0];

            var scored = ComputeNGramScored(text, anchorIdx, key, avail);

            Array.Sort(scored, static (a, b) => a.ppl.CompareTo(b.ppl));

            string ngramBest = scored.Length <= 2 ? scored[^1].cand : scored[^2].cand;

            if (!HighRiskWords.IsHighRisk(key))
            {
                return ngramBest;
            }

            string[] top2;
            if (scored.Length <= 2)
            {
                top2 = new[] { scored[0].cand, scored[1].cand };
            }
            else
            {
                top2 = new[] { scored[^2].cand, scored[^1].cand };
            }

            string chosen = await PickWithMlmAsync(text, anchorIdx, key, top2, ngramBest, scorers, ct).ConfigureAwait(false);

            if (scorers?.Guard?.IsReady == true)
            {
                try
                {
                    float cos = await scorers.Guard.ComputeLocalCosineAsync(
                        text, anchorIdx, key, chosen,
                        scorers.Options.GuardWindowChars, ct).ConfigureAwait(false);

                    if (cos < scorers.Options.GuardCosineThreshold)
                    {
                        return key;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    TM.App.Log($"[Picker] BGE 守门异常（接受 chosen 不回退）: {ex.Message}");
                }
            }

            return chosen;
        }

        private static async Task<string> PickWithMlmAsync(
            string text,
            int anchorIdx,
            string key,
            string[] top2,
            string ngramBest,
            PickerScorers? scorers,
            CancellationToken ct)
        {
            if (scorers?.Mlm == null || !scorers.Mlm.IsModelReady())
            {
                return ngramBest;
            }

            try
            {
                var mlmScores = await scorers.Mlm.ScoreCandidatesAsync(
                    text, anchorIdx, key, top2, ct).ConfigureAwait(false);

                if (mlmScores == null || mlmScores.Length != top2.Length)
                {
                    return ngramBest;
                }

                int bestIdx = 0;
                float bestScore = mlmScores[0];
                for (int i = 1; i < mlmScores.Length; i++)
                {
                    if (mlmScores[i] > bestScore)
                    {
                        bestScore = mlmScores[i];
                        bestIdx = i;
                    }
                }
                return top2[bestIdx];
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TM.App.Log($"[Picker] MLM 评分异常（回退 N-gram 次高位）: {ex.Message}");
                return ngramBest;
            }
        }

        private static (string cand, double ppl)[] ComputeNGramScored(
            string text, int anchorIdx, string key, IReadOnlyList<string> avail)
        {
            var prefix = text.AsSpan(0, anchorIdx);
            var suffix = text.AsSpan(anchorIdx + key.Length);

            var scored = new (string cand, double ppl)[avail.Count];
            for (int i = 0; i < avail.Count; i++)
            {
                string c = avail[i];
                string newText = string.Concat(prefix, c.AsSpan(), suffix);
                scored[i] = (c, NGramScorer.ComputePerplexity(newText));
            }
            return scored;
        }
    }
}
