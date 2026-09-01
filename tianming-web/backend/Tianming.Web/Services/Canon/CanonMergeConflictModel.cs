using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed record CanonMergeChangeEvidence(
    string Id,
    int ChapterNumber,
    string ChapterId,
    string ChangeType,
    string Subject,
    string ChangeJson);

public sealed record CanonMergeConflictReviewRequest(
    string UserId,
    string ProjectId,
    string GoalId,
    string GoalBaselineVersion,
    string CurrentCanonVersion,
    IReadOnlyList<CanonMergeChangeEvidence> CandidateChanges,
    IReadOnlyList<CanonMergeChangeEvidence> CurrentChanges);

public sealed record CanonMergeConflictReview(
    bool RequiresDecision,
    IReadOnlyList<string> ConflictingCandidateChangeIds,
    IReadOnlyList<string> ConflictingCurrentChangeIds,
    string Reason)
{
    public static CanonMergeConflictReview NoConflict() =>
        new(false, [], [], string.Empty);
}

public interface ICanonMergeConflictModelClient
{
    Task<CanonMergeConflictReview> ReviewAsync(
        CanonMergeConflictReviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DefaultCanonMergeConflictModelClient : ICanonMergeConflictModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IWritingModelCompletionService _completion;

    public DefaultCanonMergeConflictModelClient(IWritingModelCompletionService completion)
    {
        _completion = completion;
    }

    public async Task<CanonMergeConflictReview> ReviewAsync(
        CanonMergeConflictReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string system = """
            你是小说正史合并冲突审查内核。比较候选 CanonChange 与 Goal 提交后新增的正式 CanonChange，判断它们是否在人物身份、生死、能力边界、地点规则、组织关系、承诺、伏笔或因果上互斥，或候选是否建立在已失效前提上。
            章节不同不代表无冲突，名称不同也可能指同一实体。只依据输入证据，不补造事实；普通并存变化不算冲突。存在高影响歧义且无法安全合并时 requiresDecision=true。
            只输出 JSON object：requiresDecision、conflictingCandidateChangeIds[]、conflictingCurrentChangeIds[]、reason。ID 只能来自输入；requiresDecision=true 时两侧都必须至少给出一个 ID。
            """;
        var text = await _completion.CompleteAsync(
            request.UserId,
            system,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        var json = ModelJsonObjectExtractor.ExtractFirstObject(
            text,
            "正史合并冲突模型没有返回内容。",
            "正史合并冲突模型没有返回 JSON object。");
        return JsonSerializer.Deserialize<CanonMergeConflictReview>(json, JsonOptions)
            ?? throw new InvalidOperationException("正史合并冲突模型返回了空 JSON。");
    }
}
