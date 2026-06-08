using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.User.Services;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

namespace TM.Services.Framework.AI
{
    public class BuiltInConfigSyncService
    {
        private const string BuiltInCategoryName = "天命模型";

        public static bool PaidPasswordRequired { get; set; }
        public static bool PublicPasswordRequired { get; set; }

        private readonly ApiService _apiService;

        public BuiltInConfigSyncService(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task SyncAsync()
        {
            try
            {
                var result = await _apiService.GetBuiltInConfigsAsync().ConfigureAwait(false);
                if (!result.Success || result.Data == null)
                {
                    TM.App.Log($"[BuiltInConfigSyncService] 拉取失败: {result.Message}");
                    return;
                }

                var builtInCategoriesPath = StoragePathHelper.GetFilePath("Services", "AI/Library", "built_in_categories.json");

                PaidPasswordRequired = result.Data.PaidPasswordRequired;
                PublicPasswordRequired = result.Data.PublicPasswordRequired;

                var categoriesToPurge = await WriteBuiltInEntriesAsync(builtInCategoriesPath, result.Data.CategoryId, result.Data.Providers).ConfigureAwait(false);

                var modelService = ServiceLocator.Get<ModelService>();

                await modelService.EnsureInitializedAsync().ConfigureAwait(false);

                if (categoriesToPurge.Count > 0)
                {
                    foreach (var categoryName in categoriesToPurge)
                    {
                        modelService.CascadeDeleteCategory(categoryName);
                    }
                }

                await modelService.ReloadAsync().ConfigureAwait(false);
                await modelService.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);

                TM.App.Log($"[BuiltInConfigSyncService] 同步完成，共 {result.Data.Providers.Count} 个供应商");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BuiltInConfigSyncService] 同步异常: {ex.Message}");
            }
        }

        private static async Task<List<string>> WriteBuiltInEntriesAsync(string filePath, string categoryId, List<BuiltInProviderItem> providers)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                List<AIProviderCategory> categories;
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                    categories = JsonSerializer.Deserialize<List<AIProviderCategory>>(json, JsonHelper.Default) ?? new();
                }
                else
                {
                    categories = await LoadEmbeddedAiBuiltInCategoriesAsync().ConfigureAwait(false);
                    TM.App.Log($"[BuiltInConfigSyncService] Storage 缓存不存在，从 DLL 嵌入基线初始化 {categories.Count} 项");
                }

                var lv2Entries = categories
                    .Where(c => c.Level == 2 && c.IsBuiltIn &&
                                (string.Equals(c.ParentCategory, categoryId, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(c.ParentCategory, BuiltInCategoryName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var categoriesToPurge = new List<string>();
                foreach (var entry in lv2Entries)
                {
                    var entryCategory = ResolveEntryCategory(entry.Id);
                    if (string.IsNullOrEmpty(entryCategory))
                        continue;

                    var match = providers.FirstOrDefault(p =>
                        string.Equals(p.Category, entryCategory, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        var oldEndpoint = entry.ApiEndpoint ?? string.Empty;
                        var oldKeyPlain = entry.ApiKey ?? string.Empty;
                        bool endpointChanged = !string.Equals(oldEndpoint, match.Endpoint ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                        bool keyChanged = !string.Equals(oldKeyPlain, match.ApiKey ?? string.Empty, StringComparison.Ordinal);

                        entry.ApiEndpoint = match.Endpoint;
                        entry.ApiKey = LocalKeyProtector.Protect(match.ApiKey);

                        if (endpointChanged || keyChanged)
                        {
                            entry.ModelsEndpoint = null;
                            entry.ChatEndpoint = null;
                            entry.EndpointVerifiedAt = null;
                            entry.EndpointSignature = null;
                        }
                    }
                    else
                    {
                        entry.ApiEndpoint = null;
                        entry.ApiKeys = null;
                        entry.ModelsEndpoint = null;
                        entry.ChatEndpoint = null;
                        entry.EndpointVerifiedAt = null;
                        entry.EndpointSignature = null;
                        categoriesToPurge.Add(entry.Name ?? entry.Id ?? entryCategory);
                    }
                }

                var output = JsonSerializer.Serialize(categories, JsonHelper.Default);
                var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, output).ConfigureAwait(false);
                File.Move(tmp, filePath, overwrite: true);
                TM.App.Log($"[BuiltInConfigSyncService] 写入 built_in_categories.json: {providers.Count} 个供应商配置已同步");
                return categoriesToPurge;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BuiltInConfigSyncService] 写 built_in_categories.json 失败: {ex.Message}");
                return new List<string>();
            }
        }

        private static async Task<List<AIProviderCategory>> LoadEmbeddedAiBuiltInCategoriesAsync()
        {
            try
            {
                var asm = typeof(BuiltInConfigSyncService).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Services.Framework.AI.Library.Resources.built_in_categories.json", StringComparison.Ordinal));
                if (resourceName == null)
                {
                    TM.App.Log("[BuiltInConfigSyncService] 嵌入资源 built_in_categories.json 未找到");
                    return new List<AIProviderCategory>();
                }

                await using var stream = asm.GetManifestResourceStream(resourceName)!;
                return await JsonSerializer.DeserializeAsync<List<AIProviderCategory>>(stream, JsonHelper.Default).ConfigureAwait(false)
                    ?? new List<AIProviderCategory>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BuiltInConfigSyncService] 加载嵌入内置分类基线失败: {ex.Message}");
                return new List<AIProviderCategory>();
            }
        }

        private static string? ResolveEntryCategory(string? entryId)
            => TM.Services.Framework.AI.Core.TianmingProviderIdentity.ResolveEntryCategory(entryId);
    }
}
