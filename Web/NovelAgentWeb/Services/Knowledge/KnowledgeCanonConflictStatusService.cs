using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeCanonConflictStatusService : IKnowledgeCanonConflictStatusService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly StoryBibleService? _storyBible;

    public KnowledgeCanonConflictStatusService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public KnowledgeCanonConflictStatusService(StoryBibleService storyBible)
    {
        _storyBible = storyBible;
    }

    public async Task ApplyAsync(KnowledgeCanonConflictStatusRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.KnowledgeId))
        {
            return;
        }

        var storyBible = _storyBible ?? new StoryBibleService(
            new WebStoryBibleDocumentStore(_scopeFactory!, request.UserId, request.ProjectId));
        var document = await storyBible.LoadAsync(ct).ConfigureAwait(false);
        var entries = document.CanonLedger
            .Where(entry => IsKnowledgeSource(entry, request.KnowledgeId))
            .ToList();
        if (entries.Count == 0)
            return;

        foreach (var entry in entries)
        {
            if (request.HasConflict)
            {
                entry.Status = CanonLedgerEntryStatus.Conflict;
                entry.ConflictCheck = FormatConflictCheck(request);
            }
            else
            {
                if (entry.Status == CanonLedgerEntryStatus.Conflict)
                    entry.Status = CanonLedgerEntryStatus.Canon;
                entry.ConflictCheck = FormatClearCheck(request);
            }
        }

        await storyBible.AddLedgerEntryAsync(entries[0], confirmed: true, ct).ConfigureAwait(false);
        for (var i = 1; i < entries.Count; i++)
            await storyBible.AddLedgerEntryAsync(entries[i], confirmed: true, ct).ConfigureAwait(false);
        await EnqueueCanonIndexAsync(request.UserId, request.ProjectId, request.RunId, ct).ConfigureAwait(false);
    }

    public async Task ApplyResolutionAsync(
        KnowledgeCanonConflictResolutionStatusRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.KnowledgeId) ||
            string.IsNullOrWhiteSpace(request.ResolutionStatus))
        {
            return;
        }

        var storyBible = _storyBible ?? new StoryBibleService(
            new WebStoryBibleDocumentStore(_scopeFactory!, request.UserId, request.ProjectId));
        var document = await storyBible.LoadAsync(ct).ConfigureAwait(false);
        var candidateEntries = document.CanonLedger
            .Where(entry => IsKnowledgeSource(entry, request.KnowledgeId))
            .ToList();

        var conflictingKnowledgeIds = request.ConflictingKnowledgeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => !string.Equals(id, request.KnowledgeId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var conflictingEntries = document.CanonLedger
            .Where(entry => conflictingKnowledgeIds.Any(id => IsKnowledgeSource(entry, id)))
            .Where(entry => candidateEntries.All(candidate =>
                !string.Equals(candidate.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (candidateEntries.Count == 0 && conflictingEntries.Count == 0)
            return;

        foreach (var entry in candidateEntries)
        {
            ApplyResolution(entry, request);
            await storyBible.AddLedgerEntryAsync(entry, confirmed: true, ct).ConfigureAwait(false);
        }

        foreach (var entry in conflictingEntries)
        {
            ApplyConflictingResolution(entry, request);
            await storyBible.AddLedgerEntryAsync(entry, confirmed: true, ct).ConfigureAwait(false);
        }

        await EnqueueCanonIndexAsync(request.UserId, request.ProjectId, request.RunId, ct).ConfigureAwait(false);
    }

    private static bool IsKnowledgeSource(CanonLedgerEntry entry, string knowledgeId)
    {
        if (string.IsNullOrWhiteSpace(entry.Rationale))
            return false;

        var marker = $"KnowledgeId={knowledgeId.Trim()}";
        return entry.Rationale.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatConflictCheck(KnowledgeCanonConflictStatusRequest request)
    {
        var conflicts = request.ConflictingKnowledgeIds.Count == 0
            ? string.Empty
            : $" ConflictingKnowledgeIds={string.Join(",", request.ConflictingKnowledgeIds)};";
        return $"conflict_open; ReportId={request.ReportId}; Severity={request.Severity}; Type={request.ConflictType};{conflicts} Explanation={request.Explanation}";
    }

    private static string FormatClearCheck(KnowledgeCanonConflictStatusRequest request) =>
        $"clear; Severity={request.Severity}; Type={request.ConflictType}; RunId={request.RunId ?? string.Empty}";

    private static void ApplyResolution(
        CanonLedgerEntry entry,
        KnowledgeCanonConflictResolutionStatusRequest request)
    {
        var status = request.ResolutionStatus.Trim().ToLowerInvariant();
        entry.ConflictCheck = FormatResolutionCheck(request);
        if (status == "rejected")
        {
            entry.Status = CanonLedgerEntryStatus.Rejected;
            return;
        }

        if (status == "superseded")
        {
            entry.Status = CanonLedgerEntryStatus.Deprecated;
            return;
        }

        entry.Status = CanonLedgerEntryStatus.Canon;
        if (!string.IsNullOrWhiteSpace(request.ResolutionNote))
            entry.Content = $"冲突处理决定：{request.ResolutionNote.Trim()}";
    }

    private static void ApplyConflictingResolution(
        CanonLedgerEntry entry,
        KnowledgeCanonConflictResolutionStatusRequest request)
    {
        var status = request.ResolutionStatus.Trim().ToLowerInvariant();
        if (status == "superseded")
        {
            entry.Status = CanonLedgerEntryStatus.Deprecated;
            entry.ConflictCheck = FormatConflictingResolutionCheck(request, "superseded_by_resolution");
            return;
        }

        entry.Status = CanonLedgerEntryStatus.Canon;
        entry.ConflictCheck = FormatConflictingResolutionCheck(request, "retained_by_resolution");
    }

    private static string FormatResolutionCheck(KnowledgeCanonConflictResolutionStatusRequest request)
    {
        var conflicts = request.ConflictingKnowledgeIds.Count == 0
            ? string.Empty
            : $" ConflictingKnowledgeIds={string.Join(",", request.ConflictingKnowledgeIds)};";
        return $"resolved; ReportId={request.ReportId}; ResolutionStatus={request.ResolutionStatus}; Severity={request.Severity}; Type={request.ConflictType};{conflicts} Note={request.ResolutionNote}; RunId={request.RunId ?? string.Empty}";
    }

    private static string FormatConflictingResolutionCheck(
        KnowledgeCanonConflictResolutionStatusRequest request,
        string marker) =>
        $"{marker}; ReportId={request.ReportId}; ResolutionStatus={request.ResolutionStatus}; CandidateKnowledgeId={request.KnowledgeId}; Severity={request.Severity}; Type={request.ConflictType}; Note={request.ResolutionNote}; RunId={request.RunId ?? string.Empty}";

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
