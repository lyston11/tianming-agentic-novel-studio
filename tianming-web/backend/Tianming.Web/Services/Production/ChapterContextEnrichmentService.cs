using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ChapterContextEnrichmentService : IChapterContextEnrichmentService
{
    private readonly NovelAgentDbContext _db;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IProjectKnowledgeBindingQueryService _knowledgeBindings;
    private readonly ILogger<ChapterContextEnrichmentService> _logger;
    private readonly SemanticSearchService? _semanticSearch;

    public ChapterContextEnrichmentService(
        NovelAgentDbContext db,
        IKnowledgeService knowledgeService,
        IProjectKnowledgeBindingQueryService knowledgeBindings,
        ILogger<ChapterContextEnrichmentService> logger,
        SemanticSearchService? semanticSearch = null)
    {
        _db = db;
        _knowledgeService = knowledgeService;
        _knowledgeBindings = knowledgeBindings;
        _logger = logger;
        _semanticSearch = semanticSearch;
    }

    public async Task<ChapterContextEnrichmentResult> BuildAsync(
        ChapterContextEnrichmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new ChapterContextEnrichmentResult();
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
            return result;

        result.KnowledgeBindings = (await _knowledgeBindings
                .GetBoundKnowledgeAsync(request.UserId, request.ProjectId, cancellationToken)
                .ConfigureAwait(false))
            .ToList();

        var hardFacts = new List<string>();
        hardFacts.AddRange(result.KnowledgeBindings
            .Where(binding => string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
            .Select(ProjectKnowledgeBindingQueryService.FormatBoundKnowledgeHardFact));

        hardFacts.AddRange(await SearchKnowledgeHardFactsAsync(request, cancellationToken).ConfigureAwait(false));
        hardFacts.AddRange(await SearchStoryBibleCanonHardFactsAsync(request, cancellationToken).ConfigureAwait(false));
        hardFacts.AddRange(await _knowledgeBindings
            .GetProjectHardFactLinesAsync(request.UserId, request.ProjectId, cancellationToken)
            .ConfigureAwait(false));

        var snapshots = await LoadFactSnapshotLinesAsync(request, cancellationToken).ConfigureAwait(false);
        hardFacts.AddRange(snapshots.HardFacts);

        result.HardFacts = hardFacts
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        result.PreviousSummaries = snapshots.PreviousSummaries;
        result.CharacterStates = snapshots.CharacterStates;
        result.ActiveConflicts = snapshots.ActiveConflicts;
        result.SourceWarnings = snapshots.SourceWarnings;

        return result;
    }

    private async Task<IReadOnlyList<string>> SearchKnowledgeHardFactsAsync(
        ChapterContextEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Array.Empty<string>();

        try
        {
            var hits = await _knowledgeService.SearchKnowledgeAsync(new SearchKnowledgeRequest
                {
                    ProjectId = request.ProjectId,
                    Query = request.Query,
                    TopK = 8
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var hit in hits)
            {
                await _knowledgeService.IncrementUsageAsync(
                        hit.Id,
                        request.ProjectId,
                        request.SessionId,
                        request.RunId,
                        $"chapter-context:{FirstNonEmpty(request.RunId, request.SessionId)}:{hit.Id}",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return hits
                .Where(hit => string.Equals(hit.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase))
                .Select(hit => FormatKnowledgeHardFact(hit.Title, hit.Content))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Knowledge search unavailable while enriching context package for project {ProjectId}", request.ProjectId);
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> SearchStoryBibleCanonHardFactsAsync(
        ChapterContextEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        if (_semanticSearch == null || string.IsNullOrWhiteSpace(request.Query))
            return Array.Empty<string>();

        try
        {
            var hits = await _semanticSearch
                .SearchStoryBibleCanonAsync(
                    request.UserId,
                    request.ProjectId,
                    request.Query,
                    topK: 8,
                    cancellationToken)
                .ConfigureAwait(false);

            return hits
                .Select(hit => FormatCanonHardFact(hit.Content))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "StoryBible canon search unavailable while enriching context package for project {ProjectId}", request.ProjectId);
            return Array.Empty<string>();
        }
    }

    private async Task<FactSnapshotLines> LoadFactSnapshotLinesAsync(
        ChapterContextEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        var snapshots = await _db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.UserId == request.UserId && snapshot.ProjectId == request.ProjectId)
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .Take(3)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hardFacts = new List<string>();
        var summaries = new List<string>();
        var characterStates = new List<string>();
        var activeConflicts = new List<string>();
        var sourceWarnings = new List<string>();

        foreach (var snapshot in snapshots)
        {
            AddSnapshotLines(snapshot, hardFacts, summaries, characterStates, activeConflicts);
            sourceWarnings.Add($"FactSnapshot 来源：{snapshot.Id} / {snapshot.Source} / v{snapshot.VersionNumber}");
        }

        return new FactSnapshotLines(
            hardFacts.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList(),
            summaries.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList(),
            characterStates.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            activeConflicts.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            sourceWarnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList());
    }

    private static void AddSnapshotLines(
        ProjectFactSnapshot snapshot,
        List<string> hardFacts,
        List<string> summaries,
        List<string> characterStates,
        List<string> activeConflicts)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshot.SnapshotJson);
            var root = document.RootElement;
            var chapterTitle = GetJsonString(root, "chapterTitle");
            var chapterId = GetJsonString(root, "chapterId");
            if (!string.IsNullOrWhiteSpace(chapterTitle) || !string.IsNullOrWhiteSpace(chapterId))
                summaries.Add($"{FirstNonEmpty(chapterId, snapshot.ChapterId)}: {chapterTitle}");

            AddScalarLine(root, "protagonistName", hardFacts, "上一章主角");
            AddScalarLine(root, "protagonistIdentity", hardFacts, "上一章主角身份");
            AddScalarLine(root, "protagonistStatus", characterStates, "上一章主角状态");
            AddScalarLine(root, "currentLocation", hardFacts, "上一章当前位置");
            AddScalarLine(root, "systemState", hardFacts, "上一章系统状态");
            AddScalarLine(root, "equipmentState", hardFacts, "上一章装备状态");
            AddArrayLines(root, "keyEvents", hardFacts, "上一章关键事件");
            AddScalarLine(root, "endingState", hardFacts, "上一章结尾状态");
            AddScalarLine(root, "chapterEndingState", hardFacts, "上一章结尾状态");
            AddArrayLines(root, "worldRules", hardFacts, "上一章世界规则");
            AddArrayLines(root, "hardContinuityFacts", hardFacts, "上一章硬事实");
            AddArrayLines(root, "nextChapterMustCarry", hardFacts, "下一章必须承接");
            AddArrayLines(root, "characterStates", characterStates, "上一章角色状态");
            AddArrayLines(root, "activeConflicts", activeConflicts, "上一章冲突");
            AddArrayLines(root, "activeForeshadowing", hardFacts, "上一章伏笔");
            AddKnowledgeConstraintEvidenceLines(root, hardFacts);
        }
        catch (JsonException)
        {
            hardFacts.Add($"上一章事实快照不可解析：{snapshot.Id}");
        }
    }

    private static void AddScalarLine(JsonElement root, string propertyName, ICollection<string> target, string prefix)
    {
        var value = GetJsonString(root, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
            target.Add($"{prefix}：{value}");
    }

    private static void AddArrayLines(JsonElement root, string propertyName, ICollection<string> target, string prefix)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in property.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                target.Add($"{prefix}：{value}");
        }
    }

    private static void AddKnowledgeConstraintEvidenceLines(JsonElement root, ICollection<string> hardFacts)
    {
        if (!root.TryGetProperty("knowledgeConstraintEvidence", out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var title = FirstNonEmpty(GetJsonString(item, "title"), GetJsonString(item, "knowledgeId"));
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var entryType = GetJsonString(item, "entryType");
            var constraintLevel = GetJsonString(item, "constraintLevel");
            var packagePolicy = GetJsonString(item, "packagePolicy");
            var evidenceStatus = FormatEvidenceStatus(GetJsonString(item, "evidenceStatus"));
            var gateStatus = GetJsonString(item, "gateStatus");
            var metadata = string.Join("/", new[] { entryType, constraintLevel, packagePolicy }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var suffix = string.IsNullOrWhiteSpace(metadata) ? string.Empty : $"（{metadata}）";
            var gate = string.IsNullOrWhiteSpace(gateStatus) ? string.Empty : $"，Gate={gateStatus}";
            hardFacts.Add($"上一章知识约束证据：{title}{suffix}：{evidenceStatus}{gate}");

            var classificationRule = GetJsonString(item, "classificationRule");
            if (GetJsonBoolean(item, "shouldEnterFactSnapshot") && !string.IsNullOrWhiteSpace(classificationRule))
                hardFacts.Add($"上一章知识分类规则：{title}：{classificationRule}");
        }
    }

    private static string GetJsonString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetJsonBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static string FormatEvidenceStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "satisfied" => "已满足",
            "violated" => "已违反",
            "unknown" => "未知",
            _ => string.IsNullOrWhiteSpace(status) ? "未知" : status.Trim()
        };
    }

    private static string FormatKnowledgeHardFact(string title, string content)
    {
        title = title.Trim();
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return $"知识库硬事实：{content}";
        if (string.IsNullOrWhiteSpace(content))
            return $"知识库硬事实：{title}";
        return $"知识库硬事实：{title}：{content}";
    }

    private static string FormatCanonHardFact(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        return content.Contains("Story Bible Canon", StringComparison.OrdinalIgnoreCase)
            ? content
            : $"Story Bible Canon：{content}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private sealed record FactSnapshotLines(
        List<string> HardFacts,
        List<string> PreviousSummaries,
        List<string> CharacterStates,
        List<string> ActiveConflicts,
        List<string> SourceWarnings);
}
