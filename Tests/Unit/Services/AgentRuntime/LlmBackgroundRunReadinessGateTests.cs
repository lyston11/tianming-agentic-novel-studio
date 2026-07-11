using System.Reflection;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Settings;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public sealed class LlmBackgroundRunReadinessGateTests
{
    [Fact]
    public void BuildReply_WhenApiKeyUnavailableExplainsDirectBlockWithoutStartingTaskLanguage()
    {
        var health = LlmConnectionHealthResult.AuthenticationFailed(
            new LlmConnectionHealthInput(
                Provider: "anthropic",
                BaseUrl: "https://example.test/anthropic",
                Model: "mimo-v2.5-pro",
                ApiKey: string.Empty).Normalize(),
            401,
            "模型 API Key 不可用，可能已经过期、失效，或需要重新保存。",
            "请在用户设置中重新粘贴并保存 API Key，然后重新检测模型连接。");
        var method = typeof(LlmBackgroundRunReadinessGate).GetMethod(
            "BuildReply",
            BindingFlags.NonPublic | BindingFlags.Static);

        var reply = Assert.IsType<string>(method?.Invoke(null, new object[] { health }));

        Assert.Contains("模型 API Key 不可用", reply);
        Assert.Contains("不会启动后台工具链", reply);
        Assert.DoesNotContain("后台写作任务", reply);
    }
}
