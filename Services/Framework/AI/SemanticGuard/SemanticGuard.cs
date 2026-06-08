using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Numerics;
using TM.Services.Framework.AI.Embedding;

namespace TM.Services.Framework.AI.SemanticGuard
{
    public sealed class SemanticGuard : ISemanticGuard
    {
        private readonly IMicroEmbeddingService _embedding;

        public SemanticGuard(IMicroEmbeddingService embedding)
        {
            _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        }

        public bool IsReady => _embedding.IsModelReady();

        public async Task<float> ComputeLocalCosineAsync(
            string text,
            int anchorIdx,
            string key,
            string replacement,
            int windowChars,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key) || replacement == null) return 1f;
            if (anchorIdx < 0 || anchorIdx + key.Length > text.Length) return 1f;
            if (windowChars < 0) windowChars = 0;

            int start = Math.Max(0, anchorIdx - windowChars);
            int end = Math.Min(text.Length, anchorIdx + key.Length + windowChars);
            string originalLocal = text.Substring(start, end - start);

            int relAnchor = anchorIdx - start;
            string replacedLocal = string.Concat(
                originalLocal.AsSpan(0, relAnchor),
                replacement.AsSpan(),
                originalLocal.AsSpan(relAnchor + key.Length));

            try
            {
                var vecs = await _embedding.EncodeBatchAsync(
                    new[] { originalLocal, replacedLocal },
                    EmbeddingMode.Passage,
                    ct).ConfigureAwait(false);

                if (vecs.Length < 2 || vecs[0] == null || vecs[1] == null) return 1f;

                return VectorMath.DotProduct(vecs[0], vecs[1]);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TM.App.Log($"[SemanticGuard] 局部 cosine 计算失败（容错返回 1.0 视为通过）: {ex.Message}");
                return 1f;
            }
        }
    }
}
