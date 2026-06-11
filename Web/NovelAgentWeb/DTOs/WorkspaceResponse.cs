namespace TM.Web.NovelAgentWeb.DTOs;

public class WorkspaceResponse
{
    public List<WorkspaceProjectView> Projects { get; set; } = new();
    public int TotalCount { get; set; }
}

public class WorkspaceProjectView
{
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string SubGenre { get; set; } = string.Empty;
    public string CoreHook { get; set; } = string.Empty;
    public string ReaderPromise { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int VolumeCount { get; set; }
    public int GeneratedChapterCount { get; set; }
    public int PlannedChapterCount { get; set; }
    public int NeedsRewriteCount { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public object? SelectedChapter { get; set; }
}
