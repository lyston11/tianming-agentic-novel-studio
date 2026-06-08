using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Embedding
{
    public enum EmbeddingMode
    {
        Query = 0,
        Passage = 1
    }

    public interface IMicroEmbeddingService
    {
        Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default);

        Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default);

        void ReleaseSession();

        bool IsModelReady();

        int Dimension { get; }
    }
}
