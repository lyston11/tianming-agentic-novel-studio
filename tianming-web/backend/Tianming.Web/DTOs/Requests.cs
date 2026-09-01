namespace TM.Web.NovelAgentWeb.DTOs;

public sealed record AgentChatRequest(string Message = "", string SessionId = "", string? ClientMessageId = null);

public sealed record AgentSessionUpdateRequest(string Title = "", bool? IsArchived = null);
