namespace TM.Web.NovelAgentWeb.Support;

public static class AgentProductSpaceCatalog
{
    public static AgentProductSpaceMap Create() => new()
    {
        Spaces = new List<AgentProductSpaceDefinition>
        {
            new()
            {
                Id = "novel_library",
                Name = "小说书城",
                Purpose = "承载用户可见的小说项目、已提交章节和可阅读的最终成品状态。",
                Contains = new List<string> { "novel_projects", "volumes", "chapters", "story_constitutions" },
                ProcessArtifacts = new List<string> { "待提交候选不会直接进入书城" },
                FinalArtifacts = new List<string> { "已提交 Story Bible", "已提交章节", "项目状态与字数" },
                Capabilities = new List<string> { "查看项目与书城状态", "绑定或创建创作项目", "固化 Story Bible", "提交最终章节" }
            },
            new()
            {
                Id = "creative_workflow",
                Name = "创作工作流",
                Purpose = "承载规划、候选、草稿、门禁、修复、复盘等过程产物。",
                Contains = new List<string> { "agent_runs", "content_documents", "mission_blackboard", "tool_execution_ledger" },
                ProcessArtifacts = new List<string> { "故事地基候选", "卷规划候选", "章节候选", "上下文包", "章节草稿", "校验报告", "修复记录" },
                FinalArtifacts = new List<string> { "通过 commit 工具固化后的 Story Bible 或章节" },
                Capabilities = new List<string> { "生成规划候选", "构建上下文包", "生成草稿", "校验草稿", "修复草稿", "复盘执行结果" }
            },
            new()
            {
                Id = "knowledge_base",
                Name = "知识库",
                Purpose = "承载上传资料、抽取知识、创作原则和可检索素材。",
                Contains = new List<string> { "knowledge_base", "knowledge_processing_tasks", "project_knowledge_usages", "content_chunks", "content_vector_points" },
                ProcessArtifacts = new List<string> { "上传处理任务", "抽取条目", "检索命中", "项目引用记录" },
                FinalArtifacts = new List<string> { "可复用知识条目", "项目知识引用关系" },
                Capabilities = new List<string> { "处理上传资料", "检索创作知识", "维护知识与索引", "查看知识库概览" }
            },
            new()
            {
                Id = "memory_system",
                Name = "记忆系统",
                Purpose = "贯通聊天上下文、会话目标、项目长期约束、作者偏好和工具执行经验。",
                Contains = new List<string> { "chat_memory", "session_memory", "project_memory", "author_memory", "execution_memory", "agent_memory_events", "agent_memory_reads", "agent_memory_promotions" },
                ProcessArtifacts = new List<string> { "本轮观察", "记忆读取审计", "短期偏好", "记忆提升记录", "失败模式", "修复经验" },
                FinalArtifacts = new List<string> { "作者长期偏好", "项目长期约束", "会话待办和开放问题" },
                Capabilities = new List<string> { "读取近期对话", "审计记忆读取", "沉淀会话目标", "沉淀项目约束", "沉淀作者偏好", "沉淀执行经验", "追踪记忆提升" }
            },
            new()
            {
                Id = "agent_runtime",
                Name = "Agent Runtime",
                Purpose = "执行 Observe/Plan/Act/Reflect 循环，保留工具发现、工具账本和安全边界。",
                Contains = new List<string> { "tool_search", "available_tools", "recent_observations", "guardrails", "policy_engine" },
                ProcessArtifacts = new List<string> { "工具发现结果", "工具调用账本", "反思补丁", "治理观察" },
                FinalArtifacts = new List<string> { "面向用户的回复", "持久化后的记忆事件" },
                Capabilities = new List<string> { "检索工具语义", "记录工具执行", "生成运行时观察", "执行安全护栏", "返回用户可见回复" }
            }
        },
        MemoryLayers = new List<AgentMemoryLayerDefinition>
        {
            new()
            {
                Id = "chat_memory",
                Scope = "session timeline",
                ReadsFrom = new List<string> { "agent_chat_turns", "agent_chat_summaries" },
                WritesTo = new List<string> { "agent_chat_turns", "agent_chat_summaries" },
                Notes = "保留原始对话和压缩摘要，供新一轮 Observe 使用。"
            },
            new()
            {
                Id = "session_memory",
                Scope = "single agent session",
                ReadsFrom = new List<string> { "agent_memories session.*" },
                WritesTo = new List<string> { "agent_memories session.*", "agent_memory_reads", "agent_memory_promotions" },
                Notes = "记录当前目标、开放问题、短期偏好和最近观察；无项目阶段也有效。"
            },
            new()
            {
                Id = "project_memory",
                Scope = "single novel project",
                ReadsFrom = new List<string> { "agent_memories project.*", "story_bible" },
                WritesTo = new List<string> { "agent_memories project.*", "agent_memory_reads" },
                Notes = "只在会话明确绑定项目后写入，避免聊天阶段串项目。"
            },
            new()
            {
                Id = "author_memory",
                Scope = "cross-project user profile",
                ReadsFrom = new List<string> { "agent_memories author.*" },
                WritesTo = new List<string> { "agent_memories author.*", "agent_memory_reads" },
                Notes = "沉淀用户长期风格偏好、确认容忍度、题材习惯；无项目阶段也有效。"
            },
            new()
            {
                Id = "execution_memory",
                Scope = "project or projectless runtime experience",
                ReadsFrom = new List<string> { "agent_memories execution.*", "agent_tool_executions" },
                WritesTo = new List<string> { "agent_memories execution.*", "agent_memory_events", "agent_memory_reads" },
                Notes = "沉淀工具失败模式、阻塞原因和修复经验；无项目阶段写入 projectless 执行记忆。"
            }
        },
        DecisionPrinciples = new List<string>
        {
            "Agent 自己根据用户意图、产品空间地图和工具语义决定是否调用工具。",
            "Runtime 只提供状态、工具执行和安全边界，不做业务关键词路由。",
            "涉及真实系统状态、书城、知识库、工作流完成度时，不凭空猜测；从完整工具目录中选择具备相应读能力的工具。",
            "区分过程产物和最终产物：规划/草稿/校验在工作流中，提交后的章节和 Story Bible 才进入书城。",
            "未绑定项目时允许自然聊天、工具语义检索和安全的只读/项目解析能力；不能把项目记忆写到未知项目。"
        }
    };
}

public sealed class AgentProductSpaceMap
{
    public List<AgentProductSpaceDefinition> Spaces { get; set; } = new();
    public List<AgentMemoryLayerDefinition> MemoryLayers { get; set; } = new();
    public List<string> DecisionPrinciples { get; set; } = new();
}

public sealed class AgentProductSpaceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> Contains { get; set; } = new();
    public List<string> ProcessArtifacts { get; set; } = new();
    public List<string> FinalArtifacts { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
}

public sealed class AgentMemoryLayerDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public List<string> ReadsFrom { get; set; } = new();
    public List<string> WritesTo { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}

public sealed class AgentWorkspaceState
{
    public AgentCurrentSessionState CurrentSession { get; set; } = new();
    public AgentWorkspaceAuthorProfileState AuthorProfile { get; set; } = new();
    public int ProjectTotalCount { get; set; }
    public int ProjectPreviewCount { get; set; }
    public List<AgentWorkspaceProjectState> VisibleProjects { get; set; } = new();
    public AgentWorkspaceKnowledgeState KnowledgeBase { get; set; } = new();
    public AgentWorkspaceWorkflowState Workflow { get; set; } = new();
    public AgentWorkspaceMemoryState Memory { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    public static AgentWorkspaceState Hint(AgentSession session) => new()
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
        Memory = new AgentWorkspaceMemoryState(),
        Notes = new List<string>
        {
            "这是轻量提示，不是数据库全量状态；需要真实状态时，从完整工具目录中选择具备相应读能力的工具。"
        }
    };
}

public sealed class AgentWorkspaceAuthorProfileState
{
    public string DisplayName { get; set; } = string.Empty;
    public int StyleLikeCount { get; set; }
    public int StyleDislikeCount { get; set; }
    public int GenreHabitCount { get; set; }
}

public sealed class AgentCurrentSessionState
{
    public string SessionId { get; set; } = string.Empty;
    public string ActiveProjectId { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public bool HasActiveProject { get; set; }
}

public sealed class AgentWorkspaceProjectState
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public bool IsOwnedByCurrentUser { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public int VolumeCount { get; set; }
    public int ChapterCount { get; set; }
    public int CommittedChapterCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AgentWorkspaceKnowledgeState
{
    public int TotalCount { get; set; }
    public List<AgentWorkspaceKnowledgeTypeCount> CountsByType { get; set; } = new();
    public List<AgentWorkspaceKnowledgeItem> RecentItems { get; set; } = new();
    public List<AgentWorkspaceKnowledgeUsageState> RecentlyUsedBindings { get; set; } = new();
    public List<AgentWorkspaceKnowledgeConstraintEvidenceState> RecentConstraintEvidence { get; set; } = new();
    public List<AgentWorkspaceKnowledgeConflictReportState> RecentConflictReports { get; set; } = new();
}

public sealed class AgentWorkspaceKnowledgeTypeCount
{
    public string EntryType { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class AgentWorkspaceKnowledgeItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string SourceProjectId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentWorkspaceKnowledgeUsageState
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string ProjectUsageStatus { get; set; } = string.Empty;
    public int ProjectUsageCount { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = string.Empty;
    public string PackagePolicy { get; set; } = string.Empty;
    public List<string> UsedByChapters { get; set; } = new();
    public DateTime? LastUsedAt { get; set; }
}

public sealed class AgentWorkspaceKnowledgeConstraintEvidenceState
{
    public string KnowledgeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string ConstraintLevel { get; set; } = string.Empty;
    public string PackagePolicy { get; set; } = string.Empty;
    public string GateStatus { get; set; } = string.Empty;
    public string EvidenceStatus { get; set; } = string.Empty;
    public string FactSnapshotId { get; set; } = string.Empty;
    public int FactSnapshotVersion { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentWorkspaceKnowledgeConflictReportState
{
    public string ConflictId { get; set; } = string.Empty;
    public string KnowledgeId { get; set; } = string.Empty;
    public List<string> ConflictingKnowledgeIds { get; set; } = new();
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ImpactScope { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public bool RequiresUserDecision { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ResolutionNote { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class AgentWorkspaceWorkflowState
{
    public int ActiveRunCount { get; set; }
    public int RecentRunCount { get; set; }
    public List<AgentWorkspaceRunState> RecentRuns { get; set; } = new();
}

public sealed class AgentWorkspaceMemoryState
{
    public List<AgentWorkspaceMemoryReadState> RecentReads { get; set; } = new();
    public List<AgentWorkspaceMemoryPromotionState> RecentPromotions { get; set; } = new();
}

public sealed class AgentWorkspaceMemoryReadState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string MemoryScope { get; set; } = string.Empty;
    public List<string> MemoryKeys { get; set; } = new();
    public string SourceType { get; set; } = string.Empty;
    public string Consumer { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentWorkspaceMemoryPromotionState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string SourceScope { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string SourceMemoryKey { get; set; } = string.Empty;
    public string TargetMemoryKey { get; set; } = string.Empty;
    public string PromotionReason { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentWorkspaceRunState
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RunType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
