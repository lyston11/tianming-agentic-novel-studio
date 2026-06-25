using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class DefaultKnowledgeConflictModelClient : IKnowledgeConflictModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IWritingModelCompletionService _completion;
    private readonly ILogger<DefaultKnowledgeConflictModelClient> _logger;

    public DefaultKnowledgeConflictModelClient(
        IWritingModelCompletionService completion,
        ILogger<DefaultKnowledgeConflictModelClient> logger)
    {
        _completion = completion;
        _logger = logger;
    }

    public async Task<KnowledgeConflictDecision> DetectAsync(
        KnowledgeConflictPrompt prompt,
        CancellationToken ct = default)
    {
        var system = """
        你是小说生产系统的知识冲突审阅器。
        你的任务是判断候选知识与当前项目已绑定知识是否存在设定冲突、硬约束冲突或生产入口冲突。
        不要按关键词机械判断，要基于语义理解。
        只输出一个 JSON object，不要 Markdown，不要解释。
        字段：
        hasConflict: boolean
        conflictType: None, HardConstraintContradiction, SoftGuidelineTension, ScopeMismatch, FactSnapshotContradiction, StoryBibleContradiction
        severity: None, Soft, Medium, Hard
        impactScope: None, ChapterLocal, VolumeWide, ProjectWide, BookWide
        conflictingKnowledgeIds: string[]
        explanation: 面向 Agent 和用户解释冲突
        recommendedAction: 建议 Agent 下一步怎么处理
        requiresUserDecision: boolean
        """;

        var user = JsonSerializer.Serialize(new
        {
            project = new
            {
                prompt.ProjectId,
                prompt.ProjectTitle
            },
            candidate = prompt.Candidate,
            existingKnowledge = prompt.ExistingKnowledge
        }, JsonOptions);

        var text = await _completion.CompleteAsync(prompt.UserId, system, user, ct).ConfigureAwait(false);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "知识冲突模型没有返回内容。",
            "知识冲突模型没有返回 JSON object。");
        try
        {
            var decision = JsonSerializer.Deserialize<KnowledgeConflictDecision>(json, JsonOptions)
                ?? throw new InvalidOperationException("知识冲突模型返回了空 JSON。");
            decision.RawJson = json;
            return decision;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Knowledge conflict model returned invalid JSON: {Text}", text);
            throw new InvalidOperationException("知识冲突模型返回的 JSON 不可解析。", ex);
        }
    }

}
