using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class DefaultCommitmentAssessmentModelClient : ICommitmentAssessmentModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IWritingModelCompletionService _completion;
    private readonly ILogger<DefaultCommitmentAssessmentModelClient> _logger;

    public DefaultCommitmentAssessmentModelClient(
        IWritingModelCompletionService completion,
        ILogger<DefaultCommitmentAssessmentModelClient> logger)
    {
        _completion = completion;
        _logger = logger;
    }

    public async Task<CommitmentAssessment> AssessAsync(
        string userId,
        CommitmentAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说创作导演的承诺判断模块。根据完整对话、已接受决定、项目状态和协作模式，判断用户是在探索、形成提案、明确授权、修订还是取消。
            不得根据“开始”“继续”“写一章”等单个词进行硬路由；必须解释这些表达在完整上下文中的语义作用。
            只输出一个 JSON object，不要 Markdown，不要额外解释。
            字段：
            state: Exploring | Proposed | Committed | Revising | Cancelled
            authorization: None | UnambiguousLanguage | ConfirmedContract | ExplicitAction
            confidence: 0-1
            requiresConfirmation: boolean
            rationale: string
            proposedContract: null 或完整 CreativeGoalContract
            如果目标基本形成但执行授权仍有歧义，state 必须为 Proposed 且 requiresConfirmation=true。
            """;
        var user = JsonSerializer.Serialize(new
        {
            request.ProjectId,
            request.CollaborationMode,
            request.Dialogue,
            request.AcceptedDecisionsJson,
            request.ProjectStateJson,
            request.ProposedContract
        }, JsonOptions);
        var text = await _completion.CompleteAsync(userId, system, user, cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "承诺判断模型没有返回内容。",
            "承诺判断模型没有返回 JSON object。");

        try
        {
            return JsonSerializer.Deserialize<CommitmentAssessment>(json, JsonOptions)
                ?? throw new InvalidOperationException("承诺判断模型返回了空 JSON。");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Commitment assessment model returned invalid JSON.");
            throw new InvalidOperationException("承诺判断模型返回的 JSON 不符合协议。", exception);
        }
    }
}
