using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public sealed class ToolInputArtifactResolver : IToolInputArtifactResolver
{
    private static readonly string[] StalePackageRecoveryActions =
    {
        "QueryNovelProductionState",
        "QueryRevisionPlans",
        "CreateRevisionPlan"
    };

    private static readonly string[] InvalidRevisionPlanRecoveryActions =
    {
        "QueryRevisionPlans",
        "CreateRevisionPlan",
        "InvalidateAffectedPackages"
    };

    private static readonly string[] BlockedOutboxRecoveryActions =
    {
        "QueryNovelProductionState",
        "QueryProductionOutbox",
        "RetryProductionOutbox"
    };

    private readonly NovelAgentDbContext _db;
    private readonly IProductionDependencyGuard _dependencyGuard;

    public ToolInputArtifactResolver(
        NovelAgentDbContext db,
        IProductionDependencyGuard? dependencyGuard = null)
    {
        _db = db;
        _dependencyGuard = dependencyGuard ?? new ProductionDependencyGuard(db);
    }

    public async Task<ToolInputArtifactResolution> ResolveAsync(
        ToolInputArtifactResolutionRequest request,
        CancellationToken ct = default)
    {
        if (!RequiresChapterPlanRun(request.Tool))
            return ToolInputArtifactResolution.Allow;

        if (!string.Equals(request.Tool.Name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
            return ToolInputArtifactResolution.Allow;

        var runId = FirstNonEmpty(Arg(request.Call, "runId"), request.Session.ActiveRunId);
        if (string.IsNullOrWhiteSpace(runId))
        {
            return MissingChapterPlanRun(
                "ProduceChapter 需要已规划章节 Run。模型应先调用 PlanChapter 并选择章节候选，或显式传入已有 runId。");
        }

        var run = FindRunInDocument(request.Bible, runId);
        if (run == null && request.LatestStoryBibleLoader != null)
        {
            try
            {
                var latest = await request.LatestStoryBibleLoader(ct).ConfigureAwait(false);
                run = FindRunInDocument(latest, runId);
            }
            catch (InvalidOperationException)
            {
                run = null;
            }
        }

        if (run == null)
        {
            return MissingChapterPlanRun(
                $"ProduceChapter 找不到章节规划 Run：{runId}。不能在没有章节生产计划的情况下直接生成正文。",
                runId);
        }

        if (run.Intent != NovelAgentIntent.PlanChapter &&
            string.IsNullOrWhiteSpace(run.TargetChapterId))
        {
            return MissingChapterPlanRun(
                $"Run {runId} 不是可生产章节计划，缺少 targetChapterId。",
                runId);
        }

        var outboxBlock = await ResolvePreviousChapterOutboxStateAsync(request, run, runId, ct)
            .ConfigureAwait(false);
        if (outboxBlock.BlocksExecution)
            return outboxBlock;

        return await ResolveProduceChapterPackageStateAsync(request, run, runId, ct).ConfigureAwait(false);
    }

    private async Task<ToolInputArtifactResolution> ResolvePreviousChapterOutboxStateAsync(
        ToolInputArtifactResolutionRequest request,
        NovelAgentRun run,
        string runId,
        CancellationToken ct)
    {
        var projectId = FirstNonEmpty(request.Session.ActiveProjectId, request.WorkspaceProjectId);
        var targetChapterNumber = ExtractTrailingNumber(run.TargetChapterId);
        if (string.IsNullOrWhiteSpace(projectId) || targetChapterNumber <= 1)
            return ToolInputArtifactResolution.Allow;

        var blocks = await _dependencyGuard.FindBlocksAsync(
                new ProductionDependencyGuardRequest(
                    request.Session.UserId,
                    projectId,
                    targetChapterNumber),
                ct)
            .ConfigureAwait(false);
        var block = blocks.FirstOrDefault();
        if (block == null)
            return ToolInputArtifactResolution.Allow;

        var artifactId = block.OutboxEventIds.FirstOrDefault() ?? block.PreviousChapterId;
        var status = string.Join(",", block.Statuses.Where(static item => !string.IsNullOrWhiteSpace(item)));
        if (string.IsNullOrWhiteSpace(status))
            status = "blocked";
        var reason = $"上一章提交后后台沉淀尚未完成，暂不能构建下一章生产包：{block.Summary}";

        return new ToolInputArtifactResolution
        {
            BlocksExecution = true,
            MissingPrerequisite = "previous_chapter_post_commit_outbox",
            FailureCode = "TOOL_INPUT_ARTIFACT_BLOCKED",
            Reason = reason,
            RunId = runId,
            InputArtifacts = new List<ToolInputArtifactState>
            {
                new(
                    "chapter_plan_run",
                    "available",
                    runId,
                    $"章节规划 Run 可用：{runId}",
                    false,
                    Array.Empty<string>()),
                new(
                    "post_commit_outbox",
                    status,
                    artifactId,
                    reason,
                    true,
                    BlockedOutboxRecoveryActions)
            }
        };
    }

    private async Task<ToolInputArtifactResolution> ResolveProduceChapterPackageStateAsync(
        ToolInputArtifactResolutionRequest request,
        NovelAgentRun run,
        string runId,
        CancellationToken ct)
    {
        var revisionPlanId = FirstNonEmpty(
            Arg(request.Call, "revisionPlanId"),
            Arg(request.Call, "sourceRevisionPlanId"),
            Arg(request.Call, "planId"));
        if (!string.IsNullOrWhiteSpace(revisionPlanId))
        {
            var revisionPlanResolution = await ResolveRevisionPlanStateAsync(
                    request,
                    run,
                    runId,
                    revisionPlanId,
                    ct)
                .ConfigureAwait(false);
            if (revisionPlanResolution.BlocksExecution)
                return revisionPlanResolution;

            return ToolInputArtifactResolution.Allow;
        }

        var projectId = FirstNonEmpty(request.Session.ActiveProjectId, request.WorkspaceProjectId);
        var stalePackage = await _db.TianmingPackages
            .AsNoTracking()
            .Where(package =>
                package.UserId == request.Session.UserId &&
                package.ProjectId == projectId &&
                package.RuntimeRunId == runId &&
                package.Status == "stale")
            .OrderByDescending(package => package.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (stalePackage == null && !string.IsNullOrWhiteSpace(run.TargetChapterId))
        {
            stalePackage = await _db.TianmingPackages
                .AsNoTracking()
                .Where(package =>
                    package.UserId == request.Session.UserId &&
                    package.ProjectId == projectId &&
                    package.ChapterId == run.TargetChapterId &&
                    package.Status == "stale")
                .OrderByDescending(package => package.UpdatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        if (stalePackage == null)
            return ToolInputArtifactResolution.Allow;

        return new ToolInputArtifactResolution
        {
            BlocksExecution = true,
            MissingPrerequisite = "revision_plan_optional",
            FailureCode = "TOOL_INPUT_ARTIFACT_STALE",
            Reason = $"ProduceChapter 发现过期生产包 {stalePackage.Id}，但本次调用没有 revisionPlanId。请先查询或创建修订计划，再用 revisionPlanId 重建生产包。",
            RunId = runId,
            InputArtifacts = new List<ToolInputArtifactState>
            {
                new(
                    "chapter_plan_run",
                    "available",
                    runId,
                    $"章节规划 Run 可用：{runId}",
                    false,
                    Array.Empty<string>()),
                new(
                    "tianming_package",
                    "stale",
                    stalePackage.Id,
                    $"生产包 {stalePackage.Id} 已过期，需要 revisionPlanId 才能重建。",
                    true,
                    StalePackageRecoveryActions)
            }
        };
    }

    private async Task<ToolInputArtifactResolution> ResolveRevisionPlanStateAsync(
        ToolInputArtifactResolutionRequest request,
        NovelAgentRun run,
        string runId,
        string revisionPlanId,
        CancellationToken ct)
    {
        var projectId = FirstNonEmpty(request.Session.ActiveProjectId, request.WorkspaceProjectId);
        var plan = await _db.RevisionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                    item.UserId == request.Session.UserId &&
                    item.Id == revisionPlanId,
                ct)
            .ConfigureAwait(false);

        if (plan == null)
        {
            return InvalidRevisionPlan(
                runId,
                revisionPlanId,
                "missing",
                $"ProduceChapter 指定的 RevisionPlan 不存在或不属于当前用户：{revisionPlanId}。请先查询或创建当前项目可用的修订计划。");
        }

        if (!string.Equals(plan.ProjectId, projectId, StringComparison.Ordinal))
        {
            return InvalidRevisionPlan(
                runId,
                revisionPlanId,
                "wrong_project",
                $"RevisionPlan {revisionPlanId} 不属于当前项目 {projectId}，不能用于本次章节生产。");
        }

        if (!string.Equals(plan.Status, "ready_for_rebuild", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidRevisionPlan(
                runId,
                revisionPlanId,
                plan.Status,
                $"RevisionPlan {revisionPlanId} 当前状态为 {plan.Status}，需要 ready_for_rebuild 后才能用于 ProduceChapter 重建生产包。");
        }

        var targetChapterId = FirstNonEmpty(run.TargetChapterId);
        if (!RevisionPlanTargetsChapter(plan, targetChapterId))
        {
            return InvalidRevisionPlan(
                runId,
                revisionPlanId,
                "chapter_mismatch",
                $"RevisionPlan {revisionPlanId} 不匹配当前目标章节 {targetChapterId}，不能用于本次 ProduceChapter。");
        }

        return ToolInputArtifactResolution.Allow;
    }

    private static ToolInputArtifactResolution InvalidRevisionPlan(
        string runId,
        string revisionPlanId,
        string status,
        string reason) => new()
    {
        BlocksExecution = true,
        MissingPrerequisite = "revision_plan",
        FailureCode = "TOOL_INPUT_ARTIFACT_INVALID",
        Reason = reason,
        RunId = runId,
        InputArtifacts = new List<ToolInputArtifactState>
        {
            new(
                "chapter_plan_run",
                "available",
                runId,
                $"章节规划 Run 可用：{runId}",
                false,
                Array.Empty<string>()),
            new(
                "revision_plan",
                status,
                revisionPlanId,
                reason,
                true,
                InvalidRevisionPlanRecoveryActions)
        }
    };

    private static bool RevisionPlanTargetsChapter(RevisionPlan plan, string targetChapterId)
    {
        if (string.IsNullOrWhiteSpace(targetChapterId))
            return true;

        if (string.IsNullOrWhiteSpace(plan.TargetChapterId))
            return true;

        if (string.Equals(plan.TargetChapterId, targetChapterId, StringComparison.OrdinalIgnoreCase))
            return true;

        return JsonArrayContains(plan.AffectedChapterIdsJson, targetChapterId);
    }

    private static bool JsonArrayContains(string? json, string expected)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(expected))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any(item =>
                       item.ValueKind == JsonValueKind.String &&
                       string.Equals(item.GetString(), expected, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return json.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ToolInputArtifactResolution MissingChapterPlanRun(string reason, string runId = "") => new()
    {
        BlocksExecution = true,
        MissingPrerequisite = "chapter_plan_run",
        FailureCode = "TOOL_INPUT_ARTIFACT_MISSING",
        Reason = reason,
        RunId = runId
    };

    private static bool RequiresChapterPlanRun(AgentToolDefinition tool) =>
        tool.Semantic.InputArtifacts.Contains("chapter_plan_run", StringComparer.OrdinalIgnoreCase);

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static NovelAgentRun? FindRunInDocument(StoryBibleDocument? bible, string runId)
    {
        if (bible?.AgentRuns == null || string.IsNullOrWhiteSpace(runId))
            return null;

        return bible.AgentRuns.FirstOrDefault(run =>
            string.Equals(run.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
