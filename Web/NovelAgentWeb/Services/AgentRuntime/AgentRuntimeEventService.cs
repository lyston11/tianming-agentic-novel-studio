using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeEventService : IAgentRuntimeEventService
{
    private readonly NovelAgentDbContext _db;

    public AgentRuntimeEventService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentRuntimeEvent> AppendAsync(CreateAgentRuntimeEventRequest request, CancellationToken ct = default)
    {
        var evt = new AgentRuntimeEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = request.RuntimeRunId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId,
            Type = request.Type,
            Message = request.Message,
            DataJson = Serialize(request.Data),
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentRuntimeEvents.Add(evt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return evt;
    }

    public async Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(
        string userId,
        string sessionId,
        int limit = 50,
        CancellationToken ct = default) =>
        await _db.AgentRuntimeEvents
            .Where(e => e.UserId == userId && e.SessionId == sessionId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
