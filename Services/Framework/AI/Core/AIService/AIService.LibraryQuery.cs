using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 公共API - 库查询

    public IReadOnlyList<AICategory> GetAllCategories() => _categories.OrderBy(c => c.Order).ToList();

    public IReadOnlyList<AIProvider> GetAllProviders() => _providers.OrderBy(p => p.Order).ToList();

    public List<AIProvider> GetProvidersByCategory(string categoryId)
    {
        return _providers.Where(p => p.Category == categoryId).OrderBy(p => p.Order).ToList();
    }

    public IReadOnlyList<AIModel> GetAllModels() => _models.OrderBy(m => m.Order).ToList();

    public IReadOnlyList<AIModel> GetModelsByProvider(string providerId)
    {
        return _models.Where(m => m.ProviderId == providerId).OrderBy(m => m.Order).ToList();
    }

    public AIProvider? GetProviderById(string providerId)
    {
        return _providers.FirstOrDefault(p => p.Id == providerId);
    }

    public AIModel? GetModelById(string modelId)
    {
        var model = _models.FirstOrDefault(m => m.Id == modelId);
        if (model != null)
            return model;

        return _models.FirstOrDefault(m => m.Name == modelId);
    }

    public bool IsCompatibilityFallbackEnabled(string providerId, string modelId)
    {
        var key = BuildCompatibilityKey(providerId, modelId);
        return _compatibilityFallbackModels.Contains(key);
    }

    public void EnableCompatibilityFallback(string providerId, string modelId)
    {
        var key = BuildCompatibilityKey(providerId, modelId);
        if (string.IsNullOrWhiteSpace(key))
            return;

        _compatibilityFallbackModels.Add(key);
    }

    private static string BuildCompatibilityKey(string providerId, string modelId)
    {
        var p = providerId ?? string.Empty;
        var m = modelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(p) && string.IsNullOrWhiteSpace(m))
            return string.Empty;

        return p.ToLowerInvariant() + "|" + m.ToLowerInvariant();
    }

    public static string GetEffectiveDeveloperMessage(UserConfiguration? config)
    {
        if (config == null)
        {
            return BaseDeveloperMessage;
        }

        if (string.IsNullOrWhiteSpace(config.DeveloperMessage))
        {
            return BaseDeveloperMessage;
        }

        return BaseDeveloperMessage + "\n\n" + config.DeveloperMessage;
    }

    #endregion
}
