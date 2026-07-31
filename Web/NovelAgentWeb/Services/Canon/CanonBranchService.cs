using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class CanonBranchService : ICanonBranchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CanonBranchService(NovelAgentDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
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
        CancellationToken cancellationToken = default)
    {
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
            "UserAcceptance",
            cancellationToken);
        var existing = await _db.CandidateAcceptances.FirstOrDefaultAsync(item =>
            item.UserId == userId &&
            item.CandidateChapterId == candidate.Id &&
            item.CandidateVersion == candidate.Version,
            cancellationToken);
        if (existing != null)
        {
            await EnsureAcceptanceArtifactAsync(existing, task, cancellationToken);
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
            DecidedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.CandidateAcceptances.Add(acceptance);
        await EnsureAcceptanceArtifactAsync(acceptance, task, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return acceptance;
    }

    private async Task<KernelTask> RequireWorkflowTaskAsync(
        string userId,
        string goalId,
        string branchId,
        string taskType,
        CancellationToken cancellationToken)
    {
        var graphId = await _db.TaskGraphVersions.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Goal 缺少可追踪的任务图，不能记录人工验收证据。");
        return await _db.KernelTasks.SingleOrDefaultAsync(task =>
            task.UserId == userId &&
            task.GoalId == goalId &&
            task.BranchId == branchId &&
            task.TaskGraphVersionId == graphId &&
            task.TaskType == taskType,
            cancellationToken) ?? throw new InvalidOperationException("Goal 缺少人工验收任务，不能记录验收证据。");
    }

    private async Task EnsureAcceptanceArtifactAsync(
        CandidateAcceptance acceptance,
        KernelTask task,
        CancellationToken cancellationToken)
    {
        var artifactId = $"acceptance-decision:{acceptance.Id}";
        var exists = await _db.KernelArtifacts.AnyAsync(item =>
            item.Id == artifactId &&
            item.UserId == acceptance.UserId &&
            item.GoalId == acceptance.GoalId,
            cancellationToken);
        if (!exists)
        {
            var contentJson = JsonSerializer.Serialize(new
            {
                acceptanceId = acceptance.Id,
                candidateChapterId = acceptance.CandidateChapterId,
                candidateVersion = acceptance.CandidateVersion,
                decision = acceptance.Decision,
                decidedByUserId = acceptance.DecidedByUserId,
                decidedAt = acceptance.CreatedAt
            }, JsonOptions);
            _db.KernelArtifacts.Add(new KernelArtifact
            {
                Id = artifactId,
                UserId = acceptance.UserId,
                ProjectId = acceptance.ProjectId,
                GoalId = acceptance.GoalId,
                TaskId = task.Id,
                BranchId = acceptance.BranchId,
                ArtifactType = "AcceptanceDecision",
                SchemaVersion = 1,
                ContentJson = contentJson,
                ContentHash = Sha256(contentJson),
                Status = "adopted",
                Authorship = "human",
                IsProtected = true,
                CausationId = acceptance.Id,
                CreatedAt = acceptance.CreatedAt
            });
        }
        task.OutputArtifactIdsJson = AppendArtifactId(task.OutputArtifactIdsJson, artifactId);
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string AppendArtifactId(string json, string artifactId)
    {
        var ids = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        if (!ids.Contains(artifactId, StringComparer.Ordinal))
            ids.Add(artifactId);
        return JsonSerializer.Serialize(ids);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
