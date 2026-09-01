using System.Text.Encodings.Web;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class DefaultAgentEditorialReviewModelClient : IAgentEditorialReviewModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IWritingModelCompletionService _completion;
    private readonly ILogger<DefaultAgentEditorialReviewModelClient> _logger;

    public DefaultAgentEditorialReviewModelClient(
        IWritingModelCompletionService completion,
        ILogger<DefaultAgentEditorialReviewModelClient> logger)
    {
        _completion = completion;
        _logger = logger;
    }

    public async Task<AgentEditorialReviewDecision> ReviewAsync(
        AgentEditorialReviewRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("AgentReview 缺少用户上下文，不能调用用户模型配置。");

        var system = """
        你是小说 Agent 的总编验收模型。
        你的职责不是写正文，而是判断本章是否真正落实用户已采纳创意、RevisionPlan 和章节目标。
        StoryConstitution 是本书的类型承诺、读者承诺和核心爽点，必须作为验收依据。
        你必须基于语义判断，不要做关键词匹配，不要因为出现一两个词就判定通过。
        只输出一个 JSON object，不要 Markdown，不要解释。
        字段：
        meetsUserIntent: boolean
        meetsRevisionPlan: boolean
        meetsProjectPromise: boolean
        meetsAcceptedCreativeIntents: boolean
        continuityRisk: low | medium | high
        creativeFit: high | medium | low
        chapterPacing: good | slow | rushed | needs_revision
        decision: pass | revise_before_commit | rewrite | ask_user
        recommendedAction: commit | revise_before_commit | rewrite | ask_user
        problems: string[]
        suggestions: string[]
        evidence: string[]
        """;

        var user = $$"""
        runId: {{request.RunId}}
        chapterId: {{request.ChapterId}}
        userGoal:
        {{request.UserGoal}}

        chapterBrief:
        {{JsonSerializer.Serialize(request.ChapterBrief, JsonOptions)}}

        storyConstitution:
        {{JsonSerializer.Serialize(request.StoryConstitution, JsonOptions)}}

        acceptedCreativeIntents:
        {{JsonSerializer.Serialize(request.AcceptedCreativeIntents, JsonOptions)}}

        sourceRevisionPlans:
        {{JsonSerializer.Serialize(request.SourceRevisionPlans, JsonOptions)}}

        chapterContent:
        {{request.ChapterContent}}
        """;

        var text = await _completion.CompleteAsync(request.UserId, system, user, ct).ConfigureAwait(false);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "AgentReview 模型没有返回内容。",
            "AgentReview 模型没有返回 JSON object。");
        try
        {
            return JsonSerializer.Deserialize<AgentEditorialReviewDecision>(json, JsonOptions)
                   ?? throw new InvalidOperationException("AgentReview 模型返回了空 JSON。");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Agent editorial review model returned invalid JSON: {Text}", text);
            throw new InvalidOperationException("AgentReview 模型返回的 JSON 不可解析。", ex);
        }
    }

}
