using TM.Web.NovelAgentWeb.Models.AgentSessions;

namespace TM.Web.NovelAgentWeb.Services.AgentSessions;

public interface IAgentSessionResumeService
{
    Task<AgentSessionResumeResponse> ResumeAsync(string sessionId, CancellationToken ct = default);
}
