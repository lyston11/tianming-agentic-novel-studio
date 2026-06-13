using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryEventService : IAgentMemoryEventService
{
    private readonly NovelAgentDbContext _db;
    private readonly IAgentMemoryVersionService _versions;

    public AgentMemoryEventService(NovelAgentDbContext db, IAgentMemoryVersionService versions)
    {
        _db = db;
        _versions = versions;
    }

    public async Task AppendAsync(
        string userId,
        string? projectId,
        string? sessionId,
        string? runId,
        string sourceType,
        string triggerType,
        string memoryScope,
        string memoryKey,
        object payload,
        CancellationToken ct = default)
    {
        _db.AgentMemoryEvents.Add(new AgentMemoryEvent
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            SessionId = sessionId,
            RunId = runId,
            SourceType = sourceType,
            TriggerType = triggerType,
            MemoryScope = memoryScope,
            MemoryKey = memoryKey,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _versions.BumpAsync(userId, projectId, sessionId, memoryScope, ct);
    }
}
