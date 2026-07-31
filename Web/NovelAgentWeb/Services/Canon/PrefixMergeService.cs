using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class PrefixMergeService : IPrefixMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IContentDocumentService _documents;
    private readonly ICanonMergeConflictModelClient _conflictModel;

    public PrefixMergeService(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        IContentDocumentService documents,
        ICanonMergeConflictModelClient conflictModel)
    {
        _db = db;
        _currentUser = currentUser;
        _documents = documents;
        _conflictModel = conflictModel;
    }

    public async Task<BranchMergeRecord> MergeAcceptedPrefixAsync(
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        CanonMergeConflictException? conflict = null;
        var transactionCommitted = false;
        try
        {
        var branch = await _db.CanonBranches.SingleOrDefaultAsync(item =>
            item.Id == branchId && item.UserId == userId,
            cancellationToken) ?? throw new KeyNotFoundException("候选分支不存在。");
        if (branch.Status == "merged")
        {
            var completedRecord = await _db.BranchMergeRecords.AsNoTracking()
                .Where(record => record.UserId == userId && record.BranchId == branch.Id)
                .OrderByDescending(record => record.CreatedAt)
                .ThenByDescending(record => record.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("已合并分支缺少 BranchMergeRecord 证据。");
            var artifactExists = await _db.KernelArtifacts.AsNoTracking().AnyAsync(artifact =>
                artifact.Id == $"merge-record:{completedRecord.Id}" &&
                artifact.UserId == userId &&
                artifact.ProjectId == branch.ProjectId &&
                artifact.GoalId == branch.GoalId &&
                artifact.BranchId == branch.Id &&
                artifact.ArtifactType == "MergeRecord",
                cancellationToken);
            if (!artifactExists)
                throw new InvalidOperationException("已合并分支缺少 MergeRecord Artifact 证据。");
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            }
            return completedRecord;
        }
        if (branch.Status != "active")
            throw new KeyNotFoundException("活动候选分支不存在。");
        var priorEnd = await _db.BranchMergeRecords
            .Where(record => record.UserId == userId && record.BranchId == branch.Id)
            .Select(record => (int?)record.EndChapterNumber)
            .MaxAsync(cancellationToken);
        var nextChapter = priorEnd.HasValue ? priorEnd.Value + 1 : branch.StartChapterNumber;
        var candidates = await _db.CandidateChapters
            .Where(candidate =>
                candidate.UserId == userId &&
                candidate.BranchId == branch.Id &&
                candidate.ChapterNumber >= nextChapter)
            .OrderBy(candidate => candidate.ChapterNumber)
            .ThenByDescending(candidate => candidate.Version)
            .ToListAsync(cancellationToken);
        var latestByChapter = candidates
            .GroupBy(candidate => candidate.ChapterNumber)
            .ToDictionary(group => group.Key, group => group.First());
        var acceptances = await _db.CandidateAcceptances.AsNoTracking()
            .Where(acceptance => acceptance.UserId == userId && acceptance.BranchId == branch.Id && acceptance.Decision == "accepted")
            .ToListAsync(cancellationToken);
        var accepted = acceptances.ToDictionary(
            item => (item.CandidateChapterId, item.CandidateVersion),
            item => item);
        var prefix = new List<CandidateChapter>();
        for (var chapterNumber = nextChapter; chapterNumber <= branch.EndChapterNumber; chapterNumber++)
        {
            if (!latestByChapter.TryGetValue(chapterNumber, out var candidate) ||
                !accepted.ContainsKey((candidate.Id, candidate.Version)))
                break;
            prefix.Add(candidate);
        }
        if (prefix.Count == 0)
            throw new InvalidOperationException("没有可合并的连续已接受前缀。");

        var comparison = await CompareCanonAsync(branch, prefix, cancellationToken);
        if (comparison.RequiresDecision)
        {
            branch.Status = "needs_decision";
            branch.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            }
            conflict = new CanonMergeConflictException(
                branch.CanonBaselineVersion,
                comparison.CurrentCanonVersion,
                comparison.ConflictingChapterNumbers);
        }
        else
        {
            branch.CanonBaselineVersion = comparison.CurrentCanonVersion;
            foreach (var candidate in prefix)
                await MergeCandidateAsync(branch, candidate, cancellationToken);
            var recordId = Guid.NewGuid().ToString("N");
            var record = new BranchMergeRecord
            {
                Id = recordId,
                UserId = userId,
                ProjectId = branch.ProjectId,
                GoalId = branch.GoalId,
                BranchId = branch.Id,
                StartChapterNumber = prefix[0].ChapterNumber,
                EndChapterNumber = prefix[^1].ChapterNumber,
                CandidateVersionsJson = JsonSerializer.Serialize(prefix.ToDictionary(item => item.ChapterNumber, item => item.Version)),
                PreviousCanonVersion = branch.CanonBaselineVersion,
                NewCanonVersion = $"canon:{recordId}",
                MergedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.BranchMergeRecords.Add(record);
            var workflowTask = await RequireWorkflowTaskAsync(
                userId,
                branch.GoalId,
                branch.Id,
                "PrefixMerge",
                cancellationToken);
            var acceptanceArtifactIds = prefix
                .Select(candidate => $"acceptance-decision:{accepted[(candidate.Id, candidate.Version)].Id}")
                .ToArray();
            var acceptanceArtifactCount = await _db.KernelArtifacts.AsNoTracking().CountAsync(artifact =>
                artifact.UserId == userId &&
                artifact.ProjectId == branch.ProjectId &&
                artifact.GoalId == branch.GoalId &&
                artifact.BranchId == branch.Id &&
                artifact.ArtifactType == "AcceptanceDecision" &&
                acceptanceArtifactIds.Contains(artifact.Id),
                cancellationToken);
            if (acceptanceArtifactCount != acceptanceArtifactIds.Length)
                throw new InvalidOperationException("连续前缀缺少完整的人工验收 Artifact，不能合并正史。");
            var mergeContentJson = JsonSerializer.Serialize(new
            {
                mergeRecordId = record.Id,
                acceptanceDecisionArtifactIds = acceptanceArtifactIds,
                record.StartChapterNumber,
                record.EndChapterNumber,
                candidateVersions = prefix.ToDictionary(item => item.ChapterNumber, item => item.Version),
                record.PreviousCanonVersion,
                record.NewCanonVersion,
                record.MergedByUserId,
                mergedAt = record.CreatedAt
            }, JsonOptions);
            var mergeArtifact = new KernelArtifact
            {
                Id = $"merge-record:{record.Id}",
                UserId = userId,
                ProjectId = branch.ProjectId,
                GoalId = branch.GoalId,
                TaskId = workflowTask.Id,
                BranchId = branch.Id,
                ArtifactType = "MergeRecord",
                SchemaVersion = 1,
                ContentJson = mergeContentJson,
                ContentHash = Sha256(mergeContentJson),
                Status = "adopted",
                Authorship = "human",
                IsProtected = true,
                CausationId = record.Id,
                CreatedAt = record.CreatedAt
            };
            _db.KernelArtifacts.Add(mergeArtifact);
            workflowTask.OutputArtifactIdsJson = AppendArtifactId(
                workflowTask.OutputArtifactIdsJson,
                mergeArtifact.Id);
            workflowTask.UpdatedAt = record.CreatedAt;
            branch.CanonBaselineVersion = record.NewCanonVersion;
            branch.UpdatedAt = record.CreatedAt;
            if (record.EndChapterNumber == branch.EndChapterNumber)
            {
                branch.Status = "merged";
                branch.MergedAt = record.CreatedAt;
            }
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
                await _documents.PublishCommittedProjectChangesAsync(
                    userId,
                    branch.ProjectId,
                    cancellationToken);
            }
            return record;
        }
        }
        catch
        {
            if (transaction != null &&
                !transactionCommitted &&
                transaction.GetDbTransaction().Connection != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }

        throw conflict!;
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
            ?? throw new InvalidOperationException("Goal 缺少可追踪的任务图，不能记录正史合并证据。");
        return await _db.KernelTasks.SingleOrDefaultAsync(task =>
            task.UserId == userId &&
            task.GoalId == goalId &&
            task.BranchId == branchId &&
            task.TaskGraphVersionId == graphId &&
            task.TaskType == taskType,
            cancellationToken) ?? throw new InvalidOperationException("Goal 缺少前缀合并任务，不能记录正史合并证据。");
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

    private async Task<CanonComparison> CompareCanonAsync(
        CanonBranch branch,
        IReadOnlyList<CandidateChapter> prefix,
        CancellationToken cancellationToken)
    {
        var records = await _db.BranchMergeRecords.AsNoTracking()
            .Where(record => record.UserId == branch.UserId && record.ProjectId == branch.ProjectId)
            .OrderBy(record => record.CreatedAt)
            .ToListAsync(cancellationToken);
        var nextByPreviousVersion = records
            .GroupBy(record => record.PreviousCanonVersion)
            .ToDictionary(group => group.Key, group => group.ToList());
        var traversed = new List<BranchMergeRecord>();
        var currentVersion = branch.CanonBaselineVersion;
        var chainIsAmbiguous = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (nextByPreviousVersion.TryGetValue(currentVersion, out var nextRecords))
        {
            if (nextRecords.Count != 1 || !visited.Add(currentVersion))
            {
                chainIsAmbiguous = true;
                break;
            }
            var next = nextRecords[0];
            traversed.Add(next);
            currentVersion = next.NewCanonVersion;
        }

        if (records.Count > 0)
        {
            var heads = records
                .Select(record => record.NewCanonVersion)
                .Where(version => !nextByPreviousVersion.ContainsKey(version))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (heads.Count != 1 || !string.Equals(heads[0], currentVersion, StringComparison.Ordinal))
                chainIsAmbiguous = true;
        }

        var prefixNumbers = prefix.Select(candidate => candidate.ChapterNumber).ToHashSet();
        var conflictingNumbers = traversed
            .SelectMany(record => Enumerable.Range(
                record.StartChapterNumber,
                record.EndChapterNumber - record.StartChapterNumber + 1))
            .Where(prefixNumbers.Contains)
            .Distinct()
            .OrderBy(number => number)
            .ToArray();
        if (chainIsAmbiguous && conflictingNumbers.Length == 0)
            conflictingNumbers = prefixNumbers.OrderBy(number => number).ToArray();

        var candidateVersionToChapter = prefix.ToDictionary(
            candidate => $"candidate:{candidate.Id}",
            candidate => candidate.ChapterNumber,
            StringComparer.Ordinal);
        var candidateChanges = await _db.CanonChanges.AsNoTracking()
            .Where(change =>
                change.UserId == branch.UserId &&
                change.ProjectId == branch.ProjectId &&
                change.BranchId == branch.Id &&
                change.Status == "candidate" &&
                candidateVersionToChapter.Keys.Contains(change.ChapterVersionId))
            .OrderBy(change => change.CreatedAt)
            .ToListAsync(cancellationToken);
        var currentChanges = await _db.CanonChanges.AsNoTracking()
            .Where(change =>
                change.UserId == branch.UserId &&
                change.ProjectId == branch.ProjectId &&
                change.Status == "committed" &&
                change.BranchId != branch.Id &&
                change.CreatedAt > branch.CreatedAt)
            .OrderBy(change => change.CreatedAt)
            .ToListAsync(cancellationToken);
        if (candidateChanges.Count > 0 && currentChanges.Count > 0)
        {
            var currentChapterIds = currentChanges.Select(change => change.ChapterId).Distinct().ToArray();
            var currentChapterNumbers = await _db.Chapters.AsNoTracking()
                .Where(chapter => chapter.ProjectId == branch.ProjectId && currentChapterIds.Contains(chapter.Id))
                .ToDictionaryAsync(chapter => chapter.Id, chapter => chapter.ChapterNumber, StringComparer.Ordinal, cancellationToken);
            if (currentChanges.Any(change => !currentChapterNumbers.ContainsKey(change.ChapterId)))
                throw new InvalidOperationException("正式 CanonChange 引用的章节不存在，无法执行语义合并审查。");
            var candidateEvidence = candidateChanges.Select(change => new CanonMergeChangeEvidence(
                change.Id,
                candidateVersionToChapter[change.ChapterVersionId],
                change.ChapterId,
                change.ChangeType,
                change.Subject,
                change.ChangeJson)).ToArray();
            var currentEvidence = currentChanges.Select(change => new CanonMergeChangeEvidence(
                change.Id,
                currentChapterNumbers[change.ChapterId],
                change.ChapterId,
                change.ChangeType,
                change.Subject,
                change.ChangeJson)).ToArray();
            var semanticReview = await _conflictModel.ReviewAsync(
                new CanonMergeConflictReviewRequest(
                    branch.UserId,
                    branch.ProjectId,
                    branch.GoalId,
                    branch.CanonBaselineVersion,
                    currentVersion,
                    candidateEvidence,
                    currentEvidence),
                cancellationToken);
            ValidateSemanticReview(semanticReview, candidateEvidence, currentEvidence);
            if (semanticReview.RequiresDecision)
            {
                var semanticNumbers = candidateEvidence
                    .Where(change => semanticReview.ConflictingCandidateChangeIds.Contains(change.Id, StringComparer.Ordinal))
                    .Select(change => change.ChapterNumber);
                conflictingNumbers = conflictingNumbers
                    .Concat(semanticNumbers)
                    .Distinct()
                    .OrderBy(number => number)
                    .ToArray();
            }
        }

        return new CanonComparison(
            currentVersion,
            chainIsAmbiguous || conflictingNumbers.Length > 0,
            conflictingNumbers);
    }

    private static void ValidateSemanticReview(
        CanonMergeConflictReview review,
        IReadOnlyList<CanonMergeChangeEvidence> candidates,
        IReadOnlyList<CanonMergeChangeEvidence> current)
    {
        var candidateIds = candidates.Select(change => change.Id).ToHashSet(StringComparer.Ordinal);
        var currentIds = current.Select(change => change.Id).ToHashSet(StringComparer.Ordinal);
        if (review.ConflictingCandidateChangeIds.Any(id => !candidateIds.Contains(id)) ||
            review.ConflictingCurrentChangeIds.Any(id => !currentIds.Contains(id)))
        {
            throw new InvalidOperationException("正史合并冲突模型返回了输入中不存在的证据 ID。");
        }
        if (review.RequiresDecision &&
            (review.ConflictingCandidateChangeIds.Count == 0 || review.ConflictingCurrentChangeIds.Count == 0))
        {
            throw new InvalidOperationException("正史合并冲突模型要求人工决定，但没有提供双侧冲突证据。");
        }
    }

    private async Task MergeCandidateAsync(
        CanonBranch branch,
        CandidateChapter candidate,
        CancellationToken cancellationToken)
    {
        var artifact = await _db.KernelArtifacts.AsNoTracking().SingleAsync(item =>
            item.Id == candidate.CurrentArtifactId &&
            item.UserId == candidate.UserId &&
            item.BranchId == branch.Id,
            cancellationToken);
        var draft = JsonSerializer.Deserialize<ChapterDraftArtifact>(artifact.ContentJson)
            ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
        var chapter = await _db.Chapters.SingleAsync(item =>
            item.Id == candidate.ChapterId && item.ProjectId == branch.ProjectId,
            cancellationToken);
        var document = await _documents.SaveTextDeferredAsync(
            candidate.UserId,
            branch.ProjectId,
            "chapter",
            chapter.Id,
            "chapter_body",
            chapter.Title,
            draft.DraftContent,
            cancellationToken);
        var versionNumber = (await _db.ChapterVersions
            .Where(version => version.ChapterId == chapter.Id)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var chapterVersion = new ChapterVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = candidate.UserId,
            ProjectId = branch.ProjectId,
            ChapterId = chapter.Id,
            ContentDocumentId = document.Id,
            VersionNumber = versionNumber,
            Title = chapter.Title,
            WordCount = draft.DraftContent.Count(character => !char.IsWhiteSpace(character)),
            Status = "committed",
            AgentReviewJson = candidate.ReviewArtifactIdsJson,
            CreatedAt = DateTime.UtcNow
        };
        _db.ChapterVersions.Add(chapterVersion);
        var candidateVersionRef = $"candidate:{candidate.Id}";
        if (!string.IsNullOrWhiteSpace(candidate.ContinuitySummaryId))
        {
            var summary = await _db.ContinuitySummaries.SingleOrDefaultAsync(item =>
                item.Id == candidate.ContinuitySummaryId &&
                item.UserId == candidate.UserId &&
                item.ProjectId == candidate.ProjectId &&
                item.BranchId == candidate.BranchId &&
                item.ChapterId == candidate.ChapterId &&
                item.ChapterVersionId == candidateVersionRef &&
                item.Status == "candidate",
                cancellationToken) ?? throw new InvalidOperationException("候选连续性摘要不存在或作用域不匹配。");
            summary.ChapterVersionId = chapterVersion.Id;
            summary.Status = "committed";
        }
        var candidateCanonChanges = await _db.CanonChanges
            .Where(item =>
                item.UserId == candidate.UserId &&
                item.ProjectId == candidate.ProjectId &&
                item.BranchId == candidate.BranchId &&
                item.ChapterId == candidate.ChapterId &&
                item.ChapterVersionId == candidateVersionRef &&
                item.Status == "candidate")
            .ToListAsync(cancellationToken);
        foreach (var canonChange in candidateCanonChanges)
        {
            canonChange.ChapterVersionId = chapterVersion.Id;
            canonChange.Status = "committed";
        }
        chapter.CurrentDocumentId = document.Id;
        chapter.WordCount = chapterVersion.WordCount;
        chapter.Status = "committed";
        chapter.UpdatedAt = chapterVersion.CreatedAt;
        candidate.Status = "merged";
        _db.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = candidate.UserId,
            ProjectId = branch.ProjectId,
            EventType = "index_chapter_content",
            AggregateType = "chapter_version",
            AggregateId = chapterVersion.Id,
            IdempotencyKey = $"index-chapter-version:{chapterVersion.Id}",
            PayloadJson = JsonSerializer.Serialize(new { chapterVersionId = chapterVersion.Id }),
            Status = "pending",
            CreatedAt = chapterVersion.CreatedAt,
            UpdatedAt = chapterVersion.CreatedAt
        });
    }

    private sealed record CanonComparison(
        string CurrentCanonVersion,
        bool RequiresDecision,
        IReadOnlyList<int> ConflictingChapterNumbers)
    {
        public static CanonComparison NoConflict(string currentCanonVersion) =>
            new(currentCanonVersion, false, Array.Empty<int>());
    }
}
