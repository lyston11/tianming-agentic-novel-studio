using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Creative;

public sealed class RevisionPlanPackageInvalidationService : IRevisionPlanPackageInvalidationService
{
    private readonly NovelAgentDbContext _db;
    private readonly IProductionEventWriter _events;
    private readonly IAgentRuntimeEventService _runtimeEvents;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public RevisionPlanPackageInvalidationService(
        NovelAgentDbContext db,
        IProductionEventWriter events,
        IAgentRuntimeEventService runtimeEvents,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _db = db;
        _events = events;
        _runtimeEvents = runtimeEvents;
        _outputArtifacts = outputArtifacts;
    }

    public async Task<RevisionPlanPackageInvalidationResult> InvalidateAsync(
        InvalidateRevisionPlanPackagesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.RevisionPlanId))
        {
            return new RevisionPlanPackageInvalidationResult
            {
                Success = false,
                Message = "缺少 userId、projectId 或 revisionPlanId，无法执行修订计划包失效。"
            };
        }

        var plan = await _db.RevisionPlans
            .FirstOrDefaultAsync(p =>
                p.Id == request.RevisionPlanId &&
                p.UserId == request.UserId &&
                p.ProjectId == request.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (plan == null)
        {
            return new RevisionPlanPackageInvalidationResult
            {
                Success = false,
                Message = "未找到当前项目可执行的修订计划。"
            };
        }

        var originalTargetChapterId = plan.TargetChapterId;
        var canonicalTargetChapterId = await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                request.ProjectId,
                plan.TargetChapterId,
                cancellationToken)
            .ConfigureAwait(false);
        var explicitPackageIds = ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson);
        var affectedChapterIds = ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson);
        var projectPackages = await _db.TianmingPackages
            .Where(package => package.UserId == request.UserId && package.ProjectId == request.ProjectId)
            .OrderBy(package => package.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var expandedAffectedChapterIds = await ChapterIdentityResolver.ResolveAffectedAndDownstreamChapterIdsAsync(
            _db,
            request.ProjectId,
            projectPackages.Select(package => package.ChapterId),
            affectedChapterIds,
            cancellationToken).ConfigureAwait(false);

        if (explicitPackageIds.Count == 0 && expandedAffectedChapterIds.Count == 0)
        {
            return new RevisionPlanPackageInvalidationResult
            {
                Success = false,
                RevisionPlanId = plan.Id,
                ProjectId = plan.ProjectId,
                Status = plan.Status,
                Message = "修订计划没有 affectedChapterIds 或 invalidatedPackageIds，无法判断需要失效的生产包。"
            };
        }

        var explicitPackageSet = explicitPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedChapterSet = expandedAffectedChapterIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packages = projectPackages
            .Where(package => explicitPackageSet.Contains(package.Id) ||
                (!string.IsNullOrWhiteSpace(package.ChapterId) && affectedChapterSet.Contains(package.ChapterId)))
            .OrderBy(package => package.CreatedAt)
            .ToList();

        var packageIds = packages.Select(package => package.Id).ToList();
        var storedInvalidatedPackageSet = explicitPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alreadyInvalidated = packageIds.Count > 0 &&
                                 string.Equals(plan.Status, "ready_for_rebuild", StringComparison.OrdinalIgnoreCase) &&
                                 packageIds.All(storedInvalidatedPackageSet.Contains) &&
                                 packages.All(package => string.Equals(package.Status, "stale", StringComparison.OrdinalIgnoreCase));
        if (alreadyInvalidated)
        {
            var existingItems = packages
                .Select(package => new RevisionPlanInvalidatedPackageItem
                {
                    PackageId = package.Id,
                    ChapterId = package.ChapterId ?? string.Empty,
                    PreviousStatus = package.Status,
                    CurrentStatus = package.Status,
                    RuntimeRunId = package.RuntimeRunId ?? string.Empty
                })
                .ToList();

            return new RevisionPlanPackageInvalidationResult
            {
                Success = true,
                Message = $"修订计划已使 {existingItems.Count} 个旧生产包失效：{string.Join("、", existingItems.Select(item => item.PackageId))}。",
                RevisionPlanId = plan.Id,
                ProjectId = plan.ProjectId,
                Status = plan.Status,
                AffectedChapterIds = expandedAffectedChapterIds.ToList(),
                InvalidatedPackageIds = existingItems.Select(item => item.PackageId).ToList(),
                Packages = existingItems
            };
        }

        var now = DateTime.UtcNow;
        var items = new List<RevisionPlanInvalidatedPackageItem>();
        foreach (var package in packages)
        {
            var previousStatus = package.Status;
            if (!string.Equals(package.Status, "stale", StringComparison.OrdinalIgnoreCase))
            {
                package.Status = "stale";
                package.UpdatedAt = now;
            }

            items.Add(new RevisionPlanInvalidatedPackageItem
            {
                PackageId = package.Id,
                ChapterId = package.ChapterId ?? string.Empty,
                PreviousStatus = previousStatus,
                CurrentStatus = package.Status,
                RuntimeRunId = package.RuntimeRunId ?? string.Empty
            });
        }

        if (items.Count > 0)
        {
            plan.Status = "ready_for_rebuild";
            plan.TargetChapterId = canonicalTargetChapterId;
            plan.AffectedChapterIdsJson = JsonSerializer.Serialize(expandedAffectedChapterIds);
            plan.InvalidatedPackageIdsJson = JsonSerializer.Serialize(items.Select(item => item.PackageId).ToArray());
            plan.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var runtimeRunId = FirstNonEmpty(
                request.RuntimeRunId,
                plan.RuntimeRunId,
                items.FirstOrDefault()?.RuntimeRunId,
                $"revision-plan:{plan.Id}");
            var sessionId = FirstNonEmpty(request.SessionId, plan.SessionId, "system");
            var data = new
            {
                revisionPlanId = plan.Id,
                targetChapterId = canonicalTargetChapterId,
                logicalTargetChapterId = originalTargetChapterId,
                affectedChapterIds = expandedAffectedChapterIds,
                logicalAffectedChapterIds = affectedChapterIds,
                invalidatedPackageIds = items.Select(item => item.PackageId).ToArray(),
                packages = items
            };

            var productionEvent = await _events.AppendChapterStageAsync(
                    new AppendChapterProductionEventRequest(
                        RuntimeRunId: runtimeRunId,
                        UserId: request.UserId,
                        ProjectId: request.ProjectId,
                        ChapterId: canonicalTargetChapterId,
                        PackageId: null,
                        EventType: "revision_plan_packages_invalidated",
                        Stage: "packages_invalidated",
                        Status: "stale",
                        Message: $"修订计划已使 {items.Count} 个旧生产包失效，等待重建。",
                        ArtifactType: "RevisionPlan",
                        ArtifactId: plan.Id,
                        Data: data),
                    cancellationToken)
                .ConfigureAwait(false);

            if (_outputArtifacts != null)
            {
                await _outputArtifacts.RecordAsync(
                        new OutputArtifactRecordRequest(
                            RuntimeRunId: runtimeRunId,
                            UserId: request.UserId,
                            ProjectId: request.ProjectId,
                            ChapterId: canonicalTargetChapterId,
                            PackageId: null,
                            ToolName: "RevisionPlanPackageInvalidation",
                            Stage: "packages_invalidated",
                            Status: "stale",
                            ArtifactType: "revision_plan_packages_invalidated",
                            ArtifactId: plan.Id,
                            OutputKind: "ProcessArtifact",
                            Summary: $"修订计划已使 {items.Count} 个旧生产包失效：{string.Join("、", items.Select(item => item.PackageId))}。",
                            UserVisibleWhere: new[] { "创作工作流" },
                            VisibleInWorkflow: true,
                            VisibleInLibrary: false,
                            SourceEventType: productionEvent.EventType,
                            SourceEventId: productionEvent.Id,
                            Data: data),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _runtimeEvents.AppendAsync(
                    new CreateAgentRuntimeEventRequest(
                        RuntimeRunId: runtimeRunId,
                        UserId: request.UserId,
                        SessionId: sessionId,
                        ProjectId: request.ProjectId,
                        Type: "production_progress",
                        Message: $"修订计划已使 {items.Count} 个旧生产包失效，下一步可重建章节生产包。",
                        Data: data,
                        Stage: "packages_invalidated",
                        Status: "stale",
                        ArtifactType: "RevisionPlan",
                        ArtifactId: plan.Id,
                        DisplaySurface: AgentRuntimeEventSurface.Workflow,
                        DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new RevisionPlanPackageInvalidationResult
        {
            Success = items.Count > 0,
            Message = items.Count > 0
                ? $"已使 {items.Count} 个旧生产包失效：{string.Join("、", items.Select(item => item.PackageId))}。"
                : "没有找到需要失效的生产包。",
            RevisionPlanId = plan.Id,
            ProjectId = plan.ProjectId,
            Status = plan.Status,
            AffectedChapterIds = expandedAffectedChapterIds.ToList(),
            InvalidatedPackageIds = items.Select(item => item.PackageId).ToList(),
            Packages = items
        };
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
