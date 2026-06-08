using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.SemanticGuard
{
    public interface ISemanticGuard
    {
        bool IsReady { get; }

        Task<float> ComputeLocalCosineAsync(
            string text,
            int anchorIdx,
            string key,
            string replacement,
            int windowChars,
            CancellationToken ct = default);
    }
}
