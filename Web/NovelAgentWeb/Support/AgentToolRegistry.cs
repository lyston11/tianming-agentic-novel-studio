using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Knowledge;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentToolRegistry
{
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private static readonly AsyncLocal<NovelProjectCatalog?> _currentCatalog = new();

    private NovelAgentWorkspace _workspace => _currentWorkspace.Value ?? throw new InvalidOperationException("Workspace not set for current request");
    private NovelProjectCatalog _catalog => _currentCatalog.Value ?? throw new InvalidOperationException("Catalog not set for current request");

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

    public AgentToolRegistry(UserSettingsManager settingsManager, IServiceProvider serviceProvider, ILogger<AgentToolRegistry> logger)
    {
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _entries = BuildEntries();
    }

    private static readonly string[] ConversationTools = new[]
    {
        "QueryProjectStatus",
        "ResolveNovelProject",
        "ProcessKnowledgeFile",
    };

    private static readonly string[] PlanningTools = new[]
    {
        "QueryProjectStatus",
        "ResolveNovelProject",
        "SearchCreativeKnowledge",
        "ProcessKnowledgeFile",
        "PlanStoryFoundation",
        "CommitStoryFoundation",
        "PlanVolumeArc",
        "CommitVolumeArc",
        "PlanChapter",
        "SelectChapterCandidate",
        "BuildChapterContextPackage",
    };

    private static readonly string[] CreationTools = new[]
    {
        "QueryProjectStatus",
        "GenerateChapterWithChanges",
        "RepairChapterDraft",
        "ValidateChapterDraft",
    };

    private static readonly string[] ReviewTools = new[]
    {
        "QueryProjectStatus",
        "CommitValidatedChapter",
        "ReviewChapter",
        "RefreshProjectIndexes",
        "AnalyzeDependencyImpact",
        "ProcessKnowledgeFile",
    };

    public IReadOnlyList<AgentToolDefinition> ListTools() => _entries.Values.Select(e => e.Definition).ToList();

    public IReadOnlyList<ToolSchema> ListToolSchemas() => _entries.Values.Select(e => new ToolSchema
    {
        Name = e.Definition.Name,
        Description = e.Definition.Description,
        Risk = e.Definition.Risk,
        RequiresConfirmation = e.Definition.RequiresConfirmation,
        Parameters = e.Definition.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
        SideEffects = e.Definition.SideEffects,
    }).ToList();

    public IReadOnlyList<string> GetToolNamesForPhase(ConversationPhase phase)
    {
        return phase switch
        {
            ConversationPhase.Conversation => ConversationTools,
            ConversationPhase.Planning => PlanningTools,
            ConversationPhase.Creation => CreationTools,
            ConversationPhase.Review => ReviewTools,
            _ => ConversationTools,
        };
    }

    public IReadOnlyList<ToolSchema> ListToolSchemasForPhase(ConversationPhase phase)
    {
        var allowedNames = GetToolNamesForPhase(phase);
        return _entries
            .Where(e => allowedNames.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
            .Select(e => new ToolSchema
            {
                Name = e.Value.Definition.Name,
                Description = e.Value.Definition.Description,
                Risk = e.Value.Definition.Risk,
                RequiresConfirmation = e.Value.Definition.RequiresConfirmation,
                Parameters = e.Value.Definition.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
                SideEffects = e.Value.Definition.SideEffects,
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
            Entry("tool_search", "meta", "Low", false, new[] { "phase" }, "搜索指定阶段的可用工具。phase参数（必填）可选值：Conversation（闲聊、问候、状态查询）、Planning（规划故事地基/卷/章节）、Creation（生成章节正文）、Review（提交章节、复盘）、All（返回全部工具）。根据用户意图和当前任务状态判断阶段。", Effects(toolCache: true, sqliteSnapshot: true, sqlite: new[] { "agent_tool_search_snapshots" }), (call, session, _, _, ct) => ToolSearchAsync(call, session, ct)),
            Entry("ResolveNovelProject", "project", "Low", false, new[] { "mode", "projectId", "projectTitle", "title", "genre", "seed" }, "由 Agent 决策绑定已有小说或创建新小说。mode 可选 bind_existing/create_new/auto；绑定不创建新书，创建会切换当前会话上下文。", Effects(memory: new[] { "session", "project" }, sqlite: new[] { "novel_projects", "agent_sessions" }), (call, session, _, _, ct) => ResolveNovelProjectAsync(call, session, ct)),
            Entry("ProcessKnowledgeFile", "knowledge", "Medium", true, new[] { "taskId" }, "处理已上传的知识文件，自动提取创意写作知识条目。支持结构化文档和创意素材。", Effects(memory: new[] { "project" }, sqlite: new[] { "knowledge_base", "knowledge_processing_tasks", "content_documents", "project_knowledge_usages" }, vector: new[] { "knowledge" }), (call, _, _, _, ct) => ProcessKnowledgeFileAsync(call, ct)),
            Entry("QueryProjectStatus", "blackboard", "Low", false, Array.Empty<string>(), "读取 MissionBlackboard、Story Bible、素材、账本、当前可操作 Run 状态。", Effects(), (call, session, bible, _, ct) => QueryProjectStatusAsync(session, bible, ct)),
            Entry("SearchCreativeKnowledge", "rag", "Low", false, new[] { "query" }, "检索创意知识库、类型原则、反套路策略和项目记忆。", Effects(memory: new[] { "execution" }, sqlite: new[] { "project_knowledge_usages" }, vector: new[] { "knowledge" }), (call, session, _, _, ct) => SearchCreativeKnowledgeAsync(call, session, ct)),
            Entry("PlanStoryFoundation", "planning", "Low", false, new[] { "userSeed", "genre" }, "生成故事地基和大框架候选，不直接固化。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, _, _, ct) => PlanStoryFoundationAsync(call, session, ct)),
            Entry("CommitStoryFoundation", "commit", "High", true, new[] { "runId", "selectedMacroCandidateIndex", "selectedMacroCandidateId", "selectedMacroCandidateTitle" }, "把候选故事地基固化到 Story Bible。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "story_constitutions", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitStoryFoundationAsync(call, session, confirmed, ct)),
            Entry("PlanVolumeArc", "planning", "Low", false, new[] { "creativeBrief", "volumeId", "volumeTitle", "sourceTurnId" }, "规划卷级弧线，不直接固化。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }), (call, session, bible, _, ct) => PlanVolumeArcAsync(call, session, bible, ct)),
            Entry("CommitVolumeArc", "commit", "High", true, new[] { "runId" }, "把卷规划提交到 Story Bible。", Effects(memory: new[] { "project", "execution" }, sqlite: new[] { "volume_arcs", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitVolumeArcAsync(call, session, confirmed, ct)),
            Entry("PlanChapter", "planning", "Medium", false, new[] { "creativeBrief", "chapterId", "sourceTurnId" }, "检索项目状态和知识库，生成章节候选。", Effects(memory: new[] { "session", "execution" }, sqlite: new[] { "agent_runs", "content_documents" }, vector: new[] { "knowledge", "chapter_context" }), (call, session, bible, _, ct) => PlanChapterAsync(call, session, bible, ct)),
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
            },
            Handler = handler,
        };

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
        var wantsBinding = mode is "bind_existing" or "bind" or "existing" or "continue_existing" or "continue" ||
                           !string.IsNullOrWhiteSpace(projectId) ||
                           !string.IsNullOrWhiteSpace(projectTitle);

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

        var requestedTitle = Arg(call, "title");
        var requestedGenre = Arg(call, "genre", ExtractGenre(seed, string.Empty));
        var reusableProject = await FindReusableDraftProjectAsync(seed, requestedTitle, ct).ConfigureAwait(false);
        if (reusableProject != null)
        {
            BindSessionToFoundationProject(session, reusableProject, seed);
            return new AgentToolExecutionResult
            {
                Success = true,
                Message = $"已复用待补地基的新小说「{reusableProject.Title}」，没有重复创建同名项目。\n\n现在继续把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？",
                Phase = session.Phase,
                Data = reusableProject,
                Artifact = BuildArtifact("existing_novel_project", reusableProject.Id, reusableProject.Id, string.Empty, $"复用新小说「{reusableProject.Title}」。", new[] { "补齐故事地基", "生成故事地基候选" }),
                Suggestions = new[] { "补齐类型/核心钩子/主角引擎", "查看当前状态", "继续地基规划" },
            };
        }

        var project = await _catalog.CreateAsync(new NovelProjectCreateRequest(
            requestedTitle,
            requestedGenre,
            seed), ct).ConfigureAwait(false);

        BindSessionToFoundationProject(session, project, seed);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = $"已创建新小说「{project.Title}」，它会作为独立作品进入书城，不会覆盖旧书。\n\n现在先把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？",
            Phase = session.Phase,
            Data = project,
            Artifact = BuildArtifact("novel_project", project.Id, project.Id, string.Empty, $"新小说「{project.Title}」已创建。", new[] { "补齐故事地基", "生成故事地基候选" }),
            Suggestions = new[] { "玄幻学院流，主角有代价型能力", "都市悬疑，主角追查异常规则", "我先给你完整设定" },
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

    private static void BindSessionToFoundationProject(AgentSession session, NovelProjectInfo project, string seed)
    {
        session.ActiveProjectId = project.Id;
        session.ActiveRunId = null;
        session.RunHistory.Clear();
        session.Phase = "awaiting_user_foundation";
        session.WorkingMemory.PendingToolCall = null;
        session.WorkingMemory.CurrentGoal = seed;
        session.WorkingMemory.OpenQuestions.Clear();
        session.WorkingMemory.Mission = new AgentMissionState
        {
            CurrentGoal = seed,
            CreativePhase = "foundation_intake",
            Readiness = "needs_author_input",
            NextIntent = "ask_foundation_question",
            PendingUserDecision = "补齐故事地基设定",
            PendingQuestion = "这本新小说的类型、核心钩子、主角引擎、阅读快感和禁区分别是什么？",
        };
        if (!string.IsNullOrWhiteSpace(seed))
            session.WorkingMemory.Mission.FoundationBrief["rawSeed"] = seed;
        session.WorkingMemory.OpenQuestions.Add(session.WorkingMemory.Mission.PendingQuestion);
    }

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
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "foundation_candidates";

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = FormatFoundationCandidates(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = BuildArtifact("story_foundation_candidates", run.RunId, session.ActiveProjectId, run.RunId, $"生成 {run.MacroCandidates.Count} 个故事地基候选。", run.MacroCandidates.Select(c => c.Title).Take(3).ToArray()),
            Suggestions = run.MacroCandidates.Select((c, i) => $"选第{i + 1}个: {c.Title}").ToArray(),
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
            Message = result.Message,
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
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(result.Success ? "chapter_draft_repaired" : "chapter_repair_report", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" }),
            Suggestions = result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" },
        };
    }

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
            Message = result.Message,
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
            Artifact = BuildArtifact("chapter_review", runId, session.ActiveProjectId, runId, result.Message, new[] { "导入账本", "继续下一章" }),
            Suggestions = new[] { "导入账本", "继续下一章" },
        };
    }

    private async Task<NovelAgentRun?> FindRunAsync(string runId, CancellationToken ct)
    {
        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        return bible.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
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
            _ => type,
        };

    private static string FormatFoundationCandidates(NovelAgentRun run)
    {
        if (run.MacroCandidates.Count == 0) return "未能生成故事地基候选。";
        var lines = new List<string> { $"生成了 {run.MacroCandidates.Count} 个故事地基候选：" };
        for (var i = 0; i < run.MacroCandidates.Count; i++)
        {
            var c = run.MacroCandidates[i];
            lines.Add($"【{i + 1}】{c.Title}\n核心钩子：{c.CoreHook}\n新颖度/可持续/类型匹配：{c.NoveltyScore}/{c.SustainabilityScore}/{c.TypeMatchScore}");
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
        var phaseArg = NormalizeToolSearchPhase(Arg(call, "phase", "Conversation"));

        IReadOnlyList<string> toolNames;
        if (string.Equals(phaseArg, "All", StringComparison.OrdinalIgnoreCase))
        {
            toolNames = _entries.Keys.Where(k => k != "tool_search").ToArray();
        }
        else
        {
            var phase = phaseArg switch
            {
                "Planning" => ConversationPhase.Planning,
                "Creation" => ConversationPhase.Creation,
                "Review" => ConversationPhase.Review,
                _ => ConversationPhase.Conversation,
            };
            toolNames = GetToolNamesForPhase(phase);
        }

        var toolList = toolNames
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => $"• {def!.Name}（{def.Description}）")
            .ToList();

        var message = toolList.Count == 0
            ? $"阶段「{phaseArg}」没有可用工具。"
            : $"阶段「{phaseArg}」可用工具（{toolList.Count}个）：\n{string.Join("\n", toolList)}";

        var discoveredTools = toolNames
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => new ToolSchema
            {
                Name = def!.Name,
                Description = def.Description,
                Risk = def.Risk,
                RequiresConfirmation = def.RequiresConfirmation,
                Parameters = def.Arguments?.ToDictionary(p => p, _ => "string") ?? new Dictionary<string, string>()
            })
            .ToList();

        using var scope = _serviceProvider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IToolSearchCacheService>();
        await cache.SaveAsync(session, phaseArg, discoveredTools, ct).ConfigureAwait(false);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = session.Phase,
            Data = new { Phase = phaseArg, Tools = toolNames },
            Artifact = BuildArtifact("tool_search_result", phaseArg, session.ActiveProjectId ?? string.Empty, string.Empty, $"检索到 {toolList.Count} 个工具。", Array.Empty<string>()),
            Suggestions = Array.Empty<string>(),
        };
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
