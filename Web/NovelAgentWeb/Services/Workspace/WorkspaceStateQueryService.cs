using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public sealed class WorkspaceStateQueryService : IWorkspaceStateQueryService
{
    private readonly NovelAgentDbContext _db;

    public WorkspaceStateQueryService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentWorkspaceState> QueryAsync(
        WorkspaceStateQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.Username, u.Role })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);

        var projectQuery = _db.NovelProjects.AsNoTracking();
        if (!isAdmin)
            projectQuery = projectQuery.Where(p => p.UserId == request.UserId);

        var projectTotalCount = await projectQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var visibleProjects = await projectQuery
            .OrderByDescending(p => p.UpdatedAt)
            .Take(20)
            .Select(p => new AgentWorkspaceProjectState
            {
                Id = p.Id,
                Title = p.Title,
                OwnerUserId = p.UserId,
                OwnerUsername = _db.Users
                    .Where(u => u.Id == p.UserId)
                    .Select(u => u.Username)
                    .FirstOrDefault() ?? string.Empty,
                IsOwnedByCurrentUser = p.UserId == request.UserId,
                Status = p.Status,
                Genre = p.Genre ?? string.Empty,
                WordCount = p.WordCount,
                VolumeCount = p.Volumes.Count,
                ChapterCount = p.Chapters.Count,
                CommittedChapterCount = p.Chapters.Count(c =>
                    c.Status == "committed" ||
                    c.Status == "published" ||
                    c.Status == "completed"),
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var knowledgeQuery = _db.KnowledgeBases.AsNoTracking();
        if (!isAdmin)
            knowledgeQuery = knowledgeQuery.Where(k => k.UserId == request.UserId);

        var knowledgeTotal = await knowledgeQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var knowledgeCounts = await knowledgeQuery
            .GroupBy(k => k.EntryType)
            .Select(g => new AgentWorkspaceKnowledgeTypeCount
            {
                EntryType = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.EntryType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var recentKnowledge = await knowledgeQuery
            .OrderByDescending(k => k.CreatedAt)
            .Take(10)
            .Select(k => new AgentWorkspaceKnowledgeItem
            {
                Id = k.Id,
                Title = k.Title,
                EntryType = k.EntryType,
                OwnerUserId = k.UserId,
                SourceProjectId = k.SourceProjectId ?? string.Empty,
                CreatedAt = k.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var usageQuery = from usage in _db.ProjectKnowledgeUsages.AsNoTracking()
                         join knowledge in _db.KnowledgeBases.AsNoTracking()
                             on usage.KnowledgeId equals knowledge.Id
                         join project in _db.NovelProjects.AsNoTracking()
                             on usage.ProjectId equals project.Id
                         where usage.Status == "referenced" &&
                               usage.UsedByChaptersJson != null &&
                               usage.UsedByChaptersJson != "" &&
                               !knowledge.IsArchived
                         select new
                         {
                             Usage = usage,
                             Knowledge = knowledge,
                             Project = project
                         };
        if (!isAdmin)
            usageQuery = usageQuery.Where(row => row.Usage.UserId == request.UserId && row.Project.UserId == request.UserId);

        var recentlyUsedBindings = (await usageQuery
                .OrderByDescending(row => row.Usage.LastUsedAt ?? row.Usage.FirstSeenAt)
                .Take(12)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(row => new AgentWorkspaceKnowledgeUsageState
            {
                KnowledgeId = row.Knowledge.Id,
                Title = row.Knowledge.Title,
                EntryType = row.Knowledge.EntryType,
                ProjectId = row.Project.Id,
                ProjectTitle = row.Project.Title,
                ProjectUsageStatus = row.Usage.Status,
                ProjectUsageCount = row.Usage.UsageCount,
                Role = row.Usage.Role,
                Scope = row.Usage.Scope,
                Priority = row.Usage.Priority,
                ConstraintLevel = row.Usage.ConstraintLevel,
                PackagePolicy = row.Usage.PackagePolicy,
                UsedByChapters = ParseUsedByChapters(row.Usage.UsedByChaptersJson),
                LastUsedAt = row.Usage.LastUsedAt
            })
            .Where(item => item.UsedByChapters.Count > 0)
            .ToList();

        var evidenceQuery = from snapshot in _db.ProjectFactSnapshots.AsNoTracking()
                            join project in _db.NovelProjects.AsNoTracking()
                                on snapshot.ProjectId equals project.Id
                            select new
                            {
                                Snapshot = snapshot,
                                Project = project
                            };
        if (!isAdmin)
            evidenceQuery = evidenceQuery.Where(row => row.Snapshot.UserId == request.UserId && row.Project.UserId == request.UserId);

        var recentConstraintEvidence = (await evidenceQuery
                .Where(row => row.Snapshot.SnapshotJson.Contains("knowledgeConstraintEvidence"))
                .OrderByDescending(row => row.Snapshot.CreatedAt)
                .Take(20)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .SelectMany(row => ParseKnowledgeConstraintEvidence(row.Snapshot, row.Project.Title))
            .Take(12)
            .ToList();

        var conflictQuery = from report in _db.KnowledgeConflictReports.AsNoTracking()
                            join project in _db.NovelProjects.AsNoTracking()
                                on report.ProjectId equals project.Id
                            select new
                            {
                                Report = report,
                                Project = project
                            };
        if (!isAdmin)
            conflictQuery = conflictQuery.Where(row => row.Report.UserId == request.UserId && row.Project.UserId == request.UserId);

        var recentConflictReports = (await conflictQuery
                .OrderBy(row => row.Report.Status == "open" ? 0 : 1)
                .ThenByDescending(row => row.Report.CreatedAt)
                .Take(12)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(row => new AgentWorkspaceKnowledgeConflictReportState
            {
                ConflictId = row.Report.Id,
                KnowledgeId = row.Report.KnowledgeId,
                ConflictingKnowledgeIds = ParseStringList(row.Report.ConflictingKnowledgeIdsJson, 24),
                ProjectId = row.Report.ProjectId,
                ProjectTitle = row.Project.Title,
                ConflictType = row.Report.ConflictType,
                Severity = row.Report.Severity,
                ImpactScope = row.Report.ImpactScope,
                Explanation = row.Report.Explanation,
                RecommendedAction = row.Report.RecommendedAction,
                RequiresUserDecision = row.Report.RequiresUserDecision,
                Status = row.Report.Status,
                ResolutionNote = row.Report.ResolutionNote ?? string.Empty,
                CreatedAt = row.Report.CreatedAt,
                ResolvedAt = row.Report.ResolvedAt
            })
            .ToList();

        var runQuery = _db.AgentRuns.AsNoTracking();
        if (!isAdmin)
            runQuery = runQuery.Where(r => r.UserId == request.UserId);

        var activeRunCount = await runQuery
            .CountAsync(r => r.Status == "running" || r.Status == "pending" || r.Status == "in_progress", cancellationToken)
            .ConfigureAwait(false);
        var recentRuns = await runQuery
            .OrderByDescending(r => r.UpdatedAt)
            .Take(10)
            .Select(r => new AgentWorkspaceRunState
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                RunType = r.RunType,
                Status = r.Status,
                TargetChapterId = r.TargetChapterId ?? string.Empty,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var memoryReadQuery = _db.AgentMemoryReads.AsNoTracking();
        if (!isAdmin)
            memoryReadQuery = memoryReadQuery.Where(read => read.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(request.SessionId))
            memoryReadQuery = memoryReadQuery.Where(read => read.SessionId == request.SessionId ||
                (!string.IsNullOrWhiteSpace(request.ActiveProjectId) && read.ProjectId == request.ActiveProjectId));
        else if (!string.IsNullOrWhiteSpace(request.ActiveProjectId))
            memoryReadQuery = memoryReadQuery.Where(read => read.ProjectId == request.ActiveProjectId);

        var memoryReadRows = await memoryReadQuery
            .OrderByDescending(read => read.CreatedAt)
            .Take(10)
            .Select(read => new
            {
                read.Id,
                read.ProjectId,
                read.SessionId,
                read.RunId,
                read.MemoryScope,
                read.MemoryKeysJson,
                read.SourceType,
                read.Consumer,
                read.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var recentMemoryReads = memoryReadRows
            .Select(read => new AgentWorkspaceMemoryReadState
            {
                Id = read.Id,
                ProjectId = read.ProjectId ?? string.Empty,
                SessionId = read.SessionId ?? string.Empty,
                RunId = read.RunId ?? string.Empty,
                MemoryScope = read.MemoryScope,
                MemoryKeys = ParseStringList(read.MemoryKeysJson, 24),
                SourceType = read.SourceType,
                Consumer = read.Consumer,
                CreatedAt = read.CreatedAt
            })
            .Reverse()
            .ToList();

        var memoryPromotionQuery = _db.AgentMemoryPromotions.AsNoTracking();
        if (!isAdmin)
            memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(request.SessionId))
            memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.SessionId == request.SessionId ||
                (!string.IsNullOrWhiteSpace(request.ActiveProjectId) && promotion.ProjectId == request.ActiveProjectId));
        else if (!string.IsNullOrWhiteSpace(request.ActiveProjectId))
            memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.ProjectId == request.ActiveProjectId);

        var recentMemoryPromotions = await memoryPromotionQuery
            .OrderByDescending(promotion => promotion.CreatedAt)
            .Take(10)
            .Select(promotion => new AgentWorkspaceMemoryPromotionState
            {
                Id = promotion.Id,
                ProjectId = promotion.ProjectId ?? string.Empty,
                SessionId = promotion.SessionId ?? string.Empty,
                RunId = promotion.RunId ?? string.Empty,
                SourceScope = promotion.SourceScope,
                TargetScope = promotion.TargetScope,
                SourceMemoryKey = promotion.SourceMemoryKey,
                TargetMemoryKey = promotion.TargetMemoryKey,
                PromotionReason = promotion.PromotionReason,
                PayloadJson = promotion.PayloadJson,
                CreatedAt = promotion.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        recentMemoryPromotions.Reverse();

        return new AgentWorkspaceState
        {
            CurrentSession = new AgentCurrentSessionState
            {
                SessionId = request.SessionId,
                ActiveProjectId = request.ActiveProjectId,
                Phase = request.Phase,
                HasActiveProject = !string.IsNullOrWhiteSpace(request.ActiveProjectId) &&
                    !request.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase)
            },
            AuthorProfile = new AgentWorkspaceAuthorProfileState
            {
                DisplayName = request.AuthorDisplayName,
                StyleLikeCount = request.StyleLikeCount,
                StyleDislikeCount = request.StyleDislikeCount,
                GenreHabitCount = request.GenreHabitCount
            },
            ProjectTotalCount = projectTotalCount,
            ProjectPreviewCount = visibleProjects.Count,
            VisibleProjects = visibleProjects,
            KnowledgeBase = new AgentWorkspaceKnowledgeState
            {
                TotalCount = knowledgeTotal,
                CountsByType = knowledgeCounts,
                RecentItems = recentKnowledge,
                RecentlyUsedBindings = recentlyUsedBindings,
                RecentConstraintEvidence = recentConstraintEvidence,
                RecentConflictReports = recentConflictReports
            },
            Workflow = new AgentWorkspaceWorkflowState
            {
                ActiveRunCount = activeRunCount,
                RecentRunCount = recentRuns.Count,
                RecentRuns = recentRuns
            },
            Memory = new AgentWorkspaceMemoryState
            {
                RecentReads = recentMemoryReads,
                RecentPromotions = recentMemoryPromotions
            },
            Notes = new List<string>
            {
                isAdmin
                    ? "当前用户是 admin：可只读查看全站项目和知识库，但不会自动绑定或操作其他用户项目。"
                    : "当前只展示当前用户拥有的项目、知识库和工作流。",
                "QueryWorkspaceState 是只读快照，不会改变当前会话的 ActiveProjectId。",
                "绑定已有项目或创建新项目必须由 Agent 另行决策并调用 ResolveNovelProject。"
            }
        };
    }

    private static List<string> ParseUsedByChapters(string? json)
    {
        return ParseStringList(json, 24);
    }

    private static List<string> ParseStringList(string? json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed != null)
            {
                return parsed
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fall back to delimiter parsing for manually repaired rows.
        }

        return json
            .Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static IEnumerable<AgentWorkspaceKnowledgeConstraintEvidenceState> ParseKnowledgeConstraintEvidence(
        ProjectFactSnapshot snapshot,
        string projectTitle)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotJson))
            yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(snapshot.SnapshotJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("knowledgeConstraintEvidence", out var evidence) ||
                evidence.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            var chapterId = FirstNonEmpty(GetJsonString(root, "chapterId"), snapshot.ChapterId);
            foreach (var item in evidence.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var title = FirstNonEmpty(GetJsonString(item, "title"), GetJsonString(item, "knowledgeId"));
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                yield return new AgentWorkspaceKnowledgeConstraintEvidenceState
                {
                    KnowledgeId = GetJsonString(item, "knowledgeId"),
                    Title = title,
                    EntryType = GetJsonString(item, "entryType"),
                    ProjectId = snapshot.ProjectId,
                    ProjectTitle = projectTitle,
                    ChapterId = chapterId,
                    ConstraintLevel = GetJsonString(item, "constraintLevel"),
                    PackagePolicy = GetJsonString(item, "packagePolicy"),
                    GateStatus = GetJsonString(item, "gateStatus"),
                    EvidenceStatus = GetJsonString(item, "evidenceStatus"),
                    FactSnapshotId = snapshot.Id,
                    FactSnapshotVersion = snapshot.VersionNumber,
                    CreatedAt = snapshot.CreatedAt
                };
            }
        }
    }

    private static string GetJsonString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
