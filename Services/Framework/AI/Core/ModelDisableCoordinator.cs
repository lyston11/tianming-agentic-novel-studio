using System;
using System.Collections.Generic;
using System.Linq;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed class ModelDisableCoordinator
{
    private readonly IAIConfigurationService _aiConfigurationService;
    private readonly IAILibraryService _aiLibraryService;
    private readonly ModelService _modelService;

    public ModelDisableCoordinator(
        IAIConfigurationService aiConfigurationService,
        IAILibraryService aiLibraryService,
        ModelService modelService)
    {
        _aiConfigurationService = aiConfigurationService;
        _aiLibraryService = aiLibraryService;
        _modelService = modelService;
    }

    public UserConfiguration? DisableSingle(UserConfiguration model, string logPrefix)
    {
        if (model == null) return null;

        var activeBefore = _aiConfigurationService.GetActiveConfiguration();
        var shouldReassignActive = activeBefore == null || string.Equals(activeBefore.Id, model.Id, StringComparison.OrdinalIgnoreCase);

        model.IsEnabled = false;
        _aiConfigurationService.UpdateConfiguration(model);
        SyncDisabledState(model, logPrefix);

        if (!shouldReassignActive)
            return _aiConfigurationService.GetActiveConfiguration();

        var newActive = FindReplacement(model.Id);
        if (newActive != null)
            _aiConfigurationService.SetActiveConfiguration(newActive);

        return newActive;
    }

    public DisableBatchResult DisableBatch(IEnumerable<UserConfiguration> models, string logPrefix)
    {
        int successCount = 0;
        var modelList = models?.ToList() ?? new List<UserConfiguration>();
        var activeBefore = _aiConfigurationService.GetActiveConfiguration();
        var shouldReassignActive = activeBefore == null || modelList.Any(m =>
            m != null && string.Equals(m.Id, activeBefore.Id, StringComparison.OrdinalIgnoreCase));

        foreach (var model in modelList)
        {
            if (model == null) continue;
            try
            {
                model.IsEnabled = false;
                _aiConfigurationService.UpdateConfiguration(model);
                SyncDisabledState(model, logPrefix);
                successCount++;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{logPrefix}] 批量禁用单模型失败 {model.Name}: {ex.Message}");
            }
        }

        if (!shouldReassignActive)
            return new DisableBatchResult(successCount, _aiConfigurationService.GetActiveConfiguration());

        var newActive = FindReplacement(modelList.Select(m => m.Id));
        if (newActive != null)
            _aiConfigurationService.SetActiveConfiguration(newActive);

        return new DisableBatchResult(successCount, newActive);
    }

    private void SyncDisabledState(UserConfiguration model, string logPrefix)
    {
        try
        {
            var providerName = _aiLibraryService.GetAllProviders()
                .FirstOrDefault(p => string.Equals(p.Id, model.ProviderId, StringComparison.OrdinalIgnoreCase))
                ?.Name;

            var matchingConfig = _modelService.GetAllData().FirstOrDefault(c =>
                string.Equals(c.ModelName, model.ModelId, StringComparison.OrdinalIgnoreCase) &&
                (
                    string.Equals(c.CategoryId, model.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(providerName) && string.Equals(c.Category, providerName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(providerName) && string.Equals(c.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
                ));

            if (matchingConfig != null)
            {
                matchingConfig.IsEnabled = false;
                _modelService.UpdateConfiguration(matchingConfig);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[{logPrefix}] 禁用模型同步到ModelService失败: {ex.Message}");
        }
    }

    private UserConfiguration? FindReplacement(string excludedId)
    {
        return _aiConfigurationService.GetAllConfigurations()
            .FirstOrDefault(c => c.IsEnabled &&
                !string.Equals(c.Id, excludedId, StringComparison.OrdinalIgnoreCase));
    }

    private UserConfiguration? FindReplacement(IEnumerable<string> excludedIds)
    {
        var excluded = new HashSet<string>(
            excludedIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        return _aiConfigurationService.GetAllConfigurations()
            .FirstOrDefault(c => c.IsEnabled && !excluded.Contains(c.Id));
    }
}

public sealed class DisableBatchResult
{
    public DisableBatchResult(int successCount, UserConfiguration? newActive)
    {
        SuccessCount = successCount;
        NewActive = newActive;
    }

    public int SuccessCount { get; }
    public UserConfiguration? NewActive { get; }
}
