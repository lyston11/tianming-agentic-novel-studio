using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Services.Framework.AI.NovelAgent.Services;

public sealed class AgentSessionMigration
{
    public async Task MigrateFromJsonAsync(
        string jsonPath,
        string defaultUserId,
        NovelAgentDbContext db,
        CancellationToken ct = default)
    {
        if (!File.Exists(jsonPath))
            return;

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        var sessions = JsonSerializer.Deserialize<List<AgentSession>>(json, JsonOptions());
        if (sessions == null || sessions.Count == 0)
            return;

        foreach (var session in sessions)
        {
            if (string.IsNullOrWhiteSpace(session.UserId))
                session.UserId = defaultUserId;

            var entity = new TM.Web.NovelAgentWeb.Data.Entities.AgentSession
            {
                Id = session.SessionId,
                UserId = session.UserId,
                Title = session.Title,
                ProjectId = session.ActiveProjectId,
                IsArchived = session.IsArchived,
                SessionData = SerializeSessionData(session),
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            };

            db.AgentSessions.Add(entity);
        }

        await db.SaveChangesAsync(ct);

        File.Move(jsonPath, jsonPath + ".migrated");
    }

    private static string SerializeSessionData(AgentSession session) =>
        JsonSerializer.Serialize(new
        {
            phase = session.Phase,
            activeRunId = session.ActiveRunId,
            runHistory = session.RunHistory,
            chatHistory = session.ChatHistory,
            workingMemory = session.WorkingMemory,
            toolSearchCache = new
            {
                discoveredPhase = session.DiscoveredPhase,
                discoveredTools = session.DiscoveredTools,
                lastToolSearchAt = session.LastToolSearchAt
            }
        }, JsonOptions());

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
