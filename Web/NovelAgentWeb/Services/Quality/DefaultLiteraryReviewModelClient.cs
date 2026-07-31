using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Quality;

public sealed class DefaultLiteraryReviewModelClient : ILiteraryReviewModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultLiteraryReviewModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<LiteraryReviewDecision> ReviewAsync(
        LiteraryReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是独立小说审美审稿内核。分别评估人物可信度、剧情推进、节奏与悬念、情绪效果、文风语言、原创性和读者承诺。
            不得输出单一总分；每个维度必须给正文证据。主观偏好只能形成建议，不能伪装成硬错误。
            只输出 JSON object：verdict、dimensions object、suggestions[]。verdict 为 Pass、PassWithSuggestions、ReworkRequired、NeedsDecision。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "审美审稿模型没有返回内容。",
            "审美审稿模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<LiteraryReviewDecision>(json, JsonOptions)
            ?? throw new InvalidOperationException("审美审稿模型返回了空 JSON。");
    }
}
