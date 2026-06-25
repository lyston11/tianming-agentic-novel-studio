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
        var health = await _healthService.CheckAsync(new LlmConnectionHealthInput(
                settings.LlmProvider,
                settings.LlmBaseUrl,
                settings.LlmModel,
                settings.LlmApiKey),
            ct).ConfigureAwait(false);

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
            $"我现在不能开始后台写作任务，因为模型连接未就绪。\n" +
            $"失败阶段：{stage}\n" +
            $"当前模型：{provider} / {model}\n" +
            $"原因：{message}\n" +
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
