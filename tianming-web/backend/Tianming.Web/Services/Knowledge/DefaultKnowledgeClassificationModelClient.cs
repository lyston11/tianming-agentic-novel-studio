using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class DefaultKnowledgeClassificationModelClient : IKnowledgeClassificationModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IWritingModelCompletionService _completion;
    private readonly ILogger<DefaultKnowledgeClassificationModelClient> _logger;

    public DefaultKnowledgeClassificationModelClient(
        IWritingModelCompletionService completion,
        ILogger<DefaultKnowledgeClassificationModelClient> logger)
    {
        _completion = completion;
        _logger = logger;
    }

    public async Task<KnowledgeClassificationDecision> ClassifyAsync(
        KnowledgeClassificationPrompt prompt,
        CancellationToken ct = default)
    {
        var system = """
        你是小说生产系统的知识分类器。
        你的任务不是写小说，也不是按关键词套规则，而是理解知识条目在当前项目生产结构中的用途。
        只输出一个 JSON object，不要 Markdown，不要解释。
        字段：
        role: 例如 Reference, HardFact, ItemRule, WorldRule, CharacterRule, StyleGuide, ReaderPromise
        scope: 例如 ProjectWide, VolumeWide, ChapterLocal, CharacterSpecific
        priority: 1-100
        constraintLevel: Reference, SoftConstraint, HardConstraint
        packagePolicy: RelevantOnly, DefaultEveryChapter, GateOnly, BlueprintAndGate
        targetEntities: string[]
        rule: 该知识进入生产结构时的规则化表达
        shouldEnterGate: boolean
        shouldEnterBlueprint: boolean
        shouldEnterFactSnapshot: boolean
        confidence: 0-1
        """;

        var user = $$"""
        当前项目：
        - projectId: {{prompt.ProjectId}}
        - title: {{prompt.ProjectTitle}}

        知识条目：
        - knowledgeId: {{prompt.KnowledgeId}}
        - title: {{prompt.KnowledgeTitle}}
        - entryType: {{prompt.KnowledgeEntryType}}
        - weight: {{prompt.KnowledgeWeight}}

        内容：
        {{prompt.KnowledgeContent}}
        """;

        var text = await _completion.CompleteAsync(prompt.UserId, system, user, ct).ConfigureAwait(false);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "知识分类模型没有返回内容。",
            "知识分类模型没有返回 JSON object。");
        try
        {
            var decision = JsonSerializer.Deserialize<KnowledgeClassificationDecision>(json, JsonOptions)
                ?? throw new InvalidOperationException("知识分类模型返回了空 JSON。");
            decision.RawJson = json;
            return decision;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Knowledge classification model returned invalid JSON: {Text}", text);
            throw new InvalidOperationException("知识分类模型返回的 JSON 不可解析。", ex);
        }
    }

}
