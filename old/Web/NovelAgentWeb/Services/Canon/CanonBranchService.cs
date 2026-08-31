using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using Tianming.NovelAgent.Application.Ports;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class CanonBranchService : ICanonBranchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public CanonBranchService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _currentUser = currentUser;
        _controlPlane = controlPlane;
    }

    public CanonBranchService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser)
        : this(db, currentUser, LegacyControlPlaneCommands.Unconfigured)
    {
    }

    public async Task<CanonBranch> CreateAsync(
        string goalId,
        int startChapterNumber,
        int endChapterNumber,
        CancellationToken cancellationToken = default)
    {
        if (startChapterNumber <= 0 || endChapterNumber < startChapterNumber)
            throw new ArgumentOutOfRangeException(nameof(startChapterNumber), "候选分支章节范围无效。");
        var userId = _currentUser.GetUserId();
        var goal = await _db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == goalId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        var activeExists = await _db.CanonBranches.AsNoTracking().AnyAsync(item =>
            item.UserId == userId && item.GoalId == goalId && item.Status == "active",
            cancellationToken);
        if (activeExists)
            throw new InvalidOperationException("Goal 已有活动候选分支。");

        var branch = new CanonBranch
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            CanonBaselineVersion = goal.CanonBaselineVersion,
            Status = "active",
            StartChapterNumber = startChapterNumber,
            EndChapterNumber = endChapterNumber,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.CanonBranches.Add(branch);
        await _db.SaveChangesAsync(cancellationToken);
        return branch;
    }

    public async Task<CandidateChapter> AddCandidateAsync(
        string branchId,
        string chapterId,
        int chapterNumber,
        string artifactId,
        string? dependsOnCandidateChapterId,
        string authorship,
        bool isProtected,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var branch = await _db.CanonBranches.SingleOrDefaultAsync(item =>
            item.Id == branchId && item.UserId == userId && item.Status == "active",
            cancellationToken) ?? throw new KeyNotFoundException("活动候选分支不存在。");
        if (chapterNumber < branch.StartChapterNumber || chapterNumber > branch.EndChapterNumber)
            throw new InvalidOperationException("候选章节超出分支范围。");
        var artifactExists = await _db.KernelArtifacts.AsNoTracking().AnyAsync(artifact =>
            artifact.Id == artifactId &&
            artifact.UserId == userId &&
            artifact.ProjectId == branch.ProjectId &&
            artifact.GoalId == branch.GoalId &&
            artifact.BranchId == branch.Id &&
            artifact.ArtifactType == "CandidateChapterDraft",
            cancellationToken);
        if (!artifactExists)
            throw new InvalidOperationException("候选正文 Artifact 不存在或作用域不匹配。");
        if (authorship is not ("agent" or "human"))
            throw new InvalidOperationException("候选正文 authorship 只能是 agent 或 human。");
        if (isProtected && authorship != "human")
            throw new InvalidOperationException("只有人工候选版本可以设置保护。");
        var latestCandidate = await _db.CandidateChapters.AsNoTracking()
            .Where(candidate =>
                candidate.UserId == userId &&
                candidate.BranchId == branch.Id &&
                candidate.ChapterNumber == chapterNumber)
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestCandidate is { Authorship: "human", IsProtected: true } && authorship == "agent")
            throw new InvalidOperationException("Agent 不能覆盖最新的人工保护候选版本。");
        if (dependsOnCandidateChapterId != null)
        {
            var validDependency = await _db.CandidateChapters.AsNoTracking().AnyAsync(candidate =>
                candidate.Id == dependsOnCandidateChapterId &&
                candidate.UserId == userId &&
                candidate.BranchId == branch.Id &&
                candidate.ChapterNumber < chapterNumber,
                cancellationToken);
            if (!validDependency)
                throw new InvalidOperationException("候选章节依赖必须来自同分支的前章。");
        }

        var version = (await _db.CandidateChapters
            .Where(candidate =>
                candidate.UserId == userId &&
                candidate.BranchId == branch.Id &&
                candidate.ChapterNumber == chapterNumber)
            .Select(candidate => (int?)candidate.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var candidateChapter = new CandidateChapter
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = branch.ProjectId,
            GoalId = branch.GoalId,
            BranchId = branch.Id,
            ChapterId = chapterId,
            ChapterNumber = chapterNumber,
            Version = version,
            CurrentArtifactId = artifactId,
            DependsOnCandidateChapterId = dependsOnCandidateChapterId,
            Status = "candidate",
            Authorship = authorship,
            IsProtected = isProtected,
            CreatedAt = DateTime.UtcNow
        };
        _db.CandidateChapters.Add(candidateChapter);
        await _db.SaveChangesAsync(cancellationToken);
        return candidateChapter;
    }

    public async Task<CandidateAcceptance> AcceptAsync(
        string candidateChapterId,
        int candidateVersion,
        CancellationToken cancellationToken = default) =>
        await AcceptAsync(candidateChapterId, candidateVersion, "human", cancellationToken);

    public async Task<CandidateAcceptance> AcceptAsync(
        string candidateChapterId,
        int candidateVersion,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (actor is not ("human" or "agent"))
            throw new ArgumentException("验收主体只能是 human 或 agent。", nameof(actor));
        var userId = _currentUser.GetUserId();
        var candidate = await _db.CandidateChapters.SingleOrDefaultAsync(item =>
            item.Id == candidateChapterId &&
            item.UserId == userId &&
            item.Version == candidateVersion,
            cancellationToken) ?? throw new KeyNotFoundException("候选章节版本不存在。");
        var task = await RequireWorkflowTaskAsync(
            userId,
            candidate.GoalId,
            candidate.BranchId,
            BookProductionWorkflow.AcceptanceGate,
            actor,
            cancellationToken);
        var existing = await _db.CandidateAcceptances.FirstOrDefaultAsync(item =>
            item.UserId == userId &&
            item.CandidateChapterId == candidate.Id &&
            item.CandidateVersion == candidate.Version,
            cancellationToken);
        if (existing != null)
        {
            await EnsureAcceptanceArtifactAsync(existing, task, actor, cancellationToken);
            return existing;
        }

        var acceptance = new CandidateAcceptance
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = candidate.ProjectId,
            GoalId = candidate.GoalId,
            BranchId = candidate.BranchId,
            CandidateChapterId = candidate.Id,
            CandidateVersion = candidate.Version,
            Decision = "accepted",
            DecidedByUserId = actor == "human" ? userId : "agent",
            CreatedAt = DateTime.UtcNow
        };
        _db.CandidateAcceptances.Add(acceptance);
        await _db.SaveChangesAsync(cancellationToken);
        await EnsureAcceptanceArtifactAsync(acceptance, task, actor, cancellationToken);
        return acceptance;
    }

    private async Task<KernelTask> RequireWorkflowTaskAsync(
        string userId,
        string goalId,
        string branchId,
        string taskType,
        string actor,
        CancellationToken cancellationToken)
    {
        var batch = await _db.ProductionBatches.AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.GoalId == goalId &&
                item.CanonBranchId == branchId &&
                item.TaskGraphVersionId != null)
            .OrderByDescending(item => item.BatchNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("候选分支未绑定生产批次，不能记录验收证据。");
        var expectedActor = actor == "agent"
            ? BookProductionWorkflow.AgentPolicyActor
            : BookProductionWorkflow.UserActor;
        if (batch.AcceptanceActor != expectedActor)
            throw new InvalidOperationException("候选章节验收主体与批次执行策略不匹配。");
        if (batch.Status is not ("running" or "accepting"))
            throw new InvalidOperationException("当前生产批次不在可验收状态。");
        return await _db.KernelTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(task =>
            task.UserId == userId &&
            task.GoalId == goalId &&
            task.BranchId == branchId &&
            task.TaskGraphVersionId == batch.TaskGraphVersionId &&
            task.TaskType == taskType,
            cancellationToken) ?? throw new InvalidOperationException("Goal 缺少人工验收任务，不能记录验收证据。");
    }

    private async Task EnsureAcceptanceArtifactAsync(
        CandidateAcceptance acceptance,
        KernelTask task,
        string actor,
        CancellationToken cancellationToken)
    {
        var artifactId = $"acceptance-decision:{acceptance.Id}";
        var contentJson = JsonSerializer.Serialize(new
        {
            acceptanceId = acceptance.Id,
            candidateChapterId = acceptance.CandidateChapterId,
            candidateVersion = acceptance.CandidateVersion,
            decision = acceptance.Decision,
            decidedByUserId = acceptance.DecidedByUserId,
            decidedAt = acceptance.CreatedAt
        }, JsonOptions);
        await _controlPlane.CreateArtifactAsync(new LegacyArtifactCommand(
            acceptance.UserId,
            acceptance.ProjectId,
            acceptance.GoalId,
            task.Id,
            artifactId,
            acceptance.BranchId,
            "AcceptanceDecision",
            1,
            contentJson,
            Sha256(contentJson),
            "adopted",
            actor,
            true,
            null,
            acceptance.Id,
            acceptance.CreatedAt,
            AttachToTaskOutput: true), cancellationToken);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
