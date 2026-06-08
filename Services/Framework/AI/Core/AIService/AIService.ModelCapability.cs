using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 内部辅助方法 - 模型协议能力填充

    private async Task FillModelCapabilitiesAsync()
    {
        if (_models == null || _models.Count == 0 || _providers == null || _providers.Count == 0)
            return;

        try
        {
            var asm = typeof(AIService).Assembly;
            string? resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("model-capabilities.json", StringComparison.Ordinal));

            if (resourceName == null)
            {
                TM.App.Log("[AIService] 嵌入资源 model-capabilities.json 未找到，跳过能力填充");
                return;
            }

            await using var stream = asm.GetManifestResourceStream(resourceName)!;
            var ruleSet = await JsonSerializer.DeserializeAsync<ModelCapabilityRuleSet>(stream, _jsonOptions).ConfigureAwait(false);
            var rules = ruleSet?.Rules ?? new List<ModelCapabilityRule>();

            if (rules.Count == 0)
            {
                TM.App.Log("[AIService] 模型能力配置为空，跳过能力填充");
                return;
            }

            var providerMap = new Dictionary<string, AIProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _providers)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) && !providerMap.ContainsKey(p.Id))
                {
                    providerMap[p.Id] = p;
                }
            }

            foreach (var model in _models)
            {
                if (string.IsNullOrWhiteSpace(model.ProviderId))
                    continue;

                if (!providerMap.TryGetValue(model.ProviderId, out var provider))
                    continue;

                foreach (var rule in rules)
                {
                    if (!RuleMatches(rule, provider, model))
                        continue;

                    if (rule.SupportsArrayContent.HasValue)
                        model.SupportsArrayContent = rule.SupportsArrayContent.Value;

                    if (rule.SupportsDeveloperMessage.HasValue)
                        model.SupportsDeveloperMessage = rule.SupportsDeveloperMessage.Value;

                    if (rule.SupportsStreamOptions.HasValue)
                        model.SupportsStreamOptions = rule.SupportsStreamOptions.Value;

                    if (rule.SupportsServiceTier.HasValue)
                        model.SupportsServiceTier = rule.SupportsServiceTier.Value;

                    if (rule.SupportsEnableThinking.HasValue)
                        model.SupportsEnableThinking = rule.SupportsEnableThinking.Value;

                    if (!string.IsNullOrWhiteSpace(rule.ServiceTier))
                        model.ServiceTier = rule.ServiceTier;
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 解析模型能力配置失败: {ex.Message}");
        }
    }

    private static bool RuleMatches(ModelCapabilityRule rule, AIProvider provider, AIModel model)
    {
        if (rule == null)
            return false;

        var providerId = provider.Id ?? string.Empty;
        var providerName = (provider.Name ?? string.Empty).ToLowerInvariant();
        var endpoint = (provider.ApiEndpoint ?? string.Empty).ToLowerInvariant();
        var modelId = model.Id ?? string.Empty;
        var modelName = (model.Name ?? string.Empty).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(rule.ProviderId) &&
            !string.Equals(rule.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ProviderNameContains) &&
            !providerName.Contains(rule.ProviderNameContains.ToLowerInvariant()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.EndpointContains) &&
            !endpoint.Contains(rule.EndpointContains.ToLowerInvariant()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ModelId) &&
            !string.Equals(rule.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ModelNameContains) &&
            !modelName.Contains(rule.ModelNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    #endregion
}
