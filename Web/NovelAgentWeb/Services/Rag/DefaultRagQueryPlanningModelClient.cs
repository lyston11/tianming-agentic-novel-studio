using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class DefaultRagQueryPlanningModelClient : IRagQueryPlanningModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultRagQueryPlanningModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<RagQueryPlan> PlanAsync(
        RagQueryPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说创作 RAG 查询规划内核。结合当前用户消息和对话上下文判断真正需要的证据，不得按“开始”“继续”等命令词硬路由。
            只输出一个 JSON object，字段为 routes[]、semanticQueries[]、entityReferences[]、targetChapterIds[]、requiresLongRangeRecall。
            routes 只能使用 Setting、Character、Continuity、Promise、Knowledge、Style。
            semanticQueries 应把省略、代词和承接语义改写成可独立检索的具体查询；entityReferences 只放明确可识别的人物、地点、组织、能力、物件或承诺主体；targetChapterIds 只放上下文中有证据支持的章节 ID。
            不确定时保留必要的多路检索，但不得虚构实体或章节 ID。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "RAG 查询规划模型没有返回内容。",
            "RAG 查询规划模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<RagQueryPlan>(json, JsonOptions)
            ?? throw new InvalidOperationException("RAG 查询规划模型返回了空 JSON。");
    }
}
