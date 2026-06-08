using System.Collections.Generic;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Framework.AI.Interfaces.AI
{
    public interface IAILibraryService
    {
        IReadOnlyList<AICategory> GetAllCategories();

        IReadOnlyList<AIProvider> GetAllProviders();

        IReadOnlyList<AIModel> GetAllModels();

        IReadOnlyList<AIModel> GetModelsByProvider(string providerId);

        AIProvider? GetProviderById(string providerId);

        AIModel? GetModelById(string modelId);

        System.Threading.Tasks.Task ReloadLibraryAsync();

        bool IsCompatibilityFallbackEnabled(string providerId, string modelId);

        void EnableCompatibilityFallback(string providerId, string modelId);
    }
}
