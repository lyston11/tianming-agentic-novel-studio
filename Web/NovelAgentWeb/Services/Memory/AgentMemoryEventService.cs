using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
        var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        await using var transaction = ownsTransaction ? await _db.Database.BeginTransactionAsync(ct) : null;

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

        if (transaction != null)
        {
            await transaction.CommitAsync(ct);
        }
    }
}
