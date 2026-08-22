using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Rework;

public sealed class DefaultReworkIntentModelClient : IReworkIntentModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultReworkIntentModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<ReworkIntentDraft> CompileAsync(
        ReworkIntentCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说导演内核，负责把用户针对候选章节的自然语言反馈编译成定向返工合同。
            必须结合候选正文、对话描述和可选选区理解具体问题，不得按“开始、继续”等关键词路由。
            只输出一个 JSON object，字段为 targetScope、problem、desiredEffect、preserve[]、mayChange[]、mustNotChange[]、acceptanceCriteria[]、impactLevel、impactAssessment。
            targetScope 只能是 selection 或 chapter；没有选区时不得输出 selection。
            impactLevel 只能是 copy、local_fact、key_plot、hard_conflict。mustNotChange 必须保护合同范围外正文和用户明确保留项。
            """;
        var text = await _completion.CompleteAsync(
            context.UserId,
            system,
            JsonSerializer.Serialize(context, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "返工意图模型没有返回内容。",
            "返工意图模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<ReworkIntentDraft>(json, JsonOptions)
            ?? throw new InvalidOperationException("返工意图模型返回了空 JSON。");
    }
}
