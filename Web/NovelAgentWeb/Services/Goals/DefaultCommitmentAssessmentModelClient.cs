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
            proposedContract.executionStrategy 必须根据完整对话和项目状态选择 full_auto 或 interactive_batch；不得让用户手工先选模式。
            full_auto 表示用户已授权后可跨批持续推进；interactive_batch 表示每批合并后暂停等待用户。
            proposedContract.targetChapterRangeJson 表示整书预期章节范围，不是单批范围。
            proposedContract.bookPlanJson 必须是 JSON object，至少包含 batchSize，并可包含 targetWordCountMin、targetWordCountMax、stages、coreConflictClosureCriteria、endingCriteria。batchSize 必须在 1-20。
            如果目标基本形成但执行授权仍有歧义，state 必须为 Proposed 且 requiresConfirmation=true。
            ProjectStateJson.Knowledge 来自唯一只读工具 Knowledge.Query，是当前知识库目录和本轮相关条目的权威快照。
            用户询问知识库有什么时，应在 rationale 中概括 Knowledge.directories 和 Knowledge.items；不得编造快照中不存在的条目。
            讨论创作目标时，应主动利用 Knowledge.items 中的相关事实，并在资料不足时明确指出缺少的知识类型。
            ProjectStateJson.PendingIntents 是等待用户确认的持久提案。source=legacy_recovery 时，只能基于正式内容、知识库和记忆重新形成 CreativeGoalContract；不得恢复或重放旧 MissionPlan、旧 RuntimeRun 或 pending tool。章节范围、验收条件或授权不明确时必须先提问，并保持 requiresConfirmation=true。
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
