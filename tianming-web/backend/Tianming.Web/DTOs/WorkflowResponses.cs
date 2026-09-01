namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record WorkspaceResponse(
    IReadOnlyList<NovelBookView> Projects,
    int TotalCount);
