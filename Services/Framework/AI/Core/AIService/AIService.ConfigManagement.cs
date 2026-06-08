using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 公共API - 用户配置管理

    public IReadOnlyList<UserConfiguration> GetAllConfigurations()
    {
        lock (_userConfigurationsLock)
            return _userConfigurations.ToList();
    }

    public UserConfiguration? GetActiveConfiguration()
    {
        lock (_userConfigurationsLock)
            return _userConfigurations.FirstOrDefault(c => c.IsActive);
    }

    private void EnsureInitialized()
    {
        var task = InitializedAsync;
        if (task != null && !task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
        }
    }

    public void AddConfiguration(UserConfiguration config)
    {
        EnsureInitialized();
        UserConfiguration? existing;
        lock (_userConfigurationsLock)
            existing = _userConfigurations.FirstOrDefault(c =>
                string.Equals(c.ProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.ModelId, config.ModelId, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            config.Id = existing.Id;
            config.CreatedAt = existing.CreatedAt;
            UpdateConfiguration(config);
            return;
        }

        config.Id = ShortIdGenerator.New("D");
        config.CreatedAt = DateTime.Now;
        config.UpdatedAt = DateTime.Now;
        lock (_userConfigurationsLock)
            _userConfigurations.Add(config);
        FillCategoryPrefixForSingle(config);
        SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
        TM.App.Log($"[AIService] 添加配置: {config.Name}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateConfiguration(UserConfiguration config)
    {
        EnsureInitialized();
        int index;
        lock (_userConfigurationsLock)
            index = _userConfigurations.FindIndex(c => c.Id == config.Id);
        if (index >= 0)
        {
            config.UpdatedAt = DateTime.Now;
            FillCategoryPrefixForSingle(config);
            config.AutoDisabledBySystem = false;
            lock (_userConfigurationsLock)
            {
                _userConfigurations[index] = config;
                if (!config.IsEnabled && config.IsActive)
                {
                    UserConfiguration? replacement = null;
                    foreach (var item in _userConfigurations)
                    {
                        item.IsActive = false;
                        if (replacement == null && item.IsEnabled && !string.Equals(item.Id, config.Id, StringComparison.OrdinalIgnoreCase))
                            replacement = item;
                    }
                    if (replacement != null) replacement.IsActive = true;
                }
            }
            SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
            TM.App.Log($"[AIService] 更新配置: {config.Name}");
            ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteConfiguration(string configId)
    {
        EnsureInitialized();
        UserConfiguration? config;
        lock (_userConfigurationsLock)
            config = _userConfigurations.FirstOrDefault(c => c.Id == configId);
        if (config != null)
        {
            lock (_userConfigurationsLock)
            {
                _userConfigurations.Remove(config);
                if (config.IsActive || !_userConfigurations.Any(c => c.IsActive))
                {
                    UserConfiguration? replacement = null;
                    foreach (var item in _userConfigurations)
                    {
                        item.IsActive = false;
                        if (replacement == null && item.IsEnabled)
                            replacement = item;
                    }
                    if (replacement != null) replacement.IsActive = true;
                }
            }
            SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
            TM.App.Log($"[AIService] 删除配置: {config.Name}");
            ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetActiveConfiguration(string configId)
    {
        EnsureInitialized();

        UserConfiguration? target;
        lock (_userConfigurationsLock)
            target = _userConfigurations.FirstOrDefault(c =>
                string.Equals(c.Id, configId, StringComparison.OrdinalIgnoreCase) && c.IsEnabled);
        if (target == null)
        {
            TM.App.Log($"[AIService] 忽略激活请求：目标配置不存在或已禁用: {configId}");
            return;
        }

        lock (_userConfigurationsLock)
        {
            foreach (var config in _userConfigurations)
                config.IsActive = config.Id == configId;
        }
        SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
        TM.App.Log($"[AIService] 激活配置: {configId}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DisableConfigurationsByProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        EnsureInitialized();

        int disabledCount = 0;
        lock (_userConfigurationsLock)
        {
            UserConfiguration? replacement = null;
            foreach (var c in _userConfigurations)
            {
                if (string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) && c.IsEnabled)
                {
                    c.IsEnabled = false;
                    c.IsActive = false;
                    c.AutoDisabledBySystem = true;
                    disabledCount++;
                }
                else if (replacement == null && c.IsEnabled)
                {
                    replacement = c;
                }
            }
            if (disabledCount == 0) return;

            foreach (var item in _userConfigurations)
                item.IsActive = replacement != null && item.Id == replacement.Id;
        }

        SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
        TM.App.Log($"[AIService] 服务商Key全部耗尽，已批量禁用 {disabledCount} 个配置: providerId={providerId}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnableConfigurationsByProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        EnsureInitialized();

        int enabledCount = 0;
        lock (_userConfigurationsLock)
        {
            foreach (var c in _userConfigurations)
            {
                if (string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                    && !c.IsEnabled
                    && c.AutoDisabledBySystem)
                {
                    c.IsEnabled = true;
                    c.AutoDisabledBySystem = false;
                    enabledCount++;
                }
            }
            if (enabledCount == 0) return;

            if (!_userConfigurations.Any(c => c.IsActive))
            {
                var first = _userConfigurations.FirstOrDefault(c =>
                    string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) && c.IsEnabled);
                if (first != null) first.IsActive = true;
            }
        }

        SaveUserConfigurations().SafeFireAndForget(ex => TM.App.Log($"[AIService] {ex.Message}"));
        TM.App.Log($"[AIService] 服务商密钥恢复，已批量启用 {enabledCount} 个自动禁用配置: providerId={providerId}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    void IAIConfigurationService.SetActiveConfiguration(UserConfiguration configuration)
    {
        if (configuration == null || string.IsNullOrWhiteSpace(configuration.Id))
        {
            return;
        }

        SetActiveConfiguration(configuration.Id);
    }

    #endregion
}
