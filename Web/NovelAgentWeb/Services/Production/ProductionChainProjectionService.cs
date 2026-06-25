using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionChainProjectionService : IProductionChainProjectionService
{
    public IReadOnlyList<WorkflowProductionChain> BuildWorkflowChains(
        IEnumerable<WorkflowProductionEventSummary> productionEvents) =>
        BuildWorkflowChainsCore(productionEvents);

    public List<NovelProductionChainState> BuildNovelChains(
        IReadOnlyList<NovelProductionEventState> events,
        IReadOnlyList<NovelProductionPackageState> packages,
        IReadOnlyList<NovelProductionRevisionPlanState> revisionPlans,
        IReadOnlyList<NovelProductionOutboxState> outboxEvents,
        IReadOnlyList<NovelProductionRebuildLinkState> rebuildLinks)
    {
        if (events.Count == 0 && revisionPlans.Count == 0)
            return new List<NovelProductionChainState>();

        var packageById = packages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var outboxById = outboxEvents
            .GroupBy(outbox => outbox.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var snapshots = events
            .Select(evt => ToNovelSnapshot(evt, packageById, revisionPlans, outboxById, rebuildLinks))
            .ToList();
        snapshots.AddRange(revisionPlans
            .Where(plan => snapshots.Count == 0 || ShouldAttachRevisionPlanSnapshot(plan, snapshots))
            .Select(ToNovelRevisionPlanSnapshot));

        return BuildProjections(snapshots, take: 20)
            .Select(ToNovelChainState)
            .ToList();
    }

    public static IReadOnlyList<WorkflowProductionChain> BuildWorkflowChainsCore(
        IEnumerable<WorkflowProductionEventSummary> productionEvents)
    {
        var snapshots = productionEvents
            .Select(ToWorkflowSnapshot)
            .ToList();

        return BuildProjections(snapshots, take: 100)
            .Select(ToWorkflowChain)
            .ToList();
    }

    private static List<ProductionChainProjection> BuildProjections(
        IReadOnlyList<ProductionChainEventSnapshot> events,
        int take)
    {
        var enrichedEvents = EnrichRevisionPlanIds(events);

        return enrichedEvents
            .GroupBy(BuildProductionChainGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildProjection(group.ToList()))
            .Where(chain => chain.Steps.Count > 0)
            .OrderByDescending(chain => chain.UpdatedAt)
            .Take(take)
            .ToList();
    }

    private static List<ProductionChainEventSnapshot> EnrichRevisionPlanIds(
        IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        var revisionIdsByPackageId = events
            .Where(evt => !string.IsNullOrWhiteSpace(evt.PackageId) && evt.RevisionPlanIds.Count > 0)
            .GroupBy(evt => evt.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(evt => evt.RevisionPlanIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var revisionIdsByChapterRun = events
            .Where(evt => !string.IsNullOrWhiteSpace(evt.ChapterId) &&
                          !string.IsNullOrWhiteSpace(evt.RuntimeRunId) &&
                          evt.RevisionPlanIds.Count > 0)
            .GroupBy(evt => $"{evt.ChapterId}:{evt.RuntimeRunId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(evt => evt.RevisionPlanIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        var revisionPlanLinksByChapterCandidate = events
            .Where(evt => evt.RevisionPlanIds.Count > 0)
            .SelectMany(evt => GetChapterCandidateKeys(evt)
                .SelectMany(chapterCandidate => evt.RevisionPlanIds.Select(revisionPlanId =>
                    new RevisionPlanChapterLink(chapterCandidate, revisionPlanId, evt.CreatedAt))))
            .Where(item => !string.IsNullOrWhiteSpace(item.ChapterCandidate) &&
                           !string.IsNullOrWhiteSpace(item.RevisionPlanId))
            .GroupBy(item => item.ChapterCandidate, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(item => item.RevisionPlanId, StringComparer.OrdinalIgnoreCase)
                    .Select(planGroup => planGroup.OrderBy(item => item.CreatedAt).First())
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var revisionIdsByInvalidatedPackageId = events
            .Where(evt => evt.RevisionPlanIds.Count > 0)
            .SelectMany(evt => GetJsonStringArray(evt.DataJson, "invalidatedPackageIds", "invalidatedPackageIdsJson")
                .Select(packageId => new
                {
                    PackageId = packageId,
                    RevisionPlanIds = evt.RevisionPlanIds
                }))
            .Where(item => !string.IsNullOrWhiteSpace(item.PackageId))
            .GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(item => item.RevisionPlanIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var revisionIdsByRebuiltPackageId = events
            .SelectMany(evt => evt.RebuildLinks.SelectMany(link =>
            {
                if (string.IsNullOrWhiteSpace(link.NewPackageId) ||
                    string.IsNullOrWhiteSpace(link.OldPackageId) ||
                    !revisionIdsByInvalidatedPackageId.TryGetValue(link.OldPackageId, out var revisionPlanIds))
                {
                    return Enumerable.Empty<(string PackageId, IReadOnlyList<string> RevisionPlanIds)>();
                }

                return new[] { (PackageId: link.NewPackageId, RevisionPlanIds: (IReadOnlyList<string>)revisionPlanIds) };
            }))
            .GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(item => item.RevisionPlanIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var chapterIdByPackageId = events
            .SelectMany(evt =>
            {
                var packageLinks = new List<(string PackageId, string ChapterId)>();
                if (!string.IsNullOrWhiteSpace(evt.PackageId) && !string.IsNullOrWhiteSpace(evt.ChapterId))
                    packageLinks.Add((evt.PackageId, evt.ChapterId));

                foreach (var link in evt.RebuildLinks)
                {
                    var chapterId = FirstNonEmpty(link.ChapterId, evt.ChapterId);
                    if (string.IsNullOrWhiteSpace(chapterId))
                        continue;

                    if (!string.IsNullOrWhiteSpace(link.OldPackageId))
                        packageLinks.Add((link.OldPackageId, chapterId));
                    if (!string.IsNullOrWhiteSpace(link.NewPackageId))
                        packageLinks.Add((link.NewPackageId, chapterId));
                }

                return packageLinks;
            })
            .GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                PackageId = group.Key,
                ChapterIds = group
                    .Select(item => item.ChapterId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(item => item.ChapterIds.Count == 1)
            .ToDictionary(
                item => item.PackageId,
                item => item.ChapterIds[0],
                StringComparer.OrdinalIgnoreCase);

        var chapterIdByCandidate = events
            .SelectMany(evt =>
            {
                var chapterId = FirstNonEmpty(
                    evt.ChapterId,
                    evt.RebuildLinks.FirstOrDefault()?.ChapterId,
                    GetPrimaryChapterIdFromEvent(evt),
                    !string.IsNullOrWhiteSpace(evt.PackageId) &&
                    chapterIdByPackageId.TryGetValue(evt.PackageId, out var packageChapterId)
                        ? packageChapterId
                        : string.Empty);
                if (string.IsNullOrWhiteSpace(chapterId))
                    return Array.Empty<(string Candidate, string ChapterId, DateTime CreatedAt)>();

                return BuildChapterIdCandidates(chapterId)
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                    .Select(candidate => (Candidate: candidate, ChapterId: chapterId, evt.CreatedAt));
            })
            .GroupBy(item => item.Candidate, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ChoosePreferredChapterId(group
                    .Select(item => (item.ChapterId, item.CreatedAt))
                    .ToList()),
                StringComparer.OrdinalIgnoreCase);

        var preferredChapterIdByRevisionCandidate = events
            .Where(evt => evt.RevisionPlanIds.Count > 0)
            .SelectMany(evt =>
            {
                var chapterId = FirstNonEmpty(
                    evt.ChapterId,
                    evt.RebuildLinks.FirstOrDefault()?.ChapterId,
                    GetPrimaryChapterIdFromEvent(evt),
                    !string.IsNullOrWhiteSpace(evt.PackageId) &&
                    chapterIdByPackageId.TryGetValue(evt.PackageId, out var packageChapterId)
                        ? packageChapterId
                        : string.Empty);
                if (string.IsNullOrWhiteSpace(chapterId))
                    return Array.Empty<(string RevisionPlanId, string Candidate, string ChapterId, DateTime CreatedAt)>();

                return evt.RevisionPlanIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .SelectMany(revisionPlanId => BuildChapterIdCandidates(chapterId)
                        .Select(candidate => (RevisionPlanId: revisionPlanId, Candidate: candidate, ChapterId: chapterId, evt.CreatedAt)));
            })
            .GroupBy(item => $"{item.RevisionPlanId}:{item.Candidate}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ChoosePreferredChapterId(group
                    .Select(item => (item.ChapterId, item.CreatedAt))
                    .ToList()),
                StringComparer.OrdinalIgnoreCase);

        var chapterIdByRevisionPlanId = events
            .SelectMany(evt =>
            {
                var chapterId = FirstNonEmpty(
                    evt.ChapterId,
                    evt.RebuildLinks.FirstOrDefault()?.ChapterId,
                    GetPrimaryChapterIdFromEvent(evt),
                    !string.IsNullOrWhiteSpace(evt.PackageId) &&
                    chapterIdByPackageId.TryGetValue(evt.PackageId, out var packageChapterId)
                        ? packageChapterId
                        : string.Empty);
                return string.IsNullOrWhiteSpace(chapterId)
                    ? Array.Empty<(string RevisionPlanId, string ChapterId)>()
                    : evt.RevisionPlanIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => (RevisionPlanId: id, ChapterId: chapterId));
            })
            .GroupBy(item => item.RevisionPlanId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                RevisionPlanId = group.Key,
                ChapterIds = group
                    .Select(item => item.ChapterId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(item => item.ChapterIds.Count == 1)
            .ToDictionary(
                item => item.RevisionPlanId,
                item => item.ChapterIds[0],
                StringComparer.OrdinalIgnoreCase);

        return events
            .Select(evt =>
            {
                var ids = new HashSet<string>(evt.RevisionPlanIds, StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(evt.PackageId) &&
                    revisionIdsByPackageId.TryGetValue(evt.PackageId, out var packageRevisionIds))
                {
                    foreach (var id in packageRevisionIds)
                        ids.Add(id);
                }

                if (!string.IsNullOrWhiteSpace(evt.PackageId) &&
                    revisionIdsByInvalidatedPackageId.TryGetValue(evt.PackageId, out var invalidatedRevisionIds))
                {
                    foreach (var id in invalidatedRevisionIds)
                        ids.Add(id);
                }

                if (!string.IsNullOrWhiteSpace(evt.PackageId) &&
                    revisionIdsByRebuiltPackageId.TryGetValue(evt.PackageId, out var rebuiltRevisionIds))
                {
                    foreach (var id in rebuiltRevisionIds)
                        ids.Add(id);
                }

                foreach (var link in evt.RebuildLinks)
                {
                    if (!string.IsNullOrWhiteSpace(link.OldPackageId) &&
                        revisionIdsByInvalidatedPackageId.TryGetValue(link.OldPackageId, out var linkInvalidatedRevisionIds))
                    {
                        foreach (var id in linkInvalidatedRevisionIds)
                            ids.Add(id);
                    }

                    if (!string.IsNullOrWhiteSpace(link.NewPackageId) &&
                        revisionIdsByRebuiltPackageId.TryGetValue(link.NewPackageId, out var linkRebuiltRevisionIds))
                    {
                        foreach (var id in linkRebuiltRevisionIds)
                            ids.Add(id);
                    }
                }

                var chapterRunKey = !string.IsNullOrWhiteSpace(evt.ChapterId) &&
                                    !string.IsNullOrWhiteSpace(evt.RuntimeRunId)
                    ? $"{evt.ChapterId}:{evt.RuntimeRunId}"
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(chapterRunKey) &&
                    revisionIdsByChapterRun.TryGetValue(chapterRunKey, out var runRevisionIds))
                {
                    foreach (var id in runRevisionIds)
                        ids.Add(id);
                }

                var chapterId = FirstNonEmpty(evt.ChapterId, GetPrimaryChapterIdFromEvent(evt));
                if (string.IsNullOrWhiteSpace(chapterId) &&
                    !string.IsNullOrWhiteSpace(evt.PackageId) &&
                    chapterIdByPackageId.TryGetValue(evt.PackageId, out var packageChapterId))
                {
                    chapterId = packageChapterId;
                }

                if (string.IsNullOrWhiteSpace(chapterId) || IsLogicalChapterId(chapterId))
                {
                    var candidateMatchedChapterIds = GetChapterCandidateKeys(evt)
                        .Where(candidate => chapterIdByCandidate.ContainsKey(candidate))
                        .Select(candidate => chapterIdByCandidate[candidate])
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (candidateMatchedChapterIds.Count == 1)
                        chapterId = candidateMatchedChapterIds[0];
                }

                if (ids.Count == 0)
                {
                    var chapterMatchedRevisionIds = GetChapterCandidateKeys(evt)
                        .SelectMany(candidate =>
                            revisionPlanLinksByChapterCandidate.TryGetValue(candidate, out var links)
                                ? links
                                : Enumerable.Empty<RevisionPlanChapterLink>())
                        .Where(link => link.CreatedAt <= evt.CreatedAt)
                        .Select(link => link.RevisionPlanId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToList();
                    if (chapterMatchedRevisionIds.Count == 1)
                        ids.Add(chapterMatchedRevisionIds[0]);
                }

                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    var inferredChapterIds = ids
                        .Where(id => chapterIdByRevisionPlanId.TryGetValue(id, out _))
                        .Select(id => chapterIdByRevisionPlanId[id])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (inferredChapterIds.Count == 1)
                        chapterId = inferredChapterIds[0];
                }

                var preferredChapterIds = ids
                    .SelectMany(revisionPlanId => GetChapterCandidateKeys(evt)
                        .Select(candidate => $"{revisionPlanId}:{candidate}"))
                    .Where(key => preferredChapterIdByRevisionCandidate.ContainsKey(key))
                    .Select(key => preferredChapterIdByRevisionCandidate[key])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (preferredChapterIds.Count == 1)
                {
                    chapterId = ChoosePreferredChapterId(new[]
                    {
                        (chapterId, evt.CreatedAt),
                        (preferredChapterIds[0], evt.CreatedAt)
                    });
                }

                return ids.SetEquals(evt.RevisionPlanIds) &&
                       string.Equals(chapterId, evt.ChapterId, StringComparison.OrdinalIgnoreCase)
                    ? evt
                    : evt with
                    {
                        ChapterId = chapterId,
                        RevisionPlanIds = ids.Take(24).ToList()
                    };
            })
            .ToList();
    }

    private static ProductionChainProjection BuildProjection(
        IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        var ordered = events
            .OrderBy(evt => evt.CreatedAt)
            .ToList();
        var latest = ordered.Last();
        var commitEvent = ordered
            .LastOrDefault(evt => string.Equals(evt.EventType, "chapter_committed", StringComparison.OrdinalIgnoreCase));
        var revisionPlanIds = ordered
            .SelectMany(evt => evt.RevisionPlanIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        var rebuildLinks = ordered
            .SelectMany(evt => evt.RebuildLinks)
            .GroupBy(link => $"{link.OldPackageId}->{link.NewPackageId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Take(24)
            .ToList();
        var chapterId = FirstNonEmpty(
            latest.ChapterId,
            rebuildLinks.Select(link => link.ChapterId).FirstOrDefault());
        var chapterLogicalId = FirstNonEmpty(
            ordered.Select(evt => evt.ChapterLogicalId).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            BuildChapterIdCandidates(chapterId).FirstOrDefault(candidate => candidate.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase)));
        var chapterDisplayName = FirstNonEmpty(
            ordered.Select(evt => evt.ChapterDisplayName).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            chapterLogicalId);
        var runtimeRunId = FirstNonEmpty(
            latest.RuntimeRunId,
            ordered.Select(evt => evt.RuntimeRunId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var packageId = FirstNonEmpty(
            latest.PackageId,
            ordered.Select(evt => evt.PackageId).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var primaryRevisionPlanId = revisionPlanIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        var chainId = !string.IsNullOrWhiteSpace(primaryRevisionPlanId)
            ? (!string.IsNullOrWhiteSpace(chapterId)
                ? $"revision_plan:{primaryRevisionPlanId}:{chapterId}"
                : $"revision_plan:{primaryRevisionPlanId}")
            : (!string.IsNullOrWhiteSpace(chapterId) && !string.IsNullOrWhiteSpace(runtimeRunId)
                ? $"{chapterId}:{runtimeRunId}:{packageId}"
                : FirstNonEmpty(packageId, latest.ArtifactId, latest.Id));
        var version = ordered
            .Where(evt => !string.IsNullOrWhiteSpace(evt.ChapterVersionId))
            .LastOrDefault();
        var fact = ordered
            .Where(evt => !string.IsNullOrWhiteSpace(evt.FactSnapshotId))
            .LastOrDefault();
        var evidence = BuildChainEvidence(ordered);

        return new ProductionChainProjection(
            chainId,
            chapterId,
            chapterLogicalId,
            chapterDisplayName,
            runtimeRunId,
            packageId,
            ResolveProductionChainStatus(ordered),
            FirstNonEmpty(commitEvent?.Message, latest.Message),
            latest.CreatedAt,
            latest.CreatedAtText,
            version?.ChapterVersionId ?? string.Empty,
            version?.ChapterVersionNumber ?? 0,
            fact?.FactSnapshotId ?? string.Empty,
            fact?.FactSnapshotVersion ?? 0,
            revisionPlanIds,
            rebuildLinks,
            evidence,
            ordered.Select(ToStepProjection).ToList());
    }

    private static WorkflowProductionChainEvidence BuildChainEvidence(
        IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        var changeArtifactIds = events
            .Where(IsChangesProductionEvent)
            .Select(evt => FirstNonEmpty(evt.ChapterChangeArtifactId, evt.ArtifactId, evt.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();

        return new WorkflowProductionChainEvidence(
            events.Select(evt => evt.GateEvidence).LastOrDefault(evidence => evidence != null),
            events.Select(evt => evt.FactSnapshotEvidence).LastOrDefault(evidence => evidence != null),
            events.Select(evt => evt.AgentReviewEvidence).LastOrDefault(evidence => evidence != null),
            changeArtifactIds.Count,
            changeArtifactIds);
    }

    private static ProductionChainEventSnapshot ToWorkflowSnapshot(WorkflowProductionEventSummary evt)
    {
        var revisionPlanIds = new List<string>();
        var readablePlan = (evt.Evidence?.SourceRevisionPlans ?? Array.Empty<WorkflowRevisionPlanEvidence>())
            .LastOrDefault(plan =>
                !string.IsNullOrWhiteSpace(plan.TargetChapterLogicalId) ||
                !string.IsNullOrWhiteSpace(plan.TargetChapterDisplayName));
        if (string.Equals(evt.ArtifactType, "RevisionPlan", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(evt.ArtifactId))
        {
            revisionPlanIds.Add(evt.ArtifactId);
        }

        revisionPlanIds.AddRange(
            (evt.Evidence?.SourceRevisionPlans ?? Array.Empty<WorkflowRevisionPlanEvidence>())
            .Select(plan => plan.RevisionPlanId)
            .Where(id => !string.IsNullOrWhiteSpace(id)));
        var eventRevisionPlanId = GetJsonString(evt.DataJson, "revisionPlanId");
        if (!string.IsNullOrWhiteSpace(eventRevisionPlanId))
            revisionPlanIds.Add(eventRevisionPlanId);
        revisionPlanIds.AddRange(GetRevisionPlanIdsFromDataJson(evt.DataJson));

        var rebuildLinks = (evt.Evidence?.RebuildLinks ?? Array.Empty<WorkflowPackageRebuildLinkEvidence>())
            .Concat(BuildWorkflowRebuildLinksFromEventData(evt))
            .GroupBy(link => $"{link.OldPackageId}->{link.NewPackageId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .Select(ToRebuildSnapshot)
            .ToList();

        return new ProductionChainEventSnapshot(
            evt.Id,
            evt.RuntimeRunId,
            evt.ChapterId,
            FirstNonEmpty(
                readablePlan?.TargetChapterLogicalId,
                GetJsonString(evt.DataJson, "targetChapterLogicalId"),
                GetJsonString(evt.DataJson, "chapterLogicalId"),
                GetSourceRevisionPlanString(evt.DataJson, "targetChapterLogicalId")),
            FirstNonEmpty(
                readablePlan?.TargetChapterDisplayName,
                GetJsonString(evt.DataJson, "targetChapterDisplayName"),
                GetJsonString(evt.DataJson, "chapterDisplayName"),
                GetSourceRevisionPlanString(evt.DataJson, "targetChapterDisplayName")),
            evt.PackageId,
            evt.EventType,
            evt.Stage,
            evt.Status,
            evt.Message,
            evt.ArtifactType,
            evt.ArtifactId,
            evt.DataJson,
            ParseDate(evt.CreatedAt),
            evt.CreatedAt,
            evt.Evidence?.Outbox?.OutboxEventId ?? string.Empty,
            FirstNonEmpty(evt.Evidence?.ChapterVersionId, IsChapterVersionEvent(evt) ? evt.ArtifactId : string.Empty, GetJsonString(evt.DataJson, "chapterVersionId")),
            FirstNonZero(evt.Evidence?.ChapterVersionNumber ?? 0, GetJsonInt(evt.DataJson, "versionNumber")),
            FirstNonEmpty(evt.Evidence?.FactSnapshotId, GetJsonString(evt.DataJson, "factSnapshotId")),
            FirstNonZero(evt.Evidence?.FactSnapshotVersion ?? 0, GetJsonInt(evt.DataJson, "factSnapshotVersion")),
            revisionPlanIds,
            rebuildLinks,
            evt.Evidence?.Gate,
            evt.Evidence?.FactSnapshot,
            evt.Evidence?.AgentReview,
            BuildChapterChangeArtifactId(evt.EventType, evt.Stage, evt.ArtifactId, evt.Id));
    }

    private static IReadOnlyList<WorkflowPackageRebuildLinkEvidence> BuildWorkflowRebuildLinksFromEventData(
        WorkflowProductionEventSummary evt)
    {
        var newPackageId = FirstNonEmpty(
            evt.PackageId,
            string.Equals(evt.ArtifactType, "TianmingPackage", StringComparison.OrdinalIgnoreCase)
                ? evt.ArtifactId
                : string.Empty,
            GetJsonString(evt.DataJson, "packageId"),
            GetJsonString(evt.DataJson, "newPackageId"));
        if (string.IsNullOrWhiteSpace(newPackageId))
            return Array.Empty<WorkflowPackageRebuildLinkEvidence>();

        return GetJsonStringArray(evt.DataJson, "rebuiltFromPackageIds", "rebuiltFromPackageIdsJson")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(oldPackageId => new WorkflowPackageRebuildLinkEvidence(
                oldPackageId,
                string.Empty,
                newPackageId,
                evt.Status,
                GetJsonString(evt.DataJson, "packageKind"),
                evt.ChapterId,
                evt.RuntimeRunId))
            .Take(24)
            .ToList();
    }

    private static ProductionChainEventSnapshot ToNovelSnapshot(
        NovelProductionEventState evt,
        IReadOnlyDictionary<string, NovelProductionPackageState> packages,
        IReadOnlyList<NovelProductionRevisionPlanState> revisionPlans,
        IReadOnlyDictionary<string, NovelProductionOutboxState> outboxes,
        IReadOnlyList<NovelProductionRebuildLinkState> rebuildLinks)
    {
        packages.TryGetValue(evt.PackageId, out var package);
        var chapterId = FirstNonEmpty(evt.ChapterId, package?.ChapterId);
        var runtimeRunId = FirstNonEmpty(evt.RuntimeRunId, package?.RuntimeRunId);
        var packageId = FirstNonEmpty(evt.PackageId, package?.Id);
        var outboxId = string.Equals(evt.ArtifactType, "outbox_event", StringComparison.OrdinalIgnoreCase)
            ? evt.ArtifactId
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(outboxId) && !outboxes.ContainsKey(outboxId))
            outboxId = evt.ArtifactId;
        var chainRebuildLinks = rebuildLinks
            .Where(link =>
                string.Equals(link.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.NewPackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.OldPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .Select(ToRebuildSnapshot)
            .ToList();
        var revisionPlanIds = BuildNovelRevisionPlanIds(evt, revisionPlans, chapterId, runtimeRunId, packageId);
        var readablePlan = revisionPlans
            .LastOrDefault(plan => revisionPlanIds.Contains(plan.Id, StringComparer.OrdinalIgnoreCase));

        return new ProductionChainEventSnapshot(
            evt.Id,
            runtimeRunId,
            chapterId,
            FirstNonEmpty(
                readablePlan?.TargetChapterLogicalId,
                GetJsonString(evt.DataJson, "targetChapterLogicalId"),
                GetJsonString(evt.DataJson, "chapterLogicalId"),
                GetSourceRevisionPlanString(evt.DataJson, "targetChapterLogicalId")),
            FirstNonEmpty(
                readablePlan?.TargetChapterDisplayName,
                GetJsonString(evt.DataJson, "targetChapterDisplayName"),
                GetJsonString(evt.DataJson, "chapterDisplayName"),
                GetSourceRevisionPlanString(evt.DataJson, "targetChapterDisplayName")),
            packageId,
            evt.EventType,
            evt.Stage,
            evt.Status,
            evt.Message,
            evt.ArtifactType,
            evt.ArtifactId,
            evt.DataJson,
            evt.CreatedAt,
            evt.CreatedAt.ToString("O"),
            outboxId,
            FirstNonEmpty(IsChapterVersionEvent(evt) ? evt.ArtifactId : string.Empty, GetJsonString(evt.DataJson, "chapterVersionId")),
            GetJsonInt(evt.DataJson, "versionNumber"),
            GetJsonString(evt.DataJson, "factSnapshotId"),
            GetJsonInt(evt.DataJson, "factSnapshotVersion"),
            revisionPlanIds,
            chainRebuildLinks,
            evt.GateEvidence,
            evt.FactSnapshotEvidence,
            evt.AgentReviewEvidence,
            BuildChapterChangeArtifactId(evt.EventType, evt.Stage, evt.ArtifactId, evt.Id));
    }

    private static List<string> BuildNovelRevisionPlanIds(
        NovelProductionEventState evt,
        IReadOnlyList<NovelProductionRevisionPlanState> revisionPlans,
        string chapterId,
        string runtimeRunId,
        string packageId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(evt.ArtifactType, "RevisionPlan", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(evt.ArtifactId))
        {
            ids.Add(evt.ArtifactId);
        }

        var eventRevisionPlanId = GetJsonString(evt.DataJson, "revisionPlanId");
        if (!string.IsNullOrWhiteSpace(eventRevisionPlanId))
            ids.Add(eventRevisionPlanId);
        foreach (var id in GetRevisionPlanIdsFromDataJson(evt.DataJson))
            ids.Add(id);

        foreach (var plan in revisionPlans)
        {
            var invalidatedPackageIds = ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson);
            var planChapterCandidates = BuildRevisionPlanChapterCandidates(plan);
            var eventChapterCandidates = BuildChapterIdCandidates(chapterId);
            if (string.Equals(plan.RuntimeRunId, runtimeRunId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(plan.TargetChapterId, chapterId, StringComparison.OrdinalIgnoreCase) ||
                planChapterCandidates.Any(candidate => eventChapterCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)) ||
                invalidatedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(plan.Id);
            }
        }

        return ids.Take(24).ToList();
    }

    private static bool ShouldAttachRevisionPlanSnapshot(
        NovelProductionRevisionPlanState plan,
        IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        if (events.Count == 0)
            return false;

        var invalidatedPackageIds = ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson);
        var affectedChapterIds = ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson);
        var eventPackageIds = events
            .SelectMany(evt => new[]
            {
                evt.PackageId,
                evt.RebuildLinks.FirstOrDefault()?.OldPackageId ?? string.Empty,
                evt.RebuildLinks.FirstOrDefault()?.NewPackageId ?? string.Empty
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var eventChapterCandidates = events
            .SelectMany(evt => BuildChapterIdCandidates(FirstNonEmpty(evt.ChapterId, evt.RebuildLinks.FirstOrDefault()?.ChapterId)))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var revisionPlanChapterCandidates = BuildRevisionPlanChapterCandidates(plan);

        return events.Any(evt => evt.RevisionPlanIds.Contains(plan.Id, StringComparer.OrdinalIgnoreCase)) ||
               eventPackageIds.Any(packageId => invalidatedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase)) ||
               revisionPlanChapterCandidates.Any(chapterId => eventChapterCandidates.Contains(chapterId, StringComparer.OrdinalIgnoreCase)) ||
               affectedChapterIds
                   .SelectMany(BuildChapterIdCandidates)
                   .Any(chapterId => eventChapterCandidates.Contains(chapterId, StringComparer.OrdinalIgnoreCase));
    }

    private static ProductionChainEventSnapshot ToNovelRevisionPlanSnapshot(NovelProductionRevisionPlanState plan)
    {
        var invalidatedPackageIds = ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson);
        return new ProductionChainEventSnapshot(
            plan.Id,
            plan.RuntimeRunId,
            plan.TargetChapterId,
            plan.TargetChapterLogicalId,
            plan.TargetChapterDisplayName,
            invalidatedPackageIds.FirstOrDefault() ?? string.Empty,
            "revision_plan_ready",
            "revision_plan",
            plan.Status,
            FirstNonEmpty(plan.Recommendation, "修订计划已准备执行。"),
            "RevisionPlan",
            plan.Id,
            JsonSerializer.Serialize(new
            {
                revisionPlanId = plan.Id,
                targetChapterId = plan.TargetChapterId,
                targetChapterLogicalId = plan.TargetChapterLogicalId,
                targetChapterDisplayName = plan.TargetChapterDisplayName,
                invalidatedPackageIds,
                affectedChapterIds = ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson)
            }),
            plan.CreatedAt,
            plan.CreatedAt.ToString("O"),
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            0,
            new[] { plan.Id },
            Array.Empty<ProductionChainRebuildLinkSnapshot>(),
            null,
            null,
            null,
            string.Empty);
    }

    private static string BuildChapterChangeArtifactId(
        string eventType,
        string stage,
        string artifactId,
        string eventId) =>
        string.Equals(eventType, "chapter_changes_recorded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(stage), NovelAgentProductionStages.ChangesExtracted, StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(artifactId, eventId)
            : string.Empty;

    private static string BuildProductionChainGroupKey(ProductionChainEventSnapshot evt)
    {
        var chapterId = FirstNonEmpty(
            evt.ChapterId,
            evt.ChapterLogicalId,
            evt.RebuildLinks.FirstOrDefault()?.ChapterId,
            BuildChapterIdCandidates(evt.ChapterDisplayName).FirstOrDefault());
        var revisionPlanId = evt.RevisionPlanIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (!string.IsNullOrWhiteSpace(revisionPlanId))
        {
            var chapterGroupKey = BuildChapterGroupKey(chapterId);
            return !string.IsNullOrWhiteSpace(chapterId)
                ? $"revision_plan:{revisionPlanId}:{chapterGroupKey}"
                : $"revision_plan:{revisionPlanId}";
        }

        var runtimeRunId = FirstNonEmpty(evt.RuntimeRunId, "no-run");
        var packageId = FirstNonEmpty(evt.PackageId, evt.RebuildLinks.FirstOrDefault()?.NewPackageId);

        if (!string.IsNullOrWhiteSpace(chapterId) && !string.IsNullOrWhiteSpace(runtimeRunId))
            return $"{chapterId}:{runtimeRunId}";
        if (!string.IsNullOrWhiteSpace(packageId))
            return $"package:{packageId}";
        return $"event:{evt.Id}";
    }

    private static string BuildChapterGroupKey(string chapterId)
    {
        var number = ExtractTrailingNumber(chapterId);
        if (number > 0 && BuildChapterIdCandidates(chapterId)
                .Any(candidate => candidate.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase)))
        {
            return $"chapter-{number:000}";
        }

        return chapterId;
    }

    private static string ResolveProductionChainStatus(IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        var hasCommit = events.Any(evt => string.Equals(evt.EventType, "chapter_committed", StringComparison.OrdinalIgnoreCase));
        if (hasCommit && !HasActiveOutbox(events))
            return "completed";

        if (events.Any(evt =>
                evt.Status.Contains("running", StringComparison.OrdinalIgnoreCase) ||
                evt.Status.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
                evt.Status.Contains("pending", StringComparison.OrdinalIgnoreCase)))
        {
            return "running";
        }

        if (events.Any(IsBlockedProductionEvent))
            return "blocked";

        if (events.Any(evt =>
                evt.Status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                evt.Status.Contains("executed", StringComparison.OrdinalIgnoreCase)))
        {
            return "in_progress";
        }

        return "pending";
    }

    private static bool HasActiveOutbox(IReadOnlyList<ProductionChainEventSnapshot> events)
    {
        return events
            .Where(IsIndexProductionEvent)
            .GroupBy(evt => FirstNonEmpty(evt.OutboxEventId, evt.ArtifactId, evt.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(evt => evt.CreatedAt).First())
            .Any(evt => !evt.Status.Contains("completed", StringComparison.OrdinalIgnoreCase));
    }

    private static ProductionChainStepProjection ToStepProjection(ProductionChainEventSnapshot evt) =>
        new(
            ResolveProductionChainStepKey(evt),
            ResolveProductionChainStepLabel(evt),
            evt.Status,
            evt.Id,
            evt.EventType,
            evt.Stage,
            evt.ArtifactType,
            evt.ArtifactId,
            evt.Message,
            evt.CreatedAt,
            evt.CreatedAtText,
            evt.OutboxEventId);

    private static string ResolveProductionChainStepKey(ProductionChainEventSnapshot evt)
    {
        if (IsRevisionPlanProductionEvent(evt))
            return "revision_plan";
        if (IsContextProductionEvent(evt))
            return "context";
        if (IsDraftProductionEvent(evt))
            return "draft";
        if (IsChangesProductionEvent(evt))
            return "changes";
        if (IsGateProductionEvent(evt))
            return "gate";
        if (IsRepairProductionEvent(evt))
            return "repair";
        if (IsQualityProductionEvent(evt))
            return "quality";
        if (IsLibraryProductionEvent(evt))
            return "commit";
        if (IsFactsProductionEvent(evt))
            return "facts";
        if (IsIndexProductionEvent(evt))
            return "outbox";
        return FirstNonEmpty(evt.Stage, evt.EventType, "event");
    }

    private static string ResolveProductionChainStepLabel(ProductionChainEventSnapshot evt) =>
        ResolveProductionChainStepKey(evt) switch
        {
            "revision_plan" => "修订计划",
            "context" => "上下文包",
            "draft" => "正文草稿",
            "changes" => "CHANGES",
            "gate" => "结构门禁",
            "repair" => "自动修订",
            "quality" => "质量评审",
            "commit" => "书城入库",
            "facts" => "事实沉淀",
            "outbox" => "后台处理",
            _ => "生产事件"
        };

    private static bool IsContextProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "build_package" or "chapter_context_package_built" ||
        string.Equals(evt.Stage, "BuildChapterPackage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.PackageBuilt, StringComparison.OrdinalIgnoreCase);

    private static bool IsDraftProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_draft_generated" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.DraftGenerated, StringComparison.OrdinalIgnoreCase);

    private static bool IsChangesProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_changes_recorded" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.ChangesExtracted, StringComparison.OrdinalIgnoreCase);

    private static bool IsGateProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_gate_validated" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.GateValidated, StringComparison.OrdinalIgnoreCase);

    private static bool IsRepairProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_draft_repaired" or "chapter_agent_review_feedback_applied" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.DraftRewritten, StringComparison.OrdinalIgnoreCase);

    private static bool IsQualityProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_quality_reviewed" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.ReviewCompleted, StringComparison.OrdinalIgnoreCase);

    private static bool IsLibraryProductionEvent(ProductionChainEventSnapshot evt) =>
        !IsRevisionPlanProductionEvent(evt) &&
        (evt.EventType is "chapter_committed" ||
         string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.ChapterCommitted, StringComparison.OrdinalIgnoreCase));

    private static bool IsFactsProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType is "chapter_continuity_facts_extracted" or "creative_intents_executed" or "knowledge_bindings_used" or "chapter_summary_recorded" ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.FactsPersisted, StringComparison.OrdinalIgnoreCase);

    private static bool IsIndexProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.Stage, "index_outbox", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NovelAgentProductionStages.ToCanonicalStage(evt.Stage), NovelAgentProductionStages.IndexUpdated, StringComparison.OrdinalIgnoreCase);

    private static bool IsRevisionPlanProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.EventType.StartsWith("revision_plan_", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.ArtifactType, "RevisionPlan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.Stage, "packages_invalidated", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedProductionEvent(ProductionChainEventSnapshot evt) =>
        evt.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        evt.Status.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        evt.Status.Contains("invalid", StringComparison.OrdinalIgnoreCase);

    private static bool IsChapterVersionEvent(WorkflowProductionEventSummary evt) =>
        string.Equals(evt.ArtifactType, "chapter_version", StringComparison.OrdinalIgnoreCase);

    private static bool IsChapterVersionEvent(NovelProductionEventState evt) =>
        string.Equals(evt.ArtifactType, "chapter_version", StringComparison.OrdinalIgnoreCase);

    private static WorkflowProductionChain ToWorkflowChain(ProductionChainProjection chain) =>
        new(
            chain.Id,
            chain.ChapterId,
            chain.ChapterLogicalId,
            chain.ChapterDisplayName,
            chain.RuntimeRunId,
            chain.PackageId,
            chain.Status,
            chain.Summary,
            chain.UpdatedAtText,
            chain.ChapterVersionId,
            chain.ChapterVersionNumber,
            chain.FactSnapshotId,
            chain.FactSnapshotVersion,
            chain.RevisionPlanIds,
            chain.RebuildLinks.Select(ToWorkflowRebuildLink).ToList(),
            chain.Steps.Select(ToWorkflowStep).ToList(),
            chain.Evidence);

    private static NovelProductionChainState ToNovelChainState(ProductionChainProjection chain) =>
        new()
        {
            Id = chain.Id,
            ChapterId = chain.ChapterId,
            ChapterLogicalId = chain.ChapterLogicalId,
            ChapterDisplayName = chain.ChapterDisplayName,
            RuntimeRunId = chain.RuntimeRunId,
            PackageId = chain.PackageId,
            Status = chain.Status,
            Summary = chain.Summary,
            ChapterVersionId = chain.ChapterVersionId,
            ChapterVersionNumber = chain.ChapterVersionNumber,
            FactSnapshotId = chain.FactSnapshotId,
            FactSnapshotVersion = chain.FactSnapshotVersion,
            RevisionPlanIds = chain.RevisionPlanIds.ToList(),
            RebuildLinks = chain.RebuildLinks.Select(ToNovelRebuildLink).ToList(),
            Evidence = chain.Evidence,
            Steps = chain.Steps.Select(ToNovelStep).ToList()
        };

    private static WorkflowProductionChainStep ToWorkflowStep(ProductionChainStepProjection step) =>
        new(
            step.Key,
            step.Label,
            step.Status,
            step.EventId,
            step.EventType,
            step.Stage,
            step.ArtifactType,
            step.ArtifactId,
            step.Message,
            step.CreatedAtText,
            step.OutboxEventId);

    private static NovelProductionChainStepState ToNovelStep(ProductionChainStepProjection step) =>
        new()
        {
            Key = step.Key,
            Label = step.Label,
            Status = step.Status,
            EventId = step.EventId,
            EventType = step.EventType,
            Stage = step.Stage,
            ArtifactType = step.ArtifactType,
            ArtifactId = step.ArtifactId,
            Message = step.Message,
            OutboxEventId = step.OutboxEventId,
            CreatedAt = step.CreatedAt
        };

    private static ProductionChainRebuildLinkSnapshot ToRebuildSnapshot(WorkflowPackageRebuildLinkEvidence link) =>
        new(
            link.OldPackageId,
            link.OldPackageStatus,
            link.NewPackageId,
            link.NewPackageStatus,
            link.NewPackageKind,
            link.ChapterId,
            link.RuntimeRunId);

    private static ProductionChainRebuildLinkSnapshot ToRebuildSnapshot(NovelProductionRebuildLinkState link) =>
        new(
            link.OldPackageId,
            link.OldPackageStatus,
            link.NewPackageId,
            link.NewPackageStatus,
            link.NewPackageKind,
            link.ChapterId,
            link.RuntimeRunId);

    private static WorkflowPackageRebuildLinkEvidence ToWorkflowRebuildLink(ProductionChainRebuildLinkSnapshot link) =>
        new(
            link.OldPackageId,
            link.OldPackageStatus,
            link.NewPackageId,
            link.NewPackageStatus,
            link.NewPackageKind,
            link.ChapterId,
            link.RuntimeRunId);

    private static NovelProductionRebuildLinkState ToNovelRebuildLink(ProductionChainRebuildLinkSnapshot link) =>
        new()
        {
            OldPackageId = link.OldPackageId,
            OldPackageStatus = link.OldPackageStatus,
            NewPackageId = link.NewPackageId,
            NewPackageStatus = link.NewPackageStatus,
            NewPackageKind = link.NewPackageKind,
            ChapterId = link.ChapterId,
            RuntimeRunId = link.RuntimeRunId
        };

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : DateTime.MinValue;

    private static int FirstNonZero(params int[] values) =>
        values.FirstOrDefault(value => value > 0);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static List<string> BuildChapterIdCandidates(string? chapterId)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = chapterId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            candidates.Add(normalized);
            var markerIndex = normalized.LastIndexOf("chapter-", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                candidates.Add(normalized[markerIndex..]);
        }

        var number = ExtractTrailingNumber(normalized);
        if (number > 0)
        {
            candidates.Add($"chapter-{number:000}");
            candidates.Add($"chapter-{number}");
        }

        var chapterOrdinal = ExtractChapterOrdinal(normalized);
        if (chapterOrdinal > 0)
        {
            candidates.Add($"chapter-{chapterOrdinal:000}");
            candidates.Add($"chapter-{chapterOrdinal}");
        }

        return candidates.ToList();
    }

    private static IReadOnlyList<string> BuildRevisionPlanChapterCandidates(NovelProductionRevisionPlanState plan) =>
        new[]
            {
                plan.TargetChapterId,
                plan.TargetChapterLogicalId,
                plan.TargetChapterDisplayName
            }
            .Concat(ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson))
            .SelectMany(BuildChapterIdCandidates)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ChoosePreferredChapterId(IReadOnlyList<(string ChapterId, DateTime CreatedAt)> items) =>
        items
            .Where(item => !string.IsNullOrWhiteSpace(item.ChapterId))
            .GroupBy(item => item.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ChapterId = group.Key,
                LatestAt = group.Max(item => item.CreatedAt)
            })
            .OrderBy(item => IsLogicalChapterId(item.ChapterId))
            .ThenByDescending(item => item.ChapterId.Length)
            .ThenByDescending(item => item.LatestAt)
            .Select(item => item.ChapterId)
            .FirstOrDefault() ?? string.Empty;

    private static bool IsLogicalChapterId(string chapterId) =>
        chapterId.Trim().StartsWith("chapter-", StringComparison.OrdinalIgnoreCase);

    private static int ExtractChapterOrdinal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var markerIndex = value.IndexOf('第');
        if (markerIndex < 0 || markerIndex >= value.Length - 1)
            return 0;

        var start = markerIndex + 1;
        var end = value.IndexOfAny(new[] { '章', '回', '节' }, start);
        if (end <= start)
            return 0;

        return ParseChapterNumberToken(value[start..end]);
    }

    private static int ParseChapterNumberToken(string token)
    {
        var normalized = token.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        if (int.TryParse(normalized, out var arabic))
            return arabic;

        return ParseChineseNumber(normalized);
    }

    private static int ParseChineseNumber(string value)
    {
        var total = 0;
        var section = 0;
        var currentDigit = 0;

        foreach (var ch in value)
        {
            var digit = ChineseDigitValue(ch);
            if (digit >= 0)
            {
                currentDigit = digit;
                continue;
            }

            var unit = ChineseUnitValue(ch);
            if (unit <= 0)
                return 0;

            section += (currentDigit == 0 ? 1 : currentDigit) * unit;
            currentDigit = 0;
            if (unit >= 10000)
            {
                total += section;
                section = 0;
            }
        }

        return total + section + currentDigit;
    }

    private static int ChineseDigitValue(char ch) =>
        ch switch
        {
            '零' or '〇' => 0,
            '一' => 1,
            '二' or '两' => 2,
            '三' => 3,
            '四' => 4,
            '五' => 5,
            '六' => 6,
            '七' => 7,
            '八' => 8,
            '九' => 9,
            _ => -1
        };

    private static int ChineseUnitValue(char ch) =>
        ch switch
        {
            '十' => 10,
            '百' => 100,
            '千' => 1000,
            '万' => 10000,
            _ => 0
        };

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index]))
            index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static int GetJsonInt(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out number)
            ? number
            : 0;
    }

    private static string GetJsonString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static IReadOnlyList<string> GetJsonStringArray(string? json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        using var document = TryParseJson(json);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();

        var values = new List<string>();
        foreach (var propertyName in propertyNames)
        {
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.Array)
            {
                values.AddRange(property
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))!);
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var raw = property.GetString();
                var parsed = ChapterIdentityResolver.ParseStringArray(raw);
                values.AddRange(parsed.Count > 0
                    ? parsed
                    : string.IsNullOrWhiteSpace(raw)
                        ? Array.Empty<string>()
                        : new[] { raw });
                continue;
            }

            values.AddRange(ChapterIdentityResolver.ParseStringArray(property.ToString()));
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetPrimaryChapterIdFromEvent(ProductionChainEventSnapshot evt) =>
        FirstNonEmpty(
            evt.RebuildLinks.Select(link => link.ChapterId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            GetJsonString(evt.DataJson, "targetChapterId"),
            GetJsonString(evt.DataJson, "chapterId"),
            GetJsonStringArray(evt.DataJson, "affectedChapterIds", "affectedChapterIdsJson").FirstOrDefault());

    private static IReadOnlyList<string> GetChapterCandidateKeys(ProductionChainEventSnapshot evt)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[]
                 {
                     evt.ChapterId,
                     evt.ChapterLogicalId,
                     evt.ChapterDisplayName,
                     evt.RebuildLinks.Select(link => link.ChapterId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                     GetJsonString(evt.DataJson, "targetChapterId"),
                     GetJsonString(evt.DataJson, "chapterId"),
                     GetJsonString(evt.DataJson, "targetChapterDisplayName"),
                     GetJsonString(evt.DataJson, "chapterDisplayName"),
                     GetSourceRevisionPlanString(evt.DataJson, "targetChapterDisplayName")
                 })
        {
            foreach (var candidate in BuildChapterIdCandidates(value))
                candidates.Add(candidate);
        }

        foreach (var value in GetJsonStringArray(evt.DataJson, "affectedChapterIds", "affectedChapterIdsJson", "targetChapterIds"))
        {
            foreach (var candidate in BuildChapterIdCandidates(value))
                candidates.Add(candidate);
        }

        return candidates.ToList();
    }

    private static IReadOnlyList<string> GetRevisionPlanIdsFromDataJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        using var document = TryParseJson(json);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();

        var ids = new List<string>();
        AddJsonString(ids, document.RootElement, "revisionPlanId");
        AddJsonString(ids, document.RootElement, "sourceRevisionPlanId");
        ids.AddRange(GetJsonStringArray(json, "revisionPlanIds", "sourceRevisionPlanIds"));

        if (document.RootElement.TryGetProperty("sourceRevisionPlans", out var sourcePlans))
        {
            AddRevisionPlanIdsFromSourcePlans(ids, sourcePlans);
        }

        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetSourceRevisionPlanString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        using var document = TryParseJson(json);
        if (document == null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("sourceRevisionPlans", out var sourcePlans))
        {
            return string.Empty;
        }

        return GetSourceRevisionPlanString(sourcePlans, propertyName);
    }

    private static string GetSourceRevisionPlanString(JsonElement sourcePlans, string propertyName)
    {
        if (sourcePlans.ValueKind == JsonValueKind.String)
        {
            using var nested = TryParseJson(sourcePlans.GetString());
            return nested == null
                ? string.Empty
                : GetSourceRevisionPlanString(nested.RootElement, propertyName);
        }

        if (sourcePlans.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var item in sourcePlans.EnumerateArray().Reverse())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static void AddRevisionPlanIdsFromSourcePlans(List<string> ids, JsonElement sourcePlans)
    {
        if (sourcePlans.ValueKind == JsonValueKind.String)
        {
            using var nested = TryParseJson(sourcePlans.GetString());
            if (nested != null)
                AddRevisionPlanIdsFromSourcePlans(ids, nested.RootElement);
            return;
        }

        if (sourcePlans.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in sourcePlans.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    ids.Add(value);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            AddJsonString(ids, item, "revisionPlanId");
            AddJsonString(ids, item, "id");
        }
    }

    private static void AddJsonString(List<string> values, JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return;

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }
    }

    private static JsonDocument? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ProductionChainEventSnapshot(
        string Id,
        string RuntimeRunId,
        string ChapterId,
        string ChapterLogicalId,
        string ChapterDisplayName,
        string PackageId,
        string EventType,
        string Stage,
        string Status,
        string Message,
        string ArtifactType,
        string ArtifactId,
        string DataJson,
        DateTime CreatedAt,
        string CreatedAtText,
        string OutboxEventId,
        string ChapterVersionId,
        int ChapterVersionNumber,
        string FactSnapshotId,
        int FactSnapshotVersion,
        IReadOnlyList<string> RevisionPlanIds,
        IReadOnlyList<ProductionChainRebuildLinkSnapshot> RebuildLinks,
        WorkflowGateEvidence? GateEvidence,
        WorkflowFactSnapshotEvidence? FactSnapshotEvidence,
        WorkflowAgentReviewSummaryEvidence? AgentReviewEvidence,
        string ChapterChangeArtifactId);

    private sealed record ProductionChainProjection(
        string Id,
        string ChapterId,
        string ChapterLogicalId,
        string ChapterDisplayName,
        string RuntimeRunId,
        string PackageId,
        string Status,
        string Summary,
        DateTime UpdatedAt,
        string UpdatedAtText,
        string ChapterVersionId,
        int ChapterVersionNumber,
        string FactSnapshotId,
        int FactSnapshotVersion,
        IReadOnlyList<string> RevisionPlanIds,
        IReadOnlyList<ProductionChainRebuildLinkSnapshot> RebuildLinks,
        WorkflowProductionChainEvidence Evidence,
        IReadOnlyList<ProductionChainStepProjection> Steps);

    private sealed record ProductionChainStepProjection(
        string Key,
        string Label,
        string Status,
        string EventId,
        string EventType,
        string Stage,
        string ArtifactType,
        string ArtifactId,
        string Message,
        DateTime CreatedAt,
        string CreatedAtText,
        string OutboxEventId);

    private sealed record RevisionPlanChapterLink(
        string ChapterCandidate,
        string RevisionPlanId,
        DateTime CreatedAt);

    private sealed record ProductionChainRebuildLinkSnapshot(
        string OldPackageId,
        string OldPackageStatus,
        string NewPackageId,
        string NewPackageStatus,
        string NewPackageKind,
        string ChapterId,
        string RuntimeRunId);
}
