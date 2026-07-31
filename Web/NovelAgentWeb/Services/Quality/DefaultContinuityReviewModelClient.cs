using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Quality;

public sealed class DefaultContinuityReviewModelClient : IContinuityReviewModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultContinuityReviewModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<ContinuityReviewDecision> ReviewAsync(
        ContinuityReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是独立小说连续性审校内核。规则结果仅是疑点；必须依据候选正文和原文证据判断谎言、误解、伪装、闪回、合理变化或真实矛盾。
            只输出 JSON object：verdict、claims[]、recommendations[]。verdict 为 Pass、PassWithSuggestions、ReworkRequired、NeedsDecision。
            每个 claim 必须包含 supportingEvidence、contradictingEvidence、narrativeExplanation、confidence 和 verdict。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "连续性模型没有返回内容。",
            "连续性模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<ContinuityReviewDecision>(json, JsonOptions)
            ?? throw new InvalidOperationException("连续性模型返回了空 JSON。");
    }
}
