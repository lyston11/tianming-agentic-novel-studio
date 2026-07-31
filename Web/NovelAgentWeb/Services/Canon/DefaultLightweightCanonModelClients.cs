using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class DefaultLightweightCanonExtractionModelClient : ILightweightCanonExtractionModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultLightweightCanonExtractionModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<LightweightCanonDraft> ExtractAsync(
        LightweightCanonExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说轻量连续性提取内核。正文是最高优先级证据，只提出少量长期重要候选：关键变化、未决事件、重要承诺、伏笔和下一章承接点，以及死亡、身份公开、核心能力永久变化、重大承诺兑现等重大 CanonChange。
            不要把临时情绪、普通移动、能力冷却或无叙事意义地点强行结构化。每个候选必须给出唯一 id 和正文 UTF-16 字符区间 start/end/quote。
            只输出 JSON object：summaryItems[]、canonChanges[]。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "轻量正史提取模型没有返回内容。",
            "轻量正史提取模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<LightweightCanonDraft>(json, JsonOptions)
            ?? throw new InvalidOperationException("轻量正史提取模型返回了空 JSON。");
    }
}

public sealed class DefaultLightweightCanonSemanticReviewModelClient : ILightweightCanonSemanticReviewModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultLightweightCanonSemanticReviewModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<LightweightCanonSemanticReview> ReviewAsync(
        LightweightCanonSemanticReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是独立小说正史语义审查内核。逐项依据章节正文判断提案是否长期重要，不能因为固定关键词直接批准或拒绝。
            临时情绪、普通移动、短期冷却通常不进入轻量正史，但若正文证据表明它造成长期叙事后果，可以批准并说明原因。
            只输出 JSON object：approvedSummaryItemIds[]、approvedCanonChangeIds[]、rejectionReasons object。只能引用输入中存在的 id。
            """;
        var text = await _completion.CompleteAsync(
            request.Source.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "轻量正史审查模型没有返回内容。",
            "轻量正史审查模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<LightweightCanonSemanticReview>(json, JsonOptions)
            ?? throw new InvalidOperationException("轻量正史审查模型返回了空 JSON。");
    }
}
