using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.Kernels;

namespace TM.Web.NovelAgentWeb.Services.DomainEvents;

public sealed class DomainReducer : IDomainReducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly DomainContractValidator _validator;
    private readonly IKernelArtifactStore _artifacts;

    public DomainReducer(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        DomainContractValidator validator,
        IKernelArtifactStore artifacts)
    {
        _db = db;
        _currentUser = currentUser;
        _validator = validator;
        _artifacts = artifacts;
    }

    public async Task<DomainAdoptionResult> ApplyAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifacts,
        IReadOnlyList<DomainEventProposal> events,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await LockAndValidateExecutionScopeAsync(claim, cancellationToken);
            await _validator.ValidateAsync(claim, artifacts, events, cancellationToken);
            if (_db.Database.IsRelational() && _db.Database.GetDbConnection() is NpgsqlConnection)
            {
                var aggregates = events
                    .Select(proposal => new { proposal.AggregateType, proposal.AggregateId })
                    .Distinct()
                    .OrderBy(item => item.AggregateType, StringComparer.Ordinal)
                    .ThenBy(item => item.AggregateId, StringComparer.Ordinal);
                foreach (var aggregate in aggregates)
                {
                    var lockKey = string.Join('\u001f', claim.UserId, aggregate.AggregateType, aggregate.AggregateId);
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                        cancellationToken);
                }
            }

            var artifactIds = await _artifacts.AddOrReuseAsync(claim, artifacts, cancellationToken);
            var eventIds = new List<string>(events.Count);
            var pendingVersions = new Dictionary<(string Type, string Id), long>();
            foreach (var proposal in events)
            {
                var existing = await _db.DomainEvents.AsNoTracking().FirstOrDefaultAsync(domainEvent =>
                    domainEvent.UserId == claim.UserId &&
                    domainEvent.IdempotencyKey == proposal.IdempotencyKey,
                    cancellationToken);
                if (existing != null)
                {
                    eventIds.Add(existing.Id);
                    continue;
                }

                var aggregate = (proposal.AggregateType, proposal.AggregateId);
                if (!pendingVersions.TryGetValue(aggregate, out var currentVersion))
                {
                    currentVersion = await _db.DomainEvents
                        .Where(domainEvent =>
                            domainEvent.UserId == claim.UserId &&
                            domainEvent.AggregateType == proposal.AggregateType &&
                            domainEvent.AggregateId == proposal.AggregateId)
                        .Select(domainEvent => (long?)domainEvent.AggregateVersion)
                        .MaxAsync(cancellationToken) ?? 0;
                }
                var aggregateVersion = currentVersion + 1;
                pendingVersions[aggregate] = aggregateVersion;

                var resolvedArtifactRefs = proposal.ArtifactRefs
                    .Select(reference => ResolveArtifactReference(reference, artifactIds))
                    .ToArray();
                var domainEvent = new DomainEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    GoalId = claim.GoalId,
                    TaskId = claim.TaskId,
                    BranchId = claim.BranchId,
                    AggregateType = proposal.AggregateType,
                    AggregateId = proposal.AggregateId,
                    AggregateVersion = aggregateVersion,
                    EventType = proposal.EventType,
                    ArtifactRefsJson = JsonSerializer.Serialize(resolvedArtifactRefs),
                    EvidenceRefsJson = JsonSerializer.Serialize(proposal.EvidenceRefs),
                    CorrelationId = claim.GoalId,
                    IdempotencyKey = proposal.IdempotencyKey,
                    PayloadJson = proposal.PayloadJson,
                    CreatedAt = DateTime.UtcNow
                };
                _db.DomainEvents.Add(domainEvent);
                _db.OutboxEvents.Add(new OutboxEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    EventType = "project_domain_event",
                    AggregateType = "domain_event",
                    AggregateId = domainEvent.Id,
                    IdempotencyKey = $"domain-event:{proposal.IdempotencyKey}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        domainEventId = domainEvent.Id,
                        domainEvent.EventType,
                        domainEvent.AggregateType,
                        domainEvent.AggregateId
                    }),
                    Status = "pending",
                    CreatedAt = domainEvent.CreatedAt,
                    UpdatedAt = domainEvent.CreatedAt
                });
                if (proposal.EventType == "CandidateLightweightCanonProposed")
                {
                    await MaterializeCandidateAsync(
                        claim,
                        artifacts,
                        artifactIds,
                        proposal,
                        resolvedArtifactRefs,
                        cancellationToken);
                }
                eventIds.Add(domainEvent.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return new DomainAdoptionResult(artifactIds, eventIds);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    private async Task LockAndValidateExecutionScopeAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken)
    {
        var postgres = _db.Database.IsRelational() &&
            _db.Database.GetDbConnection() is NpgsqlConnection;
        var goalQuery = postgres
            ? _db.CreativeGoals.FromSqlInterpolated($"""
                SELECT * FROM creative_goals
                WHERE id = {claim.GoalId} AND user_id = {claim.UserId}
                FOR UPDATE
                """)
            : _db.CreativeGoals.AsQueryable();
        var goal = await goalQuery.SingleOrDefaultAsync(item =>
            item.Id == claim.GoalId &&
            item.UserId == claim.UserId &&
            item.ProjectId == claim.ProjectId,
            cancellationToken);
        if (goal == null || goal.Status is not ("committed" or "running" or "resumed"))
            throw new InvalidOperationException("Goal 已不再处于可采纳结果的执行状态。");

        var graphQuery = postgres
            ? _db.TaskGraphVersions.FromSqlInterpolated($"""
                SELECT * FROM task_graph_versions
                WHERE id = {claim.TaskGraphVersionId} AND user_id = {claim.UserId}
                FOR UPDATE
                """)
            : _db.TaskGraphVersions.AsQueryable();
        var graph = await graphQuery.SingleOrDefaultAsync(item =>
            item.Id == claim.TaskGraphVersionId && item.UserId == claim.UserId,
            cancellationToken);
        if (graph == null ||
            graph.ProjectId != claim.ProjectId ||
            graph.GoalId != claim.GoalId ||
            graph.Status != "active")
            throw new InvalidOperationException("任务图已不再是 Goal 的活动任务图。");

        var taskQuery = postgres
            ? _db.KernelTasks.FromSqlInterpolated($"""
                SELECT * FROM kernel_tasks
                WHERE id = {claim.TaskId} AND user_id = {claim.UserId}
                FOR UPDATE
                """)
            : _db.KernelTasks.AsQueryable();
        var task = await taskQuery.SingleOrDefaultAsync(item =>
            item.Id == claim.TaskId && item.UserId == claim.UserId,
            cancellationToken);
        if (task == null ||
            task.ProjectId != claim.ProjectId ||
            task.GoalId != claim.GoalId ||
            task.TaskGraphVersionId != claim.TaskGraphVersionId ||
            task.Status != "running" ||
            task.LeaseOwner != claim.LeaseOwner ||
            task.LeaseExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("任务作用域、状态或 lease 已失效。");

        if (claim.BranchId == null)
            return;
        var branchQuery = postgres
            ? _db.CanonBranches.FromSqlInterpolated($"""
                SELECT * FROM canon_branches
                WHERE id = {claim.BranchId} AND user_id = {claim.UserId}
                FOR UPDATE
                """)
            : _db.CanonBranches.AsQueryable();
        var branch = await branchQuery.SingleOrDefaultAsync(item =>
            item.Id == claim.BranchId && item.UserId == claim.UserId,
            cancellationToken);
        if (branch == null ||
            branch.ProjectId != claim.ProjectId ||
            branch.GoalId != claim.GoalId ||
            branch.Status != "active")
            throw new InvalidOperationException("候选分支已不再处于活动状态。");
    }

    private static string ResolveArtifactReference(string reference, IReadOnlyList<string> artifactIds)
    {
        const string prefix = "@artifact:";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            return reference;
        if (!int.TryParse(reference[prefix.Length..], out var index) || index < 0 || index >= artifactIds.Count)
            throw new InvalidOperationException($"Artifact 占位引用无效：{reference}");
        return artifactIds[index];
    }

    private async Task MaterializeCandidateAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> artifactProposals,
        IReadOnlyList<string> artifactIds,
        DomainEventProposal domainEvent,
        IReadOnlyList<string> resolvedArtifactRefs,
        CancellationToken cancellationToken)
    {
        var branchId = claim.BranchId
            ?? throw new InvalidOperationException("候选轻量正史事件缺少 CanonBranch。");
        var summaryArtifactId = resolvedArtifactRefs.SingleOrDefault()
            ?? throw new InvalidOperationException("候选轻量正史事件必须引用一个 ContinuitySummary Artifact。");
        var summaryIndex = artifactIds
            .Select((id, index) => new { id, index })
            .SingleOrDefault(item => item.id == summaryArtifactId)?.index
            ?? throw new InvalidOperationException("候选轻量正史 Artifact 未包含在当前写入中。");
        var summaryProposal = artifactProposals[summaryIndex];
        if (summaryProposal.ArtifactType != "ContinuitySummary")
            throw new InvalidOperationException("候选轻量正史事件引用了错误的 Artifact 类型。");
        var canon = JsonSerializer.Deserialize<LightweightCanonArtifact>(summaryProposal.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("ContinuitySummary Artifact 无法解析。");
        var reviewedArtifact = await _db.KernelArtifacts.AsNoTracking().SingleOrDefaultAsync(artifact =>
            artifact.Id == canon.BodyArtifactId &&
            artifact.UserId == claim.UserId &&
            artifact.ProjectId == claim.ProjectId &&
            artifact.GoalId == claim.GoalId &&
            artifact.BranchId == branchId &&
            artifact.ArtifactType == "ReviewedCandidateChapter",
            cancellationToken) ?? throw new InvalidOperationException("已审候选正文不存在或作用域不匹配。");
        var draft = JsonSerializer.Deserialize<TM.Services.Framework.AI.NovelAgent.Models.ChapterDraftArtifact>(
            reviewedArtifact.ContentJson,
            JsonOptions) ?? throw new InvalidOperationException("已审候选正文无法解析。");
        var chapter = await _db.Chapters.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == draft.ChapterId && item.ProjectId == claim.ProjectId,
            cancellationToken) ?? throw new InvalidOperationException("候选正文对应章节不存在。");
        var existing = await _db.CandidateChapters.SingleOrDefaultAsync(item =>
            item.UserId == claim.UserId &&
            item.BranchId == branchId &&
            item.CurrentArtifactId == reviewedArtifact.Id,
            cancellationToken);
        if (existing != null)
            return;

        var version = (await _db.CandidateChapters
            .Where(item =>
                item.UserId == claim.UserId &&
                item.BranchId == branchId &&
                item.ChapterNumber == chapter.ChapterNumber)
            .Select(item => (int?)item.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var priorCandidateId = await _db.CandidateChapters.AsNoTracking()
            .Where(item =>
                item.UserId == claim.UserId &&
                item.BranchId == branchId &&
                item.ChapterNumber < chapter.ChapterNumber)
            .OrderByDescending(item => item.ChapterNumber)
            .ThenByDescending(item => item.Version)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        string taskPrefix;
        string contextTaskId;
        if (reviewedArtifact.TaskId.EndsWith("directed-rework", StringComparison.Ordinal))
        {
            taskPrefix = reviewedArtifact.TaskId[..^"directed-rework".Length];
            contextTaskId = $"{taskPrefix}context";
        }
        else if (reviewedArtifact.TaskId.EndsWith("adopt", StringComparison.Ordinal))
        {
            taskPrefix = reviewedArtifact.TaskId[..^"adopt".Length];
            var adoptInputsJson = await _db.KernelTasks.AsNoTracking()
                .Where(task => task.Id == reviewedArtifact.TaskId && task.UserId == claim.UserId)
                .Select(task => task.InputArtifactIdsJson)
                .SingleAsync(cancellationToken);
            var adoptInputIds = JsonSerializer.Deserialize<string[]>(adoptInputsJson, JsonOptions) ?? [];
            contextTaskId = await _db.KernelArtifacts.AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    adoptInputIds.Contains(artifact.Id) &&
                    artifact.ArtifactType == "ChapterContextContract")
                .Select(artifact => artifact.TaskId)
                .SingleAsync(cancellationToken);
            var intentJson = await _db.KernelArtifacts.AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    artifact.ProjectId == claim.ProjectId &&
                    artifact.GoalId == claim.GoalId &&
                    artifact.BranchId == branchId &&
                    artifact.TaskId == $"{taskPrefix}draft" &&
                    artifact.ArtifactType == "ReworkIntent")
                .Select(artifact => artifact.ContentJson)
                .SingleAsync(cancellationToken);
            var intentContract = JsonSerializer.Deserialize<ReworkIntentArtifactContract>(intentJson, JsonOptions)
                ?? throw new InvalidOperationException("ReworkIntent Artifact 无法解析。");
            var reworkIntent = await _db.ReworkIntents.SingleAsync(item =>
                item.Id == intentContract.IntentId &&
                item.UserId == claim.UserId &&
                item.GoalId == claim.GoalId &&
                item.BranchId == branchId,
                cancellationToken);
            reworkIntent.Status = "resolved";
            reworkIntent.ImpactAssessmentJson = JsonSerializer.Serialize(new
            {
                problemResolved = true,
                adoptedCandidateArtifactId = reviewedArtifact.Id
            });
            reworkIntent.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            throw new InvalidOperationException("已审候选正文任务 ID 不符合编译器协议。");
        }
        var reviewTaskIds = new[]
        {
            $"{taskPrefix}continuity-review",
            $"{taskPrefix}literary-review"
        };
        var reviewArtifactIds = await _db.KernelArtifacts.AsNoTracking()
            .Where(artifact =>
                artifact.UserId == claim.UserId &&
                artifact.ProjectId == claim.ProjectId &&
                artifact.GoalId == claim.GoalId &&
                artifact.BranchId == branchId &&
                reviewTaskIds.Contains(artifact.TaskId) &&
                (artifact.ArtifactType == "ContinuityReview" || artifact.ArtifactType == "LiteraryReview"))
            .OrderBy(artifact => artifact.ArtifactType)
            .Select(artifact => artifact.Id)
            .ToListAsync(cancellationToken);
        if (reviewArtifactIds.Count != 2)
            throw new InvalidOperationException("候选章节缺少独立连续性或审美审稿证据。");

        var candidate = new CandidateChapter
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            BranchId = branchId,
            ChapterId = chapter.Id,
            ChapterNumber = chapter.ChapterNumber,
            Version = version,
            CurrentArtifactId = reviewedArtifact.Id,
            DependsOnCandidateChapterId = priorCandidateId,
            Status = "candidate",
            Authorship = reviewedArtifact.Authorship,
            IsProtected = reviewedArtifact.IsProtected,
            ReviewArtifactIdsJson = JsonSerializer.Serialize(reviewArtifactIds),
            CreatedAt = DateTime.UtcNow
        };
        var summary = new ContinuitySummary
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            ChapterId = chapter.Id,
            ChapterVersionId = $"candidate:{candidate.Id}",
            BranchId = branchId,
            Version = version,
            SummaryJson = summaryProposal.ContentJson,
            EvidenceRefsJson = JsonSerializer.Serialize(canon.SummaryItems.Select(item => item.Evidence)),
            Status = "candidate",
            CreatedAt = DateTime.UtcNow
        };
        candidate.ContinuitySummaryId = summary.Id;
        _db.CandidateChapters.Add(candidate);
        _db.ContinuitySummaries.Add(summary);
        _db.CanonChanges.AddRange(canon.CanonChanges.Select(change => new CanonChange
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            ChapterId = chapter.Id,
            ChapterVersionId = $"candidate:{candidate.Id}",
            BranchId = branchId,
            ChangeType = change.ChangeType,
            Subject = change.Subject,
            ChangeJson = JsonSerializer.Serialize(change),
            EvidenceRefsJson = JsonSerializer.Serialize(new[] { change.Evidence }),
            Status = "candidate",
            CreatedAt = DateTime.UtcNow
        }));
        await MaterializeKnowledgeCitationsAsync(
            claim,
            candidate,
            contextTaskId,
            cancellationToken);
    }

    private async Task MaterializeKnowledgeCitationsAsync(
        KernelTaskClaim claim,
        CandidateChapter candidate,
        string contextTaskId,
        CancellationToken cancellationToken)
    {
        var evidenceArtifact = await _db.KernelArtifacts.AsNoTracking().SingleOrDefaultAsync(artifact =>
            artifact.UserId == claim.UserId &&
            artifact.ProjectId == claim.ProjectId &&
            artifact.GoalId == claim.GoalId &&
            artifact.BranchId == claim.BranchId &&
            artifact.TaskId == contextTaskId &&
            artifact.ArtifactType == "EvidenceBundle",
            cancellationToken);
        if (evidenceArtifact == null)
            return;
        var bundle = JsonSerializer.Deserialize<EvidenceBundle>(evidenceArtifact.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("EvidenceBundle Artifact 无法解析。");
        var knowledgeEntryIds = bundle.Items
            .Where(item => item.SourceType == "knowledge_entry")
            .Select(item => item.SourceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (knowledgeEntryIds.Length == 0)
            return;
        var entries = await _db.KnowledgeEntries.AsNoTracking()
            .Where(entry =>
                entry.UserId == claim.UserId &&
                entry.Status == "active" &&
                knowledgeEntryIds.Contains(entry.Id))
            .ToListAsync(cancellationToken);
        if (entries.Count != knowledgeEntryIds.Length)
            throw new InvalidOperationException("EvidenceBundle 引用了不可用或跨用户的知识条目。");

        foreach (var entry in entries)
        {
            var idempotencyKey = $"goal:{claim.GoalId}:candidate:{candidate.Id}:knowledge:{entry.Id}:v{entry.KnowledgeVersion}";
            var citationExists = await _db.KnowledgeCitations.AsNoTracking().AnyAsync(item =>
                item.UserId == claim.UserId && item.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (!citationExists)
            {
                _db.KnowledgeCitations.Add(new KnowledgeCitation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    KnowledgeEntryId = entry.Id,
                    KnowledgeVersion = entry.KnowledgeVersion,
                    GoalId = claim.GoalId,
                    ChapterId = candidate.ChapterId,
                    ChapterVersionId = $"candidate:{candidate.Id}",
                    Purpose = "chapter_context",
                    SourceArtifactId = evidenceArtifact.Id,
                    IdempotencyKey = idempotencyKey,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var usage = await _db.ProjectKnowledgeUsages.SingleOrDefaultAsync(item =>
                item.UserId == claim.UserId &&
                item.ProjectId == claim.ProjectId &&
                item.KnowledgeId == entry.LogicalKnowledgeId,
                cancellationToken);
            if (usage == null)
            {
                usage = new ProjectKnowledgeUsage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = claim.UserId,
                    ProjectId = claim.ProjectId,
                    KnowledgeId = entry.LogicalKnowledgeId,
                    FirstSeenAt = DateTime.UtcNow,
                    Role = entry.EntryType,
                    Scope = "ProjectWide",
                    Priority = 50,
                    ConstraintLevel = "Reference",
                    PackagePolicy = "RelevantOnly",
                    BoundVersion = entry.Version.ToString()
                };
                _db.ProjectKnowledgeUsages.Add(usage);
            }
            usage.Status = "referenced";
            usage.SourceRunId = claim.GoalId;
            usage.LastUsedAt = DateTime.UtcNow;
            usage.UsageCount++;
            usage.UsedByChaptersJson = AppendJsonString(usage.UsedByChaptersJson, candidate.ChapterId);
            usage.UsageIdempotencyKeysJson = AppendJsonString(usage.UsageIdempotencyKeysJson, idempotencyKey);
        }
    }

    private static string AppendJsonString(string? json, string value)
    {
        var values = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions)
                ?? throw new InvalidOperationException("知识使用记录包含无效 JSON 数组。");
        if (!values.Contains(value, StringComparer.Ordinal))
            values.Add(value);
        return JsonSerializer.Serialize(values);
    }
}
