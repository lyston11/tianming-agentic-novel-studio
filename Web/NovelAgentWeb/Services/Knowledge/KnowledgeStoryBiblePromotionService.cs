using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeStoryBiblePromotionService : IKnowledgeStoryBiblePromotionService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly StoryBibleService? _storyBible;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public KnowledgeStoryBiblePromotionService(
        IServiceScopeFactory scopeFactory,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _scopeFactory = scopeFactory;
        _outputArtifacts = outputArtifacts;
    }

    public KnowledgeStoryBiblePromotionService(
        StoryBibleService storyBible,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _storyBible = storyBible;
        _outputArtifacts = outputArtifacts;
    }

    public async Task PromoteAsync(KnowledgeStoryBiblePromotionRequest request, CancellationToken ct = default)
    {
        if (!ShouldPromote(request))
            return;

        var storyBible = _storyBible ?? new StoryBibleService(
            new WebStoryBibleDocumentStore(_scopeFactory!, request.UserId, request.ProjectId));
        var entry = BuildCanonEntry(request);
        await storyBible.AddLedgerEntryAsync(entry, confirmed: true, ct).ConfigureAwait(false);
        if (_outputArtifacts != null)
        {
            await _outputArtifacts.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: FirstNonEmpty(request.RunId, $"story-bible-canon:{request.ClassificationId}"),
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: null,
                        PackageId: null,
                        ToolName: "KnowledgeStoryBiblePromotion",
                        Stage: "story_bible_canon_promotion",
                        Status: "completed",
                        ArtifactType: "story_bible_canon_knowledge",
                        ArtifactId: entry.Id,
                        OutputKind: "ProcessArtifact",
                        Summary: $"知识规则已进入 Story Bible：{entry.Title}。",
                        UserVisibleWhere: new[] { "创作工作流", "知识库" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: false,
                        SourceEventType: "story_bible_canon_promoted",
                        SourceEventId: entry.Id,
                        Data: new
                        {
                            entry.Id,
                            entry.Type,
                            entry.Title,
                            entry.Content,
                            entry.ImpactScope,
                            request.KnowledgeId,
                            request.ClassificationId,
                            request.Role,
                            request.ConstraintLevel,
                            request.PackagePolicy,
                            request.ShouldEnterGate,
                            request.ShouldEnterBlueprint,
                            request.ShouldEnterFactSnapshot,
                            request.Confidence
                        }),
                    ct)
                .ConfigureAwait(false);
        }
        await EnqueueCanonIndexAsync(request.UserId, request.ProjectId, request.RunId, ct).ConfigureAwait(false);
    }

    private static bool ShouldPromote(KnowledgeStoryBiblePromotionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.KnowledgeId) ||
            string.IsNullOrWhiteSpace(request.ClassificationId) ||
            string.IsNullOrWhiteSpace(request.Rule))
        {
            return false;
        }

        return request.ShouldEnterGate ||
               request.ShouldEnterBlueprint ||
               request.ShouldEnterFactSnapshot ||
               string.Equals(request.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase);
    }

    private static CanonLedgerEntry BuildCanonEntry(KnowledgeStoryBiblePromotionRequest request)
    {
        var targetEntities = request.TargetEntities
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetEntityText = targetEntities.Length == 0
            ? string.Empty
            : $"TargetEntities={string.Join(",", targetEntities)}; ";

        return new CanonLedgerEntry
        {
            Id = $"knowledge-classification:{request.ClassificationId.Trim()}",
            Type = ResolveCanonType(request),
            Status = CanonLedgerEntryStatus.Canon,
            Title = $"知识规则：{FirstNonEmpty(request.KnowledgeTitle, request.KnowledgeId)}",
            Content = request.Rule.Trim(),
            Rationale =
                $"由知识库 LLM 分类提升。KnowledgeId={request.KnowledgeId}; ClassificationId={request.ClassificationId}; " +
                $"Model={request.Model}; Role={request.Role}; ConstraintLevel={request.ConstraintLevel}; PackagePolicy={request.PackagePolicy}; " +
                $"{targetEntityText}ShouldEnterGate={request.ShouldEnterGate}; ShouldEnterBlueprint={request.ShouldEnterBlueprint}; " +
                $"ShouldEnterFactSnapshot={request.ShouldEnterFactSnapshot}; Confidence={request.Confidence:0.###}",
            ImpactScope = FirstNonEmpty(request.Scope, "ProjectWide"),
            ConflictCheck = "pending",
            SourceRunId = request.RunId?.Trim() ?? string.Empty
        };
    }

    private static CanonLedgerEntryType ResolveCanonType(KnowledgeStoryBiblePromotionRequest request)
    {
        var role = $"{request.Role} {request.KnowledgeEntryType}".Trim();
        if (role.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("Constraint", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("Boundary", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("道具", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("边界", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.Constraint;
        if (role.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("角色", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.CharacterRule;
        if (role.Contains("Faction", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("组织", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("势力", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.FactionRule;
        if (role.Contains("Location", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("地点", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.LocationRule;
        if (role.Contains("Plot", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("Promise", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("剧情", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.PlotRule;
        if (role.Contains("Theme", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("主题", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.Theme;
        if (role.Contains("Foreshadow", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("伏笔", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.Foreshadowing;
        if (role.Contains("World", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("世界", StringComparison.OrdinalIgnoreCase))
            return CanonLedgerEntryType.WorldRule;

        return CanonLedgerEntryType.Constraint;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private async Task EnqueueCanonIndexAsync(
        string userId,
        string projectId,
        string? runId,
        CancellationToken ct)
    {
        if (_scopeFactory == null)
            return;

        using var scope = _scopeFactory.CreateScope();
        var truthStore = scope.ServiceProvider.GetService<IProductionTruthStore>();
        if (truthStore == null)
            return;

        await truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: userId,
                    ProjectId: projectId,
                    RuntimeRunId: string.IsNullOrWhiteSpace(runId) ? null : runId.Trim(),
                    EventType: "index_story_bible_canon",
                    AggregateType: "story_bible",
                    AggregateId: projectId,
                    PayloadJson: "{}"),
                ct)
            .ConfigureAwait(false);
    }
}
