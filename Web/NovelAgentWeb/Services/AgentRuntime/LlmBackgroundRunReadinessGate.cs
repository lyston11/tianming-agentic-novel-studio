using TM.Web.NovelAgentWeb.Services.Settings;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class LlmBackgroundRunReadinessGate : IAgentBackgroundRunReadinessGate
{
    private readonly UserSettingsManager _settingsManager;
    private readonly ILlmConnectionHealthService _healthService;

    public LlmBackgroundRunReadinessGate(
        UserSettingsManager settingsManager,
        ILlmConnectionHealthService healthService)
    {
        _settingsManager = settingsManager;
        _healthService = healthService;
    }

    public async Task<AgentBackgroundRunReadiness> CheckAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct = default)
    {
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var input = new LlmConnectionHealthInput(
                settings.LlmProvider,
                settings.LlmBaseUrl,
                settings.LlmModel,
                settings.LlmApiKey);
        var health = settings.LlmApiKeyEncryptedValuePresent && !settings.LlmApiKeyReadable
            ? LlmConnectionHealthResult.ApiKeyUnavailable(
                input.Normalize(),
                "模型 API Key 不可用，可能已经过期、失效，或需要重新保存。",
                "请在用户设置中重新粘贴并保存 API Key，然后重新检测模型连接。")
            : await _healthService.CheckAsync(input, ct).ConfigureAwait(false);

        if (string.Equals(health.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return AgentBackgroundRunReadiness.Ready();

        return AgentBackgroundRunReadiness.Blocked(
            "llm_not_ready",
            BuildReply(health),
            new[] { "打开用户设置", "重新检测模型", "修正 API Key 后重试" });
    }

    private static string BuildReply(LlmConnectionHealthResult health)
    {
        var provider = FirstNonEmpty(health.Provider, "未配置 Provider");
        var model = FirstNonEmpty(health.Model, "未配置模型");
        var stage = FirstNonEmpty(health.FailureStage, health.Status, "unknown");
        var message = FirstNonEmpty(health.Message, "模型连接当前不可用。");
        var action = FirstNonEmpty(health.RecommendedAction, "请在用户设置中检查模型配置后重试。");

        return
            $"当前阻断点是模型连接未就绪，Agent 不会启动后台工具链。\n" +
            $"原因：{message}\n" +
            $"失败阶段：{stage}\n" +
            $"当前模型：{provider} / {model}\n" +
            $"下一步：{action}";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
