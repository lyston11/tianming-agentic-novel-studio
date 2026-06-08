using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Framework.AI.Interfaces.AI
{
    public interface IAITextGenerationService
    {
        Task<AIService.GenerationResult> GenerateAsync(string prompt);

        Task<AIService.GenerationResult> GenerateAsync(string prompt, CancellationToken ct);
    }
}
