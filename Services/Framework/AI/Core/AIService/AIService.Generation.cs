using System;
using System.Linq;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{
    #region 公共API - 统一生成入口

    public async System.Threading.Tasks.Task<GenerationResult> GenerateAsync(string prompt)
    {
        return await GenerateAsync(prompt, System.Threading.CancellationToken.None).ConfigureAwait(false);
    }

    System.Threading.Tasks.Task<GenerationResult> IAITextGenerationService.GenerateAsync(string prompt, System.Threading.CancellationToken ct)
    {
        return GenerateAsync(prompt, ct);
    }

    public async System.Threading.Tasks.Task<GenerationResult> GenerateAsync(string prompt, System.Threading.CancellationToken ct)
    {
        var result = new GenerationResult();
        using var _progressRunScope = TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.BeginRun(Guid.Empty);

        try
        {
            ct.ThrowIfCancellationRequested();

            var activeConfig = GetActiveConfiguration();
            if (activeConfig == null)
            {
                const string configureMessage = "当前没有激活的AI模型，请前往“智能助手 > 模型管理”完成配置后重试。";
                TM.App.Log("[AIService] 未检测到激活的AI模型配置");

                result.Success = false;
                result.ErrorMessage = configureMessage;
                return result;
            }

            if (string.IsNullOrWhiteSpace(activeConfig.ProviderId) || string.IsNullOrWhiteSpace(activeConfig.ModelId))
            {
                TM.App.Log($"[AIService] 激活配置缺少 ProviderId 或 ModelId: {activeConfig.Id}");
                result.Success = false;
                result.ErrorMessage = "AI配置缺少供应商或模型信息，请检查模型设置。";
                return result;
            }

            var provider = GetProviderById(activeConfig.ProviderId);
            if (provider == null)
            {
                TM.App.Log($"[AIService] 未找到供应商: {activeConfig.ProviderId}");
                result.Success = false;
                result.ErrorMessage = $"未找到供应商: {activeConfig.ProviderId}";
                return result;
            }

            var model = GetModelById(activeConfig.ModelId);
            var modelDisplayName = model?.Name ?? activeConfig.ModelId;
            if (model == null && InfoLogDedup.ShouldLog($"AIService:CustomModel:{activeConfig.ModelId}"))
            {
                TM.App.Log($"[AIService] 模型库未收录 {activeConfig.ModelId}，将作为自定义模型继续调用");
            }

            TM.App.Log($"[AIService] 使用激活配置 {activeConfig.Name} 调用模型 {modelDisplayName} 生成内容");

            try
            {
                var skService = ServiceLocator.Get<SemanticKernel.SKChatService>();

                var developerPrompt = GetEffectiveDeveloperMessage(activeConfig) ?? string.Empty;

                var text = await skService.GenerateOneShotAsync(
                    developerPrompt,
                    prompt,
                    ct).ConfigureAwait(false);

                var (isCancelled, _) = TM.Services.Framework.AI.SemanticKernel.UIMessageItem.TryExtractCancelledPartial(text);
                if (text.StartsWith("[错误]", StringComparison.Ordinal) || isCancelled)
                {
                    result.Success = false;
                    result.ErrorMessage = isCancelled ? "[已取消]" : text;
                    return result;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    result.Success = false;
                    result.ErrorMessage = "AI未返回任何内容，请稍后重试";
                    return result;
                }

                if (IsAIRefusal(text))
                {
                    result.Success = false;
                    result.ErrorMessage = $"[模型拒绝] {text.Substring(0, Math.Min(120, text.Length))}";
                    TM.App.Log($"[AIService] 检测到模型拒绝响应，model={modelDisplayName}，前60字: {text.Substring(0, Math.Min(60, text.Length))}");
                    try { GlobalToast.Warning("模型拒绝回复", "当前模型拒绝应答本次请求，请调整输入内容或更换模型重试。"); } catch { }
                    return result;
                }

                result.Success = true;
                result.Content = text;
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AIService] SK调用失败: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] GenerateAsync 调用失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = $"AI生成失败：{ex.Message}";
            return result;
        }
    }

    public async System.Threading.Tasks.Task<GenerationResult> GenerateWithConfigAsync(
        string? configId, string prompt, System.Threading.CancellationToken ct)
    {
        using var _progressRunScope = TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.BeginRun(Guid.Empty);

        if (string.IsNullOrWhiteSpace(configId))
            return await GenerateAsync(prompt, ct).ConfigureAwait(false);

        UserConfiguration? targetConfig;
        lock (_userConfigurationsLock)
            targetConfig = _userConfigurations.FirstOrDefault(c => c.Id == configId && c.IsEnabled);

        if (targetConfig == null)
        {
            TM.App.Log($"[AIService] 指定配置 {configId} 不存在或未启用，回退激活配置");
            return await GenerateAsync(prompt, ct).ConfigureAwait(false);
        }

        try
        {
            var result = new GenerationResult();
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(targetConfig.ProviderId) || string.IsNullOrWhiteSpace(targetConfig.ModelId))
            {
                result.Success = false;
                result.ErrorMessage = "润色配置缺少供应商或模型信息，请检查写作配置。";
                return result;
            }

            var skService = ServiceLocator.Get<SemanticKernel.SKChatService>();
            var developerPrompt = GetEffectiveDeveloperMessage(targetConfig) ?? string.Empty;

            TM.App.Log($"[AIService] GenerateWithConfig: 使用配置 {targetConfig.Name}({targetConfig.ModelId}) 生成");

            var text = await skService.GenerateOneShotAsync(targetConfig, developerPrompt, prompt, ct).ConfigureAwait(false);

            var (isCancelledCfg, _) = TM.Services.Framework.AI.SemanticKernel.UIMessageItem.TryExtractCancelledPartial(text);
            if (text.StartsWith("[错误]", StringComparison.Ordinal) || isCancelledCfg)
            {
                result.Success = false;
                result.ErrorMessage = isCancelledCfg ? "[已取消]" : text;
                return result;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Success = false;
                result.ErrorMessage = "AI未返回任何内容，请稍后重试";
                return result;
            }

            if (IsAIRefusal(text))
            {
                result.Success = false;
                result.ErrorMessage = $"[模型拒绝] {text.Substring(0, Math.Min(120, text.Length))}";
                try { GlobalToast.Warning("模型拒绝回复", "当前模型拒绝应答本次请求，请调整输入内容或更换模型重试。"); } catch { }
                return result;
            }

            result.Success = true;
            result.Content = text;
            return result;
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] GenerateWithConfig 调用失败: {ex.Message}");
            return new GenerationResult { Success = false, ErrorMessage = $"AI生成失败：{ex.Message}" };
        }
    }

    public static bool IsAIRefusal(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var head = content.Length > 80 ? content.Substring(0, 80) : content;

        return head.Contains("我不能协助", StringComparison.OrdinalIgnoreCase)
            || head.Contains("我无法协助", StringComparison.OrdinalIgnoreCase)
            || head.Contains("我不能帮助", StringComparison.OrdinalIgnoreCase)
            || head.Contains("无法帮助你", StringComparison.OrdinalIgnoreCase)
            || head.Contains("我无法完成", StringComparison.OrdinalIgnoreCase)
            || head.Contains("我必须拒绝", StringComparison.OrdinalIgnoreCase)
            || head.Contains("我需要拒绝", StringComparison.OrdinalIgnoreCase)
            || head.Contains("很抱歉，我无法", StringComparison.OrdinalIgnoreCase)
            || head.Contains("很抱歉，我不能", StringComparison.OrdinalIgnoreCase)
            || head.Contains("对不起，我无法", StringComparison.OrdinalIgnoreCase)
            || head.Contains("对不起，我不能", StringComparison.OrdinalIgnoreCase)
            || head.Contains("这种请求违反", StringComparison.OrdinalIgnoreCase)
            || head.Contains("违反我的使用条款", StringComparison.OrdinalIgnoreCase)
            || head.Contains("违反使用政策", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I'm unable to", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I am unable to", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I cannot help", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I can't help", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I'm not able to", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I am not able to", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I must decline", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I need to decline", StringComparison.OrdinalIgnoreCase)
            || head.Contains("I'm not comfortable", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Sorry, I cannot", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Sorry, I'm unable", StringComparison.OrdinalIgnoreCase);
    }

    public class GenerationResult
    {
        public string Content { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    #endregion
}
