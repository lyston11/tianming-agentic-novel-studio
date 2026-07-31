using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class WritingModelProviderResponseParserTests
{
    [Fact]
    public void ParseOpenAi_ReadsRequestIdTextAndStandardUsage()
    {
        const string json = """
            {
              "id": "chatcmpl-123",
              "choices": [{"message": {"content": "章节正文"}}],
              "usage": {"prompt_tokens": 321, "completion_tokens": 654}
            }
            """;

        var result = WritingModelProviderResponseParser.ParseOpenAi(
            json, "openai", "models/model-a", 2m, 8m);

        Assert.Equal("chatcmpl-123", result.ProviderRequestId);
        Assert.Equal("章节正文", result.Text);
        Assert.Equal("model-a", result.Model);
        Assert.Equal(321, result.InputTokens);
        Assert.Equal(654, result.OutputTokens);
    }

    [Fact]
    public void ParseAnthropic_ReadsRequestIdTextAndStandardUsage()
    {
        const string json = """
            {
              "id": "msg-123",
              "content": [{"type": "text", "text": "第一段"}, {"type": "text", "text": "第二段"}],
              "usage": {"input_tokens": 111, "output_tokens": 222}
            }
            """;

        var result = WritingModelProviderResponseParser.ParseAnthropic(
            json, "anthropic", "claude-test", 3m, 9m);

        Assert.Equal("msg-123", result.ProviderRequestId);
        Assert.Equal("第一段\n第二段", result.Text);
        Assert.Equal(111, result.InputTokens);
        Assert.Equal(222, result.OutputTokens);
    }

    [Fact]
    public void ParseOpenAi_WhenUsageIsMissingMarksUsageAsUnreported()
    {
        const string json = "{\"id\":\"chatcmpl-no-usage\",\"choices\":[{\"message\":{\"content\":\"正文\"}}]}";

        var result = WritingModelProviderResponseParser.ParseOpenAi(
            json, "compatible", "model-a", 2m, 8m);

        Assert.False(result.UsageReported);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
    }
}
