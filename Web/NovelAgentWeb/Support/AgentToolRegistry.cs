using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentToolRegistry
{
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private static readonly AsyncLocal<NovelProjectCatalog?> _currentCatalog = new();

    private NovelAgentWorkspace? _workspaceInstance;
    private NovelProjectCatalog? _catalogInstance;

    private NovelAgentWorkspace _workspace => _workspaceInstance ?? _currentWorkspace.Value ?? throw new InvalidOperationException("Workspace not set for current request");
    private NovelProjectCatalog _catalog => _catalogInstance ?? _currentCatalog.Value ?? throw new InvalidOperationException("Catalog not set for current request");

    private readonly UserSettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, AgentToolEntry> _entries;
    private readonly ILogger<AgentToolRegistry> _logger;

    internal static void SetWorkspace(NovelAgentWorkspace workspace, NovelProjectCatalog catalog)
    {
        _currentWorkspace.Value = workspace;
        _currentCatalog.Value = catalog;
    }

    internal static void ClearWorkspace()
    {
        _currentWorkspace.Value = null;
        _currentCatalog.Value = null;
    }

    internal void SetWorkspaceContext(NovelAgentWorkspace workspace, NovelProjectCatalog catalog)
    {
        _workspaceInstance = workspace;
        _catalogInstance = catalog;
        SetWorkspace(workspace, catalog);
    }

    internal void ClearWorkspaceContext()
    {
        _workspaceInstance = null;
        _catalogInstance = null;
        ClearWorkspace();
    }

    public AgentToolRegistry(UserSettingsManager settingsManager, IServiceProvider serviceProvider, ILogger<AgentToolRegistry> logger)
    {
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _entries = BuildEntries();
    }

    public IReadOnlyList<AgentToolDefinition> ListTools() => _entries.Values.Select(e => e.Definition).ToList();

    public IReadOnlyList<ToolSchema> ListToolSchemas() => _entries.Values.Select(e => new ToolSchema
    {
        Name = e.Definition.Name,
        Description = e.Definition.Description,
        Risk = e.Definition.Risk,
        RequiresConfirmation = e.Definition.RequiresConfirmation,
        Parameters = e.Definition.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
        SideEffects = e.Definition.SideEffects,
        Semantic = e.Definition.Semantic,
    }).ToList();

    public IReadOnlyList<string> GetToolNamesForPhase(ConversationPhase phase)
    {
        return _entries.Values
            .Where(entry => !string.Equals(entry.Definition.Name, "tool_search", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => CategoryHintRank(entry.Category, phase))
            .ThenBy(entry => entry.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Definition.Name)
            .ToArray();
    }

    internal string CurrentWorkspaceProjectIdForTests() => _workspace.ProjectId;

    public IReadOnlyList<ToolSchema> ListToolSchemasForPhase(ConversationPhase phase)
    {
        return GetToolNamesForPhase(phase)
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => new ToolSchema
            {
                Name = def!.Name,
                Description = def.Description,
                Risk = def.Risk,
                RequiresConfirmation = def.RequiresConfirmation,
                Parameters = def.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
                SideEffects = def.SideEffects,
                Semantic = def.Semantic,
            })
            .ToList();
    }

    public AgentToolDefinition? Find(string name) =>
        _entries.TryGetValue(name.Trim(), out var entry) ? entry.Definition : null;

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        bool confirmed,
        CancellationToken ct)
    {
        var name = call.Name.Trim();
        if (!_entries.TryGetValue(name, out var entry))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"未知工具：{name}",
                Phase = session.Phase,
            };
        }

        using var ledgerScope = _serviceProvider.GetService<IServiceScopeFactory>()?.CreateScope();
        var ledger = ledgerScope?.ServiceProvider.GetService<IAgentToolExecutionLedger>();
        var execution = ledger == null
            ? null
            : await ledger.StartAsync(new AgentToolExecutionStart(
                    session.UserId,
                    string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                    session.SessionId,
                    ResolveRunId(call, session),
                    session.Phase,
                    entry.Definition.Risk,
                    call,
                    entry.Definition.SideEffects), ct)
                .ConfigureAwait(false);

        try
        {
            var result = await entry.Handler(call, session, bible, confirmed || IsAutopilotAuthorizedTool(name), ct).ConfigureAwait(false);
            result.RequiresConfirmation = false;
            if (execution != null)
            {
                if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
                    !string.Equals(execution.ProjectId, session.ActiveProjectId, StringComparison.Ordinal))
                {
                    await ledger!.RebindProjectAsync(execution.Id, session.ActiveProjectId, ct).ConfigureAwait(false);
                }

                await ledger!.CompleteAsync(execution.Id, result, ct).ConfigureAwait(false);
            }
            return result;
        }
        catch (Exception ex) when (execution != null && ex is not OperationCanceledException)
        {
            await ledger!.CompleteAsync(
                    execution.Id,
                    new AgentToolExecutionResult
                    {
                        Success = false,
                        Message = ex.Message,
                        RunId = ResolveRunId(call, session),
                        Phase = session.Phase
                    },
                    ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static string? ResolveRunId(AgentToolCall call, AgentSession session) =>
        call.Arguments.TryGetValue("runId", out var runId) && !string.IsNullOrWhiteSpace(runId)
            ? runId.Trim()
            : session.ActiveRunId;

    private Dictionary<string, AgentToolEntry> BuildEntries()
    {
        var entries = new[]
        {
            Entry("tool_search", "meta", "Low", false, new[] { "query", "intent", "context", "phase", "includeAll", "limit" }, "全局工具目录与语义检索入口。query/intent/context 用于检索和排序工具语义；phase 只是可选 hint，不会限制可用工具集合；includeAll=true 可返回完整目录。", Effects(toolCache: true, sqliteSnapshot: true, sqlite: new[] { "agent_tool_search_snapshots" }), (call, session, _, _, ct) => ToolSearchAsync(call, session, ct)),
            Entry("QueryWorkspaceState", "workspace", "Low", false, Array.Empty<string>(), "只读查询当前用户可见的工作台真实状态：小说书城项目、知识库条目、创作工作流 Run、当前会话绑定情况。不会创建、绑定或切换项目。", Effects(), (call, session, _, _, ct) => QueryWorkspaceStateAsync(session, ct)),
            Entry("ResolveNovelProject", "project", "Low", false, new[] { "mode", "projectId", "projectTitle", "title", "genre", "seed" }, "由 Agent 决策绑定已有小说或创建新小说。mode 可选 bind_existing/create_new/auto；绑定不创建新书，创建会切换当前会话上下文。", Effects(memory: new[] { "session", "project" }, sqlite: new[] { "novel_projects", "agent_sessions" }), (call, session, _, _, ct) => ResolveNovelProjectAsync(call, session, ct)),
            Entry("ProcessKnowledgeFile", "knowledge", "Medium", true, new[] { "taskId" }, "处理已上传的知识文件，自动提取创意写作知识条目。支持结构化文档和创意素材。", Effects(memory: new[] { "project" }, sqlite: new[] { "knowledge_base", "knowledge_processing_tasks", "content_documents", "project_knowledge_usages" }, vector: new[] { "knowledge" }), (call, _, _, _, ct) => ProcessKnowledgeFileAsync(call, ct)),
            Entry("QueryProjectStatus", "blackboard", "Low", false, Array.Empty<string>(), "读取 MissionBlackboard、Story Bible、素材、账本、当前可操作 Run 状态。", Effects(), (call, session, bible, _, ct) => QueryProjectStatusAsync(session, bible, ct)),
            Entry("QueryProjectContent", "content", "Low", false, new[] { "chapterId", "chapterNumber", "volumeNumber", "includeBody", "includeFacts" }, "只读读取当前项目的卷、章、正文、摘要和关键连续性事实。用于回答“第几章写了什么、所属卷、正文开头、关键事实”等内容问题，不改变工作流或书城。", Effects(), (call, session, bible, _, ct) => QueryProjectContentAsync(call, session, bible, ct)),
            Entry("SearchCreativeKnowledge", "rag", "Low", false, new[] { "query" }, "检索创意知识库、类型原则、反套路策略和项目记忆。", Effects(memory: new[] { "execution" }, sqlite: new[] { "project_knowledge_usages" }, vector: new[] { "knowledge" }), (call, session, _, _, ct) => SearchCreativeKnowledgeAsync(call, session, ct)),
            Entry("PlanStoryFoundation", "planning", "Low", false, new[] { "userSeed", "genre", "subGenre", "targetReader", "desiredDirection", "candidateDirections", "forbiddenDirections" }, "生成故事地基和大框架候选，不直接固化。候选必须服从用户正向方向和明确排除项；可由大模型传 candidateDirections/forbiddenDirections 指定候选语义。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, _, _, ct) => PlanStoryFoundationAsync(call, session, ct)),
            Entry("CommitStoryFoundation", "commit", "High", true, new[] { "runId", "selectedMacroCandidateIndex", "selectedMacroCandidateId", "selectedMacroCandidateTitle" }, "把候选故事地基固化到 Story Bible。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "story_constitutions", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitStoryFoundationAsync(call, session, confirmed, ct)),
            Entry("PlanVolumeArc", "planning", "Low", false, new[] { "creativeBrief", "volumeId", "volumeTitle", "sourceTurnId", "candidateDirections", "forbiddenDirections" }, "规划卷级弧线，不直接固化。candidateDirections/forbiddenDirections 由大模型根据用户意图给出，工具按语义方向生成卷节拍。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, bible, _, ct) => PlanVolumeArcAsync(call, session, bible, ct)),
            Entry("CommitVolumeArc", "commit", "High", true, new[] { "runId" }, "把卷规划提交到 Story Bible。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "volume_arcs", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitVolumeArcAsync(call, session, confirmed, ct)),
            Entry("PlanChapter", "planning", "Medium", false, new[] { "creativeBrief", "chapterId", "sourceTurnId", "candidateDirections", "forbiddenDirections" }, "检索项目状态和知识库，生成章节候选。candidateDirections/forbiddenDirections 由大模型根据用户意图给出，工具按这些语义方向生成候选，不再套固定桥段模板。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }, vector: new[] { "knowledge", "chapter_context" }), (call, session, bible, _, ct) => PlanChapterAsync(call, session, bible, ct)),
            Entry("SelectChapterCandidate", "planning", "Medium", false, new[] { "runId", "candidateTitles" }, "选择章节候选，决定后续正文生成方向。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs" }), (call, session, _, confirmed, ct) => SelectChapterCandidateAsync(call, session, confirmed, ct)),
            Entry("BuildChapterContextPackage", "writing", "Low", false, new[] { "runId" }, "构建章节上下文包，汇总事实快照、蓝图、摘要链和长距离 RAG。", Effects(memory: new[] { "execution" }, sqlite: new[] { "agent_runs", "content_documents" }, vector: new[] { "chapter_context" }), (call, session, _, _, ct) => BuildChapterContextPackageAsync(call, session, ct)),
            Entry("GenerateChapterWithChanges", "writing", "High", true, new[] { "runId" }, "生成章节正文和 CHANGES，硬门禁通过后才提交成稿。", Effects(memory: new[] { "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, _, confirmed, ct) => GenerateChapterWithChangesAsync(call, session, confirmed, ct)),
            Entry("ValidateChapterDraft", "gate", "Medium", false, new[] { "runId" }, "校验章节草稿的 CHANGES、事实快照、蓝图和 RAG 连续性。", Effects(memory: new[] { "execution" }, sqlite: new[] { "agent_runs" }), (call, session, _, _, ct) => ValidateChapterDraftAsync(call, session, ct)),
            Entry("RepairChapterDraft", "writing", "High", true, new[] { "runId" }, "根据门禁失败项修复章节草稿和 CHANGES。", Effects(memory: new[] { "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, _, confirmed, ct) => RepairChapterDraftAsync(call, session, confirmed, ct)),
            Entry("CommitValidatedChapter", "commit", "High", true, new[] { "runId" }, "提交已通过门禁的章节成稿，刷新索引并进入书城。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "chapters", "agent_runs", "content_documents" }, vector: new[] { "chapter" }), (call, session, _, confirmed, ct) => CommitValidatedChapterAsync(call, session, confirmed, ct)),
            Entry("RefreshProjectIndexes", "maintenance", "Medium", false, new[] { "runId" }, "刷新章节摘要、事实快照、长距离 RAG 和索引标记。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "agent_runs", "content_chunks", "content_vector_points" }, vector: new[] { "chapter", "story_bible" }), (call, session, _, _, ct) => RefreshProjectIndexesAsync(call, session, ct)),
            Entry("AnalyzeDependencyImpact", "maintenance", "Low", false, new[] { "runId" }, "分析结构化设定改动对卷、章节、蓝图和校验摘要的影响。", Effects(memory: new[] { "execution" }, sqlite: new[] { "agent_runs" }), (call, session, _, _, ct) => AnalyzeDependencyImpactAsync(call, session, ct)),
            Entry("ReviewChapter", "review", "Medium", false, new[] { "runId" }, "复盘已生成章节并提出账本沉淀。", Effects(memory: new[] { "project", "author", "execution" }, sqlite: new[] { "agent_runs", "agent_memory_events" }), (call, session, _, _, ct) => ReviewChapterAsync(call, session, ct)),
        };
        return entries.ToDictionary(e => e.Definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsAutopilotAuthorizedTool(string name) =>
        name is "CommitStoryFoundation" or "CommitVolumeArc" or
            "GenerateChapterWithChanges" or "RepairChapterDraft" or "CommitValidatedChapter";

    private async Task<AgentToolExecutionResult> QueryWorkspaceStateAsync(AgentSession session, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

        var currentUser = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => new { u.Id, u.Username, u.Role })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);

        var projectQuery = db.NovelProjects.AsNoTracking();
        if (!isAdmin)
            projectQuery = projectQuery.Where(p => p.UserId == session.UserId);

        var projectTotalCount = await projectQuery.CountAsync(ct).ConfigureAwait(false);
        var visibleProjects = await projectQuery
            .OrderByDescending(p => p.UpdatedAt)
            .Take(20)
            .Select(p => new AgentWorkspaceProjectState
            {
                Id = p.Id,
                Title = p.Title,
                OwnerUserId = p.UserId,
                OwnerUsername = db.Users
                    .Where(u => u.Id == p.UserId)
                    .Select(u => u.Username)
                    .FirstOrDefault() ?? string.Empty,
                IsOwnedByCurrentUser = p.UserId == session.UserId,
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
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var knowledgeQuery = db.KnowledgeBases.AsNoTracking();
        if (!isAdmin)
            knowledgeQuery = knowledgeQuery.Where(k => k.UserId == session.UserId);

        var knowledgeTotal = await knowledgeQuery.CountAsync(ct).ConfigureAwait(false);
        var knowledgeCounts = await knowledgeQuery
            .GroupBy(k => k.EntryType)
            .Select(g => new AgentWorkspaceKnowledgeTypeCount
            {
                EntryType = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.EntryType)
            .ToListAsync(ct)
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
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var runQuery = db.AgentRuns.AsNoTracking();
        if (!isAdmin)
            runQuery = runQuery.Where(r => r.UserId == session.UserId);

        var activeRunCount = await runQuery
            .CountAsync(r => r.Status == "running" || r.Status == "pending" || r.Status == "in_progress", ct)
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
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var state = new AgentWorkspaceState
        {
            CurrentSession = new AgentCurrentSessionState
            {
                SessionId = session.SessionId,
                ActiveProjectId = session.ActiveProjectId ?? string.Empty,
                Phase = session.Phase,
                HasActiveProject = !string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
                    !session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase)
            },
            AuthorProfile = new AgentWorkspaceAuthorProfileState
            {
                DisplayName = session.WorkingMemory.AuthorMemory?.DisplayName ?? string.Empty,
                StyleLikeCount = session.WorkingMemory.AuthorMemory?.StyleLikes.Count ?? 0,
                StyleDislikeCount = session.WorkingMemory.AuthorMemory?.StyleDislikes.Count ?? 0,
                GenreHabitCount = session.WorkingMemory.AuthorMemory?.GenreHabits.Count ?? 0
            },
            ProjectTotalCount = projectTotalCount,
            ProjectPreviewCount = visibleProjects.Count,
            VisibleProjects = visibleProjects,
            KnowledgeBase = new AgentWorkspaceKnowledgeState
            {
                TotalCount = knowledgeTotal,
                CountsByType = knowledgeCounts,
                RecentItems = recentKnowledge
            },
            Workflow = new AgentWorkspaceWorkflowState
            {
                ActiveRunCount = activeRunCount,
                RecentRunCount = recentRuns.Count,
                RecentRuns = recentRuns
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

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = BuildWorkspaceStateMessage(state),
            Phase = session.Phase,
            Data = state,
            Artifact = BuildArtifact(
                "workspace_state",
                "workspace",
                session.ActiveProjectId ?? string.Empty,
                string.Empty,
                $"已读取小说书城、知识库和创作工作流状态；小说书城当前可见项目共 {projectTotalCount} 本。",
                state.VisibleProjects.Select(p => $"{p.Title} ({p.Id})").ToArray()),
            Suggestions = new[]
            {
                "根据书城状态决定是否绑定已有项目",
                "查看知识库条目",
                "继续当前创作工作流"
            }
        };
    }

    private static string BuildWorkspaceStateMessage(AgentWorkspaceState state)
    {
        var projectCountLine = $"小说书城当前可见项目共 {state.ProjectTotalCount} 本；本次快照列出最近 {state.ProjectPreviewCount} 本。";
        var projectLines = state.VisibleProjects.Count == 0
            ? projectCountLine
            : projectCountLine + "\n" + string.Join("\n", state.VisibleProjects.Select(p =>
                $"• {p.Title}（{p.Status}，owner={p.OwnerUsername}/{p.OwnerUserId}，chapters={p.ChapterCount}，committed={p.CommittedChapterCount}，owned_by_current_user={p.IsOwnedByCurrentUser}）"));
        var knowledgeTypes = state.KnowledgeBase.CountsByType.Count == 0
            ? "无分类"
            : string.Join("，", state.KnowledgeBase.CountsByType.Select(x => $"{x.EntryType}:{x.Count}"));
        var workflowLines = state.Workflow.RecentRuns.Count == 0
            ? "创作工作流：最近无 Run。"
            : "创作工作流最近 Run：\n" + string.Join("\n", state.Workflow.RecentRuns.Select(r =>
                $"• {r.RunType}/{r.Status} project={r.ProjectId} run={r.Id}"));
        var authorLine = string.IsNullOrWhiteSpace(state.AuthorProfile.DisplayName)
            ? "作者记忆：未记录用户称呼。"
            : $"作者记忆：作者称呼：{state.AuthorProfile.DisplayName}；风格喜好 {state.AuthorProfile.StyleLikeCount} 条，反感风格 {state.AuthorProfile.StyleDislikeCount} 条，题材习惯 {state.AuthorProfile.GenreHabitCount} 条。";

        return string.Join("\n\n", new[]
        {
            projectLines,
            $"知识库：共 {state.KnowledgeBase.TotalCount} 条；分类：{knowledgeTypes}。",
            workflowLines,
            authorLine,
            $"当前会话：activeProjectId={state.CurrentSession.ActiveProjectId}，phase={state.CurrentSession.Phase}，hasActiveProject={state.CurrentSession.HasActiveProject}",
            string.Join("\n", state.Notes)
        });
    }

    private static AgentToolEntry Entry(
        string name,
        string category,
        string risk,
        bool requiresConfirmation,
        IReadOnlyList<string> args,
        string description,
        AgentToolSideEffectSpec sideEffects,
        Func<AgentToolCall, AgentSession, StoryBibleDocument, bool, CancellationToken, Task<AgentToolExecutionResult>> handler) =>
        new()
        {
            Category = category,
            Definition = new AgentToolDefinition
            {
                Name = name,
                Description = description,
                Risk = risk,
                RequiresConfirmation = requiresConfirmation,
                Arguments = args.ToList(),
                SideEffects = sideEffects,
                Semantic = BuildDefaultSemantic(name, category, sideEffects),
            },
            Handler = handler,
        };

    private static AgentToolSemanticSpec BuildDefaultSemantic(string name, string category, AgentToolSideEffectSpec sideEffects)
    {
        var readsFrom = new List<string>();
        var writesTo = new List<string>();
        var domainSurface = category switch
        {
            "meta" => "Agent Runtime",
            "workspace" => "小说书城 / 创作工作流 / 知识库 / 记忆系统",
            "project" => "小说书城",
            "content" => "小说书城 / 正文内容 / Story Bible",
            "knowledge" or "rag" => "知识库",
            "blackboard" => "创作工作流",
            "planning" or "writing" or "gate" => "创作工作流",
            "commit" => "小说书城 / Story Bible",
            "review" => "记忆系统 / 创作工作流",
            "maintenance" => "知识库 / 索引维护",
            _ => "Agent Runtime"
        };
        var outputKind = category switch
        {
            "meta" => "capability_catalog",
            "workspace" => "read_only_workspace_snapshot",
            "project" => "project_binding_or_creation",
            "content" => "read_only_project_content",
            "knowledge" => "knowledge_ingestion_process_result",
            "rag" => "retrieval_context",
            "blackboard" => "project_workflow_state",
            "planning" => "workflow_process_artifact",
            "writing" => "workflow_process_artifact",
            "gate" => "workflow_gate_report",
            "commit" => "canonical_project_or_chapter_state",
            "review" => "reflection_memory_update",
            "maintenance" => "index_maintenance_result",
            _ => "runtime_result"
        };

        switch (category)
        {
            case "meta":
                readsFrom.AddRange(new[] { "agent_tool_registry", "tool_search_cache" });
                break;
            case "workspace":
                readsFrom.AddRange(new[] { "novel_projects", "knowledge_base", "agent_runs", "volumes", "chapters", "agent_sessions" });
                break;
            case "project":
                readsFrom.AddRange(new[] { "novel_projects", "agent_sessions", "session_memory" });
                break;
            case "content":
                readsFrom.AddRange(new[] { "novel_projects", "volumes", "volume_arcs", "chapters", "content_documents", "content_chunks", "story_bible", "continuity_facts" });
                break;
            case "knowledge":
            case "rag":
                readsFrom.AddRange(new[] { "knowledge_base", "project_knowledge_usages", "content_chunks", "content_vector_points", "project_memory" });
                break;
            case "blackboard":
                readsFrom.AddRange(new[] { "mission_blackboard", "story_bible", "agent_runs", "tool_execution_ledger" });
                break;
            case "planning":
            case "writing":
            case "gate":
                readsFrom.AddRange(new[] { "story_bible", "project_memory", "author_memory", "execution_memory", "knowledge_base", "agent_runs" });
                break;
            case "commit":
                readsFrom.AddRange(new[] { "agent_runs", "content_documents", "story_bible" });
                break;
            case "review":
                readsFrom.AddRange(new[] { "agent_runs", "content_documents", "execution_memory", "author_memory" });
                break;
            case "maintenance":
                readsFrom.AddRange(new[] { "chapters", "story_bible", "content_chunks", "content_vector_points" });
                break;
        }

        writesTo.AddRange(sideEffects.WritesMemoryScopes.Select(scope => $"{scope}_memory"));
        writesTo.AddRange(sideEffects.WritesSqliteEntities);
        writesTo.AddRange(sideEffects.WritesVectorIndexes.Select(index => $"vector:{index}"));
        if (sideEffects.WritesLedger) writesTo.Add("agent_tool_execution_ledger");
        if (sideEffects.WritesToolSearchCache) writesTo.Add("tool_search_cache");
        if (sideEffects.WritesRedisRecentCache) writesTo.Add("recent_runtime_cache");

        if (writesTo.Count == 0 ||
            string.Equals(name, "QueryWorkspaceState", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "QueryProjectContent", StringComparison.OrdinalIgnoreCase))
        {
            writesTo.Clear();
            writesTo.Add("none_read_only");
        }

        return new AgentToolSemanticSpec
        {
            DomainSurface = domainSurface,
            OutputKind = outputKind,
            ReadsFrom = readsFrom.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WritesTo = writesTo.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            UserVisibleWhere = category switch
            {
                "workspace" => "Agent 对话中的工作台快照；不会改变书城或会话绑定",
                "project" => "当前 Agent 会话和小说书城项目列表",
                "content" => "Agent 对话中的项目内容引用；不会改变书城、正文或工作流",
                "knowledge" or "rag" => "知识库、项目知识引用和 Agent 对话",
                "planning" or "writing" or "gate" => "创作工作流 Run 和 Agent 对话",
                "commit" => "小说书城、Story Bible、章节列表和 Agent 对话",
                "review" => "记忆系统、复盘记录和 Agent 对话",
                "maintenance" => "索引状态、知识库检索效果和 Agent 对话",
                _ => "Agent 对话"
            },
            ResultSemantics = category switch
            {
                "workspace" => "返回当前可见真实状态，帮助模型理解书城、知识库、工作流和会话绑定；结果不是项目绑定决策。",
                "content" => "返回当前项目已落库的卷、章、正文片段和连续性事实，供模型基于真实内容回答或续写。",
                "planning" => "返回候选或计划，属于过程产物，需后续 commit 才会成为最终项目状态。",
                "writing" => "返回草稿或上下文包，属于过程产物，需门禁和 commit 后才进入书城。",
                "gate" => "返回校验报告，决定是否修复或提交，报告本身不是最终章节。",
                "commit" => "把已选择/已通过门禁的产物固化为最终项目状态。",
                "rag" => "返回检索上下文，供推理使用，不直接改变最终作品。",
                _ => "返回工具执行结果，模型需要结合当前任务判断下一步。"
            }
        };
    }

    private static AgentToolSideEffectSpec Effects(
        bool toolCache = false,
        bool sqliteSnapshot = false,
        IReadOnlyList<string>? memory = null,
        IReadOnlyList<string>? sqlite = null,
        IReadOnlyList<string>? vector = null) => new()
        {
            WritesLedger = true,
            WritesRedisRecentCache = true,
            WritesToolSearchCache = toolCache,
            WritesSqliteSnapshot = sqliteSnapshot,
            WritesMemoryScopes = memory?.ToList() ?? new List<string>(),
            WritesSqliteEntities = sqlite?.ToList() ?? new List<string>(),
            WritesVectorIndexes = vector?.ToList() ?? new List<string>()
        };

    private async Task<AgentToolExecutionResult> ResolveNovelProjectAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var mode = Arg(call, "mode", "auto").Trim().ToLowerInvariant();
        var projectId = Arg(call, "projectId");
        var projectTitle = Arg(call, "projectTitle");
        var wantsCreation = mode is "create_new" or "create" or "new";
        var wantsBinding = !wantsCreation &&
                           (mode is "bind_existing" or "bind" or "existing" or "continue_existing" or "continue" ||
                            !string.IsNullOrWhiteSpace(projectId) ||
                            !string.IsNullOrWhiteSpace(projectTitle));

        if (wantsBinding)
        {
            var project = await FindProjectForBindingAsync(projectId, projectTitle, ct).ConfigureAwait(false);
            if (project == null)
            {
                return new AgentToolExecutionResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(projectTitle) && string.IsNullOrWhiteSpace(projectId)
                        ? "没有找到可绑定的已有小说项目。"
                        : $"没有找到匹配的已有小说项目：{FirstNonEmpty(projectTitle, projectId)}。",
                    Phase = session.Phase,
                    Suggestions = new[] { "列出已有项目", "创建新小说", "换一个项目名" },
                };
            }

            await _catalog.ActivateAsync(project.Id, ct).ConfigureAwait(false);
            session.ActiveProjectId = project.Id;
            session.ActiveRunId = null;
            session.Phase = "project_bound";
            session.WorkingMemory.ProjectMemory.ProjectId = project.Id;
            session.WorkingMemory.MissionPlan.ProjectId = project.Id;
            session.WorkingMemory.MissionPlan.ProjectTitle = project.Title;
            session.WorkingMemory.Mission.CurrentGoal = FirstNonEmpty(session.WorkingMemory.CurrentGoal, project.CoreHook);

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = $"已切换到已有小说「{project.Title}」。后续记忆、工具和任务都会在这个项目上下文里继续。",
                Phase = session.Phase,
                Data = project,
                Artifact = BuildArtifact("project_bound", project.Id, project.Id, string.Empty, $"已绑定已有小说「{project.Title}」。", new[] { "查看当前状态", "继续规划" }),
                Suggestions = new[] { "查看当前状态", "继续规划", "处理知识文件" },
            };
        }

        return await CreateNovelProjectAsync(call, session, ct).ConfigureAwait(false);
    }

    private async Task<NovelProjectInfo?> FindProjectForBindingAsync(string projectId, string projectTitle, CancellationToken ct)
    {
        var document = await _catalog.GetAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var byId = document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectTitle))
        {
            var byTitle = document.Projects.FirstOrDefault(p =>
                string.Equals(p.Title, projectTitle, StringComparison.OrdinalIgnoreCase));
            if (byTitle != null)
            {
                return byTitle;
            }

            var normalizedTitle = NormalizeProjectFingerprint(projectTitle);
            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                byTitle = document.Projects.FirstOrDefault(p =>
                {
                    var candidate = NormalizeProjectFingerprint(p.Title);
                    return !string.IsNullOrWhiteSpace(candidate) &&
                           (candidate.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase) ||
                            normalizedTitle.Contains(candidate, StringComparison.OrdinalIgnoreCase));
                });
                if (byTitle != null)
                {
                    return byTitle;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(document.ActiveProjectId))
        {
            return document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task<AgentToolExecutionResult> CreateNovelProjectAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var seed = Arg(call, "seed", session.WorkingMemory.CurrentGoal);
        if (IsAwaitingFoundationForExistingProject(session))
        {
            var existing = !string.IsNullOrWhiteSpace(session.ActiveProjectId)
                ? await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false)
                : null;
            existing ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
            session.ActiveProjectId = existing.Id;
            session.Phase = "awaiting_user_foundation";
            session.WorkingMemory.Mission.CreativePhase = "foundation_intake";
            session.WorkingMemory.Mission.Readiness = "needs_author_input";
            session.WorkingMemory.Mission.NextIntent = "ask_foundation_question";
            session.WorkingMemory.Mission.PendingUserDecision = "补齐故事地基设定";
            if (string.IsNullOrWhiteSpace(session.WorkingMemory.Mission.PendingQuestion))
                session.WorkingMemory.Mission.PendingQuestion = "这本新小说的类型、核心钩子、主角引擎、阅读快感和禁区分别是什么？";
            if (!string.IsNullOrWhiteSpace(seed) && !session.WorkingMemory.Mission.FoundationBrief.ContainsKey("latestSeed"))
                session.WorkingMemory.Mission.FoundationBrief["latestSeed"] = seed;
            if (!session.WorkingMemory.OpenQuestions.Contains(session.WorkingMemory.Mission.PendingQuestion))
                session.WorkingMemory.OpenQuestions.Add(session.WorkingMemory.Mission.PendingQuestion);

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = $"当前会话已经有待补地基的新小说工程「{existing.Title}」。工具未重复创建项目，下一步应读取作者补充的地基信息。",
                Phase = session.Phase,
                Data = existing,
                Artifact = BuildArtifact("existing_novel_project", existing.Id, existing.Id, string.Empty, $"新小说工程「{existing.Title}」正在等待故事地基。", new[] { "补齐故事地基", "生成故事地基候选" }),
                Suggestions = new[] { "补齐类型/核心钩子/主角引擎", "查看当前状态", "继续地基规划" },
            };
        }

        var requestedTitle = FirstNonEmpty(Arg(call, "title"), Arg(call, "projectTitle"));
        var requestedGenre = Arg(call, "genre", ExtractGenre(seed, string.Empty));
        var foundationBriefText = BuildFoundationBriefText(call, session, seed, requestedTitle, requestedGenre);
        var reusableProject = await FindReusableDraftProjectAsync(seed, requestedTitle, ct).ConfigureAwait(false);
        if (reusableProject != null)
        {
            var hasCompleteBrief = HasSufficientFoundationBrief(foundationBriefText);
            BindSessionToFoundationProject(session, reusableProject, seed, hasCompleteBrief);
            var message = hasCompleteBrief
                ? $"已复用待补地基的新小说「{reusableProject.Title}」，没有重复创建同名项目。\n\n当前需求已经包含类型、主角、爽点循环和明确不要的方向，信息足够生成故事地基候选。"
                : $"已复用待补地基的新小说「{reusableProject.Title}」，没有重复创建同名项目。\n\n现在继续把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？";
            return new AgentToolExecutionResult
            {
                Success = true,
                Message = message,
                Phase = session.Phase,
                Data = reusableProject,
                Artifact = BuildArtifact("existing_novel_project", reusableProject.Id, reusableProject.Id, string.Empty, $"复用新小说「{reusableProject.Title}」。", hasCompleteBrief ? new[] { "生成故事地基候选" } : new[] { "补齐故事地基", "生成故事地基候选" }),
                Suggestions = hasCompleteBrief ? new[] { "生成故事地基候选", "查看当前状态" } : new[] { "补齐类型/核心钩子/主角引擎", "查看当前状态", "继续地基规划" },
            };
        }

        var project = await _catalog.CreateAsync(new NovelProjectCreateRequest(
            requestedTitle,
            requestedGenre,
            seed), ct).ConfigureAwait(false);

        var hasCompleteFoundationBrief = HasSufficientFoundationBrief(foundationBriefText);
        BindSessionToFoundationProject(session, project, seed, hasCompleteFoundationBrief);
        var resultMessage = hasCompleteFoundationBrief
            ? $"已创建新小说「{project.Title}」，它会作为独立作品进入书城，不会覆盖旧书。\n\n当前需求已经包含类型、主角、爽点循环和明确不要的方向，信息足够生成故事地基候选。"
            : $"已创建新小说「{project.Title}」，它会作为独立作品进入书城，不会覆盖旧书。\n\n现在先把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？";

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = resultMessage,
            Phase = session.Phase,
            Data = project,
            Artifact = BuildArtifact("novel_project", project.Id, project.Id, string.Empty, $"新小说「{project.Title}」已创建。", hasCompleteFoundationBrief ? new[] { "生成故事地基候选" } : new[] { "补齐故事地基", "生成故事地基候选" }),
            Suggestions = hasCompleteFoundationBrief ? new[] { "生成故事地基候选", "查看当前状态" } : new[] { "玄幻学院流，主角有代价型能力", "都市悬疑，主角追查异常规则", "我先给你完整设定" },
        };
    }

    private async Task<NovelProjectInfo?> FindReusableDraftProjectAsync(string seed, string title, CancellationToken ct)
    {
        var normalizedSeed = NormalizeProjectFingerprint(seed);
        var normalizedTitle = NormalizeProjectFingerprint(title);
        if (string.IsNullOrWhiteSpace(normalizedSeed) && string.IsNullOrWhiteSpace(normalizedTitle))
            return null;

        var document = await _catalog.GetAsync(ct).ConfigureAwait(false);
        foreach (var project in document.Projects.OrderByDescending(p => p.UpdatedAt))
        {
            if (!IsPotentialDraftDuplicate(project, normalizedSeed, normalizedTitle))
                continue;

            var bible = await _catalog.WithProjectAsync(
                project,
                () => _workspace.Orchestrator.GetStoryBibleAsync(ct),
                ct).ConfigureAwait(false);
            if (HasCommittedChapter(bible))
                continue;

            if (bible.Constitution == null || project.Status is "Drafting" or "Foundation" or "Planning")
                return project;
        }

        return null;
    }

    private static bool IsPotentialDraftDuplicate(NovelProjectInfo project, string normalizedSeed, string normalizedTitle)
    {
        var projectSeed = NormalizeProjectFingerprint(project.CoreHook);
        var projectTitle = NormalizeProjectFingerprint(project.Title);
        return (!string.IsNullOrWhiteSpace(normalizedSeed) && normalizedSeed == projectSeed) ||
               (!string.IsNullOrWhiteSpace(normalizedTitle) && normalizedTitle == projectTitle && IsGenericDraftTitle(project.Title));
    }

    private static bool HasCommittedChapter(StoryBibleDocument bible) =>
        bible.AgentRuns.Any(run =>
            string.Equals(run.DraftArtifact?.Status, "committed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(run.DraftArtifact?.CommittedContent));

    private static void BindSessionToFoundationProject(AgentSession session, NovelProjectInfo project, string seed, bool foundationBriefReady = false)
    {
        session.ActiveProjectId = project.Id;
        session.ActiveRunId = null;
        session.RunHistory.Clear();
        session.Phase = foundationBriefReady ? "foundation_ready" : "awaiting_user_foundation";
        session.WorkingMemory.PendingToolCall = null;
        session.WorkingMemory.CurrentGoal = seed;
        session.WorkingMemory.OpenQuestions.Clear();
        session.WorkingMemory.Mission = new AgentMissionState
        {
            CurrentGoal = seed,
            CreativePhase = foundationBriefReady ? "ready_for_foundation_planning" : "foundation_intake",
            Readiness = foundationBriefReady ? "ready" : "needs_author_input",
            NextIntent = foundationBriefReady ? "plan_story_foundation" : "ask_foundation_question",
            PendingUserDecision = foundationBriefReady ? string.Empty : "补齐故事地基设定",
            PendingQuestion = foundationBriefReady ? string.Empty : "这本新小说的类型、核心钩子、主角引擎、阅读快感和禁区分别是什么？",
        };
        if (!string.IsNullOrWhiteSpace(seed))
            session.WorkingMemory.Mission.FoundationBrief["rawSeed"] = seed;
        if (!foundationBriefReady)
            session.WorkingMemory.OpenQuestions.Add(session.WorkingMemory.Mission.PendingQuestion);
    }

    private static bool HasSufficientFoundationBrief(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
            return false;

        var signalCount = 0;
        if (ContainsAny(seed, "玄幻", "末世", "都市", "悬疑", "科幻", "仙侠", "奇幻", "爽文", "升级流", "学院流", "废土", "机甲"))
            signalCount++;
        if (ContainsAny(seed, "主角", "男主", "女主", "底层", "幸存者", "少年", "穿越", "重生", "矿工", "奴工"))
            signalCount++;
        if (ContainsAny(seed, "系统", "金手指", "吞噬", "晶核", "升级", "打怪", "修炼", "异能", "境界", "建基地", "战甲", "材料", "改装"))
            signalCount++;
        if (ContainsAny(seed, "爽点", "打怪", "升级", "碾压", "收伙伴", "征服", "后宫", "建基地", "成长", "爆材料", "扩大地图"))
            signalCount++;
        if (ContainsAny(seed, "不要", "禁区", "排除", "禁止", "不想要", "别"))
            signalCount++;

        return signalCount >= 4 && seed.Trim().Length >= 40;
    }

    private static string BuildFoundationBriefText(
        AgentToolCall call,
        AgentSession session,
        string seed,
        string requestedTitle,
        string requestedGenre) =>
        string.Join(" ", new[]
        {
            seed,
            requestedTitle,
            requestedGenre,
            Arg(call, "creativeBrief"),
            Arg(call, "desiredDirection"),
            Arg(call, "targetReader"),
            session.WorkingMemory.CurrentGoal,
            session.WorkingMemory.Mission.CurrentGoal,
            session.WorkingMemory.Mission.PendingUserDecision,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool ContainsAny(string text, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(text) &&
        tokens.Any(token => !string.IsNullOrWhiteSpace(token) &&
                            text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeProjectFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool IsGenericDraftTitle(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("未命名", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("新书", StringComparison.OrdinalIgnoreCase);

    private static bool IsAwaitingFoundationForExistingProject(AgentSession session) =>
        !string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
        (string.Equals(session.Phase, "awaiting_user_foundation", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(session.WorkingMemory.Mission.CreativePhase, "foundation_intake", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(session.WorkingMemory.MissionPlan.Stage, "foundation", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(session.WorkingMemory.MissionPlan.Status, "blocked", StringComparison.OrdinalIgnoreCase));

    private async Task<AgentToolExecutionResult> QueryProjectStatusAsync(AgentSession session, StoryBibleDocument bible, CancellationToken ct)
    {
        var materialCount = await CountProjectMaterialsAsync(session, ct).ConfigureAwait(false);
        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        var plan = session.WorkingMemory.MissionPlan;
        var lines = new List<string>();
        lines.Add($"任务黑板：{plan.ProjectTitle}/{plan.Stage}/{plan.Status}");
        if (!string.IsNullOrWhiteSpace(plan.LastVerifiedState))
            lines.Add($"最近核验状态：{plan.LastVerifiedState}");
        if (plan.AllowedNextActions.Count > 0)
            lines.Add($"允许下一步：{string.Join("、", plan.AllowedNextActions.Take(8))}");
        if (plan.SchedulerState.Tasks.Count > 0)
        {
            var taskLines = plan.SchedulerState.Tasks.Take(5)
                .Select(t => $"{t.ChapterId}:{t.NextAction}/{t.Status}" + (string.IsNullOrWhiteSpace(t.BlockedReason) ? "" : $"({t.BlockedReason})"));
            lines.Add($"任务队列：{string.Join("；", taskLines)}");
        }
        if (bible.Constitution == null)
            lines.Add("Story Bible 尚未固化。");
        else
            lines.Add($"Story Bible：{bible.Constitution.Genre}/{bible.Constitution.SubGenre}；核心钩子：{bible.Constitution.CoreHook}");
        lines.Add($"素材：{materialCount} 份；卷规划：{bible.VolumeArcs.Count}；设定/伏笔/角色账本：{bible.CanonLedger.Count}/{bible.ForeshadowLedger.Count}/{bible.CharacterLedger.Count}");
        if (currentRun != null)
            lines.Add($"当前 Run：{currentRun.Intent}/{currentRun.Status}");

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = string.Join("\n", lines),
            Phase = "query_project",
            Artifact = BuildArtifact("project_status", "story_bible", string.Empty, currentRun?.RunId ?? string.Empty, string.Join("；", lines), bible.Constitution == null ? new[] { "开始规划故事地基" } : new[] { "规划下一章", "查看书城" }),
            Suggestions = bible.Constitution == null ? new[] { "开始规划故事地基" } : new[] { "规划下一章", "查看书城" },
        };
    }

    private async Task<AgentToolExecutionResult> QueryProjectContentAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法读取项目卷章正文。",
                Phase = "query_project_content",
                Suggestions = new[] { "先绑定小说项目", "查看工作台项目列表" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var userRole = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase);

        var projectQuery = db.NovelProjects.AsNoTracking().Where(p => p.Id == session.ActiveProjectId);
        if (!isAdmin)
            projectQuery = projectQuery.Where(p => p.UserId == session.UserId);
        var project = await projectQuery.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (project == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到当前会话可读取的小说项目。",
                Phase = "query_project_content",
                Suggestions = new[] { "重新绑定项目", "查看工作台状态" }
            };
        }

        var chapterId = Arg(call, "chapterId");
        var chapterNumber = ArgInt(call, "chapterNumber");
        var volumeNumber = ArgInt(call, "volumeNumber");
        var includeBody = ArgBool(call, "includeBody", fallback: false);
        var includeFacts = ArgBool(call, "includeFacts", fallback: true);

        var chapterQuery = db.Chapters
            .AsNoTracking()
            .Include(c => c.Volume)
            .Where(c => c.ProjectId == project.Id);
        if (!string.IsNullOrWhiteSpace(chapterId))
            chapterQuery = chapterQuery.Where(c => c.Id == chapterId || c.Title == chapterId);
        if (chapterNumber > 0)
            chapterQuery = chapterQuery.Where(c => c.ChapterNumber == chapterNumber);
        if (volumeNumber > 0)
            chapterQuery = chapterQuery.Where(c => c.Volume != null && c.Volume.VolumeNumber == volumeNumber);

        var chapters = await chapterQuery
            .OrderBy(c => c.ChapterNumber)
            .Take(string.IsNullOrWhiteSpace(chapterId) && chapterNumber <= 0 ? 12 : 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (chapters.Count == 0)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到匹配的章节内容。",
                Phase = "query_project_content",
                Suggestions = new[] { "换一个章节号", "先查询项目状态", "查看书城章节列表" }
            };
        }

        var items = new List<ProjectContentQueryItem>();
        foreach (var chapter in chapters)
        {
            var body = string.Empty;
            try
            {
                body = await contentDocuments.GetTextAsync(
                    project.UserId,
                    project.Id,
                    "chapter",
                    chapter.Id,
                    "chapter_body",
                    ct).ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                var chunks = await db.ContentChunks
                    .AsNoTracking()
                    .Where(c => c.Document.SourceType == "chapter" &&
                                c.Document.SourceId == chapter.Id &&
                                c.Document.ProjectId == project.Id &&
                                c.Document.DocumentRole == "chapter_body" &&
                                c.Document.Status == "active")
                    .OrderBy(c => c.ChunkIndex)
                    .Select(c => c.ChunkText)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                body = string.Join("", chunks);
            }

            var facts = includeFacts
                ? bible.ContinuityFacts
                    .Where(f => string.Equals(f.ChapterId, chapter.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<ChapterContinuityFacts>();
            items.Add(new ProjectContentQueryItem
            {
                ChapterId = chapter.Id,
                ChapterNumber = chapter.ChapterNumber,
                ChapterTitle = chapter.Title,
                VolumeId = chapter.VolumeId ?? string.Empty,
                VolumeNumber = chapter.Volume?.VolumeNumber ?? 0,
                VolumeTitle = chapter.Volume?.Title ?? string.Empty,
                WordCount = chapter.WordCount,
                Status = chapter.Status,
                BodyPreview = TrimBody(body, includeBody ? 2000 : 240),
                Body = includeBody ? body : string.Empty,
                ContinuityFacts = facts
            });
        }

        var message = FormatProjectContentQuery(project.Title, items, includeBody);
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = "query_project_content",
            Data = new ProjectContentQueryResult
            {
                ProjectId = project.Id,
                ProjectTitle = project.Title,
                Items = items
            },
            Artifact = BuildArtifact(
                "project_content_query",
                items.First().ChapterId,
                project.Id,
                string.Empty,
                $"已读取 {items.Count} 个章节内容。",
                new[] { "基于真实章节回答", "继续下一章" }),
            Suggestions = new[] { "基于真实章节回答", "继续下一章" }
        };
    }

    private async Task<AgentToolExecutionResult> SearchCreativeKnowledgeAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var query = Arg(call, "query");
        var result = await _workspace.Orchestrator.RetrieveCreativeKnowledgeAsync(query, ct).ConfigureAwait(false);
        var dbHits = await SearchDatabaseKnowledgeAsync(query, session, ct).ConfigureAwait(false);

        if (dbHits.Count > 0)
        {
            var seen = result.Hits.Select(h => h.Entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in dbHits)
            {
                if (seen.Add(hit.Entry.Id))
                    result.Hits.Insert(0, hit);
            }

            result.Message = $"创意知识库命中 {result.Hits.Count} 条（SQLite/Qdrant/Redis 知识通路）。";
        }

        var lines = result.Hits.Count == 0
            ? new List<string> { "创意知识库里没有检索到强相关条目。" }
            : result.Hits.Take(8).Select(h => $"【{h.Entry.Category}】{h.Entry.Title}\n{h.Entry.Content}").ToList();

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = string.Join("\n\n", lines),
            Phase = "knowledge_retrieved",
            Data = result,
            Artifact = BuildArtifact("knowledge_hits", query, string.Empty, string.Empty, $"检索到 {result.Hits.Count} 条创意知识。", new[] { "基于知识继续构思", "规划下一章" }),
            Suggestions = new[] { "基于这些知识继续构思", "规划下一章" },
        };
    }

    private async Task<List<CreativeKnowledgeHit>> SearchDatabaseKnowledgeAsync(string query, AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            _logger.LogDebug("Skipping DB knowledge search because session {SessionId} has no active project", session.SessionId);
            return new List<CreativeKnowledgeHit>();
        }

        var knowledgeService = _serviceProvider.GetService<IKnowledgeService>();
        if (knowledgeService == null)
        {
            _logger.LogDebug("Skipping DB knowledge search because IKnowledgeService is unavailable");
            return new List<CreativeKnowledgeHit>();
        }

        try
        {
            var results = await knowledgeService.SearchKnowledgeAsync(new SearchKnowledgeRequest
            {
                ProjectId = session.ActiveProjectId,
                Query = query,
                TopK = 8
            }, ct).ConfigureAwait(false);

            foreach (var result in results)
            {
                await knowledgeService.IncrementUsageAsync(
                    result.Id,
                    session.ActiveProjectId,
                    session.SessionId,
                    session.ActiveRunId,
                    ct).ConfigureAwait(false);
            }

            return results
                .Select(MapKnowledgeSearchResult)
                .ToList();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "DB knowledge search unavailable for project {ProjectId}; returning an empty knowledge hit set", session.ActiveProjectId);
            return new List<CreativeKnowledgeHit>();
        }
    }

    private async Task<int> CountProjectMaterialsAsync(AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return 0;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await db.Materials
            .AsNoTracking()
            .CountAsync(m => m.UserId == session.UserId && m.ProjectId == session.ActiveProjectId, ct)
            .ConfigureAwait(false);
    }

    private static CreativeKnowledgeHit MapKnowledgeSearchResult(KnowledgeSearchResult result)
    {
        return new CreativeKnowledgeHit
        {
            Entry = new CreativeKnowledgeEntry
            {
                Id = result.Id,
                Category = ParseKnowledgeCategory(result.EntryType),
                Title = result.Title,
                Content = result.Content,
                Source = "DBKnowledge"
            },
            Score = result.Score,
            Reason = "DB knowledge search"
        };
    }

    private static CreativeKnowledgeCategory ParseKnowledgeCategory(string value) =>
        Enum.TryParse<CreativeKnowledgeCategory>(value, ignoreCase: true, out var category)
            ? category
            : CreativeKnowledgeCategory.ProjectUsedPattern;

    private async Task<AgentToolExecutionResult> PlanStoryFoundationAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation(
            "PlanStoryFoundation starting: sessionProject={SessionProjectId}, workspaceProject={WorkspaceProjectId}, user={UserId}",
            session.ActiveProjectId,
            _workspace.ProjectId,
            _workspace.UserId);

        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var userSeed = Arg(call, "userSeed", Arg(call, "creativeBrief", session.WorkingMemory.CurrentGoal));
        var genre = Arg(call, "genre", ExtractGenre(userSeed, settings.DefaultGenre));
        var subGenre = Arg(call, "subGenre", settings.DefaultSubGenre);
        var targetReader = Arg(call, "targetReader");
        var desiredDirection = Arg(call, "desiredDirection");
        var run = await _workspace.Orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = userSeed,
            Genre = genre,
            SubGenre = subGenre,
            TargetReader = targetReader,
            DesiredDirection = desiredDirection,
            CandidateDirections = ArgList(call, "candidateDirections"),
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "foundation_candidates";
        var hasCandidates = run.MacroCandidates.Count > 0;

        return new AgentToolExecutionResult
        {
            Success = hasCandidates,
            Message = FormatFoundationCandidates(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = hasCandidates
                ? BuildArtifact("story_foundation_candidates", run.RunId, session.ActiveProjectId, run.RunId, $"生成 {run.MacroCandidates.Count} 个故事地基候选。", run.MacroCandidates.Select(c => c.Title).Take(3).ToArray())
                : null,
            Suggestions = hasCandidates
                ? run.MacroCandidates.Select((c, i) => $"选第{i + 1}个: {c.Title}").ToArray()
                : new[] { "补充正向创作方向", "重新生成故事地基" },
        };
    }

    private async Task<AgentToolExecutionResult> CommitStoryFoundationAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var selectedTitle = Arg(call, "selectedMacroCandidateTitle");
        var selectedId = Arg(call, "selectedMacroCandidateId");
        var selectedIndex = ArgInt(call, "selectedMacroCandidateIndex");
        var result = await _workspace.Orchestrator.CommitStoryFoundationAsync(runId, false, confirmed, selectedTitle, selectedId, selectedIndex, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "foundation_committed" : session.Phase;
        if (result.Success)
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("story_foundation_commit", runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "规划第一卷", "查看当前状态" } : new[] { "重新选择候选", "修改候选" }),
            Suggestions = result.Success ? new[] { "规划第一卷", "查看当前状态" } : new[] { "重新选择候选", "修改候选" },
        };
    }

    private async Task<AgentToolExecutionResult> PlanVolumeArcAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var chapterCount = Math.Clamp(settings.DefaultVolumeChapterCount, 1, 200);
        var nextVolumeNumber = NextVolumeNumber(bible);
        var startChapterNumber = NextStartChapterNumber(bible);
        var volumeId = Arg(call, "volumeId", $"volume-{nextVolumeNumber:000}");
        var volumeTitle = Arg(call, "volumeTitle", $"第{nextVolumeNumber}卷");
        var startChapterId = Arg(call, "startChapterId", $"chapter-{startChapterNumber:000}");
        var endChapterId = Arg(call, "endChapterId", $"chapter-{(startChapterNumber + chapterCount - 1):000}");
        var run = await _workspace.Orchestrator.PlanVolumeArcAsync(new VolumeArcPlanningRequest
        {
            UserGoal = Arg(call, "creativeBrief", "规划下一卷的大框架"),
            VolumeId = volumeId,
            VolumeTitle = volumeTitle,
            StartChapterId = startChapterId,
            EndChapterId = endChapterId,
            ExpectedChapterCount = chapterCount,
            CandidateDirections = ArgList(call, "candidateDirections"),
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "volume_plan";

        return new AgentToolExecutionResult
        {
            Success = run.VolumeArcPlan != null,
            Message = FormatVolumePlan(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = BuildArtifact("volume_arc_plan", run.RunId, session.ActiveProjectId, run.RunId, run.VolumeArcPlan == null ? "卷规划生成失败。" : $"卷规划「{run.VolumeArcPlan.Title}」已生成。", new[] { "提交入库", "调整卷规划" }),
            Suggestions = new[] { "提交入库", "调整卷规划" },
        };
    }

    private async Task<AgentToolExecutionResult> CommitVolumeArcAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.CommitVolumeArcAsync(runId, false, confirmed, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "volume_committed" : session.Phase;
        if (result.Success)
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("volume_arc_commit", runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "规划第一章", "查看当前状态" } : new[] { "继续修改", "查看当前状态" }),
            Suggestions = result.Success ? new[] { "规划第一章", "查看当前状态" } : new[] { "继续修改", "查看当前状态" },
        };
    }

    private async Task<AgentToolExecutionResult> PlanChapterAsync(AgentToolCall call, AgentSession session, StoryBibleDocument bible, CancellationToken ct)
    {
        if (call.Arguments.ContainsKey("userGoal"))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanChapter 已废弃 userGoal 参数。请使用 creativeBrief 和 sourceTurnId。",
                Phase = session.Phase,
                Suggestions = new[] { "查看当前状态", "补充章节创作简报" },
            };
        }

        var creativeBrief = Arg(call, "creativeBrief");
        if (string.IsNullOrWhiteSpace(creativeBrief))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanChapter 需要 creativeBrief。Agent 不能把用户原话直接塞进章节工具。",
                Phase = session.Phase,
                Suggestions = new[] { "给出章节创作简报", "查看当前任务" },
            };
        }

        var chapterId = Arg(call, "chapterId");
        if (string.IsNullOrWhiteSpace(chapterId))
        {
            var existing = bible.AgentRuns.Where(r => !string.IsNullOrWhiteSpace(r.TargetChapterId)).Select(r => r.TargetChapterId).Distinct().Count();
            chapterId = $"chapter-{(existing + 1):000}";
        }

        var run = await _workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            UserGoal = creativeBrief,
            ChapterId = chapterId,
            CandidateDirections = ArgList(call, "candidateDirections"),
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "chapter_candidates";

        return new AgentToolExecutionResult
        {
            Success = run.ChapterBrief?.Candidates.Count > 0,
            Message = FormatChapterCandidates(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = BuildArtifact("chapter_candidates", run.TargetChapterId, session.ActiveProjectId, run.RunId, $"为 {run.TargetChapterId} 生成章节候选。", new[] { "选择推荐，开始生成", "选其他候选" }),
            Suggestions = new[] { "选择推荐，开始生成", "选其他候选" },
        };
    }

    private async Task<AgentToolExecutionResult> SelectChapterCandidateAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.SelectChapterCandidateAsync(
            runId,
            Arg(call, "candidateTitles"),
            Arg(call, "selectionMode", "Recommended"),
            Arg(call, "selectionRationale"),
            true,
            ct).ConfigureAwait(false);

        session.Phase = result.Success ? "candidate_selected" : session.Phase;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success ? "章节方向已选定。接下来可以开始生成正文。" : result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("chapter_candidate_selection", runId, session.ActiveProjectId, runId, result.Success ? "章节方向已选定。" : result.Message, result.Success ? new[] { "开始生成正文", "先看简报" } : new[] { "换一个候选", "重新规划这一章" }),
            Suggestions = result.Success ? new[] { "开始生成正文", "先看简报" } : new[] { "换一个候选", "重新规划这一章" },
        };
    }

    private static string BuildConfirmationMessage(string toolName) =>
        toolName switch
        {
            "CommitStoryFoundation" => "这个操作会把故事地基写入 Story Bible，并继续推进后续规划。",
            "CommitVolumeArc" => "这个操作会把卷规划写入 Story Bible，并继续推进章节生产线。",
            "GenerateChapterWithChanges" => "这个操作会生成章节草稿和 CHANGES，随后进入硬门禁校验。",
            "RepairChapterDraft" => "这个操作会按门禁失败项修复章节草稿和 CHANGES，并重新校验。",
            "CommitValidatedChapter" => "这个操作会把已通过门禁的章节提交进书城，并刷新事实快照和索引。",
            _ => "这个操作会改变小说工程状态，并由 Agent 继续推进。",
        };

    private async Task<AgentToolExecutionResult> GenerateChapterWithChangesAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.GenerateChapterWithChangesAsync(runId, confirmed, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "draft_generated" : result.GateReport?.Status ?? session.Phase;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("生成章节正文", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(
                result.Success ? "chapter_draft_with_changes" : "chapter_generation_blocked",
                result.Run?.TargetChapterId ?? runId,
                session.ActiveProjectId,
                runId,
                result.Message,
                result.Success ? new[] { "执行硬门禁校验", "查看草稿" } : new[] { "选择章节候选", "补齐上下文" }),
            Suggestions = result.Success ? new[] { "执行硬门禁校验", "查看草稿" } : new[] { "选择章节候选", "补齐上下文" },
        };
    }

    private async Task<AgentToolExecutionResult> BuildChapterContextPackageAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.BuildChapterContextPackageAsync(runId, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "context_ready" : session.Phase;
        var package = result.ContextPackage;
        var message = result.Success && package != null
            ? $"上下文包：世界规则 {package.WorldRules.Count}，角色状态 {package.CharacterStates.Count}，长距离召回 {package.LongDistanceRecall.Count}，警告 {package.Warnings.Count}。"
            : result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("chapter_context_package", package?.ChapterId ?? runId, session.ActiveProjectId, runId, message, new[] { "生成正文和 CHANGES", "查看上下文摘要" }),
            Suggestions = new[] { "生成正文和 CHANGES", "查看上下文摘要" },
        };
    }

    private async Task<AgentToolExecutionResult> ValidateChapterDraftAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.ValidateChapterDraftAsync(runId, ct).ConfigureAwait(false);
        var report = result.GateReport;
        session.Phase = report?.Status ?? session.Phase;
        var message = result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("generation_gate_report", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, message, report?.Status == "validated" ? new[] { "提交成稿", "继续下一章" } : new[] { "修复章节草稿", "查看失败项" }),
            Suggestions = report?.Status == "validated" ? new[] { "提交成稿", "继续下一章" } : new[] { "修复章节草稿", "查看失败项" },
        };
    }

    private async Task<AgentToolExecutionResult> RepairChapterDraftAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.RepairChapterDraftAsync(runId, confirmed, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "validated" : result.GateReport?.Status ?? session.Phase;
        var repairAttempt = result.DraftArtifact?.RepairAttemptCount ?? 0;
        var canAutoRepair = !result.Success && result.GateReport != null && repairAttempt < 3;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("修复章节草稿", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            IsRepairable = canAutoRepair,
            RecommendedToolName = canAutoRepair ? "RepairChapterDraft" : string.Empty,
            RecommendedArguments = canAutoRepair
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runId"] = runId,
                    ["repairAttempt"] = (repairAttempt + 1).ToString(),
                    ["repairStrategy"] = SelectRepairStrategy(repairAttempt + 1)
                }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = canAutoRepair ? "chapter_draft_repair" : string.Empty,
            Data = result,
            Artifact = BuildArtifact(result.Success ? "chapter_draft_repaired" : "chapter_repair_report", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" }),
            Suggestions = result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" },
        };
    }

    private static string SelectRepairStrategy(int attempt) =>
        attempt switch
        {
            <= 1 => "patch_missing_continuity",
            2 => "rewrite_continuity_scene",
            _ => "regenerate_opening_with_hard_facts"
        };

    private async Task<AgentToolExecutionResult> CommitValidatedChapterAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.CommitValidatedChapterAsync(runId, confirmed, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "committed" : session.Phase;
        if (result.Success)
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("提交章节到书城", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(result.Success ? "chapter_committed" : "chapter_commit_blocked", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "继续下一章", "查看书城" } : new[] { "校验草稿", "查看失败项" }),
            Suggestions = result.Success ? new[] { "继续下一章", "查看书城" } : new[] { "校验草稿", "查看失败项" },
        };
    }

    private async Task<AgentToolExecutionResult> RefreshProjectIndexesAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.RefreshProjectIndexesAsync(runId, ct).ConfigureAwait(false);
        var impact = result.DependencyImpact;
        var message = result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = impact?.Status ?? session.Phase,
            Data = result,
            Artifact = BuildArtifact("dependency_impact", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, message, new[] { "查看工作流", "继续下一章" }),
            Suggestions = new[] { "查看工作流", "继续下一章" },
        };
    }

    private Task<AgentToolExecutionResult> AnalyzeDependencyImpactAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        return RefreshProjectIndexesAsync(call, session, ct);
    }

    private async Task<AgentToolExecutionResult> ReviewChapterAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.ReviewGeneratedChapterAsync(runId, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "chapter_reviewed" : session.Phase;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("chapter_review", runId, session.ActiveProjectId, runId, result.Message, new[] { "提交成稿", "查看评审报告" }),
            Suggestions = new[] { "提交成稿", "查看评审报告" },
        };
    }

    private static string BuildWritingFailureMessage(
        string stage,
        string phase,
        string originalMessage,
        bool hasDraft,
        bool hasGateReport)
    {
        var artifacts = new List<string>();
        if (hasDraft) artifacts.Add("章节草稿");
        if (hasGateReport) artifacts.Add("门禁报告");
        if (artifacts.Count == 0) artifacts.Add("暂无可用产物");

        return string.Join("\n", new[]
        {
            $"失败阶段：{stage}（当前状态：{(string.IsNullOrWhiteSpace(phase) ? "未知" : phase)}）",
            $"已有产物：{string.Join("、", artifacts)}",
            $"失败原因：{originalMessage}",
            hasGateReport ? "可继续动作：查看失败项后修复章节草稿，或补充设定后重试。" : "可继续动作：补齐上下文或重新生成章节草稿。",
            hasGateReport ? "是否需要用户决定：若失败项涉及设定取舍，需要用户确认；格式/连续性问题可继续自动修复。" : "是否需要用户决定：通常不需要，除非缺少关键创作设定。"
        });
    }

    private async Task<NovelAgentRun?> FindRunAsync(string runId, CancellationToken ct)
    {
        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        return bible.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatProjectContentQuery(
        string projectTitle,
        IReadOnlyList<ProjectContentQueryItem> items,
        bool includeBody)
    {
        var lines = new List<string> { $"项目「{projectTitle}」内容查询结果：" };
        foreach (var item in items)
        {
            lines.Add(
                $"第 {item.ChapterNumber} 章：{item.ChapterTitle}\n" +
                $"所属卷：{(item.VolumeNumber > 0 ? $"第 {item.VolumeNumber} 卷" : "未绑定卷")} {item.VolumeTitle}\n" +
                $"状态：{item.Status}；字数：{item.WordCount}\n" +
                $"正文{(includeBody ? "" : "开头")}：{item.BodyPreview}");
            foreach (var facts in item.ContinuityFacts)
            {
                var factLines = new[]
                {
                    string.IsNullOrWhiteSpace(facts.ProtagonistName) ? string.Empty : $"主角：{facts.ProtagonistName}",
                    string.IsNullOrWhiteSpace(facts.ProtagonistIdentity) ? string.Empty : $"身份：{facts.ProtagonistIdentity}",
                    string.IsNullOrWhiteSpace(facts.ProtagonistStatus) ? string.Empty : $"状态：{facts.ProtagonistStatus}",
                    string.IsNullOrWhiteSpace(facts.EndingState) ? string.Empty : $"结尾：{facts.EndingState}",
                    facts.NextChapterMustCarry.Count == 0 ? string.Empty : $"下一章必须承接：{string.Join("；", facts.NextChapterMustCarry)}"
                }.Where(s => !string.IsNullOrWhiteSpace(s));
                lines.Add("关键事实：" + string.Join("；", factLines));
            }
        }

        return string.Join("\n\n", lines);
    }

    private static string TrimBody(string body, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(body)
            ? "未找到正文内容。"
            : body.Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static AgentToolArtifact BuildArtifact(
        string type,
        string artifactId,
        string projectId,
        string runId,
        string summary,
        IReadOnlyList<string> nextHints) => new()
        {
            ArtifactType = type,
            ArtifactId = artifactId,
            ProjectId = projectId,
            RunId = runId,
            Summary = summary,
            NextHints = nextHints,
            VisibleInWorkflow = IsWorkflowArtifact(type),
            VisibleInLibrary = string.Equals(type, "chapter_committed", StringComparison.OrdinalIgnoreCase),
            UserVisibleStatus = MapArtifactStatus(type),
        };

    private static bool IsWorkflowArtifact(string type) =>
        type is not "knowledge_hits";

    private static string MapArtifactStatus(string type) =>
        type switch
        {
            "novel_project" or "existing_novel_project" => "新书任务已创建",
            "project_bound" => "已有项目已绑定",
            "story_foundation_candidates" => "故事地基候选已生成",
            "story_foundation_commit" => "故事地基已固化",
            "volume_arc_plan" => "卷规划已生成",
            "volume_arc_commit" => "卷规划已提交",
            "chapter_candidates" => "章节候选已生成",
            "chapter_candidate_selection" => "章节候选已选定",
            "chapter_context_package" => "章节上下文包已就绪",
            "chapter_draft_with_changes" => "章节草稿已生成",
            "chapter_generation_blocked" => "章节生成被阻塞",
            "generation_gate_report" => "章节门禁已校验",
            "chapter_draft_repaired" => "章节草稿已修复",
            "chapter_repair_report" => "章节修复需要处理",
            "chapter_committed" => "章节已提交入库",
            "chapter_commit_blocked" => "章节提交被阻塞",
            "dependency_impact" => "依赖影响已分析",
            "chapter_review" => "章节复盘已生成",
            "project_content_query" => "项目内容已读取",
            _ => type,
        };

    private static string FormatFoundationCandidates(NovelAgentRun run)
    {
        if (run.MacroCandidates.Count == 0) return "未能生成故事地基候选。";
        var lines = new List<string> { $"生成了 {run.MacroCandidates.Count} 个故事地基候选：" };
        for (var i = 0; i < run.MacroCandidates.Count; i++)
        {
            var c = run.MacroCandidates[i];
            lines.Add(
                $"【{i + 1}】{c.Title}\n" +
                $"核心钩子：{c.CoreHook}\n" +
                $"世界观：{c.WorldbuildingBlueprint}\n" +
                $"升级/能力体系：{c.ProgressionSystem}\n" +
                $"主角：{c.ProtagonistProfile}\n" +
                $"爽点循环：{c.PleasureLoop}\n" +
                $"前三卷：{string.Join(" / ", c.FirstThreeVolumes)}\n" +
                $"首批角色：{string.Join("；", c.KeyCharacters)}\n" +
                $"新颖度/可持续/类型匹配：{c.NoveltyScore}/{c.SustainabilityScore}/{c.TypeMatchScore}");
        }
        return string.Join("\n\n", lines);
    }

    private static string FormatVolumePlan(NovelAgentRun run)
    {
        var plan = run.VolumeArcPlan;
        if (plan == null) return "卷规划生成失败。";
        return $"第一卷「{plan.Title}」规划完成：\n卷承诺：{plan.VolumePromise}\n核心问题：{plan.CoreQuestion}\n中点反转：{plan.MidpointReversal}\n高潮：{plan.Climax}\n章节节拍：{plan.ChapterBeats.Count} 个";
    }

    private static string FormatChapterCandidates(NovelAgentRun run)
    {
        var brief = run.ChapterBrief;
        if (brief == null || brief.Candidates.Count == 0) return "章节候选生成失败。";
        var lines = new List<string> { $"为 {brief.ChapterId} 生成了 {brief.Candidates.Count} 个剧情候选：" };
        for (var i = 0; i < brief.Candidates.Count; i++)
        {
            var c = brief.Candidates[i];
            lines.Add($"【{i + 1}】{c.Title}\n转折：{c.CoreTwist}\n角色选择：{c.CharacterChoice}\n代价：{c.CostOrConsequence}\n总分：{c.TotalScore}");
        }
        if (!string.IsNullOrWhiteSpace(brief.RecommendedCandidateTitle))
            lines.Add($"推荐：「{brief.RecommendedCandidateTitle}」。");
        return string.Join("\n\n", lines);
    }

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static int ArgInt(AgentToolCall call, string name, int fallback = 0) =>
        call.Arguments.TryGetValue(name, out var value) && int.TryParse(value?.Trim(), out var parsed) ? parsed : fallback;

    private static List<string> ArgList(AgentToolCall call, string name)
    {
        if (!call.Arguments.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            return new List<string>();

        var text = value.Trim();
        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(text) is { Count: > 0 } items
                    ? items.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()
                    : new List<string>();
            }
            catch
            {
                // Fall through to delimiter parsing.
            }
        }

        return text
            .Split(new[] { '\n', ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int NextVolumeNumber(StoryBibleDocument bible) =>
        Math.Max(1, bible.VolumeArcs.Count + 1);

    private static int NextStartChapterNumber(StoryBibleDocument bible)
    {
        var maxChapter = 0;
        foreach (var volume in bible.VolumeArcs)
        {
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(volume.EndChapterId));
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(volume.StartChapterId) + Math.Max(0, volume.ExpectedChapterCount - 1));
        }

        foreach (var run in bible.AgentRuns.Where(r => !string.IsNullOrWhiteSpace(r.TargetChapterId)))
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(run.TargetChapterId));

        return maxChapter + 1;
    }

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static string ExtractGenre(string message, string fallback)
    {
        if (message.Contains("玄幻")) return "玄幻";
        if (message.Contains("仙侠")) return "仙侠";
        if (message.Contains("悬疑")) return "悬疑";
        if (message.Contains("都市")) return "都市";
        if (message.Contains("科幻")) return "科幻";
        if (message.Contains("历史")) return "历史";
        if (message.Contains("言情")) return "言情";
        if (message.Contains("恐怖")) return "恐怖";
        return string.IsNullOrWhiteSpace(fallback) ? "玄幻" : fallback;
    }

    private async Task<AgentToolExecutionResult> ProcessKnowledgeFileAsync(
        AgentToolCall call,
        CancellationToken ct)
    {
        var taskId = call.Arguments.GetValueOrDefault("taskId")?.ToString();
        if (string.IsNullOrEmpty(taskId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "错误：缺少 taskId 参数",
                Phase = "error",
            };
        }

        try
        {
            // Get DbContext from the service provider's scope
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

            var task = await db.KnowledgeProcessingTasks
                .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == _workspace.UserId, ct)
                .ConfigureAwait(false);

            if (task == null)
            {
                return new AgentToolExecutionResult
                {
                    Success = false,
                    Message = "错误：找不到指定的处理任务",
                    Phase = "error",
                };
            }

            if (task.Status != "pending")
            {
                return new AgentToolExecutionResult
                {
                    Success = false,
                    Message = $"错误：任务状态为 {task.Status}，无法处理",
                    Phase = "error",
                };
            }

            var processingService = scope.ServiceProvider.GetRequiredService<IKnowledgeProcessingService>();
            var result = await processingService.ProcessFileAsync(taskId, ct).ConfigureAwait(false);

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = result,
                Phase = "knowledge_processed",
                Artifact = BuildArtifact("knowledge_file_processed", taskId, string.Empty, string.Empty, result, new[] { "查看知识库", "继续规划" }),
                Suggestions = new[] { "查看知识库", "继续规划" },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process knowledge file {TaskId}", taskId);
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"处理失败：{ex.Message}",
                Phase = "error",
            };
        }
    }

    private async Task<AgentToolExecutionResult> ToolSearchAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var phaseArg = NormalizeToolSearchPhase(Arg(call, "phase", session.Phase));
        var query = FirstNonEmpty(Arg(call, "query"), Arg(call, "intent"), Arg(call, "context"), session.WorkingMemory.CurrentGoal);
        var intent = Arg(call, "intent");
        var context = Arg(call, "context");
        var includeAll = ArgBool(call, "includeAll");
        var requestedLimit = ArgInt(call, "limit", 12);
        var limit = includeAll
            ? int.MaxValue
            : Math.Clamp(requestedLimit <= 0 ? 12 : requestedLimit, 1, 50);

        var rankedTools = SearchToolDefinitions(query, intent, context, phaseArg, includeAll, limit);
        var toolNames = rankedTools.Select(def => def.Name).ToArray();

        var toolList = toolNames
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => $"• {def!.Name}（{def.Description}；空间={def.Semantic.DomainSurface}；产物={def.Semantic.OutputKind}；可见位置={def.Semantic.UserVisibleWhere}）")
            .ToList();

        var message = toolList.Count == 0
            ? "已读取当前创作上下文，正在重新判断下一步。"
            : $"已准备 {toolList.Count} 项可用创作能力，正在选择最适合当前目标的下一步。";

        var discoveredTools = rankedTools
            .Select(def => new ToolSchema
            {
                Name = def.Name,
                Description = def.Description,
                Risk = def.Risk,
                RequiresConfirmation = def.RequiresConfirmation,
                Parameters = def.Arguments?.ToDictionary(p => p, _ => "string") ?? new Dictionary<string, string>(),
                SideEffects = def.SideEffects,
                Semantic = def.Semantic
            })
            .ToList();

        using var scope = _serviceProvider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IToolSearchCacheService>();
        var searchScope = BuildToolSearchScopeKey(query, intent, context, phaseArg, includeAll);
        await cache.SaveAsync(session, searchScope, discoveredTools, ct).ConfigureAwait(false);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = session.Phase,
            Data = new { Query = query, PhaseHint = phaseArg, ScopeKey = searchScope, Tools = toolNames, ToolDetails = toolList },
            Artifact = BuildArtifact("tool_search_result", searchScope, session.ActiveProjectId ?? string.Empty, string.Empty, $"已准备 {toolList.Count} 项可用创作能力。", Array.Empty<string>()),
            Suggestions = Array.Empty<string>(),
        };
    }

    private IReadOnlyList<AgentToolDefinition> SearchToolDefinitions(
        string query,
        string intent,
        string context,
        string phaseHint,
        bool includeAll,
        int limit)
    {
        var phase = phaseHint switch
        {
            "Planning" => ConversationPhase.Planning,
            "Creation" => ConversationPhase.Creation,
            "Review" => ConversationPhase.Review,
            _ => ConversationPhase.Conversation,
        };
        var searchText = string.Join(' ', new[] { query, intent, context }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var terms = TokenizeToolSearchText(searchText);

        return _entries.Values
            .Where(entry => !string.Equals(entry.Definition.Name, "tool_search", StringComparison.OrdinalIgnoreCase))
            .Select((entry, index) => new
            {
                Definition = entry.Definition,
                Index = index,
                Score = includeAll ? 0 : ScoreTool(entry.Definition, terms),
                HintRank = CategoryHintRank(entry.Category, phase)
            })
            .Where(x => includeAll || terms.Count == 0 || x.Score > 0 || x.HintRank >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.HintRank < 0 ? int.MaxValue : x.HintRank)
            .ThenBy(x => x.Index)
            .Take(limit)
            .Select(x => x.Definition)
            .ToList();
    }

    private static int ScoreTool(AgentToolDefinition tool, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;

        var haystack = string.Join(' ', new[]
        {
            tool.Name,
            tool.Description,
            tool.Risk,
            tool.Semantic.DomainSurface,
            tool.Semantic.OutputKind,
            tool.Semantic.UserVisibleWhere,
            tool.Semantic.ResultSemantics,
            string.Join(' ', tool.Arguments),
            string.Join(' ', tool.Semantic.ReadsFrom),
            string.Join(' ', tool.Semantic.WritesTo),
        }).ToLowerInvariant();

        var score = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 6 : 2;
        }
        return score;
    }

    private static IReadOnlyList<string> TokenizeToolSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '，', '。', '；', '：', '、', '(', ')', '（', '）', '[', ']', '【', '】', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static int CategoryHintRank(string category, ConversationPhase phase)
    {
        return phase switch
        {
            ConversationPhase.Planning => category switch
            {
                "planning" => 0,
                "project" => 1,
                "content" or "knowledge" or "rag" => 2,
                "blackboard" or "workspace" => 3,
                "commit" => 4,
                _ => 10,
            },
            ConversationPhase.Creation => category switch
            {
                "writing" => 0,
                "gate" => 1,
                "planning" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            ConversationPhase.Review => category switch
            {
                "review" or "gate" => 0,
                "commit" => 1,
                "maintenance" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            _ => category switch
            {
                "workspace" or "blackboard" => 0,
                "project" or "content" => 1,
                "knowledge" or "rag" => 2,
                _ => 10,
            },
        };
    }

    private static bool ArgBool(AgentToolCall call, string name, bool fallback = false) =>
        call.Arguments.TryGetValue(name, out var value) && bool.TryParse(value?.Trim(), out var parsed) ? parsed : fallback;

    private static string BuildToolSearchScopeKey(string query, string intent, string context, string phaseHint, bool includeAll)
    {
        var normalized = string.Join("|", new[]
        {
            $"phaseHint={phaseHint}",
            $"includeAll={includeAll}",
            $"query={query}",
            $"intent={intent}",
            $"context={context}",
        }).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "global";
        var hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(normalized));
        return $"global:{phaseHint}:{hash:x}";
    }

    private static string NormalizeToolSearchPhase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Conversation";

        return value.Trim().ToLowerInvariant() switch
        {
            "planning" => "Planning",
            "creation" => "Creation",
            "review" => "Review",
            "all" => "All",
            "conversation" => "Conversation",
            _ => "Conversation"
        };
    }
}

public sealed class ProjectContentQueryResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public List<ProjectContentQueryItem> Items { get; set; } = new();
}

public sealed class ProjectContentQueryItem
{
    public string ChapterId { get; set; } = string.Empty;
    public int ChapterNumber { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string VolumeId { get; set; } = string.Empty;
    public int VolumeNumber { get; set; }
    public string VolumeTitle { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<ChapterContinuityFacts> ContinuityFacts { get; set; } = new();
}
