using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 库加载

    private async Task LoadLibraryAsync()
    {
        var newCategories = new List<AICategory>();
        var newProviders = new List<AIProvider>();
        var newModels = new List<AIModel>();

        try
        {
            var categoriesFile = Path.Combine(_libraryPath, "categories.json");
            if (File.Exists(categoriesFile))
            {
                var json = await File.ReadAllTextAsync(categoriesFile).ConfigureAwait(false);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith('['))
                {
                    newCategories = JsonSerializer.Deserialize<List<AICategory>>(json, _jsonOptions) ?? new List<AICategory>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json, _jsonOptions);
                    newCategories = wrapper?.Categories ?? new List<AICategory>();
                }

                TM.App.Log($"[AIService] 加载分类: {newCategories.Count}个");
            }

            var providersFile = Path.Combine(_libraryPath, "providers.json");
            if (File.Exists(providersFile))
            {
                var json = await File.ReadAllTextAsync(providersFile).ConfigureAwait(false);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith('['))
                {
                    newProviders = JsonSerializer.Deserialize<List<AIProvider>>(json, _jsonOptions) ?? new List<AIProvider>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<ProviderWrapper>(json, _jsonOptions);
                    newProviders = wrapper?.Providers ?? new List<AIProvider>();
                }

                TM.App.Log($"[AIService] 加载供应商: {newProviders.Count}个");
            }

            var modelsFile = Path.Combine(_libraryPath, "models.json");
            if (File.Exists(modelsFile))
            {
                var json = await File.ReadAllTextAsync(modelsFile).ConfigureAwait(false);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith('['))
                {
                    newModels = JsonSerializer.Deserialize<List<AIModel>>(json, _jsonOptions) ?? new List<AIModel>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<ModelWrapper>(json, _jsonOptions);
                    newModels = wrapper?.Models ?? new List<AIModel>();
                }

                TM.App.Log($"[AIService] 从 models.json 加载模型: {newModels.Count}个");
            }
            else
            {
                var providerModelsPath = Path.Combine(_libraryPath, "ProviderModels");
                if (Directory.Exists(providerModelsPath))
                {
                    var modelFiles = Directory.GetFiles(providerModelsPath, "*.models.json");
                    var readTasks = modelFiles.Select(async file =>
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                            return JsonSerializer.Deserialize<List<AIModel>>(json, _jsonOptions);
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[AIService] 加载 {Path.GetFileName(file)} 失败: {ex.Message}");
                            return null;
                        }
                    });
                    var results = await Task.WhenAll(readTasks).ConfigureAwait(false);
                    foreach (var models in results)
                        if (models != null && models.Count > 0)
                            newModels.AddRange(models);
                    TM.App.Log($"[AIService] 从 ProviderModels 目录加载模型: {newModels.Count}个（来自 {modelFiles.Length} 个文件）");
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 加载模型库失败: {ex.Message}");
        }

        _categories = newCategories;
        _providers = newProviders;
        _models = newModels;

        try
        {
            await FillModelCapabilitiesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 填充模型协议能力失败: {ex.Message}");
        }
    }

    public async Task ReloadLibraryAsync()
    {
        await LoadLibraryAsync().ConfigureAwait(false);
        FillCategoryPrefixes();
    }

    private async System.Threading.Tasks.Task LoadUserConfigurationsAsync()
    {
        try
        {
            Directory.CreateDirectory(_configurationsPath);
            var configFile = Path.Combine(_configurationsPath, "user_configurations.json");

            if (File.Exists(configFile))
            {
                await using var stream = File.OpenRead(configFile);
                var wrapper = await JsonSerializer.DeserializeAsync<ConfigurationWrapper>(stream, _jsonOptions).ConfigureAwait(false);
                var loaded = wrapper?.Configurations ?? new List<UserConfiguration>();
                lock (_userConfigurationsLock)
                    _userConfigurations = loaded;
                TM.App.Log($"[AIService] 异步加载用户配置: {loaded.Count}个");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 异步加载用户配置失败: {ex.Message}");
        }
    }

    private async Task SaveUserConfigurations()
    {
        var acquired = false;
        try
        {
            await _userConfigurationsSaveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            Directory.CreateDirectory(_configurationsPath);
            var configFile = Path.Combine(_configurationsPath, "user_configurations.json");

            UserConfiguration[] snapshot;
            lock (_userConfigurationsLock)
                snapshot = _userConfigurations.ToArray();

            var wrapper = new ConfigurationWrapper { Configurations = new List<UserConfiguration>(snapshot) };
            var tmp = configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, wrapper, _jsonOptions).ConfigureAwait(false);
            }
            File.Move(tmp, configFile, overwrite: true);

            TM.App.Log($"[AIService] 保存用户配置: {snapshot.Length}个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 保存用户配置失败: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _userConfigurationsSaveLock.Release();
        }
    }

    #endregion
}
