using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class DefaultKnowledgeStructureModelClient : IKnowledgeStructureModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultKnowledgeStructureModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<KnowledgeStructureAnalysis> AnalyzeAsync(
        KnowledgeStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说知识文档结构化内核。输出 documentSummary、sections[]、styleProfile、highImpactEntryIndexes[]。
            sections 必须按语义边界覆盖重要内容，提供原文 UTF-16 charStart/charEnd；不得重叠。
            styleProfile 只能提取抽象特征：叙事距离、句式节奏、对白密度、描写比例、意象类别、情绪强度和信息释放方式。
            禁止输出作者模仿指令、作者身份、原文续写模板或原文片段。
            可能改变世界设定、人物身份、能力规则、重大剧情或读者承诺的条目索引放入 highImpactEntryIndexes，等待用户确认。
            只输出一个 JSON object。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "知识结构模型没有返回内容。",
            "知识结构模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<KnowledgeStructureAnalysis>(json, JsonOptions)
            ?? throw new InvalidOperationException("知识结构模型返回了空 JSON。");
    }
}
