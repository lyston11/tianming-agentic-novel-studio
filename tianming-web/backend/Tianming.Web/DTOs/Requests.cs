namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record AgentSessionUpdateRequest(string Title = "", bool? IsArchived = null);
