namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record CommitStoryFoundationRequest(
    bool Overwrite = false,
    bool Confirmed = true,
    string SelectedMacroCandidateTitle = "",
    string SelectedMacroCandidateId = "",
    int SelectedMacroCandidateIndex = 0);

public sealed record ConfirmRequest(bool Overwrite = false, bool Confirmed = true);

public sealed record ConfirmOnlyRequest(bool Confirmed = true);

public sealed record ContinueRunRequest(string MaxAutoRisk = "Medium", int MaxAutoSteps = 12);

public sealed record SelectChapterCandidateRequest(
    string CandidateTitles = "",
    string SelectionMode = "Recommended",
    string SelectionRationale = "",
    bool Confirmed = true);

public sealed record EntryConfirmRequest(string EntryIds = "", bool Confirmed = true);

public sealed record CreativeKnowledgeQueryRequest(string Query = "");

public sealed record UsedPatternRequest(string ChapterId = "", string Pattern = "", string Note = "");

public sealed record MaterialIngestRequest(string FileName = "", string Content = "", string SourceType = "Text");

public sealed record MaterialUpdateRequest(
    string FileName = "",
    string Summary = "",
    string Tags = "",
    string Content = "");

public sealed record AgentChatRequest(string Message = "", string SessionId = "");

public sealed record AgentSessionUpdateRequest(string Title = "", bool? IsArchived = null);

public sealed record RollbackStepRequest(string RunId = "", string StepId = "");
