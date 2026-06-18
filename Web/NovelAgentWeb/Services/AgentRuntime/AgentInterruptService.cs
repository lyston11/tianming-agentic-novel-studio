using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentInterruptService : IAgentInterruptService
{
    private readonly NovelAgentDbContext _db;

    public AgentInterruptService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentInterrupt> AddAsync(CreateAgentInterruptRequest request, CancellationToken ct = default)
    {
        var interrupt = new AgentInterrupt
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = request.RuntimeRunId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId,
            Kind = string.IsNullOrWhiteSpace(request.Kind) ? "freeform" : request.Kind,
            Status = AgentInterruptStatus.Pending,
            Priority = request.Priority,
            Message = request.Message.Trim(),
            DecisionJson = "{}",
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentInterrupts.Add(interrupt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return interrupt;
    }

    public async Task<IReadOnlyList<AgentInterrupt>> GetPendingAsync(string runtimeRunId, CancellationToken ct = default) =>
        await _db.AgentInterrupts
            .Where(i => i.RuntimeRunId == runtimeRunId && i.Status == AgentInterruptStatus.Pending)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<AgentInterrupt?> MarkConsumedAsync(string interruptId, object? decision, CancellationToken ct = default)
    {
        var interrupt = await _db.AgentInterrupts.FirstOrDefaultAsync(i => i.Id == interruptId, ct).ConfigureAwait(false);
        if (interrupt == null)
            return null;

        interrupt.Status = AgentInterruptStatus.Consumed;
        interrupt.DecisionJson = Serialize(decision);
        interrupt.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return interrupt;
    }

    public async Task<AgentInterrupt?> MarkRejectedAsync(string interruptId, string reason, CancellationToken ct = default)
    {
        var interrupt = await _db.AgentInterrupts.FirstOrDefaultAsync(i => i.Id == interruptId, ct).ConfigureAwait(false);
        if (interrupt == null)
            return null;

        interrupt.Status = AgentInterruptStatus.Rejected;
        interrupt.DecisionJson = Serialize(new { reason });
        interrupt.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return interrupt;
    }

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
