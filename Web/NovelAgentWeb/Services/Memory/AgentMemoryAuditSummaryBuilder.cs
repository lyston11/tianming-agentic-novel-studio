using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public static class AgentMemoryAuditSummaryBuilder
{
    public static async Task<AgentMemoryAuditSummary> BuildAsync(
        NovelAgentDbContext db,
        string userId,
        string sessionId,
        string? projectId,
        string? runId,
        CancellationToken cancellationToken)
    {
        var readsQuery = db.AgentMemoryReads.AsNoTracking()
            .Where(read => read.UserId == userId);
        var promotionsQuery = db.AgentMemoryPromotions.AsNoTracking()
            .Where(promotion => promotion.UserId == userId);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            var normalizedRunId = runId.Trim();
            readsQuery = readsQuery.Where(read =>
                read.RunId == normalizedRunId ||
                (read.RunId == null && read.SessionId == sessionId));
            promotionsQuery = promotionsQuery.Where(promotion =>
                promotion.RunId == normalizedRunId ||
                (promotion.RunId == null && promotion.SessionId == sessionId));
        }
        else
        {
            readsQuery = readsQuery.Where(read => read.SessionId == sessionId);
            promotionsQuery = promotionsQuery.Where(promotion => promotion.SessionId == sessionId);
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var normalizedProjectId = projectId.Trim();
            readsQuery = readsQuery.Where(read => read.ProjectId == normalizedProjectId || read.ProjectId == null);
            promotionsQuery = promotionsQuery.Where(promotion => promotion.ProjectId == normalizedProjectId || promotion.ProjectId == null);
        }

        var reads = await readsQuery
            .OrderByDescending(read => read.CreatedAt)
            .Take(8)
            .Select(read => new
            {
                read.Id,
                read.ProjectId,
                read.SessionId,
                read.RunId,
                read.MemoryScope,
                read.MemoryKeysJson,
                read.SourceType,
                read.Consumer,
                read.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var promotions = await promotionsQuery
            .OrderByDescending(promotion => promotion.CreatedAt)
            .Take(8)
            .Select(promotion => new AgentMemoryPromotionAuditSummary(
                promotion.Id,
                promotion.ProjectId ?? string.Empty,
                promotion.SessionId ?? string.Empty,
                promotion.RunId ?? string.Empty,
                promotion.SourceScope,
                promotion.TargetScope,
                promotion.SourceMemoryKey,
                promotion.TargetMemoryKey,
                promotion.PromotionReason,
                promotion.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        reads.Reverse();
        promotions.Reverse();

        return new AgentMemoryAuditSummary(
            reads.Select(read => new AgentMemoryReadAuditSummary(
                    read.Id,
                    read.ProjectId ?? string.Empty,
                    read.SessionId ?? string.Empty,
                    read.RunId ?? string.Empty,
                    read.MemoryScope,
                    ParseStringArray(read.MemoryKeysJson),
                    read.SourceType,
                    read.Consumer,
                    read.CreatedAt))
                .ToList(),
            promotions);
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!.Trim())
                .Take(12)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
