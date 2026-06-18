using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record AgentChatResponse(
    string Reply,
    IReadOnlyList<string> Suggestions,
    string SessionId = "",
    string? RunId = null,
    string Phase = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentDecisionTrace? Decision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentRagContext? Rag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentWorkingMemorySnapshot? Memory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentRuntimeStep>? RuntimeTrace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentMissionPlan? MissionPlan = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentPendingConfirmation? PendingConfirmation = null,
    string ActiveProjectId = "");

public static class AgentChatResponsePublicProjection
{
    public static AgentChatResponse ToPublic(AgentChatResponse response)
    {
        var activeProjectId = response.ActiveProjectId;
        if (string.IsNullOrWhiteSpace(activeProjectId))
            activeProjectId = response.MissionPlan?.ProjectId
                ?? response.Memory?.MissionPlan?.ProjectId
                ?? string.Empty;

        return new AgentChatResponse(
            response.Reply,
            response.Suggestions,
            response.SessionId,
            response.RunId,
            response.Phase,
            PendingConfirmation: response.PendingConfirmation ?? response.Memory?.PendingConfirmation,
            ActiveProjectId: activeProjectId);
    }
}

public sealed record AgentConversationTurnView(
    string Role,
    string Content,
    string CreatedAt);

public sealed record AgentSessionSummary(
    string SessionId,
    string Title,
    string Phase,
    string ActiveProjectId,
    string? ActiveRunId,
    bool IsArchived,
    string UpdatedAt,
    int MessageCount);

public sealed record AgentSessionDetail(
    string SessionId,
    string Title,
    string Phase,
    string ActiveProjectId,
    string? ActiveRunId,
    bool IsArchived,
    IReadOnlyList<string> RunHistory,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<AgentConversationTurnView> Messages,
    AgentWorkingMemorySnapshot Memory);

public sealed record NovelLibraryDocument(
    IReadOnlyList<NovelBookView> Books,
    NovelBookView? ActiveBook,
    IReadOnlyList<NovelVolumeView> Volumes,
    NovelChapterView? SelectedChapter,
    int GeneratedChapterCount,
    int PlannedChapterCount,
    int NeedsRewriteCount);

public sealed record NovelBookView(
    string ProjectId,
    string Title,
    string Genre,
    string SubGenre,
    string CoreHook,
    string ReaderPromise,
    string Status,
    bool IsActive,
    int VolumeCount,
    int GeneratedChapterCount,
    int PlannedChapterCount,
    int NeedsRewriteCount,
    string UpdatedAt,
    NovelChapterView? SelectedChapter);

public sealed record NovelVolumeView(
    string VolumeId,
    string Title,
    string Status,
    string StartChapterId,
    string EndChapterId,
    int ExpectedChapterCount,
    IReadOnlyList<NovelChapterView> Chapters);

public sealed record NovelChapterView(
    string ChapterId,
    string Title,
    string VolumeId,
    string VolumeTitle,
    int BeatIndex,
    string BeatRole,
    string Goal,
    string Turn,
    string Cost,
    string Status,
    string RunId,
    string Intent,
    string UpdatedAt,
    bool HasGeneratedContent,
    bool NeedsRewrite,
    int WordCount,
    string Summary,
    string Content,
    string SelectedCandidateTitle,
    int QualityScore,
    int RewriteAttemptCount,
    IReadOnlyList<string> ReviewChecks,
    IReadOnlyList<string> NextSuggestions,
    string WritingStatus,
    string ContextPackageStatus,
    string DraftArtifactStatus,
    string GateStatus,
    bool ChangesProtocolPassed,
    bool FactSnapshotPassed,
    bool BlueprintPassed,
    bool LongDistanceRagPassed,
    int RagRecallCount,
    int RepairAttemptCount,
    IReadOnlyList<string> GateIssues,
    IReadOnlyList<string> RepairHints,
    IReadOnlyList<string> DependencyWarnings,
    IReadOnlyList<string> ContextWarnings,
    bool VisibleInWorkflow,
    bool VisibleInLibrary,
    string UserVisibleStatus,
    string ArtifactStatus,
    string DraftArtifactId,
    string GateReportId,
    string QualityReportId);

public sealed record ProjectWorkflowDocument(
    NovelBookView? Project,
    NovelLibraryDocument Library,
    IReadOnlyList<WorkflowSessionSummary> Sessions,
    IReadOnlyList<NovelAgentRun> Runs,
    IReadOnlyList<AgentScheduledTask> SchedulerTasks,
    IReadOnlyList<AgentMissionPlan> MissionPlans,
    IReadOnlyList<WorkflowChapterArtifactSummary> ChapterArtifacts,
    IReadOnlyList<WorkflowChapterArtifactSummary> CurrentChapterArtifacts,
    IReadOnlyList<AgentScheduledTask> DiagnosticTasks,
    int ActivityScore,
    bool IsEmptyProject,
    IReadOnlyList<string> SuspectReasons,
    IReadOnlyList<string> StaleMissionWarnings,
    AgentPendingConfirmation? PendingConfirmation,
    string PendingConfirmationSessionId,
    string ActiveSessionId,
    string ActiveRunId,
    string UpdatedAt,
    IReadOnlyList<WorkflowProductionStage> ProductionStages,
    IReadOnlyList<WorkflowArtifactTimelineItem> ArtifactTimeline);

public sealed record WorkflowSessionSummary(
    string SessionId,
    string Title,
    string Phase,
    string ActiveRunId,
    string UpdatedAt,
    AgentMissionPlan MissionPlan,
    AgentPendingConfirmation? PendingConfirmation);

public sealed record WorkflowChapterArtifactSummary(
    string ChapterId,
    string RunId,
    string Intent,
    string Status,
    string UpdatedAt,
    string CandidateTitle,
    string DraftStatus,
    string DraftPreview,
    bool HasDraft,
    string GateStatus,
    IReadOnlyList<string> GateIssues,
    IReadOnlyList<string> RepairHints,
    string QualityStatus,
    int QualityScore,
    IReadOnlyList<string> QualityIssues,
    IReadOnlyList<string> ContextWarnings,
    IReadOnlyList<string> DependencyWarnings,
    int RagRecallCount,
    IReadOnlyList<string> SourceRunIds,
    int LifecycleRank,
    bool IsCurrent);

public sealed record WorkflowProductionStage(
    string Key,
    string Label,
    string Surface,
    string Status,
    string Summary,
    string Detail,
    int ArtifactCount,
    int CurrentCount,
    int TotalCount,
    string UpdatedAt,
    string PrimaryArtifactId,
    string PrimaryRunId,
    string EmptyReason,
    string NextIntentHint);

public sealed record WorkflowArtifactTimelineItem(
    string Id,
    string Kind,
    string Label,
    string Surface,
    string Status,
    string Title,
    string Summary,
    string Preview,
    string VolumeId,
    string ChapterId,
    string RunId,
    string UpdatedAt,
    bool IsFinal,
    bool IsUserVisible,
    string Source);

public sealed class MaterialReference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "Text";
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> WorkflowReferences { get; set; } = new();
    public int CharacterCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsAnalyzed { get; set; }
    public List<MaterialAnalysisStageResult> AnalysisResults { get; set; } = new();
    public int KnowledgeEntriesCreated { get; set; }
}

// ============================================================
// Material Analysis DTOs
// ============================================================

public sealed class MaterialAnalysisProgress
{
    public string Stage { get; set; } = string.Empty;
    public string StageLabel { get; set; } = string.Empty;
    public int StageIndex { get; set; }
    public int TotalStages { get; set; } = 5;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<TM.Services.Framework.AI.NovelAgent.Models.CreativeKnowledgeEntry> Entries { get; set; } = new();
}

public sealed class MaterialAnalysisResult
{
    public bool Success { get; set; }
    public string MaterialId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TotalEntriesCreated { get; set; }
    public List<MaterialAnalysisStageResult> Stages { get; set; } = new();
    public MaterialReference? Material { get; set; }
}

public sealed class MaterialAnalysisStageResult
{
    public string Stage { get; set; } = string.Empty;
    public string StageLabel { get; set; } = string.Empty;
    public int EntriesCreated { get; set; }
    public List<string> EntryTitles { get; set; } = new();
}
